using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class Program
{
    private static int passed;
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string Magnet = "magnet:?xt=urn:btih:" + Hash;
    private static readonly string Rss = $"<rss xmlns:t='http://torznab.com/schemas/2015/feed'><channel><item><title>Ubuntu fixture</title><size>1024</size><t:attr name='magneturl' value='{Magnet}'/><t:attr name='seeders' value='9'/><t:attr name='infohash' value='{Hash}'/></item></channel></rss>";
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Contains("--comparison-local-segment")){ComparisonChecks.LocalSegment().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--channel-range-check")){ChannelComparisonChecks.RangeChecks().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--channel-comparison")){ChannelComparisonChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--routing-cache")){RoutingCacheChecks.Run();return 0;}
            if(args.Contains("--comparison-reuse")){ComparisonChecks.Reuse().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--comparison")){ComparisonChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--visible-magnet"))return VisibleMagnetChecks.Run();
            if(args.Contains("--comparison-local")){ComparisonChecks.Local().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--player-click"))return PlayerClickChecks.Run();
            if(args.Contains("--source-menu"))return SourceMenuChecks.Run();
            if(args.Contains("--player-drag"))return PlayerDragChecks.Run();
            if(args.Contains("--search-responsiveness")){SearchResponsivenessChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--search-live")){SearchResponsivenessChecks.Live().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--resource-groups")){ResourceGroupChecks.Run();return 0;}
            if(args.Contains("--resource-display")){ResourceDisplayChecks.Run();return 0;}
            if(args.Contains("--ui-motion"))return UiMotionChecks.Run();
            if(args.Contains("--native-handoff")){NativePlaybackChecks.HandoffAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--native-playback")){NativePlaybackChecks.RunAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--native-mkv")){NativePlaybackChecks.MkvAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--public-playback-soak")){SpiderManLiveChecks.ControlAsync(true).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--public-discovery")){PublicDiscoveryChecks.RunAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--tracker-playback")){SpiderManLiveChecks.TrackerPlaybackAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--tracker-playback-library")){SpiderManLiveChecks.TrackerPlaybackAsync(true).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--tracker-download")){SpiderManLiveChecks.DownloadControlAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--spiderman-live")){SpiderManLiveChecks.RunAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--spiderman-english")){SpiderManLiveChecks.RunAsync("Spider-Man").GetAwaiter().GetResult();return 0;}
            if(args.Contains("--public-playback-control")){SpiderManLiveChecks.ControlAsync().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--playback")) return PlaybackChecks.Run();
            if(args.Contains("--playback-runtime")) { PlaybackChecks.RuntimeDownloadAsync().GetAwaiter().GetResult(); return 0; }
            if(args.Contains("--serve-player-fixture")) { PlaybackChecks.ServeAsync().GetAwaiter().GetResult(); return 0; }
            if(args.Contains("--browser")||args.Contains("--browser-live")){BrowserDiscoveryChecks.Run(args.Contains("--browser-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--dialog-glass"))return DialogGlassChecks.Run();
            if(args.Contains("--discovery-deep")||args.Contains("--deep-live")){DiscoveryDeepChecks.Run(args.Contains("--deep-live")).GetAwaiter().GetResult();return 0;}
            if(args.Length>=2&&args[0]=="--probe-site"){
                using var probe=new WebsiteDiscovery();var result=probe.DiscoverAsync(args[1],args.Length>2?args[2]:"",new Progress<string>(Console.WriteLine),CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new{result.Message,Added=result.Source is not null,Query=result.Source?.DiscoveryQuery,Count=result.Preview.Count,Titles=result.Preview.Select(r=>r.Title),Template=result.Source?.DiscoveryTemplate}));return result.Source is null?2:0;
            }
            if(args.Contains("--discovery")||args.Contains("--discovery-live")){DiscoveryChecks.Run(args.Contains("--discovery-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--discovery-ui"))return DiscoveryChecks.Ui();
            if(args.Contains("--engine-ui"))return EmbeddedEngineUiChecks.Run();
            if(args.Contains("--engine-live")){EmbeddedEngineChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--jackett")){return JackettChecks.Run();}
            if(args.Contains("--foss")||args.Contains("--foss-live")){FossChecks.Run(args.Contains("--foss-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--piratebay")||args.Contains("--piratebay-live")){PirateBayChecks.Run(args.Contains("--piratebay-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--media")||args.Contains("--media-live")){MediaChecks.Run(args.Contains("--media-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--reliable")||args.Contains("--reliable-live")){ReliableChecks.Run(args.Contains("--reliable-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--baseline-live")){LiveBaseline.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--baseline-review")){LiveBaseline.Review();return 0;}
            if(args.Contains("--integrity")){IntegrityChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--handoff-live")){HandoffLiveChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--workbench"))return WorkbenchChecks.Run();
            if(args.Contains("--quality")){QualityChecks.Run().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--expanded")||args.Contains("--expanded-live")){ExpandedChecks.Run(args.Contains("--expanded-live")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--presentation-live")){PresentationLive().GetAwaiter().GetResult();return 0;}
            if(args.Contains("--tray"))return TrayCheck();
            if(args.Contains("--enable-archive")){EnableArchive().GetAwaiter().GetResult();return 0;}
            if (args.Contains("--ui")) return Ui(args.Contains("--live"));
            Checks().GetAwaiter().GetResult();
            if (args.Contains("--global") || args.Contains("--global-live")) GlobalChecks().GetAwaiter().GetResult();
            if (args.Contains("--global-live")) GlobalLive().GetAwaiter().GetResult();
            if (args.Contains("--cili-live")) CiliLive().GetAwaiter().GetResult();
            if (args.Contains("--sites")) Sites().GetAwaiter().GetResult();
            if (args.Contains("--thumbs")) Thumbnails().GetAwaiter().GetResult();
            if (args.Contains("--live")) Live().GetAwaiter().GetResult();
            Console.WriteLine($"PASSED {passed}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAILED {ex}"); return 1; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is SourceException or System.Xml.XmlException) { Check(true, name); return; } throw new InvalidOperationException(name); }
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    private static HttpResponseMessage Response(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text) };
    private static SourceConfig Source(string name) => new() { Name = name, Endpoint = "http://localhost/" + name, ApiKey = "test-not-a-real-secret" };
    private static async Task Checks()
    {
        var brief = new SearchResult { Title="Troy", Source="Brief", Hash=Hash, Size=10, CoverUrl="https://example.com/wrong.jpg" };
        var complete = new SearchResult { Title="Troy.2004.1080p.BluRay", Source="Complete", Hash=Hash, Size=20, DetailsUrl="https://example.com/complete" };
        var forward=ResultMerger.Merge([brief,complete]).Single();
        var backward=ResultMerger.Merge([complete,brief]).Single();
        Check(JsonSerializer.Serialize(forward)==JsonSerializer.Serialize(backward),"merge independent of source arrival order");
        Check(forward.Title==complete.Title && forward.Size==20 && forward.DetailsUrl==complete.DetailsUrl,"complete record fields stay together");
        Check(forward.CoverUrl is null,"do not borrow conflicting source artwork");
        PresentationChecks();
        Check(Links.MagnetHash(Magnet) == "btih:" + Hash, "valid v1 magnet");
        Check(Links.NormalizeHash(new string('A', 32)) == "btih:" + new string('0', 40), "base32 canonicalization");
        Check(Links.MagnetHash("magnet:?xt=urn:btmh:1220" + new string('a', 64)) == "btmh:1220" + new string('a', 64), "v2 magnet");
        Check(Links.MagnetHash("file:///C:/Windows/notepad.exe") is null, "reject arbitrary scheme");
        Check(Links.MagnetHash(Magnet + "\r\n") is null, "reject control characters");
        Reject(() => Links.Endpoint("http://example.com/api"), "remote API requires TLS");
        Reject(() => Links.Endpoint("https://example.com/api?apikey=secret"), "reject inline credentials");
        Reject(() => Links.WebUri("https://user:password@example.com"), "reject URL user info");
        var rows = SearchService.ParseTorznab(Utf8(Rss), "A");
        Check(rows.Count == 1 && rows[0].Seeders == 9 && rows[0].Size == 1024, "Torznab namespaced fields");
        var unknown = SearchService.ParseTorznab(Utf8($"<rss><channel><item><title>x</title><link>{Magnet}</link></item></channel></rss>"), "A")[0];
        Check(unknown.Seeders is null && unknown.SizeText == "未知", "unknown fields stay unknown");
        Check(SearchService.ParseTorznab(Utf8("<rss><channel><item><title>x</title><link>https://example.org/details</link></item></channel></rss>"), "A").Count == 0, "details page is not torrent");
        Reject(() => SearchService.ParseTorznab(Utf8("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><rss>&x;</rss>"), "A"), "reject XML external entity");
        Reject(() => SearchService.ParseTorznab(Utf8("<error code='100' description='secret_should_not_escape'/>"), "A"), "API error recognized");
        var merged = ResultMerger.Merge([rows[0], rows[0] with { Source = "B" }, rows[0] with { Magnet = null, Hash = null, Source = "C" }]);
        Check(merged.Count == 2 && merged[0].Source == "A · B", "dedup hash only, preserve no-hash same title");
        var different = ResultMerger.Merge([rows[0], rows[0] with { Hash = new string('a', 40), Magnet = null }]);
        Check(different.Count == 2, "same title different hash preserved");
        var torrent = Utf8("d4:infod6:lengthi0e4:name5:empty12:piece lengthi16384e6:pieces0:ee");
        Check(TorrentFile.Validate(torrent).Size == 0, "valid torrent metadata");
        Reject(() => TorrentFile.Validate(Utf8("<html>Not a torrent</html>")), "reject downloaded HTML");
        Reject(() => TorrentFile.Validate(Utf8("d4:infod6:lengthi0e4:name2:..12:piece lengthi16384e6:pieces0:ee")), "reject traversal filename");
        Reject(() => TorrentFile.Validate(Utf8("d4:infod6:lengthi5e4:name5:empty12:piece lengthi16384e6:pieces0:ee")), "reject piece count mismatch");
        Reject(() => TorrentFile.Validate(Utf8("d1:ai1e1:ai2ee")), "reject duplicate dictionary keys");
        var folder = Path.Combine(Path.GetTempPath(), "afmc-find-checks-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(folder); store.Save([Source("saved")]);
        Check(!File.ReadAllText(Path.Combine(folder, "sources.json")).Contains("test-not-a-real-secret") && store.Load()[0].ApiKey == "test-not-a-real-secret", "DPAPI encrypted disk and reload");
        store.Save([Source("updated")]); Check(File.Exists(Path.Combine(folder, "sources.json.bak")) && store.Load()[0].Name == "updated", "atomic save with backup");
        var reports = new List<SourceReport>();
        using var service = new SearchService(new Handler((r, _) => Task.FromResult(r.RequestUri!.AbsolutePath == "/good" ? Response(Rss) : Response("", HttpStatusCode.Unauthorized))));
        var summary = await service.SearchAsync("ubuntu", [Source("good"), Source("bad")], r => { lock (reports) reports.Add(r); }, CancellationToken.None);
        Check(summary.Succeeded == 1 && summary.Failed == 1 && reports.Sum(r => r.Results.Count) == 1, "partial source failure preserves success");
        Check(reports.Single(r => r.Error is not null).Error!.Contains("认证失败"), "authentication failure classification");
        using var timeoutService = new SearchService(new Handler((_, _) => throw new TaskCanceledException()));
        var timed = new List<SourceReport>(); await timeoutService.SearchAsync("test", [Source("timeout")], timed.Add, CancellationToken.None);
        Check(timed.Single().Error!.Contains("超时"), "timeout classification");
        using var cts = new CancellationTokenSource();
        using var waiting = new SearchService(new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Response(Rss); }));
        var count = 0; var task = waiting.SearchAsync("test", [Source("slow")], _ => count++, cts.Token); cts.Cancel();
        try { await task; throw new InvalidOperationException("Cancellation ignored"); } catch (OperationCanceledException) { Check(count == 0, "cancel emits no late result"); }
        using var caps = new SearchService(new Handler((_, _) => Task.FromResult(Response("<caps><searching><search available='yes' supportedParams='q'/></searching></caps>"))));
        Check((await caps.TestSourceAsync(Source("caps"), CancellationToken.None)).Contains("成功"), "Torznab capabilities");
        using var oversized = new SearchService(new Handler((_, _) => Task.FromResult(Response(new string('x', 20)))));
        try { await oversized.GetBytesAsync(new Uri("https://example.test"), 10, CancellationToken.None); throw new InvalidOperationException("Unbounded response"); } catch (SourceException) { Check(true, "bounded network response"); }
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new Uri("https://example.test/other");
        using var redirectService = new SearchService(new Handler((_, _) => Task.FromResult(redirect)));
        try { await redirectService.GetBytesAsync(new Uri("https://example.test/api?apikey=test"), 10, CancellationToken.None); throw new InvalidOperationException("Redirect leaked"); } catch (SourceException) { Check(true, "API redirects refused"); }
        // Only remove files individually created by this test, not a recursive computed directory.
        foreach (var file in new[] { "sources.json", "sources.json.bak" }) File.Delete(Path.Combine(folder, file)); Directory.Delete(folder);
    }
    private static void PresentationChecks()
    {
        var now=new DateTimeOffset(2026,11,4,0,0,0,TimeSpan.Zero);
        var good=new SearchResult {Title="Troy 2004 1080p",Source="fixture",Seeders=2,Published=now.AddYears(-5),Category="电影"};
        double Score(SearchResult r)=>ResultPresentation.Score("Troy 2004 1080p",r,now);
        Check(Score(good)>Score(good with {Title="Troy 2024 1080p",Seeders=1000000,Published=now}),"composite rejects popular wrong year");
        Check(Score(good)>Score(good with {Title="Troy 2004 720p",Seeders=1000000}),"composite respects resolution");
        Check(Score(good)>Score(good with {Title="Other movie",Seeders=long.MaxValue,Published=now}),"composite relevance outranks popularity");
        Check(Score(good with {Seeders=100})>Score(good),"composite rewards seed availability");
        Check(Score(good with {Published=now})>Score(good),"composite freshness tie breaker");
        Check(Score(good with {Published=now.AddDays(3)})==Score(good with {Published=null}),"future dates receive no freshness boost");
        Check(ResultPresentation.SafeCover("https://localhost/poster.png") is null && ResultPresentation.SafeCover("https://image.tmdb.org.evil.test/a") is null && ResultPresentation.SafeCover("https://u:p@image.tmdb.org/a") is null,"image host and credentials validation");
        var poster=good with {Title="特洛伊 · Troy.Directors.Cut.2004.BluRay",CoverUrl="https://i.nacloud.cc/poster.jpg"};
        var filled=ResultPresentation.WithCovers(new[]{poster,good,good with {Title="特洛伊 2004 1080p"},good with {Title="特洛伊.Troy.2004.2160p"}});
        Check(filled.Skip(1).All(r=>r.CoverInferred&&r.CoverUrl==poster.CoverUrl),"same title year shares poster with provenance");
        var guarded=ResultPresentation.WithCovers(new[]{poster,good with {Title="Troy 2024 1080p"},good with {Title="Troy 1080p"},good with {Title="Troy S01 2004 1080p"}});
        Check(guarded.Skip(1).All(r=>r.CoverUrl is null),"poster does not cross years missing years or seasons");
        var conflict=ResultPresentation.WithCovers(new[]{poster,poster with {CoverUrl="https://i.nacloud.cc/other.jpg"},good});
        Check(conflict.Last().CoverUrl is null,"ambiguous posters are not propagated");
        var rss=Rss.Replace("</item>","<t:attr name='coverurl' value='https://image.tmdb.org/t/p/w500/a.jpg'/></item>");
        Check(SearchService.ParseTorznab(Utf8(rss),"fixture")[0].CoverUrl is not null,"Torznab source poster metadata");
    }
    private static async Task Live()
    {
        using var service = new SearchService(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var evidence = new List<object>(); SearchResult? selected = null;
        foreach (var query in new[] { "ubuntu", "Big Buck Bunny", "中文" })
        {
            SourceReport? report = null;
            await service.SearchAsync(query, [SourceConfig.Archive()], r => report = r, timeout.Token);
            Check(report is { Error: null }, "LIVE API query " + query);
            if (query != "中文") Check(report!.Results.Count > 0, "LIVE nonempty " + query);
            if (query == "Big Buck Bunny") selected = report!.Results.FirstOrDefault();
            evidence.Add(new { query, count = report!.Results.Count, first = report.Results.FirstOrDefault()?.Title, error = report.Error });
        }
        if (selected is null) throw new InvalidOperationException("No live torrent candidate");
        var url = await service.ResolveTorrentAsync(selected, timeout.Token);
        var bytes = await service.GetBytesAsync(Links.WebUri(url), TorrentFile.MaxBytes, timeout.Token, true);
        var torrent = TorrentFile.Validate(bytes); Check(torrent.Hash.Length == 40, "LIVE torrent fetched and validated");
        evidence.Add(new { torrentName = torrent.Name, torrentBytes = bytes.Length, torrentHash = torrent.Hash, contentSize = torrent.Size, externalSoftwareInvoked = false });
        Directory.CreateDirectory("docs/evidence/development"); File.WriteAllText("docs/evidence/development/live-search.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(evidence));
    }
    private static async Task Thumbnails()
    {
        var matching=new SearchResult {Title="Ubuntu 24.04 Desktop",Source="x"};
        var wrong=new SearchResult {Title="Ubuntu 22.04 Desktop",Source="x",Magnet=Magnet,Seeders=10000,Size=100};
        Check(SiteSources.Rank("Ubuntu 24.04",matching)>SiteSources.Rank("Ubuntu 24.04",wrong),"explicit version outranks popularity and metadata");
        var partial=new List<SourceReport>();
        var firstPage="<rss><channel>"+string.Concat(Enumerable.Range(0,100).Select(i=>$"<item><title>item {i}</title><link>magnet:?xt=urn:btih:{i:x40}</link></item>"))+"</channel></rss>";
        using(var mock=new SearchService(new Handler((request,_)=>Task.FromResult(request.RequestUri!.Query.Contains("offset=100")?Response("",HttpStatusCode.ServiceUnavailable):Response(firstPage)))))
        {
            await mock.SearchAsync("ubuntu",[Source("paged")],partial.Add,CancellationToken.None);
            Check(partial.Single().Results.Count==100&&partial[0].Error!.Contains("已保留"),"later page error retains earlier results");
        }
        using var service=new SearchService();var reports=new List<SourceReport>();
        await service.SearchAsync("ubuntu",SiteSources.Catalog.Where(s=>s.Id.StartsWith("site-")).Append(SourceConfig.Archive()),r=>{lock(reports)reports.Add(r);},CancellationToken.None);
        Check(reports.Count==3&&reports.All(r=>r.Error is null),"LIVE all three paged sources");
        Check(reports.Single(r=>r.Name=="TPB Proxy").Results.Count>30,"LIVE TPB adds unique pages");
        Check(reports.Single(r=>r.Name=="种子楼").Results.Count>15,"LIVE Zhongzilou adds unique pages");
        Check(reports.Single(r=>r.Name=="Internet Archive").Results.Count>50,"LIVE Archive adds unique pages");
        var image=await ThumbnailView.Load("https://archive.org/services/img/big-buck-bunny-2008-4k-60fps");
        Check(image is {PixelWidth:>0},"LIVE source thumbnail downloaded and decoded");
        Check(await ThumbnailView.Load("file:///C:/Windows/win.ini") is null,"thumbnail rejects local file scheme");
        Check(await ThumbnailView.Load("https://example.com/image.jpg") is null,"thumbnail rejects unapproved host");
        File.WriteAllText("docs/evidence/v03/paging.json",JsonSerializer.Serialize(reports.Select(r=>new{r.Name,Count=r.Results.Count,r.Error}),new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(reports.Select(r=>new{r.Name,Count=r.Results.Count})));
    }
    private static async Task Sites()
    {
        Check(SiteSources.Relevance("ubuntu 24.04", "Ubuntu 24.04 Desktop amd64") > SiteSources.Relevance("ubuntu 24.04", "Ubuntu 22.04 tutorial"), "version matching ranks relevant release first");
        Check(SiteSources.Relevance("三体", "三体") > SiteSources.Relevance("三体", "三体解说幕后访谈"), "exact Chinese title ranks first");
        Check(SiteSources.ParseSize("6.07 GiB") > 6000000000, "parse binary sizes");
        Check(SiteSources.Rank("ubuntu",new SearchResult {Title="ubuntu-26.04-desktop-amd64.iso",Source="TPB",Magnet=Magnet,Size=1000,Seeders=10}) > SiteSources.Rank("ubuntu",new SearchResult{Title="ubuntu",Source="Archive"}),"resource metadata outranks bare generic archive title");
        Check(SiteSources.Catalog.Count(s=>s.Id.StartsWith("site-")) == 6 && SiteSources.Catalog.Count(s=>s.Id.StartsWith("site-")&&s.Kind=="Website")==4, "six requested sites with honest capabilities");
        using var service=new SearchService(); using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var reports=new List<SourceReport>();
        await service.SearchAsync("ubuntu", SiteSources.Catalog.Where(s=>s.Id.StartsWith("site-")), r=>{lock(reports) reports.Add(r);}, timeout.Token);
        foreach(var report in reports) Check(report.Error is null && report.Results.Count>0,"LIVE site "+report.Name+" "+report.Results.Count+" "+report.Error);
        Check(reports.Count==2,"navigation sites excluded from search success count");
        var zz=reports.Single(r=>r.Name=="种子楼").Results.First();
        var magnet=await service.VerifySiteMagnetAsync(zz,timeout.Token);
        Check(Links.MagnetHash(magnet)==Links.MagnetHash(zz.Magnet),"LIVE detail hash verified");
        File.WriteAllText("docs/evidence/source-audit/integration.json",JsonSerializer.Serialize(reports.Select(r=>new {r.Name,Count=r.Results.Count,r.Error,First=r.Results.FirstOrDefault()?.Title}),new JsonSerializerOptions {WriteIndented=true}));
    }
    private static async Task GlobalChecks()
    {
        var bit=GlobalSources.Parse("BitSearch",Utf8("{\"success\":true,\"results\":[{\"id\":\"one\",\"title\":\"Ubuntu\",\"infohash\":\""+Hash+"\",\"size\":1234,\"seeders\":5,\"category\":5}]}"));
        Check(bit.Count==1&&Links.MagnetHash(bit[0].Magnet)=="btih:"+Hash&&bit[0].Size==1234&&bit[0].Category=="软件","BitSearch validated hash and metadata");
        Check(bit[0].Published is null,"BitSearch missing publication date is not update date");
        var knaben=GlobalSources.Parse("Knaben",Utf8("{\"hits\":[{\"title\":\"Ubuntu\",\"hash\":\""+Hash+"\",\"tracker\":\"1337x\",\"details\":\"file:///bad\"}]}"));
        Check(knaben.Count==1&&knaben[0].Source=="Knaben（1337x）"&&knaben[0].DetailsUrl is null,"Knaben provenance and unsafe link rejected");
        Reject(()=>GlobalSources.Parse("Knaben",Utf8("{\"hits\":[{\"title\":\"wrong\",\"hash\":\""+Hash+"\",\"magnetUrl\":\"magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}")),"reject conflicting infohash");
        Reject(()=>GlobalSources.Parse("BitSearch",Utf8("{\"success\":false,\"results\":[]}")),"API error is not empty success");
        var xml="<rss xmlns:n='https://nyaa.si/xmlns/nyaa'><channel><item><title>Fixture</title><guid>https://nyaa.si/view/1</guid><n:infoHash>"+Hash+"</n:infoHash><n:size>2 MiB</n:size><n:seeders>9</n:seeders></item></channel></rss>";
        var nyaa=GlobalSources.Parse("Nyaa",Utf8(xml));
        Check(nyaa.Count==1&&nyaa[0].Size==2097152&&nyaa[0].Seeders==9,"Nyaa namespaced RSS");
        Check(ResultMerger.Merge(bit.Concat(knaben)).Count==1,"global duplicate hashes merge across indexers");
        Check(GlobalSources.QueryUri("Knaben","a&b 中文",2).Query.Contains("f=100")&&GlobalSources.QueryUri("BitSearch","a&b",3).Query.Contains("q=a%26b"),"encoded queries and bounded pagination");
        var calls=0;
        using var service=new SearchService(new Handler((request,ct)=>{calls++;return Task.FromResult(Response(xml));}));
        var config=new SourceConfig {Name="Nyaa",Kind="Nyaa"};
        await service.SearchAsync("fixture",[config],_=>{},CancellationToken.None);
        await service.SearchAsync("fixture",[config],_=>{},CancellationToken.None);
        Check(calls==1,"Nyaa one RSS request and repeat-query cache");
        var catalog=SiteSources.IncludeCatalog(SiteSources.IncludeCatalog([SourceConfig.Archive()]));
        Check(catalog.Count==SiteSources.Catalog.Count+1&&catalog.Select(s=>s.Id).Distinct().Count()==catalog.Count&&SiteSources.Catalog.All(s=>catalog.Any(c=>c.Id==s.Id)),"catalog merges without duplicate entries");
        var detail="<h1>影片</h1><img class='rounded-2' src='https://i.nacloud.cc/2025/test.jpg'/><div class='mv_down'><a href='"+Magnet+"'>Release[2.8G]</a></div><a href='magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'>advertisement</a>";
        var films=CiliXiong.ParseDetail(detail,new Uri("https://www.cilixiong.cc/movie/1.html"));
        Check(films.Count==1&&films[0].CoverUrl!.Contains("nacloud")&&films[0].Size>2800000000,"CiliXiong release metadata and article-only magnets");
        Check(CiliXiong.SearchLinks("<a href='/movie/1.html'>one</a><a href='https://other.example/movie/2.html'>ad</a><a href='/movie/1.html'>dup</a>").Count==1,"CiliXiong bounded same-site distinct details");
        Check(CiliXiong.SearchLinks("<div>没有搜索到相关的内容</div>").Count==0,"CiliXiong recognizes actual no-result page");
        Reject(()=>CiliXiong.SearchLinks("系统限制的搜索时间间隔为 5 秒"),"CiliXiong rate limit is not empty success");
        Check(ResultMerger.Merge(bit.Concat(films)).Single().CoverUrl is not null,"duplicate hash preserves available cover");
        using var partialService=new SearchService(new Handler((request,ct)=>
        {
            if(request.Method==HttpMethod.Post){var redirect=Response("",HttpStatusCode.Found);redirect.Headers.Location=new Uri("/e/search/result/?searchid=1",UriKind.Relative);return Task.FromResult(redirect);}
            if(request.RequestUri!.AbsolutePath.Contains("/result/"))return Task.FromResult(Response("<a href='/movie/1.html'>one</a><a href='/movie/2.html'>two</a>"));
            return Task.FromResult(request.RequestUri.AbsolutePath.Contains("/1.html")?Response(detail):Response("",HttpStatusCode.ServiceUnavailable));
        }));
        var partialReports=new List<SourceReport>();
        await partialService.SearchAsync("影片",[new SourceConfig {Kind="CiliXiong"}],partialReports.Add,CancellationToken.None);
        Check(partialReports.Single().Results.Count==1&&partialReports.Single().Error is not null,"CiliXiong detail failure retains completed results and reports error");
    }
    private static async Task CiliLive()
    {
        using var service=new SearchService(new CiliCaptureHandler());
        var reports=new List<SourceReport>();
        var config=SiteSources.Catalog.Single(s=>s.Kind=="CiliXiong");
        await service.SearchAsync("特洛伊",[config],reports.Add,CancellationToken.None);
        File.WriteAllText("docs/evidence/global-sources/cili-live.json",JsonSerializer.Serialize(reports.Select(r=>new{r.Name,Count=r.Results.Count,r.Error}),new JsonSerializerOptions{WriteIndented=true}));
        var report=reports.Single();
        Check(report.Error is null&&report.Results.Count>0&&report.Results.All(r=>Links.MagnetHash(r.Magnet)!=null),"LIVE CiliXiong magnets "+report.Results.Count+" "+report.Error);
        var poster=report.Results.First().ThumbnailUrl!;
        Check(await ThumbnailView.Load(poster) is {PixelWidth:>0},"LIVE CiliXiong source poster decoded");
        reports.Clear();
        await service.SearchAsync("afmc-no-result-9394827",[config],reports.Add,CancellationToken.None);
        Check(reports.Single().Error is null&&reports.Single().Results.Count==0,"LIVE CiliXiong empty search is valid "+reports.Single().Error);
    }
    private static async Task PresentationLive()
    {
        using var service=new SearchService();
        var reports=new System.Collections.Concurrent.ConcurrentBag<SourceReport>();
        var sources=new SettingsStore().Load().Where(s=>s.Enabled && s.Kind!="Website").ToArray();
        var summary=await service.SearchAsync("特洛伊",sources,reports.Add,CancellationToken.None);
        var merged=ResultMerger.Merge(reports.SelectMany(r=>r.Results));
        var filled=ResultPresentation.WithCovers(merged);
        var evidence=new{query="特洛伊",summary.Succeeded,summary.Failed,total=filled.Count,before=merged.Count(r=>r.ThumbnailUrl!=null),after=filled.Count(r=>r.ThumbnailUrl!=null),inferred=filled.Where(r=>r.CoverInferred).Select(r=>new{r.Title,r.CoverSource}).ToArray(),top=filled.OrderByDescending(r=>ResultPresentation.Score("特洛伊",r,DateTimeOffset.UtcNow)).Take(5).Select(r=>new{r.Title,r.Seeders,r.Published}).ToArray()};
        Directory.CreateDirectory("docs/evidence/v060");
        File.WriteAllText("docs/evidence/v060/presentation-live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
        Check(summary.Succeeded>0&&filled.Count>0,"LIVE presentation results");
        Console.WriteLine($"Covers before={evidence.before}, after={evidence.after}, inferred={evidence.inferred.Length}");
    }
    private static int TrayCheck()
    {
        var app=new Application {Resources=new ResourceDictionary {Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var directory=Path.Combine(Path.GetTempPath(),"afmc-tray-"+Guid.NewGuid().ToString("N"));
        var store=new SettingsStore(directory);store.Save([Source("fixture")]);
        var prefs=new Preferences{CloseToTray=true,MinimizeToTray=true};prefs.Save(directory);
        Check(Preferences.Load(directory)==prefs,"preferences persistence");
        (prefs with {CloseToTray=false}).Save(directory);Check(File.Exists(Path.Combine(directory,"preferences.json.bak")),"preferences backup");
        prefs.Save(directory);
        var window=new MainWindow(store,new SearchService(),true);var exit=1;var started=false;
        window.Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            try
            {
                var trayDeadline=DateTime.UtcNow.AddSeconds(5);
                while(!window.HasTrayIcon&&DateTime.UtcNow<trayDeadline)await Task.Delay(50);
                var settingsWindow=new PreferencesWindow(prefs){Owner=window};settingsWindow.Show();
                Check(window.HasTrayIcon,"real tray icon initialized");
                settingsWindow.UpdateLayout();
                Check(settingsWindow.FindName("DefaultSortBox") is null,"settings has no sorting preference");
                var preview=new RenderTargetBitmap((int)settingsWindow.ActualWidth,(int)settingsWindow.ActualHeight,96,96,PixelFormats.Pbgra32);preview.Render(settingsWindow);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(preview));
                Directory.CreateDirectory("docs/evidence/v0810");using(var image=File.Create("docs/evidence/v0810/settings-render.png"))png.Save(image);
                Check(((CheckBox)settingsWindow.FindName("CloseToTrayBox")).IsChecked==true,"settings reflects close behavior");settingsWindow.Close();
                window.Close();await Task.Delay(150);Check(!window.IsVisible,"close hides to real notification icon");
                window.RestoreFromTray();Check(window.IsVisible,"tray restore shows window");
                window.WindowState=WindowState.Minimized;await Task.Delay(150);Check(!window.IsVisible,"minimize to tray preference");
                window.RestoreFromTray();Check(window.WindowState==WindowState.Normal&&window.IsVisible,"tray restore returns normal state");
                exit=0;window.ExitApplication();Check(!window.IsVisible,"explicit exit closes window");
            }
            catch(Exception ex){Console.Error.WriteLine(ex);exit=1;window.ExitApplication();}
            finally{app.Shutdown(exit);}
        };
        app.Run(window);return exit;
    }
    private static async Task EnableArchive()
    {
        using var service=new SearchService();var reports=new System.Collections.Concurrent.ConcurrentBag<SourceReport>();
        var source=SourceConfig.Archive();var summary=await service.SearchAsync("ubuntu",[source],reports.Add,CancellationToken.None);
        Check(summary.Succeeded==1&&reports.Sum(r=>r.Results.Count)>0,"Archive real search before enabling");
        var settings=new SettingsStore();var sources=settings.Load();
        var next=sources.Select(s=>s.Id==source.Id?s with {Enabled=true}:s).ToList();
        if(!next.Any(s=>s.Id==source.Id))next.Add(source);
        settings.Save(next);
        File.WriteAllText("docs/evidence/v060/archive-restored.json",JsonSerializer.Serialize(new{query="ubuntu",count=reports.Sum(r=>r.Results.Count),enabled=true,backup=true}));
    }
    private static async Task GlobalLive()
    {
        using var service=new SearchService();
        var evidence=new List<object>();
        foreach(var source in SiteSources.Catalog.Where(s=>GlobalSources.Supports(s.Kind)))
        {
            var reports=new List<SourceReport>();
            var query=source.Kind=="Nyaa"?"blender":"ubuntu";
            var summary=await service.SearchAsync(query,[source],r=>reports.Add(r),CancellationToken.None);
            var report=reports.Single();
            evidence.Add(new {source.Name,query,count=report.Results.Count,report.Error,first=report.Results.FirstOrDefault()?.Title});
            Directory.CreateDirectory("docs/evidence/global-sources");
            File.WriteAllText("docs/evidence/global-sources/live.json",JsonSerializer.Serialize(evidence,new JsonSerializerOptions {WriteIndented=true}));
            Check(summary.Failed==0&&report.Results.Count>0&&report.Results.All(r=>Links.MagnetHash(r.Magnet)!=null),"LIVE "+source.Name+" count="+report.Results.Count+" "+report.Error);
        }
    }
    private static int Ui(bool live)
    {
        var app = new Application { Resources = new ResourceDictionary { Source = new Uri("/AFMC.Find;component/Theme.xaml", UriKind.Relative) } };
        var directory = Path.Combine(Path.GetTempPath(), "afmc-find-ui-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory);
        if (!live) store.Save([Source("fixture")]);
        var service = live ? new SearchService() : new SearchService(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.Query.Contains("slow")) await Task.Delay(Timeout.Infinite, ct);
            var item = Rss[Rss.IndexOf("<item>")..(Rss.IndexOf("</item>")+7)];
            var zero = item.Replace(Hash,new string('b',40)).Replace("value='9'","value='0'");
            var unknown = item.Replace(Hash,new string('c',40)).Replace("<t:attr name='seeders' value='9'/>","");
            var popular = item.Replace(Hash,new string('d',40)).Replace("value='9'","value='31'");
            return Response(Rss.Replace("</channel>",zero+unknown+popular+"</channel>"));
        }));
        var window = new MainWindow(store, service); int exit = 1;
        window.Loaded += async (_, _) =>
        {
            try
            {
                Check(!window.AllowsTransparency && ((Border)window.FindName("RootSurface")).Background is SolidColorBrush background && background.Color.A > 100, "WPF hardware-composited window and readable glass tint");
                Check(((Button)window.FindName("SearchButton")).Foreground is SolidColorBrush foreground && foreground.Color == Color.FromRgb(21, 18, 22), "WPF primary text contrast");
                var query = (TextBox)window.FindName("QueryBox");
                if (!live) { query.Text = "slow"; var old = window.RunSearchAsync(); query.Text = "ubuntu"; await window.RunSearchAsync(); await old; }
                else { query.Text = "ubuntu"; await window.RunSearchAsync(); }
                var list = (ListBox)window.FindName("ResultsList");
                Check(list.Items.Count > 0, "WPF rendered search results");
                if (!live) {
                    Check(((Button)window.FindName("SortButton")).Content.ToString()=="排序：综合推荐 ↓", "WPF default composite sort");
                    ((Button)window.FindName("SortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(list.Items.Cast<SearchResult>().Select(r=>r.Seeders).SequenceEqual(new long?[]{31,9,0,null}), "WPF explicit seeds sort with unknown last");
                }
                Check(list.SelectedItem is null, "WPF does not auto-select a result");
                for(var i=0;i<6;i++)((Button)window.FindName("SortButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(list.SelectedItem is null && !((FrameworkElement)window.FindName("DetailPanel")).IsVisible,"WPF repeated sort changes preserve the unselected full-width state");
                list.SelectedIndex=0;
                Check(((Button)window.FindName("OpenButton")).IsEnabled, "WPF selection enables handoff");
                Check(((TextBlock)window.FindName("StatusText")).Text.Contains("搜索完成"), "WPF current search owns status");
                ((TextBox)window.FindName("FilterBox")).Text = "no-match-xyz"; Check(list.Items.Count == 0, "WPF filter empty state");
                ((TextBox)window.FindName("FilterBox")).Text = ""; Check(list.Items.Count > 0, "WPF clear filter restores results");
                Check(window.FindName("ImagesOnly") is null, "WPF has no image filter");
                Check(new ThumbnailView().Visibility == Visibility.Collapsed, "WPF missing thumbnail occupies no space");
                ((Button)window.FindName("SourcesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(((Grid)window.FindName("SourcesView")).Visibility == Visibility.Visible, "WPF source navigation");
                ((Button)window.FindName("SearchNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                list.SelectedIndex=0;
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("docs/evidence/development");
                using (var file = File.Create("docs/evidence/development/" + (live ? "native-live.png" : "native-fixture.png"))) encoder.Save(file);
                window.Width = 1080; window.Height = 700; window.UpdateLayout(); Check(window.ActualWidth >= window.MinWidth, "WPF minimum size layout");
                var actions=(StackPanel)window.FindName("DetailActions");
                var actionPoint=actions.TranslatePoint(new Point(0,0),window);
                Check(actionPoint.Y+actions.ActualHeight<=window.ActualHeight,"WPF fixed detail actions remain within minimum window");
                ((Button)window.FindName("DensityButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((Button)window.FindName("DensityButton")).Content.ToString()=="紧凑密度","WPF compact density toggle");
                Check(VirtualizingPanel.GetIsVirtualizing(list),"WPF result virtualization preserved");
                Console.WriteLine("UI PASSED " + passed + (live ? " LIVE" : " FIXTURE")); exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("UI FAILED " + ex); }
            finally { window.Close(); }
        };
        app.Run(window); return exit;
    }
    private sealed class CiliCaptureHandler:DelegatingHandler
    {
        public CiliCaptureHandler():base(new HttpClientHandler {AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.All}) {}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var response=await base.SendAsync(request,ct);
            if(request.Method==HttpMethod.Post&&response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync(2*1024*1024,ct);
                await File.WriteAllBytesAsync("docs/evidence/global-sources/cili-post-response.html",await response.Content.ReadAsByteArrayAsync(ct),ct);
            }
            return response;
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}

