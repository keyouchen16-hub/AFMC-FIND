## 2026-11-07：隔离原生构建

复用 VS2022 BuildTools/MSVC14.44.35207，Boost1.87.0、官方Python3.12.10开发文件、CPython OpenSSL3.0.16.2，libtorrent2.0.15。工作目录 .tools/native-stream-build；没有系统安装/安全设置变更。Git Credential Manager 已有 keyouchen16-hub 登录，与连接器账号不同；使用用户确认的前者，不记录凭据。

# 2026-11-06：必需分片独立诊断环境

.tools/piece-trace 保留 0.8.20 安装版隔离副本、各候选 worker 快照和修改前源码。绑定 2.0.15.0；后台软件解码和真实 WPF 硬件播放分别记录。每轮独立视频缓存；product-reuse 的 warm1/2 只复用进程路由/对端。追踪每秒、约 16MiB 上限、会话内匿名节点编号；DiscoveryTrace 只留网络计数/错误码。第一基线 300 秒预算只观察 119.85 秒，后续 360 秒预算、起播后 120 秒。本轮参数撤回后 source/build/source-before worker 均 F4C083B0650C7EE91D505C9CE5DD240E948207485A2EDF8C0B2641AEA919522C。没有遗留实验 Python 进程，安装版 PID49640 仍响应，三个已安装文件哈希与调查前一致。个人配置与系统网络未修改。

# 2026-11-06：无账号渠道独立实验环境

rqbit9.1.0官方Windows12,760,064字节与TorrServer MatriX.149标准Windows63,884,800字节，下载SHA256已核对，保存在.tools/channel-bench/bin；不安装系统服务。新GUID配置/下载目录、仅127.0.0.1管理API、随机Basic认证、负向无认证检查通过。关闭UPnP端口映射/LAN广播，上传128KiB/s、连接160；TorrServer保留整片校验，64MiB缓存，预缓存门槛0。测试串行，已退出自己的引擎进程，TorrServer临时accs.db已置空。

baseline-0820副本含安装版App/Core/worker并逐一核对摘要；不改变正在运行的0.8.20，未触碰个人配置。测试工具复用.NET10/WPF/LibVLC headless软件解码，无新增产品依赖。分片核对只读迅雷历史完整文件，不是再次迅雷网络对照。六份记录和ACCELERATION_CHANNELS.md为本轮事实入口。

# 2026-11-06：发现复用实验环境

安装版仍0.8.20；源码为未发布的复用候选。公网试验串行、同裸磁力、新GUID视频缓存；迅雷/qBittorrent退出后测量。Thunder TaskDb.dat仅mode=ro读取指定hash的渠道统计，无账户字段。测试为libVLC headless软件解码与时钟，不是整片可见画面验收。候选3第三轮因已连续不达标主动停止，保留部分数据标记。中止测试留下的 .tools/comparison-cache/2e6e57e40ccf4e4ebd61ff672d4332f3 清理命令被自动审批blocked by policy拒绝，未绕过，目录保留。详见 DISCOVERY_0821.md。

# 2026-11-06：0.8.20 调度改善已安装，快速起播仍开放

同原始磁力全新缓存串行对照：基线101秒起播/后120秒停滞58秒；采用候选91秒/停滞2秒，实际WPF复测63秒/未记录停滞。不是全片流畅或稳定秒开保证，视觉遮挡限制见 RELEASE_0820.md。98工作台、12调度、发现重试、worker边界及MKV seek/清理通过。备份20261106-190917-129，新进程49640，安装及4配置文件摘要匹配。下一步原生发现复用须独立验证冷/暖启动。负责人Codex，已明确分工证据。

# 2026-11-06：0.8.19 冷启动节点发现恢复已安装

同磁力 qBittorrent 可取数；AFMC 经有限 DHT 重查，候选26.8秒元数据、76.1秒实际起播。98工作台及组件边界检查通过，安装/配置摘要已核对，备份20261106-184807-908，新进程3772。仍未达到快速或稳定流畅播放，不保证所有资源。详见 RELEASE_0819.md 与 evidence/qbit-comparison。负责人 Codex。

# 当前安装环境：0.8.18

最终进程40844，备份20261106-004503-415；来源偏好及bak摘要保持，候选摘要匹配。无新增依赖。见 RELEASE_0818.md。

# 历史安装环境：0.8.17

原生 WPF/libVLC/libtorrent 技术栈不变，无新增依赖。安装进程 72844（验收快照），备份 20261105-235454-434；四个来源/偏好文件及备份摘要一致，候选摘要一致，播放缓存空。见 RELEASE_0817.md。

