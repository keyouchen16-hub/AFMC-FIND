# 2026-11-05 后续连接诊断与建议（未发布）

用户问“那该如何解决”。安装版保持 0.8.10，进程 61328；本轮仅诊断与实现未发布原型，没有把失败的真实起播检查作为通过，也未覆盖安装。

## 新证据

之前 libtorrent 对照只用了 infohash/DHT，不能代表完整连接能力。此次对相同三个公开索引哈希增加六个公开 tracker，并带官方 Sintel 正对照。40 秒时：

| 条目 | libtorrent + 公开 tracker | 限 TCP 的第二次 libtorrent 对照 |
|---|---:|---:|
| Brand New Day 2026（26f1ce…） | 7,340,032 字节 | 11,468,800 字节 |
| No Way Home 2021（8148c1…） | 2,408,448 字节 | 4,161,536 字节 |
| Spider-Man 2002（8d9a66…） | 0 字节 | 0 字节 |

以上是下载/校验数据证据，不是视频首帧、内容真实性、全片流畅或完整下载证明。各轮启动时间、DHT状态和连接到的节点不同，不能当严格性能基准或固定速度保证。

AFMC 自身 MonoTorrent：仅补 tracker 的默认 25 秒试播超时；按资源节点连接有限延长后能拿到元数据但未解码。精确 NuGet 源码提交 e78faebd0aec117146cffccaaea987ab0629eec0 的 TorrentManager 把全部磁力 tracker 放入一个 tier，已对固定六个公开补充入口实验拆为独立 tier，元数据约六秒可到达，但仍未成功解码。库原生 StreamingPieceRequester 对照也未起播。去掉播放和流式取片后的普通下载对照，No Way Home 在 60.2 秒收到 851,968 字节，进度仍为零（尚未完成整片校验）；该轮 MonoTorrent DHT 节点为零，故不能归因于取片器或声称已完整定位内部根因。

证据：evidence/v0810/followup 的 public-tracker-comparison、tcp-only-comparison、afmc-tracker-playback*、monotorrent-download 日志/JSON。测试缓存按 GUID 限定目录清理，没有修改防火墙、路由器或系统网络配置。

## 当前未发布原型

- PublicTorrentDiscovery 仅为明确的内置公开源标记发现权限，标记随来源观察记录往返。Torznab、自定义/未知/手动输入默认不补充。
- 对无 tracker 的公开磁力补充固定六个入口；保留已有参数及 tracker 原样，重复调用幂等，有 URL 长度上限。
- DownloadLauncher 在来源校验后应用上述策略；TorrentPlayback 仅对这六个固定入口独立分组。
- 试播连接到真实资源节点、收到实际字节后，各允许一次有限延长，仍受整组 60 秒约束；收到字节不升级为已校验或可播。
- 增加 10 条公开来源/认证链接/合并往返/真实搜索到播放交接的测试，已通过。未完成新版全量回归或真实起播验收，不得发布。
- useLibraryStreaming 为诊断对照开关，生产默认仍为原取片器；没有宣称切换取片器解决问题。

## 下一步选择

优先试验以 libtorrent 承担下载和按需取片，保留 WPF/VLC 界面。使用有生命周期管理的独立进程，以本机受控通道交给播放器；资源退出时清理其缓存和连接，下载/解码崩溃不能带走主程序。保留原引擎回退直到新实现真实验收通过。Python 虚拟环境只用于本轮诊断，生产部署方式、体积与许可证尚需评估，尚未集成。

新实现必须实测以上已有数据的两条链接：首帧与时钟前进、至少两分钟连续播放、播放中拖动、取消换源、关闭保留主程序。只把当前实测起播成功的结果作为可播推荐；未知/失败仍保留下载入口且明确状态。源站做种数不能当作实测可播。

参考：[公开 tracker 列表](https://github.com/ngosang/trackerslist)、[libtorrent 设置文档](https://www.libtorrent.org/reference-Settings.html)、[MonoTorrent 源码](https://github.com/alanmcgovern/monotorrent/tree/e78faebd0aec117146cffccaaea987ab0629eec0)。
