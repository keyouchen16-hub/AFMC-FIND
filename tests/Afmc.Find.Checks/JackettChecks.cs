using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.App;
using Afmc.Find.Core;
internal static class JackettChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);passed++;}
        void Reject(string url,string key){try{JackettConnection.Parse(url,key);}catch(SourceException){passed++;return;}throw new Exception("invalid input accepted");}
        var c=JackettConnection.Parse("https://example.org/jackett/api/v2.0/indexers/sample/results/torznab/api?apikey=fixture%2Bkey&t=search","old");
        Check(c.BaseUrl=="https://example.org/jackett/" && c.ApiKey=="fixture+key","complete link strips credentials and retains reverse proxy prefix");
        Check(JackettConnection.Parse("http://127.0.0.1:9117/UI/Dashboard","fixture").BaseUrl=="http://127.0.0.1:9117/","dashboard normalized");
        Reject("http://remote.example/","fixture");Reject("https://example.org/","");Reject("https://example.org/?apikey=a&apikey=b","fixture");Reject("https://user:pass@example.org/","fixture");Reject("file:///tmp/test","fixture");
        var json="""[{"id":"sample","name":"Example source","configured":true},{"id":"off","configured":false},{"id":"../bad","configured":true},{"id":"sample","configured":true}]""";
        var rows=c.ParseIndexers(Encoding.UTF8.GetBytes(json));
        Check(rows.Count==1 && rows[0].ApiKey=="fixture+key" && rows[0].Endpoint=="https://example.org/jackett/api/v2.0/indexers/sample/results/torznab/api","configured only, deduplicated, path injection rejected");
        using(var service=new SearchService(new Handler(request=>{Check(request.RequestUri!.Query.Contains("configured=true") && request.RequestUri.Query.Contains("fixture%2Bkey"),"request uses configured filter and encoded key");return new(HttpStatusCode.OK){Content=new StringContent(json)};})))
            Check(service.JackettIndexersAsync(c,CancellationToken.None).GetAwaiter().GetResult().Count==1,"HTTP adapter fixture");
        using(var service=new SearchService(new Handler(_=>new(HttpStatusCode.Unauthorized))))
        {try{service.JackettIndexersAsync(c,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("unauthorized accepted");}catch(HttpRequestException ex){Check(Links.Error(ex).Contains("认证失败"),"authentication errors sanitized");}}
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var window=new SourceDialog();window.Show();window.UpdateLayout();
        Check(window.Title=="配置 Torznab 搜索源" && !((Button)window.FindName("SaveButton")).IsEnabled,"existing name and import initially disabled");
        var list=(ListBox)window.FindName("IndexerList");list.ItemsSource=rows;list.Visibility=Visibility.Visible;window.UpdateLayout();
        Check(list.Items.Count==1 && list.ActualHeight>0,"selectable source list layout");
        Directory.CreateDirectory("docs/evidence/v0810");
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create("docs/evidence/v0810/jackett-dialog.png"))png.Save(file);
        window.Close();
        var import=new SourceDialog();
        import.Loaded+=(_,_)=>import.Dispatcher.BeginInvoke(new Action(()=>{
            var choices=(ListBox)import.FindName("IndexerList");choices.ItemsSource=rows;choices.SelectedItems.Add(rows[0]);
            var save=(Button)import.FindName("SaveButton");save.IsEnabled=true;save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Check(import.ShowDialog()==true && import.Results.Count==1 && import.Results[0].Endpoint==rows[0].Endpoint,"selected sources returned by modal import");
        var edit=new SourceDialog(rows[0]);Check(((Button)edit.FindName("SaveButton")).IsEnabled,"existing source edit preserved");edit.Close();
        Console.WriteLine($"JACKETT PASSED {passed}");return 0;
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request));}
}
