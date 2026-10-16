#!/usr/bin/env python3
"""Olmec spear held in the right hand: hardwood shaft with a cord grip wrap, a fire-hardened point and a lashed obsidian blade (Mesoamerican arms are stone and wood; no metal). Output: out/gear_spear_olmec.glb"""
from gear_lib import *

WOOD = (.45, .30, .16, 1); WRAP = (.62, .55, .40, 1); LASH = (.52, .20, .13, 1); OBS = (.07, .07, .09, 1)
MAX_TRIANGLES = 300
W = [("hand.R", 1.0)]
X, Z = JOINT["hand.R"][0], JOINT["hand.R"][2]
SEG = 8


def disc(y, r, col, ru=None, rv=None):
    return dict(pts=ellipse((X, y, Z), r if ru is None else ru, r if rv is None else rv, SEG), color=col, w=W)


def build():
    m = Mesh()
    shaft = [disc(.00, .010, WOOD), disc(.03, .015, WOOD), disc(.99, .015, WOOD), disc(1.00, .017, WRAP), disc(1.14, .017, WRAP), disc(1.15, .015, WOOD),
             disc(1.84, .014, WOOD), disc(1.86, .018, LASH), disc(1.92, .018, LASH)]
    m.loft(shaft, cap_first=True)
    blade = [disc(1.92, .018, LASH), disc(1.95, .00, OBS, .028, .006), disc(2.03, .00, OBS, .036, .007), disc(2.12, .00, OBS, .022, .005), disc(2.22, .00, OBS, .002, .001)]
    m.loft(blade, cap_last=True)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_spear_olmec", MAX_TRIANGLES))
