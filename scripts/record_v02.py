from pathlib import Path
doc=Path('docs')
p=doc/'PROJECT_STATE.md';s=p.read_text(encoding='utf-8');p.write_text('''# 当前状态 — 0.2.0

用户要求整合六个指定站点并改善搜索准确性。已实现 TPB 与种子楼真实聚合，保留 Internet Archive；另外四站作为明确标注状态的原站入口，不能宣称已接通聚合。默认相关度排序与跨源哈希去重；种子楼交接前核验详情哈希。

38 项核心/来源检查、9 项原生检查通过。独立发布 0.2.0，保留 0.1.0 目录；没有覆盖个人配置或修改系统关联。详见 SOURCE_INTEGRATION.md 和 evidence/source-audit。

下一步：用用户实际查询进行相关性对比；四个入口站只有在提供实际可访问的结果接口后才能升级为聚合源。当前无翻页，下载接收与内容真实性未验证。

以下为 0.1.0 历史状态，不代表当前来源范围。

'''+s,encoding='utf-8')
updates={
'DECISIONS.md':'\n## 0.2.0\n用户指定六站加入来源目录。TPB使用入口公布且实测可用的固定镜像，种子楼按需校验详情。导航/失败站保留明确入口，不虚构结果。HtmlAgilityPack 1.13.1 MIT锁版；默认排序由做种数改为关键词与元数据相关度。\n',
'TASKS.md':'\n## 0.2.0 新任务\n- done：六站逐站核查、目录展示；TPB/种子楼真实搜索与详情验证；相关度排序。\n- done：38项检查、9项原生检查，自包含0.2.0发布。\n- 未完成：乾坤/磁力盒子无直接聚合接口，黑马403、BTAA连接验证失败，当前仅原站入口。\n- todo：用户实际关键词的相关度验收、分页与外部软件下载接收。\n',
'ENVIRONMENT.md':'\n## 0.2.0\n新增 NuGet HtmlAgilityPack 1.13.1，包锁文件已更新。网站访问保留严格TLS；黑马搜索403、BTAA证书/超时证据见source-audit。工具输出乱码曾被误判为网页编码，已通过严格UTF8解码纠正；产品使用UTF8。\n',
'TOOLS.md':'\n## 0.2.0\n复用 search-first 工作流与此前研究 Agent，进行了只读并行站点核查。实际使用 Python HTTP探测、GitHub官方许可证读取、HtmlAgilityPack DOM解析、.NET检查与Windows UIA。未执行广告脚本或绕过验证码/TLS。\n',
'CHANGELOG.md':'\n## 2026-11-04 — 0.2.0 多源搜索\n新增TPB和种子楼真实聚合、六站来源列表及状态、默认相关度排序、种子楼详情哈希核验。38项检查与9项原生检查通过。0.1.0目录保留。其余四站仅入口，未宣称聚合完成。\n',
'THIRD_PARTY.md':'\n## 0.2.0 新依赖\n新增 HtmlAgilityPack 1.13.1（MIT），PackageReference和packages.lock.json固定版本；许可文件为HtmlAgilityPack-LICENSE.txt并随包分发。此前“没有第三方NuGet依赖”仅适用于0.1.0。站点访问并未复制服务端代码。\n',
'VERIFICATION.md':'\n## 0.2.0 增量验收\n38项核心/网站检查与9项原生检查通过，见evidence/source-audit/final-checks.log与native-checks.log。新增TPB30条、种子楼15条真实ubuntu结果及详情哈希验证；保持所有失败/导航入口状态透明。完整范围见SOURCE_INTEGRATION.md。\n'}
for name,content in updates.items():
    with (doc/name).open('a',encoding='utf-8') as f:f.write(content)
p=doc/'PORTABLE.txt';s=p.read_text(encoding='utf-8').replace('0.1.0','0.2.0');s=s.replace('默认 Internet Archive 公开源无需密钥。','默认 TPB、种子楼、Internet Archive 三源聚合，无需密钥。\n另外四个指定站点保留明确状态的原站入口，不计入聚合源。\n默认相关度排序；站点元数据不等于内容真实性。');p.write_text(s,encoding='utf-8')
p=Path('README.md');s=p.read_text(encoding='utf-8').replace('0.1.0','0.2.0');s=s.replace('## 运行','## 当前多源范围\n\nTPB、种子楼、Internet Archive 参与真实聚合；乾坤、磁力盒子、BTAA、黑马显示实际状态并提供原站入口，尚不能直接聚合。见 [来源核查](docs/SOURCE_INTEGRATION.md)。默认相关度排序并保留哈希去重。\n\n## 运行',1);p.write_text(s,encoding='utf-8')
