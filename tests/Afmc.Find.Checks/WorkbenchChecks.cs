using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Afmc.Find.App;
using Afmc.Find.Core;
internal static class WorkbenchChecks
{
    private static int passed;
    private static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {yield return parent;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var child in Tree(VisualTreeHelper.GetChild(parent,i)))yield return child;}
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Snapshot(Window window,string name)
    {
        window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("docs/evidence/v0817");using var file=File.Create("docs/evidence/v0817/"+name+".png");png.Save(file);
    }
    public static int Run()
    {
        var app=new Application{Resources=new ResourceDictionary{Source=new Uri("/AFMC.Find;component/Theme.xaml",UriKind.Relative)}};
        var folder=Path.Combine(Path.GetTempPath(),"afmc-workbench-"+Guid.NewGuid().ToString("N"));var store=new SettingsStore(folder);
        var good=new SourceConfig{Id="fixture-good",Name="Good source",Endpoint="https://fixture.invalid/good"};var bad=new SourceConfig{Id="fixture-bad",Name="Retry source",Endpoint="https://fixture.invalid/bad"};
        store.Save(SiteSources.Catalog.Select(s=>s with {Enabled=false}).Concat([good,bad]));
        int goodCalls=0,badCalls=0;string? lastBadQuery=null;var service=new SearchService(new Handler(request=>
        {
            if(request.RequestUri!.AbsolutePath=="/bad")lastBadQuery=request.RequestUri.Query;
            var success=request.RequestUri!.AbsolutePath=="/good";if(success)goodCalls++;else if(++badCalls==1)return new(HttpStatusCode.BadGateway);
            var hash=new string(success?'a':'b',40);var title=success?"示例电影 · Sample Movie 2020 1080p":"Sample Movie 2020 2160p";
            return new(HttpStatusCode.OK){Content=new StringContent($"<rss xmlns:t='http://torznab.com/schemas/2015/feed'><channel><item><title>{title}</title><category>电影</category><size>2000000000</size><t:attr name='infohash' value='{hash}'/><t:attr name='magneturl' value='magnet:?xt=urn:btih:{hash}'/><t:attr name='seeders' value='4'/></item></channel></rss>")};
        }));
        var window=new MainWindow(store,service);int exit=1;
        window.Loaded+=async(_,_)=>
        {
            try
            {
                Button Button(string name)=>(Button)window.FindName(name);
                void Modal(string trigger,Action<Window> action)
                {
                    window.Dispatcher.BeginInvoke(()=>{var dialog=app.Windows.Cast<Window>().First(w=>w!=window);try{action(dialog);}finally{if(dialog.IsVisible)dialog.Close();}});
                    Click(Button(trigger));
                }
                var query=(TextBox)window.FindName("QueryBox");var results=(ListBox)window.FindName("ResultsList");query.Text="示例电影 2020";
                await window.RunSearchAsync();Check(results.Items.Count==1&&Button("RetryButton").IsEnabled,"partial failure keeps successful results and enables retry");
                Check(results.SelectedItem is null && !((FrameworkElement)window.FindName("DetailPanel")).IsVisible,"search starts with full-width results and no unsolicited selection");
                Snapshot(window,"results-unselected");
                results.SelectedIndex=0;window.UpdateLayout();
                Check(((FrameworkElement)window.FindName("DetailPanel")).IsVisible,"selecting a resource reveals its inspector");
                Click(Button("DetailCloseButton"));window.UpdateLayout();
                Check(results.SelectedItem is null && !((FrameworkElement)window.FindName("DetailPanel")).IsVisible,"closing inspector clears selection and returns list space");
                results.SelectedIndex=0;
                Check(Button("PlayButton").IsEnabled && Button("PlayerEntryButton").IsVisible,"BT result has playback action and standalone player entry");
                Check(((TextBlock)window.FindName("AvailabilityText")).Text.Contains("暂无已验证起播资源"),"search with no decoded successes explicitly reports no verified playable resources");
                Click(Button("PlayerEntryButton"));
                var playerWindow=app.Windows.OfType<PlayerWindow>().Single();
                Check(playerWindow.IsVisible && playerWindow.Title.Contains("播放器"),"standalone player opens from search workbench");
                await playerWindow.Dispatcher.InvokeAsync(playerWindow.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check(playerWindow.FindName("PlayerBrand") is null&&((TextBlock)playerWindow.FindName("PlayerDragArea")).Text.Length==0,"player has no upper-left brand/version label");
                var idleLogo=(Image)playerWindow.FindName("PlayerIdleLogo");
                Check(idleLogo.Source is not null&&idleLogo.IsVisible,"idle player displays embedded AFMC logo");
                var closeButton=Tree(playerWindow).OfType<Button>().Single(button =>
                    System.Windows.Automation.AutomationProperties.GetName(button)=="关闭窗口");
                Click(closeButton);
                await playerWindow.ClosePlayerAsync();
                Check(window.IsVisible && !window.Dispatcher.HasShutdownStarted,"closing empty player via caption preserves search window");
                Check(!playerWindow.IsVisible,"standalone player closes without closing search workbench");
                results.UpdateLayout();
                var row=(ListBoxItem)results.ItemContainerGenerator.ContainerFromIndex(0);
                var doubleClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left)
                    { RoutedEvent=Control.MouseDoubleClickEvent, Source=row };
                results.RaiseEvent(doubleClick);
                var fromRow=app.Windows.OfType<PlayerWindow>().Single();
                Check(doubleClick.Handled && fromRow.IsVisible,"double click on result opens its player");
                await fromRow.ClosePlayerAsync();
                var rightClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Right)
                    { RoutedEvent=UIElement.PreviewMouseRightButtonUpEvent, Source=row };
                results.RaiseEvent(rightClick);
                await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var menu=PresentationSource.CurrentSources.Cast<PresentationSource>().Where(s=>s.RootVisual!=null)
                    .SelectMany(s=>Tree(s.RootVisual)).OfType<ContextMenu>().Single(m=>m.IsOpen);
                var playAction=menu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,"试播"));
                Check(rightClick.Handled && playAction.IsEnabled,"right click on result offers play action");
                menu.IsOpen=false;
                playAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var fromMenu=app.Windows.OfType<PlayerWindow>().Single();
                Check(fromMenu.IsVisible,"context menu starts selected result in the player");
                await fromMenu.ClosePlayerAsync();
                query.Text="changed text that must not be retried";
                await window.RetryFailedAsync();Check(goodCalls==1&&badCalls==2&&results.Items.Count==1,"retry fetches only failed source and groups confirmed versions");
                results.SelectedIndex=0;
                Check(results.SelectedItem is SearchResult {VersionLabel:"2 个版本"},"group discloses both distinct versions");
                await Task.Delay(180);Snapshot(window,"resource-group");
                window.UpdateLayout();
                Check(Button("CopyButton").IsVisible&&Button("CopyButton").IsEnabled&&Math.Abs(Button("CopyButton").ActualWidth-Button("OpenButton").ActualWidth)<1,"download and copy share equal visible widths");
                Check(window.FindName("MoreActionsButton") is null && Button("PageButton").Visibility==Visibility.Visible && ((TextBlock)window.FindName("DetailSourceEvidence")).Text.Length>0,"source explanation and original page are directly available in details");
                Click(Button("VersionsButton"));
                Check(results.Items.Count==2 && Button("BackToGroupsButton").IsVisible,"expanding group exposes both real versions for comparison");
                Check(lastBadQuery?.Contains(Uri.EscapeDataString("示例电影 2020"))==true,"retry uses original query after input text changes");
                Check(!Button("RetryButton").IsEnabled,"all recovered sources disable retry");
                Check(new PersonalLibrary(folder).History.Count==1,"retry does not duplicate history");
                Click(Button("FavoriteButton"));Check(new PersonalLibrary(folder).Favorites.Count==1,"favorite button persists selected result");
                Click(Button("FavoritesButton"));Check(results.Items.Count==1&&((TextBlock)window.FindName("CountText")).Text.Contains("收藏"),"favorites view shows saved results");
                Click(Button("FavoritesButton"));Check(results.Items.Count==2,"return from favorites preserves search results");
                Check(Button("VersionsButton").IsEnabled,"explicit bilingual work enables version comparison");
                Click(Button("BackToGroupsButton"));Check(results.Items.Count==1,"back collapses versions without losing resources");results.SelectedIndex=0;
                Click(Button("VersionsButton"));Check(results.Items.Count==2,"version comparison contains two distinct releases");Snapshot(window,"versions-fixture");
                var category=(ComboBox)window.FindName("CategoryBox");var resolution=(ComboBox)window.FindName("ResolutionBox");
                Check(!Button("HistoryButton").IsVisible&&!Button("FavoritesButton").IsVisible&&!Button("HealthButton").IsVisible,"removed toolbar entries are not visible");
                category.SelectedIndex=1;resolution.SelectedIndex=2;
                Check(results.Items.Count==1&&((SearchResult)results.Items[0]).Title.Contains("1080p"),"video resolution selects only matching rows");
                category.SelectedIndex=2;Check(!resolution.IsEnabled&&results.Items.Count==0,"music disables video resolution and excludes video");
                category.SelectedIndex=0;Check(results.Items.Count==2&&!resolution.IsEnabled,"all types ignores retained video resolution");
                category.SelectedIndex=4;
                Check(results.Items.Count==0,"category filtering removes nonmatching results");
                category.SelectedIndex=0;
                Modal("FilterOptionsButton",dialog=>{Tree(dialog).OfType<TextBox>().Single().Text="no such filename";Click(Tree(dialog).OfType<Button>().Single(b=>b.Content.ToString()=="应用筛选"));});
                Check(results.Items.Count==0,"compact filter applies filename text");
                Modal("FilterOptionsButton",dialog=>Click(Tree(dialog).OfType<Button>().Single(b=>b.Content.ToString()=="清除筛选")));
                Check(results.Items.Count==2,"clearing advanced filters restores results");
                Modal("HistoryButton",dialog=>{Check(Tree(dialog).OfType<ListBox>().Single().Items.Count==1,"history window displays saved query");Click(Tree(dialog).OfType<Button>().Single(b=>b.Content.ToString()=="清空历史"));});
                Check(new PersonalLibrary(folder).History.Count==0,"clear history button persists empty history");
                Modal("PreferencesButton",dialog=>
                {
                    var card=(Border)dialog.FindName("HistoryCard");var scroll=(ScrollViewer)dialog.FindName("SettingsScroll");
                    dialog.UpdateLayout();var bottom=card.TranslatePoint(new Point(0,card.ActualHeight),dialog).Y;
                    Check(bottom<=scroll.TranslatePoint(new Point(0,scroll.ActualHeight),dialog).Y-2,"settings card bottom fully inside viewport");
                    dialog.Height=360;dialog.UpdateLayout();scroll.ScrollToEnd();dialog.UpdateLayout();
                    Check(scroll.ScrollableHeight>0&&card.TranslatePoint(new Point(0,card.ActualHeight),dialog).Y<=scroll.TranslatePoint(new Point(0,scroll.ActualHeight),dialog).Y,"small settings window scroll exposes card bottom");
                    dialog.Height=510;scroll.ScrollToTop();dialog.UpdateLayout();Snapshot(dialog,"settings-card-fixed");
                });
                Modal("PreferencesButton",dialog=>{Snapshot(dialog,"preferences-fixture");var history=(CheckBox)dialog.FindName("RememberHistoryBox");var save=Tree(dialog).OfType<Button>().Single(b=>b.Content.ToString()=="保存设置");Check(history.TranslatePoint(new Point(),dialog).Y+history.ActualHeight<=save.TranslatePoint(new Point(),dialog).Y,"settings history switch does not overlap footer");history.IsChecked=false;Click(save);});
                query.Text="Sample Movie";await window.RunSearchAsync();Check(new PersonalLibrary(folder).History.Count==0,"history-off prevents subsequent recording");
                results.SelectedIndex=0;Click(Button("VersionsButton"));
                Modal("HealthButton",dialog=>{Check(Tree(dialog).OfType<ListBox>().Single().Items.Count==2,"source status displays both sources");Snapshot(dialog,"health-fixture");});
                Modal("FeedbackButton",dialog=>{Snapshot(dialog,"feedback-fixture");var hint=(TextBlock)dialog.FindName("HintText");Check(hint.TranslatePoint(new Point(),dialog).Y+hint.ActualHeight<=dialog.ActualHeight-8,"feedback diagnostic hint fits window");});
                var info=MainWindow.ResourceInfo(results.SelectedItem as SearchResult ?? throw new InvalidOperationException("version selection missing"));Check(info.Contains("magnet:")&&info.Contains("大小：")&&info.Contains("来源："),"complete resource copy contains required metadata");
                Snapshot(window,"workbench-fixture");window.Width=1060;window.Height=650;window.UpdateLayout();var actions=(StackPanel)window.FindName("DetailActions");var position=actions.TranslatePoint(new Point(),window);
                Check(position.Y+actions.ActualHeight<=window.ActualHeight,"expanded actions remain within minimum window");Snapshot(window,"workbench-minimum");
                var original=(SearchResult)results.Items[0];
                var annotated=ResultMerger.Merge([original with{Observations=[],Source="来源甲",Size=2147483648},original with{Observations=[],Source="来源乙",Size=4294967296}]).Single();
                ((System.Collections.IList)results.ItemsSource).Add(annotated);results.SelectedItem=annotated;
                var fields=(TextBlock)window.FindName("DetailFields");
                var provenance=(TextBlock)window.FindName("DetailSourceEvidence");
                Check(provenance.Text.Contains("大小存在来源差异")&&provenance.Text.Contains("各来源原始记录"),"selected details disclose conflicts and raw records");
                Check(provenance.Text.Contains("来源甲")&&provenance.Text.Contains("来源乙"),"details retain field source ownership");
                Snapshot(window,"metadata-fixture");
                var cloud=new SearchResult{Title="Cloud fixture",Source="Fixture",ShareUrl="https://pan.quark.cn/s/abcdefgh"};
                ((System.Collections.IList)results.ItemsSource).Add(cloud);results.SelectedItem=cloud;
                Check(Button("OpenButton").Content.ToString()=="打开网盘"&&Button("CopyButton").Content.ToString()=="复制网盘链接"&&Button("CopyButton").IsEnabled,"cloud selection uses browser and share-copy actions");
                Check(((TextBlock)window.FindName("HandoffHint")).Text.Contains("网盘")&&fields.Text.Contains("夸克网盘"),"cloud provenance and validity boundary visible");
                Check(!Button("PlayButton").IsVisible && Button("OpenButton").IsEnabled && Button("CopyButton").IsVisible,"share resource exposes open and copy without a video action");
                Check(MainWindow.ResourceInfo(cloud).Contains(cloud.ShareUrl!)&&PersonalLibrary.Key(cloud)!=PersonalLibrary.Key(cloud with{ShareUrl="https://pan.quark.cn/s/another1"}),"cloud copy includes URL and distinct shares retain separate identities");
                results.SelectedItem=annotated;
                Check(Button("OpenButton").Content.ToString()=="用下载软件打开"&&Button("CopyButton").Content.ToString()=="复制磁力链接","BT selection restores download actions");
                var sortRows = new List<SearchResult> {
                    new(){Title="Alpha",Source="Fixture",Hash=new string('1',40),Size=10,Seeders=3,Published=new DateTimeOffset(2018,1,1,0,0,0,TimeSpan.Zero)},
                    new(){Title="Beta",Source="Fixture",Hash=new string('2',40),Size=100,Seeders=1,Published=new DateTimeOffset(2022,1,1,0,0,0,TimeSpan.Zero)},
                    new(){Title="Gamma",Source="Fixture",Hash=new string('3',40),Size=0,Seeders=0,Published=new DateTimeOffset(2020,1,1,0,0,0,TimeSpan.Zero)},
                    new(){Title="Unknown",Source="Fixture",Hash=new string('4',40)}
                };
                typeof(MainWindow).GetField("all",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,sortRows);
                var queryField=typeof(MainWindow).GetField("rankedQuery",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                queryField.SetValue(window,"少林足球");
                typeof(MainWindow).GetField("all",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,sortRows.Append(new SearchResult{Title="少林足球 2001",Source="Fixture",Hash=new string('5',40)}).ToList());
                Click(Button("BackToGroupsButton"));
                Check(results.Items.Count==1&&((SearchResult)results.Items[0]).Title=="少林足球 2001","native results exclude unrelated names for every sorting mode");
                // Column-order fixtures deliberately exercise numeric sorting without a search query.
                queryField.SetValue(window,"");
                typeof(MainWindow).GetField("all",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,sortRows);
                Click(Button("BackToGroupsButton"));
                string Order()=>string.Join(",",results.Items.Cast<SearchResult>().Select(r=>r.Title));
                foreach(var spec in new[]{("SizeSortHeader","Beta,Alpha,Gamma,Unknown","Gamma,Alpha,Beta,Unknown"),("PublishedSortHeader","Beta,Gamma,Alpha,Unknown","Alpha,Gamma,Beta,Unknown"),("SeedsSortHeader","Alpha,Beta,Gamma,Unknown","Gamma,Beta,Alpha,Unknown")})
                {
                    Click(Button(spec.Item1));
                    Check(Order()==spec.Item2&&Button(spec.Item1).Content.ToString()!.EndsWith("↓"),spec.Item1+" click sorts descending with unknown last");
                    results.SelectedItem=results.Items.Cast<SearchResult>().Single(r=>r.Title=="Beta");
                    Click(Button(spec.Item1));
                    Check(Order()==spec.Item3&&Button(spec.Item1).Content.ToString()!.EndsWith("↑"),spec.Item1+" second click sorts ascending with zero before unknown");
                    Check(((SearchResult)results.SelectedItem).Title=="Beta"&&Button("SortButton").Content.ToString()!.EndsWith("↑"),spec.Item1+" preserves selection and synchronizes toolbar");
                }
                Check(Button("SizeSortHeader").Content.ToString()=="文件大小"&&Button("PublishedSortHeader").Content.ToString()=="发布时间","inactive column arrows cleared");
                Snapshot(window,"header-sort-minimum");
                void VerifyAlignment(string name)
                {
                    window.UpdateLayout();
                    foreach(var column in new[]{("SizeSortHeader","ResultSize"),("PublishedSortHeader","ResultPublished"),("SeedsSortHeader","ResultSeeds")})
                    {
                        var header=Button(column.Item1);var value=Tree(results).OfType<TextBlock>().First(t=>t.Name==column.Item2);
                        var headerRight=header.TranslatePoint(new Point(header.ActualWidth-header.Padding.Right,0),window).X;
                        var valueRight=value.TranslatePoint(new Point(value.ActualWidth,0),window).X;
                        Console.WriteLine($"ALIGN {name} {column.Item1}: header={headerRight:F2}, value={valueRight:F2}, list={results.ActualWidth:F2}, scrollbar={Tree(results).OfType<System.Windows.Controls.Primitives.ScrollBar>().FirstOrDefault()?.ActualWidth:F2}");
                        Check(Math.Abs(headerRight-valueRight)<=1,name+" "+column.Item1+" aligns with values");
                    }
                    var seed=Tree(results).OfType<TextBlock>().First(t=>t.Name=="ResultSeeds");
                    Check(results.TranslatePoint(new Point(results.ActualWidth,0),window).X-seed.TranslatePoint(new Point(seed.ActualWidth,0),window).X>=48,name+" keeps right gutter clear");
                }
                VerifyAlignment("minimum");Snapshot(window,"columns-minimum");
                window.Width=1320;window.Height=860;VerifyAlignment("standard");Snapshot(window,"columns-standard");
                Click(Button("DensityButton"));VerifyAlignment("compact");Click(Button("DensityButton"));
                Click(Button("SortButton"));Check(Order()=="Beta,Gamma,Alpha,Unknown"&&Button("PublishedSortHeader").Content.ToString()!.EndsWith("↓"),"toolbar switch resets direction and synchronizes column");
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(MainWindow).GetField("sort",hidden)!.SetValue(window,0);
                typeof(MainWindow).GetMethod("RefreshResults",hidden)!.Invoke(window,[]);
                var orderBeforeHealth=Order();var probeRow=(SearchResult)results.Items[results.Items.Count-1];
                ((PlaybackHealth)typeof(MainWindow).GetField("playbackHealth",hidden)!.GetValue(window)!).Record(probeRow,PlaybackEvidence.Playable,DateTimeOffset.UtcNow);
                typeof(MainWindow).GetMethod("RefreshHealthResults",hidden)!.Invoke(window,[]);
                Check(((SearchResult)results.Items[0]).Hash==probeRow.Hash&&Button("ApplyHealthOrderButton").Visibility==Visibility.Collapsed,"decoded playback evidence automatically updates recommended order");
                Click(Button("ApplyHealthOrderButton"));
                Check(((SearchResult)results.Items[0]).Hash==probeRow.Hash&&Button("ApplyHealthOrderButton").Visibility==Visibility.Collapsed,"explicit refresh applies real decoded evidence to recommended order");
                var verifiedOnly=(CheckBox)window.FindName("VerifiedPlaybackOnly");verifiedOnly.IsChecked=true;verifiedOnly.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(results.Items.Count==1&&((SearchResult)results.Items[0]).Hash==probeRow.Hash,"verified-only excludes unknown resources");
                ((PlaybackHealth)typeof(MainWindow).GetField("playbackHealth",hidden)!.GetValue(window)!).Record(probeRow,PlaybackEvidence.TimedOut,DateTimeOffset.UtcNow);
                typeof(MainWindow).GetMethod("RefreshHealthResults",hidden)!.Invoke(window,[]);
                Check(results.Items.Count==0&&((TextBlock)window.FindName("EmptyText")).Text.Contains("暂无已验证"),"failed revalidation removes formerly verified result and explains empty state");
                verifiedOnly.IsChecked=false;verifiedOnly.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(results.Items.Count>0,"leaving verified-only restores unverified results for manual attempts");
                await SourceWorkbenchChecks.Run(window,store,Check);
                Click(Button("SourcesNav"));Snapshot(window,"sources-integrated");Click(Button("SearchNav"));
                Modal("PreferencesButton",dialog=>{
                    var scroll=(ScrollViewer)dialog.FindName("SettingsScroll");scroll.ScrollToEnd();dialog.UpdateLayout();
                    Check(((Image)dialog.FindName("SettingsBrandLogo")).Source is not null && ((Image)dialog.FindName("SettingsFamilyMark")).Source is not null,"settings retains product and family logos");
                    Click((Button)dialog.FindName("SettingsCopyEmail"));
                    Check(Clipboard.GetText()==ProductInfo.FeedbackEmail,"settings copies the public feedback email");
                    Snapshot(dialog,"settings-feedback");
                });
                Console.WriteLine($"WORKBENCH PASSED {passed}");exit=0;
            }
            catch(Exception error){Console.Error.WriteLine(error);}
            finally{window.ExitApplication();}
        };
        app.Run(window);return exit;
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(send(request));}
}

