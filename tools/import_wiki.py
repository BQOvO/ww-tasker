# -*- coding: utf-8 -*-
"""
从库街区官方 wiki 抓「任务合集」，整理成 data/quests.json。

    python tools/import_wiki.py

依赖：只用标准库（urllib / json）。需要能访问 api.kurobbs.com。

地区归属的判定顺序：
  1. 主线（潮汐任务）→ 按 Fandom 中文站「潮汐任务」页的章节归属，置信度高
  2. 任务名里带瑝珑子地图名 → 归该子图（如「古今多少故事·北落野」）
  3. 其余 → 按「该任务首次出现的版本批次 → 该版本主打地区」推断，**置信度低**
第 3 类会在最后一并打印出来，供人工复核。

注意：id 一旦发出去就不要再改，存档只记 id。这里的 id 用「区域短码-库街区条目 id」，
条目 id 是 wiki 自己分配的，不会因为重排而变。
"""
import json, os, re, sys, datetime, urllib.request
from collections import OrderedDict, defaultdict

API = 'https://api.kurobbs.com'
HEADERS = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    'Content-Type': 'application/x-www-form-urlencoded',
    'Referer': 'https://wiki.kurobbs.com/',
    'wiki_type': '9',
    'source': 'h5',
    'devcode': 'abcdef0123456789abcdef0123456789',
}
CATALOGUE_QUESTS = 1249   # 任务合集（381 条，全部任务类型）
CATALOGUE_STORY = 1330    # 潮汐任务剧情（主线，用来补任务合集里漏掉的）
STORY_EXTRA_REGION = {'边缘幻梦': 'yzc'}   # 1330 里有、但主线表没有的（3.4 外传）
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'data', 'quests.json')

# 库街区「类型」标签 -> 我们用的 type
TAGS = {'501': 'main', '502': 'comp', '503': 'tale', '504': 'side',
        '505': 'expl', '506': 'daily', '806': 'event', '861': 'hidden'}

# 版本上线日 -> 版本号（从「活动合集」各版本目录的 updateAt 实测）
VERS = [
    ('2024-05-22', '1.0'), ('2024-06-28', '1.1'), ('2024-08-12', '1.2'),
    ('2024-09-27', '1.3'), ('2024-11-13', '1.4'), ('2024-12-31', '2.0'),
    ('2025-02-12', '2.1'), ('2025-03-26', '2.2'), ('2025-04-27', '2.3'),
    ('2025-06-11', '2.4'), ('2025-07-23', '2.5'), ('2025-08-27', '2.6'),
    ('2025-10-08', '2.7'), ('2025-11-19', '2.8'), ('2025-12-22', '3.0'),
    ('2026-02-02', '3.1'), ('2026-03-17', '3.2'), ('2026-04-29', '3.3'),
    ('2026-06-07', '3.4'), ('2026-07-08', '3.5'), ('2026-08-18', '3.6'),
    ('2026-09-29', '3.7'), ('2026-11-11', '3.8'),
]

# 版本 -> 该版本主打地区（低置信，只用于非主线任务）
# 依据：版本内任务名的地域特征；库街区「区域探索」各地区的攻略帖标题里写的版本号
# （例：桑古伊斯狩原的帖子里写「2.6 一条龙」→ 2.6 就是桑古伊斯狩原）
VER_REGION = {
    '1.0': 'jz', '1.1': 'jz', '1.2': 'jz', '1.3': 'hha', '1.4': 'hha',
    '2.0': 'lnxt', '2.1': 'lnxt', '2.2': 'lnxt', '2.3': 'lnxt', '2.4': 'qq',
    '2.5': 'lnxt', '2.6': 'sgys', '2.7': 'lnxt', '2.8': 'hha',
    '3.0': 'lhl', '3.1': 'lhl', '3.2': 'lhl', '3.3': 'lyby', '3.4': 'yzc',
    '3.5': 'lhl', '3.6': 'mz', '3.7': 'lyby', '3.8': 'mz',
}

# 主线任务 -> 地区（依据 Fandom 中文站「潮汐任务」的分章分幕，置信度高）
MAIN_REGION = {}


def norm(s):
    """去掉所有空白（含全角空格），用来做名字比对。"""
    return re.sub(r'\s+', '', s or '')


for n in ['万象新声·上', '万象新声·下', '嘤鸣初相召', '撞金止行阵', '奔策候残星',
          '庭际刀刃鸣', '欲知天将雨', '千里卷戎旌·上', '千里卷戎旌·下',
          '行路遇新知', '往岁乘霄醒惊蛰']:
    MAIN_REGION[norm(n)] = 'jz'                # 第一章 瑝珑
