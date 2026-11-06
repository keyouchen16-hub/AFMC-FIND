# AFMC FIND 0.7.8 — 内置搜索源配置

## 交付

用户纠正0.7.7仅连接外部Jackett的方向。本版将官方Jackett 0.24.3009作为独立组件随包分发，由AFMC FIND按需启动、认证和退出，不要求用户安装Jackett或填写内部地址/Key。保留原入口名称。原生窗口按名称/网址筛选适配目录，展示站点配置，实际测试成功才加入聚合搜索。编辑已管理来源继续使用原生表单，旧外部Torznab仍可编辑。站点本身确需账号/Key时仍需用户提供。

## 验证

- 完整320项回归通过，构建0警告0错误。
- 11项真实引擎检查：650目录、配置读取/保存、Internet Archive真实测试、ubuntu100条可下载结果、管理接口拒绝匿名请求、相同配置目录互斥、退出清理、重启配置保留及重新搜索。
- 7项原生窗口检查：真实目录/表单、无内部地址Key输入框、粘贴网站过滤、操作按钮在窗口内、真实配置测试添加。最终截图已查看并修正输入裁切和下拉框样式。
- 安装版UIA：自动加载650目录、网站过滤及原生表单就绪；子进程来自安装目录、仅监听127.0.0.1；正常退出AFMC后子进程消失，已重启应用。
- 原有sources.json/preferences.json与安装前备份哈希一致；应用/核心程序集、引擎exe及随附源码包与候选一致。

## 安装

0.7.8覆盖安装；旧进程65792正常退出，无强制关闭。备份`%LOCALAPPDATA%/AFMC/Find-install-backups/20261104-210911-607`。安装初始进程56152；最终交互检查退出后重启为60296。安装包144844501字节。

## 边界

650为适配目录条目，不是已启用或全部当前可用的来源。测试使用Internet Archive，不代表所有站点/私有账号流程验证。验证码等未支持的交互字段明确阻止添加；未适配的网址不会被伪装成可搜索来源。运行时仅继承可识别的系统默认HTTP/SOCKS5代理，不修改系统设置。老版外部`%APPDATA%/Jackett`目录存在时拒绝启动，防止上游自动迁移个人数据。站点字段中未翻译的名称按上游显示。

引擎数据放在受当前Windows用户/SYSTEM ACL保护的SearchEngine目录；engine.secret使用DPAPI。上游ServerConfig等文件需要保存内部Key/自身配置，不宣称全部上游数据均DPAPI加密。退出优先请求引擎正常关闭，私有Windows Job Object作为清理兜底。未测试全部DPI/多显示器、所有站点认证和验证码。

证据：`docs/evidence/v078/engine-validation.log`、`engine-live.json`、`embedded-source-dialog.png`、`release-build.log`、`installed-engine.json`、`install.json`、`install-integrity.json`、`failures.md`。许可证/固定版本/对应源码分发说明见`ENGINE_DEPENDENCY.md`。工作树codex/initial-development保留未提交状态，无提交/推送。
