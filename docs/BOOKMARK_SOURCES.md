# 用户截图书签核查（2026-11-04，本机当次结果）

## 已接通

- **磁力熊**：最初截图名称对应的主页公告当前 .cc/.uk/.xyz，使用 `https://www.cilixiong.cc`。中文片名 POST 搜索；仅收集同域电影/剧集详情，最多前 8 部、详情并发 2。有效磁力版本逐条展示，保留真实海报。站点要求搜索间隔至少 5 秒，客户端等待 5.5 秒并支持取消，重复查询缓存 5 分钟。空结果与访问频率错误分别识别。
- **archive.org**：已有聚合接入。用户当前配置为停用，保留原选择。

## 第二张截图的精确域名

|用户提供地址|本机核查|本轮结论|
|---|---|---|
|yinfans.me|裸域 TLS 失败；www Loading 页经 /jump-node 转到 huida.one（慧答）|当前书签未返回音范丝搜索，未接入|
|pdoro.com|裸域 526；www 官方公告迁移到 MIYO|在线播放/订阅迁移，非已验证种子源|
|domp4.cc|裸域及 www TLS 失败/超时|未接通|
|tv.syrme.top|握手超时|未接通|
|dytt8.net|裸域及 www TLS 失败/超时|未接通|
|dianying4k.com|Ultra4K 目录与公开详情可读；抽样无磁力，存在登录/注册入口|不能确认匿名磁力搜索，未接入|
|btbtt12.com / btbtt13.com|Python 证书链失败；Windows curl 正常验证亦未取得页面|未接通，不关闭证书校验|
|grab4k.com|裸域/www 握手超时|未接通|
|bugutv.cn|裸域/www TLS 失败|未接通|
|ddys.tv / ddys.site / ddrk.me / ddys.art|均转到 ddys.app，访问密码+验证码页|没有匿名搜索，不自动越过验证|
|imdb.com|影视资料站，当前响应 202|不是磁力搜索源，未作为下载源加入|
|olehdtv.com|TLS 失败|未接通|
|archive.org|200，已有适配器|不重复添加|

## 第一张截图的其他标题

- **中国高清网 CNmkv.com**：站内搜索可访问，抽样详情提供夸克/百度/迅雷网盘，而非 magnet；本轮不计为种子源。
- **BT之家 1LOU**：1lou.me 主页公告可确认品牌，论坛附件流程未验证；不能把它等同于第二图的 BTBTT 域名。
- **不太灵、夸克猫，以及几个只写“4K电影/Watch Full Movies”等的标题**：没有能可靠对应的用户网址，未猜测添加。
- **直播书签**：不适用于当前种子检索与外部下载软件交接流程。

## 证据与限制

`evidence/global-sources/bookmark-domains.json`、`bookmark-www-probe.json`、`bookmark-native-curl.json`、`yinfans-jump.json` 保存有界网络检查；磁力熊搜索/详情和验证输出保存在同目录。

状态只代表本机这次检查，连接失败不等于网站永久关闭。没有进行账号登录或创建、付费、验证码自动绕过、关闭 TLS 校验或修改系统网络设置。下载来源支持和影视内容真实性是两件事。

一手页面：
- https://www.cilixiong.org/ （当前域名公告）
- https://www.cilixiong.cc/ （实际接入）
- https://www.pdoro.com/movies （迁移公告）
- https://dianying4k.com/ （公开目录）
- https://ddys.app/ （访问验证入口）
- https://www.cnmkv.com/ （抽样网盘条目）
