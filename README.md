# AFMC FIND

Windows 原生资源搜索工作台：聚合多个搜索源，核对资源版本，并交给下载软件或内置播放器打开。

**Find the You.**

## 当前状态

当前产品版本为 **0.8.20**。本仓库还包含未发布的节点复用、迟到分片调度实验及验收工具；源码快照不等于已经验证的新安装包。

磁力边下边播仍在优化：搜索源响应成功、显示做种数、连接到节点，都不代表已经可以播放。只有实际解码和播放时间推进才计入“已验证起播”。具体完成项与限制见 [项目状态](docs/PROJECT_STATE.md)、[0.8.20 验收记录](docs/RELEASE_0820.md) 和 [分片实验](docs/PIECE_REASSIGNMENT_0821.md)。

## 功能

- 多来源聚合搜索、哈希去重、名称相关度、分类/分辨率筛选与列排序。
- 有封面时显示缩略图，选中后查看文件名、大小、做种数、时间及来源。
- 管理搜索源、识别网址、连接 Torznab，以及已适配站点目录。
- 调用已有下载软件；内置简洁播放器支持视频直链、磁力和种子视频。
- 深色原生 WPF 界面、局部磨砂、托盘与基础设置。

第三方站点可用性会变化。未知或未经验证的资源会保留相应状态，不承诺所有搜索结果都能下载、秒开或持续流畅。

## 开发与构建

需要 Windows x64、`global.json` 固定的 .NET SDK，以及用于准备组件的 Python 3。依赖版本固定在项目与准备脚本中。SDK 和播放器/下载运行库不提交到 Git。

```powershell
python scripts/Prepare-StreamingRuntime.py
./scripts/Verify-StreamingRuntime.ps1
./scripts/Build.ps1
```

准备脚本从官方来源下载固定的 Python/libtorrent 组件并校验摘要。播放器依赖、许可证和分发边界见 [第三方说明](docs/THIRD_PARTY.md)。本机开发工具位于 `.tools`，新克隆需要重新准备。

`Build.ps1` 构建并运行项目已有检查；部分检查会打开隔离配置的原生窗口。`-Live` 会访问真实网络。**`Build.ps1 -Publish` 会继续打包并执行本机安装**，仅在准备发布且验收通过时使用。

分片研究脚本 `Patch-NativePieceRetry.py`、`Build-NativeStreamCandidate.ps1` 和 `Prepare-NativeRetryBench.ps1` 仅供隔离实验，额外需要已安装的 VS2022 C++ BuildTools 与固定的源码依赖；它们不会自动替换正式下载组件。受控夹具成功不等同于公网磁力播放成功。

## 配置与隐私

个人配置保存在 `%LOCALAPPDATA%\AFMC\Find`；搜索源 Key 使用当前 Windows 用户的 DPAPI 保护。请勿上传该目录或将加密配置当作可跨机器共享的凭据。

仓库忽略 `.tools`、`artifacts`、构建输出、原始验收截图/网络记录、密钥文件和下载缓存。`docs` 中保留人工整理的结论与限制；文档引用的本机原始证据不会随源码上传。

## 目录

- `src/Afmc.Find.Core`：搜索源、解析、去重、匹配和链接校验。
- `src/Afmc.Find.App`：WPF 界面、配置、播放器与流式下载接口。
- `tests/Afmc.Find.Checks`：确定性检查、原生交互及真实网络对照。
- `scripts`：组件准备、构建、安装和诊断脚本。
- `preview`：历史界面预览源码；最终产品资产在 `src`。
- `docs`：项目连续性、决策、验收报告与未解决问题。

## 反馈

邮箱：[keyouchen16@gmail.com](mailto:keyouchen16@gmail.com)

第三方组件各自适用原许可证；本仓库未另行授予项目整体的开源许可。
