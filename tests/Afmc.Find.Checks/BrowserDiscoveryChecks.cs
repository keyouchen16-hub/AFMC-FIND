using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Afmc.Find.Core;
using Afmc.Find.App;

internal static class BrowserDiscoveryChecks
{
    public static async Task Run(bool live)
    {
        int passed=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        if(live)
        {
            using var probe=new WebsiteDiscovery();
            var result=await probe.DiscoverAsync("https://www.acgndog.com/","终末列车",new Progress<string>(Console.WriteLine),CancellationToken.None);
            Check(result.Source?.DiscoveryFormat=="BrowserSearch"&&result.Preview.Any(r=>CloudShares.Provider(r.ShareUrl)=="夸克网盘"),"reported site dynamic search and two-step download yield cloud share");
            var store=new SettingsStore(Path.GetFullPath(".tools/browser-live-"+Guid.NewGuid().ToString("N")));store.Save([result.Source!]);
            var source=store.Load().Single(s=>s.Id==result.Source!.Id);
            using var search=new SearchService();var reports=new List<SourceReport>();
            var summary=await search.SearchAsync("终末列车",[source],reports.Add,CancellationToken.None);
            Check(summary.Succeeded==1&&reports.SelectMany(r=>r.Results).Any(r=>r.ShareUrl is not null),"persisted browser source works through aggregate search");
            Check((await probe.SearchAsync(source,"afmcprobe"+Guid.NewGuid().ToString("N"),CancellationToken.None)).Count==0,"real negative search stays empty");
            var json=JsonSerializer.Serialize(source);Check(!json.Contains('#')&&!json.Contains("getDownloadItems")&&!json.Contains("token",StringComparison.OrdinalIgnoreCase),"stored recipe contains no transient redirect credential");
            Directory.CreateDirectory("docs/evidence/v0810");File.WriteAllText("docs/evidence/v0810/browser-live.json",JsonSerializer.Serialize(new{source.Endpoint,source.DiscoveryFormat,source.DiscoveryQuery,Preview=result.Preview.Count,Aggregate=reports.Sum(r=>r.Results.Count),Negative=0,Passed=passed}));
            Console.WriteLine("BROWSER LIVE PASSED "+passed);return;
        }
        Check(CloudShares.Provider("https://pan.quark.cn/s/abcdefgh")=="夸克网盘","recognize exact cloud provider share path");
        foreach(var invalid in new[]{"https://pan.quark.cn.evil.test/s/abcdefgh","https://pan.quark.cn/","https://pan.quark.cn/login","http://pan.quark.cn/s/abcdefgh","https://user:pass@pan.quark.cn/s/abcdefgh","https://pan.quark.cn:444/s/abcdefgh"})Check(CloudShares.Provider(invalid)is null,"reject non-share URL");
        var row=WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes("<h1>Example</h1><a href='https://pan.quark.cn/s/abcdefgh'>下载</a>"),new("https://fixture.example/detail"),"fixture").Single();
        Check(row.ShareUrl is not null&&row.Magnet is null&&row.TorrentUrl is null&&row.Size is null&&row.Seeders is null,"cloud result never fabricated as BT or known size/seeds");
        Check(ResourceObservation.From(row).ToResult().ShareUrl==row.ShareUrl,"observation preserves cloud link");
        var visits=new List<string>();bool ignoreQuery=false,keepDefaultList=false;
        using var discovery=new WebsiteDiscovery(new DiscoveryChecks.Handler(request=>
        {
            var path=request.RequestUri!.AbsolutePath;lock(visits)visits.Add(path);
            var response=DiscoveryChecks.Reply(path switch
            {
                "/"=>"<html><head><title>Fixture</title></head><body><a href='/search/'>搜索</a><h2><a href='/detail'>《Example》资源</a></h2></body></html>",
                "/search/"=>"<html><body><form><input type='search'><button>搜索</button></form><section id='results'></section><script>document.querySelector('form').onsubmit=e=>{e.preventDefault();document.querySelector('#results').innerHTML=document.querySelector('input').value.includes('afmcprobe')?'No matches':`<h2><a href='/detail'>《Example》资源</a></h2>`;}</script></body></html>",
                "/detail"=>"<html><body><h1>《Example》资源</h1><a onclick=\"window.open('/download#ephemeral-fixture')\">下载</a></body></html>",
                "/download"=>"<html><body><a onclick=\"window.open('https://pan.quark.cn/s/abcdefgh')\">下载</a></body></html>",
                _=>"<html><body>Not found</body></html>"
            });
            var body=response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if(ignoreQuery)body=body.Replace("document.querySelector('input').value.includes('afmcprobe')","false");
            else if(keepDefaultList)body=body.Replace("'No matches'","`<h2><a href='/detail'>《Example》资源</a></h2><h2><a href='/other'>Other work</a></h2><h2><a href='/third'>Third work</a></h2>`");
            response.Content=new System.Net.Http.StringContent(body,Encoding.UTF8,"text/html");return response;
        }),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}),true);
        var found=await discovery.DiscoverAsync("https://fixture.example/","Example",new Progress<string>(Console.WriteLine),CancellationToken.None);
        Console.WriteLine(found.Message+" Requests="+string.Join(",",visits));
        Check(found.Source?.DiscoveryFormat=="BrowserSearch"&&found.Preview.Single().ShareUrl==row.ShareUrl,"dynamic form and nested href-less buttons discovered");
        Check(!JsonSerializer.Serialize(found.Source).Contains("ephemeral"),"temporary fragment never persisted");
        Check((await discovery.SearchAsync(found.Source!,"Example",CancellationToken.None)).Single().ShareUrl==row.ShareUrl,"saved dynamic recipe regenerates download flow");
        Check((await discovery.SearchAsync(found.Source!,"afmcprobeXYZ",CancellationToken.None)).Count==0,"negative dynamic query returns no results");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{await discovery.SearchAsync(found.Source!,"Example",cancelled.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){Check(true,"cancel before browser start respected");}
        using var midCancel=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try{await discovery.SearchAsync(found.Source!,"Example",midCancel.Token);throw new Exception("active cancellation ignored");}catch(OperationCanceledException){Check(true,"active browser cancellation closes context");}
        Check((await discovery.SearchAsync(found.Source!,"Example",CancellationToken.None)).Count==1,"browser slot released and search recovers after cancellation");
        keepDefaultList=true;
        Check((await discovery.DiscoverAsync("https://fixture.example/","Example",null,CancellationToken.None)).Source is not null,"keyword narrowing remains valid when no-result UI retains broader default list");
        ignoreQuery=true;
        Check((await discovery.DiscoverAsync("https://fixture.example/","Example",null,CancellationToken.None)).Source is null,"dynamic fixed list ignoring query is rejected");
        Console.WriteLine("BROWSER CHECKS PASSED "+passed);
    }
}
