using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Afmc.Find.App;

public sealed record PlaybackFile(string Path, long Length, int Index, object? Native = null);
public sealed record PlaybackStatus(string State = "Metadata", bool Metadata = false, int Nodes = 0,
    int Peers = 0, long Downloaded = 0, long Verified = 0, double Progress = 0, long Rate = 0, long Reading = -1, string Diagnostic = "", int RoutingReused = 0, int PeersReused = 0);

// A worker is owned by exactly one session. Only authenticated loopback requests
// cross the process boundary; native decoder reads never touch sparse disk files.
internal sealed class LibtorrentSession(string cache, int rate) : IAsyncDisposable
{
    // Process-local bootstrap data expires without touching personal settings.
    private static readonly object routingGate = new();
    private static string[] routing = [];
    private static long routingSaved;
    private sealed record PeerSnapshot(string[] Nodes, long Saved);
    private static readonly Dictionary<string, PeerSnapshot> resourcePeers = new();
    private string? resourceKey;
    private static string[] PeerNodes(string? key)
    {
        lock (routingGate)
            return key is not null && resourcePeers.TryGetValue(key, out var item) &&
                Stopwatch.GetElapsedTime(item.Saved) < TimeSpan.FromMinutes(5) ? item.Nodes.ToArray() : [];
    }
    private static void RememberPeers(string? key, JsonElement root)
    {
        if (key is null || !root.TryGetProperty("peersReusable", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 64) return;
        var valid = nodes.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String)
            .Select(n => n.GetString()!).Where(n => n.Length is 12 or 36 && n.All(Uri.IsHexDigit)).Distinct().ToArray();
        if (valid.Length == 0) return;
        lock (routingGate)
        {
            resourcePeers[key] = new(valid, Stopwatch.GetTimestamp());
            while (resourcePeers.Count > 16)
                resourcePeers.Remove(resourcePeers.MinBy(p => p.Value.Saved).Key);
        }
    }
    private static string[] RoutingSnapshot()
    {
        lock (routingGate)
            return Stopwatch.GetElapsedTime(routingSaved) < TimeSpan.FromMinutes(30) ? routing.ToArray() : [];
    }
    private static void RememberRouting(JsonElement root)
    {
        if (!root.TryGetProperty("routing", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() > 256) return;
        var valid = nodes.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String)
            .Select(n => n.GetString()!).Where(n => n.Length is 12 or 36 && n.All(Uri.IsHexDigit)).Distinct().ToArray();
        if (valid.Length < 8) return;
        lock (routingGate) { routing = valid; routingSaved = Stopwatch.GetTimestamp(); }
    }
    private Process? process;
    private HttpClient? http;
    private readonly CancellationTokenSource lifetime = new();
    private Task? polling;
    private Task? stderr;
    private string? failure;
    private bool disposed;
    public PlaybackStatus Status { get; private set; } = new();
    public IReadOnlyList<PlaybackFile> Files { get; private set; } = [];

