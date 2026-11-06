using Afmc.Find.App;
using LibVLCSharp.Shared;
using System.IO;
using System.Diagnostics;
using System.Text.Json;

internal static class ComparisonChecks
{
    private const string Hash = "2EE672984434179AA8A7C5D2EBD0A94B38EB2960";
    private static string OutputFolder()
    {
        var configured = Environment.GetEnvironmentVariable("AFMC_COMPARE_OUTPUT");
        var folder = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? "docs/evidence/thunder-comparison" : configured);
        Directory.CreateDirectory(folder);
        return folder;
    }
    public static async Task Local()
    {
        var file=Directory.GetFiles(@"D:\迅雷下载\Spider-Man Brand New Day (2026) [1080p] [WEBRip] [5.1] [YTS.GG - YTS.BZ]","*.mp4").Single();
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Core.Initialize(await PlayerRuntime.EnsureAsync(null,cancel.Token));
        using var vlc=new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
        using var media=new Media(vlc,file,FromType.FromPath);
        using var player=new MediaPlayer(vlc){EnableHardwareDecoding=false};var clock=Stopwatch.StartNew();
        try{player.Play(media);while(player.Time<1000||media.Statistics.DecodedVideo<10)await Task.Delay(100,cancel.Token);
            var data=new{Seconds=clock.Elapsed.TotalSeconds,Frames=media.Statistics.DecodedVideo,Time=player.Time,Length=player.Length};
            var json=JsonSerializer.Serialize(data);Console.WriteLine(json);await File.WriteAllTextAsync(Path.Combine(OutputFolder(),"local-decode.json"),json);
        }finally{player.Stop();}
    }

    public static async Task LocalSegment()
    {
        var file=Directory.GetFiles(@"D:\迅雷下载\Spider-Man Brand New Day (2026) [1080p] [WEBRip] [5.1] [YTS.GG - YTS.BZ]","*.mp4").Single();
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(55));
        Core.Initialize(await PlayerRuntime.EnsureAsync(null,budget.Token));
        using var vlc=new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length!=2869977168)throw new IOException("Local control file length changed");
        using var input=new PlaybackStreamInput(stream,budget.Token);
        using var media=new Media(vlc,input);PlaybackMedia.Configure(media,file);media.AddOption(":start-time=45");
        using var player=new MediaPlayer(vlc){EnableHardwareDecoding=false};
        var clock=Stopwatch.StartNew();var samples=new List<object>();
        double? started=null;double stalls=0,lastWall=0;long lastTime=0;var passed=false;string outcome="Running";
        try
        {
            player.Play(media);
            while(true)
            {
                budget.Token.ThrowIfCancellationRequested();
                var now=clock.Elapsed.TotalSeconds;var time=player.Time;var frames=media.Statistics.DecodedVideo;
                if(now-lastWall>=1)
                {
                    if(started is null&&time>=45000&&frames>=10)started=now;
                    else if(started.HasValue&&time-lastTime<=20)stalls+=now-lastWall;
                    samples.Add(new {Seconds=now,TimeMs=time,Frames=frames,State=player.State.ToString()});
                    Console.WriteLine($"LOCAL SEGMENT {now:F1}s time={time} frames={frames} stalls={stalls:F1}");
                    lastTime=time;lastWall=now;
                }
                if(time>=80000&&frames>=500){passed=true;outcome="Decoded through 45-80 second region";break;}
                if(player.State is VLCState.Error or VLCState.Ended)throw new IOException("Local segment stopped early");
                await Task.Delay(100,budget.Token);
            }
        }
        catch(Exception e){outcome=e.GetType().Name+": "+e.Message;}
        finally
        {
            budget.Cancel();await Task.Run(player.Stop);
            await File.WriteAllTextAsync(Path.Combine(OutputFolder(),"local-segment.json"),JsonSerializer.Serialize(new {Passed=passed,Outcome=outcome,Source="Previously completed local file, read-only; same expected title and size, not new BT transfer",StartSeconds=45,EndSeconds=80,StallSeconds=stalls,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(!passed)throw new IOException(outcome);
    }

    private sealed class Measurements(string folder, int duration, TorrentPlayback session)
    {
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public double? MetadataSeconds, FirstDecodedSeconds;
        public double PostStartObservedSeconds, StallSeconds, LongestStallSeconds;
        public string Outcome = "Running";
        public MediaPlayer? Player;
        public Media? Media;
        private readonly List<object> samples = [];
        private long playbackTime, firstPlaybackTime, previousPlaybackTime;
        private int decodedFrames;
        private string decoderState = "NotCreated";
        private double previousWall, currentStall;
        private object Snapshot() => new
        {
            Hash, Engine = "native libtorrent, no app speed cap; headless software decode",
            ObservationLimitSeconds = duration, RequestedPostStartSeconds = 120,
            MetadataSeconds, FirstDecodedSeconds, PostStartObservedSeconds,
            PostStartObservationComplete = FirstDecodedSeconds.HasValue && PostStartObservedSeconds >= 120,
            PlaybackAdvancedSeconds = FirstDecodedSeconds.HasValue ? Math.Max(0, playbackTime-firstPlaybackTime)/1000d : 0,
            PlaybackToWallRatio = PostStartObservedSeconds > 0 ? Math.Max(0, playbackTime-firstPlaybackTime)/1000d/PostStartObservedSeconds : (double?)null,
            StallSeconds, LongestStallSeconds,
            StallDefinition = "After startup, one-second samples whose playback clock advances no more than 20ms. Approximate clock-stall measure, not visible-frame/audio proof.",
            DecodedFrames = decodedFrames, PlaybackTimeMs = playbackTime, DecoderState = decoderState,
            Outcome, Samples = samples
        };
        public async Task Write()
        {
            var temporary = Path.Combine(folder,"afmc.json.tmp");
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(Snapshot(),new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,Path.Combine(folder,"afmc.json"),true);
        }
        // All calls run on the same async control flow; native objects are never
        // inspected by a background sampler while being stopped or disposed.
        public async Task Sample()
        {
            var now=Watch.Elapsed.TotalSeconds;
            if(samples.Count>0 && now-previousWall<1) return;
            if(Player is not null && Media is not null)
            {
                playbackTime=Player.Time; decodedFrames=Media.Statistics.DecodedVideo;
                decoderState=Player.State.ToString();
                if(FirstDecodedSeconds is null && decodedFrames>=10 && playbackTime>=1000)
                {
                    FirstDecodedSeconds=now; firstPlaybackTime=playbackTime;
                    Console.WriteLine($"DECODED {now:F3}");
                }
                else if(FirstDecodedSeconds.HasValue)
                {
                    if(playbackTime-previousPlaybackTime<=20)
                    {
                        currentStall+=now-previousWall; StallSeconds+=now-previousWall;
                        LongestStallSeconds=Math.Max(LongestStallSeconds,currentStall);
                    }
                    else currentStall=0;
                }
                if(FirstDecodedSeconds.HasValue) PostStartObservedSeconds=now-FirstDecodedSeconds.Value;
                previousPlaybackTime=playbackTime;
            }
            previousWall=now;
            var status=session.Status;
            var sample=new
            {
                Seconds=now,Status=status,DecodedFrames=decodedFrames,PlaybackTimeMs=playbackTime,DecoderState=decoderState,
                PostStartObservedSeconds,StallSeconds,LongestStallSeconds,
                PlaybackAdvancedSeconds=FirstDecodedSeconds.HasValue ? Math.Max(0,playbackTime-firstPlaybackTime)/1000d : 0,
                PlaybackToWallRatio=PostStartObservedSeconds>0 ? Math.Max(0,playbackTime-firstPlaybackTime)/1000d/PostStartObservedSeconds : (double?)null
            };
            samples.Add(sample);
            await File.AppendAllTextAsync(Path.Combine(folder,"samples.jsonl"),JsonSerializer.Serialize(sample)+Environment.NewLine);
            await Write();
            Console.WriteLine($"{now:F1}s metadata={status.Metadata} peers={status.Peers} bytes={status.Downloaded} verified={status.Verified} rate={status.Rate} time={playbackTime} frames={decodedFrames} state={decoderState}");
        }
        public async Task WaitWithSamples(Task operation,CancellationToken token)
        {
            while(!operation.IsCompleted)
            {
                await Sample();
                await Task.WhenAny(operation,Task.Delay(100,token));
                token.ThrowIfCancellationRequested();
            }
            await operation;
        }
    }
    public static async Task Run()
    {
        var folder=OutputFolder();
        await File.WriteAllTextAsync(Path.Combine(folder,"samples.jsonl"),string.Empty);
        var duration=int.TryParse(Environment.GetEnvironmentVariable("AFMC_COMPARE_SECONDS"),out var requested) ? Math.Clamp(requested,30,600) : 240;
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(duration));
        await using var session=new TorrentPlayback(Path.GetFullPath(".tools/comparison-cache"));
        var metrics=new Measurements(folder,duration,session);
        try
        {
            await metrics.WaitWithSamples(session.OpenAsync(Environment.GetEnvironmentVariable("AFMC_COMPARE_TORRENT")??"magnet:?xt=urn:btih:"+Hash,budget.Token),budget.Token);
            metrics.MetadataSeconds=metrics.Watch.Elapsed.TotalSeconds;
            var file=session.Videos.OrderByDescending(f=>f.Length).First();
            Console.WriteLine("VIDEO "+file.Path+" SIZE "+file.Length);
            Core.Initialize(await PlayerRuntime.EnsureAsync(null,budget.Token));
            using var vlc=new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua");
            using var stream=await session.SelectAsync(file,budget.Token);
            using var input=new PlaybackStreamInput(stream,budget.Token);
            using var media=new Media(vlc,input); PlaybackMedia.Configure(media,file.Path);
            using var decoder=new MediaPlayer(vlc){EnableHardwareDecoding=false};
            metrics.Player=decoder; metrics.Media=media;
            try
            {
                decoder.Play(media);
                while(true)
                {
                    budget.Token.ThrowIfCancellationRequested();
                    await metrics.Sample();
                    if(metrics.FirstDecodedSeconds.HasValue && metrics.PostStartObservedSeconds>=120)
                    {metrics.Outcome="120-second post-start observation complete";break;}
                    if(decoder.State is VLCState.Error or VLCState.Ended)
                        throw new IOException("Decoder stopped before observation completed: "+decoder.State);
                    await Task.Delay(100,budget.Token);
                }
            }
            finally
            {
                // Pending byte reads must be cancelled before native decoder Stop.
                budget.Cancel(); metrics.Player=null; metrics.Media=null;
                await Task.Run(decoder.Stop);
            }
        }
        catch(Exception e)
        {
            metrics.Outcome=e is OperationCanceledException ? $"{duration}-second overall observation limit reached" : e.GetType().Name+": "+e.Message;
            Console.WriteLine(metrics.Outcome);
        }
        finally
        {
            budget.Cancel(); await session.DisposeAsync(); await metrics.Write();
        }
    }

    public static async Task Reuse()
    {
        var root = OutputFolder();
        for (var round = 0; round < 3; round++)
        {
            Environment.SetEnvironmentVariable("AFMC_COMPARE_OUTPUT", Path.Combine(root, round == 0 ? "cold" : $"warm{round}"));
            await Run(); // Each run disposes its worker and fresh video cache.
        }
        Environment.SetEnvironmentVariable("AFMC_COMPARE_OUTPUT", root);
    }
}
