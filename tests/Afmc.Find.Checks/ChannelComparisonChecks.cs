using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Afmc.Find.App;
using LibVLCSharp.Shared;

// Isolated, opt-in real-network benchmark. Not part of the product or default checks.
internal static class ChannelComparisonChecks
{
    private const string Hash = "2ee672984434179aa8a7c5d2ebd0a94b38eb2960";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static int FreePort() { using var s = new TcpListener(IPAddress.Loopback, 0); s.Start(); return ((IPEndPoint)s.LocalEndpoint).Port; }
    private static async Task<JsonElement> Request(HttpClient http, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body is string text) req.Content = new StringContent(text);
        else if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct); res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    public static async Task Run()
    {
        var engine = Environment.GetEnvironmentVariable("AFMC_CHANNEL_ENGINE") ?? "rqbit";
        if (engine is not ("rqbit" or "torrserver")) throw new ArgumentException("Unknown engine");
        var folder = Path.GetFullPath(Environment.GetEnvironmentVariable("AFMC_COMPARE_OUTPUT") ?? "docs/evidence/channel-bench/" + engine);
        Directory.CreateDirectory(folder);
        var runDir = Path.GetFullPath(Path.Combine(".tools/channel-bench/runs", engine + "-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(runDir);
        var downloads = Path.Combine(runDir, "downloads"); Directory.CreateDirectory(downloads);
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("AFMC_COMPARE_SECONDS"), out var limit) ? Math.Clamp(limit, 30, 600) : 300;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var token = budget.Token;
        var port = FreePort();
        var password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("bench:" + password)));
        var psi = new ProcessStartInfo(Path.GetFullPath(".tools/channel-bench/bin/" + (engine == "rqbit" ? "rqbit.exe" : "TorrServer.exe")))
        { WorkingDirectory = runDir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (engine == "rqbit")
        {
            foreach (var arg in new[] { "--http-api-listen-addr", $"127.0.0.1:{port}", "--disable-dht-persistence", "--disable-upnp-port-forward", "--disable-lsd", "--listen-port", "0", "--ratelimit-upload", "131072", "--peer-limit", "160", "-v", "warn", "server", "start", "--disable-persistence", downloads }) psi.ArgumentList.Add(arg);
            psi.Environment["RQBIT_HTTP_BASIC_AUTH_USERPASS"] = "bench:" + password;
            psi.Environment["APPDATA"] = runDir;
            psi.Environment["LOCALAPPDATA"] = runDir;
        }
        else
        {
            var settings = new { BitTorr = new {
                CacheSize = 67108864, ReaderReadAHead = 95, PreloadCache = 0, UseDisk = false,
                ResponsiveMode = false, DisableUPNP = true, EnableLPD = false, EnableBonjour = false,
                EnableDLNA = false, EnableIPv6 = false, UploadRateLimit = 128, DownloadRateLimit = 0,
                ConnectionsLimit = 160, PeersListenPort = 0, RetrackersMode = 1,
                TorrentDisconnectTimeout = 300, StoreSettingsInJson = true, StoreViewedInJson = true
            }};
            await File.WriteAllTextAsync(Path.Combine(runDir, "settings.json"), JsonSerializer.Serialize(settings, Json));
            await File.WriteAllTextAsync(Path.Combine(runDir, "accs.db"), JsonSerializer.Serialize(new Dictionary<string,string> { ["bench"] = password }));
            foreach (var arg in new[] { "--ip", "127.0.0.1", "--port", port.ToString(), "--path", runDir, "--httpauth" }) psi.ArgumentList.Add(arg);
        }
        using var process = new Process { StartInfo = psi };
        var logs = new StringBuilder(); var logGate = new object();
        void Capture(string? line) { if (line is null) return; lock(logGate) { if(logs.Length < 16000) logs.AppendLine(line); } }
        process.OutputDataReceived += (_, e) => Capture(e.Data); process.ErrorDataReceived += (_, e) => Capture(e.Data);
        var clock = Stopwatch.StartNew();
        double? metadata = null, first = null;
        double observed = 0, stalled = 0, longest = 0, streak = 0, lastWall = 0;
        long firstTime = 0, lastTime = 0, playback = 0;
        int frames = 0;
        string stage = "Starting engine", outcome = "Running", decoderState = "NotCreated";
        bool authVerified = false, processStopped = false;
        bool started = false;
        string? statsPath = null;
        object? statsBody = null;
        Task<JsonElement>? statsTask = null;
        JsonElement? backendStats = null;
        HttpRangeInput? stream = null;
        MediaPlayer? player = null; Media? media = null;
        var samples = new List<object>();
        async Task Save()
        {
            var result = new {
                Engine = engine, Hash, RunDirectory = runDir, ObservationLimitSeconds = seconds,
                AuthVerified = authVerified, ProcessStopped = processStopped,
                MetadataSeconds = metadata, FirstDecodedSeconds = first, PostStartObservedSeconds = observed,
                PostStartObservationComplete = observed >= 120,
                PlaybackAdvancedSeconds = first.HasValue ? Math.Max(0, playback-firstTime)/1000d : 0,
                StallSeconds = stalled, LongestStallSeconds = longest,
                StallDefinition = "After startup, one-second samples with playback-clock advancement <=20ms; headless software decode, not visible/audio proof.",
                DecoderState = decoderState, DecodedFrames = frames, PlaybackTimeMs = playback,
                HttpDeliveredBytes = stream?.Delivered ?? 0, Outcome = outcome, Stage = stage, Samples = samples
            };
            var tmp = Path.Combine(folder,"result.json.tmp");
            await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(result,Json)); File.Move(tmp,Path.Combine(folder,"result.json"),true);
        }
        async Task Sample()
        {
            var now = clock.Elapsed.TotalSeconds; if (now-lastWall < 1) return;
            if (player is not null && media is not null)
            {
                playback = player.Time; frames = media.Statistics.DecodedVideo; decoderState = player.State.ToString();
                if (!first.HasValue && frames >= 10 && playback >= 1000) { first = now; firstTime = playback; Console.WriteLine($"DECODED {now:F3}"); }
                else if(first.HasValue)
                {
                    if(playback-lastTime<=20) { streak+=now-lastWall; stalled+=now-lastWall; longest=Math.Max(longest,streak); } else streak=0;
                }
                if(first.HasValue) observed=now-first.Value;
                lastTime=playback;
            }
            lastWall=now;
            if(statsTask?.IsCompleted==true)
            {
                try { backendStats=FilterStats(await statsTask); } catch(Exception e) when(e is not OutOfMemoryException) { }
                statsTask=null;
            }
            if(statsPath is not null && statsTask is null)statsTask=Request(http,statsPath,statsBody,token);
            samples.Add(new { Seconds=now,Stage=stage,Frames=frames,PlaybackTimeMs=playback,State=decoderState,HttpDeliveredBytes=stream?.Delivered??0,StallSeconds=stalled,BackendStats=backendStats });
            Console.WriteLine($"{now:F1}s {stage} frames={frames} time={playback} HTTP={stream?.Delivered??0} stall={stalled:F1}");
            await Save();
        }
        async Task<T> AwaitSampled<T>(Task<T> task)
        {
            while(!task.IsCompleted) { token.ThrowIfCancellationRequested(); await Sample(); await Task.WhenAny(task,Task.Delay(100,token)); }
            return await task;
        }
        try
        {
            process.Start(); started=true; process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var healthy=false;
            while(clock.Elapsed.TotalSeconds<20)
            {
                if(process.HasExited) throw new IOException("Engine exited before health check; exit code " + process.ExitCode);
                try { using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(500);
                    using var res=await http.GetAsync(engine=="rqbit"?"":"echo",timeout.Token); healthy=res.IsSuccessStatusCode; }
                catch(HttpRequestException){} catch(OperationCanceledException){token.ThrowIfCancellationRequested();}
                if(healthy)break; await Task.Delay(100,token);
            }
            if(!healthy) throw new IOException("Engine HTTP health timed out");
            // Verify that a second local client without the generated credentials cannot manage torrents.
            using(var anonymous=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}) { BaseAddress=http.BaseAddress })
            {
                using var unauth=engine=="rqbit" ? await anonymous.GetAsync("torrents",token) : await anonymous.PostAsync("torrents",new StringContent("{\"action\":\"list\"}",Encoding.UTF8,"application/json"),token);
                authVerified=unauth.StatusCode==HttpStatusCode.Unauthorized;
                if(!authVerified) throw new IOException("Management authentication check failed");
            }
            string url, name;long length;
            stage="Getting metadata";
            if(engine=="rqbit")
            {
                var add=await AwaitSampled(Request(http,"torrents?only_files_regex=(?i)%5C.mp4%24", "magnet:?xt=urn:btih:"+Hash.ToUpperInvariant(), token));
                var id=add.GetProperty("id").GetInt32();
                statsPath=$"torrents/{id}/stats/v1";
                var details=await Request(http,$"torrents/{id}",null,token);
                var file=details.GetProperty("files").EnumerateArray().Select((f,i)=>(File:f,Index:i)).Where(x=>TorrentPlayback.IsVideo(x.File.GetProperty("name").GetString()!)).OrderByDescending(x=>x.File.GetProperty("length").GetInt64()).First();
                length=file.File.GetProperty("length").GetInt64();name=file.File.GetProperty("name").GetString()!;url=$"torrents/{id}/stream/{file.Index}";
            }
            else
            {
                statsPath="torrents";statsBody=new {action="get",hash=Hash};
                await AwaitSampled(Request(http,"torrents",new {action="add",link="magnet:?xt=urn:btih:"+Hash.ToUpperInvariant(),save_to_db=false},token));
                JsonElement details;
                while(true)
                {
                    details=await AwaitSampled(Request(http,"torrents",new {action="get",hash=Hash},token));
                    if(details.TryGetProperty("file_stats",out var files) && files.ValueKind==JsonValueKind.Array && files.GetArrayLength()>0)break;
                    await Sample();await Task.Delay(200,token);
                }
                var file=details.GetProperty("file_stats").EnumerateArray().Where(f=>TorrentPlayback.IsVideo(f.GetProperty("path").GetString()!)).OrderByDescending(f=>f.GetProperty("length").GetInt64()).First();
                length=file.GetProperty("length").GetInt64();name=file.GetProperty("path").GetString()!;url=$"play/{Hash}/{file.GetProperty("id").GetInt32()}";
            }
            metadata=clock.Elapsed.TotalSeconds; Console.WriteLine($"METADATA {metadata:F3}, video bytes {length}");
            stage="Decoding and observing";
            Core.Initialize(await PlayerRuntime.EnsureAsync(null,token));
            using var vlc=new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
            using var ownedStream=new HttpRangeInput(http,url,length); stream=ownedStream;
            using var input=new PlaybackStreamInput(stream,token);
            using var ownedMedia=new Media(vlc,input);media=ownedMedia;PlaybackMedia.Configure(media,name);
            using var ownedPlayer=new MediaPlayer(vlc){EnableHardwareDecoding=false};player=ownedPlayer;
            try
            {
                player.Play(media);
                while(true)
                {
                    token.ThrowIfCancellationRequested();await Sample();
                    if(observed>=120){outcome="120-second post-start observation complete";break;}
                    if(player.State is VLCState.Error or VLCState.Ended)throw new IOException("Decoder stopped early: "+player.State+"; input "+input.LastReadError);
                    await Task.Delay(100,token);
                }
            }
            finally { budget.Cancel();player=null;media=null;await Task.Run(ownedPlayer.Stop); }
        }
        catch(Exception e)
        {
            outcome=e is OperationCanceledException?$"{seconds}-second overall limit":e.GetType().Name+": "+e.Message;
            Console.WriteLine(outcome);
            // Native startup diagnostics only. Never persist credentials, URLs or endpoint addresses.
            if(stage=="Starting engine") { lock(logGate) {
                var safe=logs.ToString().Split('\n').Where(l=>l.Contains("error",StringComparison.OrdinalIgnoreCase)||l.Contains("unexpected",StringComparison.OrdinalIgnoreCase)).Take(4);
                foreach(var line in safe) Console.WriteLine(System.Text.RegularExpressions.Regex.Replace(line.Replace(password,"[redacted]"),@"https?://\S+","[url]"));
            }}
        }
        finally
        {
            budget.Cancel();
            if(started && !process.HasExited) { process.Kill(true);await process.WaitForExitAsync(); }
            processStopped=!started || process.HasExited;
            if(statsTask is not null)try{await statsTask;}catch(Exception e)when(e is not OutOfMemoryException){}
            // Erase this run's generated temporary credential file only.
            var authFile=Path.Combine(runDir,"accs.db");if(File.Exists(authFile))File.WriteAllText(authFile,"{}");
            await Save();
        }
    }

    private static JsonElement FilterStats(JsonElement source)
    {
        // Numeric counters and states only; do not store peers, URLs, filenames or credentials.
        var allowed=new HashSet<string>(StringComparer.Ordinal) {
            "live","snapshot","peer_stats","download_speed","upload_speed","mbps","human_readable",
            "progress_bytes","total_bytes","downloaded_and_checked_bytes","downloaded_and_checked_pieces",
            "fetched_bytes","uploaded_bytes","all_time_download_speed","state","finished",
            "live_peers","queued","connecting","live","dead","not_needed","seen",
            "active_peers","connected_seeders","total_peers","loaded_size","preloaded_bytes",
            "preload_size","torrent_size","stat","stat_string","bytes_read","bytes_read_data","bytes_read_useful_data",
            "pieces_dirtied_good","pieces_dirtied_bad"};
        object? Walk(JsonElement item)=>item.ValueKind switch {
            JsonValueKind.Object=>item.EnumerateObject().Where(p=>allowed.Contains(p.Name)).ToDictionary(p=>p.Name,p=>Walk(p.Value)),
            JsonValueKind.Number=>item.TryGetInt64(out var n)?n:item.GetDouble(),
            JsonValueKind.True=>true,JsonValueKind.False=>false,
            JsonValueKind.String=>item.GetString(),_=>null};
        return JsonSerializer.SerializeToElement(Walk(source));
    }

    public static async Task RangeChecks()
    {
        var data=Encoding.ASCII.GetBytes("range-fixture");
        var calls=0;
        using var client=new HttpClient(new RangeHandler(req=>{
            calls++;var start=req.Headers.Range!.Ranges.Single().From!.Value;
            var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(data[(int)start..])};
            response.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,data.Length-1,data.Length);return response;
        })){BaseAddress=new Uri("http://127.0.0.1/")};
        using var stream=new HttpRangeInput(client,"video",data.Length);var buffer=new byte[3];
        if(await stream.ReadAsync(buffer)!=3 || Encoding.ASCII.GetString(buffer)!="ran")throw new Exception("initial range");
        if(await stream.ReadAsync(buffer)!=3||calls!=1)throw new Exception("per-read request distorts streaming");
        stream.Seek(7,SeekOrigin.Begin);if(await stream.ReadAsync(buffer)!=3)throw new Exception("short seek read");
        if(Encoding.ASCII.GetString(buffer)!="ixt"||calls!=2)throw new Exception("seek range");
        stream.Seek(data.Length,SeekOrigin.Begin);if(await stream.ReadAsync(buffer)!=0)throw new Exception("EOF");
        using var invalid=new HttpClient(new RangeHandler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)})){BaseAddress=client.BaseAddress};
        using var rejected=new HttpRangeInput(invalid,"video",data.Length);
        try{var count=await rejected.ReadAsync(buffer);throw new Exception("accepted full response: "+count);}catch(IOException){}
        Console.WriteLine("CHANNEL RANGE PASSED: sequential reuse, seek, EOF, reject invalid response");
    }
    private sealed class RangeHandler(Func<HttpRequestMessage,HttpResponseMessage> handler):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(handler(request));}
    }

    // Retain a single HTTP range response until VLC seeks; opening a new request for
    // every 128KiB read would distort the engines' read-ahead behavior and benchmark.
    private sealed class HttpRangeInput(HttpClient http,string url,long length):Stream
    {
        private long position, delivered;
        private HttpResponseMessage? response;
        private Stream? body;
        public long Delivered=>Interlocked.Read(ref delivered);
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>length;
        public override long Position {get=>position;set=>Seek(value,SeekOrigin.Begin);}
        private void CloseBody(){body?.Dispose();body=null;response?.Dispose();response=null;}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            if(position>=length || buffer.Length==0)return 0;
            if(body is null)
            {
                using var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.Range=new RangeHeaderValue(position,null);
                response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
                var range=response.Content.Headers.ContentRange;
                if(response.StatusCode!=HttpStatusCode.PartialContent || range?.From!=position || range.Length!=length)
                    throw new IOException("Invalid HTTP Range response: "+(int)response.StatusCode);
                body=await response.Content.ReadAsStreamAsync(ct);
            }
            var read=await body.ReadAsync(buffer[..(int)Math.Min(buffer.Length,length-position)],ct);
            if(read==0 && position<length)throw new EndOfStreamException("Premature HTTP stream end");
            position+=read;Interlocked.Add(ref delivered,read);return read;
        }
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset,SeekOrigin origin)
        {
            var target=origin switch { SeekOrigin.Begin=>offset,SeekOrigin.Current=>checked(position+offset),SeekOrigin.End=>checked(length+offset),_=>throw new ArgumentOutOfRangeException(nameof(origin))};
            if(target<0||target>length)throw new IOException("Seek outside file");
            if(target!=position){CloseBody();position=target;}return position;
        }
        protected override void Dispose(bool disposing){if(disposing)CloseBody();base.Dispose(disposing);}
        public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
