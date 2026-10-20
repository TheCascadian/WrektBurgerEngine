"""Draws the owners of the states of a start date: python tools/plot_history.py <year> <w,s,e,n> <out.png> - a dot per state, a label per polity."""
import re, sys, csv, os, collections
sys.stdout.reconfigure(encoding='utf-8')
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
states = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
year = int(sys.argv[1]); box = [float(x) for x in sys.argv[2].split(',')]; out = sys.argv[3]
best = None
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if best is None or abs(y - year) < abs(best[0] - year): best = (y, m.group(2))
own = re.search(r'owners = \{(.*?)\}', best[1], re.S).group(1).split()
nm = dict(names)
mm = re.search(r'names = \{(.*?)\}', best[1], re.S)
if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
import colorsys, hashlib
def col(t):
    h = int(hashlib.md5(t.encode()).hexdigest()[:8], 16)
    return colorsys.hsv_to_rgb((h % 997) / 997, 0.45 + (h >> 8) % 40 / 100, 0.75 + (h >> 16) % 25 / 100)
w = box[2] - box[0]; h = box[3] - box[1]
fig, ax = plt.subplots(figsize=(14, 14 * h / w * 1.0 if h / w < 1.2 else 16))
g = collections.defaultdict(list)
for o, s in zip(own, states):
    lo, la = float(s['lon']), float(s['lat'])
    if box[0] <= lo <= box[2] and box[1] <= la <= box[3]:
        g[o].append((lo, la))
for o, pts in g.items():
    ax.scatter([p[0] for p in pts], [p[1] for p in pts], s=(9 * 90 / max(w, h)) , color=('lightgrey' if o == '_' else col(o)), marker='s', linewidths=0)
for o, pts in g.items():
    if o == '_': continue
    ax.text(sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts), nm.get(o, o), fontsize=6, ha='center', va='center', clip_on=True)
ax.set_xlim(box[0], box[2]); ax.set_ylim(box[1], box[3]); ax.set_aspect(1.0 / max(0.2, abs(__import__('math').cos(__import__('math').radians((box[1] + box[3]) / 2)))))
ax.set_title(f'{best[0]}'); ax.grid(alpha=.2)
fig.savefig(out, dpi=100, bbox_inches='tight')
