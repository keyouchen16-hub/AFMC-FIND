from pathlib import Path
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8')
needle='HorizontalAlignment="Left"/></StackPanel><Button x:Name="SortButton"'
replace='HorizontalAlignment="Left"/><CheckBox x:Name="ImagesOnly" AutomationProperties.AutomationId="ImagesOnly" Content="有来源图片" Margin="16,0,0,0" Checked="ImageFilterChanged" Unchecked="ImageFilterChanged" ToolTip="仅显示有条目图片接口的来源，加载失败会明确提示。"/></StackPanel><Button x:Name="SortButton"'
assert needle in s
p.write_text(s.replace(needle,replace,1),encoding='utf-8')
