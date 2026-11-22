# -*- coding: utf-8 -*-
"""把 B 站攻略合集整理出来的任务写进 data/quests.json。

数据源：B 站 UP「叫我棉被」的《鸣潮开服以来全任务攻略合集》
        https://space.bilibili.com/570235512/lists/6498351?type=season
        合集里 24 个视频 = 24 个版本，每个视频的分 P = 一条条任务。

流程：
    python tools/bili_titles.py 合集:570235512/6498351 --json src.json --quiet
    python tools/import_bili.py src.json

生成的 id 形如 jz-10-003（区域短码-版本去点-序号），顺序稳定；
如果旧的 quests.json 里有同名同地区的任务，会沿用它的旧 id，进度不会丢。
"""

import collections
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from parse_bili import load, TAGS_ORDER  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "data", "quests.json")

# 区域定义：id -> (名字, 章节, 排序, 版本)
# 章节按大地图分（瑝珑 / 黑海岸 / 黎那汐塔 / 拉海洛 / 梦州），一个章节里有几个版本的地图，
# 折叠才有意义——否则 11 个章节头各揣一个区域，等于白折一层。
REGIONS = {
    "jz": ("今州", "瑝珑", 10, "1.0"),
    "csx": ("乘霄山", "瑝珑", 20, "1.1"),
    "hha": ("黑海岸", "黑海岸", 30, "1.3"),
    "lnxt": ("黎那汐塔", "黎那汐塔", 40, "2.0"),
    "avnl": ("阿维纽林", "黎那汐塔", 45, "2.2"),
    "qq": ("七丘", "黎那汐塔", 50, "2.4"),
    "sgys": ("桑古伊斯狩原", "黎那汐塔", 55, "2.6"),
    "lhl": ("拉海洛", "拉海洛", 60, "3.0"),
    "lyby": ("罗伊冰原", "拉海洛", 65, "3.1"),
    "yzc": ("夜之城", "拉海洛", 70, "3.4"),
    "mz": ("梦州", "梦州", 80, "3.5"),
}

# 这些地名只当 subArea 用，不单独成区域。
# 合集是按版本组织的，一个版本里的纪闻/危行散在整块地图上，
# 光凭名字分不出子图，硬拆的话 8 个子区域各挂 1 条「古今多少故事」，左列很难看。
SUB_ONLY = [
    ("北落野", "bly"), ("荒石高地", "hsgd"), ("无光之森", "wgs"),
    ("虎口山脉", "hks"), ("怨鸟泽", "ynz"), ("云陵谷", "ylg"),
    ("中曲台地", "zqtd"), ("归墟港市", "gxgs"),
]

# 名字里出现这些地名就把任务细化到那个区域——但只在同一个章节内才认，
# 否则「失踪事件：今州来客」这种会被 3.5 梦州的任务拽到今州去。
NAME_REGION = [
    ("乘霄山", "csx"), ("瑝珑", "jz"), ("今州", "jz"), ("黑海岸", "hha"),
    ("黎那汐塔", "lnxt"), ("七丘", "qq"), ("桑古伊斯狩原", "sgys"),
    ("拉海洛", "lhl"), ("罗伊冰原", "lyby"), ("夜之城", "yzc"),
    ("梦州", "mz"), ("阿维纽林", "avnl"), ("穗波市", "hha"),
]


def sub_area_of(name, chapter):
    """从名字里取子地图名（只在同章节内认），取不到返回空串。"""
    for kw, _rid in SUB_ONLY:
        if kw in name:
            return kw
    return ""


def pick_region(ver_region, name):
    """基准 = 版本对应的区域；名字里的地名只在同章节内才用来细化。"""
    base = ver_region if ver_region in REGIONS else "jz"
    ch = REGIONS[base][1]
    for kw, rid in NAME_REGION:
        if kw in name and rid in REGIONS and REGIONS[rid][1] == ch:
            return rid
    return base


def key_of(s):
    return "".join(ch for ch in (s or "").lower() if not ch.isspace())


