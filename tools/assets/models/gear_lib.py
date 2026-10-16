#!/usr/bin/env python3
"""Shared toolkit for the gear generators: the engine's exact 28-joint body rig, flat-shaded low-poly primitives with per-vertex skin weights,
and a .glb writer with a self-check. Pieces are authored directly in the engine body's rest space (1.80 m person, +Y up, facing +Z,
.L at +X, .R at -X), so no refitting step exists. Needs numpy and pygltflib.

A piece script builds a Mesh, then calls write_piece(mesh, name, max_triangles)."""
from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np
from pygltflib import (Accessor, Asset, Attributes, Buffer, BufferView, GLTF2, Material, Mesh as GltfMesh, Node,
                       PbrMetallicRoughness, Primitive, Scene, Skin)

# ---- the engine rig (src/Burger.Core/Figures/HumanMale.cs BuildRig): name, parent, world position ----
_C = [("root", None, (0, 0, 0)), ("hips", "root", (0, .95, 0)), ("spine", "hips", (0, 1.08, 0)), ("chest", "spine", (0, 1.24, 0)),
      ("neck", "chest", (0, 1.495, .004)), ("head", "neck", (0, 1.575, .012))]
_SIDE = [("shoulder", "chest", (.035, 1.455, 0)), ("upper_arm", "shoulder", (.185, 1.435, -.005)), ("forearm", "upper_arm", (.408, 1.234, -.005)),
         ("hand", "forearm", (.597, 1.064, -.005)), ("fingers", "hand", (.672, .997, -.005)), ("thumb", "hand", (.617, 1.041, .031)),
         ("thigh", "hips", (.095, .93, 0)), ("shin", "thigh", (.092, .49, 0)), ("foot", "shin", (.088, .075, 0)), ("toe", "foot", (.088, .015, .13))]
RIG: list[tuple[str, str | None, np.ndarray]] = [(n, p, np.array(w, float)) for n, p, w in _C]
for _s, _x in ((".L", 1), (".R", -1)):
    for _n, _p, _w in _SIDE:
        RIG.append((_n + _s, _p if _p in ("chest", "hips") else _p + _s, np.array((_w[0] * _x, _w[1], _w[2]), float)))
RIG.append(("rifle", "chest", np.array((0, 1.24, 0), float)))        # carries the rifle piece
RIG.append(("headgear", "head", np.array((0, 1.575, .012), float)))  # carries the helmet or cap
INDEX = {n: i for i, (n, _, _) in enumerate(RIG)}
JOINT = {n: w for n, _, w in RIG}


