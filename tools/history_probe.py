"""Who owns a place at each start date: python tools/history_probe.py "Paris,Toledo,Kiev" [years comma separated]. Places come from tools/gazetteer/places*.txt (or lon/lat)."""
import re, sys, csv, os, math
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
sys.path.insert(0, os.path.join(REPO, 'tools'))
import history_gazetteer as G
places = {}
import glob
for path in glob.glob(os.path.join(REPO, 'tools', 'gazetteer', 'places*.txt')):
    for line in open(path, encoding='utf-8'):
        line = line.split('#', 1)[0].strip()
        if line:
            f = [x.strip() for x in line.split(';')]
            places[f[0].lower()] = (float(f[1]), float(f[2]))
names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
states = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
snaps = []
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    own = re.search(r'owners = \{(.*?)\}', m.group(2), re.S).group(1).split()
    nm = dict(names)
    mm = re.search(r'names = \{(.*?)\}', m.group(2), re.S)
    if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
    snaps.append((y, own, nm))
years = [int(x) for x in sys.argv[2].split(',')] if len(sys.argv) > 2 else [s[0] for s in snaps if s[0] < 1500]
pts = []
for pl in sys.argv[1].split(','):
    pl = pl.strip()
    if '/' in pl: lo, la = (float(x) for x in pl.split('/'))
    else: lo, la = places[pl.lower()]
    best = min(range(len(states)), key=lambda i: math.hypot((float(states[i]['lon']) - lo) * math.cos(math.radians(la)), float(states[i]['lat']) - la))
    pts.append((pl, best))
for pl, i in pts:
    row = []
    for y, own, nm in snaps:
        if y in years:
            o = own[i]
            row.append(f'{y}:{nm.get(o, o) if o != "_" else "-"}')
    print(pl, '|', '; '.join(row))
