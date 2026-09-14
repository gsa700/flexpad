#!/usr/bin/env python3
"""Generate assets/flexpad.ico and assets/flexpad.png.

The icon is drawn in code rather than shipped as an opaque binary so anyone can
see exactly what it is and regenerate it. Motif: a dark rounded plate with a
2x2 grid of colored buttons, one of them lit - a button panel, mid-press.

Requires Pillow (dev-time only; the outputs are committed so users never need
this).
"""

import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "assets")

# Draw huge, downscale for anti-aliasing.
S = 1024
BG = (16, 20, 24, 255)         # the log pane's near-black
BUTTONS = [                    # the editor palette, left to right, top to bottom
    (45, 106, 79, 255),        # green
    (180, 83, 9, 255),         # amber
    (90, 24, 154, 255),        # purple
    (29, 53, 87, 255),         # navy
]
LIT = 1                        # index of the pressed button
GLOW = (255, 214, 102, 255)    # warm highlight ring on the pressed one
DOT = (240, 240, 240, 255)


def draw_master():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded-square plate. Radius tuned so the silhouette still reads square
    # at 16 px instead of collapsing into a circle.
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=S // 5, fill=BG)

    # 2x2 buttons with a gutter. Generous size so they survive 16 px.
    margin = S * 0.13
    gap = S * 0.07
    side = (S - 2 * margin - gap) / 2
    radius = side * 0.18
    for i, color in enumerate(BUTTONS):
        col, row = i % 2, i // 2
        x0 = margin + col * (side + gap)
        y0 = margin + row * (side + gap)
        box = (x0, y0, x0 + side, y0 + side)
        if i == LIT:
            # Highlight ring drawn first, slightly larger, then the face on top.
            ring = S * 0.035
            d.rounded_rectangle((x0 - ring, y0 - ring, x0 + side + ring, y0 + side + ring),
                                radius=radius + ring, fill=GLOW)
        d.rounded_rectangle(box, radius=radius, fill=color)
        if i == LIT:
            # A pale dot: the indicator lamp on the pressed button.
            cx, cy, r = x0 + side / 2, y0 + side / 2, side * 0.13
            d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=DOT)
    return img


def main():
    os.makedirs(ASSETS, exist_ok=True)
    master = draw_master()
    ico = os.path.join(ASSETS, "flexpad.ico")
    png = os.path.join(ASSETS, "flexpad.png")
    sizes = [16, 24, 32, 48, 64, 128, 256]
    master.save(ico, format="ICO", sizes=[(s, s) for s in sizes])
    master.resize((256, 256), Image.LANCZOS).save(png, format="PNG")
    print(f"wrote {os.path.normpath(ico)} ({os.path.getsize(ico)} bytes, sizes {sizes})")
    print(f"wrote {os.path.normpath(png)} ({os.path.getsize(png)} bytes)")


if __name__ == "__main__":
    main()