    public async Task OpenAsync(string input, CancellationToken ct)
    {
        var runtime = Path.Combine(AppContext.BaseDirectory, "components", "libtorrent");
        var python = Path.Combine(runtime, "python.exe");
        var worker = Path.Combine(AppContext.BaseDirectory, "Streaming", "stream_worker.py");
        if (!File.Exists(python) || !File.Exists(worker))
            throw new IOException("下载组件不完整，请重新安装 AFMC FIND。");
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = runtime };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add("-u"); start.ArgumentList.Add(worker);
        process = Process.Start(start) ?? throw new IOException("无法启动下载组件。");
        // Drain errors without copying remote content or secrets into product logs.
        stderr = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is not null) { } } catch (IOException) { } });
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        resourceKey = input.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input))) : null;
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { token, cache, input, rate, routing = RoutingSnapshot(), peers = PeerNodes(resourceKey) }));
        await process.StandardInput.FlushAsync(ct);
        var ready = await process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(15), ct);
        if (ready is null) throw new IOException("下载组件未能启动。");
        using var parsed = JsonDocument.Parse(ready);
        var port = parsed.RootElement.GetProperty("port").GetInt32();
        if (port is < 1 or > 65535) throw new IOException("下载组件返回无效端口。");
        http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false,
            MaxConnectionsPerServer = 8, ConnectTimeout = TimeSpan.FromSeconds(3) })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(100) };
        http.DefaultRequestHeaders.Add("X-AFMC-Token", token);
        polling = PollAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try {
            while (!Status.Metadata) {
                ThrowIfFailed();
                await Task.Delay(100, timeout.Token);
            }
            ThrowIfFailed();
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested && !lifetime.IsCancellationRequested) {
            throw new IOException("两分钟内没有获取到种子信息，请换一个版本。");
        }
    }

    private async Task PollAsync()
    {
        try {
            while (!lifetime.IsCancellationRequested) {
                using var response = await http!.GetAsync("status", lifetime.Token);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
                var r = doc.RootElement;
                RememberRouting(r);
                RememberPeers(resourceKey, r);
                if (r.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) {
                    failure = error.GetString(); return;
                }
                Files = r.GetProperty("files").EnumerateArray().Select(f => new PlaybackFile(
                    f.GetProperty("path").GetString()!, f.GetProperty("length").GetInt64(), f.GetProperty("id").GetInt32())).ToArray();
                Status = new(r.GetProperty("state").GetString()!, r.GetProperty("metadata").GetBoolean(),
                    r.GetProperty("nodes").GetInt32(), r.GetProperty("peers").GetInt32(), r.GetProperty("downloaded").GetInt64(),
                    r.GetProperty("verified").GetInt64(), r.GetProperty("progress").GetDouble(), r.GetProperty("rate").GetInt64(),
                    r.TryGetProperty("reading", out var reading) ? reading.GetInt64() : -1,
                    r.TryGetProperty("diagnostic", out var diagnostic) ? diagnostic.GetString() ?? "" : "",
                    r.TryGetProperty("routingReused", out var reused) ? reused.GetInt32() : 0,
                    r.TryGetProperty("peersReused", out var peersReused) ? peersReused.GetInt32() : 0);
                await Task.Delay(200, lifetime.Token);
            }
        } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException) { failure = "下载组件连接中断，请重新打开资源。"; }
    }

    private void ThrowIfFailed()
    {
        if (failure is not null) throw new IOException(failure);
        if (process?.HasExited == true) throw new IOException("下载组件意外退出，请重新打开资源。");
    }

    public async Task<Stream> SelectAsync(PlaybackFile file, CancellationToken ct)
    {
        ThrowIfFailed();
        using var body = new StringContent(JsonSerializer.Serialize(new { id = file.Index }), System.Text.Encoding.UTF8, "application/json");
        using var response = await http!.PostAsync("select", body, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return new RangeStream(http!, file, doc.RootElement.GetProperty("generation").GetInt32(), lifetime.Token);
    }

    public async Task PromoteAsync(CancellationToken ct)
    {
        ThrowIfFailed();
        using var body = new StringContent("{\"rate\":0}",System.Text.Encoding.UTF8,"application/json");
        using var response = await http!.PostAsync("rate",body,ct);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        lifetime.Cancel();
        // EOF also works after a GUI crash: the inherited pipe closes in the OS.
        if (process is not null) {
            try { process.StandardInput.Close(); } catch (IOException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        if (polling is not null) await polling;
        if (stderr is not null) await stderr;
        http?.Dispose(); process?.Dispose(); lifetime.Dispose();
    }

    private sealed class RangeStream(HttpClient http, PlaybackFile file, int generation, CancellationToken owner) : Stream
    {
        private readonly CancellationTokenSource stopped = new();
        private long position;
        public override bool CanRead => !stopped.IsCancellationRequested;
        public override bool CanSeek => CanRead;
        public override bool CanWrite => false;
        public override long Length => file.Length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(!CanRead, this);
            if (position >= Length || buffer.Length == 0) return 0;
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(owner, stopped.Token, cancellationToken);
            var count = (int)Math.Min(Math.Min(buffer.Length, 128 * 1024), Length - position);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"file/{file.Index}?generation={generation}");
            request.Headers.Range = new RangeHeaderValue(position, position + count - 1);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct.Token);
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new IOException("视频分片暂时不可用。");
            var bytes = await response.Content.ReadAsByteArrayAsync(ct.Token);
            if (bytes.Length is 0 || bytes.Length > count) throw new IOException("视频分片长度不正确。");
            var range = response.Content.Headers.ContentRange;
            if (range is null || range.Unit != "bytes" || range.From != position ||
                range.To != position + bytes.Length - 1 || range.Length != Length)
                throw new IOException("视频分片位置不正确。");
            bytes.CopyTo(buffer); position += bytes.Length; return bytes.Length;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(!CanRead, this);
            var target = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(position + offset), SeekOrigin.End => checked(Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (target < 0 || target > Length) throw new IOException("视频位置超出范围。");
            return position = target;
        }
        protected override void Dispose(bool disposing) { if (disposing) stopped.Cancel(); base.Dispose(disposing); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
