# AFMC FIND 0.7.5 — 海盗湾直连与表头排序

2026-11-04，已覆盖安装。承接海盗湾官网接入请求，同时落实用户追加的三列表头可点击排序。

## 最终行为

- 新搜索源“海盗湾”：`https://apibay.org/q.php?q=...&cat=0`，单批最多100条、五分钟有界缓存。解析标题/类别/大小/做种/时间、有效哈希组成磁力链接；详情链接指向thepiratebay.org。严格识别真实空结果，不吞掉响应异常。
- 原TPB Proxy保留，重复哈希合并并保留来源记录。16个聚合入口不等于16个独立资源库。
- 文件大小、发布时间、做种表头首次点击降序，再点升序，箭头及右侧排序文字同步；未知值始终在末尾，0不当作未知；保留当前选中资源。工具栏轮换中加入大小，保留原排序选项。筛选与整体布局不变。

## 验收证据

- release-build.log：`Build.ps1 -Publish`退出0、构建0警告0错误；280检查 = 原252 + 17海盗湾 + 11表头排序。排序包含正反方向、零/未知、选中保持、工具栏/列箭头同步；原生受控数据与路由事件，不等于全DPI物理鼠标覆盖。
- piratebay-live.json：ubuntu100、Sherlock100、无匹配查询0，均无错误，返回磁力哈希有效。没有调用下载客户端或下载正文。
- published-verification.json：安装0.7.5真实查询记录，海盗湾贡献100条，总669条、15成功/1失败；LinuxTracker上游目录不可用明确失败。原始结果量不是全库覆盖量。
- installed-header-sort.json：安装版三个表头各两次UIA InvokePattern，方向及工具栏标签同步；最小窗口截图header-sort-minimum.png正常。截图阶段用户输入已变化，installed-header-sort.png仅用于布局观察，不能作为原测试关键词结果证明；后续脚本增加输入变化停止保护，未继续操控用户现场。
- initial-failures.md保留两次测试夹具问题及根因：合并后顺序改变、继承Observations。修复夹具定位和原始来源构造，未削弱断言。

## 部署与限制

固定目录`%LOCALAPPDATA%\Programs\AFMC FIND`版本0.7.5，新PID53860；旧PID54324正常退出，未强制结束。备份`%LOCALAPPDATA%\AFMC\Find-install-backups\20261104-195644-368`。配置预检、来源/偏好哈希、候选/安装两个程序集哈希通过，见install.json和install-integrity.json。未提交推送。

官网首页与search.php在本机均20秒超时，接口HTTPS200。官网详情可访问性未解决，不将接口成功描述为官网可访问。接口字段是来源报告，不保证内容真实性和做种实时在线。没有新增依赖或修改系统网络/安全设置。

协议参考：[海盗湾官网](https://thepiratebay.org/index.html)、[APIBay](https://apibay.org/)、[SuprBay相关讨论](https://pirates-forum.org/Thread-Why-does-main-js-not-load)。字段/空结果以本次捕获JSON和应用联网实测为依据。
