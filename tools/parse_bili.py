# -*- coding: utf-8 -*-
"""把 B 站攻略合集的分 P 标题整理成任务清单。

输入：quest_src.json（bili_titles.py 拉下来的 24 个版本合集 + 各自分 P）
输出：解析后按版本归好的任务数组，供 import_bili.py 写 quests.json

标题长这样（同一个 UP 的两种写法都见得到）：
    p1【潮汐任务】1-0  万象新声-上-下
    ★p15【纪闻任务】营地突袭（1个宝箱）
    ⊙!p8【纪闻任务】修补之心（含1个取偶匣）
    【潮汐任务】第二章-第八幕：灼我与烈阳
    p5-【道引任务】爱乐之骸
"""

import io
import json
import re

# UP 主在分 P 标题里用的【】标签 -> 我们的 type
TAG_MAP = {
    "潮汐任务": "main",
    "伴星任务": "comp",
    "道引任务": "tale",
    "纪闻任务": "side",
    "危行任务": "expl",
    "隐藏任务": "hidden",
    "活动任务": "event",
    "捕鱼活动任务": "event",
    "2.17纪闻任务": "side",
    "3.19活动任务": "event",
    "3.21活动任务": "event",
    "纪闻任务-潮汐续闻": "side",
    "潮汐续闻": "main",
    "异闻任务": "side",
    "奇谭任务": "tale",
}

# 这些标签不是任务，是宝箱/成就分集，整条跳过
SKIP_TAGS = {"旌旗不落赛事宝箱", "隐藏宝箱", "千岛路书", "四方殿地下"}

# 排序用：类型在同一个版本里的先后
TAGS_ORDER = {"main": 0, "comp": 1, "tale": 2, "side": 3,
              "expl": 4, "hidden": 5, "event": 6}

# 分 P 里混进来的说明性内容（不是任务）。这里只放「整条都不是任务」的，
# 别写成「含成就就跳过」——那会把一堆真任务一起误杀。
SKIP_PART_RE = re.compile(
    r"(避免宝箱和任务重复|以防你不知道|无任务跑法|视频录制失败|成就要点$)"
)

# 结尾的宝箱/成就在括号里，属于副信息，剔掉
TAIL_PAREN_RE = re.compile(
    r"\s*[（(][^（()）]*(宝箱|成就|声匣|日灵|千瓣|潮汐之遗|星声|必开|必拿|错过|前置|解锁|含)[^（()）]*[）)]\s*$"
)

# 「成就：一直游到海水变蓝」这种是尾注不是名字的一部分，整段括号里只有成就名也剥掉
ACH_PAREN_RE = re.compile(r"\s*[（(]\s*成就[：:][^（()）]*[）)]\s*$")

# 标题开头的分 P 序号：p1 / P12 / p5- / ★p3 / ⊙p8 / ★p34
LEAD_NOISE_RE = re.compile(r"^[\s★☆⊙!·•]*[pP]\d+\s*[-－—]?\s*")

# 「1-0」「2-11」「第二章-第八幕」这类章节编号
CHAPTER_NO_RE = re.compile(
    r"^(第[一二三四五六七八九十]+章[\s.·、-]*)?(第[一二三四五六七八九十]+幕[\s.·、-]*)?"
    r"(\d+[-－]\d+[\s.·、-]*)?(序章[\s.·、-]*)?"
)

# 名字里允许保留的间隔符，统一成这个
DASH = " · "

# 版本 -> 地区短码（UP 是按版本组织的，这份映射就是本次的地区依据）
VER_REGION = {
    "1.0": "jz", "1.1": "csx", "1.2": "csx", "1.3": "hha", "1.4": "hha",
    "2.0": "lnxt", "2.1": "lnxt", "2.2": "avnl", "2.3": "lnxt", "2.4": "qq",
    "2.5": "lnxt", "2.6": "sgys", "2.7": "lnxt", "2.8": "hha",
    "3.0": "lhl", "3.1": "lyby", "3.2": "lyby", "3.3": "lyby",
    "3.4": "yzc", "3.5": "mz", "3.6": "mz", "3.7": "mz", "3.8": "mz",
}

# 名字里出现这些地名就直接判到那个区域（比版本优先）
NAME_REGION = [
    ("乘霄山", "csx"), ("瑝珑", "jz"), ("今州", "jz"), ("北落野", "bly"),
    ("荒石高地", "hsgd"), ("无光之森", "wgs"), ("虎口山脉", "hks"),
    ("怨鸟泽", "ynz"), ("云陵谷", "ylg"), ("中曲台地", "zqtd"),
    ("归墟港市", "gxgs"), ("黑海岸", "hha"), ("黎那汐塔", "lnxt"),
    ("七丘", "qq"), ("桑古伊斯狩原", "sgys"), ("拉海洛", "lhl"),
    ("罗伊冰原", "lyby"), ("夜之城", "yzc"), ("梦州", "mz"),
    ("阿维纽林", "avnl"), ("隐海试验场", "lnxt"), ("三王峰", "lnxt"),
    ("四方殿", "lnxt"), ("穗波市", "hha"),
]


# 结尾「：4个宝箱」「: 3成就」这种冒号后缀
TAIL_COLON_RE = re.compile(r"\s*[：:]\s*\d*\s*个?\s*(宝箱|成就|声匣|日灵|千瓣)[^\n]*$")

# 「（一）/（二）」这种分集写法，先转成「·一/二」，否则后面剥括号会把后半个吃掉
PAREN_SPLIT_RE = re.compile(r"[（(]([一二三四五六七八九十上中下前后尾])[）)]\s*/\s*[（(]([一二三四五六七八九十上中下前后尾])[）)]")


