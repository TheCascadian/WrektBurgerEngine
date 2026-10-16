#!/usr/bin/env python3
"""Achaemenid spara: tall rectangular wicker shield with a dark wooden rim and binding bands, carried on the left forearm. Output: out/gear_shield_spara.glb"""
import numpy as np
from gear_lib import *

WICKER_A = (.66, .53, .32, 1); WICKER_B = (.60, .48, .28, 1); BAND = (.36, .23, .12, 1); RIM = (.28, .17, .09, 1); BACK = (.30, .20, .11, 1)
MAX_TRIANGLES = 700
# The shield is authored along the left forearm, not upright: its tall axis is the forearm's axis (the arm hangs 42 degrees below horizontal in the rest pose)
# so that when the arm is folded with the forearm vertical (the carry pose of the soldier clips) the shield stands upright in front of the arm.
# Face normal at rest is +Z; the clips roll the forearm to turn the face forward and outward.
FORE = JOINT["forearm.L"]
AX = (JOINT["hand.L"] - FORE); AX = AX / np.linalg.norm(AX)       # forearm axis (toward the hand) = the shield's "up"
WD = np.array((AX[1] * -1, AX[0], 0.0)); WD = WD / np.linalg.norm(WD)   # across the shield, in the rest plane
NR = np.array((0, 0, 1.0))
C = FORE + AX * .06 + NR * .10    # center of the face
HALF_W, HALF_H, THICK, BULGE = .27, .42, .022, .045
NU, NV = 8, 20
W = [("forearm.L", 1.0)]


def surface(u, v, front=True):
    x = (u - .5) * 2 * HALF_W; y = (v - .5) * 2 * HALF_H
    z = BULGE * (1 - (x / HALF_W) ** 2) + (THICK if front else -THICK) / 2
    return C + WD * x + AX * y + NR * z


def build():
    m = Mesh()
    face = lambda i, j: RIM if i in (0, NU - 1) or j in (0, NV - 1) else (BAND if j in (6, 13) else (WICKER_A if (i + j // 2) % 2 else WICKER_B))
    m.grid(lambda u, v: surface(u, v, True), NU, NV, np.array((0, 0, 1.0)), face, W)
    m.grid(lambda u, v: surface(u, v, False), NU, 1, np.array((0, 0, -1.0)), BACK, W)
    for f, o in ((lambda t: surface(0, t), -WD), (lambda t: surface(1, t), WD), (lambda t: surface(t, 0), -AX), (lambda t: surface(t, 1), AX)):
        for i in range(NU):
            a, b = i / NU, (i + 1) / NU
            pts = [f(a), f(b)]
            # edge strip joining the front and back faces
            def pair(t):
                x = f(t); return x, x - NR * THICK
            (p0, q0), (p1, q1) = pair(a), pair(b)
            m.quad(p0, p1, q1, q0, np.asarray(o, float), RIM, W)
    # boss, and a grip bar behind the face across the forearm
    boss = [dict(pts=ellipse(tuple(C + NR * (BULGE + .012 + dz)), r, r, 10), color=(.62, .44, .18, 1), w=W) for dz, r in ((0, .075), (.012, .06), (.026, .03), (.032, .0))]
    boss[-1]["pts"] = ellipse(tuple(C + NR * (BULGE + .044)), .004, .004, 10)
    m.loft(boss, cap_last=True)
    m.box(tuple(C + (-.05, -.02, -.05)), tuple(C + (.05, .02, -.02)), BACK, W)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_shield_spara", MAX_TRIANGLES))
