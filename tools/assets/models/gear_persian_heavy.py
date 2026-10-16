#!/usr/bin/env python3
"""Achaemenid heavy infantry kit (ACH_HEAVY): a long-sleeved saffron coat covered in iron scale, a pointed iron cap with neck flap, and an akinakes
dagger-sword on the right hip. Spear, shield and boots are copied from the standard ACH kit (see the shell lines in the docs).
OPEN: scale-clad Persian infantry is attested in Greek and Persian-art sources, but this exact cut, colors and the iron cap are design choices.

Output: out/gear_tunic_ach_heavy.glb, gear_helmet_ach_heavy.glb, gear_belt_ach_heavy.glb."""
import math
from gear_lib import *
import gear_greek_roman as G

SAFF = (.85, .60, .13, 1); SAFF_D = (.70, .48, .10, 1); SCALE = (.58, .60, .64, 1); SCALE_D = (.42, .44, .48, 1); GOLD = (.80, .62, .18, 1)
W_H = [("headgear", 1.0)]


def tunic():
    m = Mesh(); rings = []
    for k, (y, hw, zf, zb) in enumerate(G.TORSO):
        col = (SCALE if k % 2 else SCALE_D) if 1.0 < y < 1.46 else SAFF
        rings.append(dict(pts=ellipse((0, y, (zf + zb) / 2), hw * 1.03, (zf - zb) / 2 * 1.04, 14), color=col, w=G.torso_w))
    m.loft(rings, cap_last=True)
    # scale skirt: two rows of hanging plates around the hips
    for layer, (top, bot, rad, col) in enumerate(((.98, .78, 1.10, SCALE_D), (.92, .70, 1.20, SCALE))):
        n = 16
        for i in range(n):
            a0, a1 = 2 * math.pi * (i + .05) / n, 2 * math.pi * (i + .95) / n
            P = lambda a, y, r: np.array((.215 * r * math.cos(a), y, -.005 + .150 * r * math.sin(a)))
            out = P((a0 + a1) / 2, 0, 1) - np.array((0, 0, -.005)); out[1] = 0
            m.quad(P(a0, top, 1.04), P(a1, top, 1.04), P(a1, bot, rad), P(a0, bot, rad), out, col if (i + layer) % 2 else SAFF_D, G.HIPS)
    # saffron sleeves to the wrist over the arms are the base tunic's; add shoulder scale caps
    for sx in (1, -1):
        m.box((sx * .10 - .05, 1.45, -.09), (sx * .10 + .05, 1.50, .11), SCALE, [("chest", 1.0)])
    m.box((-.07, 1.20, G.TORSO[7][2] + .004), (.07, 1.26, G.TORSO[7][2] + .014), GOLD, [("chest", 1.0)])   # gold chest boss
    return m


def helmet():
    m = Mesh(); CZ, RX, RZ = .02, .106, .122
    th = [2 * math.pi * i / 14 for i in range(14)]
    rings = []
    for y, s, col in ((1.62, 1.12, SCALE_D), (1.69, 1.04, SCALE), (1.76, .98, SCALE), (1.84, .70, SCALE), (1.92, .38, SCALE), (2.00, .10, GOLD)):
        zoff = -.03 * (1.76 - y) if y > 1.76 else 0
        rings.append(dict(pts=np.array([(RX * s * math.cos(t), y, CZ + zoff + RZ * s * math.sin(t)) for t in th]), color=col, w=W_H))
    m.loft(rings, cap_last=True)
    m.box((-.095, 1.50, CZ - .16), (.095, 1.66, CZ - .12), SCALE_D, W_H)   # neck flap
    for sx in (1, -1):
        m.box((sx * .105 - .008, 1.56, CZ - .06), (sx * .105 + .008, 1.68, CZ + .07), SCALE_D, W_H)   # ear flaps
    return m


def belt():
    m = Mesh()
    m.loft([dict(pts=ellipse((0, y, -.007), hw, hd, 14), color=SAFF_D if y > .95 else G.LEATHER_D, w=G.HIPS) for y, hw, hd in ((.93, .205, .141), (.97, .212, .146), (1.01, .207, .142))], cap_first=True, cap_last=True)
    m.box((-.03, .93, .144), (.03, 1.01, .152), GOLD, G.HIPS)
    side = "thigh.R"
    sw = lambda p: blend(("hips", smooth(.70, .90, p[1])), (side, 1 - smooth(.70, .90, p[1])))
    X, Z = -.225, .04
    top = .96
    # akinakes: short straight sheath with a gold-banded chape, tilted forward by its flat shape
    m.loft([dict(pts=ellipse((X, top - .34 * t, Z), .030 - .010 * t, .016, 8), color=G.LEATHER if t < .9 else GOLD, w=sw) for t in (0, .15, .6, .9, 1)], cap_last=True)
    m.loft([dict(pts=ellipse((X, y, Z), r, r * .8, 8), color=col, w=sw) for y, r, col in ((top, .014, G.BONE), (top + .07, .014, G.BONE), (top + .085, .022, GOLD))], cap_last=True)
    m.box((X - .045, top, Z - .012), (X + .045, top + .014, Z + .012), GOLD, sw)
    return m


if __name__ == "__main__":
    ok = True
    for piece, mesh, budget in (("tunic", tunic(), 900), ("helmet", helmet(), 400), ("belt", belt(), 500)):
        ok &= write_piece(mesh, f"gear_{piece}_ach_heavy", budget)
    finish(ok)
