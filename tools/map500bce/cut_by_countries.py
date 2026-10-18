"""Cuts the location grid along 2020 country borders so every land atom lies inside exactly one modern country.

Usage: python3 cut_by_countries.py <folder with ne_10m_admin_0_countries.zip> <locations .npz> <output .npz> [--box lon0 lat0 lon1 lat1]
The modern coastline and the modern borders are the base data and are used as given (Natural Earth, public domain).
Method:
  1. Rasterize admin-0 countries to a root raster (painter's order, largest first, so enclaves stay whole).
     Land pixels the rasters disagree on (coast differences) take the nearest country, so no land pixel is left without a root.
  2. Each land location is cut into one piece per root it touches. Every piece stays its own atom, so the merge-up guarantee
     holds exactly. With CUT_MERGE=1, pieces below CUT_MIN_KM2 join the same-country neighbor with the longest shared edge;
     that trades the exact parent guarantee for fewer slivers (the check then only holds per country).
  3. Output ids are new atoms. parent[atom] is the original location id; root[atom] the country index. Merging the pieces of
     one parent gives back the original location pixel for pixel, and merging the atoms of one root gives the country's land.
Output arrays: ids (uint16 or uint32), kind (by id), seeds (lon, lat), parent, root, root_names, root_iso.
"""
import os
import sys
import zipfile

import numpy as np
import shapefile
from PIL import Image, ImageDraw
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grid import H, W  # noqa: E402

MIN_KM2 = float(os.environ.get("CUT_MIN_KM2", "400"))


def log(*a):
    print(*a, flush=True)


def px(lon, lat):
    return (lon + 180.0) * W / 360.0, (90.0 - lat) * H / 180.0


def read_countries(folder):
    z = zipfile.ZipFile(os.path.join(folder, "ne_10m_admin_0_countries.zip"))
    get = lambda ext: z.open([n for n in z.namelist() if n.endswith(ext)][0])
    r = shapefile.Reader(shp=get(".shp"), dbf=get(".dbf"), shx=get(".shx"))
    names = [f[0] for f in r.fields[1:]]
    out = []
    for sr in r.shapeRecords():
        rec = dict(zip(names, sr.record))
        out.append((rec.get("NAME") or rec.get("ADMIN"), rec.get("ADM0_A3") or rec.get("ISO_A3"), sr.shape))
    return out


def shape_area(shp):
    bb = shp.bbox
    return (bb[2] - bb[0]) * (bb[3] - bb[1])


def rasterize_roots(countries, box):
    """uint16 raster of country index+1 (0 = none). box = (x0, y0, x1, y1) in pixels."""
    x0, y0, x1, y1 = box
    img = Image.new("I;16", (x1 - x0, y1 - y0), 0)
    d = ImageDraw.Draw(img)
    order = sorted(range(len(countries)), key=lambda i: -shape_area(countries[i][2]))
    for i in order:
        shp = countries[i][2]
        parts = list(shp.parts) + [len(shp.points)]
        # outer rings carry the id, holes (opposite winding) are skipped; enclaves are later countries, drawn on top.
        for a, b in zip(parts[:-1], parts[1:]):
            ring = [px(*p) for p in shp.points[a:b]]
            if len(ring) < 3:
                continue
            area2 = sum(ring[k][0] * ring[(k + 1) % len(ring)][1] - ring[(k + 1) % len(ring)][0] * ring[k][1] for k in range(len(ring)))
            if area2 > 0:      # raster y runs down, so shapefile outer rings (clockwise) have positive area here
                d.polygon([(x - x0, y - y0) for x, y in ring], fill=i + 1)
    return np.asarray(img, np.uint16)


