#!/usr/bin/env python3
"""Builds a stress-test project from World500BCE_PTest: the same map, every state owned, N countries.

Usage: tools/make_maxi.py --countries N [--src projects/World500BCE_PTest] [--out projects/World500BCE_Maxi_N] [--seed 1]

Countries are Voronoi regions of the state centers (pixel centroids of the state's locations on provinces.png), so each is
a contiguous-ish blob. Every country gets a culture and religion (the commonest of its people), a capital, a politics start
(deliberately restless, as in PTest), an army scaled to its size and a fleet when it holds a port. Big files are hard-linked.
"""
import argparse, os, re, random, shutil, sys
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None


def link_tree(src, dst, skip=()):
    for root, dirs, files in os.walk(src):
        rel = os.path.relpath(root, src)
        dirs[:] = [d for d in dirs if os.path.join(rel, d) not in skip and d not in ("cache", "bin", "obj")]
        os.makedirs(os.path.join(dst, rel), exist_ok=True)
        for f in files:
            s, d = os.path.join(root, f), os.path.join(dst, rel, f)
            if os.path.exists(d): os.unlink(d)
            try: os.link(s, d)
            except OSError: shutil.copy2(s, d)


def split_blocks(text):
    """Top-level 'key = { ... }' blocks of a states file: list of (key, start, end)."""
    out, i, n = [], 0, len(text)
    pat = re.compile(r'^([A-Za-z0-9_]+) = \{', re.M)
    while True:
        m = pat.search(text, i)
        if not m: break
        depth, j = 0, m.end() - 1
        while j < n:
            c = text[j]
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: break
            j += 1
        out.append((m.group(1), m.start(), j + 1))
        i = j + 1
    return out


