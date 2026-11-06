# AFMC FIND 0.7.6 — 三列对齐与跨类别扩源

2026-11-04，已覆盖安装。用户要求大小/发布时间/做种列左移、减少拥挤并对齐，另研究图书、音乐、摄影、视频影视、软件类来源。

## 已完成

- 三列112/112/68 DIP，列内右侧16 DIP间隔，整体右侧20 DIP留位。表头与行布局统一，读取实际滚动条宽度同步表头右边距，保留虚拟化与已有升降序/选择状态。
- 新增FOSS Torrents公开完整种子RSS；标题全部关键词匹配，最多100项，五分钟目录缓存。未知大小/做种保持未知，GUID不是infohash。来源页面为站点主页，下载地址为具体torrent，交接前沿用元数据校验。
- 公共去重修正：无有效哈希时优先具体TorrentUrl而不是共享DetailsUrl，保留不同版本/平台种子。
- 9个跨领域站点的官方文档/接口调查完成，详见SOURCE_RESEARCH_076.md。只有FOSS本批接入；未把待接入、失败候选加入已启用源。

## 验收

- release-build.log：0警告0错误，305项检查通过（此前280+13 FOSS+12列对齐）。最小/标准/紧凑布局的每列表头/数据坐标相等，右侧留白检查通过。截图columns-minimum.png、columns-standard.png为受控夹具，不是真实网络结果。
- foss-live.json：Blender26条、Krita6条，两个真实torrent文件校验通过，名称与目录文件一致。只读取种子元数据，未下载软件正文或调用下载软件。
- published-verification.json：安装版Blender532条，16源成功/1源失败，FOSS贡献26条；LinuxTracker上游目录不可用仍明确报告失败。源数包含合法空结果；总17入口不等于17独立库。
- installed-column-alignment.json：当前安装窗口150%缩放，三列右边缘误差均0像素；published-window.png真实布局检查通过。没有做全DPI/全部多屏组合测试。
- 两次初期对齐失败、FOSS去重诊断、初次UIA控件不可用和复测均记录evidence/v076/failures.md，早期数量证据不替代最终结果。

## 安装与数据

版本0.7.6固定安装目录`%LOCALAPPDATA%\Programs\AFMC FIND`，新PID67084；旧PID53860正常退出，未强杀。备份`%LOCALAPPDATA%\AFMC\Find-install-backups\20261104-201510-531`。配置预检通过，来源/偏好哈希保持，候选与安装两个程序集哈希一致。证据install.json、install-integrity.json。无新增依赖、系统代理/证书变更；未提交推送。

## 限制与下一步

FOSS首次目录请求有网络波动（curl完整下载曾耗31秒），应用后续实测约2秒且同会话缓存复用；不保证持续延迟或所有种子有活跃做种。软件目录统一归入软件类别，开源游戏专门分类仍可细化。LibriVox关键词覆盖、Jamendo客户端凭据、Commons/Openverse可达性、普通图片/EPUB/音频文件的交接方式仍待落实；本批没有声称这些站点已接通。
