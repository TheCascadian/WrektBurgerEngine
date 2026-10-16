#!/usr/bin/env python3
"""Olmec infantry armor: a sleeveless, hip-length quilted cotton tunic (padded cotton body armor is a Mesoamerican trait) with horizontal quilting bands. Output: out/gear_tunic_olmec.glb"""
from gear_lib import *

COTTON_A = (.80, .74, .60, 1); COTTON_B = (.72, .65, .50, 1); EDGE = (.46, .24, .16, 1)
MAX_TRIANGLES = 700
# torso rings: y, half-width, front z, back z (meters; 1-2 cm outside the body surface)
TORSO = [(.80, .232, .150, -.142), (.88, .222, .140, -.140), (.95, .206, .127, -.143), (1.03, .184, .120, -.120), (1.10, .166, .120, -.110),
         (1.17, .172, .126, -.114), (1.25, .200, .136, -.124), (1.33, .232, .146, -.128), (1.40, .250, .130, -.114), (1.46, .196, .098, -.098), (1.50, .120, .076, -.074)]
SIDES = 14


def weights(p):
    y = p[1]
    if y > 1.38: return blend(("chest", .85), ("neck", .15 * smooth(1.44, 1.50, y)), ("spine", .15 * (1 - smooth(1.38, 1.44, y))))
    if y > 1.15: return blend(("chest", smooth(1.15, 1.35, y)), ("spine", 1 - smooth(1.15, 1.35, y) * .7))
    if y > .9: return blend(("spine", smooth(.9, 1.15, y)), ("hips", 1 - smooth(.9, 1.15, y)))
    return blend(("hips", 1))


def build():
    m = Mesh(); rings = []
    for k, (y, hw, zf, zb) in enumerate(TORSO):
        col = EDGE if k == 0 else (COTTON_A if k % 2 else COTTON_B)
        rings.append(dict(pts=ellipse((0, y, (zf + zb) / 2), hw, (zf - zb) / 2, SIDES), color=col, w=weights))
    m.loft(rings, cap_last=True)
    return m


if __name__ == "__main__":
    finish(write_piece(build(), "gear_tunic_olmec", MAX_TRIANGLES))
