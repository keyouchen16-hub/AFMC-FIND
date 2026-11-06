## 2026-11-07：分片重新分配与上传检查

新增 Check-PieceReassignment.py（真实loopback BT wire，不是电影）、Patch-NativePieceRetry.py、Build-NativeStreamCandidate.ps1、NativePieceRetryCandidate.py、Check-NativePieceRetry.py、Prepare-NativeRetryBench.ps1、Summarize-NativeRetry.py。原始结果留 docs/evidence/piece-reassignment 本机；Check-RepositoryUpload.py 检查实际 Git 文件列表并只报告敏感匹配位置，不显示值。

# 2026-11-06：必要分片追踪工具

scripts/Prepare-PieceTrace.ps1 创建拒绝覆盖的隔离副本；可分别启用 DeadlineScheduler、ShortBlockTimeout、RelaxEndGame，缺省只追踪。PieceTrace.py 输出匿名必要块与供应时序；PieceDeadlineCandidate.py 为隔离窗口算法；Check-PieceDeadlineCandidate.py 验证 5 个边界。Summarize-PieceTrace.py 可读进行中快照；Summarize-PieceScheduling.ps1 拒绝 Running 结果，汇总已结束轮次及结果/worker SHA256。AFMC_PIECE_TRACE 指定追踪文件，AFMC_COMPARE_OUTPUT/SECONDS 控制现有真实解码基准。见 PIECE_SCHEDULING_0821.md。

DiscoveryTrace.py 为隔离 worker 注入网络计数诊断，AFMC_DISCOVERY_TRACE 指定文件，2 秒间隔、600 秒上限，无地址或令牌。PieceRescueCandidate.py 只用于专有空过滤器的隔离会话，不能直接当产品共享过滤器实现；Check-PieceRescueCandidate.py 检查 11 项供给条件。VisibleMagnetChecks 支持证据路径/总预算配置，使用真实 PlayerWindow 并记录遮挡/清理；退出码不替代性能结论。汇总脚本将未起播停顿写 null，避免“未播放=零卡顿”，并计算起播后载荷均值与结果/worker 哈希。最终构建已去除实验注入。

# 2026-11-06：无账号渠道测试入口

Prepare-ChannelBench.ps1固定官方下载/摘要；Prepare-ChannelBaseline.ps1复制安装版三个关键文件到隔离测试副本并核对哈希，不覆盖已有基线。--channel-comparison通过AFMC_CHANNEL_ENGINE=rqbit/torrserver选择本地独立引擎，AFMC_COMPARE_OUTPUT/SECONDS指定证据及总时限；--channel-range-check验证连续HTTP Range复用/seek/EOF/错误响应。Summarize-ChannelBench.ps1拒绝Running记录，汇总四轮原始JSON及摘要。

Check-ComparisonLocalPieces.ps1核对种子infohash及本地视频内1024个完整片（跨文件边界片排除）；--comparison-local-segment只读完整本地文件，实际解码45–80秒用于复核网络测试的重复停滞位置。测试退出0仅表示采集完成，性能采用须看起播/完整观察/停滞；全部开关都不是默认自动下载流程。见evidence/channel-bench和ACCELERATION_CHANNELS.md。首次CA2022和fixture预期错误均记录并修正，最终构建0警告0错误、Range和本地控制通过。

# 2026-11-06：发现复用验证入口

新增 --comparison-reuse：同父进程冷启动+两次复用，视频缓存每次独立；--routing-cache：TTL/上限/并发/资源隔离检查；Check-RoutingReuse.py：公开端点与私有种子边界；Summarize-DiscoveryComparison.ps1：读取阶段指标。Check-BlockedPiece.py只验证归档的失败加急实验，不代表生产代码。evidence/discovery-0821保留全部轮次；candidate3/warm2为主动中止的部分结果。还原文件旧时间戳会令PreserveNewest保留实验脚本，已刷新时间戳并核对源码/测试worker SHA256一致后复测。没有安装。

# 当前验收：0.8.18

新增 --player-click：确认目标 HWND 后发送鼠标，核对可见性/透明度/点命中，空闲与实际解码各三次。测试前移自己的窗口后恢复普通层级；不代表用户现场复现。98工作台检查与安装 TogglePattern 开关/空状态/候选摘要通过。见 RELEASE_0818.md。

# 历史验收：0.8.17

