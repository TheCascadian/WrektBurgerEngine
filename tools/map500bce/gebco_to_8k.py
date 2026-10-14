"""Reduces the GEBCO 2026 sub-ice GeoTIFF tiles to the 8192x4096 world grid used by the 500 BCE map.

Usage: python3 gebco_to_8k.py <gebco zip or folder of tiles> <output .npz>

The grid is equirectangular (plate carree): 360 degrees across 8192 columns and 180 degrees down 4096 rows, so every
pixel is 0.0439 degrees. Each 90x90 degree source tile (21600x21600 cells of 15 arc-seconds) maps onto exactly 2048x2048
output pixels and is box-averaged, so the full-resolution grid is never held in memory. Output arrays (float32):
  mean      mean elevation in meters, negative below sea level
  landfrac  share of source cells at or above 0 m
Source: GEBCO Compilation Group (2026) GEBCO 2026 Grid. See docs/plans/MAP_500BCE_SOURCES.md for terms and acknowledgement.
"""
import glob
import io
import os
import re
import sys
import zipfile

import numpy as np
import tifffile
from PIL import Image

W, H = 8192, 4096
TW, TH = W // 4, H // 2          # output pixels per 90x90 degree tile


def log(*a):
    print(*a, flush=True)


def tiles(src):
    """Yield (name, open-able path or bytes reader) for every .tif in the zip or folder."""
    if zipfile.is_zipfile(src):
        z = zipfile.ZipFile(src)
        for n in sorted(z.namelist()):
            if n.lower().endswith(".tif"):
                yield n, z.open(n)
    else:
        for p in sorted(glob.glob(os.path.join(src, "**", "*.tif"), recursive=True)):
            yield os.path.basename(p), p


def tile_origin(name):
    """GEBCO tile names look like gebco_2026_sub_ice_n90.0_s0.0_w-180.0_e-90.0.tif."""
    m = re.search(r"n(-?[\d.]+)_s(-?[\d.]+)_w(-?[\d.]+)_e(-?[\d.]+)", name)
    n, s, w, e = (float(x) for x in m.groups())
    return n, w


def box(a, th, tw):
    return np.asarray(Image.fromarray(a.astype(np.float32), "F").resize((tw, th), Image.BOX))


def main():
    src, out = sys.argv[1], sys.argv[2]
    mean = np.full((H, W), np.nan, np.float32)
    lf = np.full((H, W), np.nan, np.float32)
    for name, f in tiles(src):
        n, w = tile_origin(name)
        data = tifffile.imread(io.BytesIO(f.read()) if hasattr(f, "read") else f)
        assert data.shape == (21600, 21600), (name, data.shape)
        r0, c0 = int((90 - n) / 90) * TH, int((w + 180) / 90) * TW
        mean[r0:r0 + TH, c0:c0 + TW] = box(data, TH, TW)
        lf[r0:r0 + TH, c0:c0 + TW] = box((data >= 0).astype(np.float32), TH, TW)
        log(f"{name}: rows {r0}-{r0 + TH}, cols {c0}-{c0 + TW}, mean {np.nanmean(mean[r0:r0 + TH, c0:c0 + TW]):.0f} m")
        del data
    assert not np.isnan(mean).any(), "some tiles missing"
    np.savez_compressed(out, mean=mean, landfrac=lf)
    log("wrote", out, "land share", float((lf > 0.5).mean()))


if __name__ == "__main__":
    main()
