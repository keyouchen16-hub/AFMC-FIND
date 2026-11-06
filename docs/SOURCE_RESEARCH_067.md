# 0.6.7 公开BT来源研究与接入（2026-11-04）

本轮检索维护中的开源适配项目、官方接口和实际网络响应。不存在统一可核验的全球库规模排行，不宣称已覆盖全网或“全球最大”。以下记录本次实测，不保证远端长期可用。没有下载资源正文、安装索引后台、绕过验证码或登录限制。

## 已接入

| 来源 | 官方来源/依据 | 本次接入行为 | 已验证 |
|---|---|---|---|
| Torrents.csv | [官方项目](https://codeberg.org/heretic/torrents-csv-server)，公开 `/service/search?q=&after=` | 游标最多3页；哈希、大小、做种、发布时间；5分钟缓存 | ubuntu 40条；空结果；游标；后页失败保留 |
| LimeTorrents | [站点](https://www.limetorrents.fun/)，[Jackett维护定义](https://github.com/Jackett/Jackett/blob/master/src/Jackett.Common/Definitions/limetorrents.yml) | 按实际HTML独立实现；仅table2真实条目；由指定torrent缓存链接提取哈希，不抓外站文件；40条/页最多3页 | ubuntu 120条；广告排除；模糊日期未知；分页 |
| Tokyo Toshokan | [站点](https://www.tokyotosho.info/)，[RSS](https://www.tokyotosho.info/rss.php)，[维护定义](https://github.com/Jackett/Jackett/blob/master/src/Jackett.Common/Definitions/tokyotosho.yml) | 关键词RSS；Base32哈希标准化；标题、分类、大小、日期；不把torrent链接自动下载 | naruto 97条；ubuntu13条；上游空结果特殊响应识别 |
| Academic Torrents | [官网](https://academictorrents.com/)，[官方数据库RSS](https://academictorrents.com/database.xml) | 官方公开全库元数据约3MB，2919条；按标题/简介全部词匹配，会话缓存24h；最多100结果 | mnist5条；ubuntu3条；跨查询只请求一次数据库 |

数据量都是本次结果或元数据统计，做种为站点报告值，未连接种群测量。哈希合法不等于资源内容真实、安全或当前可下载。模糊时间及缺失做种不估算。

原有 Archive、磁力熊、Knaben、BitSearch、Nyaa、种子楼、TPB 保留，共11个聚合来源。新来源自动补入，旧有停用状态保留。所有输出继续使用哈希去重与原有综合推荐。没有复制第三方解析实现或新增依赖。

## 未接入/不重复计算

| 候选 | 本次证据与结论 |
|---|---|
| SolidTorrents | [当前入口](https://solidtorrents.to/)跳转BitSearch，已有同库，不重复增加来源数量。BitSearch首页报告的约4960万索引是站方自报，不作为独立审计规模。 |
| 1337x | 本机公开搜索HTTP403，保留候选，不宣称支持；没有绕过访问验证。 |
| BTDigg | 本机btdig.com普通HTTPS查询握手超时；不凭此断言永久关停。 |
| EZTV | [官方API文档](https://eztvx.to/api/)和本机API200；支持IMDb ID查询，无任意关键词全库查询参数。不能将最新条目本地过滤冒充关键词全库搜索，待有可靠剧集ID解析再接入。 |
| TorrentProject | [API文档](https://torrentproject.se/api)可读，但实际JSON请求被重定向成HTML首页，与文档不符，未接入。 |
| Jackett/Prowlarr | [Jackett项目](https://github.com/Jackett/Jackett)为索引代理，应用现有Torznab入口可连接用户实例；不是无需配置的新资源数据库，本轮不额外安装后台。 |

## 验收

- 构建0警告/0错误；56核心、26扩展来源、16UI检查通过。
- 首轮独立4源实测全部成功，见evidence/v067/expanded-live.json（Lime翻页阈值修正前为40条）。随后新增3页120条测试通过。
- 最终安装0.6.7、固定目录、旧0.6.6正常退出，无强制结束。
- 安装版第一次ubuntu：438条，10成功/1失败，BitSearch网络错误；单请求复查200，安装版第二次ubuntu：730条、11成功/0失败。第二轮复用了部分5分钟缓存；不宣称11源长期无故障。
- 证据evidence/v067/published-first-attempt.json、published-verification.json、published-window.png。窗口截图使用PrintWindow；不含DWM外围阴影/裁切。
- API测试只查元数据；没有调用下载软件。来源返回的标题、文件大小、做种未与资源正文核实。
