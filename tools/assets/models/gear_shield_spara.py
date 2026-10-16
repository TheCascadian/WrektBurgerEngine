#!/usr/bin/env python3
"""Achaemenid spara: tall rectangular wicker shield with a dark wooden rim and binding bands, carried on the left forearm. Output: out/gear_shield_spara.glb"""
import numpy as np
from gear_lib import *

WICKER_A = (.66, .53, .32, 1); WICKER_B = (.60, .48, .28, 1); BAND = (.36, .23, .12, 1); RIM = (.28, .17, .09, 1); BACK = (.30, .20, .11, 1)
MAX_TRIANGLES = 700
CX, CY, CZ = .41, 1.08, .085      # center of the face, in front of the left forearm
HALF_W, HALF_H, THICK, BULGE = .27, .46, .022, .045
NU, NV = 8, 20
W = blend(("forearm.L", .75), ("upper_arm.L", .25))


def surface(u, v, front=True):
    x = (u - .5) * 2 * HALF_W; y = (v - .5) * 2 * HALF_H
    z = BULGE * (1 - (x / HALF_W) ** 2) + (THICK if front else -THICK) / 2
    return np.array((CX + x, CY + y, CZ + z))


def build():
    m = Mesh()
    face = lambda i, j: RIM if i in (0, NU - 1) or j in (0, NV - 1) else (BAND if j in (6, 13) else (WICKER_A if (i + j // 2) % 2 else WICKER_B))
    out_front = lambda p: np.array((0, 0, 1.0)) + (p - np.array((CX, CY, CZ - .5))) * .0
    m.grid(lambda u, v: surface(u, v, True), NU, NV, np.array((0, 0, 1.0)), face, W)
    m.grid(lambda u, v: surface(u, v, False), NU, 1, np.array((0, 0, -1.0)), BACK, W)
    for f, o in ((lambda t: surface(0, t), (-1, 0, 0)), (lambda t: surface(1, t), (1, 0, 0)), (lambda t: surface(t, 0), (0, -1, 0)), (lambda t: surface(t, 1), (0, 1, 0))):
        for i in range(NU):
            a, b = i / NU, (i + 1) / NU
            pts = [f(a), f(b)]
            # edge strip joining the front and back faces
            def pair(t):
                x = f(t); return x, np.array((x[0], x[1], x[2] - THICK))
            (p0, q0), (p1, q1) = pair(a), pair(b)
            m.quad(p0, p1, q1, q0, np.array(o, float), RIM, W)
    # boss, and a grip bar behind the face across the forearm
    boss = [dict(pts=ellipse((CX, CY, CZ + BULGE + .012 + dz), r, r, 10), color=(.62, .44, .18, 1), w=W) for dz, r in ((0, .075), (.012, .06), (.026, .03), (.032, .0))]
    boss[-1]["pts"] = ellipse((CX, CY, CZ + BULGE + .044), .004, .004, 10)
    m.loft(boss, cap_last=True)
    m.box((CX - .05, CY - .02, CZ - .05), (CX + .05, CY + .02, CZ - .02), BACK, W)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_shield_spara", MAX_TRIANGLES))
