"""Write the BurgerEngine map folder from the location grid.
Usage: python3 export_map.py <locations .npz> <ne_raster .npz> <gebco .npz> <map folder>
Writes provinces.png, locations.txt, terrain.png, heightmap.png, rivers.png, default.map at the full grid size.
PLACEHOLDERS (not final data): location keys are numbered (names come later from Pleiades/Wikidata work);
terrain is plains/hills/mountains from elevation only (forest, desert and marsh need biome evidence and are OPEN);
the height scale is linear. Impassable (Antarctica) uses key prefix ice_.
"""
import os
import sys

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grid import H, W  # noqa: E402

loc, ne, gebco, out = np.load(sys.argv[1]), np.load(sys.argv[2]), np.load(sys.argv[3]), sys.argv[4]
os.makedirs(out, exist_ok=True)
ids, kind = loc["ids"], loc["kind"]
assert ids.shape == (H, W) and ids.max() == len(kind) - 1 and (ids > 0).all()
n = len(kind) - 1
assert n <= 65000

# unique color per id: multiplying by an odd constant is a bijection on 24 bits, so colors cannot collide
col = ((np.arange(n + 1, dtype=np.uint64) * np.uint64(0x9E3779B1)) & np.uint64(0xFFFFFF)).astype(np.uint32)
col[0] = 0
assert len(np.unique(col[1:])) == n and (col[1:] != 0).all()
lut = np.stack([(col >> 16) & 255, (col >> 8) & 255, col & 255], axis=1).astype(np.uint8)


def save(arr, name):
    Image.fromarray(arr).save(os.path.join(out, name), compress_level=1)
    print("wrote", name, arr.shape, flush=True)


save(lut[ids], "provinces.png")

prefix = {1: "land", 2: "sea", 3: "lake", 4: "ice"}
tname = {1: "land", 2: "sea", 3: "lake", 4: "impassable"}
cnt = {}
lines = ["# Placeholder keys. Real names come from the Pleiades/Wikidata work; see docs/plans/MAP_500BCE_SOURCES.md.", ""]
for i in range(1, n + 1):
    k = int(kind[i]); cnt[k] = cnt.get(k, 0) + 1
    lines.append(f"{col[i]:06X} = {{ id = {prefix[k]}_{cnt[k]:05d} type = {tname[k]} }}")
open(os.path.join(out, "locations.txt"), "w").write("\n".join(lines) + "\n")
print("wrote locations.txt", n, flush=True)

elev = gebco["mean"]
sea = (kind[ids] == 2) | (kind[ids] == 3)
terr = np.empty((H, W, 3), np.uint8)
terr[:] = (86, 124, 27)                                              # plains
terr[elev >= 600] = (112, 74, 31)                                    # hills
terr[elev >= 2000] = (65, 42, 17)                                    # mountains
terr[sea] = (8, 31, 130)                                             # ocean
save(terr, "terrain.png")
del terr

SEA_LEVEL = 20
hm = np.where(elev > 0, SEA_LEVEL + 1 + elev * 234 / 6000, SEA_LEVEL + elev / 500)
save(np.clip(hm, 0, 255).astype(np.uint8), "heightmap.png")
del hm

rv = np.full((H, W, 3), 255, np.uint8)
rv[ne["river"].astype(bool)] = (46, 96, 158)
save(rv, "rivers.png")

open(os.path.join(out, "default.map"), "w").write(f"""provinces = "provinces.png"
heightmap = "heightmap.png"
terrain = "terrain.png"
trees = "trees.png"
rivers = "rivers.png"
locations = "locations.txt"
adjacencies = "adjacencies.txt"
states = "states"
sea_level = {SEA_LEVEL}
height_scale = 5
pixels_per_unit = 10
tree_density = 1.0
tree_spacing = 5
river_background = {{ 255 255 255 }}
water_color = {{ 30 66 112 }}
river_color = {{ 46 96 158 }}
""")
print("done")
