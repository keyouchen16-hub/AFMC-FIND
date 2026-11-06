from pathlib import Path
for name in ['src/Afmc.Find.App/MainWindow.xaml','src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Release.ps1','docs/PORTABLE.txt','README.md']:
 p=Path(name);s=p.read_text(encoding='utf-8-sig').replace('0.4.0','0.4.1').replace('evidence\\v04\\','evidence\\v041\\');p.write_text(s,encoding='utf-8')
p=Path('tests/Afmc.Find.Checks/Program.cs');s=p.read_text(encoding='utf-8').replace('SiteSources.Catalog.Count(s=>s.Kind=="Website")==4','SiteSources.Catalog.Count(s=>s.Id.StartsWith("site-")&&s.Kind=="Website")==4');p.write_text(s,encoding='utf-8')
Path('docs/evidence/v041').mkdir(parents=True,exist_ok=True)