def unit(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def blend(*pairs):
    """Skin weights from (joint, weight) pairs, normalized, strongest four kept."""
    pairs = sorted(((n, float(w)) for n, w in pairs if w > 1e-6), key=lambda p: -p[1])[:4]
    t = sum(w for _, w in pairs)
    return [(n, w / t) for n, w in pairs]


class Mesh:
    """Flat-shaded triangle soup (vertices are never shared). Weights are a list of (joint, weight) or a function of the point."""

    def __init__(self):
        self.pos, self.nrm, self.col, self.jnt, self.wgt = [], [], [], [], []

    @property
    def triangles(self):
        return len(self.pos) // 3

    def tri(self, a, b, c, outward, color, wa, wb=None, wc=None):
        """One triangle wound counter-clockwise as seen from the side `outward` points to. wa/wb/wc: weights per corner (wa alone = all)."""
        a, b, c = (np.asarray(p, float) for p in (a, b, c))
        n = np.cross(b - a, c - a)
        if np.linalg.norm(n) < 1e-10:
            return
        if np.dot(n, outward) < 0:
            b, c = c, b
            wb, wc = wc, wb
            n = -n
        n = unit(n)
        ws = [wa, wb if wb is not None else wa, wc if wc is not None else wa]
        for p, w in zip((a, b, c), ws):
            w = w(p) if callable(w) else w
            idx = [INDEX[j] for j, _ in w] + [0] * (4 - len(w))
            wt = [x for _, x in w] + [0.0] * (4 - len(w))
            self.pos.append(p); self.nrm.append(n); self.col.append(color); self.jnt.append(idx); self.wgt.append(wt)

    def quad(self, a, b, c, d, outward, color, w):
        self.tri(a, b, c, outward, color, w)
        self.tri(a, c, d, outward, color, w)

    def loft(self, rings, cap_first=False, cap_last=False):
        """Closed tube through rings. Each ring: dict(pts=Nx3 (same N in every ring), color=RGBA of the band above it, w=weights or function of the point).
        The outside is away from the line through the ring centers. Optional fan caps close the ends."""
        cen = [np.mean(r["pts"], axis=0) for r in rings]
        for i in range(len(rings) - 1):
            r0, r1 = rings[i], rings[i + 1]
            n = len(r0["pts"])
            mid = (cen[i] + cen[i + 1]) / 2
            for j in range(n):
                k = (j + 1) % n
                a, b, c, d = r0["pts"][j], r0["pts"][k], r1["pts"][k], r1["pts"][j]
                out = (a + b + c + d) / 4 - mid
                self.tri(a, b, c, out, r0["color"], r0["w"], r0["w"], r1["w"])
                self.tri(a, c, d, out, r0["color"], r0["w"], r1["w"], r1["w"])
        for ring, c, sign, nxt in ((rings[0], cen[0], -1, cen[1]), (rings[-1], cen[-1], 1, cen[-2])):
            if (sign < 0 and not cap_first) or (sign > 0 and not cap_last):
                continue
            col = rings[0]["color"] if sign < 0 else rings[-2]["color"]
            n = len(ring["pts"])
            for j in range(n):
                self.tri(c, ring["pts"][j], ring["pts"][(j + 1) % n], c - nxt, col, ring["w"])

    def grid(self, f, nu, nv, outward, color, w):
        """Surface patch f(u, v) with u, v in 0..1 over an nu x nv grid; color may be a function of the cell (i, j)."""
        for i in range(nu):
            for j in range(nv):
                p = [f(i / nu, j / nv), f((i + 1) / nu, j / nv), f((i + 1) / nu, (j + 1) / nv), f(i / nu, (j + 1) / nv)]
                col = color(i, j) if callable(color) else color
                o = outward((p[0] + p[2]) / 2) if callable(outward) else outward
                self.quad(*p, o, col, w)

    def box(self, lo, hi, color, w):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        v = lambda x, y, z: np.array((x, y, z), float)
        c = (v(x0, y0, z0) + v(x1, y1, z1)) / 2
        for q in ([v(x0, y0, z1), v(x1, y0, z1), v(x1, y1, z1), v(x0, y1, z1)], [v(x1, y0, z0), v(x0, y0, z0), v(x0, y1, z0), v(x1, y1, z0)],
                  [v(x0, y1, z0), v(x0, y1, z1), v(x1, y1, z1), v(x1, y1, z0)], [v(x0, y0, z0), v(x1, y0, z0), v(x1, y0, z1), v(x0, y0, z1)],
                  [v(x1, y0, z1), v(x1, y0, z0), v(x1, y1, z0), v(x1, y1, z1)], [v(x0, y0, z0), v(x0, y0, z1), v(x0, y1, z1), v(x0, y1, z0)]):
            self.quad(*q, np.mean(q, axis=0) - c, color, w)

    def mirrored(self):
        """A copy of this mesh across the body's mid plane with .L and .R swapped."""
        m = Mesh()
        flip = lambda n: n[:-2] + (".R" if n.endswith(".L") else ".L") if n.endswith((".L", ".R")) else n
        names = [n for n, _, _ in RIG]
        for p, nr, c, j, w in zip(self.pos, self.nrm, self.col, self.jnt, self.wgt):
            m.pos.append(p * (-1, 1, 1)); m.nrm.append(nr * (-1, 1, 1)); m.col.append(c)
            m.jnt.append([INDEX[flip(names[k])] for k in j]); m.wgt.append(w)
        # mirroring reverses the winding: swap two corners of every triangle
        for t in range(0, len(m.pos), 3):
            for arr in (m.pos, m.nrm, m.col, m.jnt, m.wgt):
                arr[t + 1], arr[t + 2] = arr[t + 2], arr[t + 1]
        return m

    def extend(self, other):
        for name in ("pos", "nrm", "col", "jnt", "wgt"):
            getattr(self, name).extend(getattr(other, name))


# ---- shapes ----
def ellipse(c, ru, rv, n, u=(1, 0, 0), v=(0, 0, 1), start=0.0):
    c, u, v = np.asarray(c, float), unit(u), unit(v)
    return np.array([c + u * ru * math.cos(start + 2 * math.pi * i / n) + v * rv * math.sin(start + 2 * math.pi * i / n) for i in range(n)])


def frame(axis):
    """Two unit vectors perpendicular to an axis (u is horizontal when the axis is not vertical)."""
    axis = unit(axis)
    ref = np.array((0, 0, 1.0)) if abs(axis[2]) < 0.9 else np.array((1.0, 0, 0))
    u = unit(np.cross(axis, ref))
    return u, unit(np.cross(axis, u))


def limb(points, radii, colors, weights, n=8):
    """Tube rings along a polyline: points (k), radii (k) and per-ring colors/weights; each ring is perpendicular to the local direction."""
    rings = []
    for i, p in enumerate(points):
        d = np.asarray(points[min(i + 1, len(points) - 1)], float) - np.asarray(points[max(i - 1, 0)], float)
        u, v = frame(d)
        rings.append(dict(pts=ellipse(p, radii[i], radii[i], n, u, v), color=colors[i], w=weights[i]))
    return rings


# ---- output ----
def write_piece(mesh: Mesh, name: str, max_triangles: int, out_dir: Path | None = None) -> bool:
    out = (out_dir or Path(__file__).resolve().parent / "out") / f"{name}.glb"
    out.parent.mkdir(parents=True, exist_ok=True)
    pos = np.array(mesh.pos, np.float32); nrm = np.array(mesh.nrm, np.float32); col = np.array(mesh.col, np.float32)
    jnt = np.array(mesh.jnt, np.uint8); wgt = np.array(mesh.wgt, np.float32)
    idx = np.arange(len(pos), dtype=np.uint16)
    ibm = np.zeros((len(RIG), 4, 4), np.float32)
    for i, (_, _, w) in enumerate(RIG):
        ibm[i] = np.eye(4); ibm[i][:3, 3] = -w; ibm[i] = ibm[i].T          # glTF matrices are column-major
    blob = bytearray(); views, accs = [], []

    def add(arr, ctype, atype, target, with_bounds=False):
        while len(blob) % 4: blob.append(0)
        views.append(BufferView(buffer=0, byteOffset=len(blob), byteLength=arr.nbytes, target=target))
        blob.extend(arr.tobytes())
        a = Accessor(bufferView=len(views) - 1, componentType=ctype, count=len(arr) if arr.ndim < 3 else len(arr), type=atype)
        if with_bounds: a.min, a.max = [float(x) for x in arr.min(axis=0)], [float(x) for x in arr.max(axis=0)]
        accs.append(a); return len(accs) - 1
    ap = add(pos, 5126, "VEC3", 34962, True); an = add(nrm, 5126, "VEC3", 34962); ac = add(col, 5126, "VEC4", 34962)
    aj = add(jnt, 5121, "VEC4", 34962); aw = add(wgt, 5126, "VEC4", 34962); ai = add(idx, 5123, "SCALAR", 34963)
    ab = add(ibm, 5126, "MAT4", None)

    g = GLTF2(asset=Asset(version="2.0", generator="gear_lib.py"))
    kids = {i: [] for i in range(len(RIG))}
    for i, (_, p, _) in enumerate(RIG):
        if p is not None: kids[INDEX[p]].append(i)
    g.nodes = [Node(name=n, translation=[float(x) for x in (w - (JOINT[p] if p else 0))], children=kids[i]) for i, (n, p, w) in enumerate(RIG)]
    g.nodes.append(Node(name=name + "_mesh", mesh=0, skin=0))
    g.scenes = [Scene(nodes=[0, len(RIG)])]; g.scene = 0
    g.materials = [Material(name="vertex", pbrMetallicRoughness=PbrMetallicRoughness(baseColorFactor=[1, 1, 1, 1], metallicFactor=0, roughnessFactor=1))]
    g.meshes = [GltfMesh(name=name, primitives=[Primitive(attributes=Attributes(POSITION=ap, NORMAL=an, COLOR_0=ac, JOINTS_0=aj, WEIGHTS_0=aw),
                                                         indices=ai, material=0, mode=4)])]
    g.skins = [Skin(name="body", inverseBindMatrices=ab, skeleton=0, joints=list(range(len(RIG))))]
    g.accessors, g.bufferViews = accs, views
    g.buffers = [Buffer(byteLength=len(blob))]; g.set_binary_blob(bytes(blob))
    g.save_binary(str(out))

    back = GLTF2().load(str(out))
    lo, hi = pos.min(axis=0), pos.max(axis=0)
    checks = {
        "28 joints in engine order": [back.nodes[j].name for j in back.skins[0].joints] == [n for n, _, _ in RIG],
        "one mesh, one primitive": len(back.meshes) == 1 and len(back.meshes[0].primitives) == 1,
        "weights sum to 1": bool(np.all(abs(wgt.sum(axis=1) - 1) < 1e-3)),
        "no NaN": bool(np.isfinite(pos).all() and np.isfinite(nrm).all()),
        "triangles within budget": mesh.triangles <= max_triangles,
        "vertices fit 16 bits": len(pos) < 65536,
        "normals match winding": True,
    }
    for t in range(0, len(pos), 3):   # counter-clockwise winding must agree with the stored normal
        if np.dot(np.cross(pos[t + 1] - pos[t], pos[t + 2] - pos[t]), nrm[t]) <= 0:
            checks["normals match winding"] = False; break
    print(f"{name}: {mesh.triangles} triangles (budget {max_triangles}), {len(pos)} vertices, bbox {np.round(lo, 3)} .. {np.round(hi, 3)}")
    print("  " + ", ".join(f"{k}={'ok' if v else 'FAIL'}" for k, v in checks.items()))
    ok = all(checks.values())
    print("  OK" if ok else "  FAILED", "->", out)
    return ok


def finish(ok: bool):
    if not ok: sys.exit(1)
