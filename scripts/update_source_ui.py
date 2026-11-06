from pathlib import Path
p=Path('src/Afmc.Find.App/MainWindow.xaml');s=p.read_text(encoding='utf-8')
s=s.replace('排序：做种数 ↓','排序：相关度 ↓').replace('Windows 原生 · 0.1.0','Windows 原生 · 0.2.0')
s=s.replace('已提供 Internet Archive 公开资源源','已接入种子楼与 Internet Archive 公开源')
s=s.replace('未知字段不代表 0 · 每源最多 50 / 100 条','相关度优先 · 来源数不等于准确率')
s=s.replace('公开资源无需密钥。Jackett / Prowlarr 请添加单个索引器的 Torznab API 地址。','种子楼参与聚合搜索；导航站与未验证站点保留原站入口。Jackett / Prowlarr 可另行添加。')
s=s.replace('<Button Content="移除" Click="RemoveSource"/>','<Button Content="移除" Click="RemoveSource" Margin="0,0,8,0"/><Button Content="打开原站 ↗" Click="OpenSourceSite"/>')
p.write_text(s,encoding='utf-8')
p=Path('src/Afmc.Find.App/Afmc.Find.App.csproj');p.write_text(p.read_text(encoding='utf-8').replace('<Version>0.1.0</Version>','<Version>0.2.0</Version>'),encoding='utf-8')
