# -*- coding: utf-8 -*-
"""把 B 站攻略合集整理出来的任务写进 data/quests.json。

数据源：B 站 UP「叫我棉被」的《鸣潮开服以来全任务攻略合集》
        https://space.bilibili.com/570235512/lists/6498351?type=season
        合集里 24 个视频 = 24 个版本，每个视频的分 P = 一条条任务。

流程：
    python tools/bili_titles.py 合集:570235512/6498351 --json src.json --quiet
    python tools/import_bili.py src.json

按版本分组（1.0 / 1.1 / … / 3.8），不再按地图分——合集本来就是按版本组织的，
一个版本的纪闻危行散在整张大地图上，硬按地图拆既拆不准、也会把同一次更新的任务打散。
地图信息降级成每条任务的 subArea（任务行下面那行灰色小字），只当线索。

生成的 id 形如 v10-003（版本去点-序号），顺序稳定；
旧 quests.json 里同版本同名的任务会沿用它的旧 id，进度不会丢。
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

# 地名表：任务名里出现哪个，就把这条的 subArea 记成它。
# 长名字放前面（「桑古伊斯狩原」比「狩原」优先），免得被短词先截走。
AREA_WORDS = [
    "桑古伊斯狩原", "荒石高地", "无光之森", "虎口山脉", "归墟港市", "中曲台地",
    "乘霄山", "北落野", "怨鸟泽", "云陵谷", "今州", "瑝珑",
    "黎那汐塔", "阿维纽林", "七丘", "拉海洛", "罗伊冰原", "夜之城", "梦州",
    "黑海岸", "穗波市", "隐海试验场", "三王峰", "四方殿",
]

# 这些不是地图，别当 subArea 用
NOT_AREA = ("任务", "地区")

# 类型优先级：同名撞车时留更具体的那条
RANK = {"main": 6, "comp": 5, "expl": 4, "tale": 3, "side": 2, "hidden": 1, "event": 0}


def key_of(s):
    return "".join(ch for ch in (s or "").lower() if not ch.isspace())


def ver_key(v):
    """'1.0' -> '10'，用来跟 id 里的版本段对上。"""
    return (v or "").replace(".", "")


def sub_area_of(name):
    """从任务名里取地图名，取不到返回空串。"""
    for w in AREA_WORDS:
        if w in name:
            return w
    return ""


def load_old_ids():
    """旧 quests.json 里 (版本, 名字) -> id 的映射，用来保住已有进度。

    旧 id 是 `jz-10-003` 这种，中间那段就是版本去点后的样子，
    所以即使区域从「地图」换成「版本」，也能把同一个任务认回来。
    """
    try:
        old = json.load(io.open(DATA, encoding="utf-8"))
    except Exception:
        return {}
    m = {}
    for r in old.get("regions", []):
        for q in r.get("quests", []):
            qid = q.get("id") or ""
            m["%s|%s" % (ver_key(q.get("v")), key_of(q.get("name")))] = qid
            parts = qid.split("-")
            if len(parts) >= 3:
                m["%s|%s" % (parts[-2], key_of(q.get("name")))] = qid
    return m


def build(items, old_ids):
    buckets = collections.defaultdict(list)
    for x in items:
        buckets[x["version"]].append(x)

    def vord(v):
        return [int(n) for n in v.split(".")]

    regions = []
    total = 0
    for i, ver in enumerate(sorted(buckets.keys(), key=vord), 1):
        x = buckets[ver]
        # 排序：分 P 序号 -> 类型顺序（合集里分 P 本身就是播放顺序，别打乱）
        x.sort(key=lambda y: (y.get("page") or 0, TAGS_ORDER.get(y["type"], 9)))

        # 同版本同名只留一条（有的版本两个分 P 讲同一个任务）
        seen, uniq = {}, []
        for q in x:
            k = key_of(q["name"])
            if k in seen:
                a = seen[k]
                if RANK.get(q["type"], 0) > RANK.get(a["type"], 0):
                    a["type"] = q["type"]
                continue
            seen[k] = q
            uniq.append(q)

        rid = "v" + ver_key(ver)
        quests = []
        used = set()
        for n, q in enumerate(uniq, 1):
            qid = old_ids.get("%s|%s" % (ver_key(ver), key_of(q["name"])))
            if qid in used:
                qid = None
            if not qid:
                qid = "%s-%03d" % (rid, n)
                k = 2
                while qid in used:
                    qid = "%s-%03d-%d" % (rid, n, k)
                    k += 1
            used.add(qid)

            item = {"id": qid, "name": q["name"], "type": q["type"]}
            if q["type"] == "hidden":
                item["hidden"] = True
            sub = sub_area_of(q["name"])
            if sub:
                item["subArea"] = sub
            url = "https://www.bilibili.com/video/%s" % q["bvid"]
            if q.get("page"):
                url += "?p=%d" % q["page"]
            item["url"] = url
            quests.append(item)

        total += len(quests)
        regions.append({
            "id": rid,
            "name": ver,
            "chapter": "",
            "order": i * 10,
            "quests": quests,
        })
    return regions, total


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.environ.get("TEMP", "."), "quest_src.json")
    items = load(src)
    old_ids = load_old_ids()
    regions, total = build(items, old_ids)

    db = {
        "schemaVersion": 3,
        "gameVersion": "3.8",
        "updatedAt": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "note": ("任务数据来自 B 站 UP「叫我棉被」的《鸣潮开服以来全任务攻略合集》"
                 "（space.bilibili.com/570235512/lists/6498351），"
                 "合集里 24 个视频按版本分，每个视频的分 P 就是一条条任务。"
                 "类型用 UP 在标题里标的【潮汐/伴星/道引/纪闻/危行/隐藏/活动】。"
                 "分组按版本（1.0 … 3.8），地图降级成 subArea。"),
        "regions": regions,
    }
    with io.open(DATA, "w", encoding="utf-8") as f:
        f.write(json.dumps(db, ensure_ascii=False, indent=1))

    print("写入 %s" % DATA)
    print("版本 %d 个，任务 %d 条" % (len(regions), total))
    for r in regions:
        print("  %-6s %-6s %3d" % (r["id"], r["name"], len(r["quests"])))


if __name__ == "__main__":
    main()