新增 `--source-menu`：实际弹出窗口命中和屏幕采样；`--workbench` 95 项、`--playback` 99 项。安装播放器验收增加 Logo 可见/播放隐藏/停止恢复；`Verify-Player.ps1 -FullscreenButton -SkipDrag` 明确使用按钮并跳过重复拖动，不替代合成双击证据。菜单测试只关闭自己的窗口/Dispatcher。安装日志与失败边界见 RELEASE_0817.md。

# 历史验收：0.8.16

安装版650目录、设置/详情、真实搜索、默认libtorrent直链/磁力、拖动、全屏与三次关闭检查已执行。测试入口、输入类型、失败记录及最终通过项见 RELEASE_0816.md；未获前台激活的截图不计通过。

# 0.8.14 / 0.8.15 验收工具

新增 --search-responsiveness（40 项，接入 Build.ps1）和 --search-live（三查询公开源实测）；Verify-Search0814.ps1 可指定 Version/EvidenceName，核对实际输入、候选文件与配置摘要、首批时间和可见标题。输入焦点不确定时拒绝误归因，保留初次失败报告。

--player-drag 使用测试进程实际鼠标输入、独立不透明背景、窗口矩形及视频时钟取样，拒绝未实际移动的伪通过；覆盖两轮空闲/两轮播放。仅作为显式桌面检查，不强制加入可能无交互桌面的构建。Player-DragAcceptance.ps1 集成到安装播放器脚本，真实拖动后核对位置、输入恢复与时钟；脚本不证明每个显示刷新帧无残影。测试资源为原创 AVI 和回环服务。

# 0.8.13 UI验收工具（历史）

Build.ps1新增 --resource-groups / --resource-display / --ui-motion；原有 --ui、--workbench、--tray、--playback继续验证。脚本Verify-QuietWorkbench.ps1默认UI-only，-Search使用真实ubuntu查询，不注入示例数据，不保存设置；截图严格验证目标进程/前台/窗口范围/遮挡，前台拒绝时标记未验证。安装搜索可能正常写入搜索历史，来源/偏好只比摘要。Ctrl+K脚本当前缺少完整焦点诊断，失败不直接归咎产品。Verify-Player和Verify-PlayerClose版本门槛更新0.8.13。PowerShell验收脚本以异常判断失败，不以可能为空的LASTEXITCODE作为成功条件。

# 0.8.12 验收工具（沿用播放链路）

Prepare-StreamingRuntime.py 下载固定摘要的运行库；Verify-StreamingRuntime.ps1 在构建/打包/安装前核对摘要、许可和隔离导入。--native-playback、--native-mkv 与 --public-playback-soak 是独立真实下载层验收，不能用普通 --playback 替代。Verify-InstalledAvailability.ps1 接受 Query/EvidenceName，Verify-InstalledMovie.ps1 从首项实际点击并观察45秒时钟（至少前进40秒）；必须在准备会话两分钟内连续运行。保留失败日志，不把公开测试片成功冒充用户电影成功。Verify-Player.ps1 与 Verify-PlayerClose.ps1 继续验证实际安装版操作与清理。

# 2026-11-05 后续对照工具（历史）

Compare-Swarm.py 增加 --public-trackers / --tcp-only，仅用于脚本内固定的公开资源哈希，结果写 evidence/v0810/followup。检查程序新增 --public-discovery（10条边界检查）、--tracker-playback、--tracker-playback-library、--tracker-download；后三者为真实网络诊断，失败必须保留，下载控制完成不等于播放验收。MonoTorrent 精确依赖源码 ZIP 缓存在 .tools/monotorrent-source-e78.zip，仅用于阅读，不作为新产品依赖。

# 0.8.10 真实资源诊断

检查程序参数 --spiderman-live / --spiderman-english 只读实际来源配置，限一页搜索并探测十二个不同作品/版本；--public-playback-control 使用官方 Sintel torrent 验证公网传输和解码。这些诊断不替代实际用户电影成功。

scripts/Compare-Swarm.py 使用隔离 Python 环境比较相同 infohash 的 DHT 连接；--with-control 另加官方 Sintel .torrent（带原 tracker）作为正对照。证据写 docs/evidence/v0810，测试资源结束清理。无公网成功时不得以受控测试通过宣布资源可播。

# 0.8.4 播放器

