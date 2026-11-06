from pathlib import Path
state=Path('docs/PROJECT_STATE.md')
s=state.read_text(encoding='utf-8-sig').replace('# 当前状态 — 0.6.6','# 历史状态 — 0.6.6',1)
state.write_text('''# 当前状态 — 0.6.7 搜索来源扩容

新增Torrents.csv、LimeTorrents、Tokyo Toshokan、Academic Torrents四个真实聚合来源，合计11个。复用HAP/XML/JSON、超时/大小上限/哈希去重，新增游标翻页及每日科研元数据缓存。新源默认补入，已有启停保持。来源证据及未接入原因见SOURCE_RESEARCH_067.md。

已覆盖安装0.6.7，旧0.6.6正常退出，无强制结束，固定目录仅一个运行进程。56核心/26新增适配/16UI检查通过，构建0警告0错误。原测试写死来源数12，更新为完整目录+无重复ID断言。实际安装版ubuntu首轮438条、10成功1失败（BitSearch）；复查API200，第二轮730条、11成功0失败，部分来源复用缓存。未下载资源正文/调用下载软件；远端可能再次波动，不能保证资源真实性。

'''+s,encoding='utf-8')
entries={
 'CHANGELOG.md':'0.6.7：新增四个聚合来源（综合/日系/科研），游标分页、广告排除、科研缓存、异常输入检查；原有UI和配置保留。已覆盖安装，详见SOURCE_RESEARCH_067.md。',
 'TASKS.md':'[完成 / Codex] 检索候选、普通HTTP实测、四源接入、26项适配检查、原核心/UI回归、覆盖安装及11源安装版查询。暂不接入403/超时/重复跳转/不支持关键词的候选，具体证据见SOURCE_RESEARCH_067.md。',
 'DECISIONS.md':'0.6.7：独立解析公开接口，不引入后台索引器；不重复计SolidTorrents/BitSearch。Academic使用官方公开元数据XML，会话24小时缓存；Lime不抓逐条详情，限定真实结果表及允许的哈希缓存域名。已保存来源启停不改变。',
 'ENVIRONMENT.md':'0.6.7继续使用现有.NET10/WPF/HAP，无新增第三方包或系统服务。安装路径%LOCALAPPDATA%/Programs/AFMC FIND；配置路径不变。',
 'TOOLS.md':'0.6.7新增Probe-Sources067.py/Probe-Formats067.py用于有限元数据探测；--expanded检查新增适配器；--expanded-live单独真实查询。Build.ps1发布前包含扩展检查；Verify-Release.ps1改验收固定安装路径并使用PrintWindow。',
 'VERIFICATION.md':'0.6.7：56核心+26扩展+16UI通过。四源单独实测通过；安装版ubuntu复测730条/11成功/0失败，首轮BitSearch失败记录保留。evidence/v067包含两轮JSON与窗口截图；没调用下载程序。'
}
for name,text in entries.items():
 p=Path('docs')/name
 with p.open('a',encoding='utf-8') as f:f.write('\n\n## 2026-11-04 · 0.6.7\n\n'+text+'\n')
