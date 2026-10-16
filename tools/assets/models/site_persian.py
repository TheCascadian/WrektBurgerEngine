#!/usr/bin/env python3
"""Persian (Achaemenid) building kit for the 3D location sites, around 500 BCE. Metres, +Y up, facing +Z, standing on y = 0, every vertex weighted to the root
joint (see site_latin.py, which this reuses helpers from, and docs/game/SITES.md). Output: out/site_<piece>_persian.glb.

OPEN (design, not sourced fact; from general knowledge, no source looked up): sizes, colors and exact forms. Anchors: sun-dried mud-brick walls and flat roofs on timber
beams, the stone terrace with double stair and the columned audience hall (apadana) of Persepolis (begun under Darius I, c. 518 BCE, so a stage that 500 BCE would
only partly show), the tower-tomb/fire-temple form of Naqsh-e Rustam and Pasargadae, a stepped fire altar, glazed turquoise tile friezes, the Gate of All Nations
with its colossal bulls (this one is Xerxes' time, after 486 BCE: OPEN, a plainer gate is likely right for 500 BCE), cypress and date palm, the walled paradise garden (pairidaeza) with its cross channels."""
import math
from gear_lib import *
from site_latin import cylinder, oquad, W, V

MUD = (.70, .52, .36, 1); MUD_D = (.56, .41, .28, 1); MUD_L = (.78, .62, .44, 1); STONE = (.46, .42, .36, 1); STONE_D = (.34, .31, .27, 1); STONE_L = (.54, .50, .43, 1)
CEDAR = (.30, .19, .10, 1); TURQ = (.10, .48, .54, 1); TURQ_D = (.07, .35, .42, 1); PURPLE = (.38, .18, .48, 1); GOLD = (.82, .64, .18, 1); DARKD = (.10, .07, .05, 1)
WATER = (.16, .46, .60, 1); LAWN = (.30, .42, .20, 1); LAWN_D = (.25, .36, .17, 1)


def beams(m, x0, x1, z0, z1, y, step=.8):
    """Ends of roof beams sticking out under a flat roof's parapet, on the front and back faces."""
    x = x0 + .4
    while x < x1:
        for z in (z0, z1): m.box((x - .1, y - .2, z - .15), (x + .1, y, z + .15), CEDAR, W)
        x += step


def flat_building(m, x0, x1, z0, z1, h, col=MUD, parapet=.35):
    m.box((x0, 0, z0), (x1, h, z1), col, W)
    m.box((x0 - .05, h, z0 - .05), (x1 + .05, h + parapet, z0 + .3), MUD_D, W)
    m.box((x0 - .05, h, z1 - .3), (x1 + .05, h + parapet, z1 + .05), MUD_D, W)
    m.box((x0 - .05, h, z0), (x0 + .3, h + parapet, z1), MUD_D, W)
    m.box((x1 - .3, h, z0), (x1 + .05, h + parapet, z1), MUD_D, W)
    beams(m, x0, x1, z0, z1, h)


def mudbrick_house():
    """A flat-roofed mud-brick house: one block, a low door with a cedar lintel, roof beam ends, a parapet."""
    m = Mesh(); X, Z, H = 3.2, 2.8, 2.7
    flat_building(m, -X, X, -Z, Z, H)
    m.box((-.5, 0, Z - .1), (.5, 1.9, Z + .05), DARKD, W)
    m.box((-.65, 1.9, Z - .12), (.65, 2.05, Z + .1), CEDAR, W)
    m.box((X - 1.3, H - 1.1, Z - .06), (X - .7, H - .5, Z + .06), DARKD, W)             # a small window
    m.box((-X - .05, 0, -Z - .05), (X + .05, .3, Z + .05), MUD_D, W)
    return m


