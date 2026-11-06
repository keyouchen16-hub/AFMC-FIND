from pathlib import Path
p=Path('src/Afmc.Find.App/MainWindow.xaml')
s=p.read_text(encoding='utf-8')
s=s.replace('x:Name="SourcesNav" Content=', 'x:Name="SourcesNav" Margin="8,0,0,0" Content=',1)
p.write_text(s,encoding='utf-8')
