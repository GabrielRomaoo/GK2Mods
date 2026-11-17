"""Builds the Factory Planner build icons (32x32, same blueprint style as make_icons.py).

Usage: python make_planner_icons.py [preview_dir]
Writes PNGs to ../../src/FactoryPlanner/Icons/ and, optionally, 8x previews to preview_dir.
"""
import os
import sys

from PIL import Image

from make_icons import Canvas, WHITE, LIGHT, LINE, SHADE, DEEP, HOLE

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "src", "FactoryPlanner", "Icons")


def drafting_table():
    """A drawing board on legs with a small factory plan on it (two machines joined by a belt) and a pencil."""
    c = Canvas()
    # board: lit top/left edges, shaded far edges
    x0, y0, x1, y1 = 5, 5, 26, 20
    for x in range(x0, x1 + 1):
        c.set(x, y0, WHITE)
        c.set(x, y1, SHADE)
    for y in range(y0, y1 + 1):
        c.set(x0, y, WHITE)
        c.set(x1, y, SHADE)
    # the plan drawn on the board: two machines and the belt between them
    c.block(8, 9, 12, 13)
    c.block(18, 12, 22, 16)
    c.line(13, 11, 15, 11, LIGHT)
    c.line(15, 11, 15, 14, LIGHT)
    c.line(15, 14, 17, 14, LIGHT)
    c.set(10, 11, HOLE)
    c.set(20, 14, HOLE)
    # pencil lying across the corner
    c.line(17, 7, 24, 7, WHITE)
    c.line(17, 8, 24, 8, DEEP)
    c.set(25, 7, LIGHT)
    c.set(25, 8, SHADE)
    # legs
    c.line(8, 21, 6, 27, LIGHT)
    c.line(23, 21, 25, 27, SHADE)
    c.line(9, 24, 22, 24, LINE)
    return c.img


def blueprint():
    """A plan sheet unrolled from a scroll: three machines joined by belts, the paper curling at the top."""
    c = Canvas()
    # sheet with a rolled top edge
    x0, y0, x1, y1 = 5, 6, 26, 26
    c.line(x0, y0 - 1, x1, y0 - 1, LIGHT)
    c.line(x0, y0, x1, y0, WHITE)
    c.line(x0 - 1, y0 - 1, x0 - 1, y0, SHADE)
    c.line(x1 + 1, y0 - 1, x1 + 1, y0, SHADE)
    for y in range(y0 + 1, y1 + 1):
        c.set(x0, y, WHITE)
        c.set(x1, y, SHADE)
    c.line(x0, y1, x1, y1, SHADE)
    # the layout: three machines, belts between them
    c.block(8, 10, 12, 14)
    c.block(19, 10, 23, 14)
    c.block(13, 19, 18, 23)
    c.line(13, 12, 18, 12, LIGHT)
    c.line(10, 15, 10, 21, LIGHT)
    c.line(10, 21, 12, 21, LIGHT)
    c.line(21, 15, 21, 21, LIGHT)
    c.line(21, 21, 19, 21, LIGHT)
    c.set(10, 12, HOLE)
    c.set(21, 12, HOLE)
    c.set(15, 21, HOLE)
    return c.img


def blueprint_copy():
    """Copying a piece of the factory: a dashed selection frame around two machines, a pencil tracing it."""
    c = Canvas()
    x0, y0, x1, y1 = 4, 5, 22, 21
    for x in range(x0, x1 + 1):
        if (x - x0) % 3 != 2:
            c.set(x, y0, WHITE)
            c.set(x, y1, SHADE)
    for y in range(y0, y1 + 1):
        if (y - y0) % 3 != 2:
            c.set(x0, y, WHITE)
            c.set(x1, y, SHADE)
    c.block(7, 9, 11, 13)
    c.block(15, 12, 19, 16)
    c.line(12, 11, 13, 11, LIGHT)
    c.line(13, 11, 13, 14, LIGHT)
    c.line(13, 14, 14, 14, LIGHT)
    # pencil coming in from the bottom right corner
    c.line(20, 27, 27, 20, LIGHT)
    c.line(21, 27, 27, 21, DEEP)
    c.set(19, 27, WHITE)
    c.set(18, 28, HOLE)
    return c.img


def architect_table():
    """A drafting easel: the board on three legs, a T-square across it, a set square and an open compass."""
    c = Canvas()
    x0, y0, x1, y1 = 6, 4, 25, 20
    for x in range(x0, x1 + 1):
        c.set(x, y0, WHITE)
        c.set(x, y1, SHADE)
    for y in range(y0, y1 + 1):
        c.set(x0, y, WHITE)
        c.set(x1, y, SHADE)
    # T-square: head on the left edge, blade across the board
    c.line(4, 12, 4, 18, LIGHT)
    c.line(5, 12, 5, 18, DEEP)
    c.line(6, 15, 23, 15, WHITE)
    c.line(6, 16, 23, 16, DEEP)
    # set square standing on the blade
    c.line(15, 14, 22, 14, LIGHT)
    c.line(22, 7, 22, 14, LIGHT)
    c.line(15, 14, 22, 7, LIGHT)
    # open compass in the upper left
    c.set(11, 6, WHITE)
    c.set(11, 7, WHITE)
    c.line(11, 8, 8, 13, LIGHT)
    c.line(11, 8, 14, 13, LIGHT)
    c.set(8, 14, HOLE)
    # legs and the cross bar
    c.line(9, 21, 7, 28, LIGHT)
    c.line(22, 21, 24, 28, SHADE)
    c.line(16, 21, 16, 27, DEEP)
    c.line(8, 24, 23, 24, LINE)
    return c.img


ICONS = {
    "gk2fp_icon_drafting_table": drafting_table,
    "gk2fp_icon_architect_table": architect_table,
    "gk2fp_icon_blueprint": blueprint,
    "gk2fp_icon_blueprint_copy": blueprint_copy,
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
