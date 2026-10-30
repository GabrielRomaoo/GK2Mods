"""Builds the Storage Network build icons (32x32, blueprint style like the game's i_b_* icons).

blueprint_blank.png is the game's empty blueprint tile (i_b_blueprint_placeholder) recoloured to the blue
used by the regular building icons. Line colours are the ones the game's blueprints use.

Usage: python make_icons.py [preview_dir]
Writes PNGs to ../../src/StorageNetwork/Icons/ and, optionally, 8x previews to preview_dir.
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "src", "StorageNetwork", "Icons")

WHITE = (247, 250, 255, 255)
LIGHT = (198, 210, 233, 255)
LINE = (164, 181, 214, 255)
SHADE = (140, 163, 202, 255)
DEEP = (118, 145, 189, 255)
HOLE = (43, 80, 134, 255)


class Canvas:
    def __init__(self):
        self.img = Image.open(os.path.join(HERE, "blueprint_blank.png")).convert("RGBA")
        self.px = self.img.load()

    def set(self, x, y, color):
        if 2 <= x <= 29 and 2 <= y <= 29:  # keep inside the tile frame
            self.px[x, y] = color

    def line(self, x0, y0, x1, y1, color=LINE, skip=None):
        """Bresenham, 1px like the game's blueprint strokes. skip(x, y) -> True leaves a pixel untouched."""
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            if not (skip and skip(x0, y0)):
                self.set(x0, y0, color)
            if x0 == x1 and y0 == y1:
                return
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def node(self, cx, cy):
        """Open 5x5 ring centred on a pixel, lit from the top-left like the game's icons."""
        ring = [(-1, -2), (0, -2), (1, -2), (-2, -1), (-2, 0), (-2, 1), (2, -1), (2, 0), (2, 1), (-1, 2), (0, 2), (1, 2)]
        for dx, dy in ring:
            self.set(cx + dx, cy + dy, WHITE if dx + dy < 0 or (dx + dy == 0 and dx < 0) else LIGHT)

    def gear(self, cx, cy):
        """15x15 pixel gear like i_b_alchemy_gear: 4 straight + 4 diagonal teeth, shaded rim, dark hub."""
        inside = set()
        for dy in range(-7, 8):
            for dx in range(-7, 8):
                body = dx * dx + dy * dy <= 5.2 * 5.2
                straight = (abs(dx) <= 1 and abs(dy) <= 6) or (abs(dy) <= 1 and abs(dx) <= 6)
                diagonal = 4 <= abs(dx) <= 5 and 4 <= abs(dy) <= 5 and not (abs(dx) == 5 and abs(dy) == 5)
                if body or straight or diagonal:
                    inside.add((dx, dy))
        for dx, dy in inside:
            lit_edge = (dx - 1, dy) not in inside or (dx, dy - 1) not in inside
            dark_edge = (dx + 1, dy) not in inside or (dx, dy + 1) not in inside
            r2 = dx * dx + dy * dy
            if r2 <= 1:
                color = HOLE
            elif r2 <= 5:
                color = DEEP if dx + dy >= 0 else SHADE
            elif dark_edge and not lit_edge:
                color = SHADE
            elif lit_edge and not dark_edge:
                color = WHITE
            else:
                color = LINE
            self.set(cx + dx, cy + dy, color)


def central_storage():
    """Gear hub with six branches ending in nodes (the network)."""
    c = Canvas()
    nodes = [(16, 5), (16, 27), (7, 8), (25, 8), (7, 24), (25, 24)]
    def in_node(x, y):
        return any(abs(x - nx) <= 2 and abs(y - ny) <= 2 for nx, ny in nodes)

    for nx, ny in nodes:
        c.line(16, 16, nx, ny, LIGHT, skip=in_node)
    c.gear(16, 16)
    for nx, ny in nodes:
        c.node(nx, ny)
    return c.img


ICONS = {
    "gk2sn_icon_central_storage": central_storage,
}


def main():
    os.makedirs(OUT, exist_ok=True)
    preview = sys.argv[1] if len(sys.argv) > 1 else None
    for name, build in ICONS.items():
        img = build()
        img.save(os.path.join(OUT, name + ".png"))
        if preview:
            os.makedirs(preview, exist_ok=True)
            img.resize((256, 256), Image.NEAREST).save(os.path.join(preview, name + "_x8.png"))
        print("wrote", name)


if __name__ == "__main__":
    main()
