#!/usr/bin/env python3
"""Hoplite kit for the Greek city-states, Macedon and Rome, one set of pieces per country tag, built directly in engine body space.

Greek (ATH SPA COR ARG THB THS CRT NAX CRO SYR PNT MAC): linen linothorax with two layers of pteryges and shoulder flaps, short chiton skirt,
Corinthian helmet with a horsehair crest, bronze greaves over sandals, a xiphos on the left hip, dory spear, aspis shield.
Roman (ROM): Italic hoplite of the early Republic: bronze breastplate over a red wool tunic, Etruscan-Corinthian helmet with a low crest,
bronze greaves over closed shoes, a short sword on the right hip, hasta, clipeus.

Output: out/gear_<piece>_<tag>.glb for piece in tunic, trousers (the chiton skirt), belt (belt and sword), boots (greaves and footwear), helmet, spear, shield.
The shield face takes the country's map color. OPEN: real polis shield devices were painted; none are modeled here (no emblems in this engine's kits)."""
import math
import numpy as np
from gear_lib import *

# ---- per country: chiton (or tunic) color, shield face (the country's map color), crest color ----
def c(r, g, b): return (r / 255, g / 255, b / 255, 1)

LINEN = (.84, .80, .68, 1)
GREEK = {
    "ATH": (LINEN, c(70, 120, 200), (.10, .09, .09, 1)), "SPA": (c(150, 36, 40), c(170, 40, 50), c(150, 36, 40)),
    "COR": (LINEN, c(90, 170, 225), (.85, .84, .80, 1)), "ARG": (LINEN, c(55, 85, 160), (.10, .09, .09, 1)),
    "THB": (LINEN, c(150, 110, 200), (.60, .12, .12, 1)), "THS": (c(150, 120, 80), c(100, 190, 170), (.36, .22, .12, 1)),
    "CRT": (c(170, 150, 110), c(80, 150, 120), (.36, .22, .12, 1)), "NAX": (LINEN, c(130, 200, 235), (.85, .84, .80, 1)),
    "CRO": (LINEN, c(110, 130, 190), (.10, .09, .09, 1)), "SYR": (LINEN, c(190, 160, 225), (.60, .12, .12, 1)),
    "PNT": (c(150, 135, 100), c(120, 145, 95), (.36, .22, .12, 1)), "MAC": (c(120, 70, 50), c(200, 80, 60), (.60, .12, .12, 1)),
}
ROMAN = {"ROM": (c(140, 40, 34), c(95, 140, 32), (.62, .13, .11, 1))}
BRONZE = (.66, .48, .20, 1); BRONZE_D = (.46, .32, .13, 1); LEATHER = (.38, .24, .13, 1); LEATHER_D = (.24, .15, .09, 1); IRON = (.60, .61, .64, 1)
WOOD = (.50, .34, .18, 1); WRAP = (.30, .18, .10, 1); BONE = (.82, .78, .66, 1)

# torso rings: y, half-width, front z, back z (the Olmec tunic's, 1-2 cm outside the body surface)
TORSO = [(.86, .236, .154, -.146), (.92, .226, .146, -.144), (.98, .210, .131, -.147), (1.05, .188, .124, -.124), (1.11, .170, .124, -.114),
         (1.17, .176, .130, -.118), (1.25, .204, .140, -.128), (1.33, .236, .150, -.132), (1.40, .254, .134, -.118), (1.46, .200, .102, -.102), (1.50, .124, .080, -.078)]


def torso_w(p):
    y = p[1]
    if y > 1.38: return blend(("chest", .85), ("neck", .15 * smooth(1.44, 1.50, y)), ("spine", .15 * (1 - smooth(1.38, 1.44, y))))
    if y > 1.15: return blend(("chest", smooth(1.15, 1.35, y)), ("spine", 1 - smooth(1.15, 1.35, y) * .7))
    if y > .9: return blend(("spine", smooth(.9, 1.15, y)), ("hips", 1 - smooth(.9, 1.15, y)))
    return blend(("hips", 1))


HIPS = [("hips", 1.0)]


