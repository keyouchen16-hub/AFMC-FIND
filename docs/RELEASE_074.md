# AFMC FIND 0.7.4 — 来源扩容

2026-11-04，已覆盖安装并验收。用户当前优先搜索来源扩容；没有改筛选窗口或主界面布局。

## 实现

动漫花园公开关键词RSS：中英文搜索、最多100条、标准化磁力哈希、同站HTTPS详情页。没有可靠大小/做种时保持未知。EZTV通过TVmaze精确剧名映射最多2个IMDb编号，再按编号读取每个最多3页100条，并核对标题、季集/清晰度约束和磁力哈希；同哈希去重，后页HTTP失败保留已有结果并报告错误。共享有界5分钟内存缓存，无新增依赖/后台服务。目录从13到15个聚合源，不包含原站导航入口。

## 验证

- `Build.ps1 -Publish`退出0：构建0警告0错误，252项检查（59核心+26扩展+16软件源+20媒体源+16UI+32质量+44完整性+29工作台+10托盘）。UI及分页异常用受控数据，不能视为所有网站或物理输入验证。
- 新源真实联网：naruto动漫花园79条，海贼王100条，Sherlock EZTV14条，所有返回磁力哈希检查通过；见media-live.json。
- 安装版UIA真实搜索：naruto1137条去重结果，动漫花园79条；Sherlock1050条，EZTV14条/动漫花园22条。两组均14源成功（包括合法空结果）/1源失败。见installed-naruto.json、installed-sherlock.json与对应截图。
- LinuxTracker naruto/Sherlock返回“Torrent data is unavailable / not connected to legacy catalog”，明确显示失败。HTML证据linux-naruto.html，不宣称15源全部可用。
- 错误索引回归：eztv.json捕获的77条Breaking Brad挂在Breaking Bad IMDb下，全部拒绝；首次非空检查失败诊断保留research-failures.md。没有削弱标题校验。
- 未调用下载客户端、未下载资源正文；未证明种子可连接/内容真实性，也未重做全查询准确率基线。

## 部署

`%LOCALAPPDATA%\Programs\AFMC FIND`版本0.7.4，旧PID61216正常退出，新PID54324；LegacyForced为空。备份`%LOCALAPPDATA%\AFMC\Find-install-backups\20261104-192439-017`。配置预检通过，sources/preferences与备份哈希一致，候选/安装AFMC.Find.dll与Afmc.Find.Core.dll一致。证据install.json、install-integrity.json、release-build.log。原包及备份保留；未提交、推送。

## 来源研究与后续

- [TVmaze官方API](https://www.tvmaze.com/api)、[EZTV官方API](https://eztvx.to/api/)、[动漫花园](https://share.dmhy.org/)：协议/归属和实际响应均核验。TVmaze CC BY-SA归属见随包licenses/THIRD_PARTY.md。
- [AnimeTosho停更与关闭公告](https://animetosho.org/)：旧RSS/API不能作为新增源；不把继任站未经实测当作已支持。
- Torlock、ACG.RIP、AniRena本机探测超时，未纳入；旧有bt.etree.org403仍待外部条件改变。
- 后续按其他资源领域继续验证；EZTV精确名称和中文别名覆盖有限，页数受上限限制。资源条数是该时点结果，不是全站总量或新增独立资源数量；不同聚合源可能重叠。
