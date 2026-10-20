"""The build step of tools/import-history.py."""
import os, re, sys, math, hashlib, subprocess, colorsys, collections, unicodedata
import history_tags as T

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
PROJECT = os.path.join(REPO, 'projects', 'World500BCE', 'game')


GENERATED = '# Countries that do not exist at the first start date'


def git_show(rel):
    return subprocess.check_output(['git', 'show', 'HEAD:projects/World500BCE/game/' + rel], cwd=REPO).decode('utf-8').replace('\r\n', '\n')


def git_original(rel):
    """The committed file without what an earlier build appended to it (builds are not cumulative: the generated countries are cut off, and the lists of politics and technologies keep only the tags of the project's own countries)."""
    text = git_show(rel)
    if rel == 'common/countries/countries.txt':
        return text.split(GENERATED, 1)[0].rstrip('\n') + '\n'
    if rel in ('history/politics/countries.txt', 'history/technologies.txt'):
        own = set(re.findall(r'^([A-Z0-9]{3}) = \{', git_show('common/countries/countries.txt').split(GENERATED, 1)[0], re.M))
        out, seen = [], set()
        for block in re.split(r'(?m)^(?=[A-Z0-9]{3} = \{)', text):
            m = re.match(r'([A-Z0-9]{3}) = \{', block)
            if m is None:
                out.append(block)               # the comment lines at the top
            elif m.group(1) in own and m.group(1) not in seen:
                seen.add(m.group(1)); out.append(block)
        return ''.join(out).rstrip('\n') + '\n'
    return text


def ascii_name(n):
    return ''.join(c for c in unicodedata.normalize('NFKD', n) if not unicodedata.combining(c))


def family_key(name):
    norm = T.normalize(name)
    for rx, key in T.FAMILIES:
        if re.search(rx, norm):
            return key or norm, norm
    return norm, norm


def parse_pops(text):
    out = {}
    cur = None
    for line in text.split('\n'):
        m = re.match(r'^([a-z0-9_]+) = \{', line)
        if m:
            cur = m.group(1); out[cur] = collections.Counter(); continue
        m = re.search(r'culture = (\w+) religion = (\w+) type = \w+ size = (\d+)', line)
        if m and cur:
            out[cur][(m.group(1), m.group(2))] += int(m.group(3))
    return out


def cluster(items, k):
    """items: list of (index, lon, lat). Farthest-point seeds, nearest seed takes each."""
    def d(a, b):
        dx = (a[1] - b[1]) * math.cos(math.radians((a[2] + b[2]) / 2)); dy = a[2] - b[2]
        return dx * dx + dy * dy
    seeds = [items[0]]
    while len(seeds) < min(k, len(items)):
        seeds.append(max(items, key=lambda it: min(d(it, s) for s in seeds)))
    groups = collections.defaultdict(list)
    for it in items:
        groups[min(range(len(seeds)), key=lambda i: d(it, seeds[i]))].append(it)
    return [(seeds[i], groups[i]) for i in sorted(groups, key=lambda i: (seeds[i][1], seeds[i][2]))]


