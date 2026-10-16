#!/usr/bin/env python3
"""Refit generated gear .glb files to the engine body skeleton (assets/models/human_male.glb plus the two joints the built-in body adds,
"rifle" under chest and "headgear" under head): same joint names, order, parents and rest positions. A piece called *headgear* or *helmet* is skinned to "headgear".
The generators author .L at -X; the engine body has .L at +X, so side weights are swapped. Usage: fit_to_body.py in.glb out.glb"""
import sys
import numpy as np
from pygltflib import GLTF2

BODY = "../../../assets/models/human_male.glb"

def swap(n):
    return n[:-2] + (".R" if n.endswith(".L") else ".L") if n.endswith((".L", ".R")) else n

def world(g, par, i):
    t = np.array(g.nodes[i].translation or [0, 0, 0], float)
    return t + (world(g, par, par[i]) if i in par else 0)

def main(src, dst):
    body = GLTF2().load(BODY); g = GLTF2().load(src)
    bj = body.skins[0].joints
    bnames = [body.nodes[j].name for j in bj]
    gj = g.skins[0].joints
    gnames = [g.nodes[j].name for j in gj]
    assert sorted(bnames) == sorted(gnames), "joint names differ"
    blob = bytearray(g.binary_blob())
    # remap JOINTS_0: old index -> new index of the mirrored-side name
    prim = g.meshes[0].primitives[0]
    acc = g.accessors[prim.attributes.JOINTS_0]; bv = g.bufferViews[acc.bufferView]
    dt = {5121: np.uint8, 5123: np.uint16}[acc.componentType]
    off = (bv.byteOffset or 0) + (acc.byteOffset or 0)
    stride = bv.byteStride or 4 * np.dtype(dt).itemsize
    new_index = {n: i for i, n in enumerate(bnames)}
    lut = np.array([new_index[swap(n)] for n in gnames], dt)
    for v in range(acc.count):
        o = off + v * stride
        a = np.frombuffer(blob, dt, 4, o).copy()
        blob[o:o + 4 * np.dtype(dt).itemsize] = lut[a].tobytes()
    # skinned meshes ignore their node's transform in the engine: bake a mesh-node translation into the vertices
    mesh_node = next(n for n in g.nodes if n.mesh is not None)
    if mesh_node.translation and any(mesh_node.translation):
        pa = g.accessors[prim.attributes.POSITION]; pv = g.bufferViews[pa.bufferView]
        po = (pv.byteOffset or 0) + (pa.byteOffset or 0); ps = pv.byteStride or 12
        t = np.array(mesh_node.translation, np.float32)
        for v in range(pa.count):
            o = po + v * ps
            blob[o:o + 12] = (np.frombuffer(blob, np.float32, 3, o) + t).astype(np.float32).tobytes()
        pa.min = [float(x + d) for x, d in zip(pa.min, t)]; pa.max = [float(x + d) for x, d in zip(pa.max, t)]
        mesh_node.translation = [0, 0, 0]
    # rebuild nodes' transforms to the body's, keyed by name
    bpar = {c: i for i, n in enumerate(body.nodes) for c in n.children}
    bnode = {body.nodes[j].name: j for j in bj}
    gnode = {g.nodes[j].name: j for j in gj}
    from pygltflib import Node, Accessor, BufferView
    for extra, parent in (("rifle", "chest"), ("headgear", "head")):
        g.nodes.append(Node(name=extra, translation=[0, 0, 0], rotation=[0, 0, 0, 1]))
        gnode[extra] = len(g.nodes) - 1; bnames.append(extra)
        g.nodes[gnode[parent]].children = list(g.nodes[gnode[parent]].children or []) + [gnode[extra]]
    for n, j in bnode.items():
        gn = g.nodes[gnode[n]]
        gn.translation = list(body.nodes[j].translation or [0, 0, 0]); gn.rotation = [0, 0, 0, 1]
        gn.children = [gnode[body.nodes[c].name] for c in body.nodes[j].children]
        if n == "chest": gn.children.append(gnode["rifle"])
        if n == "head": gn.children.append(gnode["headgear"])
        # the index remap above assumed new order = body order, so reorder joints to the body's
    g.skins[0].joints = [gnode[n] for n in bnames]
    # inverse bind matrices: translation by -world position (column-major)
    gpar = {c: i for i, n in enumerate(g.nodes) for c in n.children}
    ibm = np.zeros((len(bnames), 4, 4), np.float32)
    for k, n in enumerate(bnames):
        m = np.eye(4, dtype=np.float32); m[:3, 3] = -world(g, gpar, gnode[n]); ibm[k] = m.T
    if "headgear" in src or "helmet" in src:   # the cap rides on the headgear joint
        hi, hd = new_index["head"], len(bnames) - 1
        for v in range(acc.count):
            o = off + v * stride
            a = np.frombuffer(blob, dt, 4, o).copy(); a[a == hi] = hd
            blob[o:o + 4 * np.dtype(dt).itemsize] = a.tobytes()
    while len(blob) % 4: blob.append(0)
    start = len(blob); blob += ibm.tobytes()
    g.bufferViews.append(BufferView(buffer=0, byteOffset=start, byteLength=ibm.nbytes))
    g.accessors.append(Accessor(bufferView=len(g.bufferViews) - 1, componentType=5126, count=len(bnames), type="MAT4"))
    g.skins[0].inverseBindMatrices = len(g.accessors) - 1
    g.buffers[0].byteLength = len(blob)
    g.set_binary_blob(bytes(blob)); g.save(dst)

main(*sys.argv[1:3])