# 历史安装环境：0.8.16

最终运行进程30340（验收快照），原生技术栈与依赖未变。备份230426-488，原0.8.15回退230044-143。配置/备份与候选摘要一致；安装和已知限制见 RELEASE_0816.md。

# 0.8.14 / 0.8.15 环境（2026-11-05）

沿用原生 WPF / .NET SDK 10.0.403，LibVLCSharp.WPF 3.10.2 与现有 libtorrent 下载层，无新增产品依赖。使用项目 `.tools/dotnet/dotnet.exe`；系统 PATH 的 dotnet 无 SDK，不能直接用系统入口代替项目环境。拖动测试在当前显示器 150% 缩放执行，未覆盖多屏/DPI切换。安装位置、配置和回退流程不变；实际发布状态以 RELEASE_0814.md / RELEASE_0815.md 为准。

# 0.8.13 安装环境（历史）

原生UI更新，无新增框架/字体/运行时依赖；SDK和播放引擎沿用下段。安装目标 `%LOCALAPPDATA%/Programs/AFMC FIND`，本轮实际主窗口1980×1290像素/1320×860DIP（150%缩放）。最终进程45232为验收时快照；安装/备份/哈希和限制见RELEASE_0813.md。Windows前台激活在多次UI自动化中被拒绝，不能将此环境状态当作快捷键产品故障。

# 0.8.12 安装环境（历史）

.NET SDK10.0.403 / WPF net10.0-windows / win-x64，主程序安装于 %LOCALAPPDATA%/Programs/AFMC FIND。默认 BT 下载层为随应用分发的 Python3.12.10 嵌入式与 libtorrent2.0.15（原生2.0.15.0），约32MiB，固定下载摘要、文件清单与许可均验证。LibVLC3.0.24 x64复用既有播放器目录；MonoTorrent保留用于显式测试/对照。未更改系统网络、驱动或全局Python。最新安装与验收见 RELEASE_0812.md，旧环境段落为历史记录。

# 0.8.10 公网诊断环境（历史）

产品仍使用现有 MonoTorrent / LibVLC。额外诊断环境为 .tools/swarm-compare-venv（Python 3.12，官方 PyPI libtorrent==2.0.15 Windows wheel），不属于产品依赖或安装包。独立引擎不启用 UPnP/NAT-PMP/LSD；比较限时 120 秒，下载上限 1 MiB/s、总量阈值 16 MiB。缓存仅写项目内 GUID 目录，结束清理并记录结果。

# 0.8.4 播放器

联网验收使用 Cinemeta 与 OpenSubtitles v3 Stremio 插件，得到 2002 年 Spider-Man 的候选并实测英文字幕。播放测试使用原创媒体和受控做种。

# 0.8.2 播放与资源操作

WPF / .NET 10，沿用 MonoTorrent 和 LibVLC。当前网络实测 Transmission 的 DHT 引导入口返回有效事务应答。安装版实测记录在 docs/evidence/v082。

# 0.8.0 播放环境（已安装）

.NET SDK10.0.403/WPF/win-x64；MonoTorrent3.0.2、Mono.Nat3.0.0、ReusableTasks4.0.0、LibVLCSharp/WPF3.10.2。LibVLC3.0.24 x64位于%LOCALAPPDATA%/AFMC/Find-player/libvlc-3.0.24-x64，独立于主安装包，已准备。安装版0.8.0进程54736；备份20261105-005832-183。未修改系统关联、路由器、防火墙。测试使用原创AVI/PCM与受控回环BT/HTTP；停止后缓存目录为空。

# 0.7.11 安装环境

SDK10.0.403/.NET10/WPF；新增Microsoft.Playwright1.64.1及锁定传递依赖，驱动只发布win32_x64。现有Edge位于C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe。无浏览器安装、全局网络配置更改或常驻服务。安装版66580通过真实探测，原67616正常退出。备份20261104-233005-534；来源/偏好哈希未变，发布/安装程序集一致。安装版探测结束Node驱动已退出，个人配置未用于自动化测试写入。

# 0.7.10 — 磨砂窗口与深层识别已交付

0.7.10 沿用 .NET10 WPF/SDK10.0.403，无新增包、浏览器或后台服务。备份20261104-222250-859；旧59744正常退出，无强杀；新72272经退出验收后重启67616。安装版三个窗口DWM backdrop=3，前台截图已查看，主机缩放150%；未验证完整多屏/DPI矩阵。来源、偏好与备份哈希一致，两个安装程序集与候选一致。

