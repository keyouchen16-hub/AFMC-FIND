# 正式磁力渠道调查与无账号开源对照

日期：2026-11-06（本机北京时间，测试跨时段时以证据为准）。用户明确没有服务账号，先测无需账号的开源方案；只有同磁力起播和持续播放确实改善后才集成安装。

## 范围与正式入口

以下结论来自项目或服务自己的文档；开源客户端/SDK不等于开源或免费的云资源池。没有注册账号、购买服务、发送联系邮件、提交商业服务测试任务或读取用户密钥。

|方案|正式入口与定位|本轮决定|
|---|---|---|
|rqbit 9.1.0|Apache-2.0 Rust BT引擎，HTTP API/Range流。https://github.com/ikatson/rqbit/tree/v9.1.0 |本机隔离实测；不是额外云缓存渠道|
|TorrServer MatriX.149|GPL-3.0 Go/anacrolix BT流服务。https://github.com/YouROK/TorrServer/tree/MatriX.149 |本机隔离实测；采用不含GST的Windows官方二进制|
|TorBox|官方SDK提供create_torrent、下载链接与缓存查询。https://github.com/TorBox-App/torbox-sdk-py ，https://api-docs.torbox.app/ |NOT_RUN_AUTH_REQUIRED；SDK开源不代表云服务开源|
|Real-Debrid|官方REST的addMagnet/selectFiles/info/unrestrict；需要认证。https://api.real-debrid.com/ |NOT_RUN_AUTH_REQUIRED；未将文档未列出的旧缓存接口当作可用|
|AllDebrid|官方magnet/upload/status/files等，需要API Key/PIN。https://docs.alldebrid.com/ |NOT_RUN_AUTH_REQUIRED；未实测账号能力、配额或网络限制|
|Premiumize|官方transfer/create、transfer/directdl、cache/check，需要认证。https://www.premiumize.me/api |NOT_RUN_AUTH_REQUIRED；不宣称旧转码字段仍可用|
|put.io|官方Go SDK提供transfer/download链接，OAuth。https://github.com/putdotio/go-putio |NOT_RUN_AUTH_REQUIRED；未确认独立缓存预检能力|
|DebriDav|MIT开源WebDAV/qBittorrent API桥接。https://github.com/skjaere/DebriDav |仍依赖debrid账号，不满足本轮无需账号要求|
|zurg-public|发布/配置仓库。https://github.com/debridmediamanager/zurg-public |需要debrid账号；未确认完整引擎开源及再分发条件|
|迅雷下载SDK|官方快速接入文档目前仅开放HTTP(S)，不支持BT/磁力/ed2k。https://open.xunlei.com/doc/download/guide/quick-start |不能直接当作独立AFMC磁力加速入口|
|WebTorrent|桌面/Node可使用BT，浏览器依赖WebRTC，仍需要实际供种端。https://webtorrent.io/faq |不是自动提供该hash额外供种的云池，本轮未增加第三个新运行时|

没有发现并验证“免费、无需账号、可正式接入、能让这条磁力稳定秒开”的额外服务池。此结论限于已调查和已测试范围，不代表此类服务绝对不存在。

## 隔离与可重复方法

- 唯一输入：`magnet:?xt=urn:btih:2EE672984434179AA8A7C5D2EBD0A94B38EB2960`，无预先torrent文件、视频缓存或已知peer注入。
- 官方下载和固定SHA256见 `scripts/Prepare-ChannelBench.ps1`、`.tools/channel-bench/bin/manifest.json`。rqbit 12,760,064字节，TorrServer 63,884,800字节；实际发布增加体积不能只按本体大小估算。
- 每轮新GUID目录，独立HTTP回环端口、随机临时Basic认证；负向请求验证管理API拒绝无认证访问。不开机启动、不注册服务、不改系统防火墙/驱动/文件关联。
- 关闭UPnP自动端口映射与LAN发现；上传限128KiB/s、连接上限160、下载不限速，与AFMC配置相当。各引擎发现与调度算法不同，不声称完全相同的peer集合。
- rqbit关闭DHT及会话持久化；TorrServer64MiB内存缓存、PreloadCache=0、ResponsiveMode=false以保留整片校验、关闭Bonjour/DLNA/LPD。TorrServer默认公开retracker模式保留，属于候选自身发现能力，不是商业加速。
- 同机串行，不和迅雷/qBittorrent并发。现有AFMC主窗保持运行但无原生下载worker。公网peer供给随时间变化，未控制ISP/NAT/远端负载。
- 起播：同AFMC的LibVLC软件解码，至少10帧且播放时钟>=1秒。之后观察120秒，总时限300秒。每秒时钟前进<=20ms记近似停滞，不是连续可见画面/音频或全片保证。
- HTTP Range读取保持同一响应直到seek，避免每128KiB重复建请求干扰预读。独立fixture检查连续读取复用、seek、EOF及错误200响应拒绝。
- 新增 `ChannelComparisonChecks.cs` 只在显式测试开关运行；没有接到主程序。日志只保留摘要/数字状态，不保留peer端点或认证信息。测试结束杀掉自己启动的子进程并清空本轮生成的临时认证文件。
- `Prepare-ChannelBaseline.ps1` 用新测试入口承载安装版0.8.20的App/Core/worker副本，逐项核对SHA256，既不修改安装目录，也不把未发布节点复用候选误称已安装基线。

