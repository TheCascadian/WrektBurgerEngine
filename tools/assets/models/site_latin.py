#!/usr/bin/env python3
"""Latin building kit for the 3D location sites (Rome and Latium, 500 BCE). Metres, +Y up, facing +Z, centered on the footprint, standing on y = 0.
Every vertex is weighted to the root joint, so a piece is an ordinary static model; SiteSystem (src/Burger.Core/Sites) places and bakes them.
Output: out/site_<piece>_latin.glb. See docs/game/SITES.md for the process.

OPEN (design, not sourced fact): sizes, colors and the exact forms. Anchors: Iron Age Latin round and oval huts (Palatine, Roman Forum cemetery hut-urns),
mud-brick or wattle-and-daub houses with thatch, terracotta-tiled roofs from the later 6th century, the Tuscan-order Temple of Jupiter Optimus Maximus on
the Capitoline (dedicated 509 BCE: high podium, deep porch, three cellae, wide eaves), the round hut-form Temple of Vesta, an earthen rampart with a timber palisade."""
import math
from gear_lib import *

W = [("root", 1.0)]
THATCH = (.38, .32, .21, 1); THATCH_D = (.29, .24, .16, 1); DAUB = (.62, .52, .38, 1); DAUB_D = (.50, .42, .31, 1); TIMBER = (.27, .17, .09, 1)
TILE = (.62, .27, .19, 1); TILE_D = (.48, .21, .15, 1); TUFF = (.56, .58, .60, 1); TUFF_D = (.42, .44, .47, 1); EARTH = (.33, .38, .19, 1); EARTH_D = (.24, .20, .12, 1)
PLASTER = (.80, .77, .68, 1); RED = (.66, .14, .10, 1); STRAW = (.78, .66, .36, 1); GRASS = (.40, .46, .24, 1); DARK = (.10, .07, .05, 1)
HALL_WALL = (.60, .25, .17, 1)
V = lambda x, y, z: np.array((x, y, z), float)


def cone(m, cx, cz, r, y0, y1, color, color2=None, n=10, rz=None):
    rings = []
    for k, (t, c) in enumerate(((0.0, color), (.35, color2 or color), (.7, color), (1.0, color))):
        s = 1 - t * .97
        rings.append(dict(pts=ellipse((cx, y0 + (y1 - y0) * t, cz), r * s, (rz or r) * s, n), color=c, w=W))
    m.loft(rings, cap_last=True)


def cylinder(m, cx, cz, r, y0, y1, color, n=12, rz=None, cap_top=False):
    m.loft([dict(pts=ellipse((cx, y, cz), r, rz or r, n), color=color, w=W) for y in (y0, y1)], cap_first=False, cap_last=cap_top)


def gable(m, x0, x1, z0, z1, y_eave, y_ridge, over, color, color2=None, gable_col=None):
    """Roof with the ridge along x: two slopes and two gable end triangles."""
    zm = (z0 + z1) / 2
    X0, X1, Z0, Z1 = x0 - over, x1 + over, z0 - over, z1 + over
    drop = (y_ridge - y_eave) * over / max(1e-6, (z1 - z0) / 2)
    ye = y_eave - drop
    for sgn, Z, col in ((1, Z1, color), (-1, Z0, color2 or color)):
        out = V(0, 1, sgn)
        ridge0, ridge1 = V(X0, y_ridge, zm), V(X1, y_ridge, zm)
        m.quad(V(X0, ye, Z), V(X1, ye, Z), ridge1, ridge0, out, col, W)
        m.quad(V(X0, ye, Z), ridge0, ridge1, V(X1, ye, Z), -out, DARK if False else THATCH_D, W)
    for X, sgn in ((x0, -1), (x1, 1)):
        m.tri(V(X, y_eave, z0), V(X, y_eave, z1), V(X, y_ridge, zm), V(sgn, 0, 0), gable_col or DAUB_D, W)


def pole_band(m, x0, x1, y, z, color=TIMBER):
    m.box((x0, y, z - .06), (x1, y + .12, z + .06), color, W)


