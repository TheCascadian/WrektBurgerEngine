"""Installs the map made by refine_cells.py into a project: provinces.png and locations.txt, and the new locations into the states of the old ones.

Usage: python tools/geo/apply_cells.py <refine_cells output folder> <game folder>
The old files are kept as provinces.png.before-cells and locations.txt.before-cells (outside git history only if you delete them).
"""
import collections
import csv
import os
import re
import shutil
import sys


def main():
    src, game = sys.argv[1], sys.argv[2]
    mapdir = os.path.join(game, 'map')
    kids = collections.defaultdict(list)
    for r in csv.DictReader(open(os.path.join(src, 'cells.csv'), encoding='utf-8')):
        if r['key'] != r['old']:
            kids[r['old']].append(r['key'])
    states_path = os.path.join(mapdir, 'states', '00_states.txt')
    text = open(states_path, encoding='utf-8').read()
    n_added = 0

    def grow(m):
        nonlocal n_added
        keys = m.group(1).split()
        extra = []
        for k in keys:
            extra.extend(kids.get(k, []))
        n_added += len(extra)
        return 'locations = { ' + ' '.join(keys + extra) + ' }'

    text = re.sub(r'locations = \{([^}]*)\}', grow, text)
    for name in ('provinces.png', 'locations.txt'):
        dst = os.path.join(mapdir, name)
        if not os.path.exists(dst + '.before-cells'):
            shutil.copy2(dst, dst + '.before-cells')
        shutil.copy2(os.path.join(src, name), dst)
    if not os.path.exists(states_path + '.before-cells'):
        shutil.copy2(states_path, states_path + '.before-cells')
    with open(states_path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    shutil.copy2(os.path.join(src, 'cells.csv'), os.path.join(mapdir, 'cells.csv'))
    print('new locations put into states:', n_added)


if __name__ == '__main__':
    main()
