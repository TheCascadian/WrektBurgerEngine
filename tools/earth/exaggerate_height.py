#!/usr/bin/env python3
"""Exaggerates land relief in a map's heightmap.png so hills and mountains read clearly in 3D.
Usage: exaggerate_height.py <game/map folder> [gamma=0.62] [relief=0.9] [sea_level=20]
 gamma  < 1 lifts low and middle land (peaks stay at their value); 1 = off
 relief adds back the local detail (value minus its blur) so ridges and valleys stand out; 0 = off
Only pixels above sea_level change, so coasts, sea floor and the sea level itself stay put.
Run it once on an original heightmap (it is not idempotent); git holds the originals."""
import sys, os
import numpy as np
from PIL import Image, ImageFilter
Image.MAX_IMAGE_PIXELS = None
d = sys.argv[1]
g = float(sys.argv[2]) if len(sys.argv) > 2 else 0.62
k = float(sys.argv[3]) if len(sys.argv) > 3 else 0.9
sea = float(sys.argv[4]) if len(sys.argv) > 4 else 20.0
path = os.path.join(d, "heightmap.png")
im = Image.open(path).convert("L")
h = np.asarray(im, dtype=np.float32)
land = h > sea
top = max(float(h.max()), sea + 1)
t = np.clip((h - sea) / (top - sea), 0, 1)
out = np.where(land, sea + (top - sea) * t ** g, h)
if k > 0:
    r = max(2, im.width // 1500)
    blur = np.asarray(Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r * 3)), dtype=np.float32)
    # more detail on higher ground, none at the shore
    w = np.clip((out - sea) / 60.0, 0, 1)
    out = np.where(land, out + k * (out - blur) * w, out)
out = np.where(land, np.clip(out, sea + 1, 255), h)
Image.fromarray(np.round(out).astype(np.uint8), "L").save(path, optimize=True)
print("wrote", path, "land median", np.median(h[land]), "->", np.median(out[land]))
