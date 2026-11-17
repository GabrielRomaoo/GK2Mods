"""Factory Planner world models, same low-poly pixel-art style as build_models.py:
  planning_board   a chalk week chart and pinned order slips on an easel
  architect_table  a drafting easel with a pinned blueprint, T-square, set square, compass and pencils
  drafting_table   (unused) a tilted drawing board on a wooden trestle with a blueprint of a small factory line
                   pinned to it, a brass-edged ruler along the front, a pencil, and rolled plans on the shelf

Outputs src/FactoryPlanner/Models/<name>.json + .png (read by the mod's Models.cs), plus previews if a folder
is given.

Usage: python planner_models.py [preview_dir]
"""
import math
import os
import sys

import build_models as bm
from build_models import (Model, Tex, export, tex_side, BRASS, BRASS_DARK, BRASS_LIGHT, GLASS, GLASS_DEEP,
                          GLASS_HI, GLASS_LIGHT, GLASS_MID, IRON_DARK, IRON_LIGHT, OUTLINE, PAPER, RED_KNOB,
                          WOOD, WOOD_DARK, WOOD_DEEP, WOOD_HI, WOOD_LIGHT, WOOD_MID)

HERE = os.path.dirname(os.path.abspath(__file__))
bm.OUT = os.path.join(HERE, "..", "..", "src", "FactoryPlanner", "Models")

PAPER_DARK = (176, 156, 112, 255)
PENCIL = (214, 170, 58, 255)


def tex_blueprint(w, h):
    """Wooden board frame around a blue plan: two machines joined by a belt with an arrow, a gear, grid dots,
    brass pins in the corners and a pencil lying at the bottom right."""
    t = Tex(w, h, WOOD)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    px0, py0, px1, py1 = 2, 2, w - 3, h - 3
    t.rect(px0, py0, px1, py1, GLASS_DEEP)
    t.rect(px0 + 1, py0 + 1, px1 - 1, py1 - 1, GLASS)
    for y in range(py0 + 2, py1 - 1, 3):
        for x in range(px0 + 2, px1 - 1, 3):
            t.set(x, y, GLASS_MID)
    # machine A (left, higher) and machine B (right, lower), outlined in white with a dark slot
    ax0, ay0 = px0 + 3, py0 + 3
    bx0, by0 = px1 - 9, py1 - 8
    for x0, y0 in ((ax0, ay0), (bx0, by0)):
        t.rect(x0, y0, x0 + 5, y0 + 4, GLASS_LIGHT)
        t.rect(x0 + 1, y0 + 1, x0 + 4, y0 + 3, GLASS_MID)
        t.set(x0 + 2, y0 + 2, GLASS_DEEP)
        t.set(x0 + 3, y0 + 2, GLASS_DEEP)
    # belt: right from A, down, right into B, arrow head at B
    mid_x = (ax0 + 6 + bx0) // 2
    t.line(ax0 + 6, ay0 + 2, mid_x, ay0 + 2, GLASS_HI)
    t.line(mid_x, ay0 + 2, mid_x, by0 + 2, GLASS_HI)
    t.line(mid_x, by0 + 2, bx0 - 1, by0 + 2, GLASS_HI)
    t.set(bx0 - 2, by0 + 1, GLASS_HI)
    t.set(bx0 - 2, by0 + 3, GLASS_HI)
    # gear under machine A
    gx, gy = ax0 + 3, py1 - 4
    if gy - 2 > ay0 + 5:
        t.ring(gx, gy, 2.2, GLASS_LIGHT, 1.0)
        for dx, dy in ((0, -3), (0, 2), (-3, 0), (2, 0)):
            t.set(int(gx) + dx, int(gy) + dy, GLASS_LIGHT)
    # brass pins
    for x, y in ((px0, py0), (px1, py0), (px0, py1), (px1, py1)):
        t.set(x, y, BRASS_LIGHT)
    # pencil across the bottom-right corner
    for i in range(7):
        x, y = px1 - 8 + i, py1 - 1 - i // 3
        t.set(x, y, PENCIL)
        t.set(x, y + 1, BRASS_DARK)
    t.set(px1 - 9, py1 - 1, PAPER)
    t.set(px1 - 10, py1 - 1, OUTLINE)
    t.set(px1 - 1, py1 - 3, RED_KNOB)
    return t.img


