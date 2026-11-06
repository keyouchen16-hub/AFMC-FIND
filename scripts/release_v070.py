from pathlib import Path
for name in ['src/Afmc.Find.App/Afmc.Find.App.csproj','scripts/Build.ps1','scripts/Package.ps1','scripts/Verify-Brand.ps1','scripts/Verify-Release.ps1','scripts/Verify-Glass.ps1','docs/PORTABLE.txt']:
 p=Path(name);p.write_text(p.read_text(encoding='utf-8-sig').replace('0.6.7','0.7.0').replace('v067','v070'),encoding='utf-8')
p=Path('tests/Afmc.Find.Checks/Program.cs');p.write_text(p.read_text(encoding='utf-8-sig').replace('docs/evidence/v064','docs/evidence/v070'),encoding='utf-8')
with Path('docs/PORTABLE.txt').open('a',encoding='utf-8') as f:f.write('''
0.7.0 搜索质量与工作台：
- 识别年份、季集、版本、4K/2160p及部分语言标签；由明确双语条目推导别名。
- 只有名称/年份/季集足够明确的影视资源可比较版本，不猜测无年份作品。
- 类别/大小/有做种筛选；资源信息复制；详情图片保留来源/推断说明。
- 来源状态显示耗时和失败类型；只重试原查询失败的来源，保留成功结果。
- 连续失败或限流进入短时冷却；来源做种是站点报告值，不保证实时健康。
- 搜索历史最多50条、收藏最多500条，Windows用户加密保存在本机；设置可关闭历史，历史窗口可清空。
- 诊断只复制版本/系统/来源类型与状态，不含查询、标题、邮箱、URL或API Key；不自动发送。
- 更新前检查现有配置可读取，暂存文件逐一校验；启动失败自动恢复旧程序，个人配置不回滚覆盖。
- 安装备份不包含history.dat，避免清空历史后在新安装备份中残留历史副本。
''')
Path('docs/evidence/v070').mkdir(exist_ok=True)
