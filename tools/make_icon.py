"""Генерує фірмовий значок (src/Varta/Assets/varta.ico) і docs/logo.png.
Запуск: uv run --with pillow python tools/make_icon.py
Геометрія збігається з BrandArt.Logo() у src/Varta/Ui/Theme.cs.
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024
ROOT = Path(__file__).resolve().parent.parent


def bez(p0, p1, p2, p3, n=48):
    pts = []
    for i in range(n + 1):
        t = i / n
        x = (1 - t) ** 3 * p0[0] + 3 * (1 - t) ** 2 * t * p1[0] + 3 * (1 - t) * t ** 2 * p2[0] + t ** 3 * p3[0]
        y = (1 - t) ** 3 * p0[1] + 3 * (1 - t) ** 2 * t * p1[1] + 3 * (1 - t) * t ** 2 * p2[1] + t ** 3 * p3[1]
        pts.append((x, y))
    return pts


def shield(x, y, w, h):
    pts = []
    pts += bez((x + w * .5, y), (x + w * .62, y + h * .10), (x + w * .82, y + h * .14), (x + w, y + h * .14))
    pts += [(x + w, y + h * .50)]
    pts += bez((x + w, y + h * .50), (x + w, y + h * .78), (x + w * .72, y + h * .92), (x + w * .5, y + h))
    pts += bez((x + w * .5, y + h), (x + w * .28, y + h * .92), (x, y + h * .78), (x, y + h * .50))
    pts += [(x, y + h * .14)]
    pts += bez((x, y + h * .14), (x + w * .18, y + h * .14), (x + w * .38, y + h * .10), (x + w * .5, y))
    return pts


def render(size):
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    x, y, w, h = S * .08, S * .03, S * .84, S * .94
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).polygon(shield(x, y, w, h), fill=255)
    grad = Image.new('RGBA', (S, S))
    gd = ImageDraw.Draw(grad)
    top, bot = (47, 111, 222), (31, 63, 112)
    for row in range(S):
        t = min(1, max(0, (row - y) / h))
        gd.line([(0, row), (S, row)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,))
    img.paste(grad, (0, 0), mask)
    d = ImageDraw.Draw(img)
    gold = (242, 183, 5, 255)
    white = (255, 255, 255, 255)

    inset = S * .035
    pts = shield(x + inset, y + inset, w - 2 * inset, h - 2 * inset)
    d.line(pts + [pts[0]], fill=gold, width=int(S * .025), joint='curve')

    cx = x + w * .5
    # купол + основа
    d.pieslice([cx - w * .17, y + h * .66 - h * .24, cx + w * .17, y + h * .66 + h * .24], 180, 360, fill=white)
    d.rectangle([cx - w * .24, y + h * .66, cx + w * .24, y + h * .66 + h * .085], fill=white)
    # промені
    cy = y + h * .58
    pw = w * .07
    for deg in (-90, -145, -35):
        a = math.radians(deg)
        p1 = (cx + math.cos(a) * w * .31, cy + math.sin(a) * w * .31)
        p2 = (cx + math.cos(a) * w * .43, cy + math.sin(a) * w * .43)
        d.line([p1, p2], fill=gold, width=int(pw))
        for p in (p1, p2):
            d.ellipse([p[0] - pw / 2, p[1] - pw / 2, p[0] + pw / 2, p[1] + pw / 2], fill=gold)
    return img.resize((size, size), Image.LANCZOS)


if __name__ == '__main__':
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [render(s) for s in sizes]
    ico = ROOT / 'src' / 'Varta' / 'Assets' / 'varta.ico'
    imgs[-1].save(ico, format='ICO', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    (ROOT / 'docs').mkdir(exist_ok=True)
    render(512).save(ROOT / 'docs' / 'logo.png')
    print('written', ico)
