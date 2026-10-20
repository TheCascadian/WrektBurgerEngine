"""Marks the wasteland of tools/gazetteer/wasteland.txt in a project: its land locations become `type = impassable` in map/locations.txt and the states that
hold both wasteland and living land are planned apart, so that a state is either all wasteland (no country ever owns it) or none.

usage: python tools/geo/mark_wasteland.py <locations.csv> <plan.txt> [<game dir>]
  locations.csv: burger export-location-centres <project> <file>
  plan.txt:      for burger split-states <project> --plan <plan.txt>  (state <TAB> wasteland group <TAB> rest)
Then: burger split-states, burger export-state-centres, python tools/import-history.py build (which gives wasteland states to nobody).
"""
import sys, os, re, csv, math, collections

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))


def inside(x, y, poly):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]; xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


def load_waste():
    polys, oases = [], []
    for line in open(os.path.join(REPO, 'tools', 'gazetteer', 'wasteland.txt'), encoding='utf-8'):
        line = line.split('#', 1)[0].strip()
        if line.startswith('W '):
            f = [x.strip() for x in line[1:].split(';')]
            polys.append((f[0], [tuple(float(v) for v in q.split()) for q in f[1].split(',')]))
        elif line.startswith('O '):
            f = [x.strip() for x in line[1:].split(';')]
            oases.append((float(f[1]), float(f[2]), float(f[3])))
    return polys, oases


def is_waste(lon, lat, polys, oases):
    if not any(inside(lon, lat, p) for _, p in polys):
        return False
    return not any(math.hypot((lon - ox) * math.cos(math.radians(lat)), lat - oy) <= r for ox, oy, r in oases)


def main():
    locs_path, plan_path = sys.argv[1], sys.argv[2]
    game = sys.argv[3] if len(sys.argv) > 3 else os.path.join(REPO, 'projects', 'World500BCE', 'game')
    polys, oases = load_waste()
    rows = list(csv.DictReader(open(locs_path, encoding='utf-8')))
    waste = {r['location'] for r in rows if is_waste(float(r['lon']), float(r['lat']), polys, oases)}
    by_state = collections.defaultdict(list)
    for r in rows:
        by_state[r['state']].append(r['location'])
    with open(plan_path, 'w', encoding='utf-8') as f:
        cut = 0
        for s, ls in by_state.items():
            w = [l for l in ls if l in waste]
            if w and len(w) < len(ls):
                f.write(s + '\t' + ' '.join(w) + '\t' + ' '.join(l for l in ls if l not in waste) + '\n'); cut += 1
    # map/locations.txt: land -> impassable for the wasteland (and back for anything no longer in it)
    path = os.path.join(game, 'map', 'locations.txt')
    text = open(path, encoding='utf-8', newline='').read()
    n = 0
    def fix(m):
        nonlocal n
        loc, typ = m.group(2), m.group(3)
        want = 'impassable' if loc in waste else ('land' if typ == 'impassable' else typ)
        if want != typ:
            n += 1
        return m.group(1) + want + m.group(4)
    text = re.sub(r'(\{ id = (land_\d+) type = )(\w+)( \})', lambda m: fix(m) if True else m.group(0), text)
    # the regex above captures the id as group 2: rebuild the head with it
    open(path, 'w', encoding='utf-8', newline='').write(text)
    print('%d wasteland locations (%d changed in locations.txt), %d states to cut' % (len(waste), n, cut))


main()
