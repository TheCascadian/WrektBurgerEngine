"""Prints a full coloured map of who owns what at a start date, pixel for pixel from provinces.png, to compare the borders with other atlases.

usage: python tools/map_owners.py --states <out.png> [options]: every state of the game in a colour of its own (no countries, wasteland grey)
       python tools/map_owners.py <year> <out.png> [--box w,s,e,n] [--scale 4] [--labels] [--project <game dir>]
  year:   the nearest snapshot is drawn (negative = BCE)
  --box:  only this part of the world (degrees); the map is equirectangular, 360 x 167 degrees over 16384 x 6848 pixels (rows 0..6847)
  --scale: pixels per output pixel (1 = full 16384 wide, 4 = 4096 wide, the default)
  --labels: write the name of every realm at the middle of its land
Needs numpy, Pillow (and matplotlib for --labels). Land borders between two owners are drawn dark, state borders are not.
"""
import re, sys, os, csv, colorsys, hashlib
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
sys.stdout.reconfigure(encoding='utf-8')

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))


def args():
    a = sys.argv[1:]
    opt = {'states': False, 'box': None, 'scale': 4, 'labels': False, 'project': os.path.join(REPO, 'projects', 'World500BCE', 'game')}
    pos = []
    i = 0
    while i < len(a):
        if a[i] == '--box': opt['box'] = [float(x) for x in a[i + 1].split(',')]; i += 2
        elif a[i] == '--scale': opt['scale'] = float(a[i + 1]); i += 2
        elif a[i] == '--project': opt['project'] = a[i + 1]; i += 2
        elif a[i] == '--states': opt['states'] = True; i += 1
        elif a[i] == '--labels': opt['labels'] = True; i += 1
        else: pos.append(a[i]); i += 1
    if opt['states']:
        return 0, pos[0], opt
    return int(pos[0]), pos[1], opt


def hashcol(t):
    h = int(hashlib.md5(t.encode()).hexdigest()[:8], 16)
    r, g, b = colorsys.hsv_to_rgb((h % 997) / 997, 0.45 + (h >> 8) % 40 / 100, 0.75 + (h >> 16) % 25 / 100)
    return int(r * 255), int(g * 255), int(b * 255)


def main():
    year, out, opt = args()
    P = opt['project']
    ctext = open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read()
    names, colors = {}, {}
    for m in re.finditer(r'^(\w+) = \{(.*?)^\}', ctext, re.S | re.M):
        n = re.search(r'name = "([^"]*)"', m.group(2)); c = re.search(r'color = \{\s*(\d+)\s+(\d+)\s+(\d+)', m.group(2))
        if n: names[m.group(1)] = n.group(1)
        if c: colors[m.group(1)] = tuple(int(x) for x in c.groups())
    # snapshot
    text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
    best = None
    for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
        y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
        if best is None or abs(y - year) < abs(best[0] - year): best = (y, m.group(2))
    owners = re.search(r'owners = \{(.*?)\}', best[1], re.S).group(1).split()
    mm = re.search(r'names = \{(.*?)\}', best[1], re.S)
    if mm: names.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
    # states in sorted key order -> locations
    stext = open(os.path.join(P, 'map/states/00_states.txt'), encoding='utf-8').read()
    st = {}
    for m in re.finditer(r'^(\w+) = \{(.*?)^\}', stext, re.S | re.M):
        lm = re.search(r'locations = \{(.*?)\}', m.group(2), re.S)
        if lm: st[m.group(1)] = lm.group(1).split()
    keys = sorted(st)
    if not opt['states'] and len(keys) != len(owners): sys.exit('states %d but owners %d: rebuild the history first' % (len(keys), len(owners)))
    loc_owner = {}
    for k, o in zip(keys, owners):
        for l in st[k]: loc_owner[l] = o
    if opt['states']:
        bad = set(re.findall(r'\{ id = (land_\d+) type = impassable \}', open(os.path.join(P, 'map/locations.txt'), encoding='utf-8').read()))
        loc_owner = {}
        for k in keys:
            for l in st[k]: loc_owner[l] = ('~' if l in bad else k)
        owners = list(loc_owner.values()) or owners
        colors = {k: hashcol(k) for k in keys}
        colors['~'] = (150, 150, 150)
        names = {k: k for k in keys}
    # colour of the province -> location id
    colour_loc = {}
    for m in re.finditer(r'^([0-9A-Fa-f]{6}) = \{ id = (\w+)', open(os.path.join(P, 'map/locations.txt'), encoding='utf-8').read(), re.M):
        colour_loc[m.group(1).upper()] = m.group(2)
    img = Image.open(os.path.join(P, 'map/provinces.png')).convert('RGB')
    W, H = img.size
    box = opt['box']
    x0, x1, y0, y1 = 0, W, 0, H
    if box:
        # the map covers lat +83.6 .. -83.6 (6848 rows of 16384 x 8192 equirectangular, 0.0439 deg/px)
        degpx = 360.0 / W
        top = 90.0 - 0
        x0 = max(0, int((box[0] + 180) / degpx)); x1 = min(W, int((box[2] + 180) / degpx))
        y0 = max(0, int((top - box[3]) / degpx)); y1 = min(H, int((top - box[1]) / degpx))
    img = img.crop((x0, y0, x1, y1))
    s = max(0.2, opt['scale'])
    if s != 1: img = img.resize((int(img.width / s), int(img.height / s)), Image.NEAREST)
    a = np.asarray(img)
    code = (a[:, :, 0].astype(np.uint32) << 16) | (a[:, :, 1].astype(np.uint32) << 8) | a[:, :, 2]
    uniq, inv = np.unique(code, return_inverse=True)
    tags = sorted(set(owners) - {'_'})
    tagidx = {t: i + 1 for i, t in enumerate(tags)}
    pal = np.zeros((len(tags) + 2, 3), np.uint8)
    pal[0] = (200, 220, 240)  # sea / unowned
    for t, i in tagidx.items(): pal[i] = colors.get(t) or hashcol(t)
    u2o = np.zeros(len(uniq), np.int32)
    for i, c in enumerate(uniq):
        loc = colour_loc.get('%06X' % c)
        if loc is None: continue
        o = loc_owner.get(loc, '_')
        u2o[i] = tagidx.get(o, len(tags) + 1)  # unowned land is grey
    pal[len(tags) + 1] = (225, 225, 215)
    own = u2o[inv].reshape(code.shape)
    rgb = pal[own]
    # borders where the owner changes between two land pixels
    b = np.zeros(own.shape, bool)
    b[:, 1:] |= (own[:, 1:] != own[:, :-1]) & (own[:, 1:] > 0) & (own[:, :-1] > 0)
    b[1:, :] |= (own[1:, :] != own[:-1, :]) & (own[1:, :] > 0) & (own[:-1, :] > 0)
    rgb[b] = (40, 40, 40)
    im = Image.fromarray(rgb)
    if opt['labels']:
        from PIL import ImageDraw
        d = ImageDraw.Draw(im)
        ys, xs = np.mgrid[0:own.shape[0], 0:own.shape[1]]
        for t, i in tagidx.items():
            m = own == i
            n = int(m.sum())
            if n < 400: continue
            d.text((int(xs[m].mean()), int(ys[m].mean())), names.get(t, t), fill=(0, 0, 0))
    im.save(out)
    print('%d (snapshot %d): %dx%d written to %s, %d realms' % (year, best[0], im.width, im.height, out, len(tags)))


main()
