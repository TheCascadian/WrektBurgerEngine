#!/usr/bin/env python3
"""
Builds history/timeline/snapshots.txt for a project from the historical-basemaps dataset (https://github.com/aourednik/historical-basemaps, GPL-3.0):
for every snapshot year the polity whose polygon holds the middle of each state owns it.

  python tools/import-history.py scan      list the polities by the number of states they hold, per snapshot (to write the alias table)
  python tools/import-history.py build     write snapshots.txt and the country definitions

The raw files (world_<year>.geojson) go in tools/historical-basemaps/ (not in the repository); states.csv comes from
`burger export-state-centres <project> tools/historical-basemaps/states.csv`.
"""
import json, os, re, sys, glob, hashlib, collections, math

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, 'historical-basemaps')
sys.path.insert(0, HERE)
import history_tags as H   # alias table, splits, colors

YEARS = ['bc400', 'bc323', 'bc300', 'bc200', 'bc100', 'bc1', '100', '200', '300', '400', '500', '600', '700', '800', '900', '1000', '1100', '1200', '1279',
         '1300', '1400', '1492', '1500', '1530', '1600', '1650', '1700', '1715', '1783', '1800', '1815', '1880', '1900', '1914', '1920', '1930', '1938',
         '1945', '1960', '1994', '2000', '2010']


def astronomical(y):
    return 1 - int(y[2:]) if y.startswith('bc') else int(y)


def load_states():
    out = []
    with open(os.path.join(DATA, 'states.csv'), encoding='utf-8') as f:
        next(f)
        for line in f:
            k, lon, lat, n = line.strip().split(',')
            out.append((k, float(lon), float(lat), int(n)))
    out.sort(key=lambda s: s[0])
    return out


def ring_area(r):
    a = 0.0
    for i in range(len(r)):
        x1, y1 = r[i][0], r[i][1]
        x2, y2 = r[(i + 1) % len(r)][0], r[(i + 1) % len(r)][1]
        a += x1 * y2 - x2 * y1
    return abs(a) / 2


def inside(ring, x, y):
    c = False
    j = len(ring) - 1
    for i in range(len(ring)):
        xi, yi = ring[i][0], ring[i][1]
        xj, yj = ring[j][0], ring[j][1]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


class Feature:
    def __init__(self, name, polys):
        self.name = name
        self.polys = polys   # list of (outer ring, [holes])
        xs = [p[0] for poly in polys for p in poly[0]]
        ys = [p[1] for poly in polys for p in poly[0]]
        self.bbox = (min(xs), min(ys), max(xs), max(ys))
        self.area = sum(ring_area(poly[0]) - sum(ring_area(h) for h in poly[1]) for poly in polys)

    def contains(self, x, y):
        b = self.bbox
        if x < b[0] or x > b[2] or y < b[1] or y > b[3]:
            return False
        for outer, holes in self.polys:
            if inside(outer, x, y) and not any(inside(h, x, y) for h in holes):
                return True
        return False


class FeatList(list):
    """The polygons of one map of the dataset, with the year and a cache of what each point of the world was in (kept between runs: the states mostly stay where they are)."""
    year = None
    memo = None


_CACHE_PATH = os.path.join(DATA, 'owner_cache.pkl')
_CACHE = None
_CACHE_DIRTY = False


def _cache():
    global _CACHE
    if _CACHE is None:
        import pickle
        try:
            _CACHE = pickle.load(open(_CACHE_PATH, 'rb'))
        except Exception:
            _CACHE = {}
    return _CACHE


def save_owner_cache():
    if _CACHE_DIRTY:
        import pickle
        with open(_CACHE_PATH, 'wb') as f:
            pickle.dump(_CACHE, f)


def load_snapshot(year):
    path = os.path.join(DATA, f'world_{year}.geojson')
    d = json.load(open(path, encoding='utf-8'))
    feats = []
    for f in d['features']:
        name = f['properties'].get('NAME')
        g = f.get('geometry')
        if not name or not g:
            continue
        polys = []
        if g['type'] == 'Polygon':
            polys = [(g['coordinates'][0], g['coordinates'][1:])]
        elif g['type'] == 'MultiPolygon':
            polys = [(p[0], p[1:]) for p in g['coordinates']]
        if polys:
            feats.append(Feature(name, polys))
    feats.sort(key=lambda f: f.area)   # the smallest polygon that holds a point wins
    out = FeatList(feats)
    out.year = str(year)
    mtime = os.path.getmtime(path)
    c = _cache()
    ent = c.get(out.year)
    if ent is None or ent[0] != mtime:
        ent = (mtime, {})
        c[out.year] = ent
    out.memo = ent[1]
    return out


def owner_of(feats, x, y):
    global _CACHE_DIRTY
    memo = getattr(feats, 'memo', None)
    if memo is not None:
        k = (round(x, 4), round(y, 4))
        if k in memo:
            return memo[k]
    name = None
    for f in feats:
        if f.contains(x, y):
            name = f.name
            break
    if memo is not None:
        memo[k] = name
        _CACHE_DIRTY = True
    return name


def scan():
    states = load_states()
    for y in YEARS:
        feats = load_snapshot(y)
        c = collections.Counter()
        for k, lon, lat, n in states:
            o = owner_of(feats, lon, lat)
            if o:
                c[o] += 1
        print(y, astronomical(y), len(c), 'polities;', ', '.join(f'{k}:{v}' for k, v in c.most_common(400)))


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'scan'
    if cmd == 'scan':
        scan()
    elif cmd == 'build':
        import history_build
        history_build.build(sys.modules[__name__])
        save_owner_cache()