def courtyard_house():
    """A larger house round a yard: three flat-roofed ranges and a wall with a gate, a turquoise tile band over the door (a satrap's or a rich man's house)."""
    m = Mesh()
    flat_building(m, -5.5, 5.5, -6.0, -2.6, 3.3)
    flat_building(m, -5.5, -2.1, -2.6, 4.0, 3.0)
    flat_building(m, 2.1, 5.5, -2.6, 4.0, 3.0)
    m.box((-5.5, 0, 4.0), (-1.1, 1.9, 4.3), MUD, W); m.box((1.1, 0, 4.0), (5.5, 1.9, 4.3), MUD, W)       # yard wall with a gate gap
    m.box((-1.2, 1.9, 3.95), (1.2, 2.3, 4.35), TURQ, W)
    m.box((-1.0, 0, 3.95), (1.0, 1.9, 4.35), DARKD, W)
    m.box((-1.4, 3.3, -6.05), (1.4, 3.6, -2.55), TURQ, W)
    m.box((-1.0, 0, -2.65), (1.0, 2.2, -2.5), DARKD, W)
    return m


def storehouse():
    """A long mud-brick storehouse with buttresses and a few narrow slits."""
    m = Mesh(); X, Z, H = 4.4, 2.2, 3.0
    flat_building(m, -X, X, -Z, Z, H)
    for x in (-X, -X / 2, 0, X / 2, X): m.box((x - .3, 0, Z), (x + .3, H, Z + .35), MUD_D, W)
    m.box((-.55, 0, Z + .3), (.55, 2.0, Z + .36), DARKD, W)
    for x in (-3.0, -1.5, 1.5, 3.0): m.box((x - .08, 1.8, Z + .3), (x + .08, 2.5, Z + .36), DARKD, W)
    return m


def fire_altar():
    """A Zoroastrian fire altar: three steps, a tapering square shaft, a fire bowl (stone, gold flame)."""
    m = Mesh()
    for i, h in enumerate((.3, .6, .9)):
        s = 2.0 - .35 * i
        m.box((-s, 0, -s), (s, h, s), STONE_L if i % 2 else STONE, W)
    m.loft([dict(pts=ellipse((0, y, 0), r, r, 4), color=STONE, w=W) for y, r in ((.9, .95), (2.6, .65), (3.0, .85))], cap_last=True)
    m.box((-.55, 3.0, -.55), (.55, 3.15, .55), STONE_D, W)
    m.loft([dict(pts=ellipse((0, y, 0), r, r, 6), color=GOLD, w=W) for y, r in ((3.15, .35), (3.7, .24), (4.3, .02))], cap_last=True)
    return m


def tower_shrine():
    """A free-standing stone tower on a stepped base with blind windows and a high door reached by a stair (after the Ka'ba-i Zardusht at Naqsh-e Rustam)."""
    m = Mesh(); S, H = 3.6, 12.0
    for i in range(3): m.box((-S - 1.2 + .4 * i, 0, -S - 1.2 + .4 * i), (S + 1.2 - .4 * i, .4 * (i + 1), S + 1.2 - .4 * i), STONE_D if i % 2 else STONE, W)
    m.box((-S, 1.2, -S), (S, H, S), STONE, W)
    m.box((-S - .1, H, -S - .1), (S + .1, H + .5, S + .1), STONE_L, W)
    for k in range(3):                                         # blind windows on the four faces, three storeys
        y = 3.6 + k * 2.9
        for sg in (-1, 1):
            for x in (-1.9, 0, 1.9): m.box((x - .5, y, sg * S - (0 if sg < 0 else .05)), (x + .5, y + 1.6, sg * S + (.05 if sg < 0 else 0)), DARKD if k == 0 else STONE_D, W)
            for z in (-1.9, 0, 1.9): m.box((sg * S - (0 if sg < 0 else .05), y, z - .5), (sg * S + (.05 if sg < 0 else 0), y + 1.6, z + .5), STONE_D, W)
    m.box((-.8, 3.0, S - .05), (.8, 5.2, S + .1), DARKD, W)
    for i in range(7): m.box((-1.2, 1.2 + (i * .4) * .0, S + .1 + (6 - i) * .3), (1.2, .4 * (i + 1) * .75, S + .4 + (6 - i) * .3), STONE_L if i % 2 else STONE, W) if False else None
    for i in range(6): m.box((-1.4, 0, S + 1.2 + (5 - i) * .35), (1.4, .4 + i * .4, S + 1.55 + (5 - i) * .35), STONE_L if i % 2 else STONE, W)
    return m


