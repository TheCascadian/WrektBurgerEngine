#!/usr/bin/env python3
"""Spartan and Egyptian kits, built on the Greek and Roman generator (gear_greek_roman.py).

Spartan (SPA, replaces the generic Greek pieces for the tunic, skirt, helmet and boots): crimson tunic and a crimson cloak (phoinikis) hanging down the back,
no body armor, a bronze pilos cap, bare feet with only ankle cords. The sword (xiphos), the dory and the aspis stay the Greek pieces.
Egyptian (EGY, Saite and Persian-period machimoi): a linen corselet with a broad collar, a pleated linen kilt with a stiff front apron, a striped
headcloth with short lappets, sandals, a spear, a curved sheathed khopesh sword on the left hip, and a tall arch-topped hide shield.
OPEN: the Egyptian colors and the hide patches are design choices; the Spartan cloak is the classic Spartan red.

Output: out/gear_<piece>_spa.glb (tunic, trousers, helmet, boots) and out/gear_<piece>_egy.glb (all seven)."""
import math
from gear_lib import *
import gear_greek_roman as G

CRIMSON = (.59, .14, .16, 1); CRIMSON_D = (.45, .10, .12, 1)
LINEN = (.88, .85, .74, 1); LINEN_D = (.78, .74, .62, 1); GOLD = (.80, .62, .18, 1); BLUE = (.14, .30, .55, 1); TURQ = (.20, .55, .55, 1)
HIDE = (.86, .80, .66, 1); HIDE_P = (.40, .26, .15, 1); WOODR = (.42, .28, .14, 1)
HIPS = [("hips", 1.0)]


def torso(m, colors, top=len(G.TORSO) - 2, cap=False):
    rings = []
    for k, (y, hw, zf, zb) in enumerate(G.TORSO[:top + 1]):
        rings.append(dict(pts=ellipse((0, y, (zf + zb) / 2), hw, (zf - zb) / 2, 14), color=colors(k, y), w=G.torso_w))
    m.loft(rings, cap_last=cap)


# ------------------------------------------------------------------------------------------ Spartan
def spartan_tunic():
    m = Mesh()
    torso(m, lambda k, y: CRIMSON if k % 2 else CRIMSON_D)
    # the cloak: a panel from the shoulders down the back, widening, drawn behind the body and hung from the chest
    ys = [1.46, 1.36, 1.20, 1.04, .90, .78]
    half = [.19, .25, .27, .28, .29, .30]
    zs = [-.115, -.150, -.175, -.190, -.205, -.215]
    cw = lambda p: blend(("chest", smooth(1.1, 1.4, p[1])), ("spine", .6 * (1 - smooth(1.1, 1.4, p[1])) * smooth(.95, 1.1, p[1])), ("hips", 1 - smooth(.95, 1.2, p[1])))
    for i in range(len(ys) - 1):
        for j in range(8):
            x0, x1 = (-1 + 2 * j / 8), (-1 + 2 * (j + 1) / 8)
            P = lambda x, i: np.array((x * half[i], ys[i], zs[i] + .02 * (x * x)))
            col = CRIMSON if (i + j) % 2 else CRIMSON_D
            if i == len(ys) - 2: col = (.30, .06, .08, 1)
            m.quad(P(x0, i), P(x1, i), P(x1, i + 1), P(x0, i + 1), np.array((0, 0, -1.0)), col, cw)
            m.quad(P(x0, i), P(x0, i + 1), P(x1, i + 1), P(x1, i), np.array((0, 0, 1.0)), CRIMSON_D, cw)
    # a bronze brooch pinning the cloak at the right shoulder, and a leather strap across the chest
    m.box((-.03, 1.42, .09), (.03, 1.455, .12), G.BRONZE, [("chest", 1.0)])
    return m


def spartan_skirt():
    return G.build_skirt(CRIMSON)


def spartan_helmet():
    """Bronze pilos: a plain conical cap with a thin rim, no crest."""
    W = [("headgear", 1.0)]
    m = Mesh(); CZ, RX, RZ = .02, .104, .120
    th = [2 * math.pi * i / 14 for i in range(14)]
    rings = [dict(pts=np.array([(RX * s * math.cos(t), y, CZ + RZ * s * math.sin(t)) for t in th]), color=col, w=W)
             for y, s, col in ((1.665, 1.04, G.BRONZE_D), (1.690, 1.0, G.BRONZE), (1.740, .96, G.BRONZE), (1.800, .72, G.BRONZE), (1.860, .36, G.BRONZE), (1.900, .0, G.BRONZE))]
    rings[-1]["pts"] = np.array([(.002 * math.cos(t), 1.905, CZ + .002 * math.sin(t)) for t in th])
    m.loft(rings, cap_last=True)
    return m


