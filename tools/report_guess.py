# -*- coding: utf-8 -*-
"""统计 data/quests.json 里每个区域的任务是怎么归过来的：
	名字里带地名（高置信） / 伴星任务（高置信） / 按版本批次推（低置信）。
	输出一份给用户看的报告底稿到 %TEMP%\\report.txt。"""
import json, io, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
import parse_bili as P  # noqa: E402

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data', 'quests.json')
d = json.load(io.open(path, encoding='utf-8'))

SUB_NAMES = [kw for kw, _ in P.NAME_REGION]
lines = []
tot = 0
low = 0
for r in d['regions']:
    high = []
    lowc = []
    for q in r['quests']:
        n = q['name']
        if any(s in n for s in SUB_NAMES):
            high.append(q)
        elif q['type'] == 'comp':
            high.append(q)
        else:
            lowc.append(q)
        tot += 1
    low += len(lowc)
    lines.append('## %s（%d 条，其中按版本推测 %d 条）' % (r['name'], len(r['quests']), len(lowc)))
    lines.append('   高置信 %d：%s' % (len(high), '、'.join(x['name'] for x in high) or '（无）'))
    if lowc:
        lines.append('   推测 %d：%s' % (len(lowc), '、'.join(x['name'] for x in lowc)))
    lines.append('')

lines.append('合计 %d 条，其中 %d 条只按「版本批次」推的（低置信）。' % (tot, low))
io.open(os.path.join(os.environ['TEMP'], 'report.txt'), 'w', encoding='utf-8').write('\n'.join(lines))
print('ok', tot, low)
