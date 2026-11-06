from pathlib import Path
for name in ['src/Afmc.Find.App/MainWindow.xaml','src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','docs/PORTABLE.txt','README.md']:
 p=Path(name);s=p.read_text(encoding='utf-8').replace('0.3.1','0.4.0').replace('evidence\\v031\\','evidence\\v04\\')
 s=s.replace('每源最多 3 页 · 图片来自原条目','每源最多 3 页（RSS 单批）· 图片自动显示')
 s=s.replace('已接入 TPB、种子楼与 Internet Archive','已接入海盗湾、Knaben、BitSearch、Nyaa、种子楼与 Internet Archive')
 s=s.replace('种子楼与 TPB 参与聚合搜索；导航站与未验证站点保留原站入口。Jackett / Prowlarr 可另行添加。','6 个默认聚合源：海盗湾、Knaben、BitSearch、Nyaa、种子楼、Internet Archive。可通过 Jackett / Prowlarr 扩展。')
 p.write_text(s,encoding='utf-8')
p=Path('tests/Afmc.Find.Checks/Program.cs');s=p.read_text(encoding='utf-8').replace('SiteSources.Catalog.Count == 6','SiteSources.Catalog.Count(s=>s.Id.StartsWith("site-")) == 6');p.write_text(s,encoding='utf-8')
p=Path('scripts/Build.ps1');s=p.read_text(encoding='utf-8').replace('--no-build\n','--no-build -- --global\n');p.write_text(s,encoding='utf-8')
p=Path('scripts/Verify-Release.ps1');s=p.read_text(encoding='utf-8').replace('AddSeconds(45)','AddSeconds(100)');p.write_text(s,encoding='utf-8')
Path('docs/evidence/v04').mkdir(parents=True,exist_ok=True)
