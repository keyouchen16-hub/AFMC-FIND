using System.IO;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Afmc.Find.App;
using Afmc.Find.Core;

internal static class SourceWorkbenchChecks
{
    internal static async Task Run(MainWindow window, SettingsStore store, Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string field,object value)=>typeof(MainWindow).GetField(field,flags)!.SetValue(window,value);
        void Call(string method)=>typeof(MainWindow).GetMethod(method,flags)!.Invoke(window,[]);
        T Find<T>(string name)=>(T)window.FindName(name);
        void Click(string name)=>Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click("SourcesNav");Click("AddSourceButton");
        check(Find<TextBox>("SourceWebsiteBox").IsKeyboardFocusWithin,"add source focuses inline URL input without opening another catalog");
        Set("sourceCatalog",new EngineSource[]{
            new("private","Account movies","https://private.example/","private","en",false){Categories=["电影"]},
            new("public","Open music","https://public.example/","public","en",false){Categories=["音乐"]}});
        Click("CatalogSourcesTab");window.UpdateLayout();
        var list=Find<ListBox>("SourceCatalogList");
        EngineSource Source(object row)=>(EngineSource)row.GetType().GetProperty("Source")!.GetValue(row)!;
        check(list.Items.Count==2&&Source(list.Items[0]).Type=="public","integrated catalog shows public sources first");
        Find<ComboBox>("SourceAccessBox").SelectedIndex=2;
        check(list.Items.Count==1&&Source(list.Items[0]).Id=="private","catalog account filter separates restricted sources");
        Find<ComboBox>("SourceAccessBox").SelectedIndex=0;
        Find<TextBox>("SourceFindBox").Text="音乐";
        check(list.Items.Count==1&&Source(list.Items[0]).Id=="public","catalog searches category labels");
        Find<TextBox>("SourceFindBox").Clear();Click("AddedSourcesTab");
        var added=Find<ListBox>("SourceList");
        check(added.IsVisible&&!list.IsVisible,"added and available sources share a single page");
        var original=store.Load().Count;
        var html="<html><title>Public fixture</title><form action='/search'><input type='search' name='q'></form><table><tr><td><a href='/details/1'>Ubuntu Linux ISO</a></td><td><a href='magnet:?xt=urn:btih:"+new string('d',40)+"'>magnet</a></td></tr></table></html>";
        Set("discoveryFactory",(Func<WebsiteDiscovery>)(()=>new WebsiteDiscovery(new DiscoveryChecks.Handler(request=>DiscoveryChecks.Reply(
            request.RequestUri!.Query.Contains("afmcprobe")?"<html>No matches</html>":html)),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}))));
        check(window.FindName("RecognizeSourceButton") is null&&window.FindName("ConfirmSourceButton") is null,"only one discovery and add action remains");
        Find<TextBox>("SourceWebsiteBox").Text="https://inline.example/";Click("AddSourceButton");
        for(var i=0;i<120&&Find<Button>("StopSourceProbeButton").IsEnabled;i++)await Task.Delay(50);
        check(store.Load().Count==original+1,"one click validates and saves a verified source");
        Click("AddSourceButton");
        for(var i=0;i<120&&Find<Button>("StopSourceProbeButton").IsEnabled;i++)await Task.Delay(50);
        check(store.Load().Count==original+1&&store.Load().Any(s=>s.Kind=="AutoWeb"&&s.Endpoint.Contains("inline.example")),"verified inline source persists through actual settings store");
        check(Find<Button>("AddSourceButton").IsEnabled,"repeat verification updates without duplicating and restores action");
        var release=new TaskCompletionSource();
        Set("discoveryFactory",(Func<WebsiteDiscovery>)(()=>new WebsiteDiscovery(new DelayedHandler(release.Task,html),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}))));
        Find<TextBox>("SourceWebsiteBox").Text="https://cancelled.example/";Click("AddSourceButton");
        check(!Find<Button>("AddSourceButton").IsEnabled,"busy discovery prevents duplicate submission");
        Find<TextBox>("SourceWebsiteBox").Text="https://changed.example/";release.SetResult();
        for(var i=0;i<120&&Find<Button>("StopSourceProbeButton").IsEnabled;i++)await Task.Delay(50);
        check(store.Load().Count==original+1,"changing URL cancels pending discovery without saving stale results");
        Set("discoveryFactory",(Func<WebsiteDiscovery>)(()=>new WebsiteDiscovery(new DiscoveryChecks.Handler(_=>DiscoveryChecks.Reply("<html>No search or download links</html>")),(_,_)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}))));
        Click("AddSourceButton");
        for(var i=0;i<120&&Find<Button>("StopSourceProbeButton").IsEnabled;i++)await Task.Delay(50);
        check(store.Load().Count==original+1,"failed recognition never adds an unusable source");
        var addedSource=added.Items.Cast<SourceConfig>().Single(s=>s.Endpoint.Contains("inline.example"));
        added.SelectedItem=addedSource;
        typeof(MainWindow).GetMethod("ToggleSource",flags)!.Invoke(window,[window,new RoutedEventArgs()]);Call("RefreshSources");
        check(!store.Load().Single(s=>s.Id==addedSource.Id).Enabled,"row enable action persists disabled state");
        check(added.Items.Cast<SourceConfig>().Single(s=>s.Id==addedSource.Id).StatusLabel.Contains("停用"),"disabled row status follows the saved switch");
        var settingsPath=Directory.GetFiles(store.DirectoryPath,"*.json").First(p=>Path.GetFileName(p).Contains("sources"));
        check(!File.ReadAllText(settingsPath).Contains("StatusLabel"),"transient connection status is not saved to source configuration");
        Click("SearchNav");
    }
    private sealed class DelayedHandler(Task release,string html):System.Net.Http.HttpMessageHandler
    {
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)
        {await release.WaitAsync(token);return DiscoveryChecks.Reply(html);}
    }
}
