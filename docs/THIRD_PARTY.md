# 第三方来源与依赖

## 0.8.0 原生播放器

新增MonoTorrent 3.0.2、Mono.Nat 3.0.0、ReusableTasks 4.0.0及LibVLCSharp/LibVLCSharp.WPF 3.10.2，均固定在项目和锁文件中。许可证与对应LibVLCSharp源码归档随发布包的licenses目录分发，详见player-licenses/SOURCES.md（发布包为licenses/SOURCES.md）。托管程序集未合并或混淆；原生VideoLAN.LibVLC.Windows 3.0.24从官方NuGet地址按需下载，SHA256固定校验，仅保留x64运行时，升级包不捆绑原生解码器。没有安装全局VLC、改变文件关联、启用UPnP或安装服务。

MonoTorrent常规请求器外增加按读取位置的约4MiB预取范围，保留原有连接、分片校验和请求限制；未修改第三方DLL。网络测试采用自生成AVI/PCM和本机HTTP/BT测试端，没有复制第三方影片。动态图标和主程序其它许可证仍沿用原声明。

## 0.7.11 动态页面识别

Microsoft.Playwright 1.64.1（Apache-2.0），固定在 PackageReference 与锁文件中。使用电脑已安装的 Microsoft Edge，未下载或捆绑浏览器。发布包保留 `.playwright/package/LICENSE`、`NOTICE`、`ThirdPartyNotices.txt` 及 `.playwright/node/LICENSE`；Node 驱动仅包含 Windows x64。每次任务使用独立临时浏览器上下文，不读取日常浏览器配置、登录信息或私有 API Key。官网文档：https://playwright.dev/dotnet/docs/browsers 。网页 JS 仅在运行时从原站读取并执行，未复制站点适配代码进产品。

## 0.7.6 FOSS Torrents

使用 [FOSS Torrents公开种子RSS](https://fosstorrents.com/feed/) 提供的软件、系统和开源游戏目录；来源标题、发布日期、种子地址来自站点。仅读取种子元数据进行验证，没有下载软件正文或复制第三方代码。RSS GUID不是已验证infohash，未知大小/做种不推测。部分种子由该站非官方制作，参见其各项目页面说明；不声称所有包均由软件作者发布。没有新增第三方包。

## 0.7.5 海盗湾直连

新增 [APIBay](https://apibay.org/) 的公开q.php JSON检索，搜索源名“海盗湾”，详情链接到 [The Pirate Bay](https://thepiratebay.org/index.html)。原有TPB Proxy镜像独立保留，重复哈希通过既有合并流程去重。接口字段来自原站报告，不将做种数当作客户端实时验证。没有复制第三方实现代码或新增依赖。2026-11-04本机官网HTML连接超时，但APIBay真实搜索200；可搜索不代表官网页面始终可打开。

## 0.7.4 服务数据归属

- [TVmaze](https://www.tvmaze.com/api)：剧名和 IMDb 编号映射来自 TVmaze API；按其 [CC BY-SA](https://creativecommons.org/licenses/by-sa/4.0/) 说明归属 TVmaze。AFMC FIND 对映射进行精确名称匹配和标识筛选，没有改写节目资料或复制图片。来源列表显示 TVmaze 归属与文档地址。本通知随发布包分发。
- [EZTV API](https://eztvx.to/api/)：按 IMDb 编号读取种子元数据，额外校验实际标题和磁力哈希。站点数据有误时排除，不代表内容真实性经过验证。
- [动漫花园](https://share.dmhy.org/)：使用其公开关键词 RSS；标题、类别、发布日期、磁力信息来自原站；未复制站点代码，不将 RSS enclosure 占位长度当作资源大小。

2026-11-04。

## 实际发布依赖

- Microsoft .NET / Windows Desktop Runtime，由 SDK 自包含发布流程提供。版本以发布目录 `AFMC.Find.runtimeconfig.json` 及 `AFMC.Find.deps.json` 为准；保留发布输出中的许可证与第三方通知文件。
- 没有第三方 NuGet PackageReference。项目锁文件记录还原图。

## 服务与协议参考

- Internet Archive：公开搜索 `https://archive.org/advancedsearch.php` 和元数据 `https://archive.org/metadata/{identifier}`；实际查询与种子读取证据见 `evidence/development/live-search.json`。接口访问成功不代表所有条目可下载。
- [Jackett 官方文档](https://github.com/Jackett/Jackett)：Torznab 适配规范与能力探测参考；未捆绑 Jackett。先前仓库许可元数据 GPL-2.0，仅作为候选记录。
- [Prowlarr](https://github.com/Prowlarr/Prowlarr)：Torznab 兼容候选，未捆绑，尚未对真实实例做联调。
- [qBittorrent](https://github.com/qbittorrent/qBittorrent)：可作为系统默认下载软件，本程序不依赖其代码或 Web API。

后续若捆绑服务，必须对固定版本重新核对许可、通知和源码提供方式。本文件不将协议调用视为任何许可豁免结论。

## 0.2.0 新依赖
新增 HtmlAgilityPack 1.13.1（MIT），PackageReference和packages.lock.json固定版本；许可文件为HtmlAgilityPack-LICENSE.txt并随包分发。此前“没有第三方NuGet依赖”仅适用于0.1.0。站点访问并未复制服务端代码。
## 0.8.11 下载组件（2026-11-05）

- libtorrent 2.0.15 Python Windows x64 wheel（原生版本 2.0.15.0），来自 PyPI 官方版本文件，固定 SHA256。采用上游 BSD 许可；完整上游 LICENSE（含适用的依赖通知）保留于 `components/libtorrent/libtorrent-LICENSE.txt`。上游源码与许可：https://github.com/arvidn/libtorrent/tree/v2.0.15 。
- CPython 3.12.10 官方 Windows x64 嵌入式包，PSF 许可，原包 LICENSE.txt 随组件保留。下载地址和固定摘要见 `scripts/Prepare-StreamingRuntime.py`。此为兼容 wheel 的固定版本，不声称是最新安全版本；升级时必须重新做原生依赖加载和播放验证。
- 组件运行于应用自有独立进程，无系统 Python 安装、无系统服务；下载组件约 32 MiB，实际发布包增量尚未测量。原有 WPF、LibVLCSharp、libVLC、MonoTorrent 依赖和许可仍保留。
- imageio-ffmpeg 0.6.0 仅在开发虚拟环境生成原创 MKV 验收样本，不随应用分发。

## 未发布的原生分片实验（2026-11-07）

`scripts/native/libtorrent-2.0.15-afmc-piece-retry.patch` 修改上游 v2.0.15 的请求调度和 Python 绑定；上游完整许可保留于 [libtorrent-LICENSE.txt](libtorrent-LICENSE.txt)。实验构建使用 Boost 1.87.0、CPython 3.12.10 开发文件和 CPython 官方 OpenSSL 3.0.16.2 依赖，固定来源见 [实验记录](PIECE_REASSIGNMENT_0821.md)。依赖源码和生成二进制仅保存在忽略的 `.tools` 目录，未上传或替换正式组件。若后续发布此候选，还须更新组件摘要清单、依赖通知并通过安装验收。

