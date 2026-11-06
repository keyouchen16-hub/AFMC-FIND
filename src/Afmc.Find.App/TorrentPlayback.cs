using System.IO;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;

namespace Afmc.Find.App;

public sealed class TorrentPlayback : IAsyncDisposable
{
    private static readonly object nodeGate = new();
    private static ReadOnlyMemory<byte>[] warmNodes = [];
    private static DateTime nodeSavedAt;
    private readonly string directory;
    private readonly ClientEngine? engine;
    private readonly LibtorrentSession? native;
    private bool disposed;
    private readonly FileStream sessionLock;
    private MonoTorrent.Dht.IDhtEngine? discovery;
    public int RoutingNodes => native?.Status.Nodes ?? engine?.Dht.NodeCount ?? 0;
    public PlaybackStatus Status => native?.Status ?? new(Manager?.State.ToString() ?? "Metadata", Manager?.HasMetadata ?? false,
        RoutingNodes, Manager?.OpenConnections ?? 0, Manager?.Monitor.DataBytesReceived ?? 0,
        Manager?.Progress > 0 ? 1 : 0, Manager?.Progress ?? 0, Manager?.Monitor.DownloadRate ?? 0);
    public bool CacheRemoved { get; private set; }
    public TorrentManager? Manager { get; private set; }
    public IReadOnlyList<PlaybackFile> Videos { get; private set; } = [];
    public Stream? Stream { get; private set; }
    public string CachePath => directory;
    public static bool IsVideo(string path) => new[] { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".mpg", ".mpeg", ".wmv", ".flv", ".ogv" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public Task PromoteAsync(CancellationToken ct) => native?.PromoteAsync(ct) ?? Task.CompletedTask;

    public TorrentPlayback(string cacheRoot, EngineSettings? settings = null, int maximumDownloadRate = 0, bool useLibraryStreaming = false)
    {
        directory = PlayerRuntime.SafeChild(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        sessionLock = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (settings is null && !useLibraryStreaming) {
            native = new LibtorrentSession(directory, maximumDownloadRate);
            return;
        }
        var factories = Factories.Default.WithDhtCreator(() => discovery = Factories.Default.CreateDht());
        if (!useLibraryStreaming) factories = factories.WithStreamingPieceRequesterCreator(() => new PlaybackPieceRequester());
        engine = new ClientEngine(settings ?? new EngineSettingsBuilder {
            CacheDirectory = Path.Combine(directory, "metadata"),
            AllowPortForwarding = false,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Any, 0) },
            DhtEndPoint = new IPEndPoint(IPAddress.Any, 0),
            MaximumConnections = 60,
            MaximumDownloadRate = maximumDownloadRate,
            DiskCacheBytes = 8 * 1024 * 1024
        }.ToSettings(), factories);
    }

