#!/usr/bin/env python3
"""Persian soft felt cap (tiara): a dome whose peaked tip folds forward, with a dark band and a lappet that falls over the ears and nape. Output: out/gear_headgear_persian.glb"""
import math
from gear_lib import *

FELT = (.72, .66, .50, 1); BAND = (.16, .20, .42, 1)
MAX_TRIANGLES = 400
N = 16
CZ, RX, RZ = .02, .108, .124      # around the skull: head is x +-.094, z -.085..+.128, top 1.80
W = [("headgear", 1.0)]


def lappet_y(theta):
    """Bottom edge of the cap: high over the forehead, low at the ears and nape."""
    s, c = math.sin(theta), math.cos(theta)        # c = x direction, s = z direction (+z = face)
    return 1.735 - .075 * (1 - s) / 2 - .035 * abs(c) * (1 + s) / 2 - .04 * max(0, -s)


def build():
    m = Mesh(); rings = []
    n = N
    th = [2 * math.pi * i / n for i in range(n)]
    base = np_pts = [np.array((CZ * 0 + RX * math.cos(t), lappet_y(t), CZ + RZ * math.sin(t))) for t in th]
    rings.append(dict(pts=np.array(base), color=BAND, w=W))
    rings.append(dict(pts=np.array([(RX * math.cos(t), 1.735, CZ + RZ * math.sin(t)) for t in th]), color=FELT, w=W))
    # dome: (y, scale, forward shift of the center) with the tip folding forward
    for y, s, shift in ((1.765, .98, 0), (1.800, .90, .004), (1.835, .70, .02), (1.862, .44, .05), (1.878, .22, .085)):
        rings.append(dict(pts=np.array([(RX * s * math.cos(t), y, CZ + shift + RZ * s * math.sin(t)) for t in th]), color=FELT, w=W))
    m.loft(rings, cap_last=True)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_headgear_persian", MAX_TRIANGLES))
