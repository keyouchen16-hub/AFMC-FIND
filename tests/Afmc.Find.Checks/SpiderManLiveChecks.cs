using Afmc.Find.App;
using Afmc.Find.Core;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using LibVLCSharp.Shared;

internal static class SpiderManLiveChecks
{
    public static async Task DownloadControlAsync()
    {
        var root=Path.GetFullPath(".tools/monotorrent-download-control/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var snapshots=new List<object>();
        using var engine=new MonoTorrent.Client.ClientEngine(new MonoTorrent.Client.EngineSettingsBuilder{
            CacheDirectory=Path.Combine(root,"metadata"),AllowPortForwarding=false,
            ListenEndPoints=new Dictionary<string,System.Net.IPEndPoint>{["ipv4"]=new(System.Net.IPAddress.Any,0)},
            DhtEndPoint=new(System.Net.IPAddress.Any,0),MaximumConnections=60,MaximumDownloadRate=1024*1024}.ToSettings());
        var magnet=MonoTorrent.MagnetLink.Parse(PublicTorrentDiscovery.Complete("magnet:?xt=urn:btih:8148c169c4b84048fcf8d44aaf275f82bc33829e",true));
        var manager=await engine.AddAsync(magnet,Path.Combine(root,"content"));
        foreach(var tracker in manager.TrackerManager.Tiers.SelectMany(t=>t.Trackers).ToArray())await manager.TrackerManager.RemoveTrackerAsync(tracker);
        foreach(var tracker in magnet.AnnounceUrls)await manager.TrackerManager.AddTrackerAsync(new Uri(tracker));
        var started=DateTimeOffset.UtcNow;
        try {
            await manager.StartAsync();
            while((DateTimeOffset.UtcNow-started).TotalSeconds<60 && manager.Monitor.DataBytesReceived<16*1024*1024){
                await Task.Delay(2000);
                var snapshot=new{Seconds=(DateTimeOffset.UtcNow-started).TotalSeconds,State=manager.State.ToString(),Nodes=engine.Dht.NodeCount,Peers=manager.OpenConnections,Bytes=manager.Monitor.DataBytesReceived,manager.Progress};
                snapshots.Add(snapshot);Console.WriteLine(JsonSerializer.Serialize(snapshot));
            }
        } finally {
            await File.WriteAllTextAsync(Path.GetFullPath("docs/evidence/v0810/followup/monotorrent-download.json"),JsonSerializer.Serialize(snapshots,new JsonSerializerOptions{WriteIndented=true}));
            await manager.StopAsync(TimeSpan.FromSeconds(3));engine.Dispose();
            if(Path.GetDirectoryName(root)==Path.GetFullPath(".tools/monotorrent-download-control"))Directory.Delete(root,true);
        }
    }
    public static async Task TrackerPlaybackAsync(bool useLibraryStreaming = false)
    {
        var root=Path.GetFullPath(".tools/public-tracker-playback");Directory.CreateDirectory(root);
        using var service=new SearchService();var launcher=new DownloadLauncher(service,root);
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(140));
        var runtime=await PlayerRuntime.EnsureAsync(null,budget.Token);LibVLCSharp.Shared.Core.Initialize(runtime);
        using var vlc=new LibVLC("--no-video-title-show","--no-lua");
        var rows=new[]{
            ("Spider-Man Brand New Day 2026","26f1ce29e36b0b4b72a1b0974776af082f97fcb0"),
            ("Spider-Man No Way Home 2021","8148c169c4b84048fcf8d44aaf275f82bc33829e")}
            .Select(r=>PublicTorrentDiscovery.Mark(new SearchResult{Title=r.Item1,Source="海盗湾",Magnet="magnet:?xt=urn:btih:"+r.Item2},"PirateBay")).ToArray();
        var sessions=new ConcurrentBag<TorrentPlayback>();var snapshots=new List<object>();
        var started=DateTimeOffset.UtcNow;bool decoded=false;string? title=null;
        try {
            var task=PlaybackRace.RunAsync(rows.Select(r=>new PlaybackCandidate(r.Title,ct=>launcher.PreparePlaybackAsync(r,ct),outcome=>Console.WriteLine($"EVIDENCE {r.Title}: {outcome}"))).ToArray(),
                ()=>{var s=new TorrentPlayback(Path.Combine(root,"cache"),useLibraryStreaming:useLibraryStreaming);sessions.Add(s);return s;},vlc,Console.WriteLine,budget.Token);
            while(!task.IsCompleted){await Task.WhenAny(task,Task.Delay(2000));
                var snapshot=new{Seconds=(DateTimeOffset.UtcNow-started).TotalSeconds,Network=sessions.Select(s=>s.Status).ToArray()};
                snapshots.Add(snapshot);Console.WriteLine(JsonSerializer.Serialize(snapshot));}
            var winner=await task;decoded=true;title=winner.Title;
            Console.WriteLine($"PUBLIC TRACKER PLAYBACK DECODED {title} after {(DateTimeOffset.UtcNow-started).TotalSeconds:F1}s");
            await winner.Session.DisposeAsync();
        } finally {
            await File.WriteAllTextAsync(Path.Combine(root,useLibraryStreaming?"library-report.json":"report.json"),JsonSerializer.Serialize(new{Decoded=decoded,Title=title,Seconds=(DateTimeOffset.UtcNow-started).TotalSeconds,Snapshots=snapshots},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    public static async Task ControlAsync(bool soak = false)
    {
        using var service=new SearchService();
        var root=Path.GetFullPath(".tools/public-playback-control");Directory.CreateDirectory(root);
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(soak ? 300 : 140));
        var cached=Environment.GetEnvironmentVariable("AFMC_CONTROL_CACHED_TORRENT")=="1";
        Console.WriteLine(cached?"INPUT: previously saved official Sintel torrent; fresh remote fetch NOT tested":"INPUT: fresh official Sintel torrent fetch");
        var bytes=cached?await File.ReadAllBytesAsync(Path.Combine(root,"sintel.torrent"),ct.Token):await service.GetBytesAsync(new Uri("https://webtorrent.io/torrents/sintel.torrent"),8*1024*1024,ct.Token,true);
        _=TorrentFile.Validate(bytes);var path=Path.Combine(root,"sintel.torrent");await File.WriteAllBytesAsync(path,bytes,ct.Token);
        var runtime=await PlayerRuntime.EnsureAsync(null,ct.Token);LibVLCSharp.Shared.Core.Initialize(runtime);
        using var vlc=new LibVLC("--no-video-title-show","--no-lua","--vout=dummy","--aout=dummy","--avcodec-hw=none");
        TorrentPlayback? session=null;
        try {
            var task=PlaybackRace.RunAsync([new("Official public Sintel test torrent",_=>Task.FromResult(path))],
                ()=>session=new TorrentPlayback(Path.Combine(root,"cache")),vlc,Console.WriteLine,ct.Token);
            while(!task.IsCompleted){await Task.WhenAny(task,Task.Delay(3000));
                if(session is not null)Console.WriteLine("CONTROL "+JsonSerializer.Serialize(session.Status));}
            var winner=await task;Console.WriteLine("CONTROL decoded video successfully");
            try {
                if(soak) {
                    var stream=await winner.Session.SelectAsync(winner.Session.Videos.OrderByDescending(f=>f.Length).First(),ct.Token);
                    using var input=new PlaybackStreamInput(stream,ct.Token);
                    using var media=new Media(vlc,input); PlaybackMedia.Configure(media,winner.Session.Videos.OrderByDescending(f=>f.Length).First().Path);
                    using var player=new MediaPlayer(vlc){EnableHardwareDecoding=false};
                    try {
                        player.Play(media);
                        while(player.Time<120000){
                            await Task.Delay(5000,ct.Token);
                            Console.WriteLine(JsonSerializer.Serialize(new{Seconds=player.Time/1000d,Frames=media.Statistics.DecodedVideo,winner.Session.Status}));
                            if(player.State is VLCState.Error or VLCState.Ended)throw new IOException("Public playback stopped early");
                        }
                        var frames=media.Statistics.DecodedVideo;player.Time=180000;await Task.Delay(1000,ct.Token);
                        while(player.Time<183000 || media.Statistics.DecodedVideo<=frames)await Task.Delay(100,ct.Token);
                        Console.WriteLine("PASS public video plays two minutes and seeks with continued decoding");
                    } finally {ct.Cancel();await Task.Run(player.Stop);}
                }
            } finally {await winner.Session.DisposeAsync();}
        }catch(IOException e){Console.WriteLine("CONTROL failed: "+e.Message);throw;}
    }
    public static async Task RunAsync(string query = "蜘蛛侠")
    {
        using var service = new SearchService();
        var settings = new SettingsStore();
        var rows = new ConcurrentBag<SearchResult>();
        using (var search = new CancellationTokenSource(TimeSpan.FromMinutes(2))) {
            try { await service.SearchAsync(query, settings.Load(), report => {
                foreach(var row in report.Results) rows.Add(row);
                Console.WriteLine($"SEARCH {report.Name}: {report.Results.Count} results, failed={report.Error is not null}");
            }, search.Token, 1); } catch(OperationCanceledException) { Console.WriteLine("Search budget reached; keeping returned results"); }
        }
        var merged = ResultPresentation.WithCovers(ResultMerger.Merge(rows));
        var aliases = SearchQuality.Aliases(query, merged);
        var candidates = PlaybackHealth.Candidates(merged.Where(r=>r.ShareUrl is null && SearchQuality.Category(r) is "影视" or "动漫")
            .OrderByDescending(r=>aliases.Select(q=>ResultPresentation.Score(q,r,DateTimeOffset.UtcNow)).Max()));
        Console.WriteLine($"LIVE {merged.Count} results; testing {candidates.Length} candidates");
        var runtime=await PlayerRuntime.EnsureAsync(null,CancellationToken.None);
        LibVLCSharp.Shared.Core.Initialize(runtime);
        using var vlc = new LibVLC("--no-video-title-show","--no-lua","--no-metadata-network-access");
        var root = Path.GetFullPath(".tools/spiderman-live"); Directory.CreateDirectory(root);
        var launcher = new DownloadLauncher(service,root);
        var evidence = new ConcurrentBag<object>();
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        foreach(var batch in candidates.Chunk(3)) {
            if(budget.IsCancellationRequested)break;
            var sessions = new ConcurrentBag<TorrentPlayback>();
            var started=DateTimeOffset.UtcNow;
            try {
                var winner=await PlaybackRace.RunAsync(batch.Select(r=>new PlaybackCandidate(r.Title,ct=>launcher.PreparePlaybackAsync(r,ct), outcome=> {
                    var network=sessions.Select(s=>s.Status).ToArray();
                    evidence.Add(new {r.Title,r.Source,Hash=Links.MagnetHash(r.Magnet)??r.Hash,r.Seeders,Outcome=outcome.ToString(),Seconds=(DateTimeOffset.UtcNow-started).TotalSeconds,Network=network});
                    Console.WriteLine($"RESULT {outcome}: {r.Title}");
                })).ToArray(),()=>{var session=new TorrentPlayback(Path.Combine(root,"cache"));sessions.Add(session);return session;},vlc,Console.WriteLine,budget.Token);
                await winner.Session.DisposeAsync();
            }catch(IOException){Console.WriteLine("Batch did not produce decoded video");}
            catch(OperationCanceledException){Console.WriteLine("Probe budget ended");}
        }
        await File.WriteAllTextAsync(Path.Combine(root,query=="蜘蛛侠"?"report.json":"english-report.json"),JsonSerializer.Serialize(new {Query=query,SearchCount=merged.Count,CandidateCount=candidates.Length,Checks=evidence}, new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Live evidence saved; no claim of success for unknown resources");
    }
}
