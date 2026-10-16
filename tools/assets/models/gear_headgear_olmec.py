#!/usr/bin/env python3
"""Olmec padded cotton helmet: a quilted dome with a red woven headband, a jade bead on the brow and jade ear spools. Output: out/gear_headgear_olmec.glb"""
import math
from gear_lib import *

COTTON_A = (.80, .74, .60, 1); COTTON_B = (.70, .63, .48, 1); BAND = (.52, .20, .13, 1); JADE = (.22, .55, .38, 1)
MAX_TRIANGLES = 450
N = 16
CZ, RX, RZ = .02, .108, .124      # around the skull: head is x +-.094, z -.085..+.128, top 1.80
W = [("headgear", 1.0)]


def ring(y, s, col, shift=0.0):
    return dict(pts=np.array([(RX * s * math.cos(t), y, CZ + shift + RZ * s * math.sin(t)) for t in [2 * math.pi * i / N for i in range(N)]]), color=col, w=W)


def build():
    m = Mesh()
    rings = [ring(1.690, 1.0, BAND), ring(1.725, 1.0, COTTON_A)]
    for k, (y, s) in enumerate(((1.765, 1.0), (1.805, .94), (1.840, .78), (1.862, .50), (1.874, .20))):
        rings.append(ring(y, s, COTTON_B if k % 2 else COTTON_A))
    m.loft(rings, cap_last=True)
    m.box((-.014, 1.700, .139), (.014, 1.730, .150), JADE, W)                   # jade bead on the brow
    for sx in (1, -1):                                                         # ear spools
        sp = [dict(pts=ellipse((sx * x, 1.655, .000), .022, .022, 8, u=(0, 1, 0), v=(0, 0, 1)), color=JADE, w=W) for x in (.092, .108)]
        m.loft(sp, cap_first=True, cap_last=True)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_headgear_olmec", MAX_TRIANGLES))
