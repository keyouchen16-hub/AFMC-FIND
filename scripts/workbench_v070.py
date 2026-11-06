from pathlib import Path
app=Path('src/Afmc.Find.App')
p=app/'MainWindow.xaml.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('ResultsList.ItemsSource = visible; RefreshSources();','InitializeWorkbench(); ResultsList.ItemsSource = visible; RefreshSources();')
s=s.replace('new FeedbackWindow {Owner=this}','new FeedbackWindow(DiagnosticReport.Build(sourceReports.Values)) {Owner=this}')
s=s.replace('public async Task RunSearchAsync()\n    {\n        var query = QueryBox.Text.Trim();','public Task RunSearchAsync()=>RunSearchCoreAsync(false);\n    private async Task RunSearchCoreAsync(bool retry)\n    {\n        var query = retry?rankedQuery:QueryBox.Text.Trim();')
s=s.replace('sources.Where(s => s.Enabled && s.Kind != "Website").ToArray();','sources.Where(s => s.Enabled && s.Kind != "Website" && (!retry||sourceReports.TryGetValue(s.Id,out var oldReport)&&oldReport.Error is not null)).ToArray();')
s=s.replace('rankedQuery = query;','rankedQuery = query; favoritesView=false;FavoritesButton.Content="收藏"; if(!retry)RecordHistory(query);')
s=s.replace('all.Clear(); visible.Clear(); reports.Clear(); SourceStatus.Text = "";','if(!retry){all.Clear(); visible.Clear(); reports.Clear();sourceReports.Clear(); SourceStatus.Text = "";} RetryButton.IsEnabled=false;')
s=s.replace('all = ResultMerger.Merge(all.Concat(report.Results));\n                reports.Add(report.Error is null ? $"{report.Name}：{report.Results.Count} 条" : $"{report.Name}：{report.Error}");\n                SourceStatus.Text = string.Join("   /   ", reports);', 'if(retry&&report.Error is not null&&sourceReports.TryGetValue(report.SourceId,out var previousReport))report=report with {Results=ResultMerger.Merge(previousReport.Results.Concat(report.Results))};\n                sourceReports[report.SourceId]=report;\n                all = ResultMerger.Merge(sourceReports.Values.SelectMany(r=>r.Results));\n                SourceStatus.Text = string.Join("   /   ", sourceReports.Values.Select(r=>r.Summary));')
s=s.replace('StatusText.Text = $"搜索完成 · {all.Count} 条结果 · {summary.Succeeded} 源成功 / {summary.Failed} 源失败";','StatusText.Text = $"搜索完成 · {all.Count} 条结果 · {sourceReports.Values.Count(r=>r.Error is null)} 源成功 / {sourceReports.Values.Count(r=>r.Error is not null)} 源失败";')
s=s.replace('searchCancel = null; CancelButton.IsEnabled = false;','searchCancel = null; RetryButton.IsEnabled=sourceReports.Values.Any(r=>r.Error is not null); CancelButton.IsEnabled = false;')
s=s.replace('var filtered = ResultPresentation.WithCovers(all).Where(r => r.Title.Contains(FilterBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Select(r=>r with {MatchReason=ResultPresentation.Explain(rankedQuery,r)});', '''var displayQuery=favoritesView?"":rankedQuery;
        var rows=ResultPresentation.WithCovers(favoritesView?library?.Favorites??[]:all);
        queryAliases=SearchQuality.Aliases(displayQuery,rows);
        var filtered = rows.Where(r => r.Title.Contains(FilterBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)&&resultFilter.Matches(r)).Select(r=>
        {
            var best=queryAliases.OrderByDescending(q=>ResultPresentation.Score(q,r,DateTimeOffset.UtcNow)).FirstOrDefault()??displayQuery;
            return r with {MatchReason=(best!=displayQuery?"双语条目别名匹配 · ":"")+ResultPresentation.Explain(best,r)};
        });''')