MAIN_REGION[norm('万象新声')] = 'jz'
MAIN_REGION[norm('千里卷戎旌')] = 'jz'
for n in ['行至海岸尽头', '曙光停摆于荒地之上', '星光流转于眼眸之间']:
    MAIN_REGION[norm(n)] = 'hha'               # 第八章 / 12 幕间
for n in ['如一叶小舟穿行于茫茫海洋', '那神圣微风时常吹入', '夜与昼，均请摘下面纱',
          '昔我悲伤，今却歌唱', '老人鱼海', '圣者，忤逆者，告死者', '荣耀暗面',
          '燃烧的心', '捕梦于神秘园中', '铁锈，剑与烈阳', '灼我以烈阳',
          '今夜，注定属于月亮', '已逝的必将归来', '暗潮将映的黎明',
          '独在异乡为异客']:
    MAIN_REGION[norm(n)] = 'lnxt'              # 第二章 黎那汐塔
for n in ['未知的既感', '冰原下的星炬', '致第二次日出', '远航星', '日光落处',
          '影下不落的黄金', '影面颠倒的兔影', '明日摇曳的绯影', '愿系铃中',
          '愿系铃中·续', '昨夜群星', '在熔解的夜空下',
          '我们选择天空']:
    MAIN_REGION[norm(n)] = 'lhl'               # 第三章 拉海洛
MAIN_REGION[norm('边缘幻梦')] = 'yzc'          # 3.4 外传（夜之城）
for n in ['山雨欲来风满楼', '玄翎振壑定澜音', '灵籁之契冥相因', '烟云幽远心剑鸣',
          '幽客销残声', '梦枢心相由心生', '予以明日的花束']:
    MAIN_REGION[norm(n)] = 'mz'                # 第四章 梦州

# 区域表：短码 -> (名字, 章节, 排序)
REGIONS = OrderedDict([
    ('jz',   ('今州',         '1.0 瑝珑',     10)),
    ('bly',  ('北落野',       '1.0 瑝珑',     20)),
    ('hsgd', ('荒石高地',     '1.0 瑝珑',     30)),
    ('wgs',  ('无光之森',     '1.0 瑝珑',     40)),
    ('hks',  ('虎口山脉',     '1.0 瑝珑',     50)),
    ('ynz',  ('怨鸟泽',       '1.0 瑝珑',     60)),
    ('ylg',  ('云陵谷',       '1.0 瑝珑',     70)),
    ('zqtd', ('中曲台地',     '1.0 瑝珑',     80)),
    ('gxgs', ('归墟港市',     '1.0 瑝珑',     90)),
    ('hha',  ('黑海岸',       '1.3 黑海岸',  100)),
    ('lnxt', ('黎那汐塔',     '2.0 黎那汐塔', 110)),
    ('qq',   ('七丘',         '2.4 黎那汐塔', 120)),
    ('sgys', ('桑古伊斯狩原', '2.6 黎那汐塔', 130)),
    ('lhl',  ('拉海洛',       '3.0 拉海洛',  140)),
    ('lyby', ('罗伊冰原',     '3.3 拉海洛',  150)),
    ('yzc',  ('夜之城',       '3.4 拉海洛',  160)),
    ('mz',   ('梦州',         '3.5 梦州',    170)),
    ('bx',   ('伴星任务',     '角色专属',    180)),
])

# 瑝珑子地图：任务名里出现这些地名就归该子图
HL_SUB = OrderedDict([
    ('北落野', 'bly'), ('荒石高地', 'hsgd'), ('无光之森', 'wgs'),
    ('虎口山脉', 'hks'), ('怨鸟泽', 'ynz'), ('云陵谷', 'ylg'),
    ('中曲台地', 'zqtd'), ('归墟港市', 'gxgs'), ('乘霄山', 'jz'),
])

TYPE_ORDER = {'main': 0, 'tale': 1, 'comp': 2, 'side': 3, 'expl': 4, 'hidden': 5}


def post(path, data=''):
    req = urllib.request.Request(API + path, data=data.encode('utf-8'),
                                 headers=HEADERS, method='POST')
    with urllib.request.urlopen(req, timeout=40) as r:
        return json.loads(r.read().decode('utf-8'))


def fetch_all(catalogue_id):
    out, page = [], 1
    while True:
        r = post('/wiki/core/catalogue/item/getPage',
                 'catalogueId=%d&page=%d&pageSize=200' % (catalogue_id, page))
        res = (r.get('data') or {}).get('results') or {}
        recs = res.get('records') or []
        out.extend(recs)
        if len(recs) < 200 or len(out) >= (res.get('total') or 0):
            break
        page += 1
    return out


