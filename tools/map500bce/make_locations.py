"""Location grid for the 500 BCE map: naturalistic cells that follow rivers, no heightmap needed yet.

Usage: python3 make_locations.py <ne_raster .npz from rasterize_ne.py> <output .npz> [gebco .npz from gebco_to_grid.py]
With the GEBCO file, terrain comes from real relief: ridge crests (topographic position), slope and elevation
shape the cells and the seed density. Without it only noise and rivers are used.

Method (all counts and weights are provisional settings):
  1. Work on a grid S times smaller per side (default 4, i.e. 4096x2048) and expand at the end.
  2. Seeds: area-weighted random points relaxed with Lloyd iterations. Above POLAR_DEG latitude the weight is
     held at a floor so polar cells stay readable on the equirectangular image instead of smearing sideways.
  3. Cells: compact watershed from the seeds over a "terrain" image made of smooth multi-octave noise
     plus river ridges (Natural Earth rivers, public domain). Flooding is slower over ridges, so borders
     settle on rivers where one is nearby and wander irregularly elsewhere. Seeds are the only input
     that is not drawn from the rasters; the noise uses a fixed seed so runs repeat.
  4. Lakes above MIN_LAKE_PX become one lake location each; smaller lakes are filled into land.
  5. Landmasses centered south of 60S (Antarctica) become one impassable location; landmasses below
     MIN_ISLAND_PX get no location.
Output: ids (uint16 full grid, 0 = none), kind (by id: 1 land, 2 sea, 3 lake, 4 impassable), seeds (lon, lat by id).
"""
import os
import sys
import time

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
from skimage.segmentation import watershed

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grid import H, W  # noqa: E402