def spartan_boots():
    """Bare feet with cords at the ankle (Spartan soldiers marched without shoes)."""
    def w(p):
        up = smooth(.05, .14, p[1])
        return blend(("shin.L", up), ("foot.L", 1 - up))
    m = Mesh()
    m.loft([dict(pts=ellipse((.089, y, -.004), .064, .064, 8), color=G.LEATHER, w=w) for y in (.06, .085)])
    out = Mesh(); out.extend(m); out.extend(m.mirrored())
    return out


# ------------------------------------------------------------------------------------------ Egyptian
def egypt_tunic():
    m = Mesh()
    torso(m, lambda k, y: GOLD if k == 0 else (LINEN if k % 2 else LINEN_D), top=7)     # a corselet to the armpits
    # shoulder straps over a bare upper chest, and the broad collar (usekh) in bands of blue, gold and turquoise
    for sx in (1, -1):
        m.box((sx * .10 - .03, 1.40, -.11), (sx * .10 + .03, 1.475, .13), LINEN, [("chest", 1.0)])
    cols = [BLUE, GOLD, TURQ, GOLD, BLUE]
    rings = []
    for i, col in enumerate(cols):
        y = 1.505 - .035 * i; hw = .115 + .028 * i; hd = .082 + .026 * i
        rings.append(dict(pts=ellipse((0, y, .004), hw, hd, 14), color=col, w=[("chest", .8), ("neck", .2)]))
    m.loft(rings)
    return m


def egypt_kilt():
    """Shendyt: a wrapped pleated linen kilt to above the knee, with a stiff triangular apron in front."""
    def w(p):
        low = smooth(.9, .66, p[1]); side = "thigh.L" if p[0] >= 0 else "thigh.R"
        return blend(("hips", 1 - .8 * low), (side, .8 * low))
    m = Mesh()
    rows = [(1.00, .200, .142), (.90, .208, .148), (.80, .218, .154), (.72, .226, .160), (.68, .230, .163)]
    rings = [dict(pts=ellipse((0, y, -.005), hw, hd, 16), color=(LINEN if k % 2 else LINEN_D) if k < len(rows) - 1 else LINEN_D, w=w) for k, (y, hw, hd) in enumerate(rows)]
    m.loft(rings, cap_first=True)
    # apron: a flared pleated triangle on the front (gold edge)
    for i in range(5):
        x0, x1 = -.12 + .048 * i, -.12 + .048 * (i + 1)
        m.quad(np.array((x0 * .55, 1.00, .156)), np.array((x1 * .55, 1.00, .156)), np.array((x1, .56, .205)), np.array((x0, .56, .205)), np.array((0, 0, 1.0)), LINEN if i % 2 else HIDE, w)
    m.quad(np.array((-.125, .56, .205)), np.array((.125, .56, .205)), np.array((.125, .545, .207)), np.array((-.125, .545, .207)), np.array((0, -1, 0.0)), GOLD, w)
    return m


def egypt_belt():
    """Broad belt with a gold plate, and a curved khopesh sword in a sheath on the left hip."""
    m = Mesh()
    m.loft([dict(pts=ellipse((0, y, -.007), hw, hd, 14), color=c, w=HIPS) for y, hw, hd, c in ((.93, .205, .141, WOODR), (.97, .212, .146, GOLD), (1.02, .207, .142, WOODR))], cap_first=True, cap_last=True)
    m.box((-.035, .93, .144), (.035, 1.02, .152), GOLD, HIPS)
    sw = lambda p: blend(("hips", smooth(.70, .90, p[1])), ("thigh.L", 1 - smooth(.70, .90, p[1])))
    # the sheath follows the sickle curve: straight handle upward, then the blade curving out and down
    path = [(.225, .98 + .12 * t, .02) for t in (1, .5, 0)] + [(.225 + .05 * math.sin(a), .96 - .30 * (1 - math.cos(a * .9)) - .10 * a, .02 + .04 * a) for a in (.0, .35, .7, 1.0, 1.3, 1.6)]
    pts = [np.array(p) for p in path]
    segs = [dict(pts=ellipse(p, .020 if i > 2 else .014, .012, 6, u=(1, 0, 0), v=(0, 0, 1)), color=(BONE if i < 3 else WOODR if i < 8 else GOLD), w=sw) for i, p in enumerate(pts)]
    m.loft(segs, cap_first=True, cap_last=True)
    return m


BONE = G.BONE


def egypt_boots():
    """Papyrus sandals: a sole with an ankle strap and an instep strap."""
    CX = .089
    def w(p):
        up = smooth(.05, .12, p[1]); toe = smooth(.12, .20, p[2]) if p[1] < .08 else 0
        return blend(("shin.L", up), ("foot.L", (1 - up) * (1 - .6 * toe)), ("toe.L", (1 - up) * .6 * toe))
    m = Mesh()
    m.loft([dict(pts=ellipse((CX, y, .072), .062, .150, 12), color=(.62, .52, .30, 1), w=w) for y in (.0, .010)], cap_first=True, cap_last=True)
    m.loft([dict(pts=ellipse((CX, y, -.004), .066, .066, 10), color=G.LEATHER, w=w) for y in (.07, .095)])
    m.loft([dict(pts=ellipse((CX, y, .095), .062, .018, 10), color=G.LEATHER, w=w) for y in (.040, .058)])
    out = Mesh(); out.extend(m); out.extend(m.mirrored())
    return out


