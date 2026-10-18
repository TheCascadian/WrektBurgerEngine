"""Applies a cut_by_countries.py result to a project's map: new provinces.png and locations.txt, and every old land id remapped.

Usage: python3 apply_cut_to_project.py <original locations .npz> <cut .npz> <project game folder>
Refuses the real World500BCE folder; use a clone. The project's own provinces.png/locations.txt fix the names (the project grid is
the top rows of the world grid, with the Antarctic rows and locations dropped). terrain, heightmap and rivers are untouched (the pixel grid and the water
locations do not change).
Remapping: states list every atom of an old location; any other single reference (sites, capitals, units, fleets) takes the
largest atom of that old location. Atoms inherit the owner of their parent location, so no ownership is invented.
Land atoms are numbered land_00001.. in atom order; sea, lake and ice names keep their old names. An old location under N pixels
that a neighbor atom absorbed whole is left out of state lists; single references to it take the absorbing atom.
"""
import os
import re
import sys

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
orig, cut, game = np.load(sys.argv[1]), np.load(sys.argv[2]), os.path.abspath(sys.argv[3])
if os.path.basename(os.path.dirname(game)) == "World500BCE" or "/World500BCE/" in game + "/":
    sys.exit("refusing: this is the original project; apply the cut to a clone")

ids, kind, parent = cut["ids"], cut["kind"], cut["parent"]
okind, oids = orig["kind"], orig["ids"]
prefix = {1: "land", 2: "sea", 3: "lake", 4: "ice"}

# The project's own locations (its provinces.png and locations.txt) are the reference for names: the project dropped the
# Antarctic locations and renumbered, so the original npz ids and the project names are tied together by pixel overlap.
hexkey = {}
for m in re.finditer(r"^([0-9A-Fa-f]{6})\s*=\s*\{\s*id\s*=\s*(\w+)", open(os.path.join(game, "map", "locations.txt")).read(), re.M):
    hexkey[int(m.group(1), 16)] = m.group(2)
img = np.asarray(Image.open(os.path.join(game, "map", "provinces.png")).convert("RGB"), np.uint32)
rgb = (img[..., 0] << 16) | (img[..., 1] << 8) | img[..., 2]
del img
keys = np.array(sorted(hexkey), np.uint32)
pos = np.searchsorted(keys, rgb)
pos[pos >= len(keys)] = 0
P = np.where(keys[pos] == rgb, pos + 1, 0).astype(np.int64)      # 1-based index into keys, 0 = no location
del rgb, pos
names = [""] + [hexkey[int(k)] for k in keys]
rows = P.shape[0]      # the project grid is the top `rows` rows of the world grid (the Antarctic rows were cut off)
assert not (kind[ids[rows:]] == 1).any(), "land below the project grid"
oids, ids = oids[:rows], ids[:rows]
pair = np.unique(oids.astype(np.int64) * 65536 + P)
o2p = {}
for v in pair.tolist():
    o, pi = v >> 16, v & 0xFFFF
    if o > 0 and pi > 0:
        assert o not in o2p, f"original id {o} overlaps two project locations"
        o2p[o] = names[pi]
del P, pair
# keep only atoms whose original location the project has (Antarctica is gone); compact the numbering
n_all = len(kind) - 1
keep_new = [i for i in range(1, n_all + 1) if int(parent[i]) in o2p]
remap = np.zeros(n_all + 1, np.int64)
remap[keep_new] = np.arange(1, len(keep_new) + 1)
ids = remap[ids]
kind = np.concatenate([[0], kind[keep_new]]).astype(np.uint8)
parent = np.concatenate([[0], parent[keep_new]])
n = len(kind) - 1
assert n <= 65000
size = np.bincount(ids.ravel(), minlength=n + 1)
new_name, c = {}, {}
for i in range(1, n + 1):
    k = int(kind[i])
    if k == 1:
        c[k] = c.get(k, 0) + 1
        new_name[i] = f"land_{c[k]:05d}"
    else:
        new_name[i] = o2p[int(parent[i])]
