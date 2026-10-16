#!/usr/bin/env python3
"""Persian soft leather shoe-boots with a darker sole and a lighter turned cuff, one per foot. Output: out/gear_boots_persian.glb"""
from gear_lib import *

LEATHER = (.40, .26, .14, 1); CUFF = (.52, .36, .20, 1); SOLE = (.14, .10, .07, 1)
MAX_TRIANGLES = 400
# rings bottom to top: y, center z, half-width x, half-length z (the left foot; mirrored for the right)
RINGS = [(.000, .075, .058, .140, SOLE), (.018, .072, .060, .142, LEATHER), (.050, .060, .062, .130, LEATHER), (.090, .030, .066, .100, LEATHER),
         (.160, .005, .068, .076, LEATHER), (.250, -.005, .066, .068, LEATHER), (.300, -.005, .068, .070, CUFF), (.318, -.005, .070, .072, CUFF)]
CX = .089


def weights(p):
    y, z = p[1], p[2]
    toe = .6 * smooth(.12, .20, z) if y < .08 else 0
    shin = smooth(.14, .30, y)
    return blend(("shin.L", shin), ("foot.L", (1 - shin) * (1 - toe)), ("toe.L", (1 - shin) * toe))


def build():
    m = Mesh(); rings = []
    for y, cz, rx, rz, col in RINGS:
        rings.append(dict(pts=ellipse((CX, y, cz), rx, rz, 12), color=col, w=weights))
    m.loft(rings, cap_first=True)
    return_mesh = Mesh(); return_mesh.extend(m); return_mesh.extend(m.mirrored())
    return return_mesh


if __name__ == "__main__":
    finish(write_piece(build(), "gear_boots_persian", MAX_TRIANGLES))
