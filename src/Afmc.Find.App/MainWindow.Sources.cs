using System.Windows;
using System.Windows.Controls;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public partial class MainWindow
{
    private sealed record CatalogRow(EngineSource Source, bool Added)
    {
        public bool CanAdd => !Added;
        public string ActionLabel => Added ? "已添加" : Source.AccessOrder == 0 ? "添加" : "配置账号";
    }
    private IReadOnlyList<EngineSource> sourceCatalog = [];
    private bool catalogVisible, catalogLoading;
    private readonly Dictionary<string, string> sourceChecks = [];
    private CancellationTokenSource? sourceProbe;
    private int sourceProbeGeneration;
    private Func<WebsiteDiscovery> discoveryFactory = () => new();

    private void ShowAddedSources(object sender, RoutedEventArgs e) => SetSourceTab(false);
    private async void ShowSourceCatalog(object sender, RoutedEventArgs e)
    {
        SetSourceTab(true);
        if (sourceCatalog.Count == 0) await LoadSourceCatalogAsync();
    }
    private void SetSourceTab(bool catalog)
    {
        catalogVisible = catalog;
        AddedSourcesTab.Tag = catalog ? null : "selected";
        CatalogSourcesTab.Tag = catalog ? "selected" : null;
        SourceList.Visibility = catalog ? Visibility.Collapsed : Visibility.Visible;
        SourceCatalogList.Visibility = catalog ? Visibility.Visible : Visibility.Collapsed;
        ReloadCatalogButton.Visibility = catalog ? Visibility.Visible : Visibility.Collapsed;
        ApplySourceFilter();
        UiMotion.Reveal(catalog ? SourceCatalogList : SourceList);
    }
    private async void ReloadSourceCatalog(object sender, RoutedEventArgs e) => await LoadSourceCatalogAsync();
    private async Task LoadSourceCatalogAsync()
    {
        if (catalogLoading) return;
        catalogLoading = true; ReloadCatalogButton.IsEnabled = false;
        ConnectionStatus.Text = "正在读取已适配目录…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try {
            sourceCatalog = await engine.CatalogAsync(timeout.Token);
            if (lifetime.IsCancellationRequested) return;
            ApplySourceFilter();
            ConnectionStatus.Text = $"{sourceCatalog.Count} 个适配来源 · 公开来源优先，添加前会测试连接。";
        } catch (Exception error) when (error is not OutOfMemoryException) {
            if (!lifetime.IsCancellationRequested) ConnectionStatus.Text = Links.Error(error);
        } finally { catalogLoading = false; ReloadCatalogButton.IsEnabled = true; }
    }
    private void SourceFilterChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplySourceFilter(); }
    private void SourceFilterSelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplySourceFilter(); }
    private void ApplySourceFilter()
    {
        if (SourceFindBox is null || SourceCategoryBox is null || sources is null) return;
        var query = SourceFindBox.Text.Trim();
        var category = (SourceCategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部资源";
        var access = SourceAccessBox.SelectedIndex;
        var selected = (SourceList.SelectedItem as SourceConfig)?.Id;
        var added = sources.Where(s => {
            var catalog = sourceCatalog.FirstOrDefault(c => c.Id == s.EngineIndexerId);
            var description = s.Name + " " + s.Description;
            var matchesQuery = query.Length == 0 || description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || catalog?.Categories.Any(c=>c.Contains(query,StringComparison.OrdinalIgnoreCase))==true;
            var matchesCategory = category == "全部资源" || (catalog is null
                ? description.Contains(category.Split('/')[0], StringComparison.OrdinalIgnoreCase) || category=="未标明" && s.Description.Length==0
                : catalog.Categories.Contains(category) || category=="未标明" && catalog.Categories.Length==0);
            // A manually configured endpoint does not establish public access.
            var publicAccess = catalog?.AccessOrder == 0 || catalog is null && s.Kind is not ("Torznab" or "Website");
            return matchesQuery && matchesCategory && (access == 0 || access == 1 && publicAccess || access == 2 && !publicAccess);
        }).OrderBy(s=>s.EngineIndexerId.Length==0 && s.Kind!="Torznab" ? 0 : sourceCatalog.FirstOrDefault(c=>c.Id==s.EngineIndexerId)?.AccessOrder??2)
          .ThenBy(s=>s.Name,StringComparer.OrdinalIgnoreCase)
          .Select(s=>s with {StatusLabel=s.Kind=="Website"?"仅网页入口":!s.Enabled?"已停用":sourceChecks.GetValueOrDefault(s.Id,"参与搜索")}).ToArray();
        SourceList.ItemsSource = added;
        SourceList.SelectedItem = added.FirstOrDefault(s=>s.Id==selected);
        var matches = EngineSource.Filter(sourceCatalog, "", access, category)
            .Where(c=>query.Length==0 || (c.Name+" "+c.Site+" "+c.CategoryLabel).Contains(query,StringComparison.OrdinalIgnoreCase));
        var rows = matches.Select(c=>new CatalogRow(c,sources.Any(s=>s.EngineIndexerId==c.Id))).ToArray();
        SourceCatalogList.ItemsSource = rows;
        SourcesEmptyText.Text = catalogVisible && sourceCatalog.Count==0 ? "选择刷新读取目录；已添加的来源仍可使用。" : "没有符合条件的来源";
        SourcesEmptyText.Visibility = (catalogVisible ? rows.Length : added.Length)==0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SourceRowToggle(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox {Tag: SourceConfig source}) { SourceList.SelectedItem=source; ToggleSource(sender,e); RefreshSources(); }
    }
    private void SourceRowMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button {Tag: SourceConfig source} button) return;
        SourceList.SelectedItem=source;
        var menu=new ContextMenu();
        void Add(string text,RoutedEventHandler action,bool enabled=true) {
            var item=new MenuItem{Header=text,IsEnabled=enabled};
            item.Click+=(_,args)=>{SourceList.SelectedItem=SourceList.Items.Cast<SourceConfig>().FirstOrDefault(s=>s.Id==source.Id);action(button,args);};
            menu.Items.Add(item);
        }
        Add("测试连接",TestSource,TestButton.IsEnabled);
        Add("编辑配置",EditSource,configHealthy);
        Add("打开原站",OpenSourceSite);
        menu.Items.Add(new Separator());
        Add("移除",RemoveSource,configHealthy && source.Kind is "Torznab" or "AutoWeb");
        menu.PlacementTarget=button;menu.IsOpen=true;
    }
    private void AddCatalogSource(object sender,RoutedEventArgs e)
    {
        if (!configHealthy || sender is not Button {Tag:EngineSource source}) return;
        var dialog=new EngineSourcesWindow(engine,source.Id,configureOnly:true){Owner=this};
        if(dialog.ShowDialog()==true && dialog.Result is {} result) AddVerifiedSource(result);
    }
    private bool AddVerifiedSource(SourceConfig result)
    {
        var old=sources.FirstOrDefault(s=>result.EngineIndexerId.Length>0?s.EngineIndexerId==result.EngineIndexerId:s.Kind=="AutoWeb"&&s.DiscoveryTemplate==result.DiscoveryTemplate);
        var next=old is null?sources.Append(result).ToList():sources.Select(s=>s.Id==old.Id?result with{Id=old.Id,Enabled=old.Enabled}:s).ToList();
        if(!SaveSources(next))return false;
        sourceChecks[old?.Id??result.Id]=$"连接通过 · {DateTime.Now:HH:mm}";
        RefreshSources();ConnectionStatus.Text=old is null?"已添加来源，后续搜索会使用此来源。":"已更新此来源，保留原启用状态。";
        return true;
    }
    private void SourceWebsiteChanged(object sender,TextChangedEventArgs e)
    {
        sourceProbeGeneration++;sourceProbe?.Cancel();
        if(SourceProbeMessage is not null && SourceProbePanel.Visibility==Visibility.Visible)SourceProbeMessage.Text="网址已更改，请重新识别后添加。";
    }
    private async void RecognizeSourceClick(object sender,RoutedEventArgs e)
    {
        if(sourceProbe is not null)return;
        if(!configHealthy){ConnectionStatus.Text="配置读取异常，已保留原文件；暂不能添加来源。";return;}
        var address=SourceWebsiteBox.Text.Trim();
        if(address.Length==0){SourceWebsiteBox.Focus();ConnectionStatus.Text="请先粘贴网站网址。";return;}
        if(!address.Contains("://"))address="https://"+address;
        sourceProbe?.Cancel();
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);sourceProbe=cancel;
        var current=++sourceProbeGeneration;
        SourceProbePanel.Visibility=Visibility.Visible;UiMotion.Reveal(SourceProbePanel);
        SourceProbeLog.Clear();SourceProbeMessage.Text="正在识别公开搜索与下载入口…";StopSourceProbeButton.IsEnabled=true;
        AddSourceButton.Content="识别中…";AddSourceButton.IsEnabled=false;
        var completed = false;
        try {
            using var discovery=discoveryFactory();
            var progress=new Progress<string>(message=>{if(!completed&&current==sourceProbeGeneration&&!cancel.IsCancellationRequested){SourceProbeMessage.Text=message;SourceProbeLog.AppendText(message+Environment.NewLine);}});
            var result=await discovery.DiscoverAsync(address,"",progress,cancel.Token);
            completed=true;
            if(current!=sourceProbeGeneration || cancel.IsCancellationRequested)return;
            SourceProbeMessage.Text=result.Message;
            SourceProbeLog.Text=string.Join(Environment.NewLine,result.Steps)+"\n\n"+string.Join("\n",result.Preview.Take(5).Select(r=>r.Title+" · "+r.ResourceType));
            if(result.Source is {} verified){
                if(AddVerifiedSource(verified)){SourceProbeMessage.Text="已验证并加入搜索；重复添加会更新原来源，不会创建副本。";SetSourceTab(false);}
                else SourceProbeMessage.Text=ConnectionStatus.Text;
            }
        } catch(Exception error) when(error is not OutOfMemoryException) {
            if(current==sourceProbeGeneration)SourceProbeMessage.Text=cancel.IsCancellationRequested?"已停止识别，未添加来源。":Links.Error(error);
        } finally {
            completed=true;
            if(ReferenceEquals(sourceProbe,cancel)){sourceProbe=null;StopSourceProbeButton.IsEnabled=false;AddSourceButton.Content="识别并添加";AddSourceButton.IsEnabled=configHealthy;}
            cancel.Dispose();
        }
    }
    private void StopSourceProbeClick(object sender,RoutedEventArgs e)=>sourceProbe?.Cancel();
    private void SourceWebsiteKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {if(e.Key==System.Windows.Input.Key.Enter&&AddSourceButton.IsEnabled){e.Handled=true;RecognizeSourceClick(sender,e);}}
}
