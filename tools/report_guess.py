# -*- coding: utf-8 -*-
"""统计 data/quests.json 里每个区域的任务来源：主线(高置信) / 地名命中(高置信) /
	伴星单列 / 按版本批次推(低置信)。输出一份给用户看的报告底稿。"""
import json, io, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
import import_wiki as W

path = os.path.join(os.path.dirname(__file__), '..', 'data', 'quests.json')
d = json.load(io.open(path, encoding='utf-8'))

hl_sub_names = list(W.HL_SUB.keys())
lines = []
tot = 0
low = 0
for r in d['regions']:
    high = []
    lowc = []
    for q in r['quests']:
        n = q['name']
        nn = W.norm(n)
        if nn in W.MAIN_REGION or nn.replace('·上', '').replace('·下', '') in W.MAIN_REGION:
            high.append(q)
        elif q['type'] == 'comp':
            high.append(q)
        elif any(s in n for s in hl_sub_names):
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

lines.append('合计 %d 条，其中 %d 条地区是按「版本批次」推的（低置信）。' % (tot, low))
io.open(os.path.join(os.environ['TEMP'], 'report.txt'), 'w', encoding='utf-8').write('\n'.join(lines))
print('ok', tot, low)