def location_centroids(game):
    ids, keys = [], []
    for line in open(os.path.join(game, 'map/locations.txt')):
        m = re.match(r'\s*([0-9A-Fa-f]{6}) = \{ id = (\w+)', line)
        if m: keys.append(int(m.group(1), 16)); ids.append(m.group(2))
    keys = np.array(keys, dtype=np.int64)
    order = np.argsort(keys)
    sk = keys[order]
    sumx = np.zeros(len(keys)); sumy = np.zeros(len(keys)); cnt = np.zeros(len(keys))
    img = Image.open(os.path.join(game, 'map/provinces.png')).convert('RGB')
    W, H = img.size
    arr = np.asarray(img)
    xs = np.arange(W, dtype=np.float64)
    for y0 in range(0, H, 256):
        blk = arr[y0:y0 + 256].astype(np.int64)
        k = (blk[..., 0] << 16) | (blk[..., 1] << 8) | blk[..., 2]
        pos = np.searchsorted(sk, k)
        pos[pos >= len(sk)] = 0
        hit = sk[pos] == k
        li = order[pos]
        rows = np.broadcast_to(np.arange(y0, y0 + blk.shape[0])[:, None], k.shape)
        cols = np.broadcast_to(xs[None, :], k.shape)
        li, rows, cols = li[hit], rows[hit], cols[hit]
        cnt += np.bincount(li, minlength=len(keys))
        sumx += np.bincount(li, weights=cols, minlength=len(keys))
        sumy += np.bincount(li, weights=rows, minlength=len(keys))
    cnt[cnt == 0] = 1
    return {ids[i]: (sumx[i] / cnt[i], sumy[i] / cnt[i]) for i in range(len(ids))}, W, H


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--countries', type=int, required=True)
    ap.add_argument('--src', default='projects/World500BCE_PTest')
    ap.add_argument('--out')
    ap.add_argument('--seed', type=int, default=1)
    a = ap.parse_args()
    out = a.out or f'projects/World500BCE_Maxi_{a.countries}'
    rng = random.Random(a.seed)
    sg, dg = os.path.join(a.src, 'game'), os.path.join(out, 'game')
    os.makedirs(out, exist_ok=True)
    link_tree(a.src, out)

    # ---- states
    path = os.path.join(sg, 'map/states/00_states.txt')
    text = open(path).read()
    blocks = split_blocks(text)
    cents, W, H = location_centroids(sg)
    states = []
    for key, s, e in blocks:
        body = text[s:e]
        locs = re.search(r'locations = \{([^}]*)\}', body).group(1).split()
        pts = np.array([cents[l] for l in locs if l in cents])
        states.append(dict(key=key, start=s, end=e, locs=locs, xy=pts.mean(axis=0), body=body,
                           port='naval_base' in body))
    N = a.countries
    if N > len(states): sys.exit(f'only {len(states)} states')
    # farthest-point seeds (wrapping in x), then Voronoi
    xy = np.array([s['xy'] for s in states])
    def dist(p, q):
        dx = np.abs(q[:, 0] - p[0]); dx = np.minimum(dx, W - dx)
        return np.hypot(dx, q[:, 1] - p[1])
    first = rng.randrange(len(states))
    seeds = [first]
    d = dist(xy[first], xy)
    while len(seeds) < N:
        nxt = int(np.argmax(d * (0.6 + 0.8 * np.array([rng.random() for _ in range(len(states))]))))
        seeds.append(nxt); d = np.minimum(d, dist(xy[nxt], xy))
    dm = np.stack([dist(xy[s], xy) for s in seeds])
    owner = dm.argmin(axis=0)

    # ---- pops of each state
    pops = {}
    ptext = open(os.path.join(sg, 'history/pops/00_pops.txt')).read()
    for key, s, e in split_blocks(ptext):
        tot = {}
        for m in re.finditer(r'pop = \{ culture = (\w+)(?: religion = (\w+))?(?: type = (\w+))? size = ([\d.]+)', ptext[s:e]):
            tot[(m.group(1), m.group(2))] = tot.get((m.group(1), m.group(2)), 0) + float(m.group(4))
        pops[key] = tot

    def tag(i):
        letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
        return letters[i // 676 % 26] + letters[i // 26 % 26] + letters[i % 26]
    # tags start at 'AAA'; keep 'AAA' unused by shifting
    tags = [tag(i + 1) for i in range(N)]

    members = [[] for _ in range(N)]
    for si, ci in enumerate(owner): members[ci].append(si)
    cs, ruling_mix = [], [('neutrality', 'fascist', 'democratic', 'communist'), ('democratic', 'neutrality', 'fascist', 'communist'),
                          ('fascist', 'neutrality', 'communist', 'democratic'), ('communist', 'neutrality', 'democratic', 'fascist')]

    # rewritten states file
    new_text, last = [], 0
    for si, st in enumerate(states):
        body = st['body']
        body = re.sub(r'\n\s*owner = \w+', '', body)
        body = re.sub(r'\n\s*cores = \{[^}]*\}', '', body)
        t = tags[owner[si]]
        body = body.replace('{', '{\n\towner = ' + t + '\n\tcores = { ' + t + ' }', 1)
        new_text.append(text[last:st['start']]); new_text.append(body); last = st['end']
    new_text.append(text[last:])
    f = os.path.join(dg, 'map/states/00_states.txt')
    os.unlink(f); open(f, 'w').write(''.join(new_text))

    # ---- countries, politics, armies, fleets
    countries, politics, armies, fleets = [], [], [], []
    for ci in range(N):
        ms = members[ci]
        if not ms: continue
        t = tags[ci]
        cx = np.mean([states[i]['xy'] for i in ms], axis=0)
        cap_state = min(ms, key=lambda i: np.hypot(*(states[i]['xy'] - cx)))
        cap = states[cap_state]['locs'][0]
        agg = {}
        for i in ms:
            for k, v in pops.get(states[i]['key'], {}).items(): agg[k] = agg.get(k, 0) + v
        (cult, rel) = max(agg, key=agg.get) if agg else ('persian', None)
        color = [rng.randrange(40, 230) for _ in range(3)]
        countries.append(f'{t} = {{\n\tname = "Realm {t}"\n\tcolor = {{ {color[0]} {color[1]} {color[2]} }}\n\tcapital = {cap}\n\tculture = {cult}\n'
                         + (f'\treligion = {rel}\n' if rel else '') + f'\tgold = {100 * len(ms)}\n\tmanpower = {2000 * len(ms)}\n}}\n')
        mix = ruling_mix[ci % 4]
        w = [45, 28, 17, 10]; rng.shuffle(w)
        pop = ' '.join(f'{m} = {v}' for m, v in zip(mix, [max(5, v + rng.randrange(-4, 5)) for v in sorted(w, reverse=True)]))
        laws = 'restricted_parties' if mix[0] in ('neutrality', 'fascist') else 'multi_party'
        politics.append(f'{t} = {{\n\truling_party = {mix[0]}\n\tpopularity = {{ {pop} }}\n\tlaws = {{ political_parties = {laws} }}\n}}\n')
        div = max(2, len(ms) * 2)
        armies.append(f'{t} = {{\n\tarmy = {{\n\t\tname = "1st Army"\n\t\tdivision = {{ template = infantry_division location = {cap} count = {div} }}\n\t}}\n}}\n')
        ports = [i for i in ms if states[i]['port']]
        if ports:
            fleets.append(f'{t} = {{\n\tfleet = {{\n\t\tname = "Home Fleet"\n\t\tlocation = {states[ports[0]]["locs"][0]}\n\t\tships = {{ destroyer = {1 + len(ms) // 4} transport = 2 }}\n\t}}\n}}\n')
    def put(rel, content):
        f = os.path.join(dg, rel)
        if os.path.exists(f): os.unlink(f)
        os.makedirs(os.path.dirname(f), exist_ok=True)
        open(f, 'w').write(content)
    put('common/countries/countries.txt', f'# generated by tools/make_maxi.py: {N} countries over every state of the 500 BCE map\n' + ''.join(countries))
    put('history/politics/countries.txt', '# generated: restless starts, as in PTest\n' + ''.join(politics))
    put('history/units/00_starting_armies.txt', ''.join(armies))
    put('history/fleets/00_starting_fleets.txt', ''.join(fleets))
    put('history/diplomacy.txt', '# none: the AI makes its own\n')
    p = os.path.join(dg, 'common/ai_profiles/10_countries.txt')
    if os.path.exists(p): os.unlink(p)
    g = open(os.path.join(sg, 'game.txt')).read()
    g = re.sub(r'name = "[^"]*"', f'name = "World500BCE_Maxi_{N}"', g, count=1)
    g = re.sub(r'default_country = \w+', f'default_country = {tags[0]}', g)
    put('game.txt', g)
    # scorecard: only determinism lines are relevant, copy as is
    print(f'{out}: {len(countries)} countries, {len(states)} states, ports {sum(1 for s in states if s["port"])}')


main()
