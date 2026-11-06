from pathlib import Path

root = Path(__file__).resolve().parent.parent
p = root / 'src/Afmc.Find.App/MainWindow.xaml'
s = p.read_text(encoding='utf-8-sig')
lines = s.splitlines()
lines = [line for line in lines if not line.startswith('  <Border Background="#551B1F24"')]
s = '\n'.join(lines)
s = s.replace('<Grid.ColumnDefinitions><ColumnDefinition Width="172"/><ColumnDefinition/></Grid.ColumnDefinitions>', '')
s = s.replace('<Grid Grid.Column="1" Margin="24,24,24,12">', '<Grid Margin="24,16,24,12">')
s = s.replace('Grid.Row="1" Grid.ColumnSpan="2"', 'Grid.Row="1"')
s = s.replace('Title="AFMC FIND', 'Icon="Assets/afmc-find.ico" Title="AFMC FIND', 1)
old = '<TextBlock Text="AFMC FIND" FontSize="12" Foreground="{StaticResource Muted}" Margin="16,0" VerticalAlignment="Center"/>'
new = '<StackPanel Orientation="Horizontal" Margin="12,0" VerticalAlignment="Center"><Image x:Name="BrandLogo" Source="Assets/afmc-find.png" Width="24" Height="24" Margin="0,0,8,0" AutomationProperties.Name="AFMC FIND 标志"/><TextBlock Text="AFMC FIND" FontSize="12" VerticalAlignment="Center"/><TextBlock x:Name="MaterialText" Visibility="Collapsed"/></StackPanel><StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,0,148,0" WindowChrome.IsHitTestVisibleInChrome="True"><Button x:Name="SearchNav" Content="聚合搜索" Click="ShowSearch" Padding="10,3" FontSize="12" Background="Transparent" BorderThickness="0"/><Button x:Name="SourcesNav" Content="搜索源" Click="ShowSources" Padding="10,3" FontSize="12" Background="Transparent" BorderThickness="0"/></StackPanel>'
assert old in s
s = s.replace(old, new).replace('排序：相关度 ↓', '排序：做种数 ↓').replace('0.5.0', '0.5.1')
p.write_text(s, encoding='utf-8')
p = root / 'src/Afmc.Find.App/MainWindow.xaml.cs'
s = p.read_text(encoding='utf-8-sig').replace('private int sort;', 'private int sort = 1;')
s = s.replace('1 => filtered.OrderByDescending(r => r.Seeders)', '1 => filtered.OrderByDescending(r => r.Seeders).ThenByDescending(r => SiteSources.Rank(rankedQuery,r))')
p.write_text(s, encoding='utf-8')
p = root / 'src/Afmc.Find.App/Afmc.Find.App.csproj'
s = p.read_text(encoding='utf-8-sig').replace('<Version>0.5.0</Version>', '<Version>0.5.1</Version><ApplicationIcon>Assets/afmc-find.ico</ApplicationIcon>')
s = s.replace('</Project>', '<ItemGroup><Resource Include="Assets/afmc-find.png"/><Resource Include="Assets/afmc-find.ico"/></ItemGroup></Project>')
p.write_text(s, encoding='utf-8')
for name in ['Build.ps1', 'Package.ps1', 'Verify-Release.ps1', 'Verify-Glass.ps1']:
 p = root / 'scripts' / name
 p.write_text(p.read_text(encoding='utf-8-sig').replace('0.5.0', '0.5.1').replace('v05\\', 'v051\\'), encoding='utf-8')
(root / 'docs/evidence/v051').mkdir(exist_ok=True)