def build_tunic(cloth, roman):
    m = Mesh(); rings = []
    for k, (y, hw, zf, zb) in enumerate(TORSO):
        if roman: col = BRONZE if 1.15 < y < 1.46 else cloth     # breastplate over a wool tunic
        else: col = BRONZE_D if k == 0 else (LINEN if k % 2 else (.76, .72, .60, 1))   # linen layers in two tones
        rings.append(dict(pts=ellipse((0, y, (zf + zb) / 2), hw, (zf - zb) / 2, 14), color=col, w=torso_w))
    m.loft(rings, cap_last=True)
    # pteryges: two layers of hanging leather-and-linen strips around the hips
    for layer, (top, bot, rad, n, col) in enumerate(((.97, .80, 1.10, 14, LEATHER), (.93, .74, 1.20, 14, cloth if roman else LINEN))):
        for i in range(n):
            a0, a1 = 2 * math.pi * (i + .06 * layer) / n, 2 * math.pi * (i + .94 - .06 * layer) / n
            P = lambda a, y, r: np.array((.215 * r * math.cos(a), y, -.005 + .150 * r * math.sin(a)))
            out = P((a0 + a1) / 2, 0, 1) - np.array((0, 0, -.005))
            out[1] = 0
            m.quad(P(a0, top, 1.04), P(a1, top, 1.04), P(a1, bot, rad), P(a0, bot, rad), out, col if i % 2 else LEATHER_D if layer == 0 else col, HIPS)
    # shoulder flaps (epomides) folded over the shoulders: thin boxes bridging chest front to back
    for sx in (1, -1):
        m.box((sx * .09 - .04, 1.455, -.09), (sx * .09 + .04, 1.480, .11), BRONZE if roman else LINEN, [("chest", 1.0)])
    if roman:   # three round plates on the chest and the belly (an early Italic breast guard)
        for dx, dy in ((-.07, 1.30), (.07, 1.30), (0, 1.20)):
            m.loft([dict(pts=ellipse((dx, dy, TORSO[7][2] + .004 + z), .045 - z * 2, .045 - z * 2, 10), color=BRONZE if z == 0 else BRONZE_D, w=torso_w)
                    for z in (0, .012)], cap_last=True)
    return m


def build_skirt(cloth):
    """The chiton: a short wool skirt from the waist to above the knee, belted at the waist."""
    def w(p):
        low = smooth(.88, .62, p[1])
        side = "thigh.L" if p[0] >= 0 else "thigh.R"
        return blend(("hips", 1 - .85 * low), (side, .85 * low))
    m = Mesh(); rows = [(1.00, .206, .146), (.90, .214, .152), (.80, .226, .160), (.70, .238, .166), (.64, .244, .170)]
    hem = (cloth[0] * .8, cloth[1] * .8, cloth[2] * .8, 1)
    rings = [dict(pts=ellipse((0, y, -.005), hw, hd, 16), color=hem if k == len(rows) - 1 else cloth, w=w) for k, (y, hw, hd) in enumerate(rows)]
    m.loft(rings, cap_first=True)
    return m


def build_belt(roman):
    """Leather belt, and the sheathed short sword hung from it: a xiphos on the left hip (Greek), a short sword on the right (Roman)."""
    m = Mesh()
    belt = [dict(pts=ellipse((0, y, -.007), hw, hd, 14), color=LEATHER_D, w=HIPS) for y, hw, hd in ((.93, .205, .141), (.97, .212, .146), (1.01, .207, .142))]
    m.loft(belt, cap_first=True, cap_last=True)
    m.box((-.03, .93, .144), (.03, 1.01, .152), BRONZE, HIPS)          # buckle plate
    sx = -1 if roman else 1
    side = "thigh.R" if roman else "thigh.L"
    sw = lambda p: blend(("hips", smooth(.70, .90, p[1])), (side, 1 - smooth(.70, .90, p[1])))
    X, Z = sx * .225, .02
    length = .50 if roman else .46
    # scabbard: a tapering flat leather case, hanging along the outside of the thigh, with a bronze chape and mouth
    top, tip = .95, .95 - length
    hw = lambda t: .028 - .014 * t          # half-width across the case
    segs = [dict(pts=ellipse((X, top - length * t, Z), hw(t), .014, 8), color=LEATHER if t < .94 else BRONZE, w=sw) for t in (0, .12, .5, .9, 1.0)]
    m.loft(segs, cap_last=True)
    m.loft([dict(pts=ellipse((X, y, Z), hw(0) + .005, .019, 8), color=BRONZE, w=sw) for y in (top - .004, top - .035)])
    # grip, guard and pommel above the mouth
    m.loft([dict(pts=ellipse((X, y, Z), r, r * .8, 8), color=col, w=sw) for y, r, col in ((top, .014, BONE), (top + .08, .014, BONE), (top + .095, .02, BRONZE))], cap_last=True)
    m.box((X - .04, top + .0, Z - .012), (X + .04, top + .015, Z + .012), BRONZE, sw)
    return m


