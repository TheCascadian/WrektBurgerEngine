#!/usr/bin/env python3
"""Olmec sandals: a thin leather sole under the bare foot, tied with an ankle strap and a toe strap (the foot and shin stay bare). Output: out/gear_sandals_olmec.glb"""
from gear_lib import *

SOLE = (.40, .28, .16, 1); STRAP = (.50, .20, .13, 1)
MAX_TRIANGLES = 300
CX = .089


def weights(p):
    z = p[2]
    toe = smooth(.12, .20, z) if p[1] < .05 else 0
    up = smooth(.05, .12, p[1])
    return blend(("shin.L", up), ("foot.L", (1 - up) * (1 - .6 * toe)), ("toe.L", (1 - up) * .6 * toe))


def build():
    m = Mesh()
    # sole: a flat oval from heel to toe, 1 cm thick
    sole = [dict(pts=ellipse((CX, y, .072), .062, .150, 12), color=SOLE, w=weights) for y in (-.0, .010)]
    sole[0]["pts"] = ellipse((CX, .0, .072), .062, .150, 12)
    m.loft(sole, cap_first=True, cap_last=True)
    # ankle strap, and an instep strap crossing over the foot
    ank = [dict(pts=ellipse((CX, y, -.004), .066, .066, 10), color=STRAP, w=weights) for y in (.07, .095)]
    m.loft(ank)
    ins = [dict(pts=ellipse((CX, y, .095), .062, .018, 10), color=STRAP, w=weights) for y in (.040, .058)]
    m.loft(ins)
    out = Mesh(); out.extend(m); out.extend(m.mirrored())
    return out


if __name__ == "__main__":
    finish(write_piece(build(), "gear_sandals_olmec", MAX_TRIANGLES))