def load_old_ids():
    """旧 quests.json 里 名字+区域 -> id 的映射，用来保住已有进度。"""
    try:
        old = json.load(io.open(DATA, encoding="utf-8"))
    except Exception:
        return {}
    m = {}
    for r in old.get("regions", []):
        rid = r.get("id")
        if rid == "bx":            # 0.3 那版把伴星任务单列成一个区域，现在按版本归了
            continue
        for q in r.get("quests", []):
            m[(rid, key_of(q.get("name")))] = q.get("id")
    return m


def build(items, old_ids):
    buckets = collections.defaultdict(list)
    for x in items:
        rid = pick_region(x["region"], x["name"])
        buckets[rid].append(x)

    regions = []
    total = 0
    for rid, x in sorted(buckets.items(), key=lambda kv: REGIONS[kv[0]][2]):
        name, chapter, order, ver = REGIONS[rid]
        # 排序：版本 -> 分 P 序号 -> 类型顺序
        x.sort(key=lambda y: (y["version"], y.get("page") or 0,
                              TAGS_ORDER.get(y["type"], 9)))
        # 同地区同名只留一条（2.6 两个视频里有重复的同一个分 P）
        seen, uniq = {}, []
        for q in x:
            k = key_of(q["name"])
            if k in seen:
                a = seen[k]
                # 类型取更具体的：main/comp 优先，expl 优先于 tale
                rank = {"main": 6, "comp": 5, "expl": 4, "tale": 3,
                        "side": 2, "hidden": 1, "event": 0}
                if rank.get(q["type"], 0) > rank.get(a["type"], 0):
                    a["type"] = q["type"]
                continue
            seen[k] = q
            uniq.append(q)

        quests = []
        used = set()
        for i, q in enumerate(uniq, 1):
            old = old_ids.get((rid, key_of(q["name"])))
            qid = old or "%s-%s-%03d" % (rid, q["version"].replace(".", ""), i)
            # 旧 id 万一撞了（早期版本按子区域分过，重排后可能有重号），谁先来谁拿
            if qid in used:
                qid = "%s-%s-%03d" % (rid, q["version"].replace(".", ""), i)
                n = 2
                while qid in used:
                    qid = "%s-%s-%03d-%d" % (rid, q["version"].replace(".", ""), i, n)
                    n += 1
            used.add(qid)
            item = {
                "id": qid,
                "name": q["name"],
                "type": q["type"],
            }
            if q["type"] == "hidden":
                item["hidden"] = True
            sub = sub_area_of(q["name"], chapter)
            if sub:
                item["subArea"] = sub
            url = "https://www.bilibili.com/video/%s" % q["bvid"]
            if q.get("page"):
                url += "?p=%d" % q["page"]
            item["url"] = url
            quests.append(item)
        total += len(quests)
        regions.append({
            "id": rid, "name": name, "chapter": chapter,
            "order": order, "quests": quests,
        })
    return regions, total


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.environ.get("TEMP", "."), "quest_src.json")
    items = load(src)
    old_ids = load_old_ids()
    regions, total = build(items, old_ids)

    db = {
        "schemaVersion": 2,
        "gameVersion": "3.8",
        "updatedAt": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "note": ("任务数据来自 B 站 UP「叫我棉被」的《鸣潮开服以来全任务攻略合集》"
                 "（space.bilibili.com/570235512/lists/6498351），"
                 "合集里 24 个视频按版本分，每个视频的分 P 就是一条条任务；"
                 "类型用 UP 在标题里标的【潮汐/伴星/道引/纪闻/危行/隐藏/活动】。"
                 "区域按版本归属，名字里带地名的细化到子图。"),
        "regions": regions,
    }
    with io.open(DATA, "w", encoding="utf-8") as f:
        f.write(json.dumps(db, ensure_ascii=False, indent=1))

    print("写入 %s" % DATA)
    print("区域 %d 个，任务 %d 条" % (len(regions), total))
    for r in regions:
        print("  %-6s %-8s %-14s %3d" % (r["id"], r["name"], r["chapter"], len(r["quests"])))


if __name__ == "__main__":
    main()