def palace():
    """The terrace palace: a high stone platform with a double stair to the front and on it a columned audience hall (apadana): 6 x 6 cedar-roofed columns on bell bases,
    mud-brick back walls, a turquoise tile band round the terrace, gold at the stair heads. The landmark of the Persian city tier."""
    m = Mesh(); X, Z, H = 15.0, 13.0, 3.2
    m.box((-X, 0, -Z), (X, H, Z), STONE, W)
    m.box((-X - .3, 0, -Z - .3), (X + .3, .5, Z + .3), STONE_D, W)
    m.box((-X - .15, H - .5, -Z - .15), (X + .15, H, Z + .15), TURQ, W)
    m.box((-X - .25, H, -Z - .25), (X + .25, H + .25, Z + .25), STONE_L, W)
    for sx in (-1, 1):                                          # the double stair on the front, in 12 steps
        for i in range(12):
            x0, x1 = (4.5, 7.5) if sx > 0 else (-7.5, -4.5)
            m.box((x0, 0, Z + .3 + (11 - i) * .35), (x1, .27 * (i + 1), Z + .65 + (11 - i) * .35), STONE_L if i % 2 else STONE, W)
        m.box((sx * 6 - .35, H, Z - .2), (sx * 6 + .35, H + .9, Z + .2), GOLD, W)
    hx, hz, hh = 9.0, 8.5, 8.4                                   # the hall
    m.box((-hx, H, -hz), (hx, H + .6, hz), STONE_L, W)
    m.box((-hx, H + .6, -hz), (hx, H + hh, -hz + .8), MUD, W)   # back and side walls
    m.box((-hx, H + .6, -hz), (-hx + .8, H + hh, hz - 3), MUD, W)
    m.box((hx - .8, H + .6, -hz), (hx, H + hh, hz - 3), MUD, W)
    for ix in range(6):
        for iz in range(6):
            x, z = -7.6 + ix * 3.04, -7.0 + iz * 2.95
            cylinder(m, x, z, .62, H + .6, H + .95, STONE_D, n=8)
            cylinder(m, x, z, .42, H + .95, H + hh - 1.1, STONE, n=8)
            m.box((x - .75, H + hh - 1.1, z - .75), (x + .75, H + hh - .35, z + .75), STONE_D, W)
            m.box((x - .35, H + hh - .35, z - .35), (x + .35, H + hh, z + .35), GOLD if (ix + iz) % 5 == 0 else CEDAR, W)
    m.box((-hx - .3, H + hh, -hz - .3), (hx + .3, H + hh + .5, hz + .3), CEDAR, W)             # flat cedar roof
    m.box((-hx - .3, H + hh + .5, -hz - .3), (hx + .3, H + hh + .75, hz + .3), MUD_D, W)
    m.box((-hx - .35, H + hh - .2, hz + .1), (hx + .35, H + hh + .05, hz + .4), TURQ, W)       # tile frieze under the front eave
    return m


def gate_tower():
    """A gate of two mud-brick towers with turquoise glazed caps and a pair of colossal stone bulls facing the road, a 3.4 m opening."""
    m = Mesh()
    for sx in (-1, 1):
        cx = sx * 2.9
        m.box((cx - 1.3, 0, -1.6), (cx + 1.3, 6.2, 1.6), MUD, W)
        m.box((cx - 1.5, 6.2, -1.8), (cx + 1.5, 6.7, 1.8), TURQ, W)
        for i in range(3): m.box((cx - 1.3 + i * .9, 6.7, -1.8), (cx - .7 + i * .9, 7.3, -1.4), MUD_D, W)
        m.box((cx - .6, 0, 1.6), (cx + .6, 1.7, 3.0), STONE, W)                                  # bull body
        m.box((cx - .5, 1.0, 2.7), (cx + .5, 2.0, 3.4), STONE_L, W)                              # head and forelock
        m.box((cx - .4, 1.7, 2.9), (cx + .4, 2.2, 3.2), GOLD, W)
    m.box((-1.7, 4.3, -1.6), (1.7, 6.2, 1.6), MUD, W)
    return m


