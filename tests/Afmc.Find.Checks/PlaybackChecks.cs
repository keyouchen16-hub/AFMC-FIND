using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Afmc.Find.App;
using Afmc.Find.Core;
using LibVLCSharp.Shared;
using MonoTorrent;
using MonoTorrent.Client;

internal static class PlaybackChecks
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
    private static async Task Until(Func<bool> condition, string name, int seconds = 30)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(100);
        Check(condition(), name);
    }
    private static async Task SeekResumed(MediaPlayer player, long target, string name)
    {
        await Task.Delay(1000); // libVLC's Time setter briefly echoes the requested value before decoding.
        var until = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < until) {
            var before = player.Time; await Task.Delay(800);
            if (before >= target && player.Time > before && player.IsPlaying) { Check(true, name); return; }
        }
        throw new Exception(name + ": playback did not advance after the seek");
    }
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = new ResourceDictionary { Source = new Uri("/AFMC.Find;component/Theme.xaml", UriKind.Relative) } };
        var root = Path.GetFullPath(".tools/player-fixture"); Directory.CreateDirectory(root);
        var sessions = new List<TorrentPlayback>();
        var window = new PlayerWindow(Path.Combine(root, "profile"), cache => { var session = new TorrentPlayback(cache, new EngineSettingsBuilder {
            CacheDirectory = Path.Combine(root, "client-meta-" + Guid.NewGuid().ToString("N")), AllowPortForwarding = false, DhtEndPoint = null,
            ListenEndPoints = new Dictionary<string, IPEndPoint>()
        }.ToSettings()); sessions.Add(session); return session; });
        var exit = 1;
        window.Loaded += async (_, _) => {
            try
            {
                await window.Dispatcher.InvokeAsync(window.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var idleLogo=(Image)window.FindName("PlayerIdleLogo");
                Check(window.FindName("PlayerBrand") is null && ((TextBlock)window.FindName("PlayerDragArea")).Text.Length==0,"player caption has no visible brand or version text");
                Check(idleLogo.Source is not null&&idleLogo.IsVisible&&((TextBlock)window.FindName("Placeholder")).Text.Length==0,"idle player uses the embedded software logo");
                var startup=new PlaybackStartupBudget();
                Check(startup.Observe(0,0,0)==60 && startup.Observe(0,50,0)==60,"connected but no payload cannot extend startup");
                Check(startup.Observe(0,51,131072)==81,"real payload progress extends an active candidate");
                Check(startup.Observe(0,70,131072)==81,"stalled payload cannot keep extending startup");
                Check(startup.Observe(1,75,2000000)==81,"a new candidate cannot claim another candidate's progress");
                Check(startup.Observe(0,95,262144)==120 && startup.Observe(0,110,400000)==120,"continuous progress has a hard two-minute cap");
                var observedHealth=new PlaybackHealth();var observedRow=new SearchResult{Hash=new string('f',40),Title="fixture",Source="fixture"};
                observedHealth.Record(observedRow,PlaybackEvidence.DataReceived,DateTimeOffset.UtcNow);
                Check(observedHealth.Tier(observedRow,DateTimeOffset.UtcNow)==3&&observedHealth.Get(observedRow,DateTimeOffset.UtcNow).Label.Contains("首片未完成校验"),"unverified payload never receives a playable badge");
                var video = Path.Combine(root, "AFMC-test.avi"); CreateAvi(video);
                Check(new FileInfo(video).Length > 10_000_000, "generated original 30-second video fixture, no third-party content");
                var nativePackage = Path.GetFullPath(".tools/videolan.libvlc.windows.3.0.24.nupkg");
                await PlayerRuntime.EnsureAsync(new Progress<string>(s => Console.WriteLine(s)), CancellationToken.None, File.Exists(nativePackage) ? nativePackage : null);
                Check(File.Exists(Path.Combine(PlayerRuntime.DefaultDirectory, "libvlc.dll")), "pinned native runtime extracted and verified");
                var invalid = Path.Combine(root, "invalid.nupkg"); await File.WriteAllTextAsync(invalid, "invalid download");
                try { await PlayerRuntime.EnsureAsync(null, CancellationToken.None, invalid, Path.Combine(root, "bad-runtime")); throw new Exception("invalid runtime accepted"); }
                catch (InvalidDataException) { Check(!Directory.Exists(Path.Combine(root, "bad-runtime")), "invalid runtime hash rejected without installation"); }
                using (var cancel = new CancellationTokenSource()) {
                    cancel.Cancel();
                    try { await PlayerRuntime.EnsureAsync(null, cancel.Token); throw new Exception("cancel ignored"); }
                    catch (OperationCanceledException) { Check(true, "runtime preparation respects cancellation"); }
                }
                try { PlayerRuntime.SafeChild(root, "../outside"); throw new Exception("traversal accepted"); } catch (InvalidDataException) { Check(true, "runtime and torrent path traversal rejected"); }
                using var http = new VideoServer(video);
                try { await PlaybackAddress.ValidateDirectAsync(http.Url + "page.avi", CancellationToken.None); throw new Exception("HTML accepted"); } catch (InvalidDataException) { Check(true, "HTML disguised as video is rejected"); }
                await window.OpenAsync(http.Url + "test.avi");
                await Until(() => window.NativePlayer is { Time: > 1000, IsPlaying: true }, "HTTP direct link decodes and advances video time");
                var player = window.NativePlayer!;
                Check(!idleLogo.IsVisible,"idle logo is hidden during decoded video playback");
                Check(player.VideoTrack > -1, "real video track decoded");
                Check(player.AudioTrack > -1, "generated PCM audio track decoded");
                var subtitle = Path.Combine(root, "sample.srt"); await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:29,000\nAFMC FIND playback test\n");
                Check(player.AddSlave(MediaSlaveType.Subtitle, new Uri(subtitle).AbsoluteUri, true), "local subtitle accepted by native decoder");
                await Until(() => player.SpuCount > 0, "external subtitle track becomes available");
                player.SetPause(true); await Task.Delay(250); var paused = player.Time; await Task.Delay(700);
                Check(Math.Abs(player.Time - paused) < 150, "pause holds playback position");
                player.SetPause(false);
                var seek = (Slider)window.FindName("SeekBar"); seek.Value = 500;
                seek.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                await SeekResumed(player, 15000, "direct link seek resumes and clock keeps advancing");
                ((Slider)window.FindName("VolumeBar")).Value = 31; await Until(() => Math.Abs(player.Volume - 31) <= 1, "volume UI changes native audio output volume", 5);
                ((Button)window.FindName("FullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); var fullVideo = (FrameworkElement)window.FindName("VideoSurface"); Check(window.IsFullscreen && window.Topmost && Math.Abs(fullVideo.ActualHeight-window.ActualHeight)<3 && Math.Abs(fullVideo.ActualWidth-window.ActualWidth)<3, "fullscreen video surface fills entire window");
                ((Button)window.FindName("FullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!window.IsFullscreen && window.WindowState == WindowState.Normal, "fullscreen restores window");
                var floating=(FrameworkElement)window.FindName("FloatingBar");
                var overlay=(FrameworkElement)window.FindName("VideoOverlay");
                var surface=(FrameworkElement)window.FindName("VideoSurface");
                await window.Dispatcher.InvokeAsync(window.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var beforeFade=surface.RenderSize;
                Check(PresentationSource.FromVisual(floating)!=PresentationSource.FromVisual(window) && floating.IsVisible,"floating controls hosted above native video");
                await Until(()=>floating.Opacity<0.02,"idle controls fade away",8);
                overlay.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount){ RoutedEvent=UIElement.PreviewMouseMoveEvent });
                await Until(()=>floating.Opacity>0.98,"pointer movement restores controls",2);
                Check(beforeFade==surface.RenderSize,"hiding and showing controls preserves video size");                if(Environment.GetEnvironmentVariable("AFMC_LIVE_SUBTITLE_CHECK")=="1")
                {
                    var subtitleControl=(Button)window.FindName("SubtitleButton");
                    subtitleControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var online=((StackPanel)window.FindName("SubtitleChoices")).Children.OfType<Button>().Single(x=>Equals(x.Content,"搜索在线字幕…"));
                    var previousTrackCount=player.SpuCount;
                    var modal=window.Dispatcher.InvokeAsync(()=>online.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    await Until(()=>Application.Current.Windows.OfType<Window>().Any(w=>w.Title=="在线字幕"),"online subtitle picker opened");
                    var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="在线字幕");
                    static IEnumerable<DependencyObject> Walk(DependencyObject node) {
                        yield return node;
                        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
                            foreach(var child in Walk(System.Windows.Media.VisualTreeHelper.GetChild(node,i))) yield return child;
                    }
                    T Find<T>(string name) where T:FrameworkElement => Walk(dialog).OfType<T>().Single(c=>c.Name==name);
                    Find<TextBox>("SubtitleQuery").Text="Spider-Man";
                    Find<Button>("SearchSubtitles").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var list=Find<ListBox>("SubtitleResults"); var choose=Find<Button>("ChooseSubtitle");
                    await Until(()=>choose.IsEnabled && list.Items.Count>0,"online title search returns films");
                    list.SelectedItem=list.Items.Cast<object>().First(x=>x.ToString()!.Contains("2002"));
                    choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(()=>choose.IsEnabled && list.Items.Count>0 && Equals(choose.Content,"加载选中字幕"),"online provider returns selectable subtitle versions");
                    list.SelectedItem=list.Items.Cast<object>().First(x=>x.ToString()!.StartsWith("英语"));
                    choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(()=>!dialog.IsVisible && player.SpuCount>previousTrackCount,"downloaded online subtitle added to native player");
                    await modal;
                }                await DialogGlassChecks.Capture(window, "player-direct");
                await window.StopAsync(); Check(!player.IsPlaying, "stop ends direct playback");
                Check(http.RangeRequests > 0, "direct video server handled real HTTP range requests");

                var creator = new TorrentCreator { PieceLength = 32 * 1024 };
                var torrentPath = Path.Combine(root, "sample.torrent");
                var encoded = await creator.CreateAsync(new TorrentFileSource(video));
                await File.WriteAllBytesAsync(torrentPath, encoded.Encode());
                var source = await Torrent.LoadAsync(torrentPath);
                var port = FreePort();
                using var engine = new ClientEngine(new EngineSettingsBuilder { CacheDirectory = Path.Combine(root, "seed-meta-"+Guid.NewGuid().ToString("N")), AllowPortForwarding = false, DhtEndPoint = null,
                    ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) }, MaximumUploadRate = 512 * 1024 }.ToSettings());
                var seed = await engine.AddAsync(source, root); await seed.StartAsync();
                await Until(() => seed.State == TorrentState.Seeding, "controlled torrent peer hash-checks and seeds original fixture");
                async Task StartPeerPlayback(string address, string label, int videoCount = 1)
                {
                    var opening = window.OpenAsync(address);
                    await Until(() => window.TorrentSession?.Manager is not null, label + " creates torrent session");
                    await window.TorrentSession!.Manager!.AddPeerAsync(new PeerInfo(new Uri("ipv4://127.0.0.1:" + port)));
                    for (var attempt = 0; attempt < 12 && !opening.IsCompleted; attempt++) {
                        await Task.WhenAny(opening, Task.Delay(5000));
                        if (window.TorrentSession?.Manager is {} m) {
                            var peers = await m.GetPeersAsync();
                            Console.WriteLine($"TRACE {label}: {m.State} progress={m.Progress:0.0} bytes={m.Monitor.DataBytesReceived} peers={peers.Count} position={window.TorrentSession.Stream?.Position} v2={m.InfoHashes.V2 is not null}");
                        }
                    }
                    await opening.WaitAsync(TimeSpan.FromSeconds(5));
                    try { await Until(() => player is { Time: > 500, IsPlaying: true }, label + " video plays before complete download", 45); }
                    catch {
                        var m = window.TorrentSession?.Manager;
                        var pm = m?.GetType().GetProperty("PieceManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(m);
                        var requester = pm?.GetType().GetProperty("Requester", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(pm);
                        var hp = requester?.GetType().GetProperty("HighPriorityPieceIndex")?.GetValue(requester);
                        var peers = m is null ? [] : await m.GetPeersAsync();
                        Console.WriteLine("DIAGNOSTIC " + ((TextBlock)window.FindName("PlayerStatus")).Text + " state=" + player.State + " time=" + player.Time + " torrent=" + m?.State + " pos=" + window.TorrentSession?.Stream?.Position + " highPriority=" + hp + " firstbits=" + string.Join("", Enumerable.Range(0, Math.Min(50, m?.Bitfield.Length ?? 0)).Select(i=>m!.Bitfield[i]?"1":"0")) + " peers=" + string.Join(";", peers.Select(p=>p.IsChoking + "/" + p.AmInterested)));
                        throw;
                    }
                    Check(window.TorrentSession!.Manager!.Progress < 100, label + " playback is genuinely progressive");
                    var picker = (ComboBox)window.FindName("VideoFiles");
                    Check(picker.Items.Count == videoCount && picker.SelectedItem is PlaybackVideo { Path: var p } && p.EndsWith(".avi") && picker.SelectedItem.ToString() == p, label + " exposes readable video selection");
                }
                await StartPeerPlayback(torrentPath, "torrent file");
                var cache = window.TorrentSession!.CachePath;
                seek.Value = 700;
                seek.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                await SeekResumed(player, 20000, "torrent seek fetches later pieces and clock keeps advancing");
                await Task.Delay(600);
                await DialogGlassChecks.Capture(window, "player-torrent");
                await window.StopAsync(); Check(window.TorrentSession is null && !Directory.Exists(cache), "stop removes session and cached torrent content");
                await Until(()=>seed.OpenConnections==0,"previous torrent fixture peer disconnects before magnet negotiation",10);
                await StartPeerPlayback(new MagnetLink(source.InfoHashes).ToV1String(), "magnet metadata exchange");
                await window.StopAsync();
                var chosen = new SearchResult { Title = "Sample Movie 2020 1080p", Source = "fixture", Category = "电影", Magnet = "magnet:?xt=urn:btih:" + new string('a',40) };
                var alternate = chosen with { Title = "Sample Movie 2020 720p", Magnet = "magnet:?xt=urn:btih:" + new string('b',40), Seeders = 10 };
                var health = new PlaybackHealth(); var checkedAt = DateTimeOffset.UtcNow;
                Check(health.Get(chosen, checkedAt).Bonus == 0, "unprobed resources never claim playback availability");
                health.Record(chosen, PlaybackEvidence.Playable, checkedAt);
                Check(health.Get(chosen, checkedAt).Bonus > 0 && health.Get(alternate, checkedAt).Bonus == 0, "playability evidence belongs to exact resource hash");
                Check(health.Get(chosen, checkedAt.AddMinutes(31)).Bonus == 0, "playability evidence expires after thirty minutes");
                health.Record(chosen, PlaybackEvidence.DataVerified, checkedAt);
                Check(health.Get(chosen,checkedAt).Label.Contains("播放未确认"), "verified data is not falsely marked playable");
                health.Record(chosen, PlaybackEvidence.TimedOut, checkedAt);
                Check(health.Get(chosen,checkedAt).Label.Contains("下载能力未知"), "timeout does not declare download impossible");
                health.Record(alternate, PlaybackEvidence.Playable, checkedAt);
                var unknown = chosen with {Magnet="magnet:?xt=urn:btih:"+new string('c',40)};
                var ordered = health.Order([chosen,unknown,alternate], r=>r==chosen?1_000_000:r==alternate?-100:0,checkedAt).ToArray();
                Check(ordered[0]==alternate && ordered[^1]==chosen,"playable tier outranks extreme relevance; timed-out resource cannot remain first");
                Check(health.Tier(alternate,checkedAt.AddMinutes(31))==2,"expired success returns to unknown sorting tier");
                health.Forget(alternate);
                Check(health.Tier(alternate,checkedAt)==2,"expired prepared session can invalidate its playable ranking");
                Check(((FrameworkElement)window.FindName("PlayerLayout")).Margin == new Thickness(0) && ((Border)window.FindName("CanvasFrame")).BorderThickness == new Thickness(0), "ordinary player video has no frame or reserved margin");
                Check(((FrameworkElement)window.FindName("InputRow")).Parent == window.FindName("OverlayTop"), "resource input overlays video without shrinking it");
                var remake = alternate with { Title = "Sample Movie 2024 720p" };
                Check(PlaybackHealth.Candidates([chosen,alternate,remake])[1]==remake,"background probe samples another work before exhausting one movie's versions");
                var tagged=chosen with {Title="【站点】蜘蛛侠：纵横宇宙[中文字幕].Spider-Man.Across.the.Spider-Verse.2023.1080p", MetadataConflict=true};
                var taggedOther=tagged with {Title="蜘蛛侠：纵横宇宙[双语].Spider-Man.Across.the.Spider-Verse.2023.720p"};
                Check(PlaybackHealth.Candidates([tagged,taggedOther,chosen])[1]==chosen,"probe diversity ignores bracketed release labels without weakening playback substitution identity");
                var sequel = alternate with { Title = "Sample Movie 2 2020 720p" };
                Check(PlaybackCandidates.Select(chosen, [chosen, alternate, remake, sequel], "Sample Movie").SequenceEqual(new[] { chosen, alternate }), "candidate selection retains selected film and excludes remake and sequel");
                Check(PlaybackCandidates.Select(chosen, [alternate], "Sample Movie 1080p").Count == 1, "explicit query resolution is preserved");
                Check(PlaybackCandidates.Select(chosen with { Title = "Sample Movie S01E01 2020" }, [alternate], "Sample Movie").Count == 1, "series never silently switches episode");
                Check(PlaybackCandidates.Select(chosen with { Title = "Sample Movie" }, [alternate], "Sample Movie").Count == 1, "missing year does not invent work identity");
                Check(PlaybackCandidates.Select(chosen with { MetadataConflict = true }, [alternate], "Sample Movie").Count == 1, "conflicting metadata disables alternative matching");
                var raceStart = sessions.Count;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var raceOpening = window.OpenCandidatesAsync([
                    new("offline selected film", _ => Task.FromResult("magnet:?xt=urn:btih:9876543210abcdef9876543210abcdef98765432")),
                    new("online alternative", _ => Task.FromResult(torrentPath)),
                    new("queued alternative", _ => Task.FromResult(torrentPath))]);
                var injected = new HashSet<TorrentPlayback>();
                while (!raceOpening.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(35))
                {
                    foreach (var session in sessions.Skip(raceStart).ToArray())
                        if (session.Manager is {} manager && manager.HasMetadata && injected.Add(session))
                            await manager.AddPeerAsync(new PeerInfo(new Uri("ipv4://127.0.0.1:" + port)));
                    await Task.Delay(100);
                }
                await raceOpening.WaitAsync(TimeSpan.FromSeconds(5));
                Check(window.PlayingVersion == "online alternative" && elapsed.Elapsed.TotalSeconds < 25, "real decoder race bypasses offline selected version before its deadline");
                Console.WriteLine($"RACE first decoded candidate and handoff {elapsed.Elapsed.TotalSeconds:0.00}s");
                await Until(() => player.IsPlaying && player.Time > 500, "race winner transfers cached torrent into visible player and advances");
                Check(sessions.Count - raceStart == 2, "race starts at most two candidates and cancels queued third");
                Check(sessions.Skip(raceStart).Where(s => s != window.TorrentSession).All(s => !Directory.Exists(s.CachePath)), "race losers stop peers and remove caches");
                Check(window.TorrentSession!.Manager!.Progress < 100, "decoded winner starts before whole file downloads");
                await window.StopAsync();
                Check(sessions.Skip(raceStart).All(s => !Directory.Exists(s.CachePath)), "stopping winning version removes final race cache");
                using(var probe = new LibVLC("--vout=dummy","--aout=dummy","--avcodec-hw=none","--no-lua"))
                using(var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40))) {
                    var readyCache = new PreparedPlaybackCache();
                    var readyRow = new SearchResult {Title="verified fixture",Source="controlled fixture",Hash=new string('a',40)};
                    var readyKey=PreparedPlaybackCache.Key(readyRow)!;
                    await using var preparedSession = new TorrentPlayback(Path.Combine(root,"prepared"),new EngineSettingsBuilder {
                        CacheDirectory=Path.Combine(root,"prepared-meta"),AllowPortForwarding=false,DhtEndPoint=null,
                        ListenEndPoints=new Dictionary<string,IPEndPoint>()}.ToSettings());
                    var preparing = PlaybackRace.RunAsync([new("verified fixture",_=>Task.FromResult(torrentPath),ResourceKey:readyKey)],()=>preparedSession,probe,_=>{},budget.Token);
                    await Until(()=>preparedSession.Manager is not null,"background prepared session starts");
                    await preparedSession.Manager!.AddPeerAsync(new PeerInfo(new Uri("ipv4://127.0.0.1:"+port)));
                    var verified=await preparing;
                    var now=DateTimeOffset.UtcNow;
                    await readyCache.StoreAsync(readyRow,verified,now);
                    Check(readyCache.Take(readyRow with {Hash=new string('b',40)},now) is null,"prepared cache never substitutes a different resource hash");
                    var taken=readyCache.Take(readyRow,now);
                    Check(ReferenceEquals(taken?.Session,preparedSession) && readyCache.Take(readyRow,now) is null,"prepared session ownership transfers exactly once");
                    var factoryCount=sessions.Count;
                    await window.OpenWinnerAsync(taken!);
                    await Until(()=>player.IsPlaying && player.Time>500,"background-verified bytes play in visible player without another race");
                    Check(sessions.Count==factoryCount && ReferenceEquals(window.TorrentSession,preparedSession),"prepared handoff creates no replacement download session");
                    await readyCache.ClearAsync();
                    Check(!preparedSession.CacheRemoved,"clearing cache does not dispose player-owned transfer");
                    await window.StopAsync();
                    Check(preparedSession.CacheRemoved,"player stop releases transferred worker and cache");
                    await using var expired=new TorrentPlayback(Path.Combine(root,"prepared-expired"));
                    await readyCache.StoreAsync(readyRow,new(readyRow.Title,expired,readyKey),now);
                    Check(readyCache.Take(readyRow,now.AddMinutes(2)) is null,"expired prepared session cannot be taken");
                    var expiredRow=await readyCache.ClearAsync(now.AddMinutes(2));
                    Check(expiredRow==readyRow && expired.CacheRemoved && !readyCache.HasPending,"expiry releases its owned cache and reports evidence to invalidate");
                }
                raceStart = sessions.Count;
                var cancelRace = window.OpenCandidatesAsync([new("offline", _ => Task.FromResult("magnet:?xt=urn:btih:9876543210abcdef9876543210abcdef98765432"))]);
                await Until(() => sessions.Count > raceStart && sessions[^1].Manager is not null, "race cancellation fixture has active metadata request");
                await window.StopAsync().WaitAsync(TimeSpan.FromSeconds(8)); await cancelRace;
                Check(sessions.Skip(raceStart).All(s => !Directory.Exists(s.CachePath)) && !player.IsPlaying, "stop cancels in-flight candidate race without late playback");
                using (var probeVlc = new LibVLC("--no-video-title-show", "--no-lua"))
                {
                    var temporary = new List<TorrentPlayback>();
                    TorrentPlayback CreateProbe()
                    {
                        var session = new TorrentPlayback(Path.Combine(root, "timeout-cache"), new EngineSettingsBuilder {
                            CacheDirectory = Path.Combine(root, "timeout-meta", Guid.NewGuid().ToString("N")),
                            DhtEndPoint = null, AllowPortForwarding = false, ListenEndPoints = new Dictionary<string,IPEndPoint>() }.ToSettings());
                        temporary.Add(session); return session;
                    }
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await PlaybackRace.RunAsync(Enumerable.Range(0, 3).Select(i => new PlaybackCandidate("offline " + i,
                            _ => Task.FromResult("magnet:?xt=urn:btih:9876543210abcdef9876543210abcdef98765432"))).ToArray(),
                            CreateProbe, probeVlc, _ => { }, CancellationToken.None, TimeSpan.FromMilliseconds(700));
                        throw new Exception("offline race unexpectedly won");
                    }
                    catch (IOException e) { Check(e.Message.Contains("限时"), "all unavailable candidates produce actionable bounded failure"); }
                    Check(temporary.Count == 3 && deadline.Elapsed.TotalSeconds < 10, "queued third candidate starts after earlier timeouts with bounded duration");
                    Check(temporary.All(s => !Directory.Exists(s.CachePath)), "all-failed race removes all session caches");
                }
                var multiDir = Path.Combine(root, "episodes"); Directory.CreateDirectory(multiDir);
                File.Copy(video, Path.Combine(multiDir, "Episode-01.avi"), true); File.Copy(video, Path.Combine(multiDir, "Episode-02.avi"), true);
                await File.WriteAllTextAsync(Path.Combine(multiDir, "README.txt"), "Not a playable file");
                var multiEncoded = await creator.CreateAsync(new TorrentFileSource(multiDir));
                var multiPath = Path.Combine(root, "episodes.torrent"); await File.WriteAllBytesAsync(multiPath, multiEncoded.Encode());
                var multi = await engine.AddAsync(await Torrent.LoadAsync(multiPath), root); await multi.StartAsync();
                await Until(() => multi.State == TorrentState.Seeding, "multi-file seed is ready");
                await StartPeerPlayback(multiPath, "multi-file torrent", 2);
                ((ComboBox)window.FindName("VideoFiles")).SelectedIndex = 1;
                for (var i = 0; i < 12 && !window.CurrentOperation.IsCompleted; i++) {
                    await Task.WhenAny(window.CurrentOperation, Task.Delay(5000));
                    Console.WriteLine($"TRACE episode switch: {((TextBlock)window.FindName("PlayerStatus")).Text} position={window.TorrentSession?.Stream?.Position} progress={window.TorrentSession?.Manager?.Progress:0.0}");
                }
                await window.CurrentOperation.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => player.Time > 500 && player.IsPlaying, "switching episode resumes native playback");
                Check(window.TorrentSession!.Manager!.Files.Count(f => f.Priority != Priority.DoNotDownload) == 1, "only selected video is prioritised; text and other episode excluded");
                await window.StopAsync();
                var noPeer = window.OpenAsync("magnet:?xt=urn:btih:9876543210abcdef9876543210abcdef98765432");
                await Until(() => window.TorrentSession?.Manager?.State == TorrentState.Metadata, "no-peer magnet waits for metadata");
                cache = window.TorrentSession!.CachePath;
                await window.StopAsync().WaitAsync(TimeSpan.FromSeconds(8)); await noPeer;
                Check(!Directory.Exists(cache) && !player.IsPlaying, "cancel without peers is prompt and removes cache");
                var stale = window.OpenAsync("magnet:?xt=urn:btih:9876543210abcdef9876543210abcdef98765432");
                await Until(() => window.TorrentSession?.Manager?.State == TorrentState.Metadata, "selected resource waits for peers");
                var next = window.OpenAsync(http.Url + "test.avi");
                await Task.WhenAll(stale,next).WaitAsync(TimeSpan.FromSeconds(12));
                await Until(() => player.IsPlaying && player.Time > 500, "selecting another resource replaces waiting magnet and plays");
                await window.StopAsync();
                var immediate = window.OpenAsync(http.Url + "test.avi"); await window.StopAsync().WaitAsync(TimeSpan.FromSeconds(8)); await immediate;
                Check(!player.IsPlaying, "immediate stop during preparation prevents delayed playback");
                await StartPeerPlayback(torrentPath, "reopen after cancel");
                cache = window.TorrentSession!.CachePath;
                await window.ClosePlayerAsync(); Check(!Directory.Exists(cache), "closing player stops peers and removes cache");
                await seed.StopAsync();
                await multi.StopAsync();
                var recoveryRoot = Path.Combine(root, "recovery"); var orphan = Path.Combine(recoveryRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
                var unrelated = Path.Combine(recoveryRoot, "user-folder"); Directory.CreateDirectory(unrelated);
                await using (var live = new TorrentPlayback(recoveryRoot, new EngineSettingsBuilder { CacheDirectory = Path.Combine(root, "recovery-meta"), DhtEndPoint = null, AllowPortForwarding = false, ListenEndPoints = new Dictionary<string, IPEndPoint>() }.ToSettings())) {
                    TorrentPlayback.RecoverCache(recoveryRoot); Check(Directory.Exists(live.CachePath), "cache recovery preserves active session lock");
                }
                TorrentPlayback.RecoverCache(recoveryRoot);
                Check(!Directory.Exists(orphan) && Directory.Exists(unrelated), "cache recovery removes only owned abandoned session folders");
                Console.WriteLine("PLAYBACK PASSED " + passed); exit = 0;
            }
            catch (Exception e) { Console.Error.WriteLine("PLAYBACK FAILED " + e); }
            finally { await window.ClosePlayerAsync(); app.Shutdown(exit); }
        };
        app.Run(window); return exit;
    }

    public static async Task ServeAsync()
    {
        var root = Path.GetFullPath(".tools/player-installed-fixture"); Directory.CreateDirectory(root);
        var video = Path.Combine(root, "AFMC-original.avi"); CreateAvi(video);
        using var http = new VideoServer(video);
        var port = FreePort(); http.SeedPort = port;
        var creator = new TorrentCreator(TorrentType.V1Only) { PieceLength = 32 * 1024 };
        creator.Announces.Add(new List<string> { http.Url + "announce" });
        var encoded = await creator.CreateAsync(new TorrentFileSource(video));
        var torrentPath = Path.Combine(root, "AFMC-original.torrent"); await File.WriteAllBytesAsync(torrentPath, encoded.Encode());
        var source = await Torrent.LoadAsync(torrentPath);
        using var engine = new ClientEngine(new EngineSettingsBuilder { CacheDirectory = Path.Combine(root, "seed-meta"), AllowPortForwarding = false, DhtEndPoint = null,
            ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(IPAddress.Loopback, port) }, MaximumUploadRate = 512 * 1024 }.ToSettings());
        var seed = await engine.AddAsync(source, root); await seed.StartAsync();
        await Until(() => seed.State == TorrentState.Seeding, "installed fixture seed ready");
        var manifest = Path.Combine(root, "server.json");
        await File.WriteAllTextAsync(manifest, System.Text.Json.JsonSerializer.Serialize(new { Direct = http.Url + "test.avi", Magnet = "magnet:?xt=urn:btih:" + source.InfoHashes.V1!.ToHex() + "&tr=" + Uri.EscapeDataString(http.Url + "announce"), Torrent = torrentPath, Process = Environment.ProcessId }));
        var stop = Path.Combine(root, "stop"); if (File.Exists(stop)) File.Delete(stop);
        var until = DateTime.UtcNow.AddMinutes(15);
        while (!File.Exists(stop) && DateTime.UtcNow < until) await Task.Delay(500);
        await seed.StopAsync();
    }

    public static async Task RuntimeDownloadAsync()
    {
        var started = DateTime.UtcNow;
        var path = await PlayerRuntime.EnsureAsync(null, CancellationToken.None, destination: Path.GetFullPath(".tools/player-runtime-network"));
        Check(File.Exists(Path.Combine(path, "libvlc.dll")) && File.Exists(Path.Combine(path, "manifest.json")), "real optional runtime HTTP download, pinned hash and atomic extraction");
        Console.WriteLine("Runtime preparation seconds=" + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0"));
    }

    private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }

    // Original uncompressed AVI with moving colour bars, generated only for deterministic playback tests.
    private static void CreateAvi(string path)
    {
        const int width = 160, height = 90, frames = 300, bytes = width * height * 3, audioBytes = 3200;
        using var output = File.Create(path); using var w = new BinaryWriter(output);
        void Tag(string text) => w.Write(Encoding.ASCII.GetBytes(text));
        long Begin(string tag) { Tag(tag); var at = output.Position; w.Write(0); return at; }
        void End(long at) { var end = output.Position; output.Position = at; w.Write((int)(end - at - 4)); output.Position = end; }
        var riff = Begin("RIFF"); Tag("AVI "); var hdrl = Begin("LIST"); Tag("hdrl");
        Tag("avih"); w.Write(56); foreach (var n in new[] { 100000, (bytes + audioBytes) * 10, 0, 16, frames, 0, 2, bytes, width, height, 0, 0, 0, 0 }) w.Write(n);
        var strl = Begin("LIST"); Tag("strl"); Tag("strh"); w.Write(56); Tag("vids"); Tag("DIB ");
        w.Write(0); w.Write((short)0); w.Write((short)0); foreach (var n in new[] { 0, 1, 10, 0, frames, bytes, -1, 0 }) w.Write(n);
        w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
        Tag("strf"); w.Write(40); w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24); foreach (var n in new[] { 0, bytes, 0, 0, 0, 0 }) w.Write(n);
        End(strl);
        var audio = Begin("LIST"); Tag("strl"); Tag("strh"); w.Write(56); Tag("auds"); w.Write(0);
        w.Write(0); w.Write((short)0); w.Write((short)0); foreach (var n in new[] { 0, 1, 16000, 0, frames * 1600, audioBytes, -1, 2 }) w.Write(n);
        for (var i = 0; i < 4; i++) w.Write((short)0);
        Tag("strf"); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        End(audio); End(hdrl); var movi = Begin("LIST"); Tag("movi"); var data = new byte[bytes];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var i = 0; i < bytes; i += 3) { var stripe = ((i / 3 % width + frame) / 20) % 2 == 0; data[i] = stripe ? (byte)180 : (byte)90; data[i + 1] = stripe ? (byte)170 : (byte)40; data[i + 2] = stripe ? (byte)40 : (byte)220; }
            Tag("00db"); w.Write(bytes); w.Write(data);
            Tag("01wb"); w.Write(audioBytes);
            for (var sample = 0; sample < 1600; sample++) w.Write((short)(Math.Sin((frame * 1600 + sample) * 2 * Math.PI * 330 / 16000) * 1000));
        }
        End(movi); Tag("idx1"); w.Write(frames * 32);
        for (var i = 0; i < frames; i++) {
            var offset = 4 + i * (bytes + audioBytes + 16);
            Tag("00db"); w.Write(16); w.Write(offset); w.Write(bytes);
            Tag("01wb"); w.Write(16); w.Write(offset + bytes + 8); w.Write(audioBytes);
        }
        End(riff);
    }

    internal sealed class VideoServer : IDisposable
    {
        private readonly HttpListener listener = new(); private readonly string file;
        public string Url { get; } = "http://127.0.0.1:" + FreePort() + "/";
        public int RangeRequests;
        public int SeedPort;
        public VideoServer(string file) { this.file = file; listener.Prefixes.Add(Url); listener.Start(); _ = Run(); }
        private async Task Run()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { break; }
                _ = Serve(context);
            }
        }
        private async Task Serve(HttpListenerContext c)
        {
            try
            {
                if (c.Request.RawUrl!.StartsWith("/announce")) {
                    var peer = new byte[] { 127, 0, 0, 1, (byte)(SeedPort >> 8), (byte)SeedPort };
                    var tracker = new MonoTorrent.BEncoding.BEncodedDictionary {
                        ["interval"] = new MonoTorrent.BEncoding.BEncodedNumber(5), ["complete"] = new MonoTorrent.BEncoding.BEncodedNumber(1),
                        ["incomplete"] = new MonoTorrent.BEncoding.BEncodedNumber(0), ["peers"] = new MonoTorrent.BEncoding.BEncodedString(peer) };
                    var body = tracker.Encode(); c.Response.ContentLength64 = body.Length; await c.Response.OutputStream.WriteAsync(body); return;
                }
                if (c.Request.RawUrl!.Contains("page")) { c.Response.ContentType = "text/html"; c.Response.Close(); return; }
                var length = new FileInfo(file).Length; long start = 0, end = length - 1;
                if (c.Request.Headers["Range"] is {} range)
                {
                    Interlocked.Increment(ref RangeRequests); var parts = range[6..].Split('-'); start = long.Parse(parts[0]); if (parts.Length > 1 && parts[1].Length > 0) end = long.Parse(parts[1]);
                    c.Response.StatusCode = 206; c.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{length}";
                }
                c.Response.ContentType = "video/x-msvideo"; c.Response.Headers["Accept-Ranges"] = "bytes"; c.Response.ContentLength64 = end - start + 1;
                await using var stream = File.OpenRead(file); stream.Position = start; var buffer = new byte[32768];
                while (stream.Position <= end) { var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, end - stream.Position + 1))); if (count == 0) break; await c.Response.OutputStream.WriteAsync(buffer.AsMemory(0, count)); }
            }
            catch (Exception e) when (e is IOException or HttpListenerException or ObjectDisposedException) { }
            finally { c.Response.Close(); }
        }
        public void Dispose() => listener.Close();
    }
}