def strip_parens(name):
    """反复剥掉结尾的括号说明（宝箱数、成就名、前置条件都在这儿）。"""
    name = PAREN_SPLIT_RE.sub(r"·\1/\2", name)
    for _ in range(4):
        new = ACH_PAREN_RE.sub("", name).strip()
        new = TAIL_PAREN_RE.sub("", new).strip()
        new = TAIL_COLON_RE.sub("", new).strip()
        if new == name:
            break
        name = new
    # 纯括号尾巴：前面紧挨着 / 的时候别剥（那是「（一）/（二）」的残留）
    name = re.sub(r"(?<!/)\s*[（(][^（()）]*[）)]\s*$", "", name).strip()
    return name


# 名字中间的括号说明（幽夜幻梦（9个宝箱）坎特蕾拉）——只剥带这些词的，
# 别把「（一/二）」那种真名字的一部分剥掉。
MID_PAREN_RE = re.compile(
    r"[（(][^（()）]*(宝箱|成就|声匣|日灵|千瓣|必开|必拿|错过|前置|解锁|含)[^（()）]*[）)]"
)


def clean_title(raw):
    """把一条分 P 标题拆成 (type, 任务名)，不是任务的返回 (None, None)。"""
    s = (raw or "").strip()
    if not s or SKIP_PART_RE.search(s):
        return None, None

    m = re.search(r"【([^】]+)】", s)
    if not m:
        # 少数潮汐任务在分 P 标题里没写类型标签，只有「第二章 第九幕 xxx」这种章节号
        if re.search(r"第[一二三四五六七八九十]+章", s):
            name = strip_parens(s)
            name = CHAPTER_NO_RE.sub("", name).strip()
            name = re.sub(r"^[：:、.·\s-]+", "", name)
            name = finalize_name(name)
            if len(name) >= 2:
                return "main", name
        return None, None
    tag = m.group(1).strip()
    if tag in SKIP_TAGS:
        return None, None
    typ = TAG_MAP.get(tag)
    if not typ:
        return None, None

    # 类型标签前后都丢掉，只留名字
    name = s[:m.start()] + " " + s[m.end():]
    name = LEAD_NOISE_RE.sub("", name).strip()
    name = re.sub(r"^[-－—\s]+", "", name)

    name = strip_parens(name)
    name = MID_PAREN_RE.sub("", name).strip()

    # 章节编号
    name = CHAPTER_NO_RE.sub("", name).strip()
    name = re.sub(r"^[：:、.·\s-]+", "", name)

    name = finalize_name(name)
    if len(name) < 2:
        return None, None
    return typ, name


# 「-上-下」「-上/下」这种分集写法，统一成「·上/下」
SPLIT_DASH_RE = re.compile(r"\s*[-—－_]+\s*([上下中后尾序])")
# 用点当层级分隔（过去即是明天.前/后、银发声骸训练师.二、巡礼之梦.其一）
DOT_SEP_RE = re.compile(r"(?<=[\u4e00-\u9fff])\s*[.．]\s*(?=[\u4e00-\u9fff0-9])")
# 双横线当层级分隔（古今多少故事--云陵谷）
DASH_SEP_RE = re.compile(r"(?<=[\u4e00-\u9fff])\s*[-—－]{2,}\s*(?=[\u4e00-\u9fff])")


def finalize_name(s):
    """把名字里各种写法的分隔符统一成「·」，方便跨版本比对同名。"""
    s = s.strip()
    # 先收双横线（古今多少故事--云陵谷）。顺序不能反：反了的话
    # 「--中曲台地」里的「中」会被下面按分集拆，变成「·中/曲台地」。
    s = DASH_SEP_RE.sub("·", s)
    # 万象新声-上-下 -> 万象新声·上/下
    parts = SPLIT_DASH_RE.split(s)
    if len(parts) > 1:
        head = parts[0].strip()
        tails = [p for p in parts[1:] if p]
        s = head + "·" + "/".join(tails)
    s = DOT_SEP_RE.sub("·", s)
    # 「.上下后日谈」-> 「·上/下后日谈」
    s = re.sub(r"·上下", "·上/下", s)
    # 收掉多余空格
    s = re.sub(r"\s+", " ", s).strip()
    # · 两侧不留空格
    s = re.sub(r"\s*·\s*", "·", s)
    return s


def region_of(version, name):
    for kw, rid in NAME_REGION:
        if kw in name:
            return rid, "高"
    return VER_REGION.get(version, "jz"), "中"


def load(src_json):
    data = json.load(io.open(src_json, encoding="utf-8"))
    out = []
    for v in data:
        ver = v["version"]
        for p in v["parts"]:
            typ, name = clean_title(p.get("title"))
            if not typ:
                continue
            rid, conf = region_of(ver, name)
            out.append({
                "version": ver,
                "type": typ,
                "name": name,
                "raw": p.get("title"),
                "region": rid,
                "conf": conf,
                "bvid": p.get("bvid") or v["bvid"],
                "page": p.get("page"),
                "videoTitle": v["title"],
            })
    return out


if __name__ == "__main__":
    import collections
    import sys
    items = load(r"C:\Users\ASUS\AppData\Local\Temp\quest_src.json")
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    print("解析出 %d 条任务" % len(items))
    c = collections.Counter(x["type"] for x in items)
    print("类型:", dict(c))
    v = collections.Counter(x["version"] for x in items)
    print("版本:", dict(sorted(v.items())))
    r = collections.Counter(x["region"] for x in items)
    print("地区:", dict(r))
    print("\n--- 抽查 1.0 ---")
    for x in items:
        if x["version"] == "1.0":
            print("  %-6s %-28s | %s" % (x["type"], x["name"], x["raw"]))
