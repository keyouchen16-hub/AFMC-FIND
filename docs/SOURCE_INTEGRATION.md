# 0.2.0 指定来源整合

2026-11-04，按用户提供的六个域名核查。原始拼接地址拆分为 TPB Proxy 和种子楼；知乎跳转链接取用户明确指定的目标。

| 来源 | 当前整合 | 实际证据 |
|---|---|---|
| tpbproxypirate.com | 真实聚合，默认启用 | 首页脚本声明镜像；固定 thepiratebay.party 搜索，ubuntu 实测 30 条，含大小/做种/磁力 |
| m.zhongzilou.com | 真实聚合，默认启用 | /list/ubuntu/1 实测 15 条；详情页磁力与列表哈希核验通过 |
| qiankun6.com | 来源列表中的导航入口 | 当前页面为导航站，不将站点链接作为种子结果 |
| btaa.xyz | 来源列表中的待恢复入口 | Python 严格 TLS 证书链失败，Windows curl 连接超时；未关闭证书校验 |
| www.cilihezi.top | 来源列表中的导航入口 | searchBT 函数直接跳百度并 return，旧聚合逻辑未执行 |
| heimacili.com | 来源列表中的原站入口 | rdata 声明 heimacili.org / heimamo.top；首页可读，正常 UA 搜索仍 HTTP 403 |

继续保留 Internet Archive，默认共三个真实聚合来源。入口类不计入搜索成功来源、不允许伪启用；可点击“打开原站”。不执行网页脚本、广告、验证码或自动改变代理/证书。

## 准确性改动

默认按相关度排序：Unicode 规范化、完整关键词、版本与年份词、完整标题，加上磁力哈希和元数据完整性；做种数作为同分辅助。目的在于减少泛泛标题和错误版本靠前。不是内容真实性保证，没有声称准确率百分比。

种子楼列表的 40 位哈希可构造标准 magnet；实际交接前读取详情并验证 hash 一致。没有做种数则保持未知。TPB 日期没有年份时保持未知，不猜测。沿用跨来源可靠哈希去重。

## 验证与边界

- `evidence/source-audit/final-checks.log`：38 项检查通过，含两个新增真实来源与种子楼详情核验。
- `evidence/source-audit/native-checks.log`：9 项 WPF 检查通过。
- `evidence/source-audit/integration.json`：真实结果数量与首条标题。
- 发布目录 `artifacts/AFMC-FIND-0.2.0-win-x64`；0.1.0 保留用于回退。
- 尚未实现四个入口站的站内聚合，受当前站点性质/访问状态限制。没有将导航入口数当作搜索引擎数。
- 当前仍取第一页，站点可能变化、限流或失效。没有验证资源内容和外部下载软件接收。
- HtmlAgilityPack 1.13.1 锁版用于 HTML DOM 解析，MIT 材料随包附带。