# ------------------------------------------------------------------------------------------ houses
def hut_round():
    """Latin round hut: low wattle-and-daub drum, a conical thatch roof, a low door."""
    m = Mesh(); R = 2.1
    cylinder(m, 0, 0, R, 0, 1.35, DAUB, n=12)
    cylinder(m, 0, 0, R + .02, 0, .30, DAUB_D, n=12)
    cone(m, 0, 0, R + .55, 1.25, 3.6, THATCH, THATCH_D, n=12)
    m.box((-.35, 0, R - .1), (.35, 1.05, R + .05), DARK, W)
    m.box((-.45, 1.05, R - .12), (.45, 1.15, R + .08), TIMBER, W)
    m.box((-.02, 3.5, -.02), (.02, 4.0, .02), TIMBER, W)   # roof pole top
    return m


def hut_oval():
    """Oval hut, the common Palatine form: elongated drum, hipped thatch roof."""
    m = Mesh(); RX, RZ = 2.0, 3.0
    cylinder(m, 0, 0, RX, 0, 1.3, DAUB, n=12, rz=RZ)
    cylinder(m, 0, 0, RX + .02, 0, .28, DAUB_D, n=12, rz=RZ + .02)
    cone(m, 0, 0, RX + .5, 1.2, 3.3, THATCH, THATCH_D, n=12, rz=RZ + .5)
    m.box((-.35, 0, RZ - .1), (.35, 1.0, RZ + .05), DARK, W)
    return m


def house_timber(tile=False):
    """Rectangular house, wattle-and-daub or mud-brick on a stone footing, timber frame, gabled roof of thatch or terracotta tile."""
    m = Mesh(); X, Z, H = 3.6, 2.6, 2.3
    m.box((-X, 0, -Z), (X, .35, Z), TUFF_D, W)
    m.box((-X + .05, .35, -Z + .05), (X - .05, H, Z - .05), PLASTER if tile else DAUB, W)
    for x in (-X, -X / 3, X / 3, X):                               # frame posts
        m.box((x - .1, .35, Z - .09), (x + .1, H, Z + .05), TIMBER, W)
        m.box((x - .1, .35, -Z - .05), (x + .1, H, -Z + .09), TIMBER, W)
    m.box((-X, H - .12, Z - .12), (X, H, Z + .08), TIMBER, W)
    m.box((-.5, .35, Z - .08), (.5, 1.75, Z + .06), DARK, W)       # door
    m.box((-.6, 1.75, Z - .1), (.6, 1.85, Z + .08), TIMBER, W)
    gable(m, -X, X, -Z, Z, H, H + (1.9 if tile else 2.4), .55, TILE if tile else THATCH, TILE_D if tile else THATCH_D, PLASTER if tile else DAUB_D)
    if tile: m.box((-X - .55, H + 1.88, -.07), (X + .55, H + 1.98, .07), TILE_D, W)   # ridge tiles
    return m


def house_tile(): return house_timber(True)


def courtyard_house():
    """Larger house with a walled yard: an L of two tiled ranges and a low wall with a gate (an Etruscan-influenced domus of the late 6th century)."""
    m = Mesh()
    for (cx, cz, sx, sz) in ((-2.2, -2.6, 4.4, 2.3), (-4.4 + .2, 1.0, 2.2, 4.6)):
        m.box((cx - sx, 0, cz - sz), (cx + sx, .35, cz + sz), TUFF_D, W)
        m.box((cx - sx + .05, .35, cz - sz + .05), (cx + sx - .05, 2.4, cz + sz - .05), PLASTER, W)
    gable(m, -6.4, 2.2, -4.9, -.3, 2.4, 4.3, .5, TILE, TILE_D, PLASTER)
    m.box((-6.4, .35, -.3 + 0), (-2.0, 2.4, 5.6), PLASTER, W)
    gable(m, -6.6, -2.0 + .4, -.1, 5.8, 2.4, 4.1, .4, TILE_D, TILE, PLASTER)
    m.box((2.0, 0, -.3), (2.2, 1.4, 5.6), TUFF, W)                  # yard wall
    m.box((-2.0, 0, 5.4), (2.2, 1.4, 5.6), TUFF, W)
    m.box((-.5, .35, 5.35), (.5, 1.9, 5.65), DARK, W)
    return m


def granary():
    """Raised granary: a thatch-roofed box on short posts."""
    m = Mesh(); X, Z = 1.6, 1.4
    for x in (-X + .15, X - .15):
        for z in (-Z + .15, Z - .15):
            m.box((x - .1, 0, z - .1), (x + .1, .7, z + .1), TIMBER, W)
    m.box((-X, .7, -Z), (X, 1.9, Z), (.58, .44, .26, 1), W)
    gable(m, -X, X, -Z, Z, 1.9, 3.3, .45, THATCH, THATCH_D, (.58, .44, .26, 1))
    return m


