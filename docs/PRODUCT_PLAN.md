# AFMC FIND — 首版产品方案

状态：2026-11-04 用户已批准真实开发，本文件保留设计方案与目标验收；实际实现及未验证边界以 PROJECT_STATE.md / VERIFICATION.md 为准。用户已确认只搜索，调用已有下载软件。

## 产品定位
一个轻量 Windows 桌面入口：输入关键词，在已配置的多个来源中查找种子；合并重复资源、展示来源与质量线索，再交给已有下载软件。名称暂定 AFMC FIND。

## 开源复用对比

| 项目 | 官方提供的能力 | 本项目的取舍 |
|---|---|---|
| [Jackett](https://github.com/Jackett/Jackett) | 把站点查询转为统一 Torznab/TorrentPotato 结果，含 Windows 支持 | 首选搜索服务，通过 API 接入；不是全网种子数据库 |
| [Prowlarr](https://github.com/Prowlarr/Prowlarr) | Torrent / Usenet 索引器管理与代理 | 通用 Torznab 的兼容候选；无需与 Jackett 同时部署 |
| [qBittorrent 搜索插件](https://github.com/qbittorrent/search-plugins) | qBittorrent 搜索插件生态 | 参考成熟搜索设计；首版不在本应用中直接执行第三方 Python 插件 |
| [qBittorrent Web API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29) | 搜索任务与结果接口 | 已有 qBittorrent 且配置 API 的用户可作为后续适配路线，增加运行和认证依赖 |
| [aria2](https://github.com/aria2/aria2)、[rqbit](https://github.com/ikatson/rqbit)、[MonoTorrent](https://github.com/alanmcgovern/monotorrent) | 下载或 BitTorrent 引擎候选 | 用户已排除首版内置下载，本轮不选型、不安装 |

以上为官方文档能力核对，不是本机集成测试。软件支持某站点不代表该站点当前可达、有结果或允许匿名访问。第三方许可与固定版本须在实际引入前逐项落账。

许可初筛：Jackett 仓库元数据标为 GPL-2.0；Prowlarr README 标为 GPL v3；qBittorrent COPYING 区分源码 GPLv2+ 与二进制发行 GPLv3+。这是候选初筛，不替代锁定版本后的完整分发检查。来源：[Jackett](https://github.com/Jackett/Jackett)、[Prowlarr README](https://github.com/Prowlarr/Prowlarr/blob/develop/README.md)、[qBittorrent COPYING](https://github.com/qbittorrent/qBittorrent/blob/master/COPYING)。

## 推荐结构

Windows WPF UI → SearchCoordinator → TorznabProvider → 已配置的 Jackett / 兼容服务。
结果 → 用户点击 → ExternalDownloadLauncher → Windows 默认 magnet / .torrent 处理程序。

自有部分包括 UI、搜索编排、结果标准化、去重、过滤、配置、错误反馈及启动交接。站点解析优先复用上游，不首批维护大量网页爬虫。先支持用户配置的服务地址与 API Key；服务随安装包交付另做验证，不承诺开箱即可搜遍全网。

Jackett 官方建议支持多源的客户端单独添加索引器；其 all 端点会受慢源拖累，结果上限为 1000。因此建议先取能力信息 t=caps，再按源并发、限流和独立超时；已有结果渐进显示。Windows 下自定义 DataFolder 参数还涉及管理员权限，便携服务方案不能直接假定可行。[官方说明](https://github.com/Jackett/Jackett#api-usage)

## 首版功能

1. 搜索源：添加/编辑服务、选择来源、连接检测；分别显示未配置、认证失败、超时、不可达。
2. 搜索：关键词、取消、逐源结果反馈；旧查询不能覆盖新查询，部分失败保留成功结果。
3. 结果：标题、大小、分类、做种数、发布时间（源提供时）、来源；缺失数据用“未知”，不能以 0 代替。
4. 去重：只在可靠哈希相同的情况下合并来源；支持规范化 v1 编码与 v2 标识，不按同名强行合并，无可靠标识保留独立项。
5. 详情与操作：磁力链接复制、默认软件下载、打开来源页。只有 .torrent 下载 URL 的结果应校验下载内容、大小和扩展名，再交给关联程序；不能把详情网页当种子。
6. 设置：来源配置与界面偏好持久化；API Key 使用 Windows 用户级保护，日志移除密钥、Cookie、私有 tracker passkey。

正式版本可包含更多资源分类；预览中的“系统镜像/开放影片”只是演示分类，不限制最终搜索类别。源的具体中文覆盖率要在真实联调后再承诺。

## 用户流程和状态

- 初次启动：没有来源 → 引导连接 → 测试成功 → 搜索；不展示假在线状态。
- 搜索中：先显示返回结果，保留取消入口；超时提示精确到来源。
- 搜索完成：无结果与全部请求失败必须区分。
- 点击打开：成功只能提示“已请求打开”，不能声称下载已开始或完成。
- 无默认关联：提示系统设置路径并允许复制链接；不替用户改默认程序。

## 验收标准

- 用两组真实公开资源查询检验结果字段、来源和链接；中文/英文查询分别记录实际表现。
- 两个来源的同哈希结果合并；同名不同哈希不能合并。
- 模拟一源超时、一源成功，成功结果保留；取消后不追加旧结果。
- 未配置、密钥错误、零结果分别展示正确反馈；日志不泄漏密钥。
- 用户点击磁力链接与 .torrent，分别验证外部软件接收；无关联机器/隔离测试验证回退。不得把 Shell 启动成功当下载成功。
- WPF 窗口缩放、键盘搜索/选择/打开、长标题、重启后配置恢复均需原生验收。

## 当前预览

[打开交互预览](../preview/index.html)。深色工具型界面，黄绿色标识主要动作，三栏分别承载导航、列表、详情。所有数值均为模拟数据；没有真实搜索、磁力链接或外部调用。

该预览阶段已完成确认。0.1.0 已实现原生桌面界面和公开源真实搜索，后续联调用户 Torznab 服务及外部软件接收。仍不开发下载引擎、在线播放、账号云同步、全网 DHT 爬虫或自动安装后台服务。
