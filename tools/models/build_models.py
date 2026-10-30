"""Storage Network world models, in the game's low-poly pixel-art style:
  central_storage  an archivist's console: card-catalog drawers, a sloped control desk and a leaning panel
                   with two round blue-glass "monitors" showing the network
  zone_relay       a transmission column: iron-bound crate, wooden mast with brass rings, beacon with waves
  connector        a junction box: riveted iron, brass gauge, blue node pane, two brass pipes

The model is built from boxes and a wedge, measured in texels (the game maps 50 texels per world unit,
material _Scale = 50). Every visible face gets its own pixel-art texture, packed into one atlas.

Outputs (consumed by the mod's ModelLoader):
  src/StorageNetwork/Models/<name>.json   quads: 4 corners (world units), 4 UVs (0..1 in the atlas)
  src/StorageNetwork/Models/<name>.png    atlas
and, if a folder is given, a quick orthographic preview at the game's camera pitch (53.13 degrees).

Usage: python build_models.py [preview_dir]
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "src", "StorageNetwork", "Models")
TEXELS_PER_UNIT = 50.0

# --- palette (wood/iron from wb_good_trunk_*, blues from the build icons, brass from the game's gold wood)
OUTLINE = (53, 40, 34, 255)
WOOD_DEEP = (70, 35, 12, 255)
WOOD_DARK = (84, 43, 16, 255)
WOOD = (116, 70, 26, 255)
WOOD_MID = (127, 81, 36, 255)
WOOD_LIGHT = (149, 105, 53, 255)
WOOD_HI = (157, 113, 57, 255)
IRON_DARK = (62, 60, 54, 255)
IRON = (93, 93, 86, 255)
IRON_MID = (113, 123, 121, 255)
IRON_LIGHT = (135, 147, 145, 255)
BRASS_DARK = (122, 84, 30, 255)
BRASS = (176, 132, 63, 255)
BRASS_LIGHT = (214, 176, 96, 255)
GLASS_DEEP = (37, 71, 120, 255)
GLASS = (55, 100, 171, 255)
GLASS_MID = (88, 124, 175, 255)
GLASS_LIGHT = (164, 181, 214, 255)
GLASS_HI = (247, 250, 255, 255)
PAPER = (214, 196, 150, 255)
RED_KNOB = (150, 52, 36, 255)


# ----------------------------------------------------------------------------------------- painting
class Tex:
    def __init__(self, w, h, fill):
        self.img = Image.new("RGBA", (w, h), fill)
        self.px = self.img.load()
        self.w, self.h = w, h

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[x, y] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, c)

    def frame(self, x0, y0, x1, y1, lit, dark):
        """1px bevel: lit top/left, dark bottom/right."""
        for x in range(x0, x1 + 1):
            self.set(x, y0, lit)
            self.set(x, y1, dark)
        for y in range(y0, y1 + 1):
            self.set(x0, y, lit)
            self.set(x1, y, dark)

    def planks_h(self, x0, y0, x1, y1, height=3):
        """Horizontal wood planks with dark seams and a few grain pixels."""
        for y in range(y0, y1 + 1):
            row = (y - y0) % height
            for x in range(x0, x1 + 1):
                if row == height - 1:
                    c = WOOD_DARK
                elif row == 0:
                    c = WOOD_MID
                else:
                    c = WOOD_LIGHT if (x * 7 + y * 3) % 11 == 0 else WOOD
                self.set(x, y, c)

    def planks_v(self, x0, y0, x1, y1, width=4):
        for x in range(x0, x1 + 1):
            col = (x - x0) % width
            for y in range(y0, y1 + 1):
                if col == width - 1:
                    c = WOOD_DARK
                elif col == 0:
                    c = WOOD_MID
                else:
                    c = WOOD_LIGHT if (x * 5 + y * 3) % 13 == 0 else WOOD
                self.set(x, y, c)

    def disc(self, cx, cy, r, c):
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.set(x, y, c)

    def ring(self, cx, cy, r, c, width=1.0):
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                if r - width < d <= r:
                    self.set(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            self.set(x0, y0, c)
            if x0 == x1 and y0 == y1:
                return
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy


def monitor(t, cx, cy, r):
    """Round blue-glass screen in a brass bezel, showing a hub with three nodes (the network)."""
    t.disc(cx, cy, r + 1.6, OUTLINE)
    t.ring(cx, cy, r + 1.0, BRASS, 1.0)
    t.disc(cx, cy, r, GLASS_DEEP)
    t.disc(cx - 0.6, cy - 0.6, r - 1.2, GLASS)
    t.ring(cx, cy, r - 0.2, GLASS_MID, 1.0)
    hub = (int(cx), int(cy))
    nodes = [(int(cx - r * 0.55), int(cy - r * 0.45)), (int(cx + r * 0.55), int(cy - r * 0.35)), (int(cx), int(cy + r * 0.6))]
    for nx, ny in nodes:
        t.line(hub[0], hub[1], nx, ny, GLASS_LIGHT)
        t.set(nx, ny, GLASS_HI)
    t.rect(hub[0] - 1, hub[1] - 1, hub[0], hub[1], GLASS_HI)
    t.set(int(cx - r * 0.5), int(cy - r * 0.75), GLASS_HI)  # glint


def drawer(t, x0, y0, x1, y1):
    """A card-catalog drawer: bevelled front, paper label, brass pull."""
    t.rect(x0, y0, x1, y1, WOOD)
    t.frame(x0, y0, x1, y1, WOOD_HI, WOOD_DEEP)
    mx = (x0 + x1) // 2
    t.rect(mx - 2, y0 + 1, mx + 1, y0 + 1, PAPER)
    t.rect(mx - 1, y1 - 2, mx, y1 - 2, BRASS_LIGHT)
    t.set(mx - 1, y1 - 1, BRASS_DARK)
    t.set(mx, y1 - 1, BRASS_DARK)


# ----------------------------------------------------------------------------------------- textures
W = 34  # console width in texels (fits the Big Chest footprint, 0.76 units = 38 texels)


def tex_cabinet_front(w, h):
    """Two rows of catalog drawers (three across, two on the narrow variant) between iron corner posts."""
    W = w
    t = Tex(W, h, WOOD_DARK)
    t.rect(0, 0, W - 1, h - 1, WOOD_DEEP)
    n = 3 if W >= 30 else 2
    span = (W - 4) // n
    cols = [(2 + i * span, 2 + (i + 1) * span - 1) for i in range(n)]
    mid = (h - 1) // 2
    for y0, y1 in ((1, mid), (mid + 1, h - 2)):
        for x0, x1 in cols:
            drawer(t, x0, y0, x1, y1)
    for x in (0, 1, W - 2, W - 1):
        for y in range(h):
            t.set(x, y, IRON if x in (0, W - 2) else IRON_DARK)
    for x in (0, W - 2):
        for y in (2, h - 3):
            t.set(x, y, IRON_LIGHT)
    t.rect(0, h - 1, W - 1, h - 1, OUTLINE)
    return t.img


def tex_desk_lip(w, h):
    """The brass stripe along the front edge of the desk (the reference's orange band)."""
    t = Tex(w, max(h, 2), BRASS)
    t.rect(0, 0, w - 1, 0, BRASS_LIGHT)
    t.rect(0, t.h - 1, w - 1, t.h - 1, BRASS_DARK)
    return t.img


def tex_desk_top(w, depth):
    """Sloped control desk: iron rim, a big round dial in the middle, levers left, brass keys right."""
    t = Tex(w, depth, WOOD)
    t.planks_h(0, 0, w - 1, depth - 1, 4)
    t.frame(0, 0, w - 1, depth - 1, IRON_MID, IRON_DARK)
    # central dial
    cx, cy = w / 2, depth / 2
    t.disc(cx, cy, 5.6, OUTLINE)
    t.ring(cx, cy, 5.0, BRASS, 1.0)
    t.disc(cx, cy, 4.0, GLASS)
    t.ring(cx, cy, 4.0, GLASS_MID, 1.0)
    t.line(int(cx), int(cy), int(cx + 2), int(cy - 3), GLASS_HI)
    t.set(int(cx), int(cy), BRASS_LIGHT)
    # levers on the left (three, or two on the narrow variant)
    for i, lx in enumerate((5, 8, 11) if w >= 34 else (4, 7)):
        t.rect(lx, 4, lx, depth - 5, IRON_DARK)
        ky = 5 + (i * 3) % (depth - 10)
        t.rect(lx - 1, ky, lx + 1, ky + 1, RED_KNOB if i == 1 else BRASS)
        t.set(lx - 1, ky, BRASS_LIGHT if i != 1 else (190, 80, 60, 255))
    # brass keys on the right, 3x2 (2x2 on the narrow variant)
    keys = 3 if w >= 34 else 2
    for r in range(2):
        for c in range(keys):
            x = w - 4 - keys * 3 + c * 3
            y = 5 + r * 4
            t.rect(x, y, x + 1, y + 1, BRASS)
            t.set(x, y, BRASS_LIGHT)
            t.set(x + 1, y + 2, OUTLINE)
            t.set(x, y + 2, OUTLINE)
    return t.img


def tex_panel_front(w, h):
    """Back panel: two round monitors with a column of small glass panes between them, brass stripe below."""
    W = w
    t = Tex(W, h, IRON)
    t.planks_v(1, 1, W - 2, h - 2, 5)
    t.frame(0, 0, W - 1, h - 1, IRON_MID, IRON_DARK)
    r = 5.5 if W >= 32 else 4.6
    monitor(t, r + 3, h / 2 - 1, r)
    monitor(t, W - r - 3, h / 2 - 1, r)
    # centre column of two small panes (only where there is room)
    if W >= 32:
        cx = W // 2 - 2
        for py in (h // 2 - 7, h // 2 - 1):
            t.rect(cx, py, cx + 3, py + 4, OUTLINE)
            t.rect(cx + 1, py + 1, cx + 2, py + 3, GLASS)
            t.set(cx + 1, py + 1, GLASS_LIGHT)
    t.rect(1, h - 4, W - 2, h - 4, BRASS_LIGHT)
    t.rect(1, h - 3, W - 2, h - 3, BRASS)
    return t.img


def tex_wood_cap(w, d):
    t = Tex(w, d, WOOD)
    t.planks_h(0, 0, w - 1, d - 1, 3)
    t.frame(0, 0, w - 1, d - 1, WOOD_HI, WOOD_DEEP)
    return t.img


def tex_side(w, h):
    t = Tex(w, h, WOOD)
    t.planks_v(0, 0, w - 1, h - 1, 4)
    t.frame(0, 0, w - 1, h - 1, IRON_MID, IRON_DARK)
    return t.img


def tex_pipe(w, h):
    """Brass pipe running from the panel to the ground (the link out to the zone)."""
    t = Tex(2, h, BRASS)
    for y in range(h):
        t.set(0, y, BRASS_LIGHT)
        t.set(1, y, BRASS_DARK)
    for y in range(3, h, 7):
        t.set(0, y, IRON_LIGHT)
        t.set(1, y, IRON)
    return t.img


def tex_crate_front(w, h):
    """Relay base: horizontal planks, two iron bands, brass lock plate (it is still a chest)."""
    t = Tex(w, h, WOOD)
    t.planks_h(0, 0, w - 1, h - 1, 3)
    for bx in (w // 4, w - w // 4 - 1):
        t.rect(bx - 1, 0, bx, h - 1, IRON)
        t.rect(bx - 1, 0, bx - 1, h - 1, IRON_MID)
        t.set(bx, 1, IRON_LIGHT)
        t.set(bx, h - 3, IRON_LIGHT)
    cx = w // 2
    t.rect(cx - 2, 1, cx + 1, 5, OUTLINE)
    t.rect(cx - 1, 2, cx, 4, BRASS)
    t.set(cx - 1, 2, BRASS_LIGHT)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    return t.img


def tex_crate_top(w, h):
    t = Tex(w, h, WOOD)
    t.planks_v(0, 0, w - 1, h - 1, 4)
    for by in (1, h - 3):
        t.rect(0, by, w - 1, by + 1, IRON)
        t.rect(0, by, w - 1, by, IRON_MID)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    return t.img


def tex_mast(w, h):
    """Wooden post with brass rings."""
    t = Tex(w, h, WOOD)
    t.planks_v(0, 0, w - 1, h - 1, max(w, 2))
    for y in range(2, h, 6):
        t.rect(0, y, w - 1, y + 1, BRASS)
        t.rect(0, y, w - 1, y, BRASS_LIGHT)
    return t.img


def tex_beacon_front(w, h):
    """Iron housing with a round blue glass showing signal waves rising from a hub."""
    t = Tex(w, h, IRON)
    t.frame(0, 0, w - 1, h - 1, IRON_MID, IRON_DARK)
    for x, y in ((1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2)):
        t.set(x, y, IRON_LIGHT)
    cx, cy = w / 2, h / 2
    r = min(w, h) / 2 - 1.5
    t.disc(cx, cy, r + 0.9, OUTLINE)
    t.ring(cx, cy, r + 0.4, BRASS, 1.0)
    t.disc(cx, cy, r - 0.6, GLASS_DEEP)
    t.disc(cx - 0.4, cy - 0.4, r - 1.4, GLASS)
    hx, hy = cx, cy + r * 0.45
    for k, rad in enumerate((r * 0.45, r * 0.85)):
        for y in range(t.h):
            for x in range(t.w):
                d = math.hypot(x + 0.5 - hx, y + 0.5 - hy)
                a = math.degrees(math.atan2(y + 0.5 - hy, x + 0.5 - hx)) % 360
                inside = (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 < (r - 0.8) ** 2
                if abs(d - rad) <= 0.5 and 215 <= a <= 325 and inside:
                    t.set(x, y, GLASS_HI if k else GLASS_LIGHT)
    t.rect(int(hx) - 1, int(hy) - 1, int(hx), int(hy), GLASS_HI)
    return t.img


def tex_iron_cap(w, h):
    t = Tex(max(w, 1), max(h, 1), IRON)
    t.frame(0, 0, t.w - 1, t.h - 1, IRON_LIGHT, IRON_DARK)
    return t.img


def tex_junction_front(w, h):
    """Riveted iron plate: brass gauge on the left, blue node pane on the right, brass label plate."""
    t = Tex(w, h, IRON)
    t.frame(0, 0, w - 1, h - 1, IRON_MID, IRON_DARK)
    for x, y in ((1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2)):
        t.set(x, y, IRON_LIGHT)
    gx, gy = 6.5, h / 2
    gr = min(4.2, h / 2 - 1.5)
    t.disc(gx, gy, gr + 0.9, OUTLINE)
    t.ring(gx, gy, gr + 0.4, BRASS, 1.0)
    t.disc(gx, gy, gr - 0.6, PAPER)
    t.line(int(gx), int(gy), int(gx + 2), int(gy - 2), RED_KNOB)
    t.set(int(gx), int(gy), OUTLINE)
    px0, py0 = w - 11, int(h / 2) - 4
    t.rect(px0, py0, px0 + 8, py0 + 7, OUTLINE)
    t.rect(px0 + 1, py0 + 1, px0 + 7, py0 + 6, GLASS)
    t.rect(px0 + 1, py0 + 1, px0 + 7, py0 + 1, GLASS_MID)
    t.line(px0 + 2, py0 + 5, px0 + 4, py0 + 3, GLASS_LIGHT)
    t.line(px0 + 4, py0 + 3, px0 + 6, py0 + 5, GLASS_LIGHT)
    for x, y in ((px0 + 4, py0 + 3), (px0 + 2, py0 + 5), (px0 + 6, py0 + 5)):
        t.set(x, y, GLASS_HI)
    t.rect(12, h - 4, w - 13, h - 3, BRASS)
    t.rect(12, h - 4, w - 13, h - 4, BRASS_LIGHT)
    return t.img


def tex_junction_top(w, h):
    t = Tex(w, h, IRON)
    t.frame(0, 0, w - 1, h - 1, IRON_LIGHT, IRON_DARK)
    for x in range(2, w - 2, 4):
        t.set(x, 1, IRON_MID)
    return t.img


# ----------------------------------------------------------------------------------------- geometry
PITCH = math.radians(53.130104)
CAM_UP = (0.0, math.cos(PITCH), math.sin(PITCH))
CAM_FWD = (0.0, -math.sin(PITCH), math.cos(PITCH))


def screen_len(a, b):
    """On-screen length (in texels) of the edge a->b under the game's orthographic camera."""
    d = [b[i] - a[i] for i in range(3)]
    sx = d[0]
    sy = d[0] * CAM_UP[0] + d[1] * CAM_UP[1] + d[2] * CAM_UP[2]
    return math.hypot(sx, sy)


class Model:
    """Quads in texel coordinates (50 per unit): x right, y up, z away from the camera.

    Each face's texture is painted at the size the face has on screen, so its pixels come out square
    in game like the game's own art (vertical faces are seen at 60%, horizontal ones at 80%)."""

    def __init__(self):
        self.faces = []  # (corners[4] texel xyz, texture image)

    def quad(self, corners, paint):
        """corners: bottom-left, bottom-right, top-right, top-left as seen when looking at the face.
        paint(w, h) -> Image, or an Image used as is."""
        if callable(paint):
            w = max(1, int(round(screen_len(corners[0], corners[1]))))
            h = max(1, int(round(screen_len(corners[0], corners[3]))))
            paint = paint(w, h)
        self.faces.append((corners, paint))

    def box(self, x0, x1, y0, y1, z0, z1, front=None, top=None, left=None, right=None):
        if front is not None:
            self.quad([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], front)
        if top is not None:
            self.quad([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], top)
        if left is not None:
            self.quad([(x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1)], left)
        if right is not None:
            self.quad([(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)], right)


def build_central_storage(width=W):
    """width: 34 for the chest's horizontal footprint (0.76), 26 for the vertical one (0.58)."""
    m = Model()
    hw = width / 2
    cab_h = 24
    # cabinet of drawers
    m.box(-hw, hw, 0, cab_h, -11, 10, front=tex_cabinet_front,
          left=lambda w, h: tex_side(w, h), right=lambda w, h: tex_side(w, h))
    # sloped desk: front edge low, rising towards the panel
    z_front, z_back, y_front, y_back = -13, 0, cab_h + 1, cab_h + 5
    m.quad([(-hw - 2, cab_h, z_front), (hw + 2, cab_h, z_front), (hw + 2, y_front, z_front), (-hw - 2, y_front, z_front)], tex_desk_lip)
    m.quad([(-hw - 2, y_front, z_front), (hw + 2, y_front, z_front), (hw + 2, y_back, z_back), (-hw - 2, y_back, z_back)], tex_desk_top)
    # back panel leaning back (like the reference's screen wall), monitors on its face
    lean = math.radians(22)
    panel = 23
    top_y, top_z = y_back + panel * math.cos(lean), z_back + panel * math.sin(lean)
    m.quad([(-hw, y_back, z_back), (hw, y_back, z_back), (hw, top_y, top_z), (-hw, top_y, top_z)], tex_panel_front)
    m.quad([(-hw, top_y, top_z), (hw, top_y, top_z), (hw, top_y, top_z + 5), (-hw, top_y, top_z + 5)],
           lambda w, h: tex_wood_cap(w, h))
    for x in (-hw, hw):
        side = [(x, cab_h, z_back + 6), (x, cab_h, z_back), (x, top_y, top_z), (x, top_y, top_z + 5)]
        if x > 0:
            side = [side[1], side[0], side[3], side[2]]
        m.quad(side, lambda w, h: tex_side(max(w, 1), h))
    # pipes from the panel's sides down to the ground
    for x in (-hw - 2, hw):
        m.box(x, x + 2, 0, top_y - 4, 6, 8, front=tex_pipe)
    return m


def build_zone_relay(width=28):
    """width: 28 for the chest's horizontal footprint (0.64), 22 for the vertical one (0.50)."""
    m = Model()
    hw = width / 2
    crate_h = 16
    m.box(-hw, hw, 0, crate_h, -8, 8, front=tex_crate_front, top=tex_crate_top,
          left=tex_side, right=tex_side)
    # mast behind the lid; the beacon plate leans back so its round glass reads round on screen
    m.box(-1.5, 1.5, crate_h, 40, 3, 6, front=tex_mast, left=tex_mast, right=tex_mast)
    bw = 8 if width >= 26 else 7
    lean = math.radians(40)
    plate = 17
    y0, z0 = 36, 1
    y1, z1 = y0 + plate * math.cos(lean), z0 + plate * math.sin(lean)
    m.quad([(-bw, y0, z0), (bw, y0, z0), (bw, y1, z1), (-bw, y1, z1)], tex_beacon_front)
    m.quad([(-bw, y1, z1), (bw, y1, z1), (bw, y1, z1 + 3), (-bw, y1, z1 + 3)], tex_iron_cap)
    m.quad([(-bw, y0 - 2, z0), (bw, y0 - 2, z0), (bw, y0, z0), (-bw, y0, z0)], tex_iron_cap)
    return m


def build_connector(width=24):
    """width: 24 for the rough chest's horizontal footprint (0.64), 20 for the vertical one (0.50)."""
    m = Model()
    hw = width / 2
    box_h = 22
    m.box(-hw, hw, 0, box_h, -4, 4, front=tex_junction_front, top=tex_junction_top,
          left=tex_iron_cap, right=tex_iron_cap)
    # two brass pipes out of the top joined by a manifold: the link to the network
    top = box_h + 12
    xs = (-hw + 3, hw - 5)
    for x in xs:
        m.box(x, x + 2, box_h, top, -1, 1, front=tex_pipe, left=tex_pipe, right=tex_pipe)
        m.box(x - 1, x + 3, box_h, box_h + 2, -2, 2, front=tex_iron_cap, top=tex_iron_cap)
    m.box(xs[0] - 1, xs[1] + 3, top - 1, top + 2, -2, 2, front=tex_desk_lip, top=tex_iron_cap,
          left=tex_iron_cap, right=tex_iron_cap)
    return m


# ----------------------------------------------------------------------------------------- export
def pack(images):
    """Shelf-pack face textures into one atlas (1px transparent gutter). Returns atlas, rects."""
    order = sorted(range(len(images)), key=lambda i: -images[i].height)
    width = 128
    x = y = shelf = 0
    rects = [None] * len(images)
    for i in order:
        w, h = images[i].size
        if x + w + 1 > width:
            x, y, shelf = 0, y + shelf + 1, 0
        rects[i] = (x, y, w, h)
        x += w + 1
        shelf = max(shelf, h)
    height = 1
    while height < y + shelf + 1:
        height *= 2
    atlas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for i, img in enumerate(images):
        atlas.paste(img, rects[i][:2])
    return atlas, rects


def export(name, model, preview_dir=None):
    os.makedirs(OUT, exist_ok=True)
    atlas, rects = pack([img for _, img in model.faces])
    aw, ah = atlas.size
    quads = []
    for (corners, img), (rx, ry, rw, rh) in zip(model.faces, rects):
        # UV origin bottom-left in Unity; image rows go down.
        u0, u1 = rx / aw, (rx + rw) / aw
        v0, v1 = 1 - (ry + rh) / ah, 1 - ry / ah
        quads.append({
            "v": [[c[0] / TEXELS_PER_UNIT, c[1] / TEXELS_PER_UNIT, c[2] / TEXELS_PER_UNIT] for c in corners],
            "uv": [[u0, v0], [u1, v0], [u1, v1], [u0, v1]],
        })
    with open(os.path.join(OUT, name + ".json"), "w") as f:
        json.dump({"name": name, "quads": quads}, f, indent=1)
    atlas.save(os.path.join(OUT, name + ".png"))
    print("wrote", name, len(quads), "quads, atlas", atlas.size)
    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        render_preview(model).save(os.path.join(preview_dir, name + "_preview.png"))
        atlas.resize((aw * 4, ah * 4), Image.NEAREST).save(os.path.join(preview_dir, name + "_atlas_x4.png"))


def render_preview(model, scale=10):
    """Orthographic view at the game's pitch, painter's algorithm, flat light per face orientation."""
    up, fwd = CAM_UP, CAM_FWD

    def project(p):
        return p[0], p[1] * up[1] + p[2] * up[2], p[1] * fwd[1] + p[2] * fwd[2]

    faces = []
    for corners, img in model.faces:
        pr = [project(c) for c in corners]
        depth = sum(p[2] for p in pr) / 4
        a = (corners[1][0] - corners[0][0], corners[1][1] - corners[0][1], corners[1][2] - corners[0][2])
        b = (corners[3][0] - corners[0][0], corners[3][1] - corners[0][1], corners[3][2] - corners[0][2])
        n = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
        ln = math.sqrt(sum(v * v for v in n)) or 1
        light = 0.62 + 0.38 * max(0.0, (n[1] * 0.8 - n[2] * 0.5 - n[0] * 0.2) / ln)
        faces.append((depth, pr, img, light))
    xs = [p[0] for _, pr, _, _ in faces for p in pr]
    ys = [p[1] for _, pr, _, _ in faces for p in pr]
    pad = 4
    W_, H_ = int((max(xs) - min(xs)) * scale) + 2 * pad, int((max(ys) - min(ys)) * scale) + 2 * pad
    canvas = Image.new("RGBA", (W_, H_), (40, 38, 46, 255))

    def to_px(p):
        return ((p[0] - min(xs)) * scale + pad, (max(ys) - p[1]) * scale + pad)

    for depth, pr, img, light in sorted(faces, key=lambda f: -f[0]):
        p0, p1, p3 = to_px(pr[0]), to_px(pr[1]), to_px(pr[3])
        # texture coords: u along p0->p1, v along p0->p3 (image row 0 is the top, i.e. at p3)
        ax, ay = p1[0] - p0[0], p1[1] - p0[1]
        bx, by = p3[0] - p0[0], p3[1] - p0[1]
        det = ax * by - ay * bx
        if abs(det) < 1e-6:
            continue
        iw, ih = img.size
        # pixel (X,Y) -> u,v in [0,1]; texture x = u*iw, y = (1-v)*ih
        ia, ib, ic, id_ = by / det, -bx / det, -ay / det, ax / det
        a_ = ia * iw
        b_ = ib * iw
        c_ = -(ia * p0[0] + ib * p0[1]) * iw
        d_ = -ic * ih
        e_ = -id_ * ih
        f_ = ih + (ic * p0[0] + id_ * p0[1]) * ih
        lit = img.point(lambda v: v) if light >= 0.999 else Image.eval(img, lambda v: v)
        shaded = Image.merge("RGBA", [ch.point(lambda v, k=light: int(v * k)) for ch in lit.split()[:3]] + [lit.split()[3]])
        warped = shaded.transform((W_, H_), Image.AFFINE, (a_, b_, c_, d_, e_, f_), resample=Image.NEAREST)
        mask = Image.new("L", (W_, H_), 0)
        ImageDraw.Draw(mask).polygon([to_px(p) for p in pr], fill=255)
        alpha = Image.eval(warped.split()[3], lambda v: v)
        mask = Image.composite(alpha, Image.new("L", (W_, H_), 0), mask)
        canvas.paste(warped, (0, 0), mask)
    return canvas


if __name__ == "__main__":
    preview = sys.argv[1] if len(sys.argv) > 1 else None
    export("central_storage", build_central_storage(), preview)
    # The vertical orientation is the chest turned a quarter; the models stay facing the camera, narrower.
    export("central_storage_v", build_central_storage(26), preview)
    export("zone_relay", build_zone_relay(), preview)
    export("zone_relay_v", build_zone_relay(22), preview)
    export("connector", build_connector(), preview)
    export("connector_v", build_connector(20), preview)
