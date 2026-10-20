"""Lists the lands still held by generated clusters (keys with #) or unowned, by date: python tools/audit_clusters.py <year> [w,s,e,n]"""
import re, sys, csv, os, collections
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
key_of = {}
for line in open(os.path.join(REPO, 'tools/historical-basemaps/alias_report.txt'), encoding='utf-8'):
    f = line.rstrip('\n').split('\t')
    key_of[f[0]] = f[1]
states = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
year = int(sys.argv[1]); box = [float(x) for x in sys.argv[2].split(',')] if len(sys.argv) > 2 else None
best = None
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if best is None or abs(y - year) < abs(best[0] - year): best = (y, m.group(2))
own = re.search(r'owners = \{(.*?)\}', best[1], re.S).group(1).split()
nm = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
mm = re.search(r'names = \{(.*?)\}', best[1], re.S)
if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
g = collections.defaultdict(list)
empty = []
for o, s in zip(own, states):
    lo, la = float(s['lon']), float(s['lat'])
    if box and not (box[0] <= lo <= box[2] and box[1] <= la <= box[3]): continue
    if o == '_': empty.append((lo, la)); continue
    if '#' in key_of.get(o, '') or key_of.get(o, '').startswith('mc:'):
        g[o].append((lo, la))
print('snapshot', best[0], 'cluster-owned states', sum(len(v) for v in g.values()), 'empty', len(empty))
for o, v in sorted(g.items(), key=lambda kv: -len(kv[1]))[:60]:
    print(nm.get(o, o), key_of.get(o), len(v), '%.0f,%.0f' % (sum(p[0] for p in v) / len(v), sum(p[1] for p in v) / len(v)))
