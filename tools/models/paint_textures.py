"""Paints the Storage Network structures' textures over the game's own chest textures.

Input: the chest textures exported by the DataDump studio (BepInEx/dumps/studio/<wgo>/tex_*.png),
copied to tools/models/base/. Output: src/StorageNetwork/Textures/<look>__<texture name>.png, which the
mod swaps in for that texture on tagged objects only.

Only the chest's own iron and wood palette is used, with the game's dark outline and top-left light.
Usage: python paint_textures.py
"""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(HERE, "base")
OUT = os.path.join(HERE, "..", "..", "src", "StorageNetwork", "Textures")

# Palette taken from wb_good_trunk_* (iron fittings and wood).
OUTLINE = (53, 40, 34, 255)
IRON_DARK = (76, 76, 70, 255)
IRON = (93, 93, 86, 255)
IRON_MID = (113, 123, 121, 255)
IRON_LIGHT = (135, 147, 145, 255)
WOOD_DARK = (70, 35, 12, 255)
CLEAR = (0, 0, 0, 0)


def gear_shape(radius):
    """Pixel offsets of a gear: round body, 4 straight + 4 diagonal teeth (like the build icons)."""
    body_r, tooth, diag = {5: (3.9, 5, (3, 4)), 4: (3.1, 4, (3, 3))}[radius]
    inside = set()
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            body = dx * dx + dy * dy <= body_r * body_r
            straight = (abs(dx) <= 1 and abs(dy) <= tooth) or (abs(dy) <= 1 and abs(dx) <= tooth)
            diagonal = diag[0] <= abs(dx) <= diag[1] and diag[0] <= abs(dy) <= diag[1] and not (abs(dx) == 4 and abs(dy) == 4)
            if body or straight or diagonal:
                inside.add((dx, dy))
    return inside


def paint_gear(img, cx, cy, radius, hole_r2=1, hole=WOOD_DARK, outline=True):
    """Iron gear with a dark outline ring; hole=CLEAR punches through (for the handle)."""
    px = img.load()
    shape = gear_shape(radius)
    if outline:
        for dx, dy in shape:
            for ox, oy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (dx + ox, dy + oy)
                if n not in shape:
                    px[cx + n[0], cy + n[1]] = OUTLINE
    for dx, dy in shape:
        r2 = dx * dx + dy * dy
        lit = (dx - 1, dy) not in shape or (dx, dy - 1) not in shape
        dark = (dx + 1, dy) not in shape or (dx, dy + 1) not in shape
        if r2 <= hole_r2:
            color = hole
        elif r2 <= hole_r2 + 3:
            color = OUTLINE if hole == CLEAR else IRON_DARK  # rim around the hub
        elif lit and not dark:
            color = IRON_LIGHT
        elif dark and not lit:
            color = IRON_DARK
        elif dx + dy < 0:
            color = IRON_MID
        else:
            color = IRON
        px[cx + dx, cy + dy] = color


def clear_box(img, x0, y0, x1, y1):
    px = img.load()
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            px[x, y] = CLEAR


def central_storage():
    """Big Chest with an iron gear on the lid's lock plate and a gear-shaped handle."""
    out = {}
    lid_h = Image.open(os.path.join(BASE, "wb_good_trunk_h_prt2.png")).convert("RGBA")
    paint_gear(lid_h, 24, 22, 5)
    out["wb_good_trunk_h_prt2"] = lid_h

    lid_v = Image.open(os.path.join(BASE, "wb_good_trunk_v_prt2.png")).convert("RGBA")
    paint_gear(lid_v, 17, 19, 4)
    out["wb_good_trunk_v_prt2"] = lid_v

    handle_h = Image.open(os.path.join(BASE, "wb_good_trunk_h_prt3.png")).convert("RGBA")
    clear_box(handle_h, 18, 18, 31, 30)
    paint_gear(handle_h, 24, 24, 5, hole_r2=2, hole=CLEAR)
    out["wb_good_trunk_h_prt3"] = handle_h

    handle_v = Image.open(os.path.join(BASE, "wb_good_trunk_v_prt3.png")).convert("RGBA")
    clear_box(handle_v, 8, 13, 20, 24)
    paint_gear(handle_v, 14, 19, 4, hole_r2=1, hole=CLEAR)
    out["wb_good_trunk_v_prt3"] = handle_v
    return out


LOOKS = {
    "central_storage": central_storage,
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for look, build in LOOKS.items():
        for texture_name, img in build().items():
            path = os.path.join(OUT, f"{look}__{texture_name}.png")
            img.save(path)
            print("wrote", os.path.relpath(path, HERE))


if __name__ == "__main__":
    main()