# ------------------------------------------------------------------------------------------ sacred
def shrine():
    """Small shrine: a podium, four columns and a tiled gabled roof (a local sanctuary)."""
    m = Mesh(); X, Z = 2.0, 2.8
    m.box((-X, 0, -Z), (X, .9, Z), TUFF, W)
    m.box((-X + .2, .9, -Z + .2), (X - .2, 3.0, 1.0), PLASTER, W)                # cella
    for x in (-X + .35, X - .35):
        for z in (1.6, 2.5):
            cylinder(m, x, z, .17, .9, 3.2, PLASTER, n=8)
    gable(m, -X, X, -Z + .2, Z - .1, 3.2, 4.9, .5, TILE, TILE_D, RED)
    return m


def temple_tuscan():
    """Tuscan temple (Jupiter Optimus Maximus on the Capitoline): high tuff podium, steps, a deep porch of two rows of columns, three cellae behind, very wide eaves."""
    m = Mesh(); X, Z = 7.5, 10.0
    m.box((-X, 0, -Z), (X, 2.6, Z), TUFF, W)
    m.box((-X - .3, 0, -Z - .3), (X + .3, .35, Z + .3), TUFF_D, W)
    for i in range(5):                                                           # front steps
        m.box((-2.2 - .1 * i, 0, Z + .2 + .5 * (4 - i) * 0 + (5 - i) * .3), (2.2 + .1 * i, .5 * (i + 1), Z + 1.8 + .0), TUFF if i % 2 else TUFF_D, W)
    for k, (cx, w) in enumerate(((-4.4, 2.9), (0, 2.9), (4.4, 2.9))):            # three cellae
        m.box((cx - w, 2.6, -Z + .4), (cx + w, 8.4, 1.2), PLASTER if k != 1 else (.90, .85, .74, 1), W)
    for row in (4.8, 8.2):
        for x in (-6.2, -3.1, 0, 3.1, 6.2):
            cylinder(m, x, row, .38, 2.6, 8.6, PLASTER, n=10)
            m.box((x - .5, 8.55, row - .5), (x + .5, 8.85, row + .5), TUFF, W)
    gable(m, -X, X, -Z, Z, 8.85, 12.4, 1.9, TILE, TILE_D, RED)
    m.box((-X - 1.9, 12.3, -.1), (X + 1.9, 12.55, .1), TILE_D, W)
    m.tri(V(-X, 8.85, Z + 1.9), V(X, 8.85, Z + 1.9), V(0, 12.4, Z + 1.9), V(0, 0, 1), RED, W)   # painted pediment gable
    for x in (-X - 1.5, X + 1.5): m.box((x - .35, 12.1, Z + 1.7), (x + .35, 12.7, Z + 2.0), (.75, .42, .25, 1), W)   # antefix acroteria
    return m


def temple_round():
    """Round hut-form temple (Vesta): a small podium and a drum with a conical thatch roof."""
    m = Mesh()
    cylinder(m, 0, 0, 3.2, 0, .6, TUFF, n=14, cap_top=True)
    cylinder(m, 0, 0, 2.5, .6, 3.0, PLASTER, n=14)
    cone(m, 0, 0, 3.0, 2.9, 6.6, THATCH, THATCH_D, n=14)
    m.box((-.5, .6, 2.35), (.5, 2.2, 2.55), DARK, W)
    return m


def hall():
    """Long hall (the Regia): a tiled gabled house on a stone base with a porch."""
    m = Mesh(); X, Z = 6.0, 3.6
    m.box((-X, 0, -Z), (X, .5, Z), TUFF, W)
    m.box((-X + .1, .5, -Z + .1), (X - .1, 3.0, Z - .1), HALL_WALL, W)
    for x in (-X + .4, -X / 2, 0, X / 2, X - .4): m.box((x - .13, .5, Z - .1), (x + .13, 3.0, Z + .35), TIMBER, W)
    gable(m, -X, X, -Z, Z, 3.0, 5.6, .6, TILE, TILE_D, RED)
    m.box((-X - .6, 5.55, -.08), (X + .6, 5.65, .08), TILE_D, W)
    return m


