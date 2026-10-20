"""Puts back together the locations that split_locations.py --all cut where no hand-drawn border needs them cut.

usage: python tools/geo/merge_split.py <game dir>
  A location made by the split (a key that is not in map/cells.csv.before-split) goes back into its parent when the parent lies away from every drawn border (the zones of
  split_locations.py) and the parent and its parts are in one state. Pixels, map/locations.txt, map/cells.csv and map/states/00_states.txt are rewritten (backups: *.before-merge).
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
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import split_locations as S


def main():
    game = sys.argv[1]
    mapdir = os.path.join(game, 'map')
    loc_path = os.path.join(mapdir, 'locations.txt')
    loc_text = open(loc_path, encoding='utf-8', newline='').read()
    colour_of, id_colour = {}, {}
    for m in re.finditer(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+) type = (\w+) \}', loc_text, re.M):
        colour_of[m.group(2)] = int(m.group(1), 16)
        id_colour[int(m.group(1), 16)] = m.group(2)
    cells_path = os.path.join(mapdir, 'cells.csv')
    before = set(r['key'] for r in csv.DictReader(open(cells_path + '.before-split', encoding='utf-8')))
    rows = list(csv.DictReader(open(cells_path, encoding='utf-8')))
    kids = collections.defaultdict(list)
    for r in rows:
        if r['key'] not in before and r['old'] in before:
            kids[r['old']].append(r['key'])
    states_path = os.path.join(mapdir, 'states', '00_states.txt')
    stext = open(states_path, encoding='utf-8', newline='').read()
    state_of = {}
    for m in re.finditer(r'\n([a-z0-9_]+) = \{(.*?)\n\}', stext, re.S):
        lm = re.search(r'locations = \{([^}]*)\}', m.group(2))
        if lm:
            for l in lm.group(1).split():
                state_of[l] = m.group(1)
    img = np.array(Image.open(os.path.join(mapdir, 'provinces.png')).convert('RGB'))
    H, W, _ = img.shape
    flat = ((img[:, :, 0].astype(np.uint32) << 16) | (img[:, :, 1].astype(np.uint32) << 8) | img[:, :, 2]).ravel()
    del img
    degpx = 360.0 / W
    u, inv = np.unique(flat, return_inverse=True)
    cnt = np.bincount(inv).astype(np.float64)
    xs = np.bincount(inv, weights=np.tile(np.arange(W, dtype=np.float64), H)) / cnt
    ys = np.bincount(inv, weights=np.repeat(np.arange(H, dtype=np.float64), W)) / cnt
    pos = {int(c): i for i, c in enumerate(u)}
    zones = S.zone_polygons()
    target = u.copy()
    merge, keep = [], 0
    for parent, children in kids.items():
        group = [parent] + children
        if any(g not in colour_of for g in group):
            continue
        idx = [pos[colour_of[g]] for g in group if colour_of[g] in pos]
        n = float(sum(cnt[i] for i in idx))
        cx = sum(xs[i] * cnt[i] for i in idx) / n
        cy = sum(ys[i] * cnt[i] for i in idx) / n
        lon, lat = -180.0 + cx * degpx, 90.0 - cy * degpx
        area = n * (degpx * 111.32) ** 2 * math.cos(math.radians(lat))
        if S.near_zone(lon, lat, math.sqrt(area) / 111.0, zones) or len({state_of.get(g) for g in group}) != 1:
            keep += 1
            continue
        for c in children:
            if colour_of[c] in pos:
                target[pos[colour_of[c]]] = colour_of[parent]
        merge.append((parent, children))
    print('%d parents merged back (%d locations), %d kept split' % (len(merge), sum(len(c) for _, c in merge), keep))
    for name in ('provinces.png', 'locations.txt', 'cells.csv', os.path.join('states', '00_states.txt')):
        p = os.path.join(mapdir, name)
        if os.path.exists(p) and not os.path.exists(p + '.before-merge'):
            shutil.copy2(p, p + '.before-merge')
    flat = target[inv]
    out = np.empty((H, W, 3), np.uint8)
    out[:, :, 0] = (flat >> 16).reshape(H, W)
    out[:, :, 1] = (flat >> 8).reshape(H, W)
    out[:, :, 2] = flat.reshape(H, W)
    Image.fromarray(out).save(os.path.join(mapdir, 'provinces.png'))
    gone = {c for _, ch in merge for c in ch}
    with open(loc_path, 'w', encoding='utf-8', newline='') as f:
        f.write('\r\n'.join(l for l in loc_text.replace('\r\n', '\n').split('\n') if not any((' id = %s ' % g) in l for g in gone)).rstrip('\r\n') + '\r\n')
    kept = [r for r in rows if r['key'] not in gone]
    with open(cells_path, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()), lineterminator='\n')
        w.writeheader()
        w.writerows(kept)

    def shrink(m):
        return 'locations = { ' + ' '.join(k for k in m.group(1).split() if k not in gone) + ' }'

    with open(states_path, 'w', encoding='utf-8', newline='') as f:
        f.write(re.sub(r'locations = \{([^}]*)\}', shrink, stext))
    print('done')


if __name__ == '__main__':
    main()