## 实测结果

四轮均使用新缓存并完成起播后120秒观察。按实际执行顺序如下；JSON原始值为准，表格保留两位小数。

|轮次|元数据秒|实际起播秒|后120秒停滞秒|结果|
|---|---:|---:|---:|---|
|rqbit 冷启动1|1.67|61.43|64.41|起播较早，但长时间停滞，拒绝采用|
|TorrServer 冷启动1|5.35|160.16|75.50|起播与连续播放都不达标，拒绝采用|
|安装版0.8.20同期基线副本|25.95|126.01|24.15|仍慢且有卡顿，不能宣称现版本问题已修复|
|rqbit 冷启动2|1.46|110.93|0.00|此轮较稳，但仍等待近两分钟，无法推翻首轮严重卡顿|

完整证据：`evidence/channel-bench/summary.json`、各目录`result.json`/`afmc.json`。摘要脚本拒绝读取仍在Running的轮次；测试收集器退出0只表示完成采集，不代表性能通过。公网串行有限轮次不构成统计显著性结论；不将单轮0停滞解释为全片或所有资源流畅。

### 进一步定位

- rqbit第二轮约44.38秒：引擎累计fetched_bytes=49,299,456，downloaded_and_checked_bytes=2,801,664，HTTP交付=0；下载速率显示1.92MiB/s。这直接说明累计抓取量/瞬时速度不能代表当前所需的完整校验片已到齐。不能仅凭差额断言全部是重复数据、哈希失败或某一种调度错误。
- rqbit第一轮与基线均在约57.35秒播放位置停滞；基线在约205.5秒取得更多校验分片后恢复，说明并非该位置始终不可解码。
- 额外本地控制：`Check-ComparisonLocalPieces.ps1` 只读验证历史`.tools/comparison.torrent`的原始info字节SHA1就是目标磁力；本地视频1024个完全位于视频内的完整分片全部符合种子SHA1，覆盖2,868,903,936字节，跨文件边界的不完整片未验证。
- `--comparison-local-segment` 使用同一LibVLC与PlaybackStreamInput，只读打开该完整本地文件，连续解码45–80秒区域通过，停滞0秒。结果见`local-segment.json`；它用于排查该段解码路径，不是重新下载、迅雷起播计时或全片可见画面验收。

### 发布决定

**不集成这两个候选、不发布新版本、不覆盖安装。** 当前安装保留0.8.20。第二轮rqbit有局部改善，但两轮起播61/111秒且首轮停滞64秒，未满足稳定改善起播与卡顿的条件。源码产品部分也没有接入新引擎，只有测试入口、隔离脚本和调查记录；前轮未发布发现复用候选保持原样。

下一步可继续取证当前必需分片的可用peer、未完成请求和校验完成时序，而不是再以节点总数/累计字节推断可播。需要账号的正式云渠道保留调查记录，本轮不要求用户注册，也不把未测试的服务宣称为有效解决方案。

## 失败与限制记录

- 首次Range fixture新增时触发CA2022三处未使用读取长度，已改为断言实际读取数；随后fixture误把seek到当前位置预期为新HTTP请求，改成真正非连续seek后通过。两次均发生在第二引擎测试前，不修改第一轮真实测量。
- 停止解码可能产生imem/PCR/H264取消日志；未将停止阶段日志当作播放时崩溃，也未据此宣称视频已损坏。
- 本轮未修改产品版本或播放器UI，未添加商业依赖。历史被审批拒绝清理的comparison-cache目录保持原状，没有换工具重试删除。
- rqbit首轮在增加后端计数采样前执行，具有完整解码/时钟/HTTP字节记录，但无后端下载计数；后续增加的采样没有改变HTTP读取和起播判定。引擎自身缓存、预读策略和发现算法不同，是所比较的方案差异。

## 后续决策标准

只在新缓存重复轮次的实际起播与120秒播放均有改善，再进入原生可见播放、seek/取消/关闭/回退和安装验证。元数据更快、累计下载更大、更多peer、单次最快均不足以通过。

libtorrent官方指出顺序下载可让关键片等待慢peer、而快peer在取后续片；重复加急也可能浪费带宽：https://www.libtorrent.org/streaming.html 。这是诊断方向，不是本轮已证实的单一根因。当前资源的关键证据仍应是“播放器当前需要的已校验字节何时到达”，而不是仅看总下载量。
