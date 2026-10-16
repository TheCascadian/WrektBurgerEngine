#!/usr/bin/env python3
"""Achaemenid Persian infantry tunic: knee-length saffron kandys with long sleeves, dark red hem, cuff and neck bands. Output: out/gear_tunic_persian.glb"""
import numpy as np
from gear_lib import *

SAFFRON = (.85, .60, .13, 1); TRIM = (.42, .12, .10, 1)
MAX_TRIANGLES = 900
# torso rings: y, half-width, front z, back z (meters; 1-2 cm outside the body surface)
TORSO = [(.55, .275, .165, -.150), (.68, .250, .150, -.142), (.82, .225, .135, -.138), (.95, .200, .122, -.140),
         (1.10, .160, .115, -.105), (1.20, .182, .125, -.118), (1.31, .225, .142, -.128), (1.40, .245, .125, -.112),
         (1.47, .190, .095, -.096), (1.515, .072, .066, -.062)]
HEM = 0   # ring index of the hem band
SIDES = 14


def torso_weights(side_of_point):
    def w(p):
        y = p[1]
        if y > 1.38: return blend(("chest", .8), ("neck", .2 * smooth(1.45, 1.52, y)), ("spine", .2 * (1 - smooth(1.38, 1.45, y))))
        if y > 1.15: return blend(("chest", smooth(1.15, 1.35, y)), ("spine", 1 - smooth(1.15, 1.35, y) * .7))
        if y > .9: return blend(("spine", smooth(.9, 1.15, y)), ("hips", 1 - smooth(.9, 1.15, y)))
        t = .5 * smooth(.9, .55, y) * smooth(0.0, .1, abs(p[0]))        # the skirt follows the thighs below the hips
        return blend(("hips", 1 - t), ("thigh.L" if p[0] > 0 else "thigh.R", t))
    return w


def build():
    m = Mesh()
    rings = []
    for k, (y, hw, zf, zb) in enumerate(TORSO):
        col = TRIM if k == 0 or k == len(TORSO) - 2 else SAFFRON
        pts = ellipse((0, y, (zf + zb) / 2), hw, (zf - zb) / 2, SIDES)
        rings.append(dict(pts=pts, color=col, w=torso_weights(None)))
    # a thin dark band just above the hem: duplicate the hem ring a few cm higher
    rings.insert(1, dict(pts=rings[0]["pts"] * (1, 0, 1) + (0, .60, 0) + 0 * rings[0]["pts"], color=SAFFRON, w=torso_weights(None)))
    rings[1]["pts"] = np.array([(p[0] * .985, .60, p[2] * .985) for p in rings[0]["pts"]])
    m.loft(rings)
    sleeve = Mesh()
    sh, ua, fa, ha = JOINT["shoulder.L"], JOINT["upper_arm.L"], JOINT["forearm.L"], JOINT["hand.L"]
    path = [np.array((.12, 1.45, 0)), ua, (ua + fa) / 2, fa, (fa + ha) / 2, fa + (ha - fa) * .88]
    radii = [.072, .062, .056, .052, .048, .046]
    wts = [blend(("chest", .5), ("upper_arm.L", .5)), blend(("upper_arm.L", 1)), blend(("upper_arm.L", .7), ("forearm.L", .3)),
           blend(("forearm.L", 1)), blend(("forearm.L", 1)), blend(("forearm.L", 1))]
    cols = [SAFFRON] * 4 + [TRIM, TRIM]
    sleeve.loft(limb(path, radii, cols, wts, 10), cap_first=False, cap_last=True)
    m.extend(sleeve); m.extend(sleeve.mirrored())
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_tunic_persian", MAX_TRIANGLES))
