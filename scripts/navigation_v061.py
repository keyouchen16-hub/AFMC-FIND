from pathlib import Path
import re
p=Path('src/Afmc.Find.App/Theme.xaml');s=p.read_text(encoding='utf-8')
styles='''
 <Style x:Key="NavigationTab" TargetType="Button">
  <Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontSize" Value="13"/><Setter Property="Height" Value="36"/><Setter Property="Padding" Value="14,0"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Hover" Background="Transparent" CornerRadius="6"><Grid><ContentPresenter Margin="{TemplateBinding Padding}" VerticalAlignment="Center" HorizontalAlignment="Center"/><Border x:Name="Indicator" Height="2" Background="Transparent" VerticalAlignment="Bottom" Margin="14,0" CornerRadius="1"/></Grid></Border><ControlTemplate.Triggers>
   <Trigger Property="Tag" Value="selected"><Setter Property="Foreground" Value="#F4F2F0"/><Setter Property="FontWeight" Value="SemiBold"/><Setter TargetName="Indicator" Property="Background" Value="{StaticResource Accent}"/></Trigger>
   <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Hover" Property="Background" Value="#12FFFFFF"/><Setter Property="Foreground" Value="#F4F2F0"/></Trigger>
   <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Hover" Property="BorderBrush" Value="{StaticResource Accent}"/><Setter TargetName="Hover" Property="BorderThickness" Value="1"/></Trigger>
  </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key="SettingsIconButton" TargetType="Button" BasedOn="{StaticResource NavigationTab}"><Setter Property="Width" Value="36"/><Setter Property="Padding" Value="0"/><Setter Property="FontFamily" Value="Segoe MDL2 Assets"/><Setter Property="FontSize" Value="16"/></Style>
'''
p.write_text(s.replace('</ResourceDictionary>',styles+'</ResourceDictionary>'),encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8')
pattern=r'<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center"><Button x:Name="SearchNav".*?</StackPanel>'
replacement='''<StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center"><Button x:Name="SearchNav" Style="{StaticResource NavigationTab}" Tag="selected" Content="聚合搜索" Click="ShowSearch"/><Button x:Name="SourcesNav" Style="{StaticResource NavigationTab}" Margin="4,0,0,0" Content="搜索源" Click="ShowSources"/><Border Width="1" Height="16" Background="#30FFFFFF" Margin="14,0"/><Button x:Name="PreferencesButton" Style="{StaticResource SettingsIconButton}" Content="&#xE713;" ToolTip="设置" AutomationProperties.Name="设置" Click="PreferencesClick"/></StackPanel>'''
s,count=re.subn(pattern,replacement,s);assert count==1
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml.cs');s=p.read_text(encoding='utf-8')
s=s.replace('SearchNav.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80,63,38,53));SourcesNav.Background=System.Windows.Media.Brushes.Transparent;','SearchNav.Tag="selected";SourcesNav.Tag=null;')
s=s.replace('SourcesNav.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80,63,38,53));SearchNav.Background=System.Windows.Media.Brushes.Transparent;','SourcesNav.Tag="selected";SearchNav.Tag=null;')
p.write_text(s,encoding='utf-8')
for name in ['src/Afmc.Find.App/MainWindow.xaml','src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.6.0','0.6.1').replace('v060\\','v061\\'),encoding='utf-8')
Path('docs/evidence/v061').mkdir(exist_ok=True)