    public async Task OpenAsync(string input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (native is not null) {
            await native.OpenAsync(input, ct);
            Videos = native.Files;
            return;
        }
        var content = Path.Combine(directory, "content");
        // Trackers may be offline or unsupported. Bootstrap the discovery network
        // independently; MonoTorrent still decides whether each torrent permits DHT.
        if (discovery is not null) {
            ReadOnlyMemory<byte>[] cached;
            lock(nodeGate) cached = DateTime.UtcNow-nodeSavedAt<TimeSpan.FromMinutes(30)?warmNodes:[];
            if(cached.Length>0) discovery.Add(cached);
            await AddBootstrapNodesAsync(discovery, ct);
        }
        if (input.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var magnet = MagnetLink.Parse(input);
            Manager = await engine!.AddStreamingAsync(magnet, content);
            // MonoTorrent groups magnet trackers into one failover tier. Our
            // bounded public hints need independent announces, so a reachable
            // but empty tracker cannot hide the peers offered by the others.
            if (Afmc.Find.Core.PublicTorrentDiscovery.IsFallbackSet(magnet.AnnounceUrls)) {
                foreach (var tracker in Manager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).ToArray())
                    await Manager.TrackerManager.RemoveTrackerAsync(tracker);
                foreach (var tracker in magnet.AnnounceUrls.Distinct())
                    await Manager.TrackerManager.AddTrackerAsync(new Uri(tracker));
            }
        }
        else
        {
            if (!input.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) || new FileInfo(input).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("请选择有效的 .torrent 文件（不超过 8 MB）。");
            var torrent = await Torrent.LoadAsync(input);
            foreach (var file in torrent.Files) _ = PlayerRuntime.SafeChild(content, file.Path);
            Manager = await engine!.AddStreamingAsync(torrent, content);
        }
        if (Manager.HasMetadata)
            foreach (var file in Manager.Files) await Manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
        await Manager.StartAsync();
        using var metadata = CancellationTokenSource.CreateLinkedTokenSource(ct);
        metadata.CancelAfter(TimeSpan.FromMinutes(2));
        try {
            await Manager.WaitForMetadataAsync(metadata.Token);
            // MetadataReceived precedes the library's asynchronous transition into download mode.
            // Opening a stream before that transition can have its seek priorities overwritten.
            while (Manager.State is not (TorrentState.Downloading or TorrentState.Seeding))
            {
                if (Manager.State == TorrentState.Error) throw new IOException("种子初始化失败，请检查磁盘空间或更换资源。");
                await Task.Delay(50, metadata.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("两分钟内没有获取到种子信息。做种可能离线，可换一个资源重试。"); }
        foreach (var file in Manager.Files)
        {
            _ = PlayerRuntime.SafeChild(content, file.Path);
            await Manager.SetFilePriorityAsync(file, Priority.DoNotDownload);
        }
        Videos = Manager.Files.Select((f,i) => new PlaybackFile(f.Path, f.Length, i, f)).Where(f => IsVideo(f.Path) && f.Length > 0).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (Videos.Count == 0) throw new InvalidDataException("这个种子没有可直接播放的视频文件；压缩包需要先完整下载并解压。");
    }

    private static async Task AddBootstrapNodesAsync(MonoTorrent.Dht.IDhtEngine dht, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        async Task<IEnumerable<ReadOnlyMemory<byte>>> Resolve(string host)
        {
            try
            {
                var ips = await Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork, timeout.Token);
                return ips.Take(2).Select(ip =>
                {
                    // Compact IPv4 node: provisional ID, IPv4 address and big-endian port.
                    var node = new byte[26];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(node.AsSpan(0, 20));
                    ip.GetAddressBytes().CopyTo(node, 20);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(node.AsSpan(24), 6881);
                    return (ReadOnlyMemory<byte>)node;
                }).ToArray();
            }
            catch (System.Net.Sockets.SocketException) { return []; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return []; }
        }
        var groups = await Task.WhenAll(Resolve("router.bittorrent.com"), Resolve("dht.transmissionbt.com"));
        ct.ThrowIfCancellationRequested();
        dht.Add(groups.SelectMany(group => group).ToArray());
    }
    public async Task<Stream> SelectAsync(PlaybackFile file, CancellationToken ct)
    {
        if (!Videos.Contains(file)) throw new InvalidOperationException("视频不属于当前种子。");
        Stream?.Dispose(); Stream = null;
        var drive = new DriveInfo(Path.GetPathRoot(directory)!);
        if (file.Length > drive.AvailableFreeSpace - 512L * 1024 * 1024) throw new IOException("磁盘剩余空间不足以缓存这个视频，请释放空间或选择较小的版本。");
        if (native is not null) { Stream = await native.SelectAsync(file, ct); return Stream; }
        var original = (ITorrentManagerFile)file.Native!;
        foreach (var item in Manager!.Files) await Manager.SetFilePriorityAsync(item, item == original ? Priority.Highest : Priority.DoNotDownload);
        Stream = await Manager.StreamProvider!.CreateStreamAsync(original, false, ct);
        // LocalStream only updates its piece picker when the position changes.
        // Reset it to this episode's first piece without waiting for any disk or network read.
        Stream.Seek(1, SeekOrigin.Begin);
        Stream.Seek(0, SeekOrigin.Begin);
        return Stream;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        Stream?.Dispose(); Stream = null;
        if (native is not null) {
            try { await native.DisposeAsync(); }
            finally { sessionLock.Dispose(); }
            try { Directory.Delete(directory, true); CacheRemoved = true; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return;
        }
        if(discovery is {NodeCount: > 0, Disposed: false}) {
            try {
                var encoded = await discovery.SaveNodesAsync().WaitAsync(TimeSpan.FromSeconds(1));
                if(encoded.Length is >0 and <=65536 && MonoTorrent.BEncoding.BEncodedValue.Decode(encoded.Span) is MonoTorrent.BEncoding.BEncodedList nodes) {
                    var fresh = nodes.OfType<MonoTorrent.BEncoding.BEncodedString>().Select(n=>n.AsMemory()).Where(n=>n.Length is 26 or 38).ToArray();
                    lock(nodeGate) {
                        warmNodes = fresh.Concat(warmNodes).DistinctBy(n=>Convert.ToHexString(n.Span)).Take(256).ToArray();
                        nodeSavedAt=DateTime.UtcNow;
                    }
                }
            } catch(Exception e) when(e is not OutOfMemoryException) { /* Discovery hints must never block cleanup. */ }
        }
        try { if (Manager is not null) await Manager.StopAsync(TimeSpan.FromSeconds(3)); }
        finally
        {
            engine!.Dispose(); Manager = null; sessionLock.Dispose();
            // A GUID session folder under the playback cache, never the user profile or original torrent.
            try { Directory.Delete(directory, true); CacheRemoved = true; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static void RecoverCache(string cacheRoot)
    {
        if (!Directory.Exists(cacheRoot)) return;
        string[] candidates;
        try { candidates = Directory.GetDirectories(cacheRoot); }
        catch (IOException) { return; } catch (UnauthorizedAccessException) { return; }
        foreach (var candidate in candidates)
        {
            try
            {
                if (!Guid.TryParseExact(Path.GetFileName(candidate), "N", out _) || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) continue;
                var path = PlayerRuntime.SafeChild(cacheRoot, Path.GetFileName(candidate));
                // In-use sessions, including those owned by another application instance, are retained.
                using (new FileStream(Path.Combine(path, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                Directory.Delete(path, true);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