def ver_of(iso):
    cur = VERS[0][1]
    for d, v in VERS:
        if iso >= d:
            cur = v
    return cur


def main():
    recs = fetch_all(CATALOGUE_QUESTS)
    print('抓到 %d 条任务条目' % len(recs), file=sys.stderr)

    buckets = defaultdict(list)
    skipped, lowconf = [], []
    seq = 0
    for r in recs:
        c = r.get('content') or {}
        name = (r.get('name') or '').strip()
        if not name:
            continue
        tags = [TAGS.get(str(t), '') for t in (c.get('relateTagIds') or [])]
        t = next((x for x in ('main', 'comp', 'tale', 'side', 'expl', 'hidden')
                  if x in tags), '')
        if 'daily' in tags or 'event' in tags:
            skipped.append(name)
            continue
        if not t:
            t = 'side'
            lowconf.append((name, 'wiki 上没有类型标签，按支线处理'))

        ua = c.get('updateAt')
        iso = datetime.datetime.fromtimestamp(ua / 1000, datetime.UTC).date().isoformat() \
            if ua else '2024-05-22'

        if norm(name) in MAIN_REGION:
            rid, conf = MAIN_REGION[norm(name)], '主线章节'
        elif t == 'comp':
            rid, conf = 'bx', '伴星任务'
        else:
            rid, conf = None, None
            for sub, code in HL_SUB.items():
                if sub in name:
                    rid, conf = code, '名字带地名'
                    break
            if rid is None:
                v = ver_of(iso)
                rid = VER_REGION.get(v, 'jz')
                conf = '%s 版本主打地区' % v
                lowconf.append((name, conf))

        sub = ''
        m = re.search(r'古今多少故事·(.+)', name)
        if m:
            sub = m.group(1).replace('（隐藏）', '')

        item = {'id': '%s-%s' % (rid, r.get('id')), 'name': name, 'type': t}
        if sub:
            item['subArea'] = sub
        if 'hidden' in tags:
            item['hidden'] = True
        seq += 1
        buckets[rid].append((TYPE_ORDER.get(t, 9), seq, item))

    # 任务合集漏掉的主线（3.5 以后的梦州主线、3.4 外传），从「潮汐任务剧情」补。
    # 上/下分集的（万象新声·上、千里卷戎旌·下）任务合集里是合的一条，跳过免得重复。
    known_norm = set(norm(x[2]['name']) for v in buckets.values() for x in v)
    extra = 0
    for r in fetch_all(CATALOGUE_STORY):
        raw = (r.get('name') or '').strip()
        name = re.sub(r'\s*·\s*[上下]$', '', raw)
        if not name or norm(name) in known_norm or norm(raw) in known_norm:
            continue
        rid = MAIN_REGION.get(norm(name)) or STORY_EXTRA_REGION.get(name)
        if not rid:
            continue
        item = {'id': '%s-%s' % (rid, r.get('id')), 'name': name, 'type': 'main'}
        seq += 1
        buckets[rid].append((TYPE_ORDER['main'], seq, item))
        known_norm.add(norm(name))
        extra += 1
    if extra:
        print('从潮汐任务剧情补了 %d 条主线' % extra, file=sys.stderr)

    regions = []
    for code, (name, chapter, order) in REGIONS.items():
        qs = [x[2] for x in sorted(buckets.get(code, []), key=lambda x: (x[0], x[1]))]
        regions.append({'id': code, 'name': name, 'chapter': chapter,
                        'order': order, 'quests': qs})

    db = {
        'schemaVersion': 1,
        'gameVersion': VERS[-1][1],
        'updatedAt': datetime.datetime.now().strftime('%Y-%m-%dT%H:%M:%S'),
        'note': ('任务名来自库街区官方 wiki「任务合集」，已剔除日常任务和纯活动任务。'
                 '地区归属：主线按 Fandom 中文站章节；带瑝珑地名的归该子图；'
                 '其余按版本批次推断，可能有偏差。id 不要手改——存档只记 id。'),
        'regions': regions,
    }
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(db, f, ensure_ascii=False, indent=2)

    total = sum(len(r['quests']) for r in regions)
    print('写入 %s' % os.path.normpath(OUT), file=sys.stderr)
    print('共 %d 条（剔除日常/活动 %d 条）' % (total, len(skipped)), file=sys.stderr)
    for r in regions:
        print('  %-12s %-12s %d' % (r['name'], r['chapter'], len(r['quests'])),
              file=sys.stderr)
    if lowconf:
        print('\n低置信归属 %d 条，需要人工复核：' % len(lowconf), file=sys.stderr)
        for n, why in lowconf:
            print('  %-26s %s' % (n, why), file=sys.stderr)


if __name__ == '__main__':
    main()
