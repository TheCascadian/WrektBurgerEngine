"""The peoples and polities of tools/gazetteer/*.txt, each with the years it existed and the lands it held.

One line each:  name ; lon ; lat ; from ; to ; radius ; flags
  or a realm:   @ name ; from ; to ; radius ; place, place, ...   (every place of places*.txt, or lon/lat, is a seed of the realm that takes the lands within the radius of it)
  lon, lat   the middle of its lands (degrees);  from, to   astronomical years (0 = 1 BCE, -499 = 500 BCE);  radius   how far its lands reach (degrees)
  flags      F = it also takes lands the polygons of the dataset give to another power (the small states inside an empire); a line without F takes only lands nobody holds
             and lands the dataset gives to a collective (tribes, nomads, kingdoms ...)
  a name written twice (a people that moved, a realm in two periods) is one country; `name = other name` on its own line is not used - use the same name.
Of the entries that reach a state, the one whose middle is nearest (in radii) takes it; at equal distance the one with the smaller radius.
"""
import os, re, glob, math

DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'gazetteer')


# names of the gazetteer that stand for a country the dataset (or the first start date) already knows under another key
ALIAS = {'athens': 'ath', 'thebes': 'thb', 'sparta': 'spa', 'corinth': 'cor', 'argos': 'arg', 'thessaly': 'ths', 'cham': 'champa', 'dabuyids': 'tabaristan', 'garamantes': 'orig:GAR', 'goguryeo': 'koguryo', 
         'indo-parthian kingdom': 'suren', 'kalinga': 'kalinga_m', 'merkits': 'merkit', 'oirats': 'oirat', 'oirat confederation': 'oirat', 'northern yuan': 'northernyuan',
         'moghulistan': 'chagatai', 'chagatai khanate': 'chagatai', 'nagas of padmavati': 'nagas', 'nanda empire': 'ind', 'shunga empire': 'ind', 'norse greenland': 'greenland', 'golden horde': 'goldenhorde',
         'northern wei': 'toba wei', 'pandya': 'tamil', 'pechenegs': 'peshemegs', 'southern song': 'song', 'northern song': 'song', 'tibetan empire': 'tufan',
         'western satraps': 'saka', 'kara-khitan': 'kara khitai khaganate', 'qara khitai': 'kara khitai khaganate', 'yao': 'yao', 'nara japan': 'yao', 'heian japan': 'yao',
         'kamakura japan': 'yao', 'muromachi japan': 'yao', 'jurchen jin': 'jin', 'jin dynasty': 'jin', 'sasanian empire': 'sasanian', 'khwarazmian empire': 'khwarazmian dynasty', 'vladimir-suzdal': 'vladimir', 'khamag mongol': 'mongol', 'southern qi': 'jin', 'yuezhi': 'kushan', 'han': 'han', 'tai': 'lanna'}


_ALIAS_N = None


def slug(name):
    """The key of a country: the same as the build gives a polity of the dataset with this name, so that the gazetteer and the dataset share one country."""
    import history_tags as T
    norm = T.normalize(name)
    global _ALIAS_N
    if _ALIAS_N is None:
        _ALIAS_N = {T.normalize(k): v for k, v in ALIAS.items()}
    if norm in _ALIAS_N:
        return _ALIAS_N[norm]
    for rx, key in T.FAMILIES:
        if re.search(rx, norm):
            return key or norm
    return norm


MISSING = []


OPEN_UNTIL = 1880      # peoples without a state of their own are on the map until about here
RENAMES = []      # R old display ; from ; to ; new display: what the dataset calls a polity at a date, when that is wrong