S = int(os.environ.get("LOC_DOWN", "4"))
h, w = H // S, W // S
N_LAND, N_SEA = 19500, int(os.environ.get("LOC_SEA", "2500"))
MIN_ISLAND_PX = max(1, 64 // (S * S) * 4)       # coarse px; smaller islands become part of the nearest sea zone
MAX_KM2, SPLIT_KM2, MIN_KM2 = 60000.0, 30000.0, 400.0   # land cells above MAX are split, below MIN merged into a neighbor
MIN_LAKE_PX = max(1, 4000 // (S * S))
COAST_SHARE = 0.65
COAST_PX = max(1, 90 * W // 16384 // S)
POLAR_DEG, POLAR_FLOOR = 70.0, 0.34       # cos(70 deg)
RIVER_RIDGE = float(os.environ.get("LOC_RIDGE", "3.0"))
NOISE_AMP, COMPACT = 0.55, float(os.environ.get("LOC_COMPACT", "0.012"))
RELIEF_WIN, TPI_SCALE = 31, 250.0      # coarse pixels (about 2.7 deg) and meters
W_RIDGE, W_SLOPE = float(os.environ.get("LOC_WRIDGE", "3.0")), float(os.environ.get("LOC_WSLOPE", "1.0"))
RIVER_R, COAST_R, HIGHLAT = 6.0, 12.0, 60.0   # density falloff distances in coarse pixels; latitude above which cells grow
DENS_MIN = 0.12                                # density far from rivers and coasts (relative to 1.0 at a river mouth)
# Relevance for a 500 BCE game: regions that held almost nobody and mattered to no state get big, few cells, and small islands
# there get none. These regions are the owner's call (Greenland, the Sahara away from the Nile, the Canadian Arctic islands,
# the far Arctic). REL is 1 everywhere else. Nothing here claims a population number.
REL_LOW = float(os.environ.get("LOC_REL_LOW", "0.2"))
DROP_KM2 = float(os.environ.get("LOC_DROP_KM2", "150000"))   # landmasses below this inside a low-relevance region get no location
rng = np.random.default_rng(500)
LAT = 90.0 - (np.arange(h) + 0.5) * 180.0 / h
LON = (np.arange(w) + 0.5) * 360.0 / w - 180.0
WEIGHT = np.maximum(np.cos(np.radians(LAT)), POLAR_FLOOR)


def make_rel(river):
    """Relevance raster (1 = normal). Greenland, the Sahara (kept near rivers, so the Nile valley stays), the Canadian Arctic
    and everything north of 72N are low."""
    la, lo = LAT[:, None], LON[None, :]
    box = lambda lat0, lat1, lon0, lon1: (la >= lat0) & (la <= lat1) & (lo >= lon0) & (lo <= lon1)
    low = box(59, 84, -75, -10) | box(66, 84, -125, -55) | (la > 72)
    sahara = box(15, 31, -17, 35)
    rd = ndimage.distance_transform_edt(~river)
    rel = np.ones((h, w), np.float32)
    rel[low] = REL_LOW
    near = np.exp(-rd / (2 * RIVER_R))
    rel[sahara] = np.maximum(REL_LOW, near[sahara])
    return ndimage.gaussian_filter(rel, 2.0).clip(REL_LOW, 1.0)


def log(*a):
    print(f"[{time.strftime('%H:%M:%S')}]", *a, flush=True)


def planar(r, c):
    """Image-space coordinates, horizontally compressed by the (floored) cosine of latitude."""
    return np.stack([c * WEIGHT[r], r.astype(float)], axis=1)


def noise():
    out = np.zeros((h, w), np.float32)
    amp = 1.0
    for cells in (24, 64, 160, 400):
        g = rng.random((cells // 2 + 2, cells + 2)).astype(np.float32)
        out += amp * ndimage.zoom(g, (h / (cells // 2), w / cells), order=3)[:h, :w]
        amp *= 0.55
    out -= out.min()
    return out / out.max()


def relax(r, c, k, iters=5):
    """Lloyd relaxation in image space; returns (row, col) integer seeds."""
    k = min(k, len(r))
    pts = planar(r, c)
    seeds = pts[rng.choice(len(pts), k, replace=False)]
    for _ in range(iters):
        _, lab = cKDTree(seeds).query(pts, workers=-1)
        cnt = np.bincount(lab, minlength=k)
        ok = cnt > 0
        for i in range(2):
            seeds[ok, i] = np.bincount(lab, pts[:, i], k)[ok] / cnt[ok]
    rr = np.clip(np.rint(seeds[:, 1]).astype(int), 0, h - 1)
    cc = np.clip(np.rint(seeds[:, 0] / WEIGHT[rr]).astype(int), 0, w - 1)
    return rr, cc


def snap(rr, cc, mask):
    """Move seeds that fell outside mask to the nearest pixel inside it."""
    bad = ~mask[rr, cc]
    if bad.any():
        idx = ndimage.distance_transform_edt(~mask, return_distances=False, return_indices=True)
        rr, cc = rr.copy(), cc.copy()
        rr[bad], cc[bad] = idx[0][rr[bad], cc[bad]], idx[1][rr[bad], cc[bad]]
    return rr, cc


DENS = None


def xyz_of(lat_deg, lon_deg):
    la, lo = np.radians(lat_deg), np.radians(lon_deg)
    return np.stack([np.cos(la) * np.cos(lo), np.cos(la) * np.sin(lo), np.sin(la) * np.ones_like(lo)], axis=-1)


def lonlat(p):
    return np.stack([np.degrees(np.arctan2(p[:, 1], p[:, 0])), np.degrees(np.arcsin(np.clip(p[:, 2], -1, 1)))], axis=1)


def sea_voronoi_seeds(sea_ok):
    """N_SEA seeds spread evenly by true surface area over the sea, relaxed so cells come out near equal size."""
    r, c = np.nonzero(sea_ok)
    keep = rng.random(len(r)) < np.cos(np.radians(LAT[r]))
    r, c = r[keep], c[keep]
    pts = xyz_of(LAT[r], LON[c])
    seeds = pts[rng.choice(len(pts), N_SEA, replace=False)]
    for _ in range(12):
        _, lab = cKDTree(seeds).query(pts, workers=-1)
        sums = np.stack([np.bincount(lab, pts[:, i], N_SEA) for i in range(3)], axis=1)
        ok = np.linalg.norm(sums, axis=1) > 0
        seeds[ok] = sums[ok] / np.linalg.norm(sums[ok], axis=1, keepdims=True)
    return pts, seeds


def make_density(river, solidmask):
    """Relative seed density (high = many small cells). Proxy for habitability until real climate and population
    layers exist: river and coast proximity raise it, high latitude lowers it. Replace with a raster when data lands."""
    rd = ndimage.distance_transform_edt(~river)
    cd = ndimage.distance_transform_edt(solidmask)       # land: distance to coast (water); sea handled separately
    sd = ndimage.distance_transform_edt(~solidmask)      # sea: distance to land
    land = DENS_MIN + 0.9 * np.exp(-rd / RIVER_R) + 0.5 * np.exp(-cd / COAST_R)
    lat = np.abs(LAT)[:, None]
    land = land * np.where(lat > HIGHLAT, np.clip(1 - (lat - HIGHLAT) / 40, 0.25, 1), 1)
    sea = 0.06 + 1.0 * np.exp(-sd / COAST_PX)
    return np.where(solidmask, land, sea).astype(np.float32)


def pick(mask):
    r, c = np.nonzero(mask)
    keep = rng.random(len(r)) < WEIGHT[r] * DENS[r, c] / DENS.max()
    return r[keep], c[keep]


WARP = ((250, 16.0), (600, 10.0), (1400, 5.0), (3000, 2.5))   # (noise cells across the map, amplitude in full-res pixels); cells are about 45 px across


def warp(full, solid_f, damp, first_sea, band=512):
    """Displace every border with multi-scale noise so straight Voronoi edges become irregular, meandering lines.
    A pixel only takes the id of its displaced neighbor if that neighbor is the same kind (land/sea)."""
    wr = np.random.default_rng(1500)
    f = []
    for _ in range(2):
        a = np.zeros((h, w), np.float32)
        for cells, amp in WARP:
            g = wr.random((cells // 2 + 2, cells + 2)).astype(np.float32) - 0.5
            a += amp * 2 * ndimage.zoom(g, (h / (cells // 2), w / cells), order=3)[:h, :w]
        f.append(a)
    dy, dx = f[0] * damp, f[1] * damp
    out = full.copy()
    for r0 in range(0, H, band):
        r1 = min(r0 + band, H)
        rr, cc = np.mgrid[r0:r1, 0:W].astype(np.float32)
        co = [rr / S - 0.5, cc / S - 0.5]
        sy = ndimage.map_coordinates(dy, co, order=1, mode="nearest")
        sx = ndimage.map_coordinates(dx, co, order=1, mode="grid-wrap")
        yy = np.clip(np.rint(rr + sy), 0, H - 1).astype(np.int32)
        xx = np.rint(cc + sx).astype(np.int32) % W
        cand = full[yy, xx]
        ok = (solid_f[yy, xx] == solid_f[r0:r1]) & solid_f[r0:r1]    # sea borders stay straight Voronoi
        out[r0:r1] = np.where(ok, cand, full[r0:r1])
    return out


def pixel_km2():
    return (np.cos(np.radians(LAT))[:, None] * np.ones(w) * (111.32 * 180 / h) ** 2)


def merge_into_neighbor(ids, i, sl, valid):
    """Give every pixel of cell i the most common valid neighbor id. Returns False if it has no neighbor."""
    r0, r1 = max(sl[0].start - 1, 0), min(sl[0].stop + 1, ids.shape[0])
    c0, c1 = max(sl[1].start - 1, 0), min(sl[1].stop + 1, ids.shape[1])
    sub = ids[r0:r1, c0:c1]
    m = sub == i
    ring = ndimage.binary_dilation(m, np.ones((3, 3), bool)) & ~m
    nb = sub[ring]
    nb = nb[(nb != i) & valid[nb]]
    if len(nb) == 0:
        return False
    sub[m] = np.bincount(nb).argmax()
    return True


def fix_fragments(ids, first, last, label=""):
    """Keep only the largest connected piece of each cell first..last; other pieces join their most common neighbor."""
    valid = np.zeros(int(ids.max()) + 2, bool)
    valid[first:last + 1] = True
    objs = ndimage.find_objects(ids.astype(np.int32))
    moved = lost = 0
    for i in range(first, last + 1):
        if i - 1 >= len(objs) or objs[i - 1] is None:
            continue
        sl = objs[i - 1]
        m = ids[sl] == i
        lab_, k = ndimage.label(m)
        if k <= 1:
            continue
        sizes = np.bincount(lab_.ravel())[1:]
        keep = int(sizes.argmax()) + 1
        for j, frag in enumerate(ndimage.find_objects(lab_), 1):
            if j == keep:
                continue
            fs = (slice(sl[0].start + frag[0].start, sl[0].start + frag[0].stop), slice(sl[1].start + frag[1].start, sl[1].start + frag[1].stop))
            fm = lab_[frag] == j
            tmp = ids[fs]
            tmp[fm] = 0                                    # take it out of cell i, then hand it to a neighbor
            r0, r1 = max(fs[0].start - 1, 0), min(fs[0].stop + 1, ids.shape[0])
            c0, c1 = max(fs[1].start - 1, 0), min(fs[1].stop + 1, ids.shape[1])
            sub = ids[r0:r1, c0:c1]
            mm = np.zeros(sub.shape, bool)
            mm[fs[0].start - r0:fs[0].stop - r0, fs[1].start - c0:fs[1].stop - c0] = fm
            ring = ndimage.binary_dilation(mm, np.ones((3, 3), bool)) & ~mm
            nb = sub[ring]
            nb = nb[(nb != i) & valid[nb]]
            if len(nb):
                sub[mm] = np.bincount(nb).argmax(); moved += int(fm.sum())
            else:
                sub[mm] = i; lost += int(fm.sum())         # isolated piece (island part): leave it
    log(f"{label} fragments: {moved:,} px handed to neighbors, {lost:,} px isolated and kept")


def refine_land(ids, kind0):
    """Split oversized land cells, merge tiny ones, drop fragments. kind0[i] is the kind of id i (index 0 unused)."""
    kind = list(kind0)
    pk = pixel_km2()
    n = len(kind) - 1
    area = np.bincount(ids.ravel(), pk.ravel(), n + 1)
    objs = ndimage.find_objects(ids.astype(np.int32))
    nxt = n + 1
    splits = 0
    relc = np.bincount(ids.ravel(), (pk * REL).ravel(), n + 1) / np.maximum(area[:n + 1], 1e-9)
    relc = np.clip(relc, REL_LOW, 1.0)
    for i in np.nonzero((area[:n + 1] > MAX_KM2 / np.maximum(relc, REL_LOW)))[0]:
        if i == 0 or kind[i] != 1 or objs[i - 1] is None:
            continue
        sl = objs[i - 1]
        r, c = np.nonzero(ids[sl] == i)
        r, c = r + sl[0].start, c + sl[1].start
        k = int(np.ceil(area[i] / (SPLIT_KM2 / relc[i])))
        if len(r) < k * 4:
            continue
        pts = xyz_of(LAT[r], LON[c])
        sd = pts[rng.choice(len(pts), k, replace=False)]
        for _ in range(6):
            _, lb = cKDTree(sd).query(pts)
            sums = np.stack([np.bincount(lb, pts[:, j], k) for j in range(3)], axis=1)
            ok = np.linalg.norm(sums, axis=1) > 0
            sd[ok] = sums[ok] / np.linalg.norm(sums[ok], axis=1, keepdims=True)
        _, lb = cKDTree(sd).query(pts)
        ids[r, c] = np.where(lb == 0, i, nxt + lb - 1).astype(np.uint16)
        kind += [1] * (k - 1); nxt += k - 1; splits += 1
    log(f"split {splits} oversized land cells")
    area = np.bincount(ids.ravel(), pk.ravel(), nxt)
    objs = ndimage.find_objects(ids.astype(np.int32))
    valid = np.zeros(nxt + 1, bool)
    valid[[j for j in range(1, nxt) if kind[j] == 1]] = True
    merged = 0
    for i in np.argsort(area[:nxt]):
        if i == 0 or kind[i] != 1 or area[i] <= 0 or area[i] >= MIN_KM2 or i - 1 >= len(objs) or objs[i - 1] is None:
            continue
        if merge_into_neighbor(ids, i, objs[i - 1], valid):
            merged += 1
    log(f"merged {merged} tiny land cells")
    fix_fragments(ids, 1, nxt - 1, "land")
    # renumber, dropping empty cells
    cnt = np.bincount(ids.ravel(), minlength=nxt)
    present = cnt > 0
    present[0] = False
    lut = np.zeros(nxt, np.uint16)
    lut[present] = np.arange(1, present.sum() + 1)
    ids[:] = lut[ids]
    newkind = [0] + [kind[j] for j in range(1, nxt) if present[j]]
    sums = [np.bincount(ids.ravel(), None) for _ in range(0)]
    pts = []
    rr, cc = np.nonzero(ids > 0)
    lab_ = ids[rr, cc]
    xyzp = xyz_of(LAT[rr], LON[cc])
    m = len(newkind)
    cs = np.stack([np.bincount(lab_, xyzp[:, j], m) for j in range(3)], axis=1)[1:]
    return ids, newkind, lonlat(cs / np.maximum(np.linalg.norm(cs, axis=1, keepdims=True), 1e-9))


def main():
    src, out = sys.argv[1], sys.argv[2]
    d = np.load(src)
    t = time.time()
    blk = lambda a: a.reshape(h, S, w, S).any(axis=(1, 3))
    land_f, lake_f = d["land"].astype(bool), d["lake"].astype(bool)
    land, lake, river = blk(land_f), blk(lake_f), blk(d["river"].astype(bool))
    log(f"coarse grid {w}x{h} (S={S})")
    global DENS
    solidmask = land | lake
    global REL
    REL = make_rel(river)
    DENS = make_density(river, solidmask) * REL
    terrain = NOISE_AMP * noise()
    tpi = None
    if len(sys.argv) > 3:
        g = np.load(sys.argv[3])["mean"].astype(np.float32)
        elev = g.reshape(h, S, w, S).mean(axis=(1, 3))
        tpi = elev - ndimage.uniform_filter(elev, size=RELIEF_WIN, mode="wrap")     # > 0 on crests, < 0 in valleys
        gy, gx = np.gradient(ndimage.gaussian_filter(elev, 1.0))
        slope = np.hypot(gy, gx)
        slope /= np.percentile(slope[solidmask], 95)
        terrain = 0.25 * terrain + W_RIDGE * np.clip(tpi / TPI_SCALE, 0, 1) + W_SLOPE * np.clip(slope, 0, 1.5)
        high = np.clip((elev - 1500) / 2500, 0, 1)               # high plateaus and ranges: fewer, larger provinces
        DENS *= (1 - 0.6 * high * solidmask).astype(np.float32)
        log(f"relief terrain from {sys.argv[3]}")
    terrain[river] = RIVER_RIDGE
    solidmask = land | lake
    lab, n = ndimage.label(solidmask, structure=np.ones((3, 3)))
    idx = np.arange(1, n + 1)
    sizes = ndimage.sum(np.ones(lab.shape, np.uint8), lab, idx)
    big = idx[sizes >= MIN_ISLAND_PX]
    relm = ndimage.mean(REL, lab, big)
    km2 = ndimage.sum(pixel_km2(), lab, big)
    drop = (relm < 0.5) & (km2 < DROP_KM2)
    log(f"dropping {int(drop.sum())} small landmasses in low-relevance regions")
    big = big[~drop]
    objs = ndimage.find_objects(lab)
    polar = np.array([LAT[int((objs[b - 1][0].start + objs[b - 1][0].stop) / 2)] < -60 for b in big])
    area = ndimage.sum(WEIGHT[:, None] * DENS * solidmask, lab, big) * ~polar
    ll, nl = ndimage.label(lake)
    lsz = ndimage.sum(np.ones(ll.shape, np.uint8), ll, np.arange(1, nl + 1))
    lake_ids = np.arange(1, nl + 1)[lsz >= MIN_LAKE_PX]
    biglake = np.isin(ll, lake_ids)
    solid = solidmask & ~biglake
    log(f"{n} landmasses, {len(big)} kept, {int(polar.sum())} polar, {len(lake_ids)} lake locations")

    share = area / area.sum() * (N_LAND - len(big))
    counts = 1 + np.floor(share).astype(int)
    counts[np.argsort(-(share - np.floor(share)))[:max(N_LAND - counts.sum(), 0)]] += 1
    counts[polar] = 1
    sr, sc, skind = [], [], []
    for b, k, pol in zip(big, counts, polar):
        sl = objs[b - 1]
        m = np.zeros((h, w), bool)
        m[sl] = (lab[sl] == b) & solid[sl]
        if not m.any():
            continue
        r, c = pick(m)
        if k == 1 or len(r) < k:
            r, c = np.nonzero(m)
            rr, cc = np.array([int(r.mean())]), np.array([int(c.mean())])
        else:
            rr, cc = relax(r, c, k)
        rr, cc = snap(rr, cc, m)
        sr.append(rr); sc.append(cc); skind += [4 if pol else 1] * len(rr)
    sr, sc = np.concatenate(sr), np.concatenate(sc)
    n_land = len(sr)
    log(f"land seeds {n_land}  ({time.time() - t:.1f}s)")
    markers = np.zeros((h, w), np.int32)
    markers[sr, sc] = np.arange(1, n_land + 1)
    land_ids = watershed(terrain, markers, mask=solid, compactness=COMPACT).astype(np.uint16)
    log(f"land cells  ({time.time() - t:.1f}s)")

    # sea: coastal belt denser than open water
    sea_ok = ~solidmask
    sea_pts, sea_seeds = sea_voronoi_seeds(sea_ok)
    npr = len(sea_seeds)
    log(f"sea seeds {npr}  ({time.time() - t:.1f}s)")

    # assemble coarse ids: land 1..n_land, lakes next, sea after
    ids = np.zeros((h, w), np.uint16)
    ids[solid] = land_ids[solid]
    ids, kinds, land_seeds = refine_land(ids, [0] + skind)
    n_land = len(kinds) - 1
    log(f"land cells after cleanup: {n_land}")
    nxt = n_land + 1
    seeds = [np.zeros((1, 2)), land_seeds]
    for li in lake_ids:
        m = ll == li
        ids[m] = nxt
        r, c = np.nonzero(m)
        seeds.append(np.array([[LON[int(c.mean())], LAT[int(r.mean())]]])); kinds.append(3); nxt += 1
    sea_first = nxt
    seeds.append(lonlat(sea_seeds)); kinds += [2] * npr
    total = sea_first - 1 + npr
    log(f"total ids {total}")

    if tpi is not None:
        bb = np.zeros((h, w), bool)
        bb[:, 1:] |= ids[:, 1:] != ids[:, :-1]; bb[1:] |= ids[1:] != ids[:-1]
        bb &= land
        log(f"mean relief position on land borders {tpi[bb].mean():+.1f} m vs all land {tpi[land].mean():+.1f} m (higher = borders on crests)")
    cell_km2 = np.bincount(ids.ravel(), (np.cos(np.radians(LAT))[:, None] * np.ones(w) * (111.32 * 180 / h) ** 2).ravel(), total + 1)
    for name, k in (("land", 1),):
        a = cell_km2[1:][np.array(kinds[1:]) == k]
        log(f"{name} cell km2: min {a.min():,.0f}  p10 {np.percentile(a, 10):,.0f}  median {np.median(a):,.0f}  p90 {np.percentile(a, 90):,.0f}  max {a.max():,.0f}")
    # expand to full resolution: land pixels take the nearest coarse land/lake id, sea pixels the sea id
    has = (ids > 0) & solidmask
    idx2 = ndimage.distance_transform_edt(~has, return_distances=False, return_indices=True)
    land_ext = ids[idx2[0], idx2[1]]
    up = lambda a: np.repeat(np.repeat(a, S, axis=0), S, axis=1)
    full = up(land_ext).astype(np.uint16)
    keepc = np.isin(lab, big)
    kept_f = (land_f | lake_f) & up(keepc)                 # land of kept landmasses; smaller islets join the sea zones
    tree = cKDTree(sea_seeds)
    flat_lat = 90.0 - (np.arange(H) + 0.5) * 180.0 / H
    flat_lon = (np.arange(W) + 0.5) * 360.0 / W - 180.0
    for r0 in range(0, H, 256):
        r1 = min(r0 + 256, H)
        m = ~kept_f[r0:r1]
        rr, cc = np.nonzero(m)
        _, lb = tree.query(xyz_of(flat_lat[r0 + rr], flat_lon[cc]), workers=-1)
        full[r0 + rr, cc] = (sea_first + lb).astype(np.uint16)
    log(f"sea Voronoi at full resolution  ({time.time() - t:.1f}s)")
    damp = np.clip(ndimage.distance_transform_edt(~river) / 3.0, 0.05, 1.0).astype(np.float32)   # borders on rivers stay put
    full = warp(full, kept_f, damp, sea_first)
    log(f"borders warped  ({time.time() - t:.1f}s)")
    fix_fragments(full, 1, n_land, "land (full resolution)")
    cnt = np.bincount(full.ravel(), minlength=total + 1)
    present = cnt > 0
    present[0] = False
    lut = np.zeros(total + 1, np.uint16)
    lut[present] = np.arange(1, present.sum() + 1)
    full = lut[full]
    kinds = [0] + [k for k, pr_ in zip(kinds[1:], present[1:]) if pr_]
    seeds_all = np.concatenate(seeds)
    seeds_all = np.concatenate([seeds_all[:1], seeds_all[1:][present[1:]]])
    log(f"{int((~present[1:]).sum())} empty cells dropped; {len(kinds) - 1} locations")
    bad = int(((full >= 0) & kept_f & (np.array(kinds, np.uint8)[full] == 2)).sum()) + int((~kept_f & (np.array(kinds, np.uint8)[full] != 2)).sum())
    log(f"full-res pixels with the wrong kind of id: {bad}")
    np.savez_compressed(out, ids=full, kind=np.array(kinds, np.uint8), seeds=seeds_all)
    log("wrote", out, f"({time.time() - t:.1f}s)")


if __name__ == "__main__":
    main()
