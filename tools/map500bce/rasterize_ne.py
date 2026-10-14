"""Rasterizes Natural Earth 10m land, lakes and river centerlines onto the the world grid (grid.py) equirectangular world grid.

Usage: python3 rasterize_ne.py <folder with ne_10m_*.zip> <output .npz>
Natural Earth is public domain (see provenance/licenses/ne_terms.html). Output arrays (uint8): land, lake, river.
Pixel (c, r) covers longitude -180 + c*360/W and latitude 90 - r*180/H.
"""
import os
import sys
import zipfile

import numpy as np
import shapefile
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grid import H, W  # noqa: E402


def log(*a):
    print(*a, flush=True)


def px(lon, lat):
    return (lon + 180.0) * W / 360.0, (90.0 - lat) * H / 180.0


def open_shp(folder, name):
    z = zipfile.ZipFile(os.path.join(folder, f"ne_10m_{name}.zip"))
    get = lambda ext: z.open([n for n in z.namelist() if n.endswith(ext)][0])
    return shapefile.Reader(shp=get(".shp"), dbf=get(".dbf"), shx=get(".shx"))


def polygons(folder, name, fill_hole=False):
    """Even-odd fill: draw each ring, XOR-ing it into the mask so holes stay empty."""
    mask = np.zeros((H, W), np.uint8)
    n = 0
    for shp in open_shp(folder, name).shapes():
        parts = list(shp.parts) + [len(shp.points)]
        for a, b in zip(parts[:-1], parts[1:]):
            ring = [px(*p) for p in shp.points[a:b]]
            if len(ring) < 3:
                continue
            x0 = max(int(min(p[0] for p in ring)) - 1, 0); x1 = min(int(max(p[0] for p in ring)) + 2, W)
            y0 = max(int(min(p[1] for p in ring)) - 1, 0); y1 = min(int(max(p[1] for p in ring)) + 2, H)
            sub = Image.new("1", (x1 - x0, y1 - y0), 0)
            ImageDraw.Draw(sub).polygon([(x - x0, y - y0) for x, y in ring], fill=1)
            mask[y0:y1, x0:x1] ^= np.asarray(sub, np.uint8)
        n += 1
    log(f"{name}: {n} shapes, {int(mask.sum())} pixels")
    return mask


def lines(folder, name):
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    n = 0
    for shp in open_shp(folder, name).shapes():
        parts = list(shp.parts) + [len(shp.points)]
        for a, b in zip(parts[:-1], parts[1:]):
            pts = [px(*p) for p in shp.points[a:b]]
            if len(pts) > 1:
                d.line(pts, fill=1, width=1)
        n += 1
    log(f"{name}: {n} shapes")
    return np.asarray(img, np.uint8)


def main():
    folder, out = sys.argv[1], sys.argv[2]
    land = polygons(folder, "land")
    land |= polygons(folder, "minor_islands")
    lake = polygons(folder, "lakes")
    river = lines(folder, "rivers_lake_centerlines")
    np.savez_compressed(out, land=land, lake=lake, river=river)
    log("land share", float(land.mean()), "lake share", float(lake.mean()), "river pixels", int(river.sum()))
    Image.fromarray((land * 120 + lake * 60 + river * 75).astype(np.uint8)).resize((4096, 2048), Image.BOX).save(os.path.splitext(out)[0] + "_preview.png")


if __name__ == "__main__":
    main()