def tex_ruler(w, h):
    """Front edge of the board: a brass-bound ruler with tick marks."""
    t = Tex(w, max(h, 2), BRASS)
    t.rect(0, 0, w - 1, 0, BRASS_LIGHT)
    for x in range(1, w - 1, 2):
        t.set(x, t.h - 1, OUTLINE if x % 6 == 1 else BRASS_DARK)
    return t.img


def tex_leg(w, h):
    t = Tex(max(w, 1), max(h, 1), WOOD)
    for y in range(t.h):
        t.set(0, y, WOOD_MID)
        t.set(t.w - 1, y, WOOD_DARK)
    t.rect(0, t.h - 1, t.w - 1, t.h - 1, OUTLINE)
    return t.img


def tex_bar(w, h):
    t = Tex(max(w, 1), max(h, 1), WOOD_MID)
    t.rect(0, 0, t.w - 1, 0, WOOD_HI)
    t.rect(0, t.h - 1, t.w - 1, t.h - 1, WOOD_DARK)
    return t.img


def tex_shelf_top(w, h):
    t = Tex(w, h, WOOD)
    t.planks_v(0, 0, w - 1, h - 1, 4)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    return t.img


def tex_roll(w, h):
    """A rolled plan seen from the side: paper with a blue edge line and a string tie."""
    t = Tex(w, max(h, 2), PAPER)
    t.rect(0, t.h - 1, w - 1, t.h - 1, PAPER_DARK)
    t.rect(1, 0, w - 2, 0, (232, 220, 186, 255))
    t.set(w // 3, 0, GLASS)
    t.rect(w // 2, 0, w // 2, t.h - 1, RED_KNOB)
    return t.img


def tex_roll_end(w, h):
    t = Tex(max(w, 1), max(h, 1), PAPER_DARK)
    t.set(t.w // 2, t.h // 2, PAPER)
    return t.img


def build_drafting_table(width=28):
    """width: 28 for the rough chest's horizontal footprint (0.64), 22 for the vertical one (0.50)."""
    m = Model()
    hw = width / 2
    zf, zb = -8, 8           # front and back legs
    yf, yb = 15, 25          # board height at the front and back edge
    # legs: front ones short, back ones tall (the board leans towards the viewer)
    for x in (-hw + 1, hw - 3):
        m.box(x, x + 2, 0, yf, zf, zf + 2, front=tex_leg, left=tex_leg, right=tex_leg)
        m.box(x, x + 2, 0, yb - 1, zb - 2, zb, front=tex_leg, left=tex_leg, right=tex_leg)
        # side rail joining front and back leg
        side = [(x, 6, zb - 2), (x, 6, zf + 2), (x, 8, zf + 2), (x, 8, zb - 2)]
        m.quad(side, tex_bar)
    # shelf between the legs with two rolled plans on it
    m.box(-hw + 1, hw - 1, 7, 8, zf + 1, zb - 1, front=tex_bar, top=tex_shelf_top)
    m.box(-hw + 4, hw - 7, 8, 10, zf + 3, zf + 5, front=tex_roll, top=tex_roll, left=tex_roll_end, right=tex_roll_end)
    m.box(-hw + 6, hw - 4, 8, 10, zf + 6, zf + 8, front=tex_roll, top=tex_roll, left=tex_roll_end, right=tex_roll_end)
    # front stretcher (foot rest)
    m.box(-hw + 1, hw - 1, 3, 5, zf, zf + 1, front=tex_bar, top=tex_bar)
    # the board: tilted top with the blueprint, ruler on its front edge, thin wooden sides
    z0, z1 = zf - 2, zb
    m.quad([(-hw - 1, yf - 2, z0), (hw + 1, yf - 2, z0), (hw + 1, yf, z0), (-hw - 1, yf, z0)], tex_ruler)
    m.quad([(-hw - 1, yf, z0), (hw + 1, yf, z0), (hw + 1, yb, z1), (-hw - 1, yb, z1)], tex_blueprint)
    for x in (-hw - 1, hw + 1):
        side = [(x, yf - 2, z1), (x, yf - 2, z0), (x, yf, z0), (x, yb, z1)]
        if x > 0:
            side = [side[1], side[0], side[3], side[2]]
        m.quad(side, lambda w, h: tex_side(max(w, 1), max(h, 1)))
    return m


SLATE_DEEP = (34, 40, 38, 255)
SLATE = (46, 56, 52, 255)
SLATE_HI = (62, 74, 68, 255)
CHALK = (222, 222, 208, 255)
CHALK_DIM = (150, 156, 146, 255)
CHALK_Y = (224, 200, 120, 255)
CHALK_B = (150, 184, 214, 255)
CHALK_G = (160, 206, 140, 255)
PRIDE = (150, 96, 186, 255)
PIN = (196, 58, 40, 255)
STRING = (170, 50, 36, 255)


def tex_planning_board(w, h):
    """The planning board's face: a brass name plate on the top rail; on the left a chalk week chart
    (one row per machine, one column per day of the six-day week, the Pride day marked purple); on the right
    order slips pinned to cork, tied to the chart with red string."""
    t = Tex(w, h, WOOD)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    # top rail with the brass plate
    t.planks_h(1, 1, w - 2, 4, 4)
    t.rect(w // 2 - 5, 1, w // 2 + 4, 4, OUTLINE)
    t.rect(w // 2 - 4, 2, w // 2 + 3, 3, BRASS)
    t.rect(w // 2 - 4, 2, w // 2 + 3, 2, BRASS_LIGHT)
    for x in range(w // 2 - 3, w // 2 + 3, 2):
        t.set(x, 3, BRASS_DARK)
    t.rect(1, 5, w - 2, 5, WOOD_DARK)
    # chalk chart
    gx0, gy0 = 2, 6
    cols = 6
    cw = 2 if w < 28 else 3
    gx1 = gx0 + 1 + cols * cw
    gy1 = h - 3
    t.rect(gx0, gy0, gx1, gy1, SLATE)
    t.frame(gx0, gy0, gx1, gy1, SLATE_DEEP, SLATE_HI)
    # day headers: Pride first, then five chalk ticks
    for c in range(cols):
        x = gx0 + 1 + c * cw
        t.rect(x, gy0 + 1, x + cw - 2 if cw > 2 else x, gy0 + 1, PRIDE if c == 0 else CHALK)
    t.rect(gx0 + 1, gy0 + 2, gx1 - 1, gy0 + 2, CHALK_DIM)
    # machine rows: Gantt bars of different lengths and colours
    bars = [(0, 3, CHALK), (1, 5, CHALK_Y), (2, 4, CHALK_B), (0, 6, CHALK_G), (3, 6, CHALK),
            (1, 3, CHALK_B), (2, 6, CHALK_Y), (0, 2, CHALK_G), (4, 6, CHALK)]
    y = gy0 + 4
    for start, end, colour in bars:
        if y + 1 >= gy1:
            break
        t.set(gx0 + 1, y, CHALK_DIM)  # row label
        t.rect(gx0 + 2 + start * cw, y, gx0 + 1 + end * cw - 1, y, colour)
        y += 2
    # cork with pinned order slips
    cx0, cx1 = gx1 + 2, w - 3
    t.rect(gx1 + 1, gy0, w - 2, gy1, WOOD_DARK)
    for x in range(cx0, cx1 + 1):
        for yy in range(gy0 + 1, gy1):
            if (x * 3 + yy * 5) % 7 == 0:
                t.set(x, yy, WOOD_MID)
    slips = []
    sy = gy0 + 1
    for i in range(3):
        x0 = cx0 + (0 if i % 2 == 0 or cx1 - cx0 < 5 else 1)
        if sy + 3 >= gy1 or x0 + 2 > cx1:
            break
        t.rect(x0, sy, min(cx1, x0 + 4), sy + 3, PAPER)
        t.rect(x0, sy + 3, min(cx1, x0 + 4), sy + 3, PAPER_DARK)
        t.rect(x0 + 1, sy + 2, min(cx1, x0 + 4) - 1, sy + 2, (120, 104, 80, 255))
        t.set(x0 + 2, sy, PIN)
        slips.append((x0, sy + 1))
        sy += 5
    # red string from the slips to the chart
    if slips:
        t.line(slips[0][0], slips[0][1] + 1, gx1 - 1, gy0 + 4, STRING)
    if len(slips) > 2:
        t.line(slips[2][0], slips[2][1], gx1 - 1, gy0 + 8, STRING)
    return t.img


def tex_chalk(w, h):
    t = Tex(max(w, 1), max(h, 1), CHALK)
    t.set(t.w - 1, t.h - 1, CHALK_DIM)
    return t.img


def tex_stamp(w, h):
    t = Tex(max(w, 1), max(h, 1), WOOD_DARK)
    t.rect(0, t.h - 1, t.w - 1, t.h - 1, PIN)
    t.set(0, 0, WOOD_HI)
    return t.img


def build_planning_board(width=30):
    """An A-frame easel holding a framed chalk/cork planning board, with a ledge for chalk and a stamp.
    width: 30 for the rough chest's horizontal footprint, 22 for the vertical one."""
    import math
    m = Model()
    hw = width / 2
    lean = math.radians(22)
    cos, sin = math.cos(lean), math.sin(lean)
    z_foot = -5

    def at(s):
        """Point on the easel's slope, s texels up the legs: (y, z)."""
        return s * cos, z_foot + s * sin

    # front legs on both sides of the board, plus their outer edge
    leg_len = 46
    yt, zt = at(leg_len)
    for x0, x1 in ((-hw - 2, -hw), (hw, hw + 2)):
        m.quad([(x0, 0, z_foot), (x1, 0, z_foot), (x1, yt, zt), (x0, yt, zt)], tex_leg)
        xo = x0 if x0 < 0 else x1
        side = [(xo, 0, z_foot + 2), (xo, 0, z_foot), (xo, yt, zt), (xo, yt, zt + 2)]
        if xo > 0:
            side = [side[1], side[0], side[3], side[2]]
        m.quad(side, tex_leg)
    # back leg (seen between the front legs' feet) and a cross bar
    m.quad([(-1, 0, 16), (1, 0, 16), (1, yt - 2, zt + 1), (-1, yt - 2, zt + 1)], tex_leg)
    yb, zb = at(7)
    m.box(-hw, hw, yb, yb + 2, zb - 0.5, zb + 0.5, front=tex_bar, top=tex_bar)
    # the board, slightly in front of the legs' plane
    s0, s1 = 11, 43
    y0, z0 = at(s0)
    y1, z1 = at(s1)
    m.quad([(-hw, y0, z0 - 1), (hw, y0, z0 - 1), (hw, y1, z1 - 1), (-hw, y1, z1 - 1)], tex_planning_board)
    m.quad([(-hw, y1, z1 - 1), (hw, y1, z1 - 1), (hw, y1, z1 + 1), (-hw, y1, z1 + 1)], tex_bar)
    # ledge under the board with a chalk stick and a stamp
    ly = y0 - 1
    m.box(-hw, hw, ly, y0, z0 - 4, z0 - 1, front=tex_bar, top=tex_shelf_top)
    m.box(-hw + 3, -hw + 7, y0, y0 + 1, z0 - 3, z0 - 2, front=tex_chalk, top=tex_chalk)
    m.box(hw - 6, hw - 3, y0, y0 + 3, z0 - 3.5, z0 - 1.5, front=tex_stamp, top=tex_stamp,
          left=tex_stamp, right=tex_stamp)
    return m


# ----------------------------------------------------------------------------------------- architect's desk
BOXWOOD = (206, 168, 98, 255)
BOXWOOD_DARK = (150, 112, 58, 255)
CELLULOID = (222, 168, 64, 255)
CELLULOID_DARK = (160, 108, 36, 255)
STEEL = (178, 188, 186, 255)
STEEL_DARK = (98, 106, 104, 255)
IVORY = (236, 226, 196, 255)
ERASER = (198, 120, 112, 255)
ROLL_BLUE = (70, 112, 176, 255)


def tex_architect_sheet(w, h):
    """The drafting board: wooden frame, a blueprint pinned to it with a small factory layout (three machines
    joined by belts), a dimension line, a compass arc, a protractor in the lower left corner and the title block
    in the lower right one. The instruments lying on top are separate raised strips."""
    t = Tex(w, h, WOOD)
    t.frame(0, 0, w - 1, h - 1, WOOD_HI, WOOD_DEEP)
    px0, py0, px1, py1 = 2, 2, w - 3, h - 3
    t.rect(px0, py0, px1, py1, GLASS_DEEP)
    t.rect(px0 + 1, py0 + 1, px1 - 1, py1 - 1, GLASS)
    for y in range(py0 + 2, py1 - 1, 3):
        for x in range(px0 + 2, px1 - 1, 3):
            t.set(x, y, GLASS_MID)
    # dimension line across the top, with end ticks
    dy = py0 + 2
    t.line(px0 + 3, dy, px1 - 3, dy, GLASS_LIGHT)
    for x in (px0 + 3, px1 - 3):
        t.line(x, dy - 1, x, dy + 1, GLASS_HI)
    t.set((px0 + px1) // 2, dy - 1, GLASS_HI)
    # three machines: two up top, one below, joined by belts with arrow heads
    mw, mh = 5, 4
    a = (px0 + 3, py0 + 5)
    b = (px1 - 3 - mw, py0 + 5)
    c = ((px0 + px1) // 2 - mw // 2, py0 + 5 + mh + 5)
    for x0, y0 in (a, b, c):
        if y0 + mh >= py1 - 6:
            continue
        t.rect(x0, y0, x0 + mw, y0 + mh, GLASS_HI)
        t.rect(x0 + 1, y0 + 1, x0 + mw - 1, y0 + mh - 1, GLASS_MID)
        t.set(x0 + 2, y0 + 2, GLASS_DEEP)
    ya = a[1] + mh // 2
    t.line(a[0] + mw + 1, ya, b[0] - 1, ya, GLASS_LIGHT)
    t.set(b[0] - 2, ya - 1, GLASS_LIGHT)
    t.set(b[0] - 2, ya + 1, GLASS_LIGHT)
    xc = c[0] + mw // 2
    t.line(xc, ya + 1, xc, c[1] - 1, GLASS_LIGHT)
    t.set(xc - 1, c[1] - 2, GLASS_LIGHT)
    t.set(xc + 1, c[1] - 2, GLASS_LIGHT)
    # compass arc around the lower machine
    arc_r = 4.5
    acx, acy = c[0] + mw + 2, c[1] + mh // 2
    for i in range(0, 14):
        ang = math.radians(-70 + i * 10)
        x, y = int(round(acx + arc_r * math.cos(ang))), int(round(acy + arc_r * math.sin(ang)))
        if px0 < x < px1 and py0 < y < py1:
            t.set(x, y, GLASS_HI)
    # protractor (half disc) in the lower left corner, ivory with ticks
    pr = 5
    pcx, pcy = px0 + 2 + pr, py1 - 1
    for y in range(pcy - pr, pcy + 1):
        for x in range(pcx - pr, pcx + pr + 1):
            d = math.hypot(x - pcx, y - pcy)
            if pr - 0.8 <= d <= pr + 0.4 or (y == pcy and d <= pr):
                t.set(x, y, IVORY)
    for ang in (30, 60, 90, 120, 150):
        x = int(round(pcx + (pr - 2) * math.cos(math.radians(ang))))
        y = int(round(pcy - (pr - 2) * math.sin(math.radians(ang))))
        t.set(x, y, IVORY)
    t.set(pcx, pcy - 1, OUTLINE)
    # title block in the lower right corner
    tx0, ty0 = px1 - 8, py1 - 4
    t.rect(tx0, ty0, px1 - 1, py1 - 1, GLASS_LIGHT)
    t.rect(tx0 + 1, ty0 + 1, px1 - 2, ty0 + 1, GLASS_DEEP)
    t.rect(tx0 + 1, ty0 + 2, px1 - 5, ty0 + 2, GLASS_MID)
    t.set(px1 - 3, ty0 + 2, RED_KNOB)
    # brass pins
    for x, y in ((px0, py0), (px1, py0), (px0, py1), (px1, py1)):
        t.set(x, y, BRASS_LIGHT)
    return t.img


def tex_solid(colour, edge=None, ticks=None):
    def paint(w, h):
        t = Tex(max(w, 1), max(h, 1), colour)
        if edge is not None:
            t.rect(0, t.h - 1, t.w - 1, t.h - 1, edge)
        if ticks is not None:
            for x in range(1, t.w - 1, 2):
                t.set(x, 0, ticks if x % 6 == 1 else edge or ticks)
        return t.img
    return paint


def tex_tsquare_blade(w, h):
    """Boxwood blade with inch marks along its upper edge."""
    t = Tex(max(w, 1), max(h, 3), BOXWOOD)
    t.rect(0, t.h - 1, t.w - 1, t.h - 1, BOXWOOD_DARK)
    for x in range(2, t.w - 1, 3):
        t.set(x, 0, OUTLINE if x % 9 == 2 else BOXWOOD_DARK)
    return t.img


def tex_pencil(w, h):
    t = Tex(max(w, 2), max(h, 1), PENCIL)
    t.rect(0, 0, 0, t.h - 1, OUTLINE)                 # graphite tip
    t.rect(1, 0, 1, t.h - 1, PAPER)                   # sharpened wood
    t.rect(t.w - 2, 0, t.w - 1, t.h - 1, ERASER)      # eraser end
    return t.img


def tex_roll_blue(w, h):
    t = Tex(w, max(h, 2), ROLL_BLUE)
    t.rect(0, t.h - 1, w - 1, t.h - 1, GLASS_DEEP)
    t.rect(1, 0, w - 2, 0, GLASS_MID)
    t.rect(w // 2, 0, w // 2, t.h - 1, RED_KNOB)
    return t.img


def tex_roll_blue_end(w, h):
    t = Tex(max(w, 1), max(h, 1), GLASS_LIGHT)
    t.set(t.w // 2, t.h // 2, GLASS_DEEP)
    return t.img


def build_architect_table(width=30):
    """A drafting easel: a steep drawing board on a wooden trestle, a blueprint pinned to it, a T-square
    clamped on its left edge, a set square, an open compass and a pencil lying on the sheet, a pencil tray on
    the front edge and rolled plans on the shelf below.
    width: 30 for the rough chest's horizontal footprint, 22 for the vertical one."""
    m = Model()
    hw = width / 2
    lean = math.radians(40)          # the board leans back this much from vertical
    cs, sn = math.cos(lean), math.sin(lean)
    yb, zb = 13, -6                  # board's lower edge
    length = 34                      # board length up the slope

    def on_board(x, s, d=0.0):
        """World point at x, s texels up the board, d texels in front of its face."""
        return (x, yb + s * cs + d * sn, zb + s * sn - d * cs)

    def strip(x0, s0, x1, s1, width_, d, paint):
        """A flat strip lying on the board (an instrument), raised d texels off it."""
        dx, ds = x1 - x0, s1 - s0
        ln = math.hypot(dx, ds) or 1
        px, ps = -ds / ln * width_ / 2, dx / ln * width_ / 2
        m.quad([on_board(x0 - px, s0 - ps, d), on_board(x1 - px, s1 - ps, d),
                on_board(x1 + px, s1 + ps, d), on_board(x0 + px, s0 + ps, d)], paint)

    top_y, top_z = on_board(0, length)[1:]
    # trestle: front legs under the board's lower edge, back legs under its top
    for x0 in (-hw + 1, hw - 3):
        m.box(x0, x0 + 2, 0, yb - 1, zb + 1, zb + 3, front=tex_leg, left=tex_leg, right=tex_leg)
        m.box(x0, x0 + 2, 0, top_y - 3, top_z - 1, top_z + 1, front=tex_leg, left=tex_leg, right=tex_leg)
        m.quad([(x0, 5, top_z - 1), (x0, 5, zb + 3), (x0, 7, zb + 3), (x0, 7, top_z - 1)], tex_bar)
    m.box(-hw + 1, hw - 1, 3, 5, zb + 1, zb + 2, front=tex_bar, top=tex_bar)          # foot rest
    # shelf with rolled plans, one paper and two blue ones
    m.box(-hw + 1, hw - 1, 6, 7, zb + 3, top_z - 1, front=tex_bar, top=tex_shelf_top)
    m.box(-hw + 3, hw - 8, 7, 9, zb + 4, zb + 6, front=tex_roll_blue, top=tex_roll_blue,
          left=tex_roll_blue_end, right=tex_roll_blue_end)
    m.box(-hw + 6, hw - 3, 7, 9, zb + 7, zb + 9, front=tex_roll, top=tex_roll, left=tex_roll_end, right=tex_roll_end)
    m.box(-hw + 4, hw - 6, 9, 11, zb + 5, zb + 7.5, front=tex_roll_blue, top=tex_roll_blue,
          left=tex_roll_blue_end, right=tex_roll_blue_end)
    # the board with the sheet, its top edge and wooden sides
    m.quad([on_board(-hw, 0), on_board(hw, 0), on_board(hw, length), on_board(-hw, length)], tex_architect_sheet)
    m.quad([on_board(-hw, length), on_board(hw, length), on_board(hw, length, -2), on_board(-hw, length, -2)], tex_bar)
    # pencil tray along the lower edge, with two pencils and an eraser
    m.box(-hw - 1, hw + 1, yb - 2, yb + 0.5, zb - 3, zb, front=tex_bar, top=tex_shelf_top)
    m.box(-hw + 2, -hw + 10, yb + 0.5, yb + 1.5, zb - 2.5, zb - 1.5, front=tex_pencil, top=tex_pencil)
    m.box(-hw + 5, -hw + 12, yb + 0.5, yb + 1.5, zb - 1.5, zb - 0.5, front=tex_pencil, top=tex_pencil)
    m.box(hw - 6, hw - 3, yb + 0.5, yb + 2, zb - 2.5, zb - 1, front=tex_solid(ERASER, edge=OUTLINE),
          top=tex_solid((220, 150, 140, 255)))
    # T-square: head clamped on the board's left edge, boxwood blade across the sheet
    s_blade = 8.5
    strip(-hw - 1, s_blade - 6, -hw - 1, s_blade + 8, 3, 1.2, tex_solid(WOOD_DARK, edge=WOOD_DEEP))
    strip(-hw, s_blade + 1, hw - 2, s_blade + 1, 4, 0.8, tex_tsquare_blade)
    # set square (esquadro): a hollow right triangle standing on the blade
    sx0, sx1 = hw - 13, hw - 4
    s0, s1 = s_blade + 3.8, s_blade + 3.8 + (sx1 - sx0)
    sq = tex_solid(CELLULOID, edge=CELLULOID_DARK)
    strip(sx0, s0, sx1, s0, 1.6, 0.9, sq)
    strip(sx1, s0, sx1, s1, 1.6, 0.9, sq)
    strip(sx0, s0, sx1, s1, 1.6, 0.9, sq)
    # open compass: brass hinge, two steel legs
    hx, hs = -hw + 9, length - 6
    strip(hx, hs, hx - 4, hs - 11, 1.2, 1.0, tex_solid(STEEL, edge=STEEL_DARK))
    strip(hx, hs, hx + 4, hs - 11, 1.2, 1.0, tex_solid(STEEL, edge=STEEL_DARK))
    strip(hx, hs - 1, hx, hs + 3, 2.2, 1.2, tex_solid(BRASS_LIGHT, edge=BRASS_DARK))
    # pencil on the sheet, at an angle
    strip(hw - 12, length - 5, hw - 3, length - 9, 1.2, 0.8, tex_pencil)
    return m


if __name__ == "__main__":
    preview = sys.argv[1] if len(sys.argv) > 1 else None
    export("planning_board", build_planning_board(), preview)
    # The vertical orientation is the chest turned a quarter; the model keeps facing the camera, narrower.
    export("planning_board_v", build_planning_board(22), preview)
    export("architect_table", build_architect_table(), preview)
    export("architect_table_v", build_architect_table(22), preview)
    if "--table" in sys.argv:
        export("drafting_table", build_drafting_table(), preview)
