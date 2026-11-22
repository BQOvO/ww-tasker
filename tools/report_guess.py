# -*- coding: utf-8 -*-
"""统计 data/quests.json 里每条的「在哪儿」是怎么来的：
	任务名里带地名（看得出来） / 名字里没地名（靠版本猜或根本没有）。
	输出一份给用户看的报告底稿到 %TEMP%\\report.txt。

0.7.0 起分组是版本（1.0 … 3.8），地图降级成 subArea，
所以这里统计的不再是「哪个地区的任务」，而是「哪条任务能看出在地图上哪儿」。"""
import json, io, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
import import_bili as B  # noqa: E402

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data', 'quests.json')
d = json.load(io.open(path, encoding='utf-8'))

lines = []
tot = 0
known = 0
for r in d['regions']:
    k = [q for q in r['quests'] if q.get('subArea')]
    u = [q for q in r['quests'] if not q.get('subArea')]
    tot += len(r['quests'])
    known += len(k)
    lines.append('## %s（%d 条，其中 %d 条能看出地图）' % (r['name'], len(r['quests']), len(k)))
    if k:
        lines.append('   地名：%s' % '、'.join('%s（%s）' % (x['name'], x['subArea']) for x in k))
    if u:
        lines.append('   没地名 %d：%s' % (len(u), '、'.join(x['name'] for x in u)))
    lines.append('')

lines.insert(0, '数据源：B 站 UP「叫我棉被」《鸣潮开服以来全任务攻略合集》')
lines.insert(1, '类型照抄 UP 视频标题里的【】标记；地图只从任务名里的地名取，取不到就留空。')
lines.insert(2, '')
lines.append('合计 %d 条，其中 %d 条名字里带地名，%d 条看不出在哪张图。' % (tot, known, tot - known))
io.open(os.path.join(os.environ['TEMP'], 'report.txt'), 'w', encoding='utf-8').write('\n'.join(lines))
print('ok', tot, known, tot - known)
