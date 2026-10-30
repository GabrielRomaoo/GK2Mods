"""Builds the Storage Network build icons (32x32, blueprint style like the game's i_b_* icons).

blueprint_blank.png is the game's empty blueprint tile (i_b_blueprint_placeholder) recoloured to the blue
used by the regular building icons. Line colours are the ones the game's blueprints use.

Usage: python make_icons.py [preview_dir]
Writes PNGs to ../../src/StorageNetwork/Icons/ and, optionally, 8x previews to preview_dir.
"""
import math
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

    def gear(self, cx, cy, small=False):
        """Pixel gear like i_b_alchemy_gear: 4 straight + 4 diagonal teeth, shaded rim, dark hub.
        Big: 13x13 (Central Storage). Small: 11x11 (Relay)."""
        body_r, tooth, diag = (3.6, 5, (3, 4)) if small else (5.2, 6, (4, 5))
        hole_r2, ring_r2 = (0, 2) if small else (1, 5)
        inside = set()
        for dy in range(-7, 8):
            for dx in range(-7, 8):
                body = dx * dx + dy * dy <= body_r * body_r
                straight = (abs(dx) <= 1 and abs(dy) <= tooth) or (abs(dy) <= 1 and abs(dx) <= tooth)
                diagonal = (diag[0] <= abs(dx) <= diag[1] and diag[0] <= abs(dy) <= diag[1]
                            and not (abs(dx) == 5 and abs(dy) == 5))
                if body or straight or diagonal:
                    inside.add((dx, dy))
        for dx, dy in inside:
            lit_edge = (dx - 1, dy) not in inside or (dx, dy - 1) not in inside
            dark_edge = (dx + 1, dy) not in inside or (dx, dy + 1) not in inside
            r2 = dx * dx + dy * dy
            if r2 <= hole_r2:
                color = HOLE
            elif r2 <= ring_r2:
                color = DEEP if dx + dy >= 0 else SHADE
            elif dark_edge and not lit_edge:
                color = SHADE
            elif lit_edge and not dark_edge:
                color = WHITE
            else:
                color = LINE
            self.set(cx + dx, cy + dy, color)


    def arc(self, cx, cy, radius, start_deg, end_deg, color=LIGHT):
        """1px arc (screen angles: 0 = right, 90 = down), for signal waves."""
        for y in range(int(cy - radius) - 1, int(cy + radius) + 2):
            for x in range(int(cx - radius) - 1, int(cx + radius) + 2):
                r = math.hypot(x - cx, y - cy)
                a = math.degrees(math.atan2(y - cy, x - cx)) % 360
                if abs(r - radius) <= 0.5 and start_deg <= a <= end_deg:
                    self.set(x, y, color)

    def block(self, x0, y0, x1, y1):
        """Filled box with the game's top-left light: white lit edges, shaded far edges."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if x == x0 or y == y0:
                    color = WHITE
                elif x == x1 or y == y1:
                    color = SHADE
                else:
                    color = LINE
                self.set(x, y, color)


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


def zone_relay():
    """Small gear passing the network on (nodes left and right) and broadcasting (waves above)."""
    c = Canvas()
    cx, cy = 16, 20
    nodes = [(6, cy), (26, cy)]

    def in_node(x, y):
        return any(abs(x - nx) <= 2 and abs(y - ny) <= 2 for nx, ny in nodes)

    for nx, ny in nodes:
        c.line(cx, cy, nx, ny, LIGHT, skip=in_node)
    c.arc(cx, cy, 7.5, 225, 315, LIGHT)
    c.arc(cx, cy, 11.0, 230, 310, WHITE)
    c.gear(cx, cy, small=True)
    for nx, ny in nodes:
        c.node(nx, ny)
    return c.img


def connector():
    """A plug on a cable that ends in a network node: what links a workbench to the network."""
    c = Canvas()
    node, socket = (7, 25), (25, 16)

    def in_node(x, y):
        return any(abs(x - nx) <= 2 and abs(y - ny) <= 2 for nx, ny in (node, socket))

    # cable: from the plug's base, down and left into a network node
    c.line(12, 21, 12, 23, LIGHT)
    c.line(12, 23, node[0], node[1], LIGHT, skip=in_node)
    # plug body with a slot, and two prongs reaching the socket node
    c.block(9, 12, 16, 20)
    for y in (14, 18):
        c.line(17, y, 22, y, WHITE)
        c.set(17, y + 1, SHADE)
    c.line(12, 15, 12, 17, HOLE)
    c.line(13, 15, 13, 17, DEEP)
    c.node(*socket)
    c.node(*node)
    return c.img


ICONS = {
    "gk2sn_icon_central_storage": central_storage,
    "gk2sn_icon_zone_relay": zone_relay,
    "gk2sn_icon_connector": connector,
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
