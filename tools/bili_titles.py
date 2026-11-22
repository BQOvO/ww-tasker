# -*- coding: utf-8 -*-
"""把 B 站合集 / 系列 / 多 P 视频里所有「小标题」拉成一份清单。

用法：
    python bili_titles.py <BV号或视频链接> [更多...]     # 自动判断类型
    python bili_titles.py 合集:515745520/6954893         # mid/season_id
    python bili_titles.py 系列:515745520/12345           # mid/series_id

给一个普通视频链接时，它会：
  1) 这个视频属于某个合集 -> 把整个合集的所有视频标题都拉下来（标题一般就是任务名）
  2) 这个视频有多个分 P -> 把每个分 P 的 part 名都拉下来（一条龙攻略常这么干）
  3) 以上都没有 -> 就只有它自己一条标题

输出：
    --json out.json   结构化结果（utf-8，不转义中文）
    --txt  out.txt    每行一条标题，方便直接看

只用标准库，不需要登录、不需要 cookie。
"""

import argparse
import io
import json
import re
import sys
import time
import urllib.parse
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/120.0 Safari/537.36")
HDR = {"User-Agent": UA, "Referer": "https://www.bilibili.com/"}
API = "https://api.bilibili.com"
FLAT = {"flat": True, "ensure_ascii": False}


def get(path, params=None, tries=3):
    """GET 一个 b 站接口，返回 data。失败重试，-412 时多等一会儿。"""
    url = path if path.startswith("http") else API + path
    if params:
        url += "?" + urllib.parse.urlencode(params)
    last = None
    for i in range(tries):
        try:
            req = urllib.request.Request(url, headers=HDR)
            with urllib.request.urlopen(req, timeout=25) as r:
                j = json.loads(r.read().decode("utf-8"))
            if j.get("code") == 0:
                return j["data"]
            last = "code=%s %s" % (j.get("code"), j.get("message"))
            if j.get("code") == -412:          # 被风控挡了一下
                time.sleep(2.0 + i * 2)
                continue
            if j.get("code") == -799:          # 参数错 / 太频繁
                time.sleep(1.5)
                continue
            break
        except Exception as e:                 # 网络抖动
            last = str(e)
            time.sleep(1.0 + i)
    raise RuntimeError(last or "未知错误")


def norm_bvid(s):
    """从一个链接或裸串里抠出 BV 号。"""
    m = re.search(r"(BV[0-9A-Za-z]{10})", s)
    if m:
        return m.group(1)
    raise ValueError("这不是 BV 号也不是视频链接：" + s)


def digits(pair):
    """'515745520/6954893' -> (515745520, 6954893)"""
    a, b = re.split(r"[/,\s]+", pair.strip())[:2]
    return int(a), int(b)


# ---------------------------------------------------------------- 三种来源

def season_items(mid, season_id):
    """合集里的所有稿件。标题一般就是任务名。每页最多 100。"""
    out, page = [], 1
    while True:
        d = get("/x/polymer/web-space/seasons_archives_list", {
            "mid": mid, "season_id": season_id, "page_num": page,
            "page_size": 100, "sort_reverse": "false"})
        arch = d.get("archives") or []
        if not arch:
            break
        for a in arch:
            out.append({"kind": "season", "bvid": a["bvid"], "title": a["title"],
                        "duration": a.get("duration"), "pubdate": a.get("pubdate")})
        total = (d.get("meta") or {}).get("total") or 0
        if len(out) >= total:
            break
        page += 1
        time.sleep(0.35)
    return out


def series_items(mid, series_id):
    """系列里的所有稿件（合集是季节性的，系列是随手归的）。"""
    out, page = [], 1
    while True:
        d = get("/x/series/archives", {"mid": mid, "series_id": series_id,
                                       "page_num": page, "page_size": 30})
        arch = (d.get("archives") or [])
        if not arch:
            break
        for a in arch:
            out.append({"kind": "series", "bvid": a["bvid"], "title": a["title"],
                        "duration": a.get("duration"), "pubdate": a.get("pubdate")})
        total = d.get("page", {}).get("total") if isinstance(d.get("page"), dict) else 0
        if total and len(out) >= total:
            break
        if len(arch) < 30:
            break
        page += 1
        time.sleep(0.35)
    return out


def part_items(bvid):
    """分 P 的 part 名。一条龙攻略里每个 P 常常就是一个任务。"""
    d = get("/x/player/pagelist", {"bvid": bvid})
    if not isinstance(d, list) or len(d) <= 1:
        return []
    out = []
    for p in d:
        part = (p.get("part") or "").strip()
        # 有些 UP 没给分 P 起名，B 站会留成 studio_video_xxx.mp4 这种，跳掉
        if not part or part.endswith(".mp4"):
            continue
        out.append({"kind": "part", "bvid": bvid, "page": p.get("page"),
                    "title": part, "duration": p.get("duration")})
    return out


