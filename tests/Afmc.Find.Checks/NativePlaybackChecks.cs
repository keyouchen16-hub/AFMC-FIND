using Afmc.Find.App;
using Afmc.Find.Core;
using LibVLCSharp.Shared;
using System.IO;
using System.Text.Json;

internal static class NativePlaybackChecks
{
    public static async Task HandoffAsync()
    {
        var fixture = Path.GetFullPath(".tools/player-installed-fixture");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture,"server.json")));
        var address = manifest.RootElement.GetProperty("Magnet").GetString()!;
        var row = new SearchResult { Title="Original native handoff fixture", Source="Controlled fixture", Magnet=address };
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        LibVLCSharp.Shared.Core.Initialize(await PlayerRuntime.EnsureAsync(null,budget.Token));
        using var vlc = new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
        await using var session = new TorrentPlayback(Path.GetFullPath(".tools/native-handoff-cache"),maximumDownloadRate:512*1024);
        var cache = new PreparedPlaybackCache();
        var created = 0;
        try {
            var winner = await PlaybackRace.RunAsync([new(row.Title,_=>Task.FromResult(address),ResourceKey:PreparedPlaybackCache.Key(row))],
                ()=>{created++;return session;},vlc,Console.WriteLine,budget.Token);
            var before = session.Status.Downloaded;
            await cache.StoreAsync(row,winner,DateTimeOffset.UtcNow);
            var taken = cache.Take(row,DateTimeOffset.UtcNow) ?? throw new Exception("Ready native session missing");
            if(created!=1 || !ReferenceEquals(taken.Session,session))throw new Exception("Native session replaced during handoff");
            await taken.Session.PromoteAsync(budget.Token);
            await cache.ClearAsync();
            if(session.CacheRemoved)throw new Exception("Cache manager disposed transferred native session");
            var started = DateTime.UtcNow;
            var file = session.Videos.OrderByDescending(f=>f.Length).First();
            using var stream = await session.SelectAsync(file,budget.Token);
            using var input = new PlaybackStreamInput(stream,budget.Token);
            using var media = new Media(vlc,input); PlaybackMedia.Configure(media,file.Path);
            using var player = new MediaPlayer(vlc){EnableHardwareDecoding=false};
            try {
                if(!player.Play(media))throw new Exception("Transferred native session rejected by decoder");
                while(player.Time<1000 || media.Statistics.DecodedVideo<10)await Task.Delay(100,budget.Token);
                Console.WriteLine($"PASS native warm handoff decodes in {(DateTime.UtcNow-started).TotalSeconds:F2}s; retained {before} bytes; no new session");
                while(player.Time<10000){
                    if(player.State is VLCState.Error or VLCState.Ended)throw new Exception("Native handoff playback stopped early");
                    await Task.Delay(100,budget.Token);
                }
                Console.WriteLine("PASS transferred native session advances ten seconds with decoded frames");
            } finally {budget.Cancel();await Task.Run(player.Stop);}
        } finally {await cache.ClearAsync();await session.DisposeAsync();}
        if(!session.CacheRemoved)throw new Exception("Native handoff cache retained after stop");
        Console.WriteLine("PASS native transferred worker and cache cleanup");
    }
    public static async Task MkvAsync()
    {
        var root = Path.GetFullPath(".tools/native-mkv-fixture");
        var video = Path.Combine(root, "original.mkv");
        using var tracker = new PlaybackChecks.VideoServer(video);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        tracker.SeedPort = port;
        var creator = new MonoTorrent.TorrentCreator(MonoTorrent.TorrentType.V1Only) { PieceLength = 256*1024 };
        creator.Announces.Add(new List<string>{tracker.Url+"announce"});
        var encoded = await creator.CreateAsync(new MonoTorrent.TorrentFileSource(video));
        var path = Path.Combine(root,"original.torrent"); await File.WriteAllBytesAsync(path,encoded.Encode());
        using var engine = new MonoTorrent.Client.ClientEngine(new MonoTorrent.Client.EngineSettingsBuilder {
            CacheDirectory=Path.Combine(root,"seed-meta"),AllowPortForwarding=false,DhtEndPoint=null,MaximumUploadRate=512*1024,
            ListenEndPoints=new Dictionary<string,System.Net.IPEndPoint>{["ipv4"]=new(System.Net.IPAddress.Loopback,port)}}.ToSettings());
        var seed = await engine.AddAsync(await MonoTorrent.Torrent.LoadAsync(path),root); await seed.StartAsync();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while(seed.State != MonoTorrent.Client.TorrentState.Seeding) await Task.Delay(100,ct.Token);
        LibVLCSharp.Shared.Core.Initialize(await PlayerRuntime.EnsureAsync(null,ct.Token));
        using var vlc = new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
        await using var session = new TorrentPlayback(Path.Combine(root,"cache"));
        var start = DateTime.UtcNow;
        await session.OpenAsync(path,ct.Token);
        using var stream = await session.SelectAsync(session.Videos.First(),ct.Token);
        using var checkedStream = new FixtureStream(stream,await File.ReadAllBytesAsync(video,ct.Token));
        using var input = new PlaybackStreamInput(checkedStream,ct.Token);
        using var media = new Media(vlc,input); PlaybackMedia.Configure(media,"original.mkv");
        using var player = new MediaPlayer(vlc){EnableHardwareDecoding=false};
        try {
            player.Play(media);
            while(player.Time<1500 || media.Statistics.DecodedVideo<10) await Task.Delay(100,ct.Token);
            if(player.AudioTrack<0)throw new Exception("MKV audio missing");
            Console.WriteLine($"PASS MKV real video and audio decode in {(DateTime.UtcNow-start).TotalSeconds:F2}s; downloaded {session.Status.Downloaded} bytes");
            player.Time=15000;await Task.Delay(1000,ct.Token);
            while(player.Time<18000 || !player.IsPlaying){
                Console.WriteLine(JsonSerializer.Serialize(new {SeekTime=player.Time,Frames=media.Statistics.DecodedVideo,input.LastReadError,session.Status}));
                await Task.Delay(2000,ct.Token);
            }
            Console.WriteLine("PASS MKV seek resumes and advances");
        } finally {ct.Cancel();await Task.Run(player.Stop);await seed.StopAsync();}
        await session.DisposeAsync();
        if(!session.CacheRemoved)throw new Exception("MKV cache remains");
        Console.WriteLine("PASS MKV worker and cache cleanup");
    }
    public static async Task RunAsync()
    {
        var fixture = Path.GetFullPath(".tools/player-installed-fixture");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "server.json")));
        var root = Path.GetFullPath(".tools/native-player-checks"); Directory.CreateDirectory(root);
        var runtime = await PlayerRuntime.EnsureAsync(null, CancellationToken.None);
        LibVLCSharp.Shared.Core.Initialize(runtime);
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none", "--no-lua");
        foreach (var kind in new[] { "Torrent", "Magnet" }) {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await using var session = new TorrentPlayback(root);
            await session.OpenAsync(manifest.RootElement.GetProperty(kind).GetString()!, budget.Token);
            var file = session.Videos.OrderByDescending(f => f.Length).First();
            var stream = await session.SelectAsync(file, budget.Token);
            var actual = new byte[8192]; await stream.ReadExactlyAsync(actual, budget.Token);
            var original = File.ReadAllBytes(Path.Combine(fixture, "AFMC-original.avi"));
            if (!actual.SequenceEqual(original.Take(actual.Length))) throw new Exception("Validated first range differs from original");
            stream.Seek(file.Length - actual.Length, SeekOrigin.Begin); await stream.ReadExactlyAsync(actual, budget.Token);
            if (!actual.SequenceEqual(original.TakeLast(actual.Length))) throw new Exception("Validated tail range differs from original");
            Console.WriteLine("PASS " + kind + " verified head and tail bytes match original");
            stream.Seek(0, SeekOrigin.Begin);
            using var input = new PlaybackStreamInput(stream, budget.Token);
            using var media = new Media(vlc, input);
            using var player = new MediaPlayer(vlc) { EnableHardwareDecoding = false };
            try {
                player.Play(media);
                while (player.Time < 1500 || media.Statistics.DecodedVideo < 10) {
                    Console.WriteLine(JsonSerializer.Serialize(new { session.Status, player.Time, Frames=media.Statistics.DecodedVideo, input.LastReadError }));
                    await Task.Delay(2000, budget.Token);
                }
                Console.WriteLine("PASS " + kind + " decodes real frames before full download: " + JsonSerializer.Serialize(session.Status));
                player.Time = 15000; await Task.Delay(1000, budget.Token);
                while (player.Time < 17000 || !player.IsPlaying) await Task.Delay(100, budget.Token);
                Console.WriteLine("PASS " + kind + " seek resumes and advances");
            } finally { budget.Cancel(); await Task.Run(player.Stop); }
            await session.DisposeAsync();
            if (!session.CacheRemoved || Directory.Exists(session.CachePath)) throw new Exception("Cache cleanup failed");
            Console.WriteLine("PASS " + kind + " worker stops and cache is removed");
        }
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800))) {
            await using var session = new TorrentPlayback(root);
            try { await session.OpenAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", cancel.Token); throw new Exception("Missing magnet completed"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS missing magnet cancellation"); }
            await session.DisposeAsync();
            if (!session.CacheRemoved) throw new Exception("Cancelled cache retained");
        }
        Console.WriteLine("NATIVE PLAYBACK PASSED");
    }

    // Compare every byte fed to the real decoder with the original fixture,
    // including short reads and the tail index accessed during seek.
    private sealed class FixtureStream(Stream inner, byte[] original) : Stream
    {
        public override bool CanRead=>true;
        public override bool CanSeek=>true;
        public override bool CanWrite=>false;
        public override long Length=>inner.Length;
        public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override int Read(byte[] buffer,int offset,int count)=>ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){
            var position=inner.Position;
            var count=await inner.ReadAsync(buffer,ct);
            if(!buffer.Span[..count].SequenceEqual(original.AsSpan((int)position,count))){
                Console.WriteLine($"FAIL fixture bytes at {position}, length {count}");
                throw new InvalidDataException("Verified transport bytes differ from fixture");
            }
            return count;
        }
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void Flush(){}
        public override void SetLength(long length)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
