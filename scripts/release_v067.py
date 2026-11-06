from pathlib import Path
for name in ['src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Brand.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.6.6','0.6.7').replace('v066','v067'),encoding='utf-8')
p=Path('scripts/Verify-Release.ps1');s=p.read_text(encoding='utf-8-sig')
s=s.replace("$exe = Join-Path $root 'artifacts\\AFMC-FIND-0.6.7-win-x64\\AFMC.Find.exe'","$exe = Join-Path $env:LOCALAPPDATA 'Programs\\AFMC FIND\\AFMC.Find.exe'")
s=s.replace('[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();','[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();\n [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr dc,uint flags);')
s=s.replace('$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $image.Size)','$dc=$graphics.GetHdc(); try { if(-not [FindCapture]::PrintWindow($process.MainWindowHandle,$dc,2)){throw "Window capture failed"} } finally {$graphics.ReleaseHdc($dc)}')
s=s.replace("version = $process.MainModule.FileVersionInfo.ProductVersion","version = (Get-Item (Join-Path (Split-Path $exe -Parent) 'AFMC.Find.dll')).VersionInfo.ProductVersion")
s=s.replace("status = $status","status = $status\n    sourceStatus = (Find-Control 'SourceStatus').Current.Name")
p.write_text(s,encoding='utf-8')
Path('docs/PORTABLE.txt').write_text('''AFMC FIND 0.6.7 — Windows x64

仅搜索和交接下载软件；不内置下载引擎。
本版新增 Torrents.csv、LimeTorrents、Tokyo Toshokan、Academic Torrents。
共11个内置聚合源（含原有7个）；网页入口不计入。
按哈希去重，综合推荐默认；做种、时间等是来源报告值，不保证内容真实性或实时可下载。
新源会自动补入，既有启停与API密钥保持。

来源查询有超时、响应上限和5分钟缓存；Academic元数据在会话中缓存24小时。
最多3页，远端验证、限流、故障会分别报告，部分结果会保留。

关闭默认留在托盘；右键托盘可退出。设置可改变关闭/最小化行为。
右下角作者标记打开反馈，邮箱 keyouchen16@gmail.com。

项目维护发布：scripts/Build.ps1 -Publish 将构建、检查、打包并覆盖安装。
固定安装目录：%LOCALAPPDATA%\\Programs\\AFMC FIND。
个人配置：%LOCALAPPDATA%\\AFMC\\Find（覆盖安装不替换）。
更新自动退出本项目旧版本，安装前备份至 %LOCALAPPDATA%\\AFMC\\Find-install-backups。
直接解压本ZIP属于便携启动，不自动覆盖已有安装；使用安装流程才能完成覆盖更新。
''',encoding='utf-8')
