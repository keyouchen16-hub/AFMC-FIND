from pathlib import Path
import re

app = Path('src/Afmc.Find.App')
p = app/'MainWindow.xaml'
s = p.read_text(encoding='utf-8')
s = re.sub(r'<CheckBox x:Name="ImagesOnly"[^>]*/>', '', s)
s = s.replace('<ColumnDefinition Width="86"/>', '<ColumnDefinition Width="Auto"/>')
s = s.replace('Width="72" Height="58" HorizontalAlignment="Left"', 'Width="72" Height="58" Margin="0,0,14,0" HorizontalAlignment="Left"')
p.write_text(s, encoding='utf-8')
p = app/'MainWindow.xaml.cs'
s = p.read_text(encoding='utf-8')
s = '\n'.join(line for line in s.split('\n') if 'ImagesOnly.IsChecked' not in line and 'private void ImageFilterChanged' not in line)
p.write_text(s, encoding='utf-8')
p = Path('tests/Afmc.Find.Checks/Program.cs')
s = p.read_text(encoding='utf-8')
s = '\n'.join('                Check(window.FindName("ImagesOnly") is null, "WPF has no image filter");\n                Check(new ThumbnailView().Visibility == Visibility.Collapsed, "WPF missing thumbnail occupies no space");' if 'if(live){var images=' in line else line for line in s.split('\n'))
p.write_text(s, encoding='utf-8')
for name in ['src/Afmc.Find.App/MainWindow.xaml', 'src/Afmc.Find.App/Afmc.Find.App.csproj', 'scripts/Build.ps1', 'scripts/Package.ps1', 'scripts/Verify-Release.ps1', 'docs/PORTABLE.txt', 'README.md']:
    p=Path(name)
    s=p.read_text(encoding='utf-8').replace('0.3.0','0.3.1').replace('evidence\\v03\\','evidence\\v031\\')
    p.write_text(s, encoding='utf-8')
p=Path('scripts/Verify-Release.ps1')
s=p.read_text(encoding='utf-8').replace('[switch]$ImagesOnly','[switch]$CheckImages').replace('if ($ImagesOnly) {','if ($CheckImages) {')
s=re.sub(r"    \$filter = Find-Control 'ImagesOnly'.*?    \$imageDeadline", '    $imageDeadline', s, flags=re.S)
s=s.replace('$thumbnailCount -gt 2', '$thumbnailCount -gt 0').replace('$thumbnailCount -lt 3', '$thumbnailCount -lt 1')
s=s.replace('$thumbnailCount = 0', "if ($null -ne (Find-Control 'ImagesOnly')) { throw 'Obsolete image filter is still visible' }\n$thumbnailCount = 0")
p.write_text(s, encoding='utf-8')
Path('docs/evidence/v031').mkdir(parents=True, exist_ok=True)
