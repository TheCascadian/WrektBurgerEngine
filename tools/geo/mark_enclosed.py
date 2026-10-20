"""Makes every state that is completely surrounded by impassable ones impassable too (a pocket nobody can reach by land or sea has no country either).

usage: python tools/geo/mark_enclosed.py <locations.csv> [<game dir>]    (locations.csv: burger export-location-centres <project> <file>)
Run it after tools/geo/mark_wasteland.py and burger split-states. A state is enclosed when (nearly) all of its border touches impassable states (a state on a coast, a lake or a river mouth never is). SHARE (0.75): a state is also closed when at least that share of its border runs along impassable states (a pocket with one open corner). Oases that stay open are cut out of the wasteland polygons, but a state walled in by wasteland is closed whatever lies in it.
Repeats until nothing changes. Prints the states it closed.
"""
import sys, os, re, csv, math, collections, importlib.util
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
spec = importlib.util.spec_from_file_location('mw', os.path.join(HERE, 'mark_wasteland.py'))
src = open(os.path.join(HERE, 'mark_wasteland.py'), encoding='utf-8').read().replace('\nmain()\n', '\n')
mw = type(sys)('mw'); mw.__dict__['__file__'] = os.path.join(HERE, 'mark_wasteland.py'); exec(compile(src, 'mark_wasteland.py', 'exec'), mw.__dict__)


SHARE = 0.75      # a state whose border runs this far along impassable states is closed too (a sea, lake or river coast is never impassable)


def main():
    locs_path = sys.argv[1]
    game = sys.argv[2] if len(sys.argv) > 2 else os.path.join(REPO, 'projects', 'World500BCE', 'game')
    loc_text = open(os.path.join(game, 'map', 'locations.txt'), encoding='utf-8', newline='').read()
    colour_id = {}
    for m in re.finditer(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+)', loc_text, re.M):
        colour_id[int(m.group(1), 16)] = m.group(2)
    impass = set(re.findall(r'\{ id = (land_\d+) type = impassable \}', loc_text))
    state_of = {}
    states = {}
    text = open(os.path.join(game, 'map', 'states', '00_states.txt'), encoding='utf-8').read()
    for m in re.finditer(r'\n([a-z0-9_]+) = \{(.*?)\n\}', text, re.S):
        lm = re.search(r'locations = \{([^}]*)\}', m.group(2))
        if lm:
            states[m.group(1)] = lm.group(1).split()
            for l in states[m.group(1)]:
                state_of[l] = m.group(1)
    # which provinces touch which (pixel neighbours, full resolution, in bands of rows)
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
    nb = collections.defaultdict(collections.Counter)         # location id -> {id it touches: pixels of contact}
    for p, v in pairs.items():
        a, b = colour_id.get(p >> 32), colour_id.get(p & 0xFFFFFFFF)
        if a and b:
            nb[a][b] += v; nb[b][a] += v
    share = SHARE
    closed, rnd = [], 0
    while True:
        rnd += 1
        new = []
        for s, ls in states.items():
            if all(l in impass for l in ls):
                continue
            own = set(ls)
            wall = open_ = 0
            for l in ls:
                for n, v in nb.get(l, {}).items():
                    if n in own:
                        continue
                    if n in impass:
                        wall += v
                    else:
                        open_ += v
            if wall and wall / (wall + open_) >= share:
                new.append(s)
        if not new:
            break
        for s in new:
            impass.update(states[s]); closed.append(s)
    pat = re.compile(r'(\{ id = (land_\d+) type = )(\w+)( \})')
    n = 0
    def fix(m):
        nonlocal n
        if m.group(2) in impass and m.group(3) != 'impassable':
            n += 1
            return m.group(1) + 'impassable' + m.group(4)
        return m.group(0)
    loc_text = pat.sub(fix, loc_text)
    open(os.path.join(game, 'map', 'locations.txt'), 'w', encoding='utf-8', newline='').write(loc_text)
    print('%d enclosed states closed in %d rounds (%d locations)' % (len(closed), rnd - 1, n))
    for s in closed[:40]:
        print('  ', s)


main()