# ------------------------------------------------------------------------------------------ ground and defense
def path():
    """A strip of packed earth, 8 m long, 3 m wide, along x, cut every metre so it follows the terrain."""
    m = Mesh()
    for i in range(8):
        x0 = -4 + i
        for z0 in (-1.5, 0):
            m.quad(V(x0, .06, z0), V(x0 + 1, .06, z0), V(x0 + 1, .06, z0 + 1.5), V(x0, .06, z0 + 1.5), V(0, 1, 0), (.40, .32, .20, 1) if (i + (z0 < 0)) % 2 else (.35, .28, .18, 1), W)
    return m


def plaza():
    """Beaten-earth open ground, 18 m square (a forum or market), 2 m paving cells in two tones so it follows the terrain."""
    m = Mesh()
    for i in range(9):
        for j in range(9):
            x0, z0 = -9 + 2 * i, -9 + 2 * j
            edge = i in (0, 8) or j in (0, 8)
            col = TUFF_D if edge else ((.42, .34, .21, 1) if (i + j) % 2 else (.37, .30, .19, 1))
            m.quad(V(x0, .07, z0), V(x0 + 2, .07, z0), V(x0 + 2, .07, z0 + 2), V(x0, .07, z0 + 2), V(0, 1, 0), col, W)
    return m


def rampart(palisade=True):
    """8 m of grassed earthen bank (agger) with a ditch in front and a timber palisade on top, along x, outside toward +Z. Cut into 2 m slices so it follows the terrain."""
    m = Mesh()
    prof = [(-3.6, 0), (-2.4, 1.2), (-1.2, 3.0), (.2, 3.0), (1.6, 1.0), (2.6, -.4), (3.8, -.3)]
    for x0 in (-4, -2, 0, 2):
        x1 = x0 + 2
        for (z0, y0), (z1, y1) in zip(prof, prof[1:]):
            col = EARTH if y1 >= y0 else EARTH_D
            m.quad(V(x0, y0, z0), V(x1, y0, z0), V(x1, y1, z1), V(x0, y1, z1), V(0, 1, 0), col, W)
        if palisade:
            m.box((x0, 3.9, -.2), (x1, 4.0, .26), (.22, .14, .08, 1), W)
    if palisade:
        for i in range(17):
            x = -4 + i * .5
            m.box((x - .13, 3.0, -.1), (x + .13, 5.0 - .25 * (i % 3), .16), TIMBER, W)
    return m


def gate_tower():
    """A timber gate: two plank towers with a platform and gabled roofs, and a lintel over a 3 m opening."""
    m = Mesh()
    for sx in (-1, 1):
        cx = sx * 2.6
        m.box((cx - 1.1, 0, -1.3), (cx + 1.1, 4.8, 1.3), TIMBER, W)
        m.box((cx - 1.4, 4.8, -1.6), (cx + 1.4, 5.1, 1.6), (.30, .20, .11, 1), W)
        gable(m, cx - 1.1, cx + 1.1, -1.3, 1.3, 5.9, 7.1, .3, THATCH, THATCH_D, TIMBER)
        m.box((cx - 1.1, 5.1, -1.3), (cx + 1.1, 5.9, -1.15), TIMBER, W)
    m.box((-1.5, 3.4, -.5), (1.5, 4.8, .5), TIMBER, W)
    return m


def oquad(m, a, b, c, d, out, color):
    """A quad whose winding is fixed to face the direction `out`."""
    a, b, c, d = V(*a), V(*b), V(*c), V(*d)
    if np.dot(np.cross(b - a, c - a), V(*out)) < 0: a, b, c, d = d, c, b, a
    m.quad(a, b, c, d, V(*out), color, W)


# ------------------------------------------------------------------------------------------ bridges
def bridge_wood():
    """Timber trestle bridge, 8 m along x and 3.2 m wide: pile bents driven into the river bed, cross-beams, a plank deck at 1.5 m and a post-and-rail parapet.
    (The Pons Sublicius, the first Tiber bridge, was all timber, built without iron, 6th c. BCE.)"""
    m = Mesh(); DECK = 1.5
    for x in (-3.4, 0, 3.4):                                    # pile bents: two posts and a cap beam
        for z in (-1.25, 1.25):
            m.box((x - .22, -1.4, z - .22), (x + .22, DECK - .2, z + .22), TIMBER, W)
        m.box((x - .3, DECK - .55, -1.55), (x + .3, DECK - .25, 1.55), (.33, .21, .11, 1), W)
        m.box((x - .1, -.2, -1.25), (x + .1, DECK - .6, 1.25), (.33, .21, .11, 1), W)     # sway brace across
    for z in (-1.0, 0, 1.0):                                    # stringers
        m.box((-4, DECK - .25, z - .15), (4, DECK, z + .15), (.36, .24, .13, 1), W)
    for i in range(16):                                         # deck planks, alternating tone
        x = -4 + i * .5
        m.box((x + .02, DECK, -1.6), (x + .48, DECK + .12, 1.6), (.40, .30, .18, 1) if i % 2 else (.35, .26, .16, 1), W)
    for z in (-1.55, 1.55):
        for x in (-3.8, -1.9, 0, 1.9, 3.8):
            m.box((x - .1, DECK, z - .1), (x + .1, DECK + 1.0, z + .1), TIMBER, W)
        m.box((-4, DECK + .85, z - .08), (4, DECK + 1.0, z + .08), TIMBER, W)
    return m