新增功能先检索已有开源项目，确认所需能力、依赖成本和实际可用性，再决定集成。在线字幕验收设置 AFMC_LIVE_SUBTITLE_CHECK=1 后运行 --playback。

# 0.8.2 播放与资源操作

Build.ps1 -Publish、WorkbenchChecks、PlaybackChecks 和 Verify-Player.ps1。安装版受控磁力验收增加点击到首次观察到播放时钟前进的秒数。

# 0.8.0 验证入口（已完成）

Build.ps1完整回归439条PASS；Checks --playback最终46条；--playback-runtime实际下载/哈希/提取47.6秒；Verify-Player.ps1安装版UIA真实直链、暂停、停止、磁力、关闭缓存检查通过。安装版截图校验明确记录Windows前台激活失败，不计作截图通过。Package.ps1保留许可与对应源码；Install.ps1正常退出等待15秒，备份覆盖安装成功。证据docs/evidence/v080，详情RELEASE_080.md。

# 0.7.11 验证入口（已完成安装验收）

实际结果：完整回归389条PASS；最后分享标识/复制改动补跑Workbench56；联网Browser4+Deep4+Discovery3。安装版Verify-BrowserDiscovery与Verify-DialogGlass通过，证据docs/evidence/v0711。ZIP184810500字节，运行文件完整性检查通过。

新增--browser（动态表单/两层无href按钮、网盘类型、取消及重试、固定列表/默认回退对照）、--browser-live（次元狗真实发现、SettingsStore保存后SearchService、负查询、配置无临时令牌）。Build默认加入browser。Verify-BrowserDiscovery.ps1只在安装版网址窗口探测并检查结果类型，不写个人来源配置，检查驱动退出。Package检查Playwright许可证、NOTICE、Node文件及ZIP运行文件存在。使用project-manager/search-first/security-review/reply-timestamp工作流；无新增子Agent。

# 0.7.10 — 磨砂窗口与深层识别已交付

新增 --discovery-deep（13）、--deep-live（4）和 --dialog-glass；Build默认包含深层识别及玻璃检查。--probe-site URL [query]用于复现。Verify-EmbeddedEngine改用用户4kbt网址、验证词保持空；新增Verify-DialogGlass.ps1检查安装版3窗口材质及前台截图。截图先验证焦点，必要时临时连接输入线程后立即解除，未改变系统设置。托盘测试从固定300ms改为最多5秒等待实际图标就绪。命令输出通过Tee-Object保存。

# 0.7.9 — 网址自动识别与目录类别已交付

WebsiteDiscovery + --discovery（33）、--discovery-ui（5）、--discovery-live（3）；前两项加入Build默认回归。Engine UI扩展到11项。Verify-EmbeddedEngine.ps1覆盖安装版网址探测、嵌套目录、分类控件、回环监听、退出清理与重启。Start-Transcript未捕获原生命令标准输出，仅记录打包/安装锁失败；测试结果来自实际命令退出码/控制台，见RELEASE_079.md。未新增Agent。

# 0.7.8 — 内置引擎已交付

新增Bundle-Engine.ps1、Verify-EmbeddedEngine.ps1；检查参数--engine-live/--engine-ui，Build.ps1 -Live纳入验证。代理下载解决直连超时；Windows Job Object+原生WPF+UIA完成生命周期与安装交互验证，无新增Agent。

# 0.7.7 — Jackett 简化配置已安装

保留“添加 Torznab 源 / 配置 Torznab 搜索源 / 连接你的搜索源”名称。
输入自己的 Jackett 地址与 Key（完整 Torznab 链接可提取 Key），读取已配置来源、勾选导入；重复端点跳过，已有来源仍可编辑。凭据沿用 Windows 用户加密保存。未安装 Jackett 后台服务，未复制 Jackett 源码或新增依赖。

新增15项解析、模拟HTTP、认证失败、原生窗口和模态导入检查；完整320项检查通过，构建0警告0错误。安装版UIA验证入口、窗口名称、读取按钮可见、未连接时不可导入；布局截图已查看。默认9117无监听，真实Jackett服务/真实上游搜索未验证，不能将模拟响应当作真实联通。

安装版本0.7.7，新进程65792，旧67084正常退出，无强制关闭；备份20261104-204220-427；配置预检、发布/安装程序集一致性通过。工作树codex/initial-development未提交。证据docs/evidence/v077；首次测试程序退出生命周期错误及修复见failures.md。下一步连接用户运行的Jackett后验证真实来源导入和搜索。
# 工具与能力

