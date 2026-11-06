from pathlib import Path
p=Path('src/Afmc.Find.App/FeedbackWindow.xaml')
s=p.read_text(encoding='utf-8-sig').replace('Source="Assets/author-mark.png" Width="96" Height="48"','Source="Assets/family-mark.png" Width="64" Height="64" Opacity="0.8" RenderOptions.BitmapScalingMode="HighQuality"')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/MainWindow.xaml')
s=p.read_text(encoding='utf-8-sig').replace('x:Name="FeedbackButton" Grid.Column="1"','x:Name="FeedbackButton" FocusVisualStyle="{x:Null}" Grid.Column="1"')
s=s.replace('Padding="8,1" Margin="0,1"><Image Source="Assets/author-mark.png"','Padding="8,1" Margin="0,1"><Button.Template><ControlTemplate TargetType="Button"><Border Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter x:Name="Mark" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.85"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Mark" Property="Opacity" Value="1"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Mark" Property="Opacity" Value="1"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template><Image Source="Assets/author-mark.png"')
p.write_text(s,encoding='utf-8')
for name in ['src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Brand.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.6.5','0.6.6').replace('v065','v066'),encoding='utf-8')
Path('docs/evidence/v066').mkdir(exist_ok=True)
