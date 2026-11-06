from pathlib import Path
import re
p=Path('src/Afmc.Find.App/Theme.xaml');s=p.read_text(encoding='utf-8')
for source,target in [('PillInput','CompactInput'),('PillButton','CompactButton')]:
 style=re.search(r'<Style x:Key="'+source+r'".*?</Style>',s).group().replace('x:Key="'+source+'"','x:Key="'+target+'"').replace('CornerRadius="20"','CornerRadius="8"')
 s=s.replace('</ResourceDictionary>',style+'</ResourceDictionary>')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8').replace('Content="排序：做种数 ↓"','Content="排序：综合推荐 ↓" ToolTip="综合名称匹配、指定年份/版本、做种数和发布时间；点击切换单项排序"');s=s.replace('Url="{Binding ThumbnailUrl}"','Url="{Binding ThumbnailUrl}" ToolTip="{Binding ThumbnailLabel}"').replace('Url="{Binding SelectedItem.ThumbnailUrl, ElementName=ResultsList}"','Url="{Binding SelectedItem.ThumbnailUrl, ElementName=ResultsList}" ToolTip="{Binding SelectedItem.ThumbnailLabel, ElementName=ResultsList}"');p.write_text(s,encoding='utf-8')
for name in ['src/Afmc.Find.App/MainWindow.xaml','src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.5.3','0.6.0').replace('v053\\','v060\\'),encoding='utf-8')
Path('docs/evidence/v060').mkdir(exist_ok=True)
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8').replace('Style="{StaticResource PillInput}" Height="32"','Style="{StaticResource CompactInput}" Height="32"').replace('Style="{StaticResource PillButton}" Height="32"','Style="{StaticResource CompactButton}" Height="32"');p.write_text(s,encoding='utf-8')
