"""Which provinces touch which (pixel contacts of map/provinces.png), cached: tools/geo/location_adjacency.csv (a,b,pixels) with the size and modified time of the map in its first line.

usage: python tools/geo/adjacency.py [game dir]      (or import: adjacency.load(game_dir) -> {location id: {location id: pixels}})
"""
import os, re, sys, collections
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
CACHE = os.path.join(HERE, 'location_adjacency.csv')


def stamp(game):
    st = os.stat(os.path.join(game, 'map', 'provinces.png'))
    return '# %d %d' % (st.st_size, int(st.st_mtime))


def compute(game):
    colour_id = {}
    for m in re.finditer(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+)', open(os.path.join(game, 'map', 'locations.txt'), encoding='utf-8').read(), re.M):
        colour_id[int(m.group(1), 16)] = m.group(2)
    img = np.asarray(Image.open(os.path.join(game, 'map', 'provinces.png')).convert('RGB'))
    code = (img[:, :, 0].astype(np.uint32) << 16) | (img[:, :, 1].astype(np.uint32) << 8) | img[:, :, 2]
    del img
    pairs = collections.Counter()
    H = code.shape[0]
    for y0 in range(0, H, 512):
        blk = code[y0:min(H, y0 + 513)]
        for a, b in ((blk[:, 1:], blk[:, :-1]), (blk[1:], blk[:-1])):
            m = a != b
            lo = np.minimum(a[m], b[m]).astype(np.uint64); hi = np.maximum(a[m], b[m]).astype(np.uint64)
            u, c = np.unique((lo << np.uint64(32)) | hi, return_counts=True)
            for k, v in zip(u.tolist(), c.tolist()):
                pairs[k] += v
    nb = collections.defaultdict(collections.Counter)
    for p, v in pairs.items():
        a, b = colour_id.get(p >> 32), colour_id.get(p & 0xFFFFFFFF)
        if a and b:
            nb[a][b] += v; nb[b][a] += v
    return nb


def load(game=None):
    game = game or os.path.join(REPO, 'projects', 'World500BCE', 'game')
    s = stamp(game)
    if os.path.exists(CACHE):
        with open(CACHE, encoding='utf-8') as f:
            if f.readline().strip() == s:
                nb = collections.defaultdict(collections.Counter)
                for line in f:
                    a, b, v = line.strip().split(',')
                    nb[a][b] = int(v); nb[b][a] = int(v)
                return nb
    nb = compute(game)
    with open(CACHE, 'w', encoding='utf-8') as f:
        f.write(s + '\n')
        for a in sorted(nb):
            for b, v in nb[a].items():
                if a < b:
                    f.write('%s,%s,%d\n' % (a, b, v))
    return nb


if __name__ == '__main__':
    n = load(sys.argv[1] if len(sys.argv) > 1 else None)
    print(len(n), 'provinces with neighbours')
