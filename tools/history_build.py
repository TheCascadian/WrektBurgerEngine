"""The build step of tools/import-history.py."""
import os, re, sys, math, hashlib, subprocess, colorsys, collections, unicodedata
import history_tags as T

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
PROJECT = os.path.join(REPO, 'projects', 'World500BCE', 'game')


def git_original(rel):
    return subprocess.check_output(['git', 'show', 'HEAD:projects/World500BCE/game/' + rel], cwd=REPO).decode('utf-8').replace('\r\n', '\n')


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


def resolve(m, states, raw, year):
    import history_names as N
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
            merge_small = re.match(r'^(hallstatt|celts$|germanic tribes$|scythians)', norm) is not None or any(rx_ for a0, b0, rx_, sd in __import__('history_tribes').YEAR_SEEDS if sd is seeds)
            def nearest(i, pool):
                lon, lat = states[i][1], states[i][2]
                return min(pool, key=lambda sd: ((lon - sd[2]) * math.cos(math.radians(lat))) ** 2 + (lat - sd[3]) ** 2)
            while merge_small and len(live) > 1:
                cnt = collections.Counter(nearest(i, live)[0] for i in idx)
                small = [sd for sd in live if cnt[sd[0]] < 3]
                if not small:
                    break
                live.remove(min(small, key=lambda sd: cnt[sd[0]]))
            for i in idx:
                k, disp, _, _ = nearest(i, live)
                result[i] = (k, N.tidy(disp))
            continue
        if T.TRIBAL.search(norm) or N.COLLECTIVE.search(norm) and not T.NOT_UMBRELLA.search(norm):
            result_tribes(result, states, idx, name, norm, year)
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


def build(m):
    states = m.load_states()
    keys = [s[0] for s in states]
    order = hashlib.sha1('\n'.join(keys).encode()).hexdigest()
    pops = parse_pops(open(os.path.join(PROJECT, 'history', 'pops', '00_pops.txt'), encoding='utf-8').read().replace('\r\n', '\n'))
    original_countries = git_original('common/countries/countries.txt')
    for o, n in (('Libyan and Berber Tribes', 'Libu and Mazyes'), ('Bantu Homeland Peoples', 'Mongo'), ('Thracian Tribes', 'Thracians')):
        original_countries = original_countries.replace('"' + o + '"', '"' + n + '"')
    used_codes = set(re.findall(r'^([A-Z0-9]{3}) = \{', original_countries, re.M))

    snaps = []
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
    feats0 = m.load_snapshot('bc500')
    import history_names as N
    active = {o for o in orig.values() if o}
    N.ALL_USED.update(oname[t] for t in active if t in oname)
    oname_rev = {}
    for t, nme in oname.items():
        if t not in active:
            oname_rev.setdefault(nme, t)
    for fn in ('history_tags.py', 'history_tribes.py'):
        N.ALL_USED.update(N.tidy(x) for x in re.findall(r"'([A-Z][^'0-9]{2,40})'", open(os.path.join(REPO, 'tools', fn), encoding='utf-8').read()))
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
        else:
            first.append(('orig:' + o, oname[o]))
    for i, st in enumerate(states):
        if first[i] is None:
            for (w, so, e, no), seeds in TR.MASK_SEEDS:
                if w <= st[1] <= e and so <= st[2] <= no:
                    k, disp, _, _ = min(seeds, key=lambda sd: ((st[1] - sd[2]) * math.cos(math.radians(st[2]))) ** 2 + (st[2] - sd[3]) ** 2)
                    first[i] = (k, disp)
                    break
    snaps.append(('bc500', -499, first))
    for y in m.YEARS:
        feats = m.load_snapshot(y)
        raw = [m.owner_of(feats, s[1], s[2]) for s in states]
        res = resolve(m, states, raw, y)
        if m.astronomical(y) <= 200:
            # the lands the dataset leaves empty keep the peoples of the first start date (a people with fewer than three states of them is left out)
            groups = collections.defaultdict(list)
            for (w, so, e, no), seeds in TR.MASK_SEEDS + TR.MASK_LATER:
                idx = [i for i, st in enumerate(states) if res[i] is None and not raw[i] and w <= st[1] <= e and so <= st[2] <= no]
                live = list(seeds)
                def nearest(i):
                    return min(live, key=lambda sd: ((states[i][1] - sd[2]) * math.cos(math.radians(states[i][2]))) ** 2 + (states[i][2] - sd[3]) ** 2)
                while len(live) > 1 and idx:
                    cnt = collections.Counter(nearest(i)[0] for i in idx)
                    small = [sd for sd in live if cnt[sd[0]] < 3]
                    if not small:
                        break
                    live.remove(min(small, key=lambda sd: cnt[sd[0]]))
                for i in idx:
                    sd = nearest(i)
                    res[i] = (sd[0], N.tidy(sd[1]))
                    groups[sd[0]].append(i)
            for k, members in groups.items():
                if len(members) > 14:
                    parts = cluster([(i, states[i][1], states[i][2]) for i in members], round(len(members) / TR.TRIBE_SIZE))
                    for j, (seed, part) in enumerate(parts):
                        if j == 0:
                            continue
                        ikey = f'{k}#{j}'
                        nm2 = N.cluster_name('steppe tribes' if states[members[0]][1] > 28 and states[members[0]][2] > 38 else 'celts', ikey, N.year_of(y))
                        for it in part:
                            res[it[0]] = (ikey, nm2)
        if -130 <= m.astronomical(y) <= -70:
            # Greece and Macedon are Roman provinces
            for i, o in enumerate(res):
                if o and o[0] in ('ath', 'thb', 'spa', 'cor', 'arg', 'ths', 'macedon', 'epirus', 'aetolia', 'achaea', 'elis', 'phocis') and 19 <= states[i][1] <= 28:
                    res[i] = ('rom', 'Roman Republic')
        res = [(o[0], N.YEAR_NAMES.get((y, o[1]), o[1])) if o else None for o in res]
        snaps.append((y, m.astronomical(y), res))
        print(y, 'done', file=sys.stderr)

    # polities that never hold more than a couple of states (small tribes, single towns) are not countries: their land has no owner
    peak = collections.Counter()
    for y, ay, owners in snaps:
        c = collections.Counter(o[0] for o in owners if o)
        for k, n in c.items():
            peak[k] = max(peak[k], n)
    MIN_STATES = 3
    first_keys = {o[0] for o in snaps[0][2] if o}
    snaps = [(y, ay, owners if y == 'bc500' else [o if (o and (o[0] in T.EXISTING or o[0] in first_keys or peak[o[0]] >= MIN_STATES)) else None for o in owners]) for y, ay, owners in snaps]

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
    print('written', file=sys.stderr)