0.7.6：延续本会话项目工程/QA技能流程，web核对各站官方接口/访问条件，curl及SearchService实测候选；原生WPF坐标测量发现滚动条实际宽度差异，SizeChanged同步。Verify-Columns.ps1使用UIA及GetDpiForWindow核验安装版，0像素差；查询变化即停止。初次安装UIA搜索控件未就绪/不可见退出1，后续只读确认主界面已暴露控件后重测通过，未据此断言根因。无新Agent或插件。

0.7.5：延续项目工程/QA流程，web及curl核验海盗湾官网/接口；独立原生解析器，无第三方客户端包。WPF路由点击测试完整排序与未知值、UIA InvokePattern检查安装版表头六次动作与工具栏标签。PrintWindow截屏时用户输入与测试查询不同，截图不作为测试查询证明；Verify-HeaderSort增加检测输入改变后停止/跳过截图。没有新Agent或系统配置变更。

0.7.4：沿用project-manager工程/QA/记录模块和reply-timestamp。web查TVmaze/EZTV官方协议与AnimeTosho停更公告，curl保存公开响应，SearchService真实查询，受控HTTP Handler验证分页/错误处理；Build.ps1发布检查与Install.ps1事务安装、UIA实际搜索、PrintWindow截图。没有委派新Agent或新增插件。外部下载客户端本轮未调用。

0.7.3：复用project-manager及本会话工程/安全/测试流程；web查阅官网与Debian官方说明，curl采集公开页面，应用HttpClient执行新源真实搜索/种子元数据验证；HAP解析复用现有依赖；WPF尺寸/滚动检查+安装UIA/PrintWindow验证卡片底边。未新建Agent或安装工具。

0.7.2：沿用本会话技能/工具，PowerShell/.NET自带检查、UIA展开选择真实下拉选项、PrintWindow截图验收。Verify-Toolbar072.ps1区分显示副本和具备SelectionItem的实际选项。没有新增插件或Agent。

## 0.7.1
复用project-manager/reply-timestamp/ECC search-first/security-review/csharp-testing，PowerShell/.NET/WPF UIA；未新开子Agent。50真实查询通过SearchService；qBittorrent真实进程+认证环回API验证停止任务和哈希，非模拟。Chrome分类预览DOM检查不等于WPF产品功能已实现。

| 工具 / 技能 | 本轮用途 | 实际状态 |
|---|---|---|
| project-manager | 产品范围与连续性记录 | 已读取并应用 |
| reply-timestamp | 最终北京时间与命令可见性 | 已读取并应用；首批读取命令执行时尚未读到规则 |
| ecc:search-first | 先查复用方案 | 已读取并应用；按其指令启动一个只读研究 Agent |
| GitHub MCP fetch | 官方 README/API/许可核对 | 已成功读取公共仓库资料 |
| web | 官方资料搜索和归因 | 已调用；不代表站点运行验证 |
| Chrome DevTools | 打开预览及 DOM 交互检查 | 成功；截图保存路径权限失败，内联截图成功 |
| apply_patch | 项目文档与概念页面 | 已使用 |

未调用第三方平台写入、消息发送、部署或安装工具。

## 实现阶段
- csharp-testing：已用于行为测试设计；使用原生 C# 检查程序适配无第三方依赖项目，不声明使用 xUnit。
- security-review：审查 API Key、输入、XML 外部实体、响应上限、协议白名单和 Shell 调用；测试与扫描证据见 VERIFICATION.md。
- verification-loop：编译/类型检查、核心测试、真实网络、原生渲染与发布目标验收；未测覆盖率，无独立 lint 工具。
- .NET SDK：真实构建、检查与 publish 通过。SDK 便携解压，不是系统级安装。
- Windows UI Automation：发布程序真实查询通过；不等于物理输入。
- 本阶段没有新增子 Agent；上轮只读研究结果作为既有资料复用。

## 0.2.0
复用 search-first 工作流与此前研究 Agent，进行了只读并行站点核查。实际使用 Python HTTP探测、GitHub官方许可证读取、HtmlAgilityPack DOM解析、.NET检查与Windows UIA。未执行广告脚本或绕过验证码/TLS。

