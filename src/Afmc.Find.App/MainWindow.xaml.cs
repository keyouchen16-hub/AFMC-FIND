using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Afmc.Find.Core;

namespace Afmc.Find.App;

public partial class MainWindow : Window
{
    private readonly SearchService service;
    private readonly SettingsStore settings;
    private readonly DownloadLauncher launcher;
    private readonly EmbeddedEngine engine;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? searchCancel;
    private readonly ObservableCollection<SearchResult> visible = [];
    private List<SearchResult> all = [];
    private List<SourceConfig> sources;
    private readonly List<string> reports = [];
    private long generation;
    private int sort;
    private bool sortAscending;
    private System.Windows.Controls.Primitives.ScrollBar? resultVerticalBar;
    private string rankedQuery = "";
    private bool compact;
    private GlassBackdrop? backdrop;
    private bool opening;
    private bool configHealthy = true;
    private Preferences preferences=new();
    private TrayController? tray;
    private bool exiting;
    private PlayerWindow? playerWindow;
    private bool waitingForPlayer;
    private readonly PlaybackHealth playbackHealth = new();
    private CancellationTokenSource? healthCancellation;
    private Task healthOperation = Task.CompletedTask;
    private long healthStartedGeneration = -1;
    private Dictionary<string, int>? stableHealthOrder;
    private void RefreshHealthResults()
    {
        // Playback evidence must take effect immediately, not require a manual refresh.
        stableHealthOrder = null;
        RefreshResults();
    }
    private void ApplyHealthOrder(object sender, RoutedEventArgs e) { stableHealthOrder=null; RefreshResults(); }
    private void VerifiedPlaybackChanged(object sender,RoutedEventArgs e) { if(IsLoaded) RefreshResults(); }
    private void StartHealthProbe(long current, bool early)
    {
        if (healthStartedGeneration==current || playerWindow?.IsVisible==true || searchCancel?.IsCancellationRequested==true) return;
        var candidates=PlaybackHealth.Candidates(filteredResults.Where(r=>r.ShareUrl is null && SearchQuality.Category(r) is "影视" or "动漫")
            .Where(r=>playbackHealth.Get(r,DateTimeOffset.UtcNow).Label=="未验证播放"));
        if(candidates.Length==0 || early && candidates.Length<6) return;
        healthStartedGeneration=current;
        healthCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        healthOperation=ProbeResultsAsync(healthOperation,candidates,current,healthCancellation);
        RefreshResults();
    }
    private readonly PreparedPlaybackCache preparedPlayback = new();
    private readonly System.Windows.Threading.DispatcherTimer preparedTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private async Task ReleasePreparedAsync(DateTimeOffset? expiredAt = null)
    {
        if(await preparedPlayback.ClearAsync(expiredAt) is {} row) {
            playbackHealth.Forget(row);
            if(!lifetime.IsCancellationRequested) RefreshResults();
        }
    }
    private async Task ProbeResultsAsync(Task previous, SearchResult[] rows, long expected, CancellationTokenSource cancel)
    {
        try {
            await previous;
            var ct = cancel.Token; ct.ThrowIfCancellationRequested();
            if (rows.Length == 0 || !System.IO.File.Exists(System.IO.Path.Combine(PlayerRuntime.DefaultDirectory,"libvlc.dll"))) return;
            var runtime = await PlayerRuntime.EnsureAsync(null,ct);
            LibVLCSharp.Shared.Core.Initialize(runtime);
            using var decoder = new LibVLCSharp.Shared.LibVLC("--no-video-title-show", "--no-lua", "--no-metadata-network-access");
            var root = System.IO.Path.Combine(settings.DirectoryPath,"HealthProbeCache");
            TorrentPlayback.RecoverCache(root);
            cancel.CancelAfter(TimeSpan.FromMinutes(3));
            foreach (var batch in rows.Chunk(3)) {
            ct.ThrowIfCancellationRequested();
            try {
            var winner = await PlaybackRace.RunAsync(batch.Select(row => new PlaybackCandidate(row.Title,
                token => launcher.PreparePlaybackAsync(row,token), evidence => {
                    if (expected != generation || ct.IsCancellationRequested || evidence == PlaybackEvidence.Playable) return;
                    playbackHealth.Record(row,evidence,DateTimeOffset.UtcNow); RefreshHealthResults();
                }, PreparedPlaybackCache.Key(row))).ToArray(), () => new TorrentPlayback(root, maximumDownloadRate: 512 * 1024), decoder, _ => { }, ct);
            if(ct.IsCancellationRequested || expected != generation || winner.ResourceKey is null) {
                await winner.Session.DisposeAsync(); break;
            }
            var readyRow=batch.First(r=>PreparedPlaybackCache.Key(r)==winner.ResourceKey);
            await preparedPlayback.StoreAsync(readyRow,winner,DateTimeOffset.UtcNow);
            playbackHealth.Record(readyRow,PlaybackEvidence.Playable,DateTimeOffset.UtcNow);
            RefreshHealthResults();
            break; // Keep one ready session, bounded to two minutes, for immediate handoff.
            } catch (System.IO.IOException) { /* An unavailable batch must not prevent checking the next versions. */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when(e is not OutOfMemoryException) { /* Unknown stays unknown; playback remains available. */ }
        finally { if (healthCancellation == cancel) healthCancellation = null; cancel.Dispose(); if(expected==generation&&!lifetime.IsCancellationRequested) RefreshResults(); }
    }
    private async void PlayerEntryClick(object sender, RoutedEventArgs e) {
        healthCancellation?.Cancel(); ShowPlayer();
        await healthOperation; await ReleasePreparedAsync();
    }
    private PlayerWindow ShowPlayer()
    {
        if (playerWindow is null)
        {
            playerWindow = new PlayerWindow(settings.DirectoryPath) { Owner = this };
            playerWindow.Closed += (_, _) => playerWindow = null;
        }
        playerWindow.Show();
        if (playerWindow.WindowState == WindowState.Minimized) playerWindow.WindowState = WindowState.Normal;
        playerWindow.Activate(); return playerWindow;
    }
    private async void PlayClick(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult result || result.ShareUrl is not null) return;
        var targetPlayer = ShowPlayer();
        healthCancellation?.Cancel(); await healthOperation;
        if(preparedPlayback.Take(result,DateTimeOffset.UtcNow) is {} ready) {
            await targetPlayer.OpenWinnerAsync(ready); return;
        }
        await ReleasePreparedAsync();
        var candidates = PlaybackCandidates.Select(result, filteredResults, rankedQuery);
        await targetPlayer.OpenCandidatesAsync(candidates.Select(row =>
            new PlaybackCandidate(row.Title, ct => launcher.PreparePlaybackAsync(row, ct), evidence => {
                playbackHealth.Record(row, evidence, DateTimeOffset.UtcNow);
                RefreshHealthResults();
            })).ToArray());
    }
    private bool SelectClickedResult(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin ||
            ItemsControl.ContainerFromElement(ResultsList, origin) is not ListBoxItem row) return false;
        ResultsList.SelectedItem = row.DataContext;
        return true;
    }

    private void ResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !SelectClickedResult(e)) return;
        e.Handled = true;
        if(PlayButton.IsEnabled && PlayButton.Visibility==Visibility.Visible) PlayClick(sender, e);
        else if(OpenButton.IsEnabled) OpenClick(sender,e);
    }

    private void ResultRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!SelectClickedResult(e)) return;
        var menu = new ContextMenu();
        foreach (var button in new[] { PlayButton, OpenButton, CopyButton, PageButton }.Where(b=>b!=PlayButton||b.Visibility==Visibility.Visible))
        {
            var item = new MenuItem { Header = button.Content, IsEnabled = button.IsEnabled };
            item.Click += (_, _) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            menu.Items.Add(item);
        }
        menu.PlacementTarget = ResultsList;
        menu.IsOpen = true;
        e.Handled = true;
    }
    public bool HasTrayIcon=>tray is not null;
    public MainWindow() : this(new SettingsStore(), new SearchService(),true) { }
    public MainWindow(SettingsStore store, SearchService searchService,bool enableTray=false)
    {
        InitializeComponent(); Title=ProductInfo.Title; settings = store; service = searchService; launcher = new(service, settings.DirectoryPath);
        preparedTimer.Tick += async (_,_) => await ReleasePreparedAsync(DateTimeOffset.UtcNow);
        preparedTimer.Start();
        Closed += (_,_) => preparedTimer.Stop();
        engine=new EmbeddedEngine(settings.DirectoryPath);service.SourceResolver=engine.ResolveAsync;
        try { preferences=Preferences.Load(settings.DirectoryPath); } catch(Exception e) when(e is not OutOfMemoryException) { StatusText.Text="基础设置读取失败，原文件已保留。"; }
        UiMotion.Enabled=!preferences.ReduceMotion;
        UpdateDetailLayout(false);
        UpdateSortLabel();
        try { sources = settings.Load(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { sources = [SourceConfig.Archive()]; configHealthy = false; StatusText.Text = "配置读取失败；原文件已保留，暂用公开源。请检查用户目录的 sources.json。"; }
        InitializeWorkbench(); ResultsList.ItemsSource = visible; RefreshSources(); Loaded += (_, _) => QueryBox.Focus();
        ResultsList.Loaded += (_, _) => InitializeColumnAlignment();
        SourceInitialized += (_,_) => { backdrop=new GlassBackdrop(this,on => MaterialText.Text=on?"系统磨砂材质":"深色兼容模式");backdrop.Attach(); UpdateExit.Attach(this); };
        Loaded += (_,_) => {if(enableTray&&tray is null)Dispatcher.BeginInvoke(() => {if(tray is not null||exiting)return;try {tray=new TrayController(this);} catch(Exception e) when(e is not OutOfMemoryException){StatusText.Text="托盘初始化失败，关闭窗口将退出。";} },System.Windows.Threading.DispatcherPriority.ApplicationIdle);};
        StateChanged += (_,_) => {if(WindowState==WindowState.Minimized&&preferences.MinimizeToTray&&tray is not null)Hide();};
        Closed += (_,_)=>backdrop?.Dispose();
        Closing += (_, e) => { if(!exiting&&tray is not null&&preferences.CloseToTray){e.Cancel=true;Hide();return;} searchCancel?.Cancel();lifetime.Cancel(); };
        Closing += async (_, e) => {
            if(e.Cancel || (playerWindow is null && healthOperation.IsCompleted && !preparedPlayback.HasPending)) return;
            e.Cancel = true;
            if(waitingForPlayer) return;
            waitingForPlayer = true;
            await System.Windows.Threading.Dispatcher.Yield();
            await healthOperation;
            await ReleasePreparedAsync();
            if (playerWindow is not null) await playerWindow.ClosePlayerAsync();
            waitingForPlayer = false; exiting = true; Close();
        };
        Closed += (_,_)=>tray?.Dispose();
        Closed += (_, _) => service.Dispose();
        Closed += (_, _) => engine.Dispose();
    }
    public void RestoreFromTray(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
    private void InitializeColumnAlignment()
    {
        if(resultVerticalBar is not null)return;
        System.Windows.Controls.Primitives.ScrollBar? FindBar(DependencyObject node)
        {
            if(node is System.Windows.Controls.Primitives.ScrollBar {Orientation:Orientation.Vertical} bar)return bar;
            for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
                if(FindBar(System.Windows.Media.VisualTreeHelper.GetChild(node,i)) is {} child)return child;
            return null;
        }
        resultVerticalBar=FindBar(ResultsList);
        if(resultVerticalBar is null)return;
        void Align()=>ResultHeaders.Margin=new Thickness(12,0,12+resultVerticalBar.ActualWidth,0);
        resultVerticalBar.SizeChanged+=(_,_)=>Align();
        Align();
    }
    public void ExitApplication(){exiting=true;Close();}
    public void OpenPreferences()
    {
        RestoreFromTray();var dialog=new PreferencesWindow(preferences){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        try {dialog.Value.Save(settings.DirectoryPath);preferences=dialog.Value;UiMotion.Enabled=!preferences.ReduceMotion;UpdateSortLabel();RefreshResults();StatusText.Text="设置已保存。";}
        catch(Exception e) when(e is not OutOfMemoryException){StatusText.Text="设置保存失败，请检查目录权限；当前设置保持不变。";}
    }
    private void FeedbackClick(object sender,RoutedEventArgs e)=>new FeedbackWindow(DiagnosticReport.Build(sourceReports.Values)) {Owner=this}.ShowDialog();
    private void PreferencesClick(object sender,RoutedEventArgs e)=>OpenPreferences();
    private void UpdateSortLabel()
    {
        var arrow = sortAscending || sort == 4 ? " ↑" : " ↓";
        SortButton.Content = "排序：" + new[] { "综合推荐", "做种数", "发布时间", "相关度", "名称", "文件大小" }[sort] + arrow;
        SizeSortHeader.Content = "文件大小" + (sort == 5 ? arrow : "");
        PublishedSortHeader.Content = "发布时间" + (sort == 2 ? arrow : "");
        SeedsSortHeader.Content = "做种" + (sort == 1 ? arrow : "");
    }
    private void MinimizeWindowClick(object sender,RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindowClick(object sender,RoutedEventArgs e) { if(WindowState==WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindowClick(object sender,RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void WindowKeyDown(object sender,KeyEventArgs e) {
        if(e.Key==Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){ShowSearch(this,new());QueryBox.Focus();QueryBox.SelectAll();e.Handled=true;}
        else if(e.Key==Key.Escape && DetailPanel.Visibility==Visibility.Visible){CloseDetailClick(sender,new());e.Handled=true;}
    }
    private void DensityClick(object sender,RoutedEventArgs e)
    {
        compact=!compact;DensityButton.Content=compact?"紧凑密度":"标准密度";
        var style=new Style(typeof(ListBoxItem),(Style)FindResource("ResultItemStyle"));
        style.Setters.Add(new Setter(Control.PaddingProperty,compact?new Thickness(12,4,12,4):new Thickness(12,10,12,10)));
        ResultsList.ItemContainerStyle=style;
    }
    private void ShowSearch(object sender, RoutedEventArgs e) { PageTitle.Text="聚合搜索"; SearchView.Visibility = Visibility.Visible; SourcesView.Visibility = Visibility.Collapsed; SearchNav.Tag="selected";SourcesNav.Tag=null; QueryBox.Focus(); }
    private void ShowSources(object sender, RoutedEventArgs e) { PageTitle.Text="搜索源"; SearchView.Visibility = Visibility.Collapsed; SourcesView.Visibility = Visibility.Visible; SourcesNav.Tag="selected";SearchNav.Tag=null; }
    private async void QueryKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await RunSearchAsync(); } }
    private async void SearchClick(object sender, RoutedEventArgs e) => await RunSearchAsync();
    public Task RunSearchAsync()=>RunSearchCoreAsync(false);
    private SearchRanking searchRanking=new(DateTimeOffset.UtcNow);
    private async Task RunSearchCoreAsync(bool retry)
    {
        var query = retry?rankedQuery:QueryBox.Text.Trim(); if (query.Length == 0) { StatusText.Text = "请输入关键词。"; QueryBox.Focus(); return; }
        var enabled = sources.Where(s => s.Enabled && s.Kind != "Website" && (!retry||sourceReports.TryGetValue(s.Id,out var oldReport)&&oldReport.Error is not null)).ToArray(); if (enabled.Length == 0) { StatusText.Text = "请先启用至少一个可聚合搜索源。"; ShowSources(this, new()); return; }
        rankedQuery = query; favoritesView=false;FavoritesButton.Content="收藏"; if(!retry)RecordHistory(query);
        if(!retry)searchRanking=new(DateTimeOffset.UtcNow);
        healthCancellation?.Cancel();
        searchCancel?.Cancel(); var current = ++generation;
        stableHealthOrder=null;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); searchCancel = cancel;
        if(!retry){expandedVersions=null;all.Clear();filteredResults.Clear();resourceGroups=[];visible.Clear(); reports.Clear();sourceReports.Clear(); SourceStatus.Text = "";} RetryButton.IsEnabled=false; RetryButton.Visibility=Visibility.Collapsed; CountText.Text = "搜索中…"; EmptyText.Text = "正在查询真实搜索源…"; EmptyText.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true; CancelButton.Visibility=Visibility.Visible; SearchButton.Content = "重新搜索"; StatusText.Text = $"正在搜索 {enabled.Length} 个来源…";
        try
        {
            await ReleasePreparedAsync();
            cancel.Token.ThrowIfCancellationRequested();
            void Receive(SourceReport report) => Dispatcher.InvokeAsync(() =>
            {
                if (current != generation || cancel.IsCancellationRequested) return;
                if(retry&&report.Error is not null&&sourceReports.TryGetValue(report.SourceId,out var previousReport))report=report with {Results=ResultMerger.Merge(previousReport.Results.Concat(report.Results))};
                sourceReports[report.SourceId]=report;
                all = ResultMerger.Merge(sourceReports.Values.SelectMany(r=>r.Results));
                UpdateSourceSummary(); RefreshResults();
                StartHealthProbe(current, early:true);
            });
            var summary = await service.SearchAsync(query, enabled, Receive, cancel.Token, progress:Receive);
            // Flush queued provider callbacks before deciding the final empty-state text.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (current != generation) return;
            RefreshResults();
            StatusText.Text = $"搜索完成 · {all.Count} 条结果 · {sourceReports.Values.Count(r=>r.Error is null)} 源成功 / {sourceReports.Values.Count(r=>r.Error is not null)} 源失败";
            StartHealthProbe(current, early:false);
            if (all.Count == 0) EmptyText.Text = summary.Succeeded == 0 ? "所有搜索源均失败，请检查连接与配置。" : "没有找到可下载的匹配资源。试试其他关键词或添加搜索源。";
        }
        catch (OperationCanceledException) { if (current == generation) { StatusText.Text = "已取消搜索，保留已返回结果。"; EmptyText.Text = "搜索已取消。"; } }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (current == generation) { StatusText.Text = Links.Error(ex); EmptyText.Text = StatusText.Text; } }
        finally
        {
            if (current == generation) { searchCancel = null; RetryButton.IsEnabled=sourceReports.Values.Any(r=>r.Error is not null); RetryButton.Visibility=RetryButton.IsEnabled?Visibility.Visible:Visibility.Collapsed; CancelButton.IsEnabled = false; CancelButton.Visibility=Visibility.Collapsed; SearchButton.Content = "搜索资源"; UpdateCountLabel(); }
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => searchCancel?.Cancel();
    private void RefreshResults()
    {
        var previous = ResultsList.SelectedItem as SearchResult;
        var displayQuery=favoritesView?"":rankedQuery;
        var rows=ResultPresentation.WithCovers(favoritesView?library?.Favorites??[]:all);
        queryAliases=SearchQuality.Aliases(displayQuery,rows);
        var now=DateTimeOffset.UtcNow;
        SearchRanking.Match Match(SearchResult r)=>searchRanking.Evaluate(queryAliases,r);
        var filtered = rows.Where(r => (favoritesView||Match(r).Tier<3)&&r.Title.Contains(FilterBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)&&resultFilter.Matches(r)&&ResourceSelection.Matches(r,CategoryBox.SelectedIndex,ResolutionBox.SelectedIndex)).Select(r=>
        {
            var match=Match(r);
            return r with {PlaybackLabel=ResourceDisplay.PlaybackLabel(r,playbackHealth,now),MatchReason=playbackHealth.Get(r,now).Label+" · "+(match.Query!=displayQuery?"双语条目别名匹配 · ":"")+match.Explanation+(r.MetadataConflict?" · 来源信息有差异":"")};
        });
        filtered = sort switch {
            1 => OrderKnown(filtered, r => r.Seeders).ThenByDescending(r => SiteSources.Rank(rankedQuery,r)).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            2 => OrderKnown(filtered, r => r.Published).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            5 => OrderKnown(filtered, r => r.Size).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase),
            3 => filtered.OrderBy(r=>Match(r).Tier).ThenByDescending(r=>Match(r).NameScore),
            4 => filtered.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => SearchRanking.Order(filtered,Match,playbackHealth,now)
        };
        filteredResults=filtered.ToList();
        var displayedResults=VerifiedPlaybackOnly.IsChecked==true?filteredResults.Where(r=>playbackHealth.Tier(r,now)==0).ToList():filteredResults;
        resourceGroups=ResourceGroups.Build(displayedResults);
        var display=expandedVersions is null?resourceGroups.Select(g=>g.Representative with {VersionLabel=g.Versions.Count>1?$"{g.Versions.Count} 个版本":""}):displayedResults.Where(r=>expandedVersions.Contains(PersonalLibrary.Key(r)));
        if (sort==0 && stableHealthOrder is not null) display=display.OrderBy(r=>Match(r).Tier).ThenBy(r=>stableHealthOrder.GetValueOrDefault(PersonalLibrary.Key(r),int.MaxValue));
        ApplyHealthOrderButton.Visibility=sort==0 && stableHealthOrder is not null?Visibility.Visible:Visibility.Collapsed;
        refreshingResults=true;
        try {
            visible.Clear(); foreach(var item in display)visible.Add(item);
            ResultsList.SelectedItem=previous is null?null:visible.FirstOrDefault(r=>PersonalLibrary.Key(r)==PersonalLibrary.Key(previous));
        } finally {refreshingResults=false;}
        BackToGroupsButton.Visibility=expandedVersions is null?Visibility.Collapsed:Visibility.Visible;
        var playableCount = filteredResults.Count(r=>playbackHealth.Tier(r,now)==0);
        AvailabilityText.Visibility=filteredResults.Any(r=>ResourceDisplay.IsVideo(r)&&r.ShareUrl is null)?Visibility.Visible:Visibility.Collapsed;
        AvailabilityText.Text = playableCount>0 ? $"近期起播成功 {playableCount} 条" : "暂无已验证起播资源";
        if(healthCancellation is not null) AvailabilityText.Text += " · 检测中";
        UpdateCountLabel(); EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if(VerifiedPlaybackOnly.IsChecked==true)CountText.Text=$"{displayedResults.Count} 条已验证 · 共 {filteredResults.Count} 条";
        CountText.ToolTip=favoritesView?null:$"来源返回 {all.Count} 条；已排除 {rows.Count(r=>Match(r).Tier==3)} 条名称未匹配的记录。";
        if(favoritesView&&visible.Count==0)EmptyText.Text="暂无符合筛选的收藏。选择资源后点击收藏即可保存到本机。";
        else if (visible.Count == 0 && all.Count > 0) EmptyText.Text = "当前条件没有匹配结果。试试全部类型、不限分辨率，或清除筛选。";
        if(VerifiedPlaybackOnly.IsChecked==true && visible.Count==0)EmptyText.Text=healthCancellation is not null?"正在检测，暂无已验证起播资源。可关闭此选项查看全部结果。":"暂无已验证起播资源。可关闭此选项查看未验证结果并尝试播放。";
        ResultSelected(this,new SelectionChangedEventArgs(SelectorEvent(),Array.Empty<object>(),Array.Empty<object>()));
    }
    private static RoutedEvent SelectorEvent()=>System.Windows.Controls.Primitives.Selector.SelectionChangedEvent;
    private void FilterChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshResults(); }
    private void IntentChanged(object sender, SelectionChangedEventArgs e)
    {
        if(ResolutionBox is null)return;
        ResolutionBox.IsEnabled=CategoryBox.SelectedIndex==1;
        if(IsLoaded)RefreshResults();
    }
    private IOrderedEnumerable<SearchResult> OrderKnown<T>(IEnumerable<SearchResult> rows, Func<SearchResult,T?> key) where T : struct
    {
        var knownFirst = rows.OrderBy(r => !key(r).HasValue);
        return sortAscending ? knownFirst.ThenBy(key) : knownFirst.ThenByDescending(key);
    }
    private void HeaderSortClick(object sender, RoutedEventArgs e)
    {
        var next = int.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
        sortAscending = sort == next && !sortAscending;
        stableHealthOrder=null; sort = next; UpdateSortLabel(); RefreshResults();
    }
    private void SortClick(object sender, RoutedEventArgs e) { stableHealthOrder=null; sort = (sort + 1) % 6;sortAscending=false;UpdateSortLabel();RefreshResults(); }
    private void ResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if(refreshingResults)return;
        var r = ResultsList.SelectedItem as SearchResult;
        UpdateDetailLayout(r is not null,true);
        DetailTitle.Text = r?.ResultName ?? "选择一条结果"; DetailFileName.Text = r?.Title ?? ""; DetailCategory.Text = r is null?"":SearchQuality.Category(r);
        UpdateWorkbenchSelection(r);
        DetailFields.Text = r is null ? "" : $"规格　{SearchQuality.TechnicalSummary(r)}\n大小　{r.SizeText}\n做种　{r.SeedsText}\n发布　{r.PublishedText}"+(r.ThumbnailUrl is null?"":"\n图片　"+r.ThumbnailLabel);
        DetailSourceEvidence.Text = r is null ? "" : r.Source+"\n"+SourceEvidence(r);
        var cloud=CloudShares.Provider(r?.ShareUrl) is not null;
        OpenButton.Content=cloud?"打开网盘":"用下载软件打开";
        CopyButton.Content=cloud?"复制网盘链接":"复制磁力链接";
        HandoffHint.Text=cloud?"来源提供的分享链接，有效性以网盘页面为准":"调用下载软件，不代表下载完成";
        if(cloud)DetailFields.Text="类型　"+r!.ResourceType+"\n"+DetailFields.Text;
        OpenButton.IsEnabled = r is not null && !opening; CopyButton.IsEnabled = cloud||Links.MagnetHash(r?.Magnet) is not null; PageButton.IsEnabled = r?.DetailsUrl is not null;
        var video=r is not null && ResourceDisplay.IsVideo(r) && !cloud;
        PlayButton.Visibility=video?Visibility.Visible:Visibility.Collapsed;
        PlayButton.IsEnabled = video && r is not null && (Links.MagnetHash(r.Magnet) is not null || r.TorrentUrl is not null || r.ArchiveId is not null);
        PlayButton.Content=r is not null&&playbackHealth.Tier(r,DateTimeOffset.UtcNow)==0?"播放":"试播";
        OpenButton.Style=(Style)FindResource("CompactButton");
        DetailPlaybackState.Text=r is null?"":video?playbackHealth.Get(r,DateTimeOffset.UtcNow).Label:"下载可用性以下载软件或来源页面为准";
        DetailPlaybackState.ToolTip=video?"近期起播成功不保证全片持续流畅；来源报告做种数不代表已验证播放。":null;
    }
    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult r || opening) return;
        if(CloudShares.Provider(r.ShareUrl)is not null)
        {try{DownloadLauncher.OpenPage(r.ShareUrl!);StatusText.Text="已请求用浏览器打开网盘分享，请在网页中确认资源。";}catch(Exception ex)when(ex is not OutOfMemoryException){StatusText.Text=Links.Error(ex);}return;}
        opening = true; OpenButton.IsEnabled = false; StatusText.Text = "正在获取并校验种子…";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(60));
        try { var target = await launcher.PrepareAsync(r, cancel.Token); cancel.Token.ThrowIfCancellationRequested(); DownloadLauncher.OpenPrepared(target, settings.DirectoryPath); StatusText.Text = "已请求用默认下载软件打开；请在下载软件中确认。"; }
        catch (Win32Exception) { StatusText.Text = "无法打开默认下载软件。请在 Windows「默认应用」中关联 magnet / .torrent，或复制磁力链接。"; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { StatusText.Text = Links.Error(ex); }
        finally { opening = false; OpenButton.IsEnabled = ResultsList.SelectedItem is not null; }
    }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult r) return;
        var cloud=CloudShares.Provider(r.ShareUrl)is not null;var link=cloud?r.ShareUrl:Links.MagnetHash(r.Magnet)is not null?r.Magnet:null;if(link is null)return;
        try { Clipboard.SetText(link); StatusText.Text = cloud?"已复制网盘链接。":"已复制磁力链接。"; } catch (System.Runtime.InteropServices.ExternalException) { StatusText.Text = "剪贴板正被其他程序使用，请稍后重试。"; }
    }
    private void PageClick(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResult { DetailsUrl: { } url }) return;
        try { DownloadLauncher.OpenPage(url); } catch (Exception ex) when (ex is not OutOfMemoryException) { StatusText.Text = Links.Error(ex); }
    }
    private void RefreshSources() => ApplySourceFilter();
    private bool SaveSources(List<SourceConfig> next)
    {
        if (!configHealthy) { ConnectionStatus.Text = "原配置读取失败，为保护数据已禁止覆盖。请先检查用户目录 sources.json。"; return false; }
        try { settings.Save(next); sources = next; RefreshSources(); searchCancel?.Cancel(); ConnectionStatus.Text = "已保存配置。"; return true; }
        catch (Exception e) when (e is not OutOfMemoryException) { ConnectionStatus.Text = "保存失败，原配置已保留。请检查目录权限。"; return false; }
    }
    private void AddSource(object sender, RoutedEventArgs e)
    {
        if(!configHealthy){ConnectionStatus.Text="原配置读取失败，暂不能添加来源。";return;}
        SourceWebsiteBox.Focus();SourceWebsiteBox.SelectAll();ConnectionStatus.Text="粘贴网址并点击识别，验证后确认添加；也可以切换已适配目录。";
    }
    private void EditSource(object sender, RoutedEventArgs e)
    {
        if (SourceList.SelectedItem is not SourceConfig s) return;
        if(s.Kind=="AutoWeb")
        {var discovered=new WebsiteDiscoveryWindow(engine,s){Owner=this};if(discovered.ShowDialog()==true&&discovered.Result is {} found)SaveSources(sources.Select(x=>x.Id==s.Id?found with{Id=s.Id,Enabled=s.Enabled}:x).ToList());return;}
        if(s.EngineIndexerId.Length>0)
        {
            var configured=new EngineSourcesWindow(engine,s.EngineIndexerId,configureOnly:true){Owner=this};
            if(configured.ShowDialog()==true&&configured.Result is {} updatedSource)SaveSources(sources.Select(x=>x.Id==s.Id?updatedSource with{Id=s.Id,Enabled=s.Enabled}:x).ToList());
            return;
        }
        if (s.Kind != "Torznab") { ConnectionStatus.Text = s.Description.Length>0?s.Description:"内置公开源无需配置；可用启用 / 停用切换。"; return; }
        var dialog = new SourceDialog(s) { Owner = this }; if (dialog.ShowDialog() == true && dialog.Result is { } updated) SaveSources(sources.Select(x => x.Id == s.Id ? updated : x).ToList());
    }
    private void ToggleSource(object sender, RoutedEventArgs e) { if (SourceList.SelectedItem is SourceConfig s) { if(s.Kind=="Website") { ConnectionStatus.Text="这是站点入口，尚不能聚合结果。"+s.Description; return; } SaveSources(sources.Select(x => x.Id == s.Id ? x with { Enabled = !x.Enabled } : x).ToList()); } }
    private void OpenSourceSite(object sender,RoutedEventArgs e)
    {
        if(SourceList.SelectedItem is not SourceConfig s) return;
        try { DownloadLauncher.OpenPage(s.Endpoint); } catch(Exception ex) when(ex is not OutOfMemoryException) { ConnectionStatus.Text=Links.Error(ex); }
    }
    private void RemoveSource(object sender, RoutedEventArgs e)
    {
        if (SourceList.SelectedItem is not SourceConfig s) return;
        if (s.Kind is not ("Torznab" or "AutoWeb")) { ConnectionStatus.Text = "内置来源保留状态记录；可聚合源可停用。"; return; }
        if (MessageBox.Show(this, $"移除搜索源“{s.Name}”？", "移除配置", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) SaveSources(sources.Where(x => x.Id != s.Id).ToList());
    }
    private async void TestSource(object sender, RoutedEventArgs e)
    {
        if (SourceList.SelectedItem is not SourceConfig s) return;
        TestButton.IsEnabled = false; ConnectionStatus.Text = $"正在测试 {s.Name}…";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(30));
        try { if(s.EngineIndexerId.Length>0){await engine.TestAsync(s.EngineIndexerId,cancel.Token);ConnectionStatus.Text=$"{s.Name}：真实来源测试通过";}else ConnectionStatus.Text = $"{s.Name}：{await service.TestSourceAsync(s, cancel.Token)}"; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ConnectionStatus.Text = $"{s.Name}：{Links.Error(ex)}"; }
        finally { TestButton.IsEnabled = true; sourceChecks[s.Id]=ConnectionStatus.Text+ $" · {DateTime.Now:HH:mm}";RefreshSources(); }
    }
}