def load():
    out = []
    places = {}
    files = sorted(glob.glob(os.path.join(DIR, '*.txt')))
    # places first: "name ; lon ; lat" in places*.txt
    for path in files:
        if not os.path.basename(path).startswith('places'):
            continue
        for n, line in enumerate(open(path, encoding='utf-8'), 1):
            line = line.split('#', 1)[0].strip()
            if line:
                f = [x.strip() for x in line.split(';')]
                places[f[0].lower()] = (float(f[1]), float(f[2]))
    for path in files:
        if os.path.basename(path).startswith(('places', 'wasteland')):
            continue
        for n, line in enumerate(open(path, encoding='utf-8'), 1):
            line = line.split('#', 1)[0].strip()
            if not line:
                continue
            if line.startswith('R '):
                f = [x.strip() for x in line[1:].split(';')]
                RENAMES.append((f[0], int(f[1]), int(f[2]), f[3]))
                continue
            if line.startswith('U '):
                # U name ; from ; to ; unit, unit ...   a realm made of the administrative units of Natural Earth: ISO3 (a country), ESP-1234 (an adm1 code) or ESP:Galicia (country:name)
                f = [x.strip() for x in line[1:].split(';')]
                out.append(dict(name=f[0], key=slug(f[0]), a=int(f[1]), b=int(f[2]), units=[x.strip() for x in f[3].split(',') if x.strip()], force=True, r=0.0, lon=0, lat=0))
                continue
            if line.startswith('P '):
                f = [x.strip() for x in line[1:].split(';')]
                pts = [tuple(float(v) for v in q.split()) for q in f[3].split(',')]
                out.append(dict(name=f[0], key=slug(f[0]), a=int(f[1]), b=int(f[2]), poly=pts, force=True, r=0.0, lon=0, lat=0))
                continue
            if line.startswith('@'):
                # @ name ; from ; to ; radius ; place, place, ... (each place becomes a seed of the realm; a place may be written lon/lat)
                f = [x.strip() for x in line[1:].split(';')]
                name, a, b, r = f[0], int(f[1]), int(f[2]), float(f[3])
                for pl in f[4].split(','):
                    pl = pl.strip()
                    if not pl:
                        continue
                    if re.match(r'^-?[\d.]+/-?[\d.]+$', pl):
                        lo, la = (float(x) for x in pl.split('/'))
                    elif pl.lower() in places:
                        lo, la = places[pl.lower()]
                    else:
                        MISSING.append(f'{os.path.basename(path)}:{n}: {pl}')
                        continue
                    out.append(dict(name=name, key=slug(name), lon=lo, lat=la, a=a, b=b, r=r, force=True))
                continue
            f = [x.strip() for x in line.split(';')]
            if len(f) < 6:
                raise ValueError(f'{path}:{n}: {line}')
            b = int(f[4])
            # a people listed to 1500 (the end of the table) lives on until the colonial powers draw the map: after 1500 it fills the lands nobody holds, up to OPEN_UNTIL
            out.append(dict(name=f[0], key=slug(f[0]), lon=float(f[1]), lat=float(f[2]), a=int(f[3]), b=b, r=float(f[5]), force='F' in (f[6] if len(f) > 6 else ''), open=(b == 1500), people=True))
    return out


ENTRIES = load()
if MISSING:
    raise ValueError('unknown places: ' + '; '.join(MISSING))
KEYS = {e['key'] for e in ENTRIES}
FALLBACK = 5.0     # degrees: how far a people may reach to take the lands round it that the table does not name
NEAR_EMPTY = True  # lands nobody holds stay empty (the unowned lands of the dataset are mostly sea coasts and deserts)
# lands where nobody lived (or nobody a source names): the fallback never fills them
EMPTY_BOXES = [(-17.0, 17.5, 33.0, 29.0), (46.0, 17.0, 56.0, 23.0), (79.0, 37.0, 90.0, 40.5), (100.0, 40.5, 112.0, 44.5), (80.0, 33.0, 92.0, 36.0), (-60.0, 66.0, -20.0, 84.0),
               (-180.0, 70.0, 180.0, 90.0), (-180.0, -90.0, 180.0, -60.0), (120.0, -29.0, 138.0, -22.0), (-72.0, -26.0, -68.0, -18.0), (-120.0, 24.0, -112.0, 33.0)]
PEOPLE_KEYS = {e['key'] for e in ENTRIES if e.get('people')} - {e['key'] for e in ENTRIES if not e.get('people')}   # keys that are only peoples, never realms
UNIT_REALMS = {e['key'] for e in ENTRIES if 'units' in e}
POLY_REALMS = {e['key'] for e in ENTRIES if 'poly' in e}   # drawn borders are exact: no province is snapped across them   # the realms of states, drawn after maps; peoples are not snapped to modern provinces
SOFT = set()      # keys of peoples cut out of collectives or masks by the build (a real people of the gazetteer may replace them)


