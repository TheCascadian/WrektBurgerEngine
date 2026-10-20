"""Plans the cutting of the states of a project along the borders of every start date.

usage: python tools/refine_states.py <locations.csv> <plan.txt> [min_group] [tolerance] [model]
  model: the owners are those of the whole model of tools/import-history.py (with the gazetteer) rather than the polygons of the dataset
  locations.csv: burger export-location-centres <project> <file>
  plan.txt:      state key <TAB> group of location keys <TAB> group ...   (only states that fall in more than one group)

Two lands of one state stay together when the same polity of the dataset (historical-basemaps) holds both at every date of the data; a state that a border of any
date cuts through is cut along it. Then: burger split-states <project> --plan plan.txt (the pieces of a group that do not touch each other become states of their own).
"""
import sys, os, csv, collections, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
spec = importlib.util.spec_from_file_location('imp', os.path.join(HERE, 'import-history.py'))
imp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(imp)
import history_tags as T
import history_build as B


def main():
    locs_path, plan_path = sys.argv[1], sys.argv[2]
    min_group = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    tolerance = int(sys.argv[4]) if len(sys.argv) > 4 else 3
    rows = list(csv.DictReader(open(locs_path, encoding='utf-8')))
    sig = {r['location']: [] for r in rows}
    cache = {}

    def key_of(name):
        if name is None:
            return None
        if name not in cache:
            norm = T.normalize(name)
            cache[name] = None if (T.IGNORE.search(norm) or T.IGNORE.search(name.lower())) else B.family_key(name)[0]
        return cache[name]

    if len(sys.argv) > 5 and sys.argv[5] == 'model':
        # the owners of the whole model (dataset, masks, seeds and the gazetteer), asked at every location
        pts = [(r['state'], float(r['lon']), float(r['lat']), r['location']) for r in rows]
        orig, oname, _ = B.prepare(imp)
        for y, ay, res in B.compute_snaps(imp, pts, orig, oname):
            for r, o in zip(rows, res):
                sig[r['location']].append(o[0].split('#')[0] if o else None)
    else:
        for y in ['bc500'] + list(imp.YEARS):
            feats = imp.load_snapshot(y)
            for r in rows:
                sig[r['location']].append(key_of(imp.owner_of(feats, float(r['lon']), float(r['lat']))))
            print(y, 'done', file=sys.stderr)
    by_state = collections.defaultdict(lambda: collections.defaultdict(list))
    for r in rows:
        by_state[r['state']][tuple(sig[r['location']])].append(r['location'])
    out = []
    cut = pieces = 0
    for state, groups in by_state.items():
        if len(groups) < 2:
            continue
        # groups whose polities differ at only a few dates are one (the borders of the dataset move a little between its maps): the smallest joins the nearest
        gs = [(s, list(g)) for s, g in groups.items()]
        merged = True
        while merged and len(gs) > 1:
            merged = False
            gs.sort(key=lambda t: (len(t[1]), t[1][0]))
            for i, small in enumerate(gs):
                others = gs[:i] + gs[i + 1:]
                dist = lambda t: sum(1 for a, c in zip(small[0], t[0]) if a != c)
                near = min(others, key=lambda t: (dist(t), -len(t[1])))
                if len(small[1]) < min_group or dist(near) <= tolerance:
                    near[1].extend(small[1])
                    gs.pop(i)
                    merged = True
                    break
        gl = [g for _, g in gs]
        if len(gl) < 2:
            continue
        cut += 1
        pieces += len(gl)
        out.append(state + '\t' + '\t'.join(' '.join(g) for g in gl))
    open(plan_path, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
    print(f'{len(by_state)} states, {cut} cut into {pieces} groups (+{pieces - cut} states)')


if __name__ == '__main__':
    main()