def main():
    folder, loc_path, out_path = sys.argv[1:4]
    box = (0, 0, W, H)
    if "--box" in sys.argv:
        k = sys.argv.index("--box")
        lon0, lat0, lon1, lat1 = map(float, sys.argv[k + 1:k + 5])
        xa, ya = px(lon0, lat1)
        xb, yb = px(lon1, lat0)
        box = (int(xa), int(ya), int(xb) + 1, int(yb) + 1)
    x0, y0, x1, y1 = box
    z = np.load(loc_path)
    ids = z["ids"][y0:y1, x0:x1]
    kind, seeds = z["kind"], z["seeds"]
    countries = read_countries(folder)
    log(f"{len(countries)} countries, window {x1 - x0}x{y1 - y0}")
    root = rasterize_roots(countries, box)
    land = (kind[ids] == 1) & (ids > 0)
    # land pixels without a country take the nearest one (coast differences)
    gap = land & (root == 0)
    if gap.any():
        log(f"{int(gap.sum())} land pixels outside every country polygon; assigning nearest country")
        idx = ndimage.distance_transform_edt(root == 0, return_distances=False, return_indices=True)
        root = np.where(gap, root[idx[0], idx[1]], root)
    root = np.where(land, root, 0)

    key = np.where(land, ids.astype(np.uint64) * 1024 + root.astype(np.uint64), 0)
    uniq, inv = np.unique(key, return_inverse=True)
    inv = inv.reshape(key.shape)
    log(f"{len(uniq) - 1} pieces from {len(np.unique(ids[land]))} land locations")
    lat = 90.0 - (np.arange(y0, y1) + 0.5) * 180.0 / H
    w_px = np.cos(np.radians(lat))[:, None] * (6371.0088 * np.radians(360.0 / W)) * (6371.0088 * np.radians(180.0 / H))
    area = np.bincount(inv.ravel(), weights=np.broadcast_to(w_px, inv.shape).ravel(), minlength=len(uniq))
    parent = (uniq // 1024).astype(np.int64)
    rt = (uniq % 1024).astype(np.int64)
    # merge small pieces into the neighbor piece (same parent first, then same root) with the longest shared edge
    target = np.arange(len(uniq))
    small = [p for p in range(1, len(uniq)) if area[p] < MIN_KM2]
    log(f"{len(small)} pieces under {MIN_KM2:g} km2")
    if small and os.environ.get("CUT_MERGE") == "1":
        pairs = {}
        for a, b in ((inv[:, :-1], inv[:, 1:]), (inv[:-1, :], inv[1:, :])):
            m = (a != b) & (a > 0) & (b > 0)
            for u, v in zip(a[m].tolist(), b[m].tolist()):
                pairs[(u, v)] = pairs.get((u, v), 0) + 1
                pairs[(v, u)] = pairs.get((v, u), 0) + 1
        by_src = {}
        for (u, v), c in pairs.items():
            by_src.setdefault(u, []).append((c, v))
        merged = 0
        for p in sorted(small, key=lambda q: area[q]):
            cand = [(c, v) for c, v in by_src.get(p, []) if rt[v] == rt[p] and target[v] != p]
            if not cand:
                continue
            c, v = max(cand)
            tgt = int(target[v])
            target[p] = tgt
            area[tgt] += area[p]
            merged += 1
        log(f"{merged} of {len(small)} small pieces merged into a same-country neighbor; {len(small) - merged} stay (no neighbor)")
    for _ in range(4):
        target = target[target]
    keep = np.unique(target[1:])
    new_id = np.zeros(len(uniq), np.int64)
    new_id[keep] = np.arange(1, len(keep) + 1)
    final = new_id[target]
    out_ids = final[inv].astype(np.uint16 if len(keep) < 65535 else np.uint32)
    out_ids[~land] = 0
    # ids of non-land locations (sea, lake, impassable) keep identity: append them after the land atoms
    n_land = len(keep)
    other = np.unique(ids[(ids > 0) & ~land])
    omap = {int(o): n_land + 1 + k for k, o in enumerate(other)}
    if omap:
        lut = np.zeros(int(ids.max()) + 1, out_ids.dtype)
        for o, n in omap.items():
            lut[o] = n
        out_ids = np.where((ids > 0) & ~land, lut[ids], out_ids)
    parent_o = np.concatenate([[0], parent[keep], np.array(list(omap), np.int64)])
    root_o = np.concatenate([[0], rt[keep], np.zeros(len(omap), np.int64)])
    kind_o = np.concatenate([[0], np.ones(n_land, np.uint8), kind[other]]).astype(np.uint8)
    seeds_o = np.concatenate([[[0, 0]], seeds[parent[keep]], seeds[other]]) if len(other) else np.concatenate([[[0, 0]], seeds[parent[keep]]])
    np.savez_compressed(out_path, ids=out_ids, kind=kind_o, seeds=seeds_o, parent=parent_o, root=root_o,
                        root_names=np.array([c[0] for c in countries]), root_iso=np.array([c[1] for c in countries]), box=np.array(box))
    # proof: pieces merge back to the original location, atoms of a root merge to the country land
    back = parent_o[out_ids]
    ok_parent = bool((np.where(land, back, 0) == np.where(land, ids, 0)).all())
    n_split = int((np.bincount(parent_o[1:n_land + 1]) > 1).sum())
    log(f"atoms: {n_land} land; original locations split across borders: {n_split}")
    log(f"merge-up check (atoms of one parent == original location, pixel for pixel): {'PASS' if ok_parent else 'FAIL'}")
    # every atom inside exactly one country, by construction of the key; verify the count of roots per atom
    chk = np.stack([out_ids[land].astype(np.int64), root[land].astype(np.int64)])
    pairs_u = np.unique(chk[0] * 1024 + chk[1])
    log(f"one-country-per-atom check: {'PASS' if len(pairs_u) == len(np.unique(chk[0])) else 'FAIL'}")
    sys.exit(0 if ok_parent else 1)


if __name__ == "__main__":
    main()
