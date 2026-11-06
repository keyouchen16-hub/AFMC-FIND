using System.Windows;
using System.Windows.Controls;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public partial class MainWindow
{
    private List<SearchResult> filteredResults = [];
    private IReadOnlyList<ResourceGroup> resourceGroups = [];
    private HashSet<string>? expandedVersions;
    private bool refreshingResults;

    private void UpdateCountLabel()
    {
        CountText.Text=favoritesView?$"{filteredResults.Count} / {library?.Favorites.Count??0} 条收藏":
            expandedVersions is not null?$"{visible.Count} 个版本 · 共 {filteredResults.Count} 条结果":
            resourceGroups.Count<filteredResults.Count?$"{resourceGroups.Count} 项资源 · {filteredResults.Count} 个版本":$"{filteredResults.Count} / {all.Count} 条结果";
    }
    private void UpdateDetailLayout(bool show, bool animate = false)
    {
        var wasVisible=DetailPanel.Visibility==Visibility.Visible;
        DetailPanel.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        DetailColumn.Width=new GridLength(show?300:0);
        DetailGap.Width=new GridLength(show?20:0);
        if(show && !wasVisible && animate) UiMotion.Reveal(DetailPanel);
    }
    private void CloseDetailClick(object sender,RoutedEventArgs e)
    {
        ResultsList.SelectedItem=null;
        UpdateDetailLayout(false);
        ResultsList.Focus();
    }
    private void MoreActionsClick(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var button in new[]{CopyButton,PageButton,CopyInfoButton}) {
            var item=new MenuItem{Header=button.Content,IsEnabled=button.IsEnabled};
            item.Click+=(_,_)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            menu.Items.Add(item);
        }
        menu.PlacementTarget=(Button)sender;
        menu.IsOpen=true;
    }
    private void BackToGroupsClick(object sender,RoutedEventArgs e)
    {
        expandedVersions=null;
        RefreshResults();
        ResultsList.Focus();
    }
    private void UpdateSourceSummary()
    {
        var failed=sourceReports.Values.Count(r=>r.Error is not null);
        SourceStatus.Text=$"{sourceReports.Count} 个来源已返回"+(failed>0?$" · {failed} 个需重试":"");
        SourceStatus.ToolTip=string.Join("\n",sourceReports.Values.Select(r=>r.Summary));
    }
}
