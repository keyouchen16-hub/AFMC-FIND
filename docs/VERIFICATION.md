# 0.1.0 验证记录

日期：2026-11-04。范围：原生 WPF 种子搜索 MVP 与 Windows x64 便携发布。

| 项目 | 结果 | 证据 / 边界 |
|---|---|---|
| 编译、C# 类型检查 | PASS，0 警告 / 0 错误 | final-checks.log |
| 核心行为检查 | PASS，29 项 | final-checks.log；哈希规范化、去重、XXE、错误分类、取消、局部失败、DPAPI、种子结构等 |
| 真实公开源 | PASS，6 项网络检查 | checks-live-2.log / live-search.json；ubuntu、Big Buck Bunny、中文各返回 50 条，真实种子获取并解析 |
| WPF 固定响应 | PASS，9 项 | final-checks.log；含旧请求取消、新查询不被覆盖、筛选、导航、主题对比、最小尺寸 |
| WPF 真实数据 | PASS，9 项 | checks-ui-live-final.log / native-live.png |
| 实际发布目标 | VERIFIED | published-verification.json；版本 0.1.0，UIA 输入 ubuntu，50 条结果，1 源成功 / 0 失败，Responding=true |
| 安全审查 | 已执行，有针对性测试通过 | 源码无第三方插件执行、无命令解释器拼接；XML 禁 DTD、限制响应与种子大小/深度、密钥 DPAPI、API 重定向禁止、远程源 HTTPS、来源文案不直接呈现为 HTML |
| 代码范围 | 已检查 | 新独立 Git 分支 codex/initial-development；无提交和推送，未改其他 AFMC 源码或配置 |
| 独立 lint / 覆盖率 | NOT MEASURED | 仅编译器分析；不能将测试数量当覆盖率 |
| 用户 Jackett / Prowlarr | NOT VERIFIED | 接口代码与受控响应通过，缺少实际用户实例联调 |
| 默认下载软件实际接收 | NOT VERIFIED | 已实现 ShellExecute 路径及失败提示；未启动外部下载、未获取资源内容 |
| 多屏 / 完整 DPI / 物理输入 | NOT VERIFIED | 本机实际窗口截图，UIA 与路由事件检查，不替代全矩阵 |

## 已修复失败

1. checks-live.log：测试项目缺少 System.Net.Http 导入，退出码 1；补充后 checks-live-2.log 退出码 0。
2. checks-ui.log：测试 Application.StartupUri=null 引发 ArgumentNullException，退出码 1；共享 Theme.xaml + 独立 Application 宿主后通过。
3. 原生截图人工检查发现窗口基类样式未应用及按钮文字继承错误；显式应用 Window 风格并修正文字继承，新增颜色检查。
4. published-verification.log：WaitForInputIdle 后主窗口句柄仍为 0，退出码 1；增加等待实际窗口，复用同一进程，published-verification-2.log 退出码 0。

## 交付与已知限制

发布目录 `artifacts/AFMC-FIND-0.1.0-win-x64`，保留完整目录运行。ZIP 的 SHA-256 见 package-hash.json。运行中的发布程序为本轮产物，便携启动不等于系统安装。

每源返回数有限，无分页；公开源未提供大小/做种时显示未知。只按可靠相同哈希合并，无哈希保留独立项。纯 v2 种子文件暂不支持；v2 磁力链接支持。配置损坏时保留原文件并禁止覆盖。

下一项外部验收：用户配置可访问的 Torznab 服务后测试连接与搜索，再选定资源观察默认软件下载弹窗/接收结果。当前不把这些路径标成已完成。

## 0.2.0 增量验收
38项核心/网站检查与9项原生检查通过，见evidence/source-audit/final-checks.log与native-checks.log。新增TPB30条、种子楼15条真实ubuntu结果及详情哈希验证；保持所有失败/导航入口状态透明。完整范围见SOURCE_INTEGRATION.md。

## 0.3.0
38项核心/网络检查通过（evidence/v03/checks-final.log）；测试真实分页增量、后续页失败保留、版本约束、真实JPEG读取/解码、图片协议及域名拒绝。原生测试新增有图来源筛选。实际发布版图片渲染见v03发布证据。Big Buck Bunny查询曾出现种子楼后续页跳转，前页保留，其余源完成；不视作所有来源无故障。发布脚本首次因Query/query冲突失败，修复后通过。
# 0.4.0 最终验收