def weak(o):
    """A claim on a state that the dataset gives to nobody or to a collective cut into clusters."""
    return o is None or '#' in o[0] or o[0].startswith('mc:') or o[0] in SOFT


_STATE_UNIT = None


def state_units():
    """state key -> (adm1 code, adm0 code, adm1 name): the unit holding most locations of the state (map/cells.csv from tools/geo/refine_cells.py)."""
    global _STATE_UNIT
    if _STATE_UNIT is None:
        import collections, csv
        game = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'projects', 'World500BCE', 'game', 'map')
        cells = {}
        path = os.path.join(game, 'cells.csv')
        if os.path.exists(path):
            for r in csv.DictReader(open(path, encoding='utf-8')):
                cells[r['key']] = (r['adm1'].replace('\x00', ''), r['adm0'].replace('\x00', ''), r['name'].replace('\x00', ''))
        _STATE_UNIT = dict(cells)      # a location is a unit of its own too (the refinement of states asks per location)
        text = open(os.path.join(game, 'states', '00_states.txt'), encoding='utf-8').read()
        for m in re.finditer(r'\n([a-z0-9_]+) = \{(.*?)\n\}', text, re.S):
            lm = re.search(r'locations = \{([^}]*)\}', m.group(2))
            if not lm:
                continue
            c = collections.Counter(cells[k] for k in lm.group(1).split() if k in cells and cells[k][0])
            if c:
                _STATE_UNIT[m.group(1)] = c.most_common(1)[0][0]
    return _STATE_UNIT


def in_units(unit, tokens, lon=None, lat=None):
    """Tokens: ISO3, adm1_code, ISO3:name, or ISO3@w/s/e/n (the part of the country whose state middle lies in that box, in degrees; ISO3 may be empty for any country)."""
    adm1, adm0, name = unit
    for t in tokens:
        if '@' in t:
            c, box = t.split('@')
            if lon is not None and (not c or c == adm0):
                w, s, e_, n = (float(x) for x in box.split('/'))
                if w <= lon <= e_ and s <= lat <= n:
                    return True
            continue
        if t == adm0 or t == adm1 or t == f'{adm0}:{name}':
            return True
    return False


_UNIT_SIZE = None
SNAP_MAX_LOCATIONS = 30     # units with more locations than this (the big provinces of Russia, China, Canada ...) keep the borders of the cells
SNAP_SHARE = 0.6            # the share of a unit a realm must hold for the whole unit to join it


def unit_sizes():
    global _UNIT_SIZE
    if _UNIT_SIZE is None:
        import collections
        c = collections.Counter(u[0] for u in state_units().values() if u[0])
        _UNIT_SIZE = c
    return _UNIT_SIZE


SNAP_FROM = 1000                       # modern provinces are no proof of ancient borders: nothing is snapped onto them before this year
SNAP_BOX = (-12.0, 34.0, 45.0, 62.0)   # and only in Europe, where the counties and provinces of the Middle Ages followed older lines


def snap_to_units(res, states, ay=0):
    """Straightens the borders onto the administrative lines: when one realm holds most of a small unit, the whole unit is its."""
    import collections
    su = state_units()
    groups = collections.defaultdict(list)
    for i, st in enumerate(states):
        un = su.get((st[3] if len(st) > 3 and isinstance(st[3], str) else st[0]))
        if un and un[0]:
            groups[un[0]].append(i)
    snapped = 0
    if ay < SNAP_FROM:
        return 0
    for code, idx in groups.items():
        if len(idx) < 2 or len(idx) > SNAP_MAX_LOCATIONS:
            continue
        if not all(SNAP_BOX[0] <= states[i][1] <= SNAP_BOX[2] and SNAP_BOX[1] <= states[i][2] <= SNAP_BOX[3] for i in idx):
            continue
        cnt = collections.Counter(res[i][0] if res[i] else None for i in idx)
        key, n = cnt.most_common(1)[0]
        if key is None or n / len(idx) < SNAP_SHARE or n == len(idx):
            continue
        if key in KEYS and key not in UNIT_REALMS or '#' in key or key.startswith('mc:'):
            continue
        if any(res[i] and res[i][0] in POLY_REALMS and res[i][0] not in UNIT_REALMS for i in idx):
            continue        # a people without a state keeps its own land: tribes do not follow modern borders
        pick = next(res[i] for i in idx if res[i] and res[i][0] == key)
        for i in idx:
            if not res[i] or res[i][0] != key:
                res[i] = pick; snapped += 1
    return snapped