s=s.replace('ResultPresentation.Score(rankedQuery,r,now)','queryAliases.Select(q=>ResultPresentation.Score(q,r,now)).DefaultIfEmpty(0).Max()')
s=s.replace('CountText.Text = $"{visible.Count} / {all.Count} 条结果"; EmptyText.Visibility', 'CountText.Text = favoritesView?$"{visible.Count} / {library?.Favorites.Count??0} 条收藏":$"{visible.Count} / {all.Count} 条结果"; EmptyText.Visibility')
s=s.replace('if (visible.Count == 0 && all.Count > 0) EmptyText.Text', 'if(favoritesView&&visible.Count==0)EmptyText.Text="暂无符合筛选的收藏。选择资源后点击收藏即可保存到本机。";\n        else if (visible.Count == 0 && all.Count > 0) EmptyText.Text')
s=s.replace('DetailFields.Text = r is null ? "" :', 'UpdateWorkbenchSelection(r);\n        DetailFields.Text = r is null ? "" :')
s=s.replace('$"大小　{r.SizeText}', '$"规格　{SearchQuality.TechnicalSummary(r)}\\n大小　{r.SizeText}')
p.write_text(s,encoding='utf-8')
p=app/'MainWindow.xaml';s=p.read_text(encoding='utf-8-sig')
s=s.replace('<Grid x:Name="SearchView"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition/><RowDefinition Height="Auto"/></Grid.RowDefinitions>','<Grid x:Name="SearchView"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition/><RowDefinition Height="Auto"/></Grid.RowDefinitions>')
s=s.replace('<Grid Grid.Row="3"><Grid.ColumnDefinitions>', '''<WrapPanel Grid.Row="3" Margin="0,0,0,12">
      <Button x:Name="HistoryButton" Content="历史" Style="{StaticResource CompactButton}" Padding="12,6" Margin="0,0,8,0" Click="HistoryClick"/>
      <Button x:Name="FavoritesButton" Content="收藏" Style="{StaticResource CompactButton}" Padding="12,6" Margin="0,0,8,0" Click="FavoritesClick"/>
      <Button x:Name="FilterOptionsButton" Content="筛选" Style="{StaticResource CompactButton}" Padding="12,6" Margin="0,0,8,0" Click="FilterOptionsClick"/>
      <Button x:Name="HealthButton" Content="来源状态" Style="{StaticResource CompactButton}" Padding="12,6" Margin="0,0,8,0" Click="SourceHealthClick"/>
      <Button x:Name="RetryButton" Content="重试失败来源" Style="{StaticResource CompactButton}" Padding="12,6" IsEnabled="False" Click="RetryClick"/>
    </WrapPanel><Grid Grid.Row="4"><Grid.ColumnDefinitions>''',1)
s=s.replace('x:Name="SourceStatus" Grid.Row="4"','x:Name="SourceStatus" Grid.Row="5"')
s=s.replace('<StackPanel x:Name="DetailActions" Grid.Row="1" Margin="0,16,0,0">','''<StackPanel x:Name="DetailActions" Grid.Row="1" Margin="0,12,0,0">
       <Button x:Name="VersionsButton" Content="暂无可比较版本" IsEnabled="False" Click="VersionsClick" Padding="8,6" FontSize="12" Margin="0,0,0,8"/>
       <Grid Margin="0,0,0,8"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="8"/><ColumnDefinition/></Grid.ColumnDefinitions><Button x:Name="FavoriteButton" Content="收藏资源" Click="FavoriteClick" IsEnabled="False" Padding="6,6" FontSize="12"/><Button x:Name="CopyInfoButton" Grid.Column="2" Content="复制信息" Click="CopyInfoClick" IsEnabled="False" Padding="6,6" FontSize="12"/></Grid>''')
p.write_text(s,encoding='utf-8')
p=app/'PreferencesWindow.xaml';s=p.read_text(encoding='utf-8-sig').replace('Height="360"','Height="455"')
s=s.replace('</StackPanel></Border></StackPanel>','''</StackPanel></Border><TextBlock Text="本机记录" Foreground="{StaticResource Muted}" FontSize="12" Margin="0,14,0,7"/><Border Style="{StaticResource GlassPanel}" Padding="16,0"><CheckBox x:Name="RememberHistoryBox" Style="{StaticResource Switch}"><StackPanel><TextBlock Text="记录搜索历史"/><TextBlock Text="最近50条，仅本机保存；在历史窗口清空" FontSize="11" Foreground="{StaticResource Muted}" Margin="0,4,0,0"/></StackPanel></CheckBox></Border></StackPanel>''')
p.write_text(s,encoding='utf-8')
p=app/'FeedbackWindow.xaml';s=p.read_text(encoding='utf-8-sig').replace('Height="310"','Height="365"')
s=s.replace('<TextBlock x:Name="HintText"','<Button Content="复制诊断信息" Click="CopyDiagnostics" Margin="0,10,0,0" Padding="10,6" FontSize="12" ToolTip="仅版本、系统和来源状态，不含搜索词或密钥"/><TextBlock x:Name="HintText"')
p.write_text(s,encoding='utf-8')
