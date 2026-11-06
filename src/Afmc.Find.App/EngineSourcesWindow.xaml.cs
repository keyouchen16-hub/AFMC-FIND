using System.Net;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Afmc.Find.Core;
namespace Afmc.Find.App;

public partial class EngineSourcesWindow:Window
{
    private sealed record Choice(string Key,string Value){public override string ToString()=>Value;}
    private readonly EmbeddedEngine engine;
    private readonly string? initialId;
    private readonly CancellationTokenSource lifetime=new();
    private CancellationTokenSource? selection;
    private IReadOnlyList<EngineSource> catalog=[];
    private readonly List<Func<JsonObject>> readers=[];
    private bool saving;
    public SourceConfig? Result{get;private set;}
    public EngineSourcesWindow(EmbeddedEngine engine,string? selectedId=null,bool configureOnly=false)
    {
        InitializeComponent();this.engine=engine;initialId=selectedId;VersionText.Text=ProductInfo.Title;
        DialogGlass.Attach(this);
        VersionText.Visibility=Visibility.Collapsed;
        if(configureOnly){CatalogFilters.Visibility=CatalogList.Visibility=Visibility.Collapsed;CatalogColumn.Width=CatalogGap.Width=new GridLength(0);MinWidth=540;Width=620;}
        MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;
        Loaded+=async(_,_)=>await LoadCatalog();Closed+=(_,_)=>{lifetime.Cancel();selection?.Cancel();};
    }
    private async void Reload(object sender,RoutedEventArgs e)=>await LoadCatalog();
    private async Task LoadCatalog()
    {
        ReloadButton.IsEnabled=false;AddButton.IsEnabled=false;
        try
        {
            catalog=await engine.CatalogAsync(lifetime.Token);
            if(lifetime.IsCancellationRequested)return;
            ApplyFilter();StatusText.Text=$"目录已就绪 · {catalog.Count} 个适配来源，实际可用性以测试为准。";
            if(initialId is not null)CatalogList.SelectedItem=catalog.FirstOrDefault(x=>x.Id==initialId);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(!lifetime.IsCancellationRequested)StatusText.Text=Links.Error(ex);}
        finally{ReloadButton.IsEnabled=true;}
    }
    private void FilterChanged(object sender,TextChangedEventArgs e){if(CatalogList is not null&&!saving)ApplyFilter();}
    private void CategoryChanged(object sender,SelectionChangedEventArgs e){if(CatalogList is not null&&!saving)ApplyFilter();}
    private void ApplyFilter()
    {
        var query=FindSourceBox.Text.Trim();
        if(Uri.TryCreate(query,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")query=uri.Host;
        CatalogList.ItemsSource=EngineSource.Filter(catalog,query,AccessFilter?.SelectedIndex??0,(ResourceFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString()??"全部资源").ToArray();
    }
    private async void SelectSource(object sender,SelectionChangedEventArgs e)
    {
        selection?.Cancel();selection=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=selection.Token;
        readers.Clear();FieldsPanel.Children.Clear();AddButton.IsEnabled=false;
        if(CatalogList.SelectedItem is not EngineSource source){SelectedName.Text="选择一个来源";SelectedSite.Text="";return;}
        SelectedName.Text=source.Name;SelectedSite.Text=source.Site;SelectedDescription.Text=source.CategoryLabel+Environment.NewLine+Plain(source.Description);StatusText.Text="正在读取来源配置…";
        try
        {
            var fields=await engine.ConfigurationAsync(source.Id,token);token.ThrowIfCancellationRequested();
            var unsupported=false;
            foreach(var field in fields.OfType<JsonObject>())if(!RenderField(field))unsupported=true;
            AddButton.IsEnabled=!unsupported;
            StatusText.Text=unsupported?"此来源包含暂不支持的验证控件，不能直接添加。请选择其他来源。":"核对配置后点击“测试并添加”。已有账号的来源使用你自己的凭据。";
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(!token.IsCancellationRequested)StatusText.Text=Links.Error(ex);}
    }
    private static string Text(JsonNode? value)=>value is JsonValue v&&v.TryGetValue<string>(out var s)?s:value?.ToJsonString()??"";
    private static string Plain(string text)
    {var html=new HtmlAgilityPack.HtmlDocument();html.LoadHtml(text);var value=WebUtility.HtmlDecode(html.DocumentNode.InnerText);return value.Length>4000?value[..4000]:value;}
    private bool RenderField(JsonObject field)
    {
        var id=Text(field["id"]);var type=Text(field["type"]);var label=Plain(Text(field["name"]));var value=Text(field["value"]);
        label=label switch{"Site Link"=>"网站地址","Username"=>"用户名","Password"=>"密码","API Key"=>"站点 API Key","Search only in title"=>"仅搜索标题","Download using .torrent only. No Magnets."=>"仅使用种子文件，不使用磁力链接","Sort requested from site"=>"来源排序","Order requested from site"=>"排列顺序",_=>label};
        var box=new StackPanel{Margin=new Thickness(0,0,0,14)};
        if(type is "displayinfo")
        {box.Children.Add(new TextBlock{Text=Plain(value),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=(System.Windows.Media.Brush)FindResource("Muted")});FieldsPanel.Children.Add(box);return true;}
        if(type=="hiddendata"){readers.Add(()=>new JsonObject{["id"]=id,["value"]=value});return true;}
        box.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)});
        Func<JsonObject> read;
        switch(type)
        {
            case "inputbool":
                var check=new CheckBox{Content="启用",IsChecked=field["value"]?.GetValue<bool>()??false};box.Children.Add(check);read=()=>new(){["id"]=id,["value"]=(check.IsChecked==true).ToString().ToLowerInvariant()};break;
            case "inputselect":
                var options=(field["options"] as JsonObject??new()).Select(p=>new Choice(p.Key,Text(p.Value))).ToArray();
                var select=new ComboBox{Style=(Style)FindResource("CompactSelect"),ItemsSource=options,DisplayMemberPath="Value",SelectedValuePath="Key",SelectedValue=value,MinHeight=34};box.Children.Add(select);
                read=()=>new(){["id"]=id,["value"]=select.SelectedValue?.ToString()??value};break;
            case "inputcheckbox":
                var choices=new ListBox{SelectionMode=SelectionMode.Multiple,MaxHeight=150};
                var pairs=(field["options"] as JsonObject??new()).Select(p=>new KeyValuePair<string,string>(p.Key,Text(p.Value))).ToArray();
                choices.ItemsSource=pairs;choices.DisplayMemberPath="Value";
                var chosen=(field["values"] as JsonArray??new()).Select(Text).ToHashSet();foreach(var pair in pairs)if(chosen.Contains(pair.Key))choices.SelectedItems.Add(pair);
                box.Children.Add(choices);read=()=>new(){["id"]=id,["values"]=new JsonArray(choices.SelectedItems.Cast<KeyValuePair<string,string>>().Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};break;
            case "password":
                var password=new PasswordBox{Password=value,Height=36,Padding=new Thickness(10,5,10,5)};box.Children.Add(password);read=()=>new(){["id"]=id,["value"]=password.Password};break;
            case "inputstring":case "inputtags":
                if((id+label).Contains("key",StringComparison.OrdinalIgnoreCase)||(id+label).Contains("cookie",StringComparison.OrdinalIgnoreCase)||(id+label).Contains("password",StringComparison.OrdinalIgnoreCase))
                {var secret=new PasswordBox{Password=value,Height=36,Padding=new Thickness(10,5,10,5)};box.Children.Add(secret);read=()=>new(){["id"]=id,["value"]=secret.Password};}
                else{var input=new TextBox{Style=(Style)FindResource("CompactInput"),Text=value,Height=36,MaxLength=4096};box.Children.Add(input);read=()=>new(){["id"]=id,["value"]=input.Text};}break;
            default:
                box.Children.Add(new TextBlock{Text="此验证控件暂不支持。",TextWrapping=TextWrapping.Wrap});FieldsPanel.Children.Add(box);return false;
        }
        FieldsPanel.Children.Add(box);readers.Add(read);return true;
    }
    private async void Add(object sender,RoutedEventArgs e)
    {
        if(CatalogList.SelectedItem is not EngineSource source||saving)return;
        saving=true;CatalogList.IsEnabled=FindSourceBox.IsEnabled=ReloadButton.IsEnabled=FieldsPanel.IsEnabled=AddButton.IsEnabled=false;
        StatusText.Text="正在保存并测试来源，请稍候…";
        try
        {
            var values=new JsonArray(readers.Select(read=>(JsonNode)read()).ToArray());
            await engine.ConfigureAsync(source.Id,values,lifetime.Token);
            await engine.TestAsync(source.Id,lifetime.Token);
            if(lifetime.IsCancellationRequested)return;
            Result=source.ToConfig();DialogResult=true;
        }
        catch(Exception ex) when(ex is not OutOfMemoryException){if(!lifetime.IsCancellationRequested)StatusText.Text=Links.Error(ex);}
        finally{saving=false;CatalogList.IsEnabled=FindSourceBox.IsEnabled=ReloadButton.IsEnabled=FieldsPanel.IsEnabled=AddButton.IsEnabled=true;}
    }
}
