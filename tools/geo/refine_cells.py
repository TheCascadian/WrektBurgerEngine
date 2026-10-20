"""Cuts the land locations of a map along the first-level administrative borders of Natural Earth (public domain), so that every border of a province unit is a border of the map.

Usage: python tools/geo/refine_cells.py <natural earth admin_1 folder> <map folder> <output folder> [--window lon0,lat0,lon1,lat1]
Reads <map folder>/provinces.png and locations.txt, writes the same two files to <output folder> and cells.csv (new key, old key, adm1 code, adm0, area km2).
A land location is cut by the units it overlaps; pieces under MIN_KM2 join the neighbour they share the longest border with; the biggest piece keeps the old key and colour.
"""
import os
import re
import sys
import time

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import shp  # noqa: E402

Image.MAX_IMAGE_PIXELS = None
GRID_W, GRID_H = 16384, 8192          # the world grid of tools/map500bce/grid.py; the map keeps its top rows
MIN_KM2 = float(os.environ.get('CELL_MIN_KM2', '700'))


def log(*a):
    print(*a, flush=True)


def lon_of(c):
    return -180.0 + (c + 0.5) * 360.0 / GRID_W


def lat_of(r):
    return 90.0 - (r + 0.5) * 180.0 / GRID_H


def px_area_km2(rows):
    """Area of one pixel at each row, in square kilometres."""
    lat = np.radians(90.0 - (np.asarray(rows) + 0.5) * 180.0 / GRID_H)
    dy = 180.0 / GRID_H * 111.32
    dx = 360.0 / GRID_W * 111.32
    return dx * dy * np.cos(lat)


def read_locations(path):
    keys, colors, types = [], [], []
    head = []
    for line in open(path, encoding='utf-8'):
        m = re.match(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+) type = (\w+) \}', line)
        if m:
            colors.append(int(m.group(1), 16)); keys.append(m.group(2)); types.append(m.group(3))
        elif not colors:
            head.append(line)
    return head, keys, np.array(colors, np.uint32), types


