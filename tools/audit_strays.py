"""Stray states of every start date: small pieces of a country that lie cut off inside the land of another one (python tools/audit_strays.py [max_size] [share] [date ...]).

A piece is a group of touching states with one owner. A stray is a piece of at most max_size states (default 6), other than the biggest piece of its owner, whose land border runs for at least `share`
(default 0.7) along one other owner. Exclaves that are real (Kaliningrad, Alaska, Ceuta, Lesotho ...) show up as well: read the list, then fix with a U realm or an R rename.
Also lists holes: unowned pieces enclosed by one owner. Output: date, owner, size, enclosing owner, middle of the piece, a state key.
"""
import re, sys, os, csv, collections
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..'))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, 'geo'))
import history_build as B
import adjacency

P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
args = [a for a in sys.argv[1:]]
max_size = int(args[0]) if len(args) > 0 else 6
share = float(args[1]) if len(args) > 1 else 0.7
min_owner = int(os.environ.get('MIN_OWNER', '15'))     # only countries with at least this many states (tribes inside tribes are normal)
want = set(int(a) for a in args[2:])

names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
rows = sorted(csv.DictReader(open(os.path.join(REPO, 'tools/historical-basemaps/states.csv'), encoding='utf-8')), key=lambda r: r['key'])
keys = [r['key'] for r in rows]
pos = [(float(r['lon']), float(r['lat'])) for r in rows]
locs_of = B.state_locations()
nb = adjacency.load(P)
state_of = {}
idx = {k: i for i, k in enumerate(keys)}
for k, ls in locs_of.items():
    for l in ls:
        state_of[l] = idx.get(k)
near = [collections.Counter() for _ in keys]
for a, row in nb.items():
    sa = state_of.get(a)
    if sa is None:
        continue
    for b, v in row.items():
        sb = state_of.get(b)
        if sb is not None and sb != sa:
            near[sa][sb] += v


def components(owner):
    seen = [False] * len(keys)
    comps = []
    for s in range(len(keys)):
        if seen[s] or owner[s] == '_':
            continue
        stack = [s]; seen[s] = True; comp = []
        while stack:
            a = stack.pop(); comp.append(a)
            for b in near[a]:
                if not seen[b] and owner[b] == owner[s]:
                    seen[b] = True; stack.append(b)
        comps.append(comp)
    return comps


total = 0
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if want and y not in want:
        continue
    owner = re.search(r'owners = \{(.*?)\}', m.group(2), re.S).group(1).split()
    nm = dict(names)
    mm = re.search(r'names = \{(.*?)\}', m.group(2), re.S)
    if mm:
        nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
    comps = components(owner)
    by_owner = collections.defaultdict(list)
    for c in comps:
        by_owner[owner[c[0]]].append(c)
    found = []
    for o, cs in by_owner.items():
        cs.sort(key=len, reverse=True)
        if sum(len(c) for c in cs) < min_owner:
            continue
        for c in cs[1:]:
            if len(c) > max_size:
                continue
            members = set(c)
            contact = collections.Counter()
            for a in c:
                for b, v in near[a].items():
                    if b not in members:
                        contact[owner[b]] += v
            tot = sum(contact.values())
            if not tot:
                continue
            top, v = contact.most_common(1)[0]
            if top != o and top != '_' and v / tot >= share:
                lon = sum(pos[a][0] for a in c) / len(c); lat = sum(pos[a][1] for a in c) / len(c)
                found.append((nm.get(o, o), len(c), nm.get(top, top) if top != '_' else '(unowned)', lon, lat, keys[c[0]]))
    # holes: unowned pieces enclosed by one owner
    seen = [False] * len(keys)
    for s in range(len(keys)):
        if seen[s] or owner[s] != '_':
            continue
        stack = [s]; seen[s] = True; comp = []
        while stack:
            a = stack.pop(); comp.append(a)
            for b in near[a]:
                if not seen[b] and owner[b] == '_':
                    seen[b] = True; stack.append(b)
        if len(comp) > max_size:
            continue
        members = set(comp)
        contact = collections.Counter()
        for a in comp:
            for b, v in near[a].items():
                if b not in members:
                    contact[owner[b]] += v
        tot = sum(contact.values())
        if tot and contact.most_common(1)[0][1] / tot >= 0.9 and contact.most_common(1)[0][0] != '_':
            top = contact.most_common(1)[0][0]
            lon = sum(pos[a][0] for a in comp) / len(comp); lat = sum(pos[a][1] for a in comp) / len(comp)
            found.append(('(unowned hole)', len(comp), nm.get(top, top), lon, lat, keys[comp[0]]))
    total += len(found)
    print('== %d: %d strays' % (y, len(found)))
    for f in sorted(found, key=lambda f: -f[1])[:40]:
        print('   %-30s %2d states inside %-28s at %7.2f %6.2f  %s' % f)
print('total', total)
