# 跨类别来源研究 — 2026-11-04

用户要求图书、音乐、摄影、视频/影视、软件均可扩充。优先以公开接口、真实结果和可验证下载元数据作为纳入依据。以下明确区分完成接入、接口候选、网络失败；并非每个站都提供BT。

| 站点 | 领域 | 本次证据 | 本批决定 |
|---|---|---|---|
| [FOSS Torrents](https://fosstorrents.com/feed/) | 软件、系统、开源游戏 | 官方完整种子RSS；应用Blender/Krita检索与真实torrent元数据校验 | 已实现聚合，目录共享5分钟缓存；单次最多100匹配项 |
| [LibriVox](https://librivox.org/api/info) | 有声书 | 官方API；列表和完整书名Count of Monte Cristo返回200；Sherlock及官方前缀形式返回404无匹配 | 未接入；关键词覆盖与正常404语义仍需专门处理，不能用一次完整书名成功冒充广泛检索完成 |
| [Project Gutenberg](https://www.gutenberg.org/policy/robot_access.html) | 电子书 | 官方提供机器可读目录和自动取文件渠道，常规网页不用于自动抓取 | 候选；应使用目录方案并支持EPUB等普通文件，不能伪造种子 |
| [Standard Ebooks](https://standardebooks.org/feeds) | 精校电子书 | 官方新书RSS开放；其他OPDS/完整feeds需要Patrons Circle或符合其访问资格 | 未接入；不能将开放新书feed当作完整免费搜索接口 |
| [Jamendo](https://developer.jamendo.com/v3.0/tracks) | 音乐 | 官方API要求client_id，并用audiodownload_allowed指明是否可下载 | 未接入；当前没有本项目client_id，且需普通音频下载路径与授权字段展示 |
| [Openverse](https://docs.openverse.org/api/) | 摄影、图像/音频 | 文档可读；本机图片search请求15秒连接超时，web工具API入口403 | 未接入；网络可用性未通过，需保留来源/作者/许可字段；不是BT库 |
| [Wikimedia Commons](https://commons.wikimedia.org/wiki/Commons:API) | 摄影、图像、影音 | 官方MediaWiki搜索和imageinfo资料可读；本机3条图片元数据请求15秒超时 | 候选；普通媒体文件，不是种子源，未实测成功 |
| [Pexels](https://www.pexels.com/api/documentation/) | 摄影、视频素材 | 要求API Key；[最新帮助页](https://help.pexels.com/hc/en-us/articles/900004904026-How-do-I-get-an-API-key)提示新Key发放暂停 | 未接入；没有凭据，不注册账号、不请求或使用他人密钥 |
| [Public Domain Torrents](https://publicdomaintorrents.info/nshowcat.html?category=ALL) | 老电影 | 网站目录可被搜索工具读取，本机HTTPS目录请求403 | 未接入；没有获得可用于应用检索的稳定响应 |

现有Internet Archive已检索带Archive BitTorrent的条目，涵盖多种mediatype；本轮不把同库分类入口重复算作独立资源库。原有Nyaa/动漫花园/东京图书馆、EZTV和综合源也继续覆盖其已有类别。

## 后续接入顺序

1. 图书：核实LibriVox关键词/别名语义，或使用Gutenberg机器目录。对普通EPUB/音频，先完善普通资源结果与交接方式，再接入。
2. 音乐：Jamendo取得本项目client_id后按可下载字段接入；没有密钥前不能宣称可用。
3. 摄影/视频素材：Commons/Openverse联网检查通过后，再做作者、许可、缩略图和原文件入口。Pexels取决于有效Key和服务开放状态。
4. 影视：Public Domain Torrents需要稳定的本机查询/种子验证；403不绕过、不冒充支持。

本批没有把候选塞进启用列表凑数量。网络探测只是该时点本机结果，不断言站点全球不可用。所有应用改动与发布证据见RELEASE_076.md及evidence/v076。