def units_raster(folder, shape, row0, col0):
    """uint16 raster of the admin-1 units over the window (rows row0.., cols col0..); returns (raster, table of (code, adm0, name))."""
    base = os.path.join(folder, 'ne_10m_admin_1_states_provinces')
    S, R = shp.shapes(base), shp.records(base)
    order = sorted(range(len(S)), key=lambda i: -float(R[i]['area_sqkm'] or 0))
    img = Image.new('I', (shape[1], shape[0]), 0)
    d = ImageDraw.Draw(img)
    table = [('', '', '')]
    h, w = shape
    for n, i in enumerate(order, 1):
        table.append((R[i]['adm1_code'], R[i]['adm0_a3'], R[i]['name_en'] or R[i]['name']))
        for part in S[i]:
            ring = [((x + 180.0) * GRID_W / 360.0 - col0 - 0.5, (90.0 - y) * GRID_H / 180.0 - row0 - 0.5) for x, y in part]
            if len(ring) < 3:
                continue
            xs = [p[0] for p in ring]; ys = [p[1] for p in ring]
            if max(xs) < -2 or min(xs) > w + 2 or max(ys) < -2 or min(ys) > h + 2:
                continue
            d.polygon(ring, fill=n)
    return np.asarray(img, np.int32).astype(np.uint16), table


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    ne, mapdir, out = args[0], args[1], args[2]
    window = None
    if '--window' in sys.argv:
        window = [float(x) for x in sys.argv[sys.argv.index('--window') + 1].split(',')]
    os.makedirs(out, exist_ok=True)
    t0 = time.time()
    img = Image.open(os.path.join(mapdir, 'provinces.png')).convert('RGB')
    full_w, full_h = img.size
    assert full_w == GRID_W
    if window:
        c0 = int((window[0] + 180.0) * GRID_W / 360.0); c1 = int((window[2] + 180.0) * GRID_W / 360.0)
        r0 = int((90.0 - window[3]) * GRID_H / 180.0); r1 = int((90.0 - window[1]) * GRID_H / 180.0)
    else:
        c0, c1, r0, r1 = 0, full_w, 0, full_h
    rgb = np.asarray(img.crop((c0, r0, c1, r1)), np.uint8)
    del img
    col = (rgb[..., 0].astype(np.uint32) << 16) | (rgb[..., 1].astype(np.uint32) << 8) | rgb[..., 2].astype(np.uint32)
    del rgb
    head, keys, colors, types = read_locations(os.path.join(mapdir, 'locations.txt'))
    order = np.argsort(colors)
    idx = np.searchsorted(colors[order], col)
    idx[idx >= len(colors)] = 0
    old = np.where(colors[order][idx] == col, order[idx] + 1, 0).astype(np.uint32)     # 1-based index into keys; 0 = unknown
    del idx, col
    log(f'map read: {old.shape}, {len(keys)} locations, {time.time() - t0:.0f}s')
    unit, table = units_raster(ne, old.shape, r0, c0)
    log(f'units drawn: {len(table) - 1}, {time.time() - t0:.0f}s')

    land_old = np.array([t == 'land' for t in types] + [False])           # by 1-based index: land? (index 0 -> keys[-1]; patched below)
    is_land = np.zeros(len(keys) + 1, bool)
    for i, t in enumerate(types, 1):
        is_land[i] = (t == 'land')
    rows = np.arange(r0, r1)
    area_row = px_area_km2(rows).astype(np.float32)

    new = old.copy()                                                         # new location index raster (1-based into the output table)
    next_idx = len(keys) + 1
    out_keys = list(keys); out_types = list(types); out_colors = list(int(c) for c in colors)
    parent = list(range(len(keys)))
    info = {}                                                                # new index -> (adm1 code, adm0, name)
    used = set(out_colors)
    land_ids = [i for i in range(1, len(keys) + 1) if is_land[i]]
    objs = ndimage.find_objects(old.astype(np.int32), max_label=len(keys))
    log(f'cutting {len(land_ids)} land locations, {time.time() - t0:.0f}s')
    next_num = max(int(k.split('_')[1]) for k, t in zip(keys, types) if t == 'land') + 1
    next_col = 0x010203
    made = 0
    for n_done, i in enumerate(land_ids):
        sl = objs[i - 1]
        if sl is None:
            continue
        mask = old[sl] == i
        u = unit[sl]
        comb = np.where(mask, u.astype(np.int64) + 1, 0)                     # 0 outside, 1+unit inside
        vals = np.unique(comb[mask])
        pieces = []                                                         # (mask, unit)
        for v in vals:
            lab, nl = ndimage.label(comb == v)
            for k in range(1, nl + 1):
                pieces.append([lab == k, int(v) - 1])
        if len(pieces) == 1:
            u0 = pieces[0][1]
            info[i] = table[u0] if u0 else ('', '', '')
            continue
        ar = area_row[sl[0]][:, None]
        areas = [float((m * ar).sum()) for m, _ in pieces]
        # merge the small pieces into the neighbour they share most border with
        changed = True
        while changed and len(pieces) > 1:
            changed = False
            small = min(range(len(pieces)), key=lambda k: areas[k])
            if areas[small] >= MIN_KM2:
                break
            grow = ndimage.binary_dilation(pieces[small][0])
            best, bestn = None, 0
            for k in range(len(pieces)):
                if k == small:
                    continue
                nb = int((grow & pieces[k][0]).sum())
                if nb > bestn:
                    best, bestn = k, nb
            if best is None:
                areas[small] = 1e18      # an enclave: keep as it is
                continue
            pieces[best][0] = pieces[best][0] | pieces[small][0]; areas[best] += areas[small]
            del pieces[small]; del areas[small]
            changed = True
        areas = [a if a < 1e17 else MIN_KM2 for a in areas]
        big = int(np.argmax([float((m * ar).sum()) for m, _ in pieces]))
        for k, (m, u0) in enumerate(pieces):
            if k == big:
                info[i] = table[u0] if u0 else ('', '', '')
                continue
            while next_col in used:
                next_col += 0x010101 if False else 0x01
            j = next_idx; next_idx += 1
            out_keys.append(f'land_{next_num:05d}'); next_num += 1
            out_types.append('land'); out_colors.append(next_col); used.add(next_col); next_col += 0x00000F + 1
            parent.append(i - 1)
            info[j] = table[u0] if u0 else ('', '', '')
            sub = new[sl]
            sub[m] = j
            made += 1
        if n_done % 2000 == 0:
            log(f'  {n_done}/{len(land_ids)} done, {made} new cells, {time.time() - t0:.0f}s')
    log(f'new cells: {made}; total locations {len(out_keys)}; {time.time() - t0:.0f}s')

    lut = np.zeros((len(out_keys) + 1, 3), np.uint8)
    for j, c in enumerate(out_colors, 1):
        lut[j] = ((c >> 16) & 255, (c >> 8) & 255, c & 255)
    Image.fromarray(lut[new]).save(os.path.join(out, 'provinces.png'), compress_level=1)
    with open(os.path.join(out, 'locations.txt'), 'w', encoding='utf-8') as f:
        f.writelines(head)
        for k, c, t in zip(out_keys, out_colors, out_types):
            f.write(f'{c:06X} = {{ id = {k} type = {t} }}\n')
    with open(os.path.join(out, 'cells.csv'), 'w', encoding='utf-8') as f:
        f.write('key,old,adm1,adm0,name\n')
        for j, k in enumerate(out_keys, 1):
            a = info.get(j, ('', '', ''))
            f.write(f'{k},{out_keys[parent[j - 1]]},{a[0]},{a[1]},{a[2].replace(",", " ")}\n')
    log(f'written to {out}, {time.time() - t0:.0f}s')


if __name__ == '__main__':
    main()
