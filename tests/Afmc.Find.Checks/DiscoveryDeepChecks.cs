using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Afmc.Find.Core;
using Afmc.Find.App;

internal static class DiscoveryDeepChecks
{
    public static async Task Run(bool live=false)
    {
        int passed=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        if(live)
        {
            using var discovery=new WebsiteDiscovery();
            var result=await discovery.DiscoverAsync("https://4kbt.org/category/movie","",new Progress<string>(Console.WriteLine),CancellationToken.None);
            Check(result.Source is not null&&result.Preview.Count>0&&result.Source.DiscoveryQuery!="ubuntu","reported URL alone discovers and validates its own sample");
            var store=new SettingsStore(Path.GetFullPath(".tools/discovery-deep-"+Guid.NewGuid().ToString("N")));store.Save([result.Source!]);
            var source=store.Load().Single(s=>s.Id==result.Source!.Id);using var service=new SearchService();var reports=new List<SourceReport>();
            var summary=await service.SearchAsync("Hancock",[source],reports.Add,CancellationToken.None);
            var entries=reports.SelectMany(r=>r.Results).ToArray();
            Check(summary.Succeeded==1&&entries.Length==5&&entries.Select(r=>r.Title).Distinct().Count()==5,"saved discovered site aggregates five distinct English release variants");
            var chinese=await discovery.SearchAsync(source,"全民超人汉考克",CancellationToken.None);
            Check(chinese.Count==5&&chinese.All(r=>r.Title.Contains("2160p")),"Chinese query finds releases without losing specifications");
            Check((await discovery.SearchAsync(source,"afmcprobe"+Guid.NewGuid().ToString("N"),CancellationToken.None)).Count==0,"reported site random query stays empty");
            File.WriteAllText("docs/evidence/v0810/reported-site-live.json",System.Text.Json.JsonSerializer.Serialize(new{Website=source.Endpoint,Probe=source.DiscoveryQuery,ProbeCount=result.Preview.Count,English=entries.Length,Chinese=chinese.Count,DistinctVersions=entries.Select(r=>r.Title).Distinct().Count(),NegativeCount=0,SettingsStore=true,SearchService=true}));
            Console.WriteLine("DEEP LIVE PASSED "+passed);return;
        }
        var uri=new Uri("https://unlisted.example/");var magnet="magnet:?xt=urn:btih:"+new string('b',40);
        var html=$"<html><h1>示例作品</h1><ul><li><div class='release-name'>Example.2024.2160p.REMUX【4.5GB】</div><div><a href='{magnet}'>复制磁力</a></div></li></ul></html>";
        var rows=WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes(html),uri,"fixture");
        Check(rows.Count==1&&rows[0].Title.Contains("2160p.REMUX")&&rows[0].Size>0,"nested download label retains version and explicit size");
        Check(WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes($"<h1>Example</h1><p>{magnet}</p>"),uri,"fixture").Count==1,"plain text magnet recognized");
        Check(WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes($"<h1>Example</h1><button data-clipboard-text='{magnet}'>复制</button>"),uri,"fixture").Count==1,"copy button magnet recognized");
        Check(WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes($"<h1>Example</h1><aside>{magnet}</aside><script>var m='{magnet}'</script>"),uri,"fixture").Count==0,"sidebar and executable script do not become downloads");
        HttpResponseMessage Response(string body)=>DiscoveryChecks.Reply(body);
        var fixture=new DiscoveryChecks.Handler(req=>req.RequestUri!.AbsolutePath switch{
            "/category/movie"=>Response("<form action='/'><input name='s'></form><a href='/'>Brand</a><h2><a href='/details'>电影《示例作品》 Example</a></h2>"),
            "/"=>Response(req.RequestUri.Query.Contains("afmcprobe")?"<h2>无结果</h2><a href='/'>Brand</a>":"<h2><a href='/details'>电影《示例作品》 Example</a></h2>"),
            "/details"=>Response(html),_=>Response("No result")});
        using(var discovery=new WebsiteDiscovery(fixture,(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")})))
        {
            var result=await discovery.DiscoverAsync("https://unlisted.example/category/movie","",null,CancellationToken.None);
            Check(result.Source?.DiscoveryQuery=="示例作品"&&result.Preview.Count==1,"URL-only discovery chooses observed work instead of brand or ubuntu");
            Check(result.Preview[0].Title.Contains("示例作品")&&result.Preview[0].Title.Contains("2160p.REMUX"),"Chinese work context preserves English filename");
        }
        var binary=IntegrityChecks.TestTorrent("Example.iso");
        var requested=new List<string>();
        using(var discovery=new WebsiteDiscovery(new DiscoveryChecks.Handler(req=>{
            var path=req.RequestUri!.AbsolutePath;requested.Add(path);
            return path switch{
                "/"=>Response("<form action='/search'><input name='q'></form>"),
                "/search"=>Response(req.RequestUri.Query.Contains("afmcprobe")?"<html>No matches</html>":"<h2><a href='/details'>Example</a></h2>"),
                "/details"=>Response("<h1>Example</h1><a href='/download-page'>种子下载</a><a href='/delete'>下载</a>"),
                "/download-page"=>Response("<a href='/download.php?id=1'>Torrent</a>"),
                "/download.php"=>new(HttpStatusCode.OK){Content=new ByteArrayContent(binary)},
                _=>Response("No result")};}),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")})))
        {
            var result=await discovery.DiscoverAsync(uri.AbsoluteUri,"Example",null,CancellationToken.None);
            Check(result.Source is not null&&result.Preview.Single().TorrentUrl!.Contains("download.php"),"three-hop detail download page and extensionless torrent validated");
            Check(result.Preview[0].Size==1&&result.Preview[0].Hash is not null,"torrent bytes supply actual hash and size");
            Check(!requested.Contains("/delete"),"mutation-looking download link is never followed");
        }
        var captured=Path.GetFullPath("docs/evidence/v0710/4kbt-detail.html");
        var real=WebsiteDiscovery.ParseResults(File.ReadAllBytes(captured),new Uri("https://4kbt.org/64751.html"),"fixture");
        Check(real.Count==5&&real.Select(r=>r.Title).Distinct().Count()==5&&real.All(r=>r.Title.Contains("Hancock.2008.2160p")),"captured reported site yields five distinct real release names");
        Check(real.Count(r=>r.Size is null)==1,"missing size unit remains unknown");
        using(var discovery=new WebsiteDiscovery(new DiscoveryChecks.Handler(req=>Response(req.RequestUri!.AbsolutePath=="/"?"<form action='/search'><input name='q'></form>":$"<a href='/'>Example</a><aside><a href='/details'>Example</a></aside>")),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")})))
            Check((await discovery.DiscoverAsync(uri.AbsoluteUri,"Example",null,CancellationToken.None)).Source is null,"brand and recommendation links cannot validate a search");
        using(var discovery=new WebsiteDiscovery(new DiscoveryChecks.Handler(req=>Response(req.RequestUri!.AbsolutePath switch{"/"=>"<form action='/search'><input name='q'></form>","/search"=>"<a href='/details'>Example</a>",_=>html.Replace("示例作品","Example")})),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")})))
            Check((await discovery.DiscoverAsync(uri.AbsoluteUri,"Example",null,CancellationToken.None)).Source is null,"ignored query detected even when downloads are on detail pages");
        Console.WriteLine("DISCOVERY DEEP PASSED "+passed);
    }
}
