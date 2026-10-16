#!/usr/bin/env python3
"""Persian infantry spear held in the right hand: ash shaft, leather grip wrap, bronze butt spike and an iron leaf blade. Output: out/gear_spear_persian.glb"""
import math
from gear_lib import *

WOOD = (.50, .34, .18, 1); WRAP = (.30, .18, .10, 1); IRON = (.60, .61, .64, 1); BRONZE = (.62, .44, .18, 1)
MAX_TRIANGLES = 300
W = [("hand.R", 1.0)]
X, Z = JOINT["hand.R"][0], JOINT["hand.R"][2]    # the shaft passes through the closed right hand
SEG = 8


def disc(y, r, col, ru=None, rv=None):
    return dict(pts=ellipse((X, y, Z), r if ru is None else ru, r if rv is None else rv, SEG), color=col, w=W)


def build():
    m = Mesh()
    # butt spike, shaft with a grip wrap at the hand, then the socket and leaf
    shaft = [disc(.00, .004, BRONZE), disc(.09, .017, BRONZE), disc(.16, .016, BRONZE), disc(.17, .014, WOOD),
             disc(.99, .014, WOOD), disc(1.00, .016, WRAP), disc(1.14, .016, WRAP), disc(1.15, .014, WOOD),
             disc(1.86, .013, WOOD), disc(1.88, .019, IRON), disc(1.93, .019, IRON)]
    m.loft(shaft, cap_first=True)
    # leaf blade: a flattened diamond lathe (wide in X, thin in Z)
    blade = [disc(1.93, .019, IRON), disc(1.97, .00, IRON, .040, .007), disc(2.02, .00, IRON, .047, .009), disc(2.09, .00, IRON, .030, .006), disc(2.17, .00, IRON, .002, .001)]
    m.loft(blade, cap_last=True)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_spear_persian", MAX_TRIANGLES))