def video_info(bvid):
    return get("/x/web-interface/view", {"bvid": bvid})


def all_seasons_of(mid):
    """某个 UP 名下所有合集 / 系列，方便你挑要哪个。"""
    # 注意：这个接口 page_size 超过 20 会回 code=-400 请求错误
    d = get("/x/polymer/web-space/seasons_series_list",
            {"mid": mid, "page_num": 1, "page_size": 20})
    lists = d.get("items_lists") or {}
    res = []
    for it in lists.get("seasons_list") or []:
        m = it["meta"]
        res.append({"kind": "season", "id": m["season_id"], "mid": mid,
                    "name": m["name"], "total": m.get("total")})
    for it in lists.get("series_list") or []:
        m = it["meta"]
        res.append({"kind": "series", "id": m["series_id"], "mid": mid,
                    "name": m["name"], "total": m.get("total")})
    return res


# ------------------------------------------------------------------ 主流程

def collect(args):
    sources = []
    for a in args:
        a = a.strip()
        if a.startswith("合集:"):
            mid, sid = digits(a[3:])
            items = season_items(mid, sid)
            sources.append({"kind": "season", "mid": mid, "id": sid,
                            "title": "", "items": items})
        elif a.startswith("系列:"):
            mid, sid = digits(a[3:])
            items = series_items(mid, sid)
            sources.append({"kind": "series", "mid": mid, "id": sid,
                            "title": "", "items": items})
        elif a.startswith("up:"):
            mid = int(a[3:].strip())
            sources.append({"kind": "uplist", "mid": mid, "id": 0,
                            "title": "UP 的合集目录", "items": all_seasons_of(mid)})
        else:
            bvid = norm_bvid(a)
            info = video_info(bvid)
            season = info.get("ugc_season")
            parts = part_items(bvid)
            if season:
                mid = info["owner"]["mid"]
                sid = season["id"]
                items = season_items(mid, sid)
                sources.append({"kind": "season", "mid": mid, "id": sid,
                                "title": season.get("title", ""),
                                "count": season.get("ep_count"), "items": items})
            if parts:
                sources.append({"kind": "parts", "mid": info["owner"]["mid"],
                                "id": 0, "title": info["title"], "items": parts})
            if not season and not parts:
                sources.append({"kind": "single", "mid": info["owner"]["mid"],
                                "id": 0, "title": info.get("title", ""),
                                "items": [{"kind": "single", "bvid": bvid,
                                           "title": info["title"],
                                           "duration": info.get("duration"),
                                           "pubdate": info.get("pubdate")}]})
    return sources


def dedupe(sources):
    """同一个 bvid+page 只留一条，但保持第一次出现的顺序。"""
    seen, n = set(), 0
    for s in sources:
        keep = []
        for it in s["items"]:
            # 合集目录那种条目没有 bvid，得用 kind+id+名字做键，否则全被当成同一条
            key = (it.get("bvid"), it.get("page"),
                   it.get("id"), it.get("title") or it.get("name"))
            if key in seen:
                continue
            seen.add(key)
            keep.append(it)
        n += len(keep)
        s["items"] = keep
    return n


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("sources", nargs="+", help="BV号 / 视频链接 / 合集:mid/sid / 系列:mid/sid / up:mid")
    ap.add_argument("--json", dest="json_out", default="", help="结构化结果写到这里")
    ap.add_argument("--txt", dest="txt_out", default="", help="纯标题清单写到这里")
    ap.add_argument("--quiet", action="store_true", help="不往屏幕打印标题")
    ns = ap.parse_args()

    sources = collect(ns.sources)
    total = dedupe(sources)

    if ns.json_out:
        with io.open(ns.json_out, "w", encoding="utf-8") as f:
            json.dump({"sources": sources}, f, ensure_ascii=False, indent=1)

    lines = []
    for s in sources:
        head = s.get("title") or ("%s %s" % (s["kind"], s["id"]))
        lines.append("## " + head + "  （%d 条）" % len(s["items"]))
        for i, it in enumerate(s["items"], 1):
            tag = ""
            if it.get("page"):
                tag = "P%d " % it["page"]
            # 合集目录（up: 模式）里字段叫 name 不叫 title
            label = it.get("title") or it.get("name") or ""
            if it.get("kind") in ("season", "series") and it.get("total"):
                label += "  （%d）" % it["total"]
            tail = "  [%s]" % it["bvid"] if it.get("bvid") else ""
            lines.append("%3d. %s%s%s" % (i, tag, label, tail))
        lines.append("")
    text = "\n".join(lines)

    if ns.txt_out:
        with io.open(ns.txt_out, "w", encoding="utf-8") as f:
            f.write(text)
    if not ns.quiet:
        print(text)
    print("合计 %d 条标题" % total, file=sys.stderr)


if __name__ == "__main__":
    main()
