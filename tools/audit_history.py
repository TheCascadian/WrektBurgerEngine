"""Lists the polities of a start date: python tools/audit_history.py <year> [min_states] - name, tag, states, middle (lon,lat), extent."""
import re, sys, csv, os, collections
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
states = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
year = int(sys.argv[1]); mn = int(sys.argv[2]) if len(sys.argv) > 2 else 1
box = [float(x) for x in sys.argv[3].split(',')] if len(sys.argv) > 3 else None
best = None
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if best is None or abs(y - year) < abs(best[0] - year): best = (y, m.group(2))
y, body = best
own = re.search(r'owners = \{(.*?)\}', body, re.S).group(1).split()
nm = dict(names)
mm = re.search(r'names = \{(.*?)\}', body, re.S)
if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
g = collections.defaultdict(list)
for o, s in zip(own, states):
    if o != '_' and (box is None or (box[0] <= float(s['lon']) <= box[2] and box[1] <= float(s['lat']) <= box[3])): g[o].append(s)
print('snapshot', y, len(own), 'states', sum(len(v) for v in g.values()), 'owned')
for t, v in sorted(g.items(), key=lambda kv: -len(kv[1])):
    if len(v) < mn: continue
    lo = [float(s['lon']) for s in v]; la = [float(s['lat']) for s in v]
    print(f'{nm.get(t, t)}|{t}|{len(v)}|{sum(lo)/len(lo):.0f},{sum(la)/len(la):.0f}|{min(lo):.0f}..{max(lo):.0f},{min(la):.0f}..{max(la):.0f}')
