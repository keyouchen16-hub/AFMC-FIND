from pathlib import Path
import re
p=Path('src/Afmc.Find.App/Theme.xaml');s=p.read_text(encoding='utf-8-sig')
textbox=re.search(r'<Style TargetType="TextBox">.*?</Style>',s).group()
textbox=textbox.replace('<Style TargetType="TextBox">','<Style x:Key="PillInput" TargetType="TextBox">').replace('CornerRadius="6" Padding="{TemplateBinding Padding}"','CornerRadius="20"').replace('<ScrollViewer x:Name="PART_ContentHost"/>','<ScrollViewer x:Name="PART_ContentHost" Margin="14,0" VerticalAlignment="Center"/>').replace('Value="12,10"','Value="0"')
button=re.search(r'<Style TargetType="Button">.*?</Style>',s).group().replace('<Style TargetType="Button">','<Style x:Key="PillButton" TargetType="Button">').replace('CornerRadius="6"','CornerRadius="20"')
s=s.replace('</ResourceDictionary>',textbox+button+'<Style x:Key="PillPrimary" TargetType="Button" BasedOn="{StaticResource PillButton}"><Setter Property="Background" Value="{StaticResource Accent}"/><Setter Property="Foreground" Value="#151216"/><Setter Property="FontWeight" Value="SemiBold"/></Style></ResourceDictionary>')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8')
s=s.replace('x:Name="QueryBox"','Style="{StaticResource PillInput}" Height="40" x:Name="QueryBox"').replace('FontSize="16" Padding="14,11"','FontSize="14" Padding="0"')
s=s.replace('x:Name="FilterBox"','Style="{StaticResource PillInput}" Height="32" VerticalAlignment="Center" x:Name="FilterBox"').replace('Padding="10,7" Width="150"','Padding="0" Width="180"')
s=s.replace('Content="搜索资源" Style="{StaticResource Primary}"','Content="搜索资源" Height="40" Padding="18,0" Style="{StaticResource PillPrimary}"')
s=s.replace('x:Name="CancelButton"','Style="{StaticResource PillButton}" Height="40" Padding="16,0" x:Name="CancelButton"')
for name in ['DensityButton','SortButton']:
 s=s.replace('x:Name="'+name+'"','Style="{StaticResource PillButton}" Height="32" x:Name="'+name+'"')
s=s.replace('Padding="10,7"','Padding="12,0"')
p.write_text(s.replace('0.5.2','0.5.3'),encoding='utf-8')
for name in ['src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.5.2','0.5.3').replace('v052\\','v053\\'),encoding='utf-8')
Path('docs/evidence/v053').mkdir(exist_ok=True)