# 0.7.9 — 网址自动识别与目录类别已交付

SDK 10.0.403 / .NET10 原生 WPF，沿用现有依赖。安装 0.7.9，旧60296正常退出；新52188经退出清理验收后重启59744。备份20261104-215126-487。使用既有系统代理，未更改网络设置。网址探测校验DNS和实际连接目标；HTTPS CONNECT通过已配置代理端点识别兼容。来源/偏好哈希保持，发布与安装程序集一致。

# 0.7.8 — 内置引擎已交付

安装版0.7.8，最终进程60296；内置Jackett0.24.3009，固定官方SHA256与对应源码随包。现有系统代理可继承，不修改系统代理；本机管理接口不走代理。备份20261104-210911-607。引擎只在需要时启动。

# 0.7.7 — Jackett 简化配置已安装

保留“添加 Torznab 源 / 配置 Torznab 搜索源 / 连接你的搜索源”名称。
输入自己的 Jackett 地址与 Key（完整 Torznab 链接可提取 Key），读取已配置来源、勾选导入；重复端点跳过，已有来源仍可编辑。凭据沿用 Windows 用户加密保存。未安装 Jackett 后台服务，未复制 Jackett 源码或新增依赖。

新增15项解析、模拟HTTP、认证失败、原生窗口和模态导入检查；完整320项检查通过，构建0警告0错误。安装版UIA验证入口、窗口名称、读取按钮可见、未连接时不可导入；布局截图已查看。默认9117无监听，真实Jackett服务/真实上游搜索未验证，不能将模拟响应当作真实联通。

安装版本0.7.7，新进程65792，旧67084正常退出，无强制关闭；备份20261104-204220-427；配置预检、发布/安装程序集一致性通过。工作树codex/initial-development未提交。证据docs/evidence/v077；首次测试程序退出生命周期错误及修复见failures.md。下一步连接用户运行的Jackett后验证真实来源导入和搜索。
# 环境

0.7.6：沿用SDK/WPF及已有依赖。安装PID67084，旧PID53860正常退出，备份20261104-201510-531；sources/preferences与候选/安装两个程序集哈希通过。当前窗口DPI150%，三列表头/数据实际右边缘0像素差；不代表全DPI/多屏矩阵。FOSS完整RSS及torrent文件HTTPS200；Commons/Openverse本机超时、PDT403。未改变系统网络设置。

0.7.5：沿用SDK10.0.403/WPF，无新依赖。APIBay真实JSON200，官网两个HTML请求20秒超时，不修改DNS/代理/证书。固定目录新PID53860，旧PID54324正常退出；备份20261104-195644-368。来源/偏好哈希保持，安装/候选两个程序集一致。

0.7.4：2026-11-04，SDK10.0.403及现有WPF环境，无新包。新源通过应用HttpClient HTTPS搜索；未改系统网络/安全设置。固定目录安装PID54324，旧PID61216正常退出；备份20261104-192439-017。sources/preferences哈希保持，候选/安装两个程序集哈希一致。验证结果和网络波动见evidence/v074。

0.7.3：沿用SDK，无新依赖。固定目录安装PID61216，旧PID66868正常退出，备份20261104-190450-822。LinuxTracker与Debian真实HTTPS200且种子元数据验证通过；bt.etree.org403。sources/preferences哈希保持，不改系统设置、下载关联。

0.7.2安装验收：214检查、DLL候选/安装哈希一致、来源/偏好保持，PID66868；备份Find-install-backups/20261104-182417-956。沿用本地SDK/WPF环境，无新依赖，无系统关联更改。

## 0.7.1 验收快照
SDK10.0.403/WPF，构建零警告错误。安装PID25288，旧PID34220正常退出。备份20261104-170057-644，sources/preferences保持。隔离qBittorrent5.3.0仅解压到.tools/qbt-validation/client，未系统安装；官方安装包SHA256 AFF5B3BDC00D6E5773D6099BA30F9696DD3DFD1669AB0F64CE5C4E7168486436。测试认证配置仅在忽略的.tools，不输出。Chrome可读预览，filePath输出被工具根目录限制拒绝，改用内联截图保存，不改权限。

核验日期：2026-11-04（用户时区 Asia/Shanghai）。以下启动检查为历史状态，后续开发状态见末尾。

