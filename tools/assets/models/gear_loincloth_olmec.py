#!/usr/bin/env python3
"""Olmec breechcloth (maxtlatl): front and back flaps with red and cream bands, hung from a woven waist sash. Outputs: out/gear_loincloth_olmec.glb, out/gear_sash_olmec.glb"""
from gear_lib import *

CREAM = (.82, .76, .62, 1); RED = (.52, .20, .13, 1); SASH = (.50, .22, .14, 1); KNOT = (.82, .76, .62, 1)
MAX_TRIANGLES = 300
HIPS = [("hips", 1.0)]


def flap(m, zlo, zhi, bands):
    y = .95
    for hw, h, col in bands:
        m.box((-hw, y - h, zlo), (hw, y, zhi), col, HIPS)
        y -= h


def build_loincloth():
    m = Mesh()
    bands = [(.080, .06, RED), (.080, .09, CREAM), (.078, .09, RED), (.076, .09, CREAM), (.070, .08, RED)]
    flap(m, .136, .146, bands)           # front
    flap(m, -.152, -.142, bands)         # back
    return m


def build_sash():
    m = Mesh()
    rings = [dict(pts=ellipse((0, y, -.009), hw, hd, 14), color=SASH, w=HIPS) for y, hw, hd in ((.935, .200, .134), (.965, .207, .139), (1.0, .201, .135))]
    m.loft(rings, cap_first=True, cap_last=True)
    m.box((-.03, .90, .138), (.03, 1.0, .150), KNOT, HIPS)
    return m


if __name__ == "__main__":
    a = write_piece(build_loincloth(), "gear_loincloth_olmec", MAX_TRIANGLES)
    b = write_piece(build_sash(), "gear_sash_olmec", MAX_TRIANGLES)
    finish(a and b)
