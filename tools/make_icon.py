# -*- coding: utf-8 -*-
"""从 assets/logo.png 生成多尺寸 app.ico。

用法：
    python tools/make_icon.py

源图只要正方形、浅色底随便什么颜色都行。这个脚本会把底做成
圆角奶油底板再缩尺寸——不是抠图。原因是源图只有 195px、线条又细，
抠成透明之后在 16/24px 上整个人会散架，任务栏上一坨白的看不出是啥。
底板是实色的，放深色任务栏、浅色资源管理器、Alt-Tab 里都认得出。
"""

import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    sys.exit("需要 Pillow：C:\\Python314\\python.exe -m pip install pillow")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "logo.png")
# ico 放在 src 下，csproj 的 ApplicationIcon 和 Resource 都指它
DST = os.path.join(ROOT, "src", "app.ico")

# ico 里塞这些尺寸。16/24 是任务栏与列表小图标，256 是资源管理器大图。
SIZES = [16, 24, 32, 48, 64, 128, 256]
MASTER = 1024          # 先放大到这么大再往下缩，圆角边缘才不毛
RADIUS_FRAC = 0.22     # 圆角半径占边长的比例
PAD_FRAC = 0.04        # 四周留一点奶油边，别让耳朵被圆角切掉


def build_master(path):
    src = Image.open(path).convert("RGB")
    side = min(src.size)
    if src.size[0] != src.size[1]:
        left = (src.size[0] - side) // 2
        top = (src.size[1] - side) // 2
        src = src.crop((left, top, left + side, top + side))

    # 底板取四角均值，这样圆角接缝看不出来
    px = src.load()
    corners = [px[0, 0], px[side - 1, 0], px[0, side - 1], px[side - 1, side - 1]]
    bg = tuple(sum(c[i] for c in corners) // len(corners) for i in range(3))

    canvas = Image.new("RGBA", (MASTER, MASTER), bg + (255,))
    pad = int(MASTER * PAD_FRAC)
    art = src.resize((MASTER - pad * 2, MASTER - pad * 2), Image.LANCZOS)
    canvas.paste(art.convert("RGBA"), (pad, pad))

    radius = int(MASTER * RADIUS_FRAC)
    mask = Image.new("L", (MASTER, MASTER), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, MASTER - 1, MASTER - 1], radius=radius, fill=255
    )
    out = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    out.paste(canvas, (0, 0), mask)

    # 一圈很淡的暗边，浅色背景上不会糊成一片
    edge = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle(
        [0, 0, MASTER - 1, MASTER - 1],
        radius=radius,
        outline=(58, 52, 46, 96),
        width=max(1, MASTER // 170),
    )
    return Image.alpha_composite(out, edge)


def shrink(master, size):
    if size >= MASTER:
        return master.resize((size, size), Image.LANCZOS)
    im = master.resize((size, size), Image.LANCZOS)
    # 小尺寸缩完会发糊，补一点点锐化
    if size <= 64:
        im = im.filter(ImageFilter.UnsharpMask(radius=0.6, percent=55, threshold=2))
    return im


def main():
    if not os.path.exists(SRC):
        sys.exit("找不到源图：" + SRC)
    master = build_master(SRC)
    frames = [shrink(master, s) for s in SIZES]
    os.makedirs(os.path.dirname(DST), exist_ok=True)
    # 第一个放最大的，Windows 挑尺寸是按帧里存的宽高来的，顺序无所谓
    frames[-1].save(DST, format="ICO", sizes=[(s, s) for s in SIZES],
                    append_images=frames[:-1])
    print("写入 " + DST)
    for f in frames:
        print("  %3dpx  %d 字节" % (f.size[0], len(f.tobytes())))

    # 顺手导一份 256 PNG，README 和发布页能直接用
    png = os.path.join(os.path.dirname(DST), "logo-256.png")
    frames[-1].save(png)
    print("写入 " + png)


if __name__ == "__main__":
    main()
