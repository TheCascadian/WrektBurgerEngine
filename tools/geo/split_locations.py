"""Cuts the big land locations of a project into smaller ones, so that borders drawn by hand (polygons, boxes) have a province to bite on.

usage: python tools/geo/split_locations.py <game dir> [fine_km2] [other_km2] [--all]
  A land location above `fine_km2` that lies at (or near) a border drawn by hand in tools/gazetteer (a P polygon or a U box smaller than ZONE_MAX_DEG2 square degrees) is
  cut into equal parts along its longest direction (median cuts); with --all every location above `fine_km2` in the fine region (Europe, the Mediterranean, the Near
  East: lon -12..50, lat 28..56) is cut. Locations above `other_km2` anywhere are cut either way. The biggest part keeps the old location; the others get new keys and colours in map/provinces.png, new lines in
  map/locations.txt and map/cells.csv, and join the state of the old location (map/states/00_states.txt). Impassable locations stay impassable.
  The old files are kept as *.before-split. Defaults: 1500 and 8000 km2.
"""
import collections
import csv
import math
import os
import re
import shutil
import sys

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

FINE_BOX = (-12.0, 28.0, 50.0, 56.0)
ZONE_MAX_DEG2 = 5.0      # drawn borders of realms smaller than this make the provinces they touch fine


def zone_polygons():
    """The small drawn realms of the gazetteer (polygons and the boxes of unit realms) as [(minx, miny, maxx, maxy, [(x, y), ...])]."""
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, os.path.join(here, '..'))
    import history_gazetteer as G
    out = []
    for e in G.ENTRIES:
        polys = []
        if 'poly' in e:
            polys.append(e['poly'])
        for tok in e.get('units', []):
            if '@' in tok:
                w, s, ea, n = (float(x) for x in tok.split('@')[1].split('/'))
                polys.append([(w, s), (ea, s), (ea, n), (w, n)])
        for poly in polys:
            if G.area(poly) <= ZONE_MAX_DEG2:
                xs = [p[0] for p in poly]; ys = [p[1] for p in poly]
                out.append((min(xs), min(ys), max(xs), max(ys), poly))
    return out


def near_zone(lon, lat, r, zones):
    """Is the point inside a zone polygon, or within r degrees of its edge?"""
    for x0, y0, x1, y1, poly in zones:
        if lon < x0 - r or lon > x1 + r or lat < y0 - r or lat > y1 + r:
            continue
        c = False
        j = len(poly) - 1
        d2 = float('inf')
        for i in range(len(poly)):
            xi, yi = poly[i]; xj, yj = poly[j]
            if (yi > lat) != (yj > lat) and lon < (xj - xi) * (lat - yi) / (yj - yi) + xi:
                c = not c
            dx, dy = xj - xi, yj - yi
            L = dx * dx + dy * dy
            u = 0.0 if L == 0 else max(0.0, min(1.0, ((lon - xi) * dx + (lat - yi) * dy) / L))
            d2 = min(d2, (lon - xi - u * dx) ** 2 + (lat - yi - u * dy) ** 2)
            j = i
        if c or d2 <= r * r:
            return True
    return False


def bisect(xs, ys, k):
    """Index arrays of k parts of equal size (by pixel count) of the points: median cuts along the long axis."""
    if k <= 1 or len(xs) < 2:
        return [np.arange(len(xs))]
    pts = np.column_stack([xs, ys]).astype(np.float64)
    pts -= pts.mean(axis=0)
    w, v = np.linalg.eigh(np.cov(pts.T) if len(pts) > 2 else np.eye(2))
    axis = v[:, int(np.argmax(w))]
    order = np.argsort(pts @ axis, kind='stable')
    k1 = k // 2
    cut = int(round(len(order) * k1 / k))
    a, b = order[:cut], order[cut:]
    out = []
    for part, kk in ((a, k1), (b, k - k1)):
        for sub in bisect(xs[part], ys[part], kk):
            out.append(part[sub])
    return out


