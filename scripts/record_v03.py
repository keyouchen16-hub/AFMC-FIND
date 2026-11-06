from pathlib import Path
d=Path('docs')
note='''# 当前状态 — 0.3.0

已实现每源最多3页搜索、跨页/跨源去重、重复页和短页停止、后续页失败保留前页。ubuntu实测TPB90、种子楼45、Archive150，总计285条；0.2.0相同查询95条。此数字仅为该查询当次结果，不是总资源量。

原生列表和详情新增异步缩略图，当前使用Internet Archive原条目图片，支持图片来源筛选。TPB/种子楼抽查无封面，显示暂无封面，不用logo、二维码或同名作品图替代。4并发、15秒超时、4MB响应上限、240像素解码、内存缓存上限160条。

明确年份/版本缺失时降权并显示匹配提示，避免做种数或元数据完整性盖过查询约束。没有承诺内容真实性或准确率百分比。

38项核心/联网检查通过，原生界面验收见evidence/v03/native-final.log；发布截图/结果见evidence/v03/published-verification.json。0.2.0及更早产物保留，配置不覆盖。

仍未完成：为无图来源补齐经过消歧的影视/书籍元数据封面、四个失效/导航站的直接聚合、超过3页的交互翻页、默认下载软件接收。TMDB等元数据服务需要用户自己的凭据和对应使用条款，当前没有接入。

以下为历史版本记录。

'''
p=d/'PROJECT_STATE.md';p.write_text(note+p.read_text(encoding='utf-8'),encoding='utf-8')
updates={
'TASKS.md':'\n## 0.3.0\n- done：三页搜索、去重/短页/重复页停止、部分失败保留、年份版本排序约束。\n- done：原条目图片异步加载、缺图提示、图片来源筛选、大小/协议限制。\n- done：38项检查；原生筛选/窗口验证见v03证据。\n- todo：无图来源的可信元数据匹配；四个未接通来源；3页之外交互翻页。\n',
'DECISIONS.md':'\n## 0.3.0\n保留既有三栏布局，只在结果行和详情增加缩略图，不做大型重设计。每源最多3页，45秒总时限，短页/重复页停止。缩略图只取Archive原条目，其他无封面，不按标题随意补图。年份/版本约束优先于热度，匹配理由可见。\n',
'ENVIRONMENT.md':'\n## 0.3.0\n沿用SDK与现有依赖，无新包。Archive services/img真实JPEG读取及WPF解码通过。TPB/种子楼第2页与第1页不同，第三页集成测试通过。源查询可能出现后续页跳转，保留前页并提示。\n',
'TOOLS.md':'\n## 0.3.0\n复用只读研究Agent核查分页、封面，不执行广告脚本。使用.NET/WPF、UIA、真实API和图片解码验收。发布脚本曾出现PowerShell Query/query大小写冲突，已修复并保留失败日志。\n',
'CHANGELOG.md':'\n## 2026-11-04 — 0.3.0\n三页资源扩展、部分失败保留、来源缩略图及筛选、年份/版本排序约束。ubuntu实测95→285。38项核心/联网检查通过。无图来源明确占位，不能声称所有资源均有封面。旧版保留。\n',
'VERIFICATION.md':'\n## 0.3.0\n38项核心/网络检查通过（evidence/v03/checks-final.log）；测试真实分页增量、后续页失败保留、版本约束、真实JPEG读取/解码、图片协议及域名拒绝。原生测试新增有图来源筛选。实际发布版图片渲染见v03发布证据。Big Buck Bunny查询曾出现种子楼后续页跳转，前页保留，其余源完成；不视作所有来源无故障。发布脚本首次因Query/query冲突失败，修复后通过。\n'}
for name,text in updates.items():
    with (d/name).open('a',encoding='utf-8') as f:f.write(text)
for p in [Path('README.md'),d/'PORTABLE.txt']:
    s=p.read_text(encoding='utf-8').replace('0.2.0','0.3.0')
    s=s.replace('当前无翻页','每源最多自动读取3页').replace('当前没有翻页','每源最多自动读取3页')
    s += '\n0.3.0：自动扩展最多3页，短页/重复页停止；图片当前仅来自Internet Archive原条目，其他来源可能无封面。支持“有来源图片”筛选。年份/版本缺失结果降权。\n'
    p.write_text(s,encoding='utf-8')