atoms_of = {}
for i in range(1, n + 1):
    if kind[i] == 1:
        atoms_of.setdefault(o2p[int(parent[i])], []).append(i)


# an old location under N pixels can be absorbed whole by a neighbor atom; it then has no atom of its own
absorbed = sorted(set(o2p.values()) - set(atoms_of) - {v for v in o2p.values() if not v.startswith("land_")})
print(len(absorbed), "old land locations were absorbed whole into a neighbor atom (dropped from state lists):", absorbed[:5])
_cover = {}


def covering_atom(name):
    """new id of the atom holding most of an absorbed old location's pixels."""
    if not _cover:
        p2o = {v: k for k, v in o2p.items()}
        pairs, cnt = np.unique(oids.astype(np.int64) * (1 << 20) + ids, return_counts=True)
        for k, c in zip(pairs.tolist(), cnt.tolist()):
            o, a = k >> 20, k & 0xFFFFF
            if o > 0 and a > 0 and o in o2p:
                cur = _cover.get(o2p[o])
                if cur is None or c > cur[0]:
                    _cover[o2p[o]] = (c, a)
    return _cover[name][1]


def land_atoms(name):
    """new names of every atom of an old location name; non-land names map to themselves."""
    return [new_name[a] for a in atoms_of.get(name, [])] if name.startswith("land_") else [name]


def main_atom(name):
    if not name.startswith("land_"):
        return name
    if name not in atoms_of:
        return new_name[covering_atom(name)]
    return new_name[max(atoms_of[name], key=lambda a: (size[a], -a))]


TOKEN = re.compile(r"\b(?:land|sea|lake|ice)_\d{5}\b")
LIST = re.compile(r"(locations\s*=\s*\{)([^}]*)(\})")


def remap_file(path, expand):
    text = open(path, encoding="utf-8").read()
    if expand:
        text = LIST.sub(lambda m: m.group(1) + " " + " ".join(a for t in TOKEN.findall(m.group(2)) for a in land_atoms(t)) + " " + m.group(3), text)
    else:
        text = TOKEN.sub(lambda m: main_atom(m.group(0)), text)
    open(path, "w", encoding="utf-8").write(text)


# provinces.png and locations.txt
col = ((np.arange(n + 1, dtype=np.uint64) * np.uint64(0x9E3779B1)) & np.uint64(0xFFFFFF)).astype(np.uint32)
col[0] = 0
assert len(np.unique(col[1:])) == n and (col[1:] != 0).all()
lut = np.stack([(col >> 16) & 255, (col >> 8) & 255, col & 255], axis=1).astype(np.uint8)
Image.fromarray(lut[ids]).save(os.path.join(game, "map", "provinces.png"), compress_level=1)
tname = {1: "land", 2: "sea", 3: "lake", 4: "impassable"}
ids = ids.astype(np.uint16 if n < 65535 else np.uint32)
lines = ["# Placeholder keys. Land atoms are the original locations cut along 2020 admin-1 borders (tools/map500bce/cut_by_countries.py).", ""]
for i in range(1, n + 1):
    lines.append(f"{col[i]:06X} = {{ id = {new_name[i]} type = {tname[int(kind[i])]} }}")
open(os.path.join(game, "map", "locations.txt"), "w").write("\n".join(lines) + "\n")
print("wrote provinces.png, locations.txt:", n, "locations")

# other files that name locations
for rel, expand in (("map/states/00_states.txt", True), ("common/sites/01_sites.txt", False), ("common/countries/countries.txt", False),
                    ("history/units/00_starting_armies.txt", False), ("history/fleets/00_starting_fleets.txt", False)):
    p = os.path.join(game, rel)
    if os.path.exists(p):
        remap_file(p, expand)
        print("remapped", rel)
