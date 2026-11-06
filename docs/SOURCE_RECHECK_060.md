# 2026-11-04 来源复查

| 来源 | 实测与处理 | 聚合状态 |
|---|---|---|
| Internet Archive | 官方advancedsearch真实ubuntu查询通过，恢复启用并备份用户配置 | 已可用 |
| 黑马 | com原站跳安全检测页；org、heimamo.top首页200，search?keyword=ubuntu均403；heimasou.top为安全检测页；更新为org网页入口 | 未恢复 |
| BTAA | 主域与www以及/search/ubuntu返回200，但正文是Redirecting/parklogic域名停放路由 | 无可用搜索服务 |
| 乾坤 | 页面为资源导航，非种子结果接口 | 仅导航 |
| 磁力盒子 | searchBT立即跳baidu.com并return，旧搜索逻辑不可达 | 仅导航 |
| 无极 | 匿名API三体10条，hash均为空；ubuntu0条；不安装外部推广App | 无可聚合磁力 |

原始响应、最终URL和错误见evidence/v060/source-recheck；未关闭TLS验证，未将200或网页可达冒充搜索成功。结果属于当次网络环境与站点状态，不保证永久可用。