def result_tribes(result, states, idx, name, norm, year='bc500'):
    """A region of tribes: seeds when there are some, else clusters of about TRIBE_SIZE states named by compass point."""
    import history_tribes as TR
    import history_names as N
    seeds = TR.CULTURE_SEEDS.get(norm)
    pretty = re.sub(r' \(.*\)$', '', name)
    if seeds:
        for i in idx:
            lon, lat = states[i][1], states[i][2]
            k, disp, _, _ = min(seeds, key=lambda s: ((lon - s[2]) * math.cos(math.radians(lat))) ** 2 + (lat - s[3]) ** 2)
            result[i] = (k, N.tidy(disp))
        return
    k = max(1, round(len(idx) / TR.TRIBE_SIZE))
    parts = cluster([(i, states[i][1], states[i][2]) for i in idx], k)
    cx = sum(states[i][1] for i in idx) / len(idx); cy = sum(states[i][2] for i in idx) / len(idx)
    span = max(max(abs(states[i][1] - cx) for i in idx), max(abs(states[i][2] - cy) for i in idx), 1)
    import history_names as N
    cx = sum(states[i][1] for i in idx) / len(idx); cy = sum(states[i][2] for i in idx) / len(idx)
    span = max(max(abs(states[i][1] - cx) for i in idx), max(abs(states[i][2] - cy) for i in idx), 1)
    seen = collections.Counter()
    for seed, members in parts:
        mx = sum(it[1] for it in members) / len(members) - cx; my = sum(it[2] for it in members) / len(members) - cy
        slot = 8 if abs(mx) < 0.28 * span and abs(my) < 0.28 * span else int((math.degrees(math.atan2(my, mx)) % 360 + 22.5) // 45) % 8
        seen[slot] += 1
        ikey = f'{norm}#{slot}.{seen[slot]}'
        pick = N.cluster_name(pretty, ikey, N.year_of(year))
        label = pick
        for it in members:
            result[it[0]] = (ikey, label)


import history_gazetteer as G


def resolve(m, states, raw, year):
    import history_names as N
    import history_tribes as TR2
    import history_gazetteer as G
    """Per state: None or (key, display name)."""
    result = [None] * len(states)
    by_name = collections.defaultdict(list)
    for i, name in enumerate(raw):
        if name:
            by_name[name].append(i)
    for name, idx in by_name.items():
        norm = T.normalize(name)
        if T.IGNORE.search(norm) or T.IGNORE.search(name.lower()):
            continue
        seeds = next((v for rx, v in T.SEED_NAMES if re.search(rx, norm)), None)
        if seeds is None:
            import history_tribes as TR2
            yr = N.year_of(year)
            seeds = next((sd for a0, b0, rx, sd in TR2.YEAR_SEEDS if a0 <= yr <= b0 and re.search(rx, norm)), None)
            if seeds:
                merge_year = True
        if seeds is None and norm == 'scythians' and len(idx) >= 20:
            import history_tribes as TR1
            seeds = TR1.BLOCK_SEEDS['SCY'] + TR1.BLOCK_SEEDS['SAR'] + TR1.BLOCK_SEEDS['SAK']
        if seeds is None and re.match(r'^(hallstatt|celts$|germanic tribes$)', norm):
            import history_tribes as TR0
            seeds = TR0.hallstatt_seeds()
        if seeds and len(idx) >= 2:
            live = list(seeds)
            merge_small = re.match(r'^(hallstatt|celts$|germanic tribes$|scythians|holy roman)', norm) is not None or any(rx_ for a0, b0, rx_, sd in __import__('history_tribes').YEAR_SEEDS if sd is seeds)
            def nearest(i, pool):
                lon, lat = states[i][1], states[i][2]
                return min(pool, key=lambda sd: ((lon - sd[2]) * math.cos(math.radians(lat))) ** 2 + (lat - sd[3]) ** 2)
            while merge_small and len(live) > 1:
                cnt = collections.Counter(nearest(i, live)[0] for i in idx)
                small = [sd for sd in live if cnt[sd[0]] < TR2.MIN_SIZE]
                if not small:
                    break
                live.remove(min(small, key=lambda sd: cnt[sd[0]]))
            soft = re.match(r'^(hallstatt|celts$|germanic tribes$|scythians)', norm) is not None
            for i in idx:
                k, disp, _, _ = nearest(i, live)
                result[i] = (k, N.tidy(disp))
                if soft:
                    G.SOFT.add(k)
            continue
        if T.TRIBAL.search(norm) or N.COLLECTIVE.search(norm) and not T.NOT_UMBRELLA.search(norm):
            result_tribes(result, states, idx, name, norm, year)
            G.SOFT.update(result[i][0] for i in idx if result[i])
            continue
        key, norm = family_key(name)
        base = re.sub(r' and republics$', '', norm)
        if re.match(r'^(minor )?hindu', base) or re.match(r'^rajput', base) or re.match(r'^islamic and hindu', base):
            base = 'hindu kingdoms' if not base.startswith(('islamic', 'rajput')) else base.split(' ')[0] + ' kingdoms'
        if T.UMBRELLA.search(base) and not T.NOT_UMBRELLA.search(base):
            parts = cluster([(i, states[i][1], states[i][2]) for i in idx], max(1, min(8, round(len(idx) / 8))))
            cx = sum(states[i][1] for i in idx) / len(idx); cy = sum(states[i][2] for i in idx) / len(idx)
            span = max(max(abs(states[i][1] - cx) for i in idx), max(abs(states[i][2] - cy) for i in idx), 1)
            cx = sum(states[i][1] for i in idx) / len(idx); cy = sum(states[i][2] for i in idx) / len(idx)
            span = max(max(abs(states[i][1] - cx) for i in idx), max(abs(states[i][2] - cy) for i in idx), 1)
            pretty = re.sub(r' \(.*\)$', '', name)
            seen = collections.Counter()
            for n, (seed, members) in enumerate(parts):
                mx = sum(it[1] for it in members) / len(members) - cx; my = sum(it[2] for it in members) / len(members) - cy
                slot = 8 if abs(mx) < 0.28 * span and abs(my) < 0.28 * span else int((math.degrees(math.atan2(my, mx)) % 360 + 22.5) // 45) % 8
                seen[slot] += 1
                ikey = f'{base}#{slot}.{seen[slot]}'
                pick = N.cluster_name(pretty, ikey, N.year_of(year))
                label = pick
                for it in members:
                    result[it[0]] = (ikey, label)
            continue
        for i in idx:
            result[i] = (key, N.clean(name))
    return result


def gen_code(name, used):
    letters = re.sub('[^A-Z]', '', ascii_name(name).upper())
    cands = []
    if len(letters) >= 3:
        cands.append(letters[:3])
        for a in range(1, len(letters)):
            for b in range(a + 1, len(letters)):
                cands.append(letters[0] + letters[a] + letters[b])
    for c in cands:
        if c not in used:
            return c
    import itertools, string
    for a, b in itertools.product(string.digits + string.ascii_uppercase, repeat=2):
        c = (letters[:1] or 'X') + a + b
        if c not in used:
            return c
    for a, b, c0 in itertools.product(string.ascii_uppercase, string.ascii_uppercase + string.digits, string.ascii_uppercase + string.digits):
        c = a + b + c0
        if c not in used:
            return c
    for d, a, b in itertools.product(string.digits, string.ascii_uppercase, string.ascii_uppercase):
        c = d + a + b   # digit first, then two letters: never a number
        if c not in used:
            return c
    raise RuntimeError('no tag code left for ' + name)


def region_hue(lon, lat):
    if lat < -10 and -20 < lon < 55: return 0.10    # southern Africa
    if -20 < lon < 55 and lat < 35 and lat > -35: return 0.12   # Africa and Arabia
    if -25 < lon < 45 and lat >= 35: return 0.60    # Europe
    if 25 < lon < 75 and lat > 12: return 0.30      # the Near East and Central Asia
    if 60 <= lon < 100: return 0.05                 # South Asia
    if lon >= 100: return 0.95                      # East Asia
    if lon < -30: return 0.48                       # the Americas
    return 0.55


GOLDEN = {}


def make_color(key, lon, lat):
    if key in T.COLORS:
        return T.COLORS[key]
    n = GOLDEN.setdefault(key, len(GOLDEN))
    h = int(hashlib.md5(key.encode()).hexdigest()[:8], 16)
    hue = (n * 0.61803398875 + region_hue(lon, lat) * 0.15) % 1.0
    sat = 0.50 + ((h >> 10) % 100) / 100 * 0.30
    val = 0.62 + ((h >> 20) % 100) / 100 * 0.28
    r, g, b = colorsys.hsv_to_rgb(hue, sat, val)
    return int(r * 255), int(g * 255), int(b * 255)


def compute_snaps(m, states, orig, oname):
    """The owner of every state (a point) at every start date: [(year name, astronomical year, [None | (key, name)])]. states: (key of the state it belongs to, lon, lat, ...)."""
    import history_tribes as TR
    import history_names as N
    snaps = []
    feats0 = m.load_snapshot('bc500')
    raw0 = [None if orig[s[0]] else m.owner_of(feats0, s[1], s[2]) for s in states]
    res0 = resolve(m, states, raw0, 'bc500')
    first = []
    for i, st in enumerate(states):
        o = orig[st[0]]
        if o is None:
            first.append(res0[i])
        elif o in TR.BLOCK_SEEDS:
            lon, lat = st[1], st[2]
            k, disp, _, _ = min(TR.BLOCK_SEEDS[o], key=lambda sd: ((lon - sd[2]) * math.cos(math.radians(lat))) ** 2 + (lat - sd[3]) ** 2)
            first.append((k, disp))
            G.SOFT.add(k)
        else:
            first.append(('orig:' + o, oname[o]))
    for i, st in enumerate(states):
        if first[i] is None:
            for (w, so, e, no), seeds in TR.MASK_SEEDS:
                if w <= st[1] <= e and so <= st[2] <= no:
                    k, disp, _, _ = min(seeds, key=lambda sd: ((st[1] - sd[2]) * math.cos(math.radians(st[2]))) ** 2 + (st[2] - sd[3]) ** 2)
                    first[i] = (k, disp)
                    G.SOFT.add(k)
                    break
    print('bc500 gazetteer', G.apply(first, states, -499), file=sys.stderr)
    snaps.append(('bc500', -499, first))
    for y in m.YEARS:
        feats = m.load_snapshot(y)
        raw = [m.owner_of(feats, s[1], s[2]) for s in states]
        res = resolve(m, states, raw, y)
        if m.astronomical(y) <= 700:
            # the lands the dataset leaves empty keep the peoples of the first start date (a people with fewer than three states of them is left out)
            groups = collections.defaultdict(list)
            for (w, so, e, no), seeds in [(b0, sd0) for a0, z0, b0, sd0 in TR.MASK_YEAR if a0 <= m.astronomical(y) <= z0] + (TR.MASK_SEEDS + TR.MASK_LATER if m.astronomical(y) <= 250 else []):
                idx = [i for i, st in enumerate(states) if res[i] is None and not raw[i] and w <= st[1] <= e and so <= st[2] <= no]
                live = list(seeds)
                def nearest(i):
                    return min(live, key=lambda sd: ((states[i][1] - sd[2]) * math.cos(math.radians(states[i][2]))) ** 2 + (states[i][2] - sd[3]) ** 2)
                while len(live) > 1 and idx:
                    cnt = collections.Counter(nearest(i)[0] for i in idx)
                    small = [sd for sd in live if cnt[sd[0]] < TR.MIN_SIZE]
                    if not small:
                        break
                    live.remove(min(small, key=lambda sd: cnt[sd[0]]))
                for i in idx:
                    sd = nearest(i)
                    res[i] = (sd[0], N.tidy(sd[1]))
                    groups[sd[0]].append(i)
                    G.SOFT.add(sd[0])
            for bi, ((w, so, e, no), fam) in enumerate(TR.MASK_CLUSTER):
                idx = [i for i, st in enumerate(states) if res[i] is None and not raw[i] and w <= st[1] <= e and so <= st[2] <= no]
                if len(idx) >= TR.MIN_SIZE:
                    parts = cluster([(i, states[i][1], states[i][2]) for i in idx], max(1, round(len(idx) / TR.TRIBE_SIZE)))
                    for j, (seed, part) in enumerate(parts):
                        ikey = f'mc:{bi}#{j}'
                        nm2 = N.cluster_name(fam, ikey, N.year_of(y))
                        for it in part:
                            res[it[0]] = (ikey, nm2)
            for k, members in groups.items():
                if len(members) > TR.TRIBE_SIZE * 2:
                    parts = cluster([(i, states[i][1], states[i][2]) for i in members], round(len(members) / TR.TRIBE_SIZE))
                    for j, (seed, part) in enumerate(parts):
                        if j == 0:
                            continue
                        ikey = f'{k}#{j}'
                        nm2 = N.cluster_name('steppe tribes' if states[members[0]][1] > 28 and states[members[0]][2] > 38 else 'celts', ikey, N.year_of(y))
                        for it in part:
                            res[it[0]] = (ikey, nm2)
        # the dataset knows the Mongol and Chinese empire of 1260-1500 by one polygon: Yuan, then Ming
        ay0 = m.astronomical(y)
        res = [(('yuan' if ay0 < 1368 else 'ming'), o[1]) if o and o[0] == 'great' else o for o in res]
        KEY_MERGE = {'huns': 'hunnic', 'ruanruan': 'rouran', 'western gokturk khaganate': 'western turkic khaganate', 'uyghurs': 'uyghur khaganate', 'unified silla': 'silla', 'khazars': 'khazar khaganate',
                     'the golden horde': 'goldenhorde', 'croatian': 'croatia', 'croats': 'croatia', 'kyivan': 'kievan rus', 'tibetan': 'tufan', 'indogreek': 'gan', 'oirat confederation': 'oirat', 'hanstate': 'han', 'parthian': 'pth'}
        res = [(KEY_MERGE[o[0]], o[1]) if o and o[0] in KEY_MERGE else o for o in res]
        if ay0 >= 224:
            res = [('sasanian', o[1]) if o and o[0] in ('pth', 'iran') else o for o in res]
        print(y, 'gazetteer', G.apply(res, states, m.astronomical(y)), file=sys.stderr)
        if -130 <= m.astronomical(y) <= 200:
            # Greece and Macedon are Roman provinces
            for i, o in enumerate(res):
                if o and o[0] in ('ath', 'thb', 'spa', 'cor', 'arg', 'ths', 'macedon', 'epirus', 'aetolia', 'achaea', 'elis', 'phocis') and 19 <= states[i][1] <= 28:
                    res[i] = ('rom', 'Roman Republic')
        res = [(o[0], N.YEAR_NAMES.get((y, o[1]), o[1])) if o else None for o in res]
        snaps.append((y, m.astronomical(y), res))
        print(y, 'done', file=sys.stderr)
    return snaps


def prepare(m):
    """The data of the first start date and the names in use: (owner of each state of the project, name of each original country, original countries idle at the first date)."""
    import history_tribes as TR
    # the first start date: the data of the project, with its blocks and the unowned farming and herding lands cut into tribes
    cur_states = open(os.path.join(PROJECT, 'map', 'states', '00_states.txt'), encoding='utf-8').read().replace('\r\n', '\n')
    orig = {}
    for k, b in re.findall(r'\n([a-z0-9_]+) = \{(.*?)\n\}', cur_states, re.S):
        mo = re.search(r'owner = (\w+)', b)
        orig[k] = mo.group(1) if mo else None
    oc = git_original('common/countries/countries.txt')
    oname = dict(re.findall(r'^([A-Z0-9]{3}) = \{\n\tname = "([^"]*)"', oc, re.M))
    for t in oname:
        T.EXISTING['orig:' + t] = t
    for tag, seeds in TR.BLOCK_SEEDS.items():
        T.EXISTING[seeds[0][0]] = tag
    import history_names as N
    active = {o for o in orig.values() if o}
    N.ALL_USED.update(oname[t] for t in active if t in oname)
    oname_rev = {}
    for t, nme in oname.items():
        if t not in active:
            oname_rev.setdefault(nme, t)
    for fn in ('history_tags.py', 'history_tribes.py'):
        N.ALL_USED.update(N.tidy(x) for x in re.findall(r"'([A-Z][^'0-9]{2,40})'", open(os.path.join(REPO, 'tools', fn), encoding='utf-8').read()))
    return orig, oname, oname_rev


def wasteland_states():
    """Keys of the states whose locations are all impassable (type = impassable in map/locations.txt, see tools/geo/mark_wasteland.py): no country owns them at any date."""
    loc_text = open(os.path.join(PROJECT, 'map', 'locations.txt'), encoding='utf-8').read()
    bad = set(re.findall(r'\{ id = (land_\d+) type = impassable \}', loc_text))
    if not bad:
        return set()
    out = set()
    text = open(os.path.join(PROJECT, 'map', 'states', '00_states.txt'), encoding='utf-8').read()
    for m in re.finditer(r'\n([a-z0-9_]+) = \{(.*?)\n\}', text, re.S):
        lm = re.search(r'locations = \{([^}]*)\}', m.group(2))
        if lm and lm.group(1).split() and all(k in bad for k in lm.group(1).split()):
            out.add(m.group(1))
    return out


FUSE_SIZE = 6      # locations a state has on a start date: touching states of one owner are fused up to about this size (tools/geo/adjacency.py says who touches)


def state_locations():
    """state key -> its location ids (map/states/00_states.txt)."""
    text = open(os.path.join(PROJECT, 'map', 'states', '00_states.txt'), encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'\n([a-z0-9_]+) = \{(.*?)\n\}', text, re.S):
        lm = re.search(r'locations = \{([^}]*)\}', m.group(2))
        if lm:
            out[m.group(1)] = lm.group(1).split()
    return out


_FUSE_GRAPH = {}      # the graph of touching states is the same for every date: made once


def _fuse_graph(keys, locs_of, nb, pos, reach):
    idx = {k: i for i, k in enumerate(keys)}
    state_of = {}
    for k, ls in locs_of.items():
        for l in ls:
            state_of[l] = idx.get(k)
    n = len(keys)
    near = [dict() for _ in range(n)]
    for a, row in nb.items():
        sa = state_of.get(a)
        if sa is None:
            continue
        for b, v in row.items():
            sb = state_of.get(b)
            if sb is not None and sb != sa:
                near[sa][sb] = near[sa].get(sb, 0) + v
    # states that face the same lake, strait or bay and lie close are neighbours too (islands, lake lands), but weaker than a shared land border
    for w, row in nb.items():
        if not w.startswith(('sea_', 'lake_')):
            continue
        around = sorted({state_of[x] for x in row if state_of.get(x) is not None})
        if len(around) > 60:
            continue
        for ai, a in enumerate(around):
            for b in around[ai + 1:]:
                if math.hypot((pos[a][0] - pos[b][0]) * math.cos(math.radians(pos[a][1])), pos[a][1] - pos[b][1]) <= reach:
                    near[a].setdefault(b, 1); near[b].setdefault(a, 1)
    return near


def fuse_groups(keys, tags, waste, locs_of, nb, pos, size=FUSE_SIZE, reach=1.5):
    """The states of one start date: groups of touching states with one owner (impassable ones apart) fused up to about `size` locations. keys: the sorted state keys; tags: the owner
    of each; returns [[index, ...]], the first index of a group being the state that stays."""
    n = len(keys)
    memo = (len(keys), id(nb), reach)
    if _FUSE_GRAPH.get('key') != memo:
        _FUSE_GRAPH['key'] = memo
        _FUSE_GRAPH['near'] = _fuse_graph(keys, locs_of, nb, pos, reach)
    near = _FUSE_GRAPH['near']
    cls = [(tags[i], waste[i]) for i in range(n)]
    cnt = [len(locs_of.get(k, ())) or 1 for k in keys]
    group = [-1] * n
    groups = []
    order = sorted(range(n), key=lambda i: (sum(1 for j in near[i] if cls[j] == cls[i]), i))      # states with the fewest neighbours of their owner first, so lonely ones are not left over
    for s in order:
        if group[s] >= 0:
            continue
        g = [s]; group[s] = len(groups); total = cnt[s]
        frontier = [s]
        while frontier and total < size:
            nxt = []
            for a in frontier:
                for b in sorted(near[a], key=lambda x: (-near[a][x], x)):
                    if group[b] < 0 and cls[b] == cls[s] and total + cnt[b] <= size * 1.5:
                        group[b] = group[s]; g.append(b); total += cnt[b]; nxt.append(b)
                        if total >= size:
                            break
                if total >= size:
                    break
            frontier = nxt
        groups.append(g)
    # small left-overs join the neighbouring group of the same owner
    sizes = [sum(cnt[i] for i in g) for g in groups]
    for gi, g in sorted(enumerate(groups), key=lambda x: sum(cnt[i] for i in x[1])):
        if not g or sizes[gi] >= size / 2:
            continue
        best = None
        for a in g:
            for b, v in near[a].items():
                gb = group[b]
                if gb != gi and cls[b] == cls[g[0]] and groups[gb] and sizes[gb] + sizes[gi] <= size * 3 and (best is None or v > best[0]):
                    best = (v, gb)
        if best:
            gb = best[1]
            for a in g:
                group[a] = gb
            groups[gb].extend(g); sizes[gb] += sizes[gi]; groups[gi] = []
    return [sorted(g) for g in groups if len(g) > 1]


def absorb_enclaves(owners, near, max_size=8, share=0.8, min_state=40, big=None):
    """Small pieces of a people (a tribe of the gazetteer or a generated cluster) that lie inside the land of one real state are part of that state: the dataset leaves holes in the big realms, and
    the peoples that fill the empty land must not leave islands in them. owners: per state None or (key, name). Returns the number of states changed."""
    def tribal(k):
        return k in G.PEOPLE_KEYS or '#' in k or k.startswith('mc:')
    n = len(owners)
    count = collections.Counter(o[0] for o in owners if o)
    first = {}
    for o in owners:
        if o and o[0] not in first:
            first[o[0]] = o
    seen = [False] * n
    changed = 0
    for s in range(n):
        if seen[s] or owners[s] is None or not tribal(owners[s][0]):
            continue
        key = owners[s][0]
        stack = [s]; seen[s] = True; comp = []
        while stack:
            a = stack.pop(); comp.append(a)
            for b in near[a]:
                if not seen[b] and owners[b] is not None and owners[b][0] == key:
                    seen[b] = True; stack.append(b)
        if len(comp) > (big[0] if big else max_size):
            continue
        members = set(comp)
        contact = collections.Counter()
        for a in comp:
            for b, v in near[a].items():
                if b not in members:
                    contact[owners[b][0] if owners[b] else None] += v
        tot = sum(contact.values())
        if not tot:
            continue
        top, v = contact.most_common(1)[0]
        need = big[1] if (big and len(comp) > max_size) else share      # a bigger piece must be all but surrounded
        if top is not None and top != key and v / tot >= need and not tribal(top) and count[top] >= min_state:
            for a in comp:
                owners[a] = first[top]
            changed += len(comp)
    return changed


def absorb_holes(owners, near, waste, max_size=12, share=0.9, min_state=12):
    """Unowned pieces of a few states that a real state all but surrounds are part of that state (the dataset leaves small holes in its big realms). Impassable lands stay as they are."""
    n = len(owners)
    count = collections.Counter(o[0] for o in owners if o)
    first = {}
    for o in owners:
        if o and o[0] not in first:
            first[o[0]] = o
    seen = [False] * n
    changed = 0
    for s in range(n):
        if seen[s] or owners[s] is not None:
            continue
        stack = [s]; seen[s] = True; comp = []
        while stack:
            a = stack.pop(); comp.append(a)
            for b in near[a]:
                if not seen[b] and owners[b] is None:
                    seen[b] = True; stack.append(b)
        if len(comp) > max_size or any(waste[a] for a in comp):
            continue
        members = set(comp)
        contact = collections.Counter()
        for a in comp:
            for b, v in near[a].items():
                if b not in members:
                    contact[owners[b][0] if owners[b] else None] += v
        tot = sum(contact.values())
        if not tot:
            continue
        top, v = contact.most_common(1)[0]
        if top is not None and v / tot >= share and count[top] >= min_state:
            for a in comp:
                owners[a] = first[top]
            changed += len(comp)
    return changed


def build_near(keys, locs_of, nb):
    """Which states touch which on land: [{state index: contact pixels}]."""
    idx = {k: i for i, k in enumerate(keys)}
    state_of = {}
    for k, ls in locs_of.items():
        for l in ls:
            state_of[l] = idx.get(k)
    near = [dict() for _ in keys]
    for a, row in nb.items():
        sa = state_of.get(a)
        if sa is None:
            continue
        for b, v in row.items():
            sb = state_of.get(b)
            if sb is not None and sb != sa:
                near[sa][sb] = near[sa].get(sb, 0) + v
    return near


def build(m):
    states = m.load_states()
    keys = [s[0] for s in states]
    order = hashlib.sha1('\n'.join(keys).encode()).hexdigest()
    pops = parse_pops(open(os.path.join(PROJECT, 'history', 'pops', '00_pops.txt'), encoding='utf-8').read().replace('\r\n', '\n'))
    original_countries = git_original('common/countries/countries.txt')
    for o, n in (('Libyan and Berber Tribes', 'Libu and Mazyes'), ('Bantu Homeland Peoples', 'Bantu'), ('Thracian Tribes', 'Thracians'), ('Irrawaddy and Mainland Southeast Asia', 'Pyu'), ('Tamil South', 'Tamilakam'), ('Syracuse and Eastern Sicily', 'Syracuse'), ('Sahel and Mande Lands', 'Mande'),
                 ('Pontic Greek Cities', 'Pontic Greeks'), ('Ionian Greek Cities', 'Ionian Greeks'), ('Naxos and the Cyclades', 'Naxos'), ('Yayoi and Jomon Japan', 'Jomon'), ('Magadha and Ganges', 'Magadha')):
        original_countries = original_countries.replace('"' + o + '"', '"' + n + '"')
    used_codes = set(re.findall(r'^([A-Z0-9]{3}) = \{', original_countries, re.M))

    import history_tribes as TR
    import history_names as N
    orig, oname, oname_rev = prepare(m)
    snaps = compute_snaps(m, states, orig, oname)
    # the dataset's map of 1900 leaves most of colonial Africa without a power (the scramble was just finished): its borders are those of 1914
    sys.path.insert(0, os.path.join(REPO, 'tools', 'geo'))
    import adjacency as ADJ
    near_all = build_near(keys, state_locations(), ADJ.load(PROJECT))
    wasteset0 = wasteland_states()
    absorbed = 0
    fixed = []
    for y, ay, owners in snaps:
        owners = list(owners)
        absorbed += absorb_enclaves(owners, near_all, big=(40, 0.92) if ay >= 1500 else None)
        if ay >= 1500:
            absorbed += absorb_holes(owners, near_all, [False] * len(keys) if ay >= 1815 else [k in wasteset0 for k in keys])     # from 1815 the ice and desert inside a country are its own
        fixed.append((y, ay, owners))
    snaps = fixed
    print(absorbed, 'states of peoples inside real states joined them', file=sys.stderr)
    names_at = {y: k for k, (y, _, _) in enumerate(snaps)}
    if '1900' in names_at and '1914' in names_at:
        s1900, s1914 = snaps[names_at['1900']], snaps[names_at['1914']]
        filled = 0
        o1900 = list(s1900[2])
        for i, st in enumerate(states):
            if o1900[i] is None and s1914[2][i] is not None and -20.0 <= st[1] <= 52.0 and -36.0 <= st[2] <= 37.5:
                o1900[i] = s1914[2][i]; filled += 1
        snaps[names_at['1900']] = (s1900[0], s1900[1], o1900)
        print(filled, 'African states of 1900 taken from 1914', file=sys.stderr)
    # impassable states may be owned, but a country made only of them is no country (nobody can march in or live there): its land has no owner
    waste = wasteland_states()
    if waste:
        dead = [s[0] in waste for s in states]
        cut = 0
        out = []
        for y, ay, owners in snaps:
            live = {o[0] for o, d in zip(owners, dead) if o and not d}
            fixed = [None if (o is not None and o[0] not in live) else o for o in owners]
            cut += sum(1 for o, f in zip(owners, fixed) if o is not None and f is None)
            out.append((y, ay, fixed))
        snaps = out
        print(sum(dead), 'impassable states; owners that held only impassable states dropped:', cut, file=sys.stderr)

    # polities that never hold more than a couple of states (small tribes, single towns) are not countries: their land has no owner
    peak = collections.Counter()
    for y, ay, owners in snaps:
        c = collections.Counter(o[0] for o in owners if o)
        for k, n in c.items():
            peak[k] = max(peak[k], n)
    MIN_STATES = 3
    first_keys = {o[0] for o in snaps[0][2] if o}
    snaps = [(y, ay, owners if y == 'bc500' else [o if (o and (o[0] in T.EXISTING or o[0] in first_keys or o[0] in G.KEYS or peak[o[0]] >= (MIN_STATES if ('#' in o[0] or o[0].startswith('mc:') or o[0] in G.SOFT) else 1))) else None for o in owners]) for y, ay, owners in snaps]
    # the polities dropped just now (too small to be countries) leave holes too: they go to the state around them
    cleaned = []
    holes = 0
    for y, ay, owners in snaps:
        owners = list(owners)
        if ay >= 1500 and y != 'bc500':
            holes += absorb_holes(owners, near_all, [False] * len(keys) if ay >= 1815 else [k in wasteset0 for k in keys])
        cleaned.append((y, ay, owners))
    snaps = cleaned
    print(holes, 'states of dropped polities joined the state around them', file=sys.stderr)

    # one name, one country: polities that show the same name under two keys (a people of the dataset and of the gazetteer) are one
    canon = {}
    for y, ay, owners in snaps:
        for o in owners:
            if o is not None:
                n = N.tidy(o[1]).lower()
                if n not in canon or (o[0] in T.EXISTING and canon[n] not in T.EXISTING):
                    canon[n] = o[0]
    snaps = [(y, ay, [None if o is None else (canon[N.tidy(o[1]).lower()], o[1]) for o in owners]) for y, ay, owners in snaps]

    # tags: the existing ones keep theirs, the others get a code from the name they first appear with
    tag_of, first_name, first_year, first_states = {}, {}, {}, collections.defaultdict(list)
    for y, ay, owners in snaps:
        for i, o in enumerate(owners):
            if o is None:
                continue
            key, disp = o
            disp = N.tidy(disp)
            if key not in first_name:
                first_name[key] = disp; first_year[key] = ay
            if ay == first_year[key]:
                first_states[key].append(i)
    reused = set(T.EXISTING.values())
    for key in sorted(first_name, key=lambda k: (first_year[k], first_name[k])):
        t0 = oname_rev.get(first_name[key])
        if key not in T.EXISTING and t0 and t0 not in reused:
            T.EXISTING[key] = t0       # a country of the project that stands idle at the first start date takes the people of that name
            reused.add(t0)
    for key in sorted(first_name, key=lambda k: (first_year[k], first_name[k])):
        if key in T.EXISTING:
            tag_of[key] = T.EXISTING[key]
        else:
            code = gen_code(first_name[key], used_codes)
            used_codes.add(code)
            tag_of[key] = code
    print('invented names:', N.INVENTED, file=sys.stderr)
    print(len(tag_of), 'tags;', sum(1 for k in tag_of if k not in T.EXISTING), 'new', file=sys.stderr)

    # country definitions
    existing_names = dict(re.findall(r'^([A-Z0-9]{3}) = \{\n\tname = "([^"]*)"', original_countries, re.M))
    defs, politics, tech = [], [], []
    for key in sorted(tag_of, key=lambda k: tag_of[k]):
        tag = tag_of[key]
        if key in T.EXISTING:
            continue
        idx = first_states[key]
        lon = sum(states[i][1] for i in idx) / len(idx); lat = sum(states[i][2] for i in idx) / len(idx)
        pc = collections.Counter()
        for i in idx:
            pc.update(pops.get(keys[i], {}))
        culture, religion = (pc.most_common(1)[0][0] if pc else ('persian', 'mazdayasna'))
        r, g, b = make_color(key, lon, lat)
        defs.append(f'{tag} = {{\n\tname = "{first_name[key].replace(chr(34), "")}"\n\tcolor = {{ {r} {g} {b} }}\n\tculture = {culture}\n\treligion = {religion}\n\tgold = 100\n\tmanpower = 10000\n}}\n')
        politics.append(f'{tag} = {{\n\truling_party = neutrality\n\tpopularity = {{ neutrality = 40 fascist = 30 democratic = 10 communist = 20 }}\n\tlaws = {{ political_parties = restricted_parties }}\n}}\n')
        tech.append(f'{tag} = {{ lag = 40 }}\n')

    def write(rel, text):
        with open(os.path.join(PROJECT, rel), 'w', encoding='utf-8', newline='') as f:
            f.write(text.replace('\n', '\r\n'))

    write('common/countries/countries.txt', original_countries.rstrip('\n') + '\n\n# Countries that do not exist at the first start date: generated by tools/import-history.py from the polities of the historical-basemaps dataset;\n# they appear in history/timeline/snapshots.txt when they own a state.\n' + ''.join(defs))
    write('history/politics/countries.txt', git_original('history/politics/countries.txt').rstrip('\n') + '\n' + ''.join(politics))
    write('history/technologies.txt', git_original('history/technologies.txt').rstrip('\n') + '\n' + ''.join(tech))

    # the snapshots
    out = ['# Who owned each state, by date: generated by tools/import-history.py from the polygons of the historical-basemaps dataset',
           '# (https://github.com/aourednik/historical-basemaps, GPL-3.0) - the polity whose polygon holds the middle of a state owns it; umbrellas such as the Holy Roman',
           '# Empire and the Zhou states are split. owners lists the tag of every state in the order of the sorted state keys (_ = nobody); names lists the countries whose name changed.',
           f'states = {{ hash = {order} count = {len(keys)} }}', '']
    sys.path.insert(0, os.path.join(REPO, 'tools', 'geo'))
    import adjacency
    adj_nb = adjacency.load(PROJECT)
    locs_of = state_locations()
    wasteset = wasteland_states()
    waste_flags = [k in wasteset for k in keys]
    fused_counts = []
    for y, ay, owners in snaps:
        tags = ['_' if o is None else tag_of[o[0]] for o in owners]
        names = {}
        for o in owners:
            if o is None:
                continue
            tag = tag_of[o[0]]
            disp = N.tidy(o[1]).replace('"', '')
            default = existing_names.get(tag) or first_name[o[0]].replace('"', '')
            if disp != default:
                names[tag] = disp
        out.append('snapshot = {')
        out.append(f'\tdate = {ay}.1.1')
        out.append(f'\tname = "{y}"')
        if y == 'bc500': out.append('\tkeep = yes')
        out.append('\towners = {')
        for i in range(0, len(tags), 24):
            out.append('\t\t' + ' '.join(tags[i:i + 24]))
        out.append('\t}')
        gs = fuse_groups(keys, tags, waste_flags, locs_of, adj_nb, [(s[1], s[2]) for s in states])
        out.append('\tgroups = {')
        toks = ['+'.join(str(i) for i in g) for g in gs]
        for i in range(0, len(toks), 12):
            out.append('\t\t' + ' '.join(toks[i:i + 12]))
        out.append('\t}')
        fused_counts.append((y, len(keys) - sum(len(g) - 1 for g in gs)))
        out.append('\tnames = {')
        for t in sorted(names):
            out.append(f'\t\t{t} = "{names[t]}"')
        out.append('\t}')
        out.append('}')
    write('history/timeline/snapshots.txt', '\n'.join(out) + '\n')
    rep = open(os.path.join(m.DATA, 'alias_report.txt'), 'w', encoding='utf-8')
    for key in sorted(tag_of, key=lambda k: tag_of[k]):
        rep.write(f'{tag_of[key]}\t{key}\t{first_name[key]}\t{first_year[key]}\n')
    rep.close()
    print('states per date after fusing:', ' '.join(f'{y}:{n}' for y, n in fused_counts), file=sys.stderr)
    print('written', file=sys.stderr)


if os.environ.get('BUILD_TIMING'):
    # BUILD_TIMING=1 python tools/import-history.py build: the seconds spent in each part of the build
    import time, atexit, functools
    _SPENT = collections.Counter()

    def _timed(fn, label):
        @functools.wraps(fn)
        def inner(*a, **k):
            t0 = time.time()
            try:
                return fn(*a, **k)
            finally:
                _SPENT[label] += time.time() - t0
        return inner

    for _n in ('prepare', 'compute_snaps', 'resolve', 'absorb_enclaves', 'build_near', 'fuse_groups', 'wasteland_states', 'state_locations', 'parse_pops'):
        if _n in globals():
            globals()[_n] = _timed(globals()[_n], _n)
    G.apply = _timed(G.apply, 'gazetteer apply')
    _orig_build = build

    def build(m):
        t0 = time.time()
        try:
            return _orig_build(m)
        finally:
            print('BUILD TIMING total %.0fs:' % (time.time() - t0), ', '.join(f'{k} {v:.0f}s' for k, v in _SPENT.most_common()), file=sys.stderr, flush=True)