def wall():
    """8 m of mud-brick city wall, 6 m high, with merlons and two buttresses, along x, outside toward +Z. Cut every 2 m so it follows the terrain."""
    m = Mesh()
    for x0 in (-4, -2, 0, 2):
        m.box((x0, -.5, -1.0), (x0 + 2, 5.6, 1.0), MUD, W)
        m.box((x0, 5.6, -1.1), (x0 + 2, 5.9, 1.1), MUD_D, W)
        for k in (0, 1): m.box((x0 + .15 + k, 5.9, .35), (x0 + .75 + k, 6.6, 1.1), MUD_L, W)
    for x in (-2.8, 2.8): m.box((x - .5, -.5, 1.0), (x + .5, 5.0, 1.5), MUD_D, W)
    return m


def tessellated(name_w, name_d, cell, colors, y):
    m = Mesh(); n = int(name_w / cell)
    for i in range(n):
        for j in range(int(name_d / cell)):
            x0, z0 = -name_w / 2 + i * cell, -name_d / 2 + j * cell
            m.quad(V(x0, y, z0), V(x0 + cell, y, z0), V(x0 + cell, y, z0 + cell), V(x0, y, z0 + cell), V(0, 1, 0), colors(i, j, n), W)
    return m


def path():
    """Beaten earth strip, 8 m long, 3 m wide, along x, one-metre cells."""
    return tessellated(8, 3, 1, lambda i, j, n: (.50, .38, .26, 1) if (i + j) % 2 else (.45, .34, .23, 1), .06)


def plaza():
    """A paved square, 18 m: pale stone paving in two tones with a dark border."""
    return tessellated(18, 18, 2, lambda i, j, n: STONE_D if i in (0, n - 1) or j in (0, n - 1) else (STONE_L if (i + j) % 2 else (.64, .60, .52, 1)), .07)


def garden():
    """A walled-garden quarter of the paradise garden (pairidaeza): 12 m square, lawns and a cross of water channels, in 1 m cells so it follows the terrain."""
    return tessellated(12, 12, 1, lambda i, j, n: WATER if i in (5, 6) or j in (5, 6) else (LAWN if (i // 3 + j // 3) % 2 else LAWN_D), .07)


def cypress():
    """A cypress: a narrow dark tower of foliage on a short trunk."""
    m = Mesh()
    cylinder(m, 0, 0, .18, 0, .9, (.32, .22, .12, 1), n=6)
    m.loft([dict(pts=ellipse((0, y, 0), r, r, 8), color=(.12, .26, .14, 1) if k % 2 else (.15, .31, .17, 1), w=W) for k, (y, r) in enumerate(((.7, .1), (1.2, .85), (3.0, 1.0), (5.0, .75), (7.0, .38), (8.0, .02)))], cap_last=True)
    return m


def palm():
    """A date palm: a ringed trunk and a flat crown of fronds."""
    m = Mesh()
    m.loft([dict(pts=ellipse((0, y, 0), r, r, 6), color=(.46, .34, .20, 1), w=W) for y, r in ((0, .30), (2.5, .22), (5.0, .20))], cap_last=True)
    for k in range(7):
        a = math.tau * k / 7
        d = V(math.cos(a), 0, math.sin(a)); n = V(-math.sin(a), 0, math.cos(a))
        base = V(0, 5.0, 0)
        tip = base + d * 2.6 + V(0, -.9, 0)
        m.tri(base + n * .35, base - n * .35, tip, V(0, 1, 0), (.22, .38, .16, 1), W)
        m.tri(base - n * .35, base + n * .35, tip, V(0, -1, 0), (.18, .32, .13, 1), W)
    return m


PIECES = [("mudbrick_house", mudbrick_house, 400), ("courtyard_house", courtyard_house, 800), ("storehouse", storehouse, 450), ("fire_altar", fire_altar, 300),
          ("tower_shrine", tower_shrine, 900), ("palace", palace, 2600), ("gate_tower", gate_tower, 500), ("wall", wall, 400), ("path", path, 100),
          ("plaza", plaza, 400), ("garden", garden, 400), ("cypress", cypress, 250), ("palm", palm, 250)]

if __name__ == "__main__":
    ok = True
    for name, fn, budget in PIECES:
        ok &= write_piece(fn(), f"site_{name}_persian", budget)
    finish(ok)