def egypt_helmet():
    """Striped headcloth (nemes style) in blue and linen, with short lappets at the sides and a gold brow band."""
    W = [("headgear", 1.0)]
    CZ, RX, RZ = .02, .106, .122
    th = [2 * math.pi * i / 16 for i in range(16)]
    def edge(t):
        s, cc = math.sin(t), math.cos(t)
        return 1.735 - .08 * (1 - s) / 2 - .05 * abs(cc) * (1 + s) / 2 - .05 * max(0, -s)
    m = Mesh()
    rings = [dict(pts=np.array([(RX * math.cos(t), edge(t), CZ + RZ * math.sin(t)) for t in th]), color=GOLD, w=W)]
    for i, (y, s) in enumerate(((1.76, 1.0), (1.79, .95), (1.82, .84), (1.85, .62), (1.875, .30))):
        rings.append(dict(pts=np.array([(RX * s * math.cos(t), y, CZ + RZ * s * math.sin(t)) for t in th]), color=BLUE if i % 2 == 0 else LINEN, w=W))
    m.loft(rings, cap_last=True)
    for sx in (1, -1):    # lappets over the shoulders
        for k, y in enumerate((1.60, 1.52)):
            m.box((sx * .11 - .025, y, CZ + .02), (sx * .11 + .025, y + .08, CZ + .075), BLUE if k == 0 else LINEN, W)
    return m


def egypt_shield():
    """Tall, arch-topped hide shield (authored along the left forearm like the others), hide with brown patches and a wooden rim."""
    FORE = JOINT["forearm.L"]
    AX = JOINT["hand.L"] - FORE; AX = AX / np.linalg.norm(AX)
    WD = np.array((-AX[1], AX[0], 0.0)); WD = WD / np.linalg.norm(WD)
    NR = np.array((0, 0, 1.0))
    HW, HH, THICK, BULGE = .20, .42, .022, .035
    CT = FORE + AX * .10 + NR * .10
    NU, NV = 8, 14
    Wt = [("forearm.L", 1.0)]

    def surface(u, v, front=True):
        a = (u - .5) * 2; b = (v - .5) * 2
        width = 1.0 if b < .35 else math.sqrt(max(0.0, 1 - ((b - .35) / .65) ** 2))   # the arched top
        x = a * HW * width; y = b * HH
        z = BULGE * (1 - a * a * width * width) + (THICK if front else -THICK) / 2
        return CT + WD * x + AX * y + NR * z

    def tint(i, j):
        if i in (0, NU - 1) or j in (0, NV - 1): return WOODR
        return HIDE_P if ((i // 2) * 2 + (j // 3)) % 5 == 0 else HIDE

    m = Mesh()
    m.grid(lambda u, v: surface(u, v, True), NU, NV, np.array((0, 0, 1.0)), tint, Wt)
    m.grid(lambda u, v: surface(u, v, False), NU, NV, np.array((0, 0, -1.0)), (.30, .20, .11, 1), Wt)
    for f, o in ((lambda t: surface(0, t), -WD), (lambda t: surface(1, t), WD), (lambda t: surface(t, 0), -AX), (lambda t: surface(t, 1), AX)):
        for i in range(NU):
            p0, p1 = f(i / NU), f((i + 1) / NU)
            m.quad(p0, p1, p1 - NR * THICK, p0 - NR * THICK, np.asarray(o, float), WOODR, Wt)
    m.box(tuple(CT + (-.05, -.02, -.05)), tuple(CT + (.05, .02, -.02)), (.30, .20, .11, 1), Wt)
    return m


if __name__ == "__main__":
    ok = True
    jobs = [("spa", "tunic", spartan_tunic(), 800), ("spa", "trousers", spartan_skirt(), 400), ("spa", "helmet", spartan_helmet(), 400), ("spa", "boots", spartan_boots(), 300),
            ("egy", "tunic", egypt_tunic(), 800), ("egy", "trousers", egypt_kilt(), 500), ("egy", "belt", egypt_belt(), 500), ("egy", "boots", egypt_boots(), 400),
            ("egy", "helmet", egypt_helmet(), 500), ("egy", "spear", G.build_spear(True), 300), ("egy", "shield", egypt_shield(), 800)]
    for tag, piece, mesh, budget in jobs:
        ok &= write_piece(mesh, f"gear_{piece}_{tag}", budget)
    finish(ok)