45项核心确定性检查、11项原生界面检查通过（evidence/v04/validated-build.log）；国际三源真实检索通过；磁力熊4磁力版本、图片解码、空结果通过（cili-live-final.log）。磁力熊5秒请求限制已适配，失败记录保留。真实发布窗口、PID、版本及哈希见 evidence/v04/published-verification.json；未调用外部下载软件。



## 0.5.0 最终验证
构建零警告零错误；45 核心、14 UI 检查通过。发布搜索“特洛伊”：211 条、6 源成功/0 失败；图片控件 2 个。DWM type=3/hr=0；真实列表滚动位置大于零；注入 Ctrl+K 聚焦成功，测试筛选清空。证据位于 evidence/v05。未触发外部下载；非物理键盘测试；旧系统回退、完整 DPI/多显示器未验证。


## 2026-11-04 标题栏统一与 LOGO 候选
用户指出顶部实色条与磨砂不统一。改为 WindowChrome 全客户区磨砂（GlassFrameThickness=-1）、同根背景标题区及简洁窗口按钮，保留系统拖动/缩放机制。构建、14 UI 检查通过；窗口按钮 UIA 最大化/还原/最小化通过，物理拖动/双击尚未验证。最终发布搜索 211 条/6 源成功，2 个图片控件，DWM type3、滚动、Ctrl+K 通过。首次未筛选列表首屏无图片导致图片断言失败，定位已知图片条目后通过；测试筛选已清空。旧包保留 artifacts/AFMC-FIND-0.5.0-before-title.zip。
用户要求9张LOGO选择图，已通过 imagegen 生成并保存 preview/logo-concepts/option-01.png 至 option-09.png。候选尚未选定，不替换应用图标。


0.5.1：45核心/15UI通过，sort-ui.log验证31/9/0/null。published-verification.json真实211条/6成功；glass-verification.json验证type3、滚动、Ctrl+K及清空筛选。executable-icon.png从EXE提取用于验证图标嵌入。


## 0.5.2 / Codex / done
系统标题独立填色抑制，导航移到内容区标题行；最终截图确认横条消失。45核心、15UI及真实搜索/材质验证通过，见evidence/v052。旧系统未逐一验证。


## 0.5.3 / Codex / done
搜索框40、筛选32 DIP胶囊形，文字居中；配套按钮同步紧凑。45核心/15UI及发布真实搜索、图片、材质、滚动/快捷键通过。截图见evidence/v053/published-window.png。


0.6.0最终56核心、16UI、9托盘检查通过。图片实测4->20（6源211条）；Archive启用后发布215条/7成功，图片控件7。第一次极值/托盘时序/焦点失败及后续修复复测日志均保留evidence/v060。关闭/恢复通过真实NotifyIcon与WPF调用验证，未通过物理鼠标点击托盘菜单，不宣称完整DPI或开机重启验证。


0.6.1 / Codex / done：页面入口改轻页签，设置独立齿轮；56核心/16UI通过，截图/滚动/快捷键通过。联网201条，种子楼后续页超时且保留部分结果，见evidence/v061。


0.6.2/0.6.3：设置改磨砂卡片、开关、直接排序选择；10项设置托盘检查通过。主窗口控制符号10 DIP、设置关闭11 DIP，点击区域不缩小。0.6.3发布启动，保存旧版本。


0.6.4：取消设置排序偏好与对应持久化逻辑，保留主界面排序。系统统一圆角，设置根层不再另画圆角；缩短设置窗口。10项设置托盘检查与发布DWM/截图验收通过，见evidence/v064。


## 2026-11-04 · 0.6.7

0.6.7：56核心+26扩展+16UI通过。四源单独实测通过；安装版ubuntu复测730条/11成功/0失败，首轮BitSearch失败记录保留。evidence/v067包含两轮JSON与窗口截图；没调用下载程序。

## 2026-11-04 · 0.7.0

0.7.0：160项检查通过、构建0警告0错误；16个合成查询首位命中8/16到16/16。安装版特洛伊215条/11来源成功/3缩略图；品牌设置反馈UIA及截图核对通过。程序集哈希与配置保留核对通过。回退仅隔离文件事务模拟，未真实诱发应用崩溃。证据evidence/v070，边界RELEASE_070.md。
# 0.7.1 最终验收

详见RELEASE_071.md及evidence/v071：202检查通过；50真实查询/500候选元数据判断（探索集非盲测）；真实qBittorrent两路接收停止、正文0字节；安装版215条11源成功0失败4图片，设置反馈版本验证通过，配置哈希保持。初次带年份查询首屏图片断言失败，不保证每次有图。默认Quark端到端与完整DPI/多屏未测。

