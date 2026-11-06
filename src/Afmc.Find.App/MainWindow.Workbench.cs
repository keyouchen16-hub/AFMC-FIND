using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Afmc.Find.Core;
namespace Afmc.Find.App;

public partial class MainWindow
{
    private readonly Dictionary<string,SourceReport> sourceReports=new();
    private PersonalLibrary? library;
    private bool favoritesView;
    private ResultFilter resultFilter=new();
    private IReadOnlyList<string> queryAliases=[];
    private void InitializeWorkbench()
    {
        try{library=new PersonalLibrary(settings.DirectoryPath);}
        catch(Exception e) when(e is not OutOfMemoryException){StatusText.Text="个人记录读取失败，原文件保留；历史与收藏暂不可写。";}
    }
    public static string ResourceInfo(SearchResult row)=>$"名称：{row.Title}\n类别：{row.Category}\n类型：{row.ResourceType}\n规格：{SearchQuality.TechnicalSummary(row)}\n大小：{row.SizeText}\n做种：{row.SeedsText}\n发布时间：{row.PublishedText}\n来源：{row.Source}\n磁力：{row.Magnet??"未知"}\n网盘：{row.ShareUrl??"无"}\n页面：{row.DetailsUrl??"未知"}\n图片：{row.ThumbnailLabel}\n{SourceEvidence(row)}";
    public static string SourceEvidence(SearchResult row)
    {
        var text=row.MetadataNote;
        if(row.Observations.Count>1)text+="\n\n各来源原始记录：\n"+string.Join("\n",row.Observations.Select(o=>
            $"{o.Source}：{o.Title}\n{o.ToResult().SizeText} · 发布 {o.ToResult().PublishedText} · 做种 {o.ToResult().SeedsText}"));
        return text;
    }
    private void CopyText(string text,string success)
    {
        try{Clipboard.SetText(text);StatusText.Text=success;}
        catch(System.Runtime.InteropServices.ExternalException){StatusText.Text="剪贴板忙，请稍后重试。";}
    }
    private void CopyInfoClick(object sender,RoutedEventArgs e){if(ResultsList.SelectedItem is SearchResult row)CopyText(ResourceInfo(row),"完整资源信息已复制。");}
    private void FavoriteClick(object sender,RoutedEventArgs e)
    {
        if(library is null||ResultsList.SelectedItem is not SearchResult row)return;
        try{library.Toggle(row);StatusText.Text=library.IsFavorite(row)?"已收藏到本机。":"已取消收藏。";RefreshResults();}
        catch(Exception error) when(error is not OutOfMemoryException){StatusText.Text="收藏保存失败，原记录已保留；请检查空间/权限或收藏数量。";}
    }
    private void FavoritesClick(object sender,RoutedEventArgs e)
    {
        favoritesView=!favoritesView;FavoritesButton.Content=favoritesView?"返回结果":"收藏";RefreshResults();
    }
    private void RecordHistory(string query)
    {
        if(!preferences.RememberHistory||library is null)return;
        try{library.Remember(query);}catch(Exception e) when(e is not OutOfMemoryException){StatusText.Text="历史记录保存失败，搜索仍可继续。";}
    }
    private async void HistoryClick(object sender,RoutedEventArgs e)
    {
        var dialog=new WorkbenchDialog(this,"搜索历史",600,440);
        var list=new ListBox{Name="HistoryList",ItemsSource=library?.History.ToArray(),Margin=new(0,0,0,10)};
        var panel=new DockPanel();dialog.Body.Children.Add(panel);
        var note=new TextBlock{Text=preferences.RememberHistory?"仅保存在本机，最近50条；可在设置中关闭记录。":"记录已关闭；以下为之前保存的历史。",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};DockPanel.SetDock(note,Dock.Top);panel.Children.Add(note);panel.Children.Add(list);
        string? selected=null;
        var search=new Button{Content="再次搜索",Margin=new(0,0,8,0)};search.Click+=(_,_)=>{if(list.SelectedItem is string q){selected=q;dialog.Close();}};
        var clear=new Button{Content="清空历史",IsEnabled=library is not null};clear.Click+=(_,_)=>
        {try{library?.ClearHistory();list.ItemsSource=Array.Empty<string>();note.Text="历史已清空。";}catch(Exception error) when(error is not OutOfMemoryException){note.Text="清空失败，请检查目录权限。";}};
        dialog.Tools.Children.Add(search);dialog.Tools.Children.Add(clear);dialog.ShowDialog();
        if(selected is not null){QueryBox.Text=selected;await RunSearchAsync();}
    }
    private void FilterOptionsClick(object sender,RoutedEventArgs e)
    {
        var dialog=new WorkbenchDialog(this,"筛选",520,380);var panel=new StackPanel();dialog.Body.Children.Add(panel);
        panel.Children.Add(new TextBlock{Text="结果内关键词",Margin=new(0,0,0,8)});
        var text=new TextBox{Text=FilterBox.Text,Height=32,Padding=new(8,4,8,4),MaxLength=200};panel.Children.Add(text);
        panel.Children.Add(new TextBlock{Text="文件大小",Margin=new(0,8,0,8)});var sizes=new WrapPanel();panel.Children.Add(sizes);
        long? min=resultFilter.MinBytes,max=resultFilter.MaxBytes;
        foreach(var option in new (string Label,long? Min,long? Max)[]{("不限",null,null),("小于1 GB",null,1073741824),("1–10 GB",1073741824,10737418240),("10 GB以上",10737418240,null)})
        {var radio=new RadioButton{Content=option.Label,GroupName="size",IsChecked=min==option.Min&&max==option.Max,Margin=new(0,0,14,12),Foreground=Brushes.White};radio.Checked+=(_,_)=>{min=option.Min;max=option.Max;};sizes.Children.Add(radio);}
        var seeds=new CheckBox{Content="只显示来源报告有做种的结果",IsChecked=resultFilter.SeededOnly};panel.Children.Add(seeds);
        panel.Children.Add(new TextBlock{Text="选择大小范围会排除大小未知的条目；类别根据来源和文件名识别。",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,12)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(buttons);
        var apply=new Button{Content="应用筛选",Style=(Style)FindResource("Primary"),Margin=new(0,0,8,0)};apply.Click+=(_,_)=>{resultFilter=new("全部",min,max,seeds.IsChecked==true);FilterBox.Text=text.Text;dialog.Close();};buttons.Children.Add(apply);
        var reset=new Button{Content="清除筛选"};reset.Click+=(_,_)=>{resultFilter=new();FilterBox.Text="";dialog.Close();};buttons.Children.Add(reset);
        dialog.ShowDialog();FilterOptionsButton.Content=resultFilter.Active||FilterBox.Text.Length>0?"筛选 ·":"筛选";RefreshResults();
    }
    private IReadOnlyList<SearchResult> Versions(SearchResult selected)
    {
        var key=PersonalLibrary.Key(selected);
        return resourceGroups.FirstOrDefault(g=>g.Versions.Any(r=>PersonalLibrary.Key(r)==key))?.Versions??[selected];
    }
    private void VersionsClick(object sender,RoutedEventArgs e)
    {
        if(ResultsList.SelectedItem is not SearchResult selected)return;
        expandedVersions=Versions(selected).Select(PersonalLibrary.Key).ToHashSet(StringComparer.Ordinal);
        RefreshResults();
        StatusText.Text="已展开同作品版本，可按大小、发布时间或做种数比较；状态只对应各自资源。";
        ResultsList.Focus();
    }
    private async void RetryClick(object sender,RoutedEventArgs e)=>await RetryFailedAsync();
    public Task RetryFailedAsync()=>RunSearchCoreAsync(true);
    private void SourceHealthClick(object sender,RoutedEventArgs e)
    {
        var dialog=new WorkbenchDialog(this,"来源状态",850,480);var list=new ListBox();dialog.Body.Children.Add(list);
        void Refresh()=>list.ItemsSource=sourceReports.Values.Select(r=>r.Summary+(r.RetryAfter is {} time?" · 可重试时间 "+time.ToLocalTime().ToString("HH:mm:ss"):"")).ToArray();
        var refresh=new Button{Content="刷新状态",Margin=new(0,0,8,0)};refresh.Click+=(_,_)=>Refresh();dialog.Tools.Children.Add(refresh);
        var copy=new Button{Content="复制诊断",Margin=new(0,0,8,0)};copy.Click+=(_,_)=>CopyText(DiagnosticReport.Build(sourceReports.Values),"已复制不含个人信息的诊断。");dialog.Tools.Children.Add(copy);
        var retry=new Button{Content="只重试失败来源",IsEnabled=searchCancel is null&&sourceReports.Values.Any(r=>r.Error is not null)};
        retry.Click+=async(_,_)=>{dialog.Close();await RetryFailedAsync();};dialog.Tools.Children.Add(retry);Refresh();dialog.ShowDialog();
    }
    private void UpdateWorkbenchSelection(SearchResult? row)
    {
        FavoriteButton.IsEnabled=row is not null&&library is not null;FavoriteButton.Content=row is not null&&library?.IsFavorite(row)==true?"取消收藏":"收藏资源";
        CopyInfoButton.IsEnabled=row is not null;
        var count=row is null?0:Versions(row).Count;VersionsButton.IsEnabled=count>1;VersionsButton.Content=count>1?$"展开 {count} 个版本":"暂无可比较版本";
        VersionsButton.Visibility=count>1&&expandedVersions is null?Visibility.Visible:Visibility.Collapsed;
    }
}
