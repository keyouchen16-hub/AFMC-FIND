from pathlib import Path
import re
p=Path('src/Afmc.Find.App/PreferencesWindow.xaml');s=p.read_text(encoding='utf-8')
s=re.sub(r'  <Style x:Key="Choice".*?</Style>\n','',s)
start=s.index('<TextBlock Text="搜索偏好"')
end=s.index('</StackPanel></Border></StackPanel>',start)
s=s[:start]+'</StackPanel>'+s[end+len('</StackPanel></Border></StackPanel>'):]
s=s.replace('Height="500"','Height="360"').replace('CornerRadius="10"><DockPanel>','CornerRadius="0"><DockPanel>')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/PreferencesWindow.xaml.cs');s=p.read_text(encoding='utf-8').replace(';DefaultSortBox.SelectedIndex=value.DefaultSort','').replace(',DefaultSort=DefaultSortBox.SelectedIndex','');p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/Preferences.cs');s=p.read_text(encoding='utf-8').replace('    public int DefaultSort {get;init;}\n','').replace('return result with {DefaultSort=Math.Clamp(result.DefaultSort,0,4)};','return result;');p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml.cs');s=p.read_text(encoding='utf-8').replace('sort=preferences.DefaultSort;UpdateSortLabel();','UpdateSortLabel();');p.write_text(s,encoding='utf-8')
p=Path('tests/Afmc.Find.Checks/Program.cs');s=p.read_text(encoding='utf-8').replace(',DefaultSort=1','').replace('(prefs with {DefaultSort=0})','(prefs with {CloseToTray=false})')
# Restore desired close behavior after exercising the backup path.
s=s.replace('var window=new MainWindow(store,new SearchService(),true);','prefs.Save(directory);\n        var window=new MainWindow(store,new SearchService(),true);')
s=re.sub(r'                var choices=.*?"settings choices fit without clipping"\);', '                Check(settingsWindow.FindName("DefaultSortBox") is null,"settings has no sorting preference");',s,flags=re.S)
s=s.replace('docs/evidence/v062','docs/evidence/v064');p.write_text(s,encoding='utf-8')
for name in ['src/Afmc.Find.App/MainWindow.xaml','src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.6.3','0.6.4').replace('v063\\','v064\\'),encoding='utf-8')
Path('docs/evidence/v064').mkdir(exist_ok=True)
