# 无极磁链入口核查

用户地址：https://july.xincili01ng.top/sh/?channel=&cid=2513

2026-11-04 本机入口 HTTP 200，真实网页搜索为 POST `/api/v1/search`，参数为 page、keyword、order_by、in_app=false。查询“三体”返回10条标题，hash全部为空、idx全部为null，未提供magnet/torrent/详情链接。网页按钮调用外部应用引导，不能作为有效磁力结果交接。没有安装推广应用、执行统计JS或伪造APP访问状态。

0.4.1 将它列为“无极磁链”原站入口，Disabled/Website，不参与聚合，不增加可用搜索源数量。证据见 evidence/xincili/summary.json 及接口样本。原生发布版入口状态已验证，见 evidence/v041/source-entry.json。