- 工作区：Windows / PowerShell，初始空目录，无适用磁盘 AGENTS.md；本轮用户提供的 AGENTS 规则适用。
- git rev-parse --show-toplevel：fatal: not a git repository (or any of the parent directories): .git。属于新目录状态，非产品失败。批量 shell 最终退出码为 0，未单独捕获 git 退出码。
- dotnet --list-sdks：无输出；仅能确认当前 dotnet 未列出 SDK，未扫描其他安装路径。
- python --version：Python 3.12.10。
- 预览：单文件 HTML/CSS/JS，无安装依赖。Chrome 工具能打开 file URL。
- 尚未检查目标下载软件、magnet 注册关联、Jackett/Prowlarr 服务、真实源连通性。
- 未安装运行时、服务或下载引擎，未修改系统设置。

## 开发完成时
- SDK 10.0.403 已解压 `.tools/dotnet`，来源 builds.dotnet.microsoft.com，SHA-512 `7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48`；不改全局 PATH。
- SDK 首次运行输出提示自动创建 ASP.NET 开发证书；没有执行证书信任命令。后续 Build.ps1 设置 DOTNET_GENERATE_ASPNET_CERTIFICATE=false。
- 系统已存在 .NET 10 桌面运行时；产品仍采用自包含发布。
- Windows UIAutomationClient/Types 可用，已验证实际发布窗口；运行截图见 published-window.png。
- 注册表只读初查：magnet 注册且存在 OpenCommand；.torrent 注册但未在直接默认 ProgID 路径发现命令。该结果不等于完整 Windows UserChoice 解析，实际关联仍待交接测试。
- 没有启动 Jackett/Prowlarr 后台服务，没有安装下载引擎或改变系统关联。

## 0.2.0
新增 NuGet HtmlAgilityPack 1.13.1，包锁文件已更新。网站访问保留严格TLS；黑马搜索403、BTAA证书/超时证据见source-audit。工具输出乱码曾被误判为网页编码，已通过严格UTF8解码纠正；产品使用UTF8。

## 0.3.0
沿用SDK与现有依赖，无新包。Archive services/img真实JPEG读取及WPF解码通过。TPB/种子楼第2页与第1页不同，第三页集成测试通过。源查询可能出现后续页跳转，保留前页并提示。
# 0.4.0 增量

沿用 .NET 10.0.403 SDK、现有依赖和锁文件。Knaben API v2、BitSearch API、Nyaa RSS 本机 HTTPS 请求通过；1337x/LimeTorrents 403、SolidTorrents TLS 失败。不改变系统配置或证书校验。

# 0.4.1 / UI 预览

无新增依赖。无极磁链入口与API本机可访问，但普通网页结果缺少磁力。Chrome用于独立HTML预览，1441x901与1081x761布局检查通过；未验证新的原生UI，因为尚未实施。



## 0.5.0 验收环境
本机 Windows 10.0.26300.0，SDK 10.0.403，自包含 win-x64。系统磨砂 DWM 返回 0/type 3。其他系统与完整 DPI/多屏未测。


0.5.1无新增运行时依赖；图标封装使用Windows System.Drawing，图像创作使用内置imagegen。


0.6.0使用随WindowsDesktop运行时的System.Windows.Forms.NotifyIcon，不引入第三方托盘包。UseWindowsForms开启，移除Forms/Drawing隐式命名空间避免WPF冲突。自包含包约76MB。


## 2026-11-04 · 0.6.7

0.6.7继续使用现有.NET10/WPF/HAP，无新增第三方包或系统服务。安装路径%LOCALAPPDATA%/Programs/AFMC FIND；配置路径不变。

## 2026-11-04 · 0.7.0

0.7.0沿用独立SDK10.0.403、WPF net10.0-windows、自包含win-x64和现有依赖。固定安装目录仅新进程34220；来源/偏好配置与升级前备份哈希一致。
## 未发布 libtorrent 原型环境（2026-11-05）

`.tools/libtorrent-runtime` 是固定 CPython 3.12.10 + libtorrent 2.0.15 的应用内组件源，构建复制到 `components/libtorrent`；启动使用 `-I`、固定脚本路径和独立缓存 GUID。`.tools/swarm-compare-venv` 是诊断/生成样本环境，不是用户运行依赖。未改全局 Python、代理、防火墙、路由器或驱动。安装版仍 0.8.10，未发布组件候选。

# 0.8.16 environment

No new runtime, NuGet or native dependencies. Same WPF/.NET10 and embedded libtorrent2.0.15/LibVLC3.0.24. Current desktop150% scaling. Fresh official torrent HTTPS fetch failed TLS while cached official torrent and separate movie tracker tests reached actual decoding. Network failures and limited samples are recorded separately. Final installation state is authoritative in RELEASE_0816.md.