def bridge_stone():
    """Stone arch bridge, 8 m along x and 3.6 m wide: one round travertine arch (5.8 m span) between abutments, a paved deck at 3.4 m, parapets.
    (Rome's first stone-arched bridges date from the 2nd c. BCE, the Pons Aemilius of 179 BCE on stone piers; the arch itself came from the Etruscans.)"""
    m = Mesh(); R0, R1, DECK, Zh = 2.9, 3.5, 3.4, 1.8
    STONE, STONE_D, STONE_L = (.60, .58, .52, 1), (.46, .45, .41, 1), (.70, .68, .60, 1)
    n = 10
    for k in range(n):                                          # the arch ring: voussoirs, alternating tone
        a0, a1 = math.pi * k / n, math.pi * (k + 1) / n
        p = lambda a, r, z: (r * math.cos(a), r * math.sin(a), z)
        col = STONE_L if k % 2 else STONE
        oquad(m, p(a0, R0, -Zh), p(a1, R0, -Zh), p(a1, R1, -Zh), p(a0, R1, -Zh), (0, 0, -1), col)
        oquad(m, p(a0, R0, Zh), p(a1, R0, Zh), p(a1, R1, Zh), p(a0, R1, Zh), (0, 0, 1), col)
        mid = (a0 + a1) / 2
        oquad(m, p(a0, R0, -Zh), p(a1, R0, -Zh), p(a1, R0, Zh), p(a0, R0, Zh), (-math.cos(mid), -math.sin(mid), 0), STONE_D)   # soffit
    for i in range(16):                                         # spandrel and abutment fill from the arch up to the deck
        x0 = -4 + i * .5; xm = abs(x0 + .25)
        ybot = math.sqrt(R1 * R1 - xm * xm) - .02 if xm < R1 else -.6
        m.box((x0, ybot, -Zh), (x0 + .5, DECK, Zh), STONE_D if xm > R1 else STONE, W)
    m.box((-4, DECK, -Zh), (4, DECK + .08, Zh), (.40, .39, .35, 1), W)                    # paving
    for z in (-Zh, Zh - .35):
        m.box((-4, DECK, z), (4, DECK + .8, z + .35), STONE_L, W)                           # parapets
    return m


def tree():
    """A small tree: for the olives and oaks around a town."""
    m = Mesh()
    cylinder(m, 0, 0, .22, 0, 2.2, (.36, .26, .15, 1), n=6)
    for y, r in ((2.0, 1.6), (3.0, 1.3), (3.9, .8)):
        m.loft([dict(pts=ellipse((0, y - .5, 0), r, r, 8), color=(.30, .40, .20, 1), w=W), dict(pts=ellipse((0, y + .5, 0), r * .55, r * .55, 8), color=(.36, .46, .24, 1), w=W)], cap_last=True)
    return m


PIECES = [("hut_round", hut_round, 220), ("hut_oval", hut_oval, 220), ("house_timber", house_timber, 300), ("house_tile", house_tile, 300),
          ("courtyard_house", courtyard_house, 400), ("granary", granary, 200), ("shrine", shrine, 300), ("temple_tuscan", temple_tuscan, 700),
          ("temple_round", temple_round, 250), ("hall", hall, 300), ("path", path, 100), ("plaza", plaza, 400), ("rampart", rampart, 600),
          ("gate_tower", gate_tower, 300), ("tree", tree, 250), ("bridge_wood", bridge_wood, 600), ("bridge_stone", bridge_stone, 700)]

if __name__ == "__main__":
    ok = True
    for name, fn, budget in PIECES:
        ok &= write_piece(fn(), f"site_{name}_latin", budget)
    finish(ok)
