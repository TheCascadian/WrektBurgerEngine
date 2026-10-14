#!/usr/bin/env python3
"""Makes map/normal.png (tangent space, x east, y north, z up) from a map's heightmap.png.
Usage: make_normal.py <game/map folder> [strength]   (default strength 3.0; bigger = steeper)"""
import sys, os
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
d = sys.argv[1]; k = float(sys.argv[2]) if len(sys.argv) > 2 else 3.0
im = Image.open(os.path.join(d, "heightmap.png")).convert("L")
if im.width > 8192: im = im.resize((8192, max(1, im.height * 8192 // im.width)), Image.BILINEAR)  # the engine scales anything wider down anyway
h = np.asarray(im, dtype=np.float32) / 255.0
p = np.pad(h, 1, mode="edge")
dx = (p[1:-1, 2:] - p[1:-1, :-2]) * 0.5       # east
dy = (p[:-2, 1:-1] - p[2:, 1:-1]) * 0.5       # north (image y points south)
n = np.stack([-dx * k * 16, -dy * k * 16, np.ones_like(h)], -1)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(d, "normal.png"), optimize=True)
print("wrote", os.path.join(d, "normal.png"), h.shape[::-1])