def build_boots(roman, cloth):
    """Bronze greaves from the knee to the ankle over sandals (Greek) or closed shoes (Roman)."""
    CX = .089
    def w(p):
        up = smooth(.05, .14, p[1]); toe = smooth(.12, .20, p[2]) if p[1] < .08 else 0
        return blend(("shin.L", up), ("foot.L", (1 - up) * (1 - .6 * toe)), ("toe.L", (1 - up) * .6 * toe))
    m = Mesh()
    # greave: wide at the calf, narrow at the ankle, with a darker lower edge
    G = [(.13, .052, .054, BRONZE_D), (.20, .058, .060, BRONZE), (.32, .068, .072, BRONZE), (.42, .070, .074, BRONZE), (.485, .068, .070, BRONZE_D)]
    m.loft([dict(pts=ellipse((CX, y, -.004), rx, rz, 12), color=col, w=lambda p: blend(("shin.L", 1))) for y, rx, rz, col in G], cap_first=False)
    if roman:   # closed leather shoe up to the ankle
        R = [(.000, .075, .058, .140, LEATHER_D), (.018, .072, .060, .142, LEATHER), (.050, .060, .062, .130, LEATHER), (.090, .030, .066, .100, LEATHER), (.130, .008, .060, .070, LEATHER)]
        m.loft([dict(pts=ellipse((CX, y, cz), rx, rz, 12), color=col, w=w) for y, cz, rx, rz, col in R], cap_first=True)
    else:       # leather sole with an ankle strap and an instep strap
        sole = [dict(pts=ellipse((CX, y, .072), .062, .150, 12), color=LEATHER_D, w=w) for y in (.0, .010)]
        m.loft(sole, cap_first=True, cap_last=True)
        m.loft([dict(pts=ellipse((CX, y, -.004), .066, .066, 10), color=LEATHER, w=w) for y in (.07, .095)])
        m.loft([dict(pts=ellipse((CX, y, .095), .062, .018, 10), color=LEATHER, w=w) for y in (.040, .058)])
    out = Mesh(); out.extend(m); out.extend(m.mirrored())
    return out


def build_helmet(roman, crest):
    """Bronze helmet. Greek: Corinthian with cheek guards and a nasal bar, a horsehair crest along the top. Roman: lower dome, a short crest comb."""
    W = [("headgear", 1.0)]
    CZ, RX, RZ = .02, .106, .122
    th = [2 * math.pi * i / 16 for i in range(16)]
    def edge(t):
        s, cc = math.sin(t), math.cos(t)
        return 1.725 - .09 * (1 - s) / 2 - .05 * abs(cc) * (1 + s) / 2 - .04 * max(0, -s)   # high on the brow, low at the ears and nape
    m = Mesh()
    rings = [dict(pts=np.array([(RX * math.cos(t), edge(t), CZ + RZ * math.sin(t)) for t in th]), color=BRONZE_D, w=W)]
    top = 1.84 if roman else 1.88
    for y, s in ((1.76, 1.0), (1.80, .94), (top - .04, .66), (top, .30)):
        rings.append(dict(pts=np.array([(RX * s * math.cos(t), y, CZ + RZ * s * math.sin(t)) for t in th]), color=BRONZE, w=W))
    m.loft(rings, cap_last=True)
    # cheek guards hang forward of the ears on both sides, and a nasal bar between the eyes (Greek helmets close around the face)
    for sx in (1, -1):
        m.box((sx * .096 - .008, 1.585, CZ + .045), (sx * .096 + .008, 1.72, CZ + .12), BRONZE, W)
    if not roman:
        m.box((-.007, 1.62, CZ + .120), (.007, 1.73, CZ + .132), BRONZE, W)
    # crest: a stand along the top and a horsehair brush (Greek tall and long, Roman a low comb)
    h, lo, hi = (.05, -.10, .08) if roman else (.11, -.15, .10)
    m.box((-.006, top - .01, CZ + lo), (.006, top + .012, CZ + hi), BRONZE, W)
    steps = 7
    for i in range(steps):
        z0 = CZ + lo + (hi - lo) * i / steps; z1 = CZ + lo + (hi - lo) * (i + 1) / steps
        hh = h * (.65 + .35 * math.sin(math.pi * (i + .5) / steps))
        m.box((-.012, top + .012, z0), (.012, top + .012 + hh, z1), crest, W)
    return m


