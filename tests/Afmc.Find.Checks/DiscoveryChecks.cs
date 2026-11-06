using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class DiscoveryChecks
{
    private static int passed;
    private static readonly string Hash=new('a',40);
    private static string Magnet=>"magnet:?xt=urn:btih:"+Hash;
    private static string Results=>$"<html><table><tr><td><a href='/details/1'>Ubuntu Linux ISO</a></td><td><a href='{Magnet}'>magnet</a></td></tr></table></html>";
    internal static HttpResponseMessage Reply(string body,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(body)};
    internal sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(action(request));}}
    private static Task<IPAddress[]> PublicDns(string _,CancellationToken ct)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")});
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
    internal static WebsiteDiscovery Fixture(bool ignoreQuery=false)=>new(new Handler(request=>request.RequestUri!.AbsolutePath=="/"?Reply("<html><title>Unlisted fixture</title><form action='/search'><input name='q' type='search'></form></html>"):Reply(ignoreQuery||request.RequestUri.Query.Contains("ubuntu",StringComparison.OrdinalIgnoreCase)?Results:"<html>No matches</html>")),PublicDns);
    public static async Task Run(bool live)
    {
        if(live)
        {
            using var discovery=new WebsiteDiscovery();var result=await discovery.DiscoverAsync("https://fosstorrents.com/","Blender",new Progress<string>(Console.WriteLine),CancellationToken.None);
            Console.WriteLine(result.Message);
            Check(result.Source is not null&&result.Preview.Count>0,"real website auto-discovery validates download entries without catalog");
            var saved=JsonSerializer.Deserialize<SourceConfig>(JsonSerializer.Serialize(result.Source))!;
            using var reopened=new WebsiteDiscovery();var results=await reopened.SearchAsync(saved,"Krita",CancellationToken.None);
            Check(results.Count>0&&results.All(r=>r.Title.Contains("Krita",StringComparison.OrdinalIgnoreCase)),"persisted recipe executes a new keyword query");
            var store=new SettingsStore(Path.GetFullPath(".tools/discovery-store-"+Guid.NewGuid().ToString("N")));store.Save([saved]);
            var restored=store.Load().Single(s=>s.Id==saved.Id);using var service=new SearchService();var reports=new List<SourceReport>();
            var summary=await service.SearchAsync("Krita",[restored],reports.Add,CancellationToken.None);
            Check(summary.Succeeded==1&&summary.Failed==0&&reports.Any(r=>r.Results.Count>0),"actual settings persistence and aggregation execute discovered source");
            Directory.CreateDirectory("docs/evidence/v0810");File.WriteAllText("docs/evidence/v0810/discovery-live.json",JsonSerializer.Serialize(new{Website=saved.Endpoint,Mode=saved.DiscoveryFormat,Template=saved.DiscoveryTemplate,ProbeCount=result.Preview.Count,NewQueryCount=results.Count,Steps=result.Steps}));
            Console.WriteLine("DISCOVERY LIVE PASSED "+passed);return;
        }
        using(var discovery=Fixture())
        {
            var result=await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None);
            Check(result.Source?.Kind=="AutoWeb"&&result.Source.EngineIndexerId==""&&result.Preview.Count==1,"unknown site GET form discovered without catalog");
            Check(result.Steps.Any(s=>s.Contains("对照验证通过")),"positive and negative query validated");
            var source=JsonSerializer.Deserialize<SourceConfig>(JsonSerializer.Serialize(result.Source))!;
            Check((await discovery.SearchAsync(source,"ubuntu",CancellationToken.None)).Count==1,"serialized recipe searches again");
            Check((await discovery.SearchAsync(source,"other",CancellationToken.None)).Count==0,"new query not replaced by probe query");
        }
        using(var discovery=Fixture(true))Check((await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None)).Source is null,"ignored keyword and recommended list rejected");
        using(var discovery=new WebsiteDiscovery(new Handler(_=>Reply("Login",HttpStatusCode.Forbidden)),PublicDns))Check((await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None)).Source is null,"403 never becomes successful source");
        using(var discovery=new WebsiteDiscovery(new Handler(_=>Reply("<html><form><input type='password'></form></html>")),PublicDns))Check((await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None)).Source is null,"login page not added");
        var page=new Uri("https://unlisted.example/");
        Check(WebsiteDiscovery.HtmlCandidates("<form method='post'><input name='q'></form>",page).Count==0,"POST form not submitted automatically");
        Check(WebsiteDiscovery.HtmlCandidates("<form action='https://other.example/search'><input name='q'></form>",page).Count==0,"cross-origin form excluded");
        foreach(var ip in new[]{"127.0.0.1","10.0.0.1","169.254.169.254","192.168.1.1","172.20.0.1","::1","fc00::1","::ffff:127.0.0.1"})Check(!WebsiteDiscovery.PublicAddress(IPAddress.Parse(ip)),"private address rejected "+ip);
        int calls=0;
        using(var discovery=new WebsiteDiscovery(new Handler(_=>{calls++;return Reply(Results);}),(_,_)=>Task.FromResult(new[]{IPAddress.Loopback})))Check((await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None)).Source is null&&calls==0,"DNS private address blocked before HTTP");
        using(var discovery=new WebsiteDiscovery(new Handler(_=>new(HttpStatusCode.Redirect){Headers={Location=new Uri("http://127.0.0.1/")}}),PublicDns))Check((await discovery.DiscoverAsync("https://unlisted.example/","ubuntu",null,CancellationToken.None)).Source is null,"redirect to loopback blocked");
        foreach(var url in new[]{"file:///tmp/test","https://user:pass@unlisted.example/","https://unlisted.example/?apikey=secret"})
        {try{WebsiteDiscovery.PublicUrl(url);throw new Exception("unsafe URL accepted");}catch(SourceException){Check(true,"unsafe or credential URL rejected");}}
        var rss=$"<rss><channel><item><title>Ubuntu Linux</title><link>{Magnet}</link></item></channel></rss>";
        using(var discovery=new WebsiteDiscovery(new Handler(_=>Reply(rss)),PublicDns)){var result=await discovery.DiscoverAsync("https://unlisted.example/feed.xml","ubuntu",null,CancellationToken.None);Check(result.Source?.DiscoveryFormat=="Feed"&&result.Message.Contains("订阅"),"feed scope clearly distinguished from full-site search");}
        using(var discovery=new WebsiteDiscovery(new Handler(_=>Reply("<rss><channel><item><title>Ubuntu news</title><link>https://unlisted.example/news</link></item></channel></rss>")),PublicDns))Check((await discovery.DiscoverAsync("https://unlisted.example/rss.xml","ubuntu",null,CancellationToken.None)).Source is null,"news-only feed not downloadable source");
        var spec="""{"openapi":"3.0.0","paths":{"/find":{"get":{"parameters":[{"name":"q","in":"query","required":true,"schema":{"type":"string"}}]}}}}""";
        Check(WebsiteDiscovery.OpenApiCandidates(Encoding.UTF8.GetBytes(spec),page).Count==1,"public OpenAPI search operation found");
        using(var discovery=new WebsiteDiscovery(new Handler(req=>req.RequestUri!.AbsolutePath switch{"/"=>Reply("<a href='/openapi.json'>API</a>"),"/openapi.json"=>Reply(spec),_=>Reply(req.RequestUri.Query.Contains("ubuntu")?$"[{{\"title\":\"Ubuntu Linux\",\"magnet\":\"{Magnet}\"}}]":"[]")}),PublicDns))Check((await discovery.DiscoverAsync(page.AbsoluteUri,"ubuntu",null,CancellationToken.None)).Source is not null,"advertised API discovered and searched end-to-end");
        var os=Encoding.UTF8.GetBytes("<OpenSearchDescription xmlns='http://a9.com/-/spec/opensearch/1.1/'><Url type='text/html' template='https://unlisted.example/find?q={searchTerms}'/></OpenSearchDescription>");
        Check(WebsiteDiscovery.OpenSearchCandidates(os,page).Count==1,"OpenSearch description template recognized");
        using(var discovery=new WebsiteDiscovery(new Handler(req=>req.RequestUri!.AbsolutePath switch{"/"=>Reply("<link rel='search' type='application/opensearchdescription+xml' href='/opensearch.xml'>"),"/opensearch.xml"=>Reply(Encoding.UTF8.GetString(os)),_=>Reply(req.RequestUri.Query.Contains("ubuntu")?Results:"<html>No matches</html>")}),PublicDns))Check((await discovery.DiscoverAsync(page.AbsoluteUri,"ubuntu",null,CancellationToken.None)).Source is not null,"OpenSearch discovery validation succeeds");
        Check(WebsiteDiscovery.ParseResults(Encoding.UTF8.GetBytes("<html><a href='/page'>Ubuntu</a></html>"),page,"fixture").Count==0,"ordinary page link is not a download");
        using(var discovery=Fixture()){using var cancel=new CancellationTokenSource();cancel.Cancel();try{await discovery.DiscoverAsync(page.AbsoluteUri,"ubuntu",null,cancel.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){Check(true,"cancellation propagated");}}
        var categories=EngineSource.Classify(JsonNode.Parse("""[{"ID":"5070"},{"ID":"2000"},{"ID":"3000"},{"ID":"7000"}]""")!.AsArray());
        Check(new[]{"动漫","电影","音乐","书籍"}.All(categories.Contains),"catalog category tags from declared capabilities");
        var sources=new[]{new EngineSource("private","A","https://a.example/","private","en",false){Categories=["书籍"]},new EngineSource("public","Z","https://z.example/","public","en",false){Categories=["音乐"]}};
        Check(EngineSource.Filter(sources,"",0,"全部资源").First().Type=="public","public sources sort before account sources");
        Check(EngineSource.Filter(sources,"",1,"音乐").Count()==1&&EngineSource.Filter(sources,"",1,"书籍").Count()==0,"access and resource filters combine");
        Console.WriteLine("DISCOVERY PASSED "+passed);
    }
    public static int Ui()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        using var engine=new EmbeddedEngine(Path.Combine(Path.GetTempPath(),"afmc-discovery-ui-"+Guid.NewGuid().ToString("N")));
        var window=new WebsiteDiscoveryWindow(engine,factory:()=>Fixture());Exception? failure=null;
        window.Loaded+=async(_,_)=>
        {
            try
            {
                var site=(TextBox)window.FindName("WebsiteBox");var query=(TextBox)window.FindName("ProbeQueryBox");var detect=(Button)window.FindName("DetectButton");var save=(Button)window.FindName("SaveDiscoveryButton");
                Check(engine.ProcessId is null,"website discovery does not start catalog engine");site.Text="https://unlisted.example/";query.Text="ubuntu";
                detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(var i=0;i<100&&!detect.IsEnabled;i++)await Task.Delay(50);
                Check(save.IsEnabled&&((ListBox)window.FindName("PreviewList")).Items.Count==1,"paste detect preview enables verified add");
                query.Text="Linux";Check(!save.IsEnabled,"input change invalidates prior verification");query.Text="ubuntu";
                detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(var i=0;i<100&&!detect.IsEnabled;i++)await Task.Delay(50);
                Check(save.IsEnabled,"probe can run again after editing");window.UpdateLayout();
                await Task.Delay(300);await DialogGlassChecks.Capture(window,"discovery");
                Directory.CreateDirectory("docs/evidence/v0810");var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create("docs/evidence/v0810/discovery-window.png"))png.Save(file);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }catch(Exception ex){failure=ex;window.Close();}
        };
        var result=window.ShowDialog();if(failure is not null)throw failure;Check(result==true&&window.Result?.Kind=="AutoWeb","native add returns discovered source");Console.WriteLine("DISCOVERY UI PASSED "+passed);return 0;
    }
}
