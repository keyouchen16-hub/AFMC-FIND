from pathlib import Path
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8')
s=s.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"','xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Afmc.Find.App"',1)
start=s.index('<ListBox.ItemTemplate>');end=s.index('</ListBox.ItemTemplate>',start)+len('</ListBox.ItemTemplate>');old=s[start:end]
new=old.replace('<ColumnDefinition/>','<ColumnDefinition Width="86"/><ColumnDefinition/>',1)
new=new.replace('Grid.Column="2"','Grid.Column="3"').replace('Grid.Column="1"','Grid.Column="2"')
new=new.replace('<StackPanel>','<local:ThumbnailView Url="{Binding ThumbnailUrl}" Width="72" Height="58" HorizontalAlignment="Left"/><StackPanel Grid.Column="1">',1)
new=new.replace('</StackPanel>','<TextBlock Text="{Binding MatchReason}" Foreground="{StaticResource Muted}" FontSize="10" Margin="0,5,0,0" TextTrimming="CharacterEllipsis"/></StackPanel>',1)
s=s[:start]+new+s[end:]
s=s.replace('<TextBlock x:Name="DetailTitle"','<local:ThumbnailView Url="{Binding SelectedItem.ThumbnailUrl, ElementName=ResultsList}" Height="110" Margin="0,14,0,0"/><TextBlock x:Name="DetailTitle"',1)
s=s.replace('0.2.0','0.3.0').replace('相关度优先 · 来源数不等于准确率','每源最多 3 页 · 图片来自原条目').replace('已接入种子楼与 Internet Archive 公开源','已接入 TPB、种子楼与 Internet Archive')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/Afmc.Find.App.csproj');p.write_text(p.read_text(encoding='utf-8').replace('<Version>0.2.0</Version>','<Version>0.3.0</Version>'),encoding='utf-8')
for name in ['Build.ps1','Package.ps1','Verify-Release.ps1']:
    p=Path('scripts')/name;s=p.read_text(encoding='utf-8-sig').replace('0.2.0','0.3.0').replace('docs\\evidence\\source-audit','docs\\evidence\\v03');p.write_text(s,encoding='utf-8-sig')
Path('docs/evidence/v03').mkdir(parents=True,exist_ok=True)
