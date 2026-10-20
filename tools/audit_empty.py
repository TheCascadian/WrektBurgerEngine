"""Where the lands nobody holds are, by date: python tools/audit_empty.py <year> [cell degrees]"""
import re, csv, collections, sys, os
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects/World500BCE/game/')
st = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
text = open(P + 'history/timeline/snapshots.txt', encoding='utf-8').read()
year = int(sys.argv[1]); cell = float(sys.argv[2]) if len(sys.argv) > 2 else 8
best = None
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if best is None or abs(y - year) < abs(best[0] - year): best = (y, m.group(2))
own = re.search(r'owners = \{(.*?)\}', best[1], re.S).group(1).split()
c = collections.defaultdict(list)
for o, s in zip(own, st):
    if o == '_':
        lo, la = float(s['lon']), float(s['lat'])
        c[(int(lo // cell) * cell, int(la // cell) * cell)].append((lo, la))
print('snapshot', best[0], 'empty', sum(len(v) for v in c.values()))
for k, v in sorted(c.items(), key=lambda kv: -len(kv[1]))[:25]:
    print(f'  cell lon {k[0]:.0f}..{k[0]+cell:.0f} lat {k[1]:.0f}..{k[1]+cell:.0f}: {len(v)}')