def area(poly):
    return abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))) / 2


def inside(x, y, poly):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]; xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


_EMPTY = None


def empty_lands():
    """The wasteland polygons and oases of wasteland.txt: ([polygon, ...], [(lon, lat, radius), ...])."""
    global _EMPTY
    if _EMPTY is None:
        polys, oases = [], []
        path = os.path.join(DIR, 'wasteland.txt')
        for line in open(path, encoding='utf-8') if os.path.exists(path) else []:
            line = line.split('#', 1)[0].strip()
            if line.startswith('W '):
                f = [x.strip() for x in line[1:].split(';')]
                polys.append([tuple(float(v) for v in q.split()) for q in f[1].split(',')])
            elif line.startswith('O '):
                f = [x.strip() for x in line[1:].split(';')]
                oases.append((float(f[1]), float(f[2]), float(f[3])))
        _EMPTY = (polys, oases)
    return _EMPTY


def apply(res, states, ay):
    """res: per state None or (key, name); changes it in place. Then the lands still held by nobody or by a collective go to the nearest people that existed (within FALLBACK degrees)."""
    live = [e for e in ENTRIES if e['a'] <= ay <= e['b']]
    if ay >= 1500 and ay <= OPEN_UNTIL:
        # only the lands nobody holds; the peoples of the Americas end with 1800 (the states of the dataset cover the continent then)
        live += [dict(e, force=False) for e in ENTRIES if e.get('open') and e['a'] <= ay and e['b'] < ay and (ay <= 1800 or e['lon'] > -30) and not (-25.0 <= e['lon'] <= 45.0 and 34.0 <= e['lat'] <= 72.0)]
    taken = 0
    import collections
    seen = collections.defaultdict(collections.Counter)
    for o in res:
        if o:
            seen[o[0]][o[1]] += 1
    def shown(e):
        # a realm the dataset also knows keeps the name the dataset shows it with at this date
        return seen[e['key']].most_common(1)[0][0] if e['key'] in seen else e['name']
    polys = sorted((e for e in live if 'poly' in e), key=lambda e: -area(e['poly']))   # the smaller polygon wins where two overlap (an enclave)
    live = [e for e in live if 'poly' not in e]
    unit_realms = [e for e in live if 'units' in e]
    live = [e for e in live if 'units' not in e]
    import numpy as np
    N = len(states)
    lons = np.array([st[1] for st in states], dtype=np.float64)
    lats = np.array([st[2] for st in states], dtype=np.float64)
    coslat = np.cos(np.radians(lats))
    weak0 = np.array([bool(weak(res[i])) for i in range(N)])
    # 1. seeds: the nearest seed within its radius (peoples and realms with a point), for the states that may be taken
    if live:
        sl = np.array([e['lon'] for e in live]); sa = np.array([e['lat'] for e in live]); sr = np.array([e['r'] for e in live], dtype=np.float64)
        sf = np.array([bool(e['force']) for e in live])
        for c0 in range(0, N, 3000):
            c1 = min(N, c0 + 3000)
            D = np.hypot((lons[c0:c1, None] - sl[None, :]) * coslat[c0:c1, None], lats[c0:c1, None] - sa[None, :]) / sr[None, :]
            D[~(sf[None, :] | weak0[c0:c1, None])] = np.inf
            D[D > 1.0] = np.inf
            dmin = D.min(axis=1)
            R = np.where(D == dmin[:, None], sr[None, :], np.inf)
            j = R.argmin(axis=1)
            for k in np.flatnonzero(np.isfinite(dmin)):
                e = live[j[k]]
                res[c0 + k] = (e['key'], shown(e))
                taken += 1
    # 2. drawn borders beat seeds: the last polygon listed wins where two overlap
    for e in polys:
        xs = [q[0] for q in e['poly']]; ys = [q[1] for q in e['poly']]
        cand = np.flatnonzero((lons >= min(xs)) & (lons <= max(xs)) & (lats >= min(ys)) & (lats <= max(ys)))
        for i in cand:
            if inside(lons[i], lats[i], e['poly']):
                res[i] = (e['key'], shown(e))
                taken += 1
    # 3. the administrative units of Natural Earth beat a drawn polygon: the later realm of the files wins
    if unit_realms:
        su = state_units()
        plain = []
        for e in unit_realms:
            whole = {tk for tk in e['units'] if '@' not in tk}
            boxes = []
            for tk in e['units']:
                if '@' in tk:
                    c, bx = tk.split('@')
                    boxes.append((c, tuple(float(x) for x in bx.split('/'))))
            plain.append((e, whole, boxes))
        for i, st in enumerate(states):
            un = su.get((st[3] if len(st) > 3 and isinstance(st[3], str) else st[0]))
            if not un:
                continue
            adm1, adm0, nm = un
            mine = {adm0, adm1, f'{adm0}:{nm}'}
            lon, lat = st[1], st[2]
            for e, whole, boxes in plain:
                hit = bool(mine & whole)
                if not hit:
                    for c, (w, s, ea, n) in boxes:
                        if (not c or c == adm0) and w <= lon <= ea and s <= lat <= n:
                            hit = True
                            break
                if hit:
                    res[i] = (e['key'], shown(e)); taken += 1
    needy = []
    for i, st in enumerate(states):
        if res[i] is not None and not (('#' in res[i][0] or res[i][0].startswith('mc:') or res[i][0] in SOFT) and res[i][0] not in KEYS):
            continue
        if res[i] is None and not NEAR_EMPTY:
            continue
        lon, lat = st[1], st[2]
        if any(w <= lon <= e_ and so <= lat <= n for w, so, e_, n in EMPTY_BOXES):
            continue
        needy.append(i)
    if needy and live:
        ni = np.array(needy)
        dd = np.hypot((lons[ni, None] - sl[None, :]) * coslat[ni, None], lats[ni, None] - sa[None, :])
        ratio = dd / sr[None, :]
        ratio[(dd > FALLBACK) | (ratio > 4.0)] = np.inf
        j = ratio.argmin(axis=1)
        for r, k in enumerate(ni):
            if np.isfinite(ratio[r, j[r]]):
                e = live[j[r]]
                res[k] = (e['key'], shown(e))
                taken += 1
    if ay < 1500 and os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'projects', 'World500BCE', 'game', 'map', 'cells.csv')):
        taken += snap_to_units(res, states, ay)
    if ay < 1500:
        # the dataset draws some old realms with the outline of a modern country; nobody ruled the empty desert inside them
        wpolys, oases = empty_lands()
        for i, st in enumerate(states):
            if res[i] is not None and any(inside(st[1], st[2], pg) for pg in wpolys) and not any(math.hypot((st[1] - ox) * math.cos(math.radians(st[2])), st[2] - oy) <= r for ox, oy, r in oases):
                res[i] = None
    for i, o in enumerate(res):
        if o is not None:
            for old, a, b, new in RENAMES:
                if a <= ay <= b and o[1] == old:
                    res[i] = (o[0], new)
    if live or polys or unit_realms:
        # generated clusters nobody of the table claims are lands without a known people: they stay unowned
        for i in range(len(res)):
            o = res[i]
            if o is not None and o[0] not in KEYS and ('#' in o[0] or o[0].startswith('mc:') or o[0] in SOFT):
                res[i] = None
    return taken


def check_units():
    """Tokens of the U realms that name no unit of the map (a misspelt province)."""
    known = set()
    for u in state_units().values():
        known.add(u[0]); known.add(u[1]); known.add(f'{u[1]}:{u[2]}')
    bad = []
    for e in ENTRIES:
        for t in e.get('units', []):
            if t not in known and '@' not in t:
                bad.append((e['name'], e['a'], t))
    return bad


if __name__ == '__main__':
    import sys
    if '--check' in sys.argv:
        for b in check_units():
            print(b)
        print('checked')
