"""
Render Ardel mark — pure Pillow geometry (not AI).

C3f: solid flat-top hexagon with two concentric open rings,
openings on opposite sides (sparse gap between rings).
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

OUT_DIR = Path(__file__).resolve().parent

HEX_R = 48.0
# Outer open ring (BR facet open)
OUTER_IR = 32.0
OUTER_STROKE = 7.0
OUTER_PAD = 0.12
# Inner open ring (opposite: TL facet open)
INNER_IR = 12.0
INNER_STROKE = 6.0
INNER_PAD = 0.12


def _hex_pts(cx: float, cy: float, r: float) -> list[tuple[float, float]]:
    return [
        (cx + r * math.cos(math.radians(60 * i)), cy + r * math.sin(math.radians(60 * i)))
        for i in range(6)
    ]


def _open_ring(
    cx: float,
    cy: float,
    ir: float,
    stroke: float,
    size: int,
    open_verts: tuple[int, int],
    pad: float,
) -> Image.Image:
    outer = _hex_pts(cx, cy, ir + stroke / 2)
    ring = Image.new("L", (size, size), 0)
    rd = ImageDraw.Draw(ring)
    rd.polygon(outer, fill=255)
    inner_r = ir - stroke / 2
    if inner_r > 1:
        rd.polygon(_hex_pts(cx, cy, inner_r), fill=0)

    i0, i1 = open_verts
    a0 = math.atan2(outer[i0][1] - cy, outer[i0][0] - cx) - pad
    a1 = math.atan2(outer[i1][1] - cy, outer[i1][0] - cx) + pad
    diff = a1 - a0
    while diff > math.pi:
        diff -= 2 * math.pi
    while diff < -math.pi:
        diff += 2 * math.pi

    fan = [(cx, cy)]
    for i in range(20):
        t = i / 19
        ang = a0 + diff * t
        fan.append(
            (
                cx + math.cos(ang) * (ir + stroke * 3.2),
                cy + math.sin(ang) * (ir + stroke * 3.2),
            )
        )
    gap = Image.new("L", (size, size), 0)
    ImageDraw.Draw(gap).polygon(fan, fill=255)
    return ImageChops.subtract(ring, gap)


def draw_logo(size: int, *, ink: tuple[int, int, int, int] = (255, 255, 255, 255)) -> Image.Image:
    ss = 4
    big = size * ss
    scale = big / 128.0
    cx = cy = 64 * scale
    r = HEX_R * scale

    pts = _hex_pts(cx, cy, r)
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(img).polygon(pts, fill=ink)

    outer_cut = _open_ring(
        cx, cy, OUTER_IR * scale, OUTER_STROKE * scale, big, (0, 1), OUTER_PAD
    )
    inner_cut = _open_ring(
        cx, cy, INNER_IR * scale, INNER_STROKE * scale, big, (3, 4), INNER_PAD
    )
    cut = ImageChops.lighter(outer_cut, inner_cut)

    hex_mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(hex_mask).polygon(pts, fill=255)
    cut = ImageChops.multiply(hex_mask, cut)

    r_ch, g_ch, b_ch, a_ch = img.split()
    img = Image.merge("RGBA", (r_ch, g_ch, b_ch, ImageChops.subtract(a_ch, cut)))
    return img.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    draw_logo(512).save(OUT_DIR / "ardel-logo.png", "PNG")
    draw_logo(512, ink=(28, 32, 36, 255)).save(OUT_DIR / "ardel-logo-ink.png", "PNG")
    print("wrote ardel-logo.png / ardel-logo-ink.png")


if __name__ == "__main__":
    main()
