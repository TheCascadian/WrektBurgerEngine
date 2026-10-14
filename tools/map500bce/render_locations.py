"""Render a locations .npz (from make_locations.py) as a color PNG at native size, plus a native-pixel crop.
Usage: python3 render_locations.py <locations.npz> <out.png> [crop out.png lon0 lat0 lon1 lat1]
"""
import sys
import numpy as np
from PIL import Image

d = np.load(sys.argv[1])
ids, kind = d["ids"], d["kind"]
rng = np.random.default_rng(7)
pal = rng.integers(40, 235, (len(kind), 3), dtype=np.uint8)
pal[kind == 2] = (pal[kind == 2] * 0.25 + [20, 50, 110]).astype(np.uint8)   # sea: blue tint
pal[kind == 3] = [60, 150, 230]
pal[kind == 4] = [235, 240, 245]
pal[0] = 0
img = pal[ids]
# darken cell borders
b = np.zeros(ids.shape, bool)
b[:, 1:] |= ids[:, 1:] != ids[:, :-1]
b[1:, :] |= ids[1:, :] != ids[:-1, :]
img[b] = (img[b] * 0.35).astype(np.uint8)
Image.fromarray(img).save(sys.argv[2], optimize=False)
if len(sys.argv) > 3:
    H, W = ids.shape
    lon0, lat0, lon1, lat1 = map(float, sys.argv[4:8])
    c0, c1 = int((lon0 + 180) / 360 * W), int((lon1 + 180) / 360 * W)
    r0, r1 = int((90 - lat1) / 180 * H), int((90 - lat0) / 180 * H)
    Image.fromarray(img[r0:r1, c0:c1]).save(sys.argv[3])