def main():
    game = sys.argv[1]
    fine_t = float(sys.argv[2]) if len(sys.argv) > 2 else 1500.0
    other_t = float(sys.argv[3]) if len(sys.argv) > 3 and not sys.argv[3].startswith('--') else 8000.0
    everything = '--all' in sys.argv
    zones = [] if everything else zone_polygons()
    mapdir = os.path.join(game, 'map')
    loc_path = os.path.join(mapdir, 'locations.txt')
    loc_text = open(loc_path, encoding='utf-8', newline='').read()
    colour_info = {}                     # colour -> (id, type)
    for m in re.finditer(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+) type = (\w+) \}', loc_text, re.M):
        colour_info[int(m.group(1), 16)] = (m.group(2), m.group(3))
    top = max(int(i.split('_')[1]) for i, t in colour_info.values() if i.startswith('land_'))
    img = np.array(Image.open(os.path.join(mapdir, 'provinces.png')).convert('RGB'))
    H, W, _ = img.shape
    flat = ((img[:, :, 0].astype(np.uint32) << 16) | (img[:, :, 1].astype(np.uint32) << 8) | img[:, :, 2]).ravel()
    degpx = 360.0 / W
    lat_row = 90.0 - (np.arange(H) + 0.5) * degpx
    parea = (degpx * 111.32) ** 2 * np.cos(np.radians(lat_row))
    used = set(colour_info) | set(np.unique(flat).tolist())
    rng = np.random.default_rng(12345)

    def new_colour():
        while True:
            c = int(rng.integers(0x101010, 0xF0F0F0))
            if c not in used:
                used.add(c)
                return c

    order = np.argsort(flat, kind='stable')
    srt = flat[order]
    starts = np.concatenate([[0], np.flatnonzero(srt[1:] != srt[:-1]) + 1, [len(srt)]])
    kids = collections.defaultdict(list)
    new_lines, new_cells = [], []
    cells = {}
    cells_path = os.path.join(mapdir, 'cells.csv')
    if os.path.exists(cells_path):
        for r in csv.DictReader(open(cells_path, encoding='utf-8')):
            cells[r['key']] = r
    split = 0
    for gi in range(len(starts) - 1):
        a, b = starts[gi], starts[gi + 1]
        col = int(srt[a])
        info = colour_info.get(col)
        if info is None or not info[0].startswith('land_'):
            continue
        idx = order[a:b]
        ys = idx // W
        area = float(parea[ys].sum())
        lon = -180.0 + (idx % W).mean() * degpx
        lat = 90.0 - ys.mean() * degpx
        if everything:
            fine = FINE_BOX[0] <= lon <= FINE_BOX[2] and FINE_BOX[1] <= lat <= FINE_BOX[3]
        else:
            fine = near_zone(lon, lat, math.sqrt(area) / 111.0, zones)     # within about one location width of a drawn border
        limit = fine_t if fine else other_t
        k = int(math.ceil(area / limit))
        if k < 2:
            continue
        xs = (idx % W).astype(np.float64) * math.cos(math.radians(lat))
        parts = bisect(xs, ys.astype(np.float64), k)
        parts.sort(key=len, reverse=True)
        old = info[0]
        for part in parts[1:]:
            top += 1
            nk = 'land_%05d' % top
            c = new_colour()
            flat[idx[part]] = c
            new_lines.append('%06X = { id = %s type = %s }' % (c, nk, info[1]))
            kids[old].append(nk)
            r = cells.get(old)
            new_cells.append('%s,%s,%s,%s,%s' % (nk, old, r['adm1'] if r else '', r['adm0'] if r else '', r['name'] if r else ''))
        split += 1
    print('%d locations cut into %d new ones' % (split, len(new_lines)))
    for name in ('provinces.png', 'locations.txt', 'cells.csv', os.path.join('states', '00_states.txt')):
        p = os.path.join(mapdir, name)
        if os.path.exists(p) and not os.path.exists(p + '.before-split'):
            shutil.copy2(p, p + '.before-split')
    out = np.empty((H, W, 3), np.uint8)
    out[:, :, 0] = (flat >> 16).reshape(H, W)
    out[:, :, 1] = (flat >> 8).reshape(H, W)
    out[:, :, 2] = flat.reshape(H, W)
    Image.fromarray(out).save(os.path.join(mapdir, 'provinces.png'))
    with open(loc_path, 'w', encoding='utf-8', newline='') as f:
        f.write(loc_text.rstrip('\r\n') + '\r\n' + '\r\n'.join(new_lines) + '\r\n')
    if os.path.exists(cells_path):
        ctext = open(cells_path, encoding='utf-8', newline='').read()
        with open(cells_path, 'w', encoding='utf-8', newline='') as f:
            f.write(ctext.rstrip('\r\n') + '\n' + '\n'.join(new_cells) + '\n')
    states_path = os.path.join(mapdir, 'states', '00_states.txt')
    text = open(states_path, encoding='utf-8', newline='').read()

    def grow(m):
        keys = m.group(1).split()
        extra = []
        for k in keys:
            extra.extend(kids.get(k, []))
        return 'locations = { ' + ' '.join(keys + extra) + ' }'

    with open(states_path, 'w', encoding='utf-8', newline='') as f:
        f.write(re.sub(r'locations = \{([^}]*)\}', grow, text))
    print('done')


if __name__ == '__main__':
    main()
