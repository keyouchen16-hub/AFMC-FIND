# 国际搜索源核查与接入（2026-11-04）

没有可靠统一的全球索引规模排名。本次按跨类别覆盖、公开接口、本机可用性选择，未将镜像数量或站点自报索引量当成独立资源量。

|来源|实现|结果与限制|
|---|---|---|
|Knaben|官方 v2 GET API，100 条/页，最多 3 页|ubuntu 实测 80 条，保留 `Knaben（原始索引）`，不是直接连接每个上游站|
|BitSearch|官方免密钥 API，100 条/页，最多 3 页|ubuntu 实测 300 条；免费额度 200 请求/IP/天，5 分钟会话缓存，429 报告限流|
|Nyaa|官方 RSS，最多 75 条，单批不伪造分页|blender 实测 3 条；偏动漫、音乐及东亚资源，特定查询可能没有结果|
|磁力熊|中文片名 POST 搜索、同域详情读取|特洛伊实测 4 个磁力版本、海报加载成功；最多前 8 部影片、2 并发详情、5.5 秒查询间隔|
|海盗湾、种子楼、Internet Archive|沿用已有适配器|共 7 个内置聚合源；用户当前停用 Archive 的选择保留，实际启用 6 个。哈希去重；有图显示、无图收起|
|1337x|直接搜索 HTTP 403|本轮未接通；Knaben 可能提供缓存来源，明确标注|
|LimeTorrents|Jackett 主域 .fun 搜索 HTTP 403，旧 .lol TLS 失败|本轮未接通|
|SolidTorrents|.to TLS 连接失败|本轮未接通|
|EZTV|RSS 可访问|最新更新流不等同关键词检索，本轮未加入|

## 一手资料

- https://knaben.org/api/v2/ ：GET、偏移分页与上游 tracker 字段。保留站点默认过滤，不请求 unsafe/xxx/dead 标记。
- https://bitsearch.eu/api ：API 免费额度、分页与响应格式。站点迁移由旧 .to 实测重定向到 .eu。
- https://github.com/nyaadevs/nyaa/blob/master/nyaa/search.py ：RSS 分支不使用分页偏移。
- https://github.com/nyaadevs/nyaa/blob/master/nyaa/templates/rss.xml ：命名空间、哈希及元数据结构。
- https://github.com/Jackett/Jackett ：可复用 Torznab 扩展，不新增本机常驻服务或依赖。
- https://raw.githubusercontent.com/Jackett/Jackett/master/src/Jackett.Common/Definitions/limetorrents.yml ：当前主域。
- https://1337x-status.org/ ：官方访问限制公告。

## 验证

最终构建零警告零错误，45 项确定性核心检查、11 项原生界面检查通过；国际三源的真实请求通过，磁力熊的磁力结果/海报解码/空结果三项真实请求亦通过。见 `evidence/v04/validated-build.log`、`cili-live-final.log` 与 `evidence/global-sources/live.json`。磁力熊初始失败来自未识别空结果文案和 5 秒间隔限制，修复后的证据与原始失败均保留。不执行资源内容下载，不把做种数、缩略图或上游的 verified 字段当成内容真实性保证。

## 截图书签补充

用户补充了精确域名，17 个地址已探测，重点站点用 www 及 Windows 网络工具复核；详见 [截图书签核查](BOOKMARK_SOURCES.md)。磁力熊已接入，其余没有验证通过的匿名磁力接口。网盘/在线播放/影视资料页面不计作种子聚合成功。
