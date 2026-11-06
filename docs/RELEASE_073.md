# AFMC FIND 0.7.3 — 搜索源与设置卡片修复

2026-11-04，用户先要求筛选内分类，随后明确“筛选不用改了”，因此没有修改筛选窗口。本轮完成新增来源与截图所示设置卡片下边缘裁切修复。

## 新增来源

- [LinuxTracker](https://linuxtracker.org/)：Linux与开源软件关键词检索，使用官网表单的 `/torrents/?q=...&page=...`，每页25条，最多3页；严格接受同站40位哈希详情路径，复用磁力交接和跨源哈希去重。做种只取本站strong数值，外部统计不叠加；相对/无时区日期保持未知，5分钟会话缓存。
- [Debian官方种子说明](https://www.debian.org/CD/torrent-cd/)指向的 `cdimage.debian.org/debian-cd/current/`：amd64/arm64的bt-cd/bt-dvd四目录，仅索引稳定版系统镜像种子。1小时会话缓存后本地关键词匹配。链接必须是固定目录内白名单文件名；目录显示的是.torrent文件大小，不能当作ISO大小，所以未知大小/做种/日期留空。这是专用来源，不是全网搜索引擎。

来源目录从11个聚合源扩为13个。已有启停与用户配置保留，新来源按现有目录迁移逻辑加载。

LinuxTracker对无结果词实测返回“Torrent data is unavailable / legacy catalog”页面，明确记为来源失败，不伪装成正常零结果；失效、限流和冷却仍可能发生。本次能访问不构成持续可用保证。音乐候选bt.etree.org本机和web均403，未加入。没有绕过登录/验证或改证书校验。

## 设置修复

窗口455→510 DIP，高度不超过工作区减40；设置内容放入自动滚动区并留底部6 DIP，底部操作区独立。命名HistoryCard便于测量整张卡片，测试不再只检查开关是否在保存按钮上方。移除已隐藏历史入口对应的过期提示文字，历史数据未删除，偏好保持。筛选弹窗/主工具栏没有改动。

## 证据

- `evidence/v073/release-build.log`：零警告、零错误，232项检查通过：59核心+26既有源+16新源+16原生UI+32质量+44完整性+29工作台+10托盘。设置卡片底边正常高度可见，360 DIP受限窗口可滚动到完整底边。
- `reliable-live.json`：真实LinuxTracker ubuntu75条、Debian debian4条；分别获取一份真实.torrent，TorrentFile结构验证通过，Linux路径哈希与种子infohash一致。总20项新源检查（含16确定性+4联网）通过；未下载资源正文或调用下载客户端。
- 安装版 `published-verification.json`：ubuntu804条，13源成功/0失败，其中LinuxTracker75、Debian无匹配是正常结果。此数仅是当次查询，不能当成来源总容量。此查询未要求图片断言。
- `settings-installed.png`、`settings-card-fixed.png`：已目视核对设置卡片完整底边、底部按钮无裁切；品牌、反馈、设置UIA检查通过。完整DPI/多屏矩阵未测。
- 已覆盖安装0.7.3，旧0.7.2 PID66868正常退出，无强制结束，新PID61216（验收快照）。来源/偏好配置哈希保留，候选和安装DLL哈希相同；备份 `%LOCALAPPDATA%/AFMC/Find-install-backups/20261104-190450-822`，见install/profile-preservation/installed-hashes证据。

工作树codex/initial-development，无提交推送。只新增软件/系统镜像覆盖，未宣称本轮扩大了影视资源库。