def build_spear(roman):
    """Dory (Greek, long, leaf head, bronze butt spike) or hasta (Roman, shorter): held in the right hand."""
    W = [("hand.R", 1.0)]
    X, Z = JOINT["hand.R"][0], JOINT["hand.R"][2]
    SEG = 8
    def disc(y, r, col, ru=None, rv=None): return dict(pts=ellipse((X, y, Z), r if ru is None else ru, r if rv is None else rv, SEG), color=col, w=W)
    top = 1.98 if roman else 2.08
    m = Mesh()
    shaft = [disc(.00, .004, BRONZE), disc(.10, .018, BRONZE), disc(.20, .017, BRONZE), disc(.21, .014, WOOD), disc(.99, .014, WOOD), disc(1.00, .016, WRAP),
             disc(1.14, .016, WRAP), disc(1.15, .014, WOOD), disc(top, .013, WOOD), disc(top + .02, .019, BRONZE), disc(top + .07, .019, BRONZE)]
    m.loft(shaft, cap_first=True)
    L = top + .07
    blade = [disc(L, .019, IRON), disc(L + .05, .0, IRON, .042, .008), disc(L + .12, .0, IRON, .048, .010), disc(L + .22, .0, IRON, .030, .006), disc(L + .30, .0, IRON, .002, .001)]
    m.loft(blade, cap_last=True)
    return m


def build_shield(face, roman):
    """Round shield carried on the left forearm (authored along it, like the Olmec one): aspis (large, deep, bronze rim) or clipeus (smaller)."""
    RIM = BRONZE; BACK = (.30, .20, .11, 1)
    FORE = JOINT["forearm.L"]
    AX = JOINT["hand.L"] - FORE; AX = AX / np.linalg.norm(AX)
    WD = np.array((-AX[1], AX[0], 0.0)); WD = WD / np.linalg.norm(WD)
    NR = np.array((0, 0, 1.0))
    HALF, THICK, BULGE = (.30, .022, .05) if roman else (.36, .024, .07)
    CT = FORE + AX * .06 + NR * .10
    NU = NV = 12
    Wt = [("forearm.L", 1.0)]
    dark = (face[0] * .8, face[1] * .8, face[2] * .8, 1)

    def surface(u, v, front=True):
        a = (u - .5) * 2; b = (v - .5) * 2
        x = a * math.sqrt(1 - b * b / 2) * HALF; y = b * math.sqrt(1 - a * a / 2) * HALF
        z = BULGE * (1 - (x * x + y * y) / HALF ** 2) + (THICK if front else -THICK) / 2
        return CT + WD * x + AX * y + NR * z

    def tint(i, j):
        a = (i + .5) / NU * 2 - 1; b = (j + .5) / NV * 2 - 1
        r = math.hypot(a, b)
        if r > 1.0 or i in (0, NU - 1) or j in (0, NV - 1): return RIM
        if r > .82: return RIM
        return dark if .52 < r < .64 else face          # a darker ring painted on the face

    m = Mesh()
    m.grid(lambda u, v: surface(u, v, True), NU, NV, np.array((0, 0, 1.0)), tint, Wt)
    m.grid(lambda u, v: surface(u, v, False), NU, NV, np.array((0, 0, -1.0)), BACK, Wt)
    for f, o in ((lambda t: surface(0, t), -WD), (lambda t: surface(1, t), WD), (lambda t: surface(t, 0), -AX), (lambda t: surface(t, 1), AX)):
        for i in range(NU):
            p0, p1 = f(i / NU), f((i + 1) / NU)
            m.quad(p0, p1, p1 - NR * THICK, p0 - NR * THICK, np.asarray(o, float), RIM, Wt)
    r0 = .05 if roman else .035
    boss = [dict(pts=ellipse(tuple(CT + NR * (BULGE + .012 + dz)), r, r, 10), color=BRONZE, w=Wt) for dz, r in ((0, r0 * 1.4), (.012, r0), (.026, r0 * .5), (.032, .0))]
    boss[-1]["pts"] = ellipse(tuple(CT + NR * (BULGE + .044)), .004, .004, 10)
    m.loft(boss, cap_last=True)
    m.box(tuple(CT + (-.05, -.02, -.05)), tuple(CT + (.05, .02, -.02)), BACK, Wt)     # grip bar behind the face
    return m


if __name__ == "__main__":
    ok = True
    for roman, table in ((False, GREEK), (True, ROMAN)):
        for tag, (cloth, face, crest) in table.items():
            t = tag.lower()
            for name, mesh, budget in ((f"gear_tunic_{t}", build_tunic(cloth, roman), 800), (f"gear_trousers_{t}", build_skirt(cloth), 400),
                                       (f"gear_belt_{t}", build_belt(roman), 500), (f"gear_boots_{t}", build_boots(roman, cloth), 600),
                                       (f"gear_helmet_{t}", build_helmet(roman, crest), 500), (f"gear_spear_{t}", build_spear(roman), 300),
                                       (f"gear_shield_{t}", build_shield(face, roman), 800)):
                ok &= write_piece(mesh, name, budget)
    finish(ok)
