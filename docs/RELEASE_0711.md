# AFMC FIND 0.7.11

## 本轮范围

解决用户报告的次元狗动态下载无法发现，增强通用识别流程；不把目录数量、网页能打开或编译通过当作来源已可用。静态4kbt能力保留，设置与配置窗口沿用0.7.10玻璃效果。

## 已取得证据

- 次元狗真实“终末列车”搜索及两层下载按钮取得夸克分享；保存来源后通过SearchService再次搜索及负查询通过，配置不保存临时fragment。
- 只贴https://www.acgndog.com/自动选择“万能鉴定士Q的事件簿”，返回1条网盘验证结果。
- 动态模拟覆盖无href按钮、临时地址不持久化、重新搜索、取消后恢复；原静态46项通过。
- 最终回归、发布与安装已完成，详见下段。

## 最终验收

- `regression-final.log`：389条实际PASS输出，Build退出0。最后网盘完整信息复制/不同分享标识改动后重新构建0警告0错误，`final-targeted-live.log` Workbench56项通过（比完整回归中的55项新增1项），Browser live4+Deep live4+Discovery live3通过。
- 次元狗“终末列车”识别、独立SettingsStore持久化后SearchService聚合、负查询与无临时token配置通过。4kbt仅分类网址自动发现，英文/中文各5版本与负查询通过；FOSS发现26条、保存后新关键词及聚合通过。
- 发布ZIP184810500字节，Playwright驱动/cli/程序集及许可证、NOTICE完整性校验通过。包哈希见package-hash.json。
- 安装记录见install.json：旧67616正常关闭、LegacyForced为空，新66580；备份`%LOCALAPPDATA%/AFMC/Find-install-backups/20261104-233005-534`，配置预检与暂存哈希Passed。
- 安装版UIA仅粘贴次元狗网址，验证词空，结果1条网盘，可添加按钮启用；任务后Node驱动退出，未写个人来源、未调用外部下载客户端。见installed-browser-discovery.json。
- 设置/识别/目录窗口安装版DWM backdrop=3，前台确认及截图，识别窗口截图已查看，无布局截断。见installed-dialog-glass.json及installed-*.png。
- sources.json/preferences.json与备份哈希一致；AFMC.Find.dll和Afmc.Find.Core.dll与发布候选一致。见installed-integrity.json。最终进程66580窗口标题AFMC FIND v0.7.11，Responding=true。

## 调试记录与限制

初次编译分别遇到不存在的Playwright.TimeoutException、错误局部变量名、缺少SettingsStore命名空间、已弃用NoWaitAfter，均退出码1，修正后构建0警告0错误。模拟HTML未声明UTF-8导致文字损坏，已修正测试响应。真实浏览器截获响应重放过多头导致DOM加载超时，改为必要内容/安全头后恢复。按钮前空白导致漏识别，修正后两层点击通过。无结果页保留默认内容造成自动采样过严，改为验证整份列表按关键词显著收缩，固定列表继续拒绝。一次并发构建遭测试DLL占用，出现MSB3026重试警告；最终构建测试改为串行。

仅覆盖可访问的公开流程；验证码/登录/付费/任意站点专门接口不保证兼容。最多4条详情/轮用于控制浏览器成本，不代表全站穷举。只识别分享入口，不下载网盘文件，不保证分享永不过期。未实际调用外部下载客户端。
