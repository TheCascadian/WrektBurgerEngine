"""Cuts the location grid along 2020 national and administrative borders so every land atom lies in one modern unit.

Usage: python3 cut_by_countries.py <folder with ne_10m_admin_0_countries.zip and ne_10m_admin_1_states_provinces.zip>
                                   <locations .npz> <output .npz> [--box lon0 lat0 lon1 lat1]
The modern coastline and the modern borders are the base data and are used as given (Natural Earth, public domain).

Minimum size N (MIN_PX, default 64 pixels), by the math:
  The engine needs real borders (shared edge of at least 2 pixels) and a unit that is mostly interior. A compact unit of N pixels
  has a rim of about 2*sqrt(pi*N) pixels, so at least half of it is interior only when N >= 16*pi, about 50. The next
  clean size is 8x8 = 64 pixels, about 380 km2 at the equator (pixel = 0.0220 deg), which also matches the 400 km2
  floor the location grid already uses. N is CUT_MIN_PX and can be changed.
Method:
  1. Regions: every admin-1 unit; countries Natural Earth gives no admin-1 for stay one region (admin-0). Rasterized
     with the largest shapes first so enclaves stay whole. Land pixels outside every polygon (coast differences) take
     the nearest region.
  2. Regions under N land pixels fold into the neighbor region of the same country with the longest shared edge
     (an island or exclave with no such neighbor stays, and is counted).
  3. Each land location is cut into one piece per region it touches. Pieces under N pixels join, in order of
     preference, the neighbor in the same region, else the same country, with the longest edge. A sliver that joins a
     different region moves that unit border by a few pixels, and one that joins another parent changes that pixel's
     parent label; the report counts both.
Output arrays: ids, kind (by id), seeds (lon, lat), parent (original location id), region, region_names, region_country, box.
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

MIN_PX = int(os.environ.get("CUT_MIN_PX", "64"))
RMAX = 8192   # key multiplier; regions must stay below this


def log(*a):
    print(*a, flush=True)


def px(lon, lat):
    return (lon + 180.0) * W / 360.0, (90.0 - lat) * H / 180.0


def read_shp(folder, name):
    z = zipfile.ZipFile(os.path.join(folder, f"ne_10m_{name}.zip"))
    get = lambda ext: z.open([n for n in z.namelist() if n.endswith(ext)][0])
    r = shapefile.Reader(shp=get(".shp"), dbf=get(".dbf"), shx=get(".shx"))
    names = [f[0] for f in r.fields[1:]]
    return [(dict(zip(names, sr.record)), sr.shape) for sr in r.shapeRecords()]


def bbox_area(shp):
    bb = shp.bbox
    return (bb[2] - bb[0]) * (bb[3] - bb[1])


def regions_list(folder):
    """[(name, country_a3, shape)] : admin-1 units, plus admin-0 for countries with no admin-1 shape."""
    a1 = read_shp(folder, "admin_1_states_provinces")
    a0 = read_shp(folder, "admin_0_countries")
    have = {r["adm0_a3"] for r, _ in a1}
    out = [(f'{r["name"]} ({r["adm0_a3"]})', r["adm0_a3"], s) for r, s in a1]
    for r, s in a0:
        a3 = r.get("ADM0_A3")
        if a3 not in have:
            out.append((f'{r.get("NAME")} ({a3})', a3, s))
    return out


def rasterize(regions, box):
    x0, y0, x1, y1 = box
    img = Image.new("I;16", (x1 - x0, y1 - y0), 0)
    d = ImageDraw.Draw(img)
    for i in sorted(range(len(regions)), key=lambda i: -bbox_area(regions[i][2])):
        shp = regions[i][2]
        parts = list(shp.parts) + [len(shp.points)]
        for a, b in zip(parts[:-1], parts[1:]):
            ring = [px(*p) for p in shp.points[a:b]]
            if len(ring) < 3:
                continue
            if sum(ring[k][0] * ring[(k + 1) % len(ring)][1] - ring[(k + 1) % len(ring)][0] * ring[k][1] for k in range(len(ring))) > 0:
                d.polygon([(x - x0, y - y0) for x, y in ring], fill=i + 1)
    return np.asarray(img, np.uint16)


def neighbor_edges(lab):
    """dict label -> list of (shared edge length, neighbor label), 4-connected."""
    keys = []
    for a, b in ((lab[:, :-1], lab[:, 1:]), (lab[:-1, :], lab[1:, :])):
        m = (a != b) & (a > 0) & (b > 0)
        u, v = a[m].astype(np.int64), b[m].astype(np.int64)
        keys.append(np.concatenate([u * (1 << 32) + v, v * (1 << 32) + u]))
    uq, cnt = np.unique(np.concatenate(keys), return_counts=True)
    out = {}
    for k, c in zip(uq.tolist(), cnt.tolist()):
        out.setdefault(k >> 32, []).append((c, k & 0xFFFFFFFF))
    return out


def fold_small(lab, size, prefer):
    """target[label] after folding labels under MIN_PX into a neighbor. prefer(p, t) -> rank (lower is better) or None if not allowed."""
    edges = neighbor_edges(lab)
    target = np.arange(len(size))
    stuck = 0
    for p in sorted((q for q in range(1, len(size)) if 0 < size[q] < MIN_PX), key=lambda q: size[q]):
        cand = []
        for c, v in edges.get(p, []):
            t = int(target[v])
            r = prefer(p, t)
            if t != p and r is not None:
                cand.append((r, -c, t))
        if not cand:
            stuck += 1
            continue
        target[p] = min(cand)[2]
    for _ in range(8):
        target = target[target]
    return target, stuck


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
    regions = regions_list(folder)
    assert len(regions) < RMAX
    cid = np.array([""] + [r[1] for r in regions])
    log(f"{len(regions)} regions ({len(set(cid)) - 1} countries), window {x1 - x0}x{y1 - y0}, N = {MIN_PX} px")
    reg = rasterize(regions, box)
    land = (kind[ids] == 1) & (ids > 0)
    gap = land & (reg == 0)
    if gap.any():
        log(f"{int(gap.sum())} land pixels outside every region polygon; assigning the nearest region")
        idx = ndimage.distance_transform_edt(reg == 0, return_distances=False, return_indices=True)
        reg = np.where(gap, reg[idx[0], idx[1]], reg)
    reg = np.where(land, reg, 0)

    # step 2: fold tiny regions
    rsize = np.bincount(reg.ravel(), minlength=len(regions) + 1)
    tgt, stuck = fold_small(reg, rsize, lambda p, t: 0 if cid[p] == cid[t] else None)
    folded = int(((tgt != np.arange(len(tgt))) & (rsize > 0)).sum())
    reg = tgt[reg]
    log(f"regions with land: {int((rsize[1:] > 0).sum())}; {folded} under {MIN_PX} px folded into a same-country neighbor; {stuck} stay (no same-country neighbor)")

    # step 3: cut locations by region
    key = np.where(land, ids.astype(np.uint64) * RMAX + reg.astype(np.uint64), 0)
    uniq, inv = np.unique(key, return_inverse=True)
    inv = inv.reshape(key.shape).astype(np.int32)
    del key
    parent = (uniq // RMAX).astype(np.int64)
    rg = (uniq % RMAX).astype(np.int64)
    size = np.bincount(inv.ravel(), minlength=len(uniq))
    log(f"{len(uniq) - 1} pieces from {len(np.unique(ids[land]))} land locations; {int((size[1:] < MIN_PX).sum())} under {MIN_PX} px")

    def prefer(p, t):
        if rg[p] == rg[t]:
            return 0
        return 1 if cid[rg[p]] == cid[rg[t]] else None
    tgt2, stuck2 = fold_small(inv, size, prefer)
    keep = np.unique(tgt2[1:])
    new_id = np.zeros(len(uniq), np.int64)
    new_id[keep] = np.arange(1, len(keep) + 1)
    final = new_id[tgt2]
    n_land = len(keep)
    dt = np.uint16 if n_land + int(ids.max()) < 65535 else np.uint32
    out_ids = final[inv].astype(dt)
    out_ids[~land] = 0
    other = np.unique(ids[(ids > 0) & ~land])
    if len(other):
        lut = np.zeros(int(ids.max()) + 1, dt)
        lut[other] = np.arange(n_land + 1, n_land + 1 + len(other))
        out_ids = np.where((ids > 0) & ~land, lut[ids], out_ids).astype(dt)
    parent_o = np.concatenate([[0], parent[keep], other.astype(np.int64)])
    region_o = np.concatenate([[0], rg[keep], np.zeros(len(other), np.int64)])
    kind_o = np.concatenate([[0], np.ones(n_land, np.uint8), kind[other]]).astype(np.uint8)
    seeds_o = np.concatenate([[[0, 0]], seeds[parent[keep]], seeds[other]])
    np.savez_compressed(out_path, ids=out_ids, kind=kind_o, seeds=seeds_o, parent=parent_o, region=region_o,
                        region_names=np.array([""] + [r[0] for r in regions]), region_country=cid, box=np.array(box))

    # checks, on the arrays just built
    n_px = int(land.sum())
    log(f"atoms: {n_land} land ({len(uniq) - 1 - n_land} pieces merged away; {stuck2} under {MIN_PX} px stay, no same-country neighbor)")
    moved_reg = int((np.where(land, region_o[out_ids], 0) != np.where(land, reg, 0)).sum())
    ok_reg = bool((cid[region_o[out_ids]][land] == cid[reg][land]).all())
    log(f"every atom lies in one country: {'PASS' if ok_reg else 'FAIL'}; unit borders: {100 * (n_px - moved_reg) / max(n_px, 1):.4f}% of land pixels keep their 2020 unit, {moved_reg} sliver pixels moved to a same-country neighbor")
    changed = int((np.where(land, parent_o[out_ids], 0) != np.where(land, ids, 0)).sum())
    log(f"merge-up: {n_px - changed} of {n_px} land pixels ({100 * (n_px - changed) / max(n_px, 1):.4f}%) keep their original parent; {changed} sliver pixels moved")
    sizes = np.bincount(out_ids[land].astype(np.int64), minlength=n_land + 1)[1:n_land + 1]
    log(f"atom size px: min {sizes.min()}, median {int(np.median(sizes))}, max {sizes.max()}; under {MIN_PX}: {int((sizes < MIN_PX).sum())}")
    sys.exit(0 if ok_reg else 1)


if __name__ == "__main__":
    main()
