"""Countries of one date that carry the same name under two tags: python tools/audit_dupnames.py"""
import re, csv, collections, sys, os
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects/World500BCE/game/')
names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(P + 'common/countries/countries.txt', encoding='utf-8').read(), re.M))
key_of = {}
for l in open(os.path.join(REPO, 'tools/historical-basemaps/alias_report.txt'), encoding='utf-8'):
    f = l.rstrip('\n').split('\t'); key_of[f[0]] = f[1]
text = open(P + 'history/timeline/snapshots.txt', encoding='utf-8').read()
allname = collections.defaultdict(set)
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1))
    if y > 1492: continue
    own = re.search(r'owners = \{(.*?)\}', m.group(2), re.S).group(1).split()
    nm = dict(names)
    mm = re.search(r'names = \{(.*?)\}', m.group(2), re.S)
    if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
    c = collections.Counter(o for o in own if o != '_')
    byname = collections.defaultdict(list)
    for t, n in c.items():
        if n >= 2: byname[nm.get(t, t).lower()].append((key_of.get(t), n))
    for n, l in byname.items():
        if len(l) > 1: allname[n].add((y, tuple(l)))
for n, s in sorted(allname.items()):
    ys = sorted(x[0] for x in s)
    print(n, '|', ys[:6], '|', sorted(s)[0][1])
