using System.Windows;
using System.Windows.Controls;
using Afmc.Find.Core;
namespace Afmc.Find.App;
public partial class WebsiteDiscoveryWindow:Window
{
    private readonly EmbeddedEngine engine;
    private readonly Func<WebsiteDiscovery> factory;
    private readonly CancellationTokenSource lifetime=new();
    private CancellationTokenSource? operation;
    private SourceConfig? verified;
    private int generation;
    public SourceConfig? Result{get;private set;}
    public WebsiteDiscoveryWindow(EmbeddedEngine engine,SourceConfig? previous=null,Func<WebsiteDiscovery>? factory=null)
    {
        InitializeComponent();this.engine=engine;this.factory=factory??(()=>new());VersionText.Text=ProductInfo.Title;
        DialogGlass.Attach(this);
        VersionText.Visibility=Visibility.Collapsed;
        CatalogButton.Visibility=Visibility.Collapsed; // Directory now lives on the main sources page.
        MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;
        if(previous is not null){WebsiteBox.Text=previous.Endpoint;ProbeQueryBox.Text=previous.DiscoveryQuery;}
        Loaded+=(_,_)=>WebsiteBox.Focus();Closed+=(_,_)=>{generation++;lifetime.Cancel();operation?.Cancel();};
    }
    private void InputChanged(object sender,TextChangedEventArgs e)
    {if(SaveDiscoveryButton is null)return;generation++;verified=null;SaveDiscoveryButton.IsEnabled=false;operation?.Cancel();}
    private async void Detect(object sender,RoutedEventArgs e)
    {
        operation?.Cancel();operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=operation.Token;var current=++generation;
        verified=null;SaveDiscoveryButton.IsEnabled=false;DetectButton.IsEnabled=WebsiteBox.IsEnabled=ProbeQueryBox.IsEnabled=CatalogButton.IsEnabled=false;CancelProbeButton.IsEnabled=true;
        ProbeLog.Clear();PreviewList.ItemsSource=null;OutcomeText.Text="正在探测网站…";
        var address=WebsiteBox.Text.Trim();if(!address.Contains("://"))address="https://"+address;
        var completed=false;
        try
        {
            using var discovery=factory();
            var progress=new Progress<string>(message=>{if(!completed&&current==generation&&!token.IsCancellationRequested){ProbeLog.AppendText(message+Environment.NewLine);ProbeLog.ScrollToEnd();}});
            var result=await discovery.DiscoverAsync(address,ProbeQueryBox.Text.Trim(),progress,token);
            completed=true;
            if(current!=generation||token.IsCancellationRequested)return;
            verified=result.Source;OutcomeText.Text=result.Message;PreviewList.ItemsSource=result.Preview;SaveDiscoveryButton.IsEnabled=verified is not null;
            ProbeLog.Text=string.Join(Environment.NewLine,result.Steps);
            ProbeLog.ScrollToHome();
        }
        catch(Exception ex)when(ex is not OutOfMemoryException){if(current==generation)OutcomeText.Text=Links.Error(ex);}
        finally{completed=true;if(current==generation){DetectButton.IsEnabled=WebsiteBox.IsEnabled=ProbeQueryBox.IsEnabled=CatalogButton.IsEnabled=true;CancelProbeButton.IsEnabled=false;}}
    }
    private void CancelProbe(object sender,RoutedEventArgs e)=>operation?.Cancel();
    private void Save(object sender,RoutedEventArgs e){if(verified is null)return;Result=verified;DialogResult=true;}
    private void OpenCatalog(object sender,RoutedEventArgs e)
    {var dialog=new EngineSourcesWindow(engine){Owner=this};if(dialog.ShowDialog()==true&&dialog.Result is {} source){Result=source;DialogResult=true;}}
}
