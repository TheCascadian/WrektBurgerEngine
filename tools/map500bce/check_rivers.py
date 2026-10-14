"""Measure how well land-location borders follow rivers.
Usage: python3 check_rivers.py <ne_raster .npz> <locations .npz>
For each distance D, reports the share of river pixels (on land) within D pixels of a land-land border, the share of ALL
land pixels within D pixels of a border (what a river dropped at random would score), and their ratio. Ratio 1.0 = no
alignment; higher = rivers and borders coincide.
"""
import sys
import numpy as np
from scipy import ndimage

d = np.load(sys.argv[1]); loc = np.load(sys.argv[2])
ids, kind = loc["ids"], loc["kind"]
river = d["river"].astype(bool)
isl = (kind[ids] == 1)
b = np.zeros(ids.shape, bool)
dx = (ids[:, 1:] != ids[:, :-1]) & isl[:, 1:] & isl[:, :-1]
dy = (ids[1:] != ids[:-1]) & isl[1:] & isl[:-1]
b[:, 1:] |= dx; b[1:] |= dy
river &= isl
print(f"land px {isl.sum():,}  river px on land {river.sum():,}  border px {b.sum():,}")
cur = b.copy()
st = ndimage.generate_binary_structure(2, 2)
done = 0
for D in (1, 2, 4, 8, 16):
    cur = ndimage.binary_dilation(cur, st, iterations=D - done); done = D
    fr, fl = river[cur].sum() / river.sum(), isl[cur].sum() / isl.sum()
    print(f"within {D:2d}px: rivers {fr:6.1%}   all land {fl:6.1%}   ratio {fr / fl:5.2f}")
