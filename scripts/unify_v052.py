from pathlib import Path
import re
p=Path('src/Afmc.Find.App/MainWindow.xaml')
s=p.read_text(encoding='utf-8')
pattern=r'<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,0,148,0".*?</StackPanel>'
nav=re.search(pattern,s).group()
s=s.replace(nav,'')
nav=nav.replace('Margin="0,0,148,0" WindowChrome.IsHitTestVisibleInChrome="True"','VerticalAlignment="Center"').replace('Padding="10,3"','Padding="14,8"')
s=s.replace('<Grid Margin="24,16,24,12">','<DockPanel Margin="24,16,24,12"><Grid DockPanel.Dock="Top" Margin="0,0,0,18"><TextBlock x:Name="PageTitle" Text="聚合搜索" FontSize="23" FontWeight="SemiBold"/>'+nav+'</Grid><Grid>')
s=s.replace('    <Grid Margin="0,0,0,18"><TextBlock Text="聚合搜索" FontSize="23" FontWeight="SemiBold"/><TextBlock Text="Ctrl + K 聚焦搜索" HorizontalAlignment="Right" VerticalAlignment="Center" Foreground="{StaticResource Muted}" FontSize="12"/></Grid>','')
s=s.replace('<TextBlock Text="搜索源" FontSize="23" FontWeight="SemiBold" Margin="0,0,0,10"/>','')
s=s.replace('  </Grid><Border Grid.Row="1"','  </Grid></DockPanel><Border Grid.Row="1"')
p.write_text(s.replace('0.5.1','0.5.2'),encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml.cs');s=p.read_text(encoding='utf-8').replace('{ SearchView.Visibility = Visibility.Visible;', '{ PageTitle.Text="聚合搜索"; SearchView.Visibility = Visibility.Visible;').replace('{ SearchView.Visibility = Visibility.Collapsed;', '{ PageTitle.Text="搜索源"; SearchView.Visibility = Visibility.Collapsed;');p.write_text(s,encoding='utf-8')
for name in ['src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.5.1','0.5.2').replace('v051\\','v052\\'),encoding='utf-8')
Path('docs/evidence/v052').mkdir(exist_ok=True)