## 0.3.0
复用只读研究Agent核查分页、封面，不执行广告脚本。使用.NET/WPF、UIA、真实API和图片解码验收。发布脚本曾出现PowerShell Query/query大小写冲突，已修复并保留失败日志。
# 0.4.0 使用记录

使用 project-manager、search-first、security-review 指导；一个只读研究子 Agent 核查上游接口。Python urllib 有界可用性探测、.NET 构建/检查、官方 API 文档核查。无新工具安装。

# 0.4.1 / UI 预览

复用project-manager、search-first研究子Agent；UI使用ui-ux-pro-max和impeccable本地WPF适配。Impeccable context未识别已有WPF视觉实现，人工依据XAML/真实截图及AFMC_UNIVERSE保留原生平台。浏览器截图指定写入路径被工具拒绝，改用原生内联截图。未更新技能或修改其配置。



## 0.5.0 验证工具
scripts/Verify-Glass.ps1 使用 DwmGetWindowAttribute、UI Automation ScrollPattern 和定向 SendKeys 验证发布窗口；不修改系统配置。scripts/implement_v05.py、finish_glass.py 是一次性迁移脚本，不应重复执行。


Build-BrandIcon.ps1：将imagegen正式透明PNG封装为16/20/24/32/40/48/64/128/256 PNG帧ICO；brand_v051.py为一次性迁移脚本，不应重复运行。


新增ResultPresentation规则检查与--presentation-live、--tray验证。--enable-archive为用户本次恢复来源授权的一次性真实验证后启用动作，后续不可无故重放。scripts/recheck_sources.py只读复查，原始证据在v060/source-recheck。


## 2026-11-04 · 0.6.7

0.6.7新增Probe-Sources067.py/Probe-Formats067.py用于有限元数据探测；--expanded检查新增适配器；--expanded-live单独真实查询。Build.ps1发布前包含扩展检查；Verify-Release.ps1改验收固定安装路径并使用PrintWindow。

## 2026-11-04 · 0.7.0

0.7.0增加ApplicationTransaction.ps1及Test-ApplicationTransaction.ps1；Build.ps1纳入quality/workbench/tray检查。Verify-Release与Verify-Brand验证安装目录并使用PrintWindow截图；图片不证明外部DWM圆角。
## 未发布下载组件验证工具（2026-11-05）

`Prepare-StreamingRuntime.py` 从官方归档准备固定版本，校验下载摘要；`Verify-StreamingRuntime.ps1` 校验组件摘要、许可证和隔离模式原生库加载，已接入 Build.ps1。`Check-StreamingWorker.py --ranges` 在现有受控做种夹具运行期间检查鉴权、无选择无数据、无效请求拒绝、短读取连续性和父进程管道退出。Checks 的 `--native-playback` 验证新引擎种子/磁力真实解码和跳转，须先运行 `--serve-player-fixture`；`--native-mkv` 使用 `Prepare-MkvFixture.py` 生成的原创 MKV 及自有做种；`--public-playback-soak` 单独测公网样片，结果不能由受控检查代替。日志保存 evidence/v0811，不包含 API Key 或下载组件令牌。

# 0.8.16 acceptance additions

Workbench checks cover inline website discovery persistence/invalidation, integrated catalog filters, equal detail actions, direct provenance, settings logo/email, stable health ordering. Playback checks add bounded progress policy and unverified-payload labeling. Pointer-drag checks add reverse paths, white-frame detection, settled desktop residue and input diagnostics. Tests mutate isolated settings only. Public control supports an explicit AFMC_CONTROL_CACHED_TORRENT=1 input label; this never claims a fresh remote fetch. Installed Verify-QuietWorkbench now reads the actual integrated catalog and validates detail/settings changes; Verify-Player/Close expect0.8.16.


2026-11-06：官方qBittorrent5.3.0独立解包、原生UIA对照，哈希已核验；WebUI配置自动审批拒绝故未使用。Diagnose-Swarm.py保留有界隔离实验，Check-DiscoveryRetry.py验证重试上限与停止。

2026-11-06：ComparisonChecks支持AFMC_COMPARE_OUTPUT与AFMC_COMPARE_SECONDS，增量JSON记录时钟/帧/停滞；--visible-magnet运行隔离配置真实PlayerWindow。必须读Outcome/PostStartObservationComplete，不能仅凭进程退出0判验收。Check-StreamingSchedule.py离线12项；本批详情RELEASE_0820.md。
