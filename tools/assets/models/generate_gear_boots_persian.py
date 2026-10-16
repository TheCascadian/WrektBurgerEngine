#!/usr/bin/env python3
"""Generate low-poly Achaemenid Persian ankle boots as one skinned GLB."""
from __future__ import annotations
import math
import sys
from pathlib import Path
import numpy as np
from pygltflib import (
    Accessor, Asset, Attributes, Buffer, BufferView, GLTF2, Material, Mesh,
    Node, PbrMetallicRoughness, Primitive, Scene, Skin,
)

# Tunable dimensions in meters; all colors are RGBA vertex colors.
OUTPUT_NAME = "gear_boots_persian.glb"
MAX_TRIANGLES = 200
SEGMENTS = 8
# Profile rows: (Y, half-width X, half-length Z, center Z). Toes face +Z.
BOOT_PROFILE = (
    (0.000, 0.050, 0.112, 0.100),  # dark sole bottom
    (0.018, 0.050, 0.112, 0.100),  # sole top
    (0.030, 0.048, 0.108, 0.098),  # leather upper
    (0.075, 0.045, 0.095, 0.085),  # vamp
    (0.130, 0.041, 0.076, 0.065),  # instep / ankle
    (0.190, 0.040, 0.061, 0.055),  # ankle cuff
)
FOOT_CENTER_X = 0.116
ANKLE_HEIGHT = 0.190
TOE_JOINT_Z = 0.170
COLOR_LEATHER = (112/255, 72/255, 42/255, 1.0)       # #70482A
COLOR_LEATHER_SHADE = (132/255, 86/255, 49/255, 1.0)
COLOR_SOLE = (65/255, 42/255, 30/255, 1.0)           # #412A1E

# Exact shared humanoid rest pose from the other Persian gear pieces, in meters.
CENTRAL = {
    "root": (0.000, 0.000, 0.000), "hips": (0.000, 0.920, 0.000),
    "spine": (0.000, 1.140, 0.000), "chest": (0.000, 1.400, 0.000),
    "neck": (0.000, 1.540, 0.000), "head": (0.000, 1.660, 0.000),
}
SIDE_PARTS = {
    "shoulder": (0.200, 1.405, 0.000),
    "upper_arm": (0.300, 1.320, 0.000),
    "forearm": (0.455, 1.170, 0.000),
    "hand": (0.595, 1.015, 0.000),
    "fingers": (0.610, 0.940, 0.025),
    "thumb": (0.570, 0.995, 0.025),
    "thigh": (0.105, 0.820, 0.000),
    "shin": (0.112, 0.430, 0.000),
    "foot": (0.116, 0.070, 0.060),
    "toe": (0.116, 0.040, 0.170),
}
SIDES = ("L", "R")
COMPONENT_UNSIGNED_SHORT, COMPONENT_UNSIGNED_BYTE, COMPONENT_FLOAT = 5123, 5121, 5126
ARRAY_BUFFER, ELEMENT_ARRAY_BUFFER, TRIANGLES = 34962, 34963, 4


def joint_tables():
    names = list(CENTRAL)
    parents = {
        "root": None, "hips": "root", "spine": "hips",
        "chest": "spine", "neck": "chest", "head": "neck",
    }
    world = {name: np.array(pos, dtype=np.float64) for name, pos in CENTRAL.items()}
    for side in SIDES:
        sign = -1.0 if side == "L" else 1.0
        for part, pos in SIDE_PARTS.items():
            name = f"{part}.{side}"
            world[name] = np.array((sign*pos[0], pos[1], pos[2]), dtype=np.float64)
            names.append(name)
        parents.update({
            f"shoulder.{side}": "chest",
            f"upper_arm.{side}": f"shoulder.{side}",
            f"forearm.{side}": f"upper_arm.{side}",
            f"hand.{side}": f"forearm.{side}",
            f"fingers.{side}": f"hand.{side}",
            f"thumb.{side}": f"hand.{side}",
            f"thigh.{side}": "hips",
            f"shin.{side}": f"thigh.{side}",
            f"foot.{side}": f"shin.{side}",
            f"toe.{side}": f"foot.{side}",
        })
    for side in SIDES:
        sign = -1.0 if side == "L" else 1.0
        assert np.allclose(world[f"foot.{side}"], (sign*FOOT_CENTER_X, .070, .060))
        assert np.allclose(world[f"toe.{side}"], (sign*FOOT_CENTER_X, .040, TOE_JOINT_Z))
    return names, parents, world


class Builder:
    def __init__(self, names):
        self.joint_index = {name: i for i, name in enumerate(names)}
        self.positions, self.normals, self.colors = [], [], []
        self.joints, self.weights = [], []
        self.triangles = 0

    @staticmethod
    def toe_weight(p):
        forward = np.clip((float(p[2]) - 0.055) / 0.145, 0.0, 1.0)
        lower_foot = np.clip((ANKLE_HEIGHT - float(p[1])) / 0.105, 0.0, 1.0)
        return float(forward * lower_foot)

    def tri(self, a, b, c, color, outward, side):
        pts = [np.asarray(p, dtype=np.float64) for p in (a, b, c)]
        cross = np.cross(pts[1]-pts[0], pts[2]-pts[0])
        area = float(np.linalg.norm(cross))
        if not math.isfinite(area) or area < 1e-9:
            raise ValueError(f"Degenerate triangle: {pts}")
        outward = np.asarray(outward, dtype=np.float64)
        outward /= np.linalg.norm(outward)
        if float(np.dot(cross, outward)) < 0:
            pts[1], pts[2] = pts[2], pts[1]
            cross = np.cross(pts[1]-pts[0], pts[2]-pts[0])
            area = float(np.linalg.norm(cross))
        normal = (cross / area).astype(np.float32)
        foot_idx, toe_idx = self.joint_index[f"foot.{side}"], self.joint_index[f"toe.{side}"]
        for p in pts:
            tw = self.toe_weight(p)
            self.positions.append(p.astype(np.float32))
            self.normals.append(normal)
            self.colors.append(np.asarray(color, dtype=np.float32))
            self.joints.append((foot_idx, toe_idx, foot_idx, toe_idx))
            self.weights.append((1.0-tw, tw, 0.0, 0.0))
        self.triangles += 1

    def quad(self, a, b, c, d, color, outward, side):
        self.tri(a, b, c, color, outward, side)
        self.tri(a, c, d, color, outward, side)

    def arrays(self):
        return {
            "positions": np.asarray(self.positions, dtype="<f4"),
            "normals": np.asarray(self.normals, dtype="<f4"),
            "colors": np.asarray(self.colors, dtype="<f4"),
            "joints": np.asarray(self.joints, dtype=np.uint8),
            "weights": np.asarray(self.weights, dtype="<f4"),
            "indices": np.arange(len(self.positions), dtype="<u2"),
            "triangle_count": self.triangles,
        }


def ring_point(cx, cz, y, rx, rz, angle):
    return np.array((cx+rx*math.cos(angle), y, cz+rz*math.sin(angle)), dtype=np.float64)


def add_boot(builder, side):
    cx = (-1.0 if side == "L" else 1.0) * FOOT_CENTER_X
    rings = []
    for y, rx, rz, cz in BOOT_PROFILE:
        rings.append([
            ring_point(cx, cz, y, rx, rz, 2*math.pi*i/SEGMENTS)
            for i in range(SEGMENTS)
        ])
    for layer in range(len(rings)-1):
        lower, upper = rings[layer], rings[layer+1]
        color = COLOR_SOLE if layer == 0 else COLOR_LEATHER_SHADE if layer == 1 else COLOR_LEATHER
        for i in range(SEGMENTS):
            j = (i+1) % SEGMENTS
            angle = 2*math.pi*(i+.5)/SEGMENTS
            builder.quad(
                lower[i], lower[j], upper[j], upper[i], color,
                (math.cos(angle), 0.0, math.sin(angle)), side,
            )
    bottom = np.array((cx, BOOT_PROFILE[0][0], BOOT_PROFILE[0][3]), dtype=np.float64)
    top = np.array((cx, BOOT_PROFILE[-1][0], BOOT_PROFILE[-1][3]), dtype=np.float64)
    for i in range(SEGMENTS):
        j = (i+1) % SEGMENTS
        builder.tri(bottom, rings[0][j], rings[0][i], COLOR_SOLE, (0,-1,0), side)
        builder.tri(top, rings[-1][i], rings[-1][j], COLOR_LEATHER, (0,1,0), side)


def build_geometry(names):
    builder = Builder(names)
    for side in SIDES:
        add_boot(builder, side)
    return builder.arrays()


def make_nodes(names, parents, world):
    indices = {name: i+1 for i, name in enumerate(names)}
    children = {name: [] for name in names}
    for name, parent in parents.items():
        if parent is not None:
            children[parent].append(name)
    nodes = [Node(name="gear_boots_persian_mesh", mesh=0, skin=0)]
    for name in names:
        parent = parents[name]
        parent_pos = np.zeros(3) if parent is None else world[parent]
        local = world[name] - parent_pos
        nodes.append(Node(
            name=name, translation=[float(v) for v in local],
            children=[indices[c] for c in children[name]],
        ))
    return nodes, indices


def inverse_binds(names, world):
    matrices = []
    for name in names:
        m = np.eye(4, dtype=np.float64)
        m[:3, 3] = -world[name]
        matrices.append(m.reshape(16, order="F"))
    return np.asarray(matrices, dtype="<f4")


def append_array(gltf, blob, array, target=None):
    while len(blob) % 4:
        blob.append(0)
    offset = len(blob)
    raw = np.ascontiguousarray(array).tobytes()
    blob.extend(raw)
    gltf.bufferViews.append(BufferView(buffer=0, byteOffset=offset, byteLength=len(raw), target=target))
    return len(gltf.bufferViews)-1


def accessor(gltf, view, component, count, kind, minimum=None, maximum=None):
    gltf.accessors.append(Accessor(
        bufferView=view, byteOffset=0, componentType=component, count=count, type=kind,
        min=None if minimum is None else [float(v) for v in minimum],
        max=None if maximum is None else [float(v) for v in maximum],
    ))
    return len(gltf.accessors)-1


def make_gltf(a, names, parents, world):
    gltf = GLTF2()
    gltf.asset = Asset(version="2.0", generator="Procedural Persian Boots Generator")
    gltf.asset.extras = {"units":"meters", "upAxis":"Y", "forwardAxis":"+Z", "animationsRequested":"none"}
    gltf.nodes, node_indices = make_nodes(names, parents, world)
    gltf.scenes = [Scene(name="Scene", nodes=[0, node_indices["root"]])]
    gltf.scene = 0
    blob = bytearray()
    views = {
        "pos": append_array(gltf, blob, a["positions"], ARRAY_BUFFER),
        "norm": append_array(gltf, blob, a["normals"], ARRAY_BUFFER),
        "color": append_array(gltf, blob, a["colors"], ARRAY_BUFFER),
        "joints": append_array(gltf, blob, a["joints"], ARRAY_BUFFER),
        "weights": append_array(gltf, blob, a["weights"], ARRAY_BUFFER),
        "indices": append_array(gltf, blob, a["indices"], ELEMENT_ARRAY_BUFFER),
        "ibm": append_array(gltf, blob, inverse_binds(names, world)),
    }
    while len(blob) % 4:
        blob.append(0)
    n = len(a["positions"])
    ac = {
        "pos": accessor(gltf, views["pos"], COMPONENT_FLOAT, n, "VEC3", a["positions"].min(0), a["positions"].max(0)),
        "norm": accessor(gltf, views["norm"], COMPONENT_FLOAT, n, "VEC3"),
        "color": accessor(gltf, views["color"], COMPONENT_FLOAT, n, "VEC4"),
        "joints": accessor(gltf, views["joints"], COMPONENT_UNSIGNED_BYTE, n, "VEC4"),
        "weights": accessor(gltf, views["weights"], COMPONENT_FLOAT, n, "VEC4"),
        "indices": accessor(gltf, views["indices"], COMPONENT_UNSIGNED_SHORT, len(a["indices"]), "SCALAR", [0], [int(a["indices"].max())]),
        "ibm": accessor(gltf, views["ibm"], COMPONENT_FLOAT, len(names), "MAT4"),
    }
    gltf.materials = [Material(
        name="leather_vertex_colors",
        pbrMetallicRoughness=PbrMetallicRoughness(baseColorFactor=[1,1,1,1], metallicFactor=0.0, roughnessFactor=1.0),
        doubleSided=False,
    )]
    gltf.meshes = [Mesh(name="gear_boots_persian", primitives=[Primitive(
        attributes=Attributes(POSITION=ac["pos"], NORMAL=ac["norm"], COLOR_0=ac["color"], JOINTS_0=ac["joints"], WEIGHTS_0=ac["weights"]),
        indices=ac["indices"], material=0, mode=TRIANGLES,
    )])]
    gltf.skins = [Skin(name="humanoid_26_joint_skin", inverseBindMatrices=ac["ibm"], skeleton=node_indices["root"], joints=[node_indices[x] for x in names])]
    gltf.animations = []
    gltf.buffers = [Buffer(byteLength=len(blob))]
    gltf.set_binary_blob(bytes(blob))
    return gltf, node_indices


def self_check(gltf, a, names, indices, world, path):
    p, n, j, w = a["positions"], a["normals"], a["joints"], a["weights"]
    lo, hi = p.min(0), p.max(0)
    tris = int(a["triangle_count"])
    primitive = gltf.meshes[0].primitives[0] if gltf.meshes else None
    attrs = primitive.attributes if primitive else None
    tri_points = p.reshape(-1, 3, 3)
    areas = np.linalg.norm(np.cross(tri_points[:,1]-tri_points[:,0], tri_points[:,2]-tri_points[:,0]), axis=1)
    expected_active = {names.index(x) for x in ("foot.L", "foot.R", "toe.L", "toe.R")}
    active = set(j[w > 1e-7].tolist())
    checks = {
        "one_mesh": len(gltf.meshes) == 1,
        "one_primitive": len(gltf.meshes) == 1 and len(gltf.meshes[0].primitives) == 1,
        "one_scene": len(gltf.scenes) == 1 and gltf.scene == 0,
        "joint_limit": len(gltf.skins[0].joints) <= 64,
        "exact_26_joints": len(names) == 26 and len(set(names)) == 26 and all(gltf.nodes[indices[x]].name == x for x in names),
        "four_joint_slots": j.shape == (len(p),4) and w.shape == j.shape,
        "weights_only_foot_toe": active == expected_active,
        "weights_sum_to_one": bool(np.all(np.abs(w.sum(1)-1.0) <= .001)),
        "no_nans": all(np.isfinite(a[k]).all() for k in ("positions","normals","colors","joints","weights","indices")),
        "normals_present": attrs is not None and attrs.NORMAL is not None,
        "no_textures_or_uvs": not getattr(gltf,"textures",None) and not getattr(gltf,"images",None) and attrs is not None and getattr(attrs,"TEXCOORD_0",None) is None,
        "indexed_triangles": primitive is not None and primitive.indices is not None and primitive.mode == TRIANGLES and len(a["indices"]) % 3 == 0 and len(a["indices"])//3 == tris,
        "nondegenerate_triangles": bool(np.all(areas > 1e-9)),
        "y_up": gltf.asset.extras.get("upAxis") == "Y",
        "forward_plus_z": gltf.asset.extras.get("forwardAxis") == "+Z" and all(world[f"foot.{s}"][2] < world[f"toe.{s}"][2] for s in SIDES) and hi[2] > 0,
        "lowest_vertex_y_zero": abs(float(lo[1])) <= 1e-5,
        "ankle_height": abs(float(hi[1])-ANKLE_HEIGHT) <= 1e-5,
        "triangle_budget": tris <= MAX_TRIANGLES,
        "no_animations": len(gltf.animations) == 0,
        "file_written": path.exists() and path.stat().st_size > 0,
    }
    print(f"Triangle count: {tris}")
    print(f"Joint count: {len(gltf.skins[0].joints)}")
    print("Bounding box min (m): " + np.array2string(lo, precision=4, separator=", "))
    print("Bounding box max (m): " + np.array2string(hi, precision=4, separator=", "))
    print("Joint names: " + ", ".join(names))
    print("Animation names and durations: []")
    print("Checks: " + ", ".join(f"{k}={'PASS' if v else 'FAIL'}" for k,v in checks.items()))
    if all(checks.values()):
        print("OK")
        return True
    print("SELF-CHECK FAILED", file=sys.stderr)
    return False


def main():
    output = Path(__file__).resolve().parent / OUTPUT_NAME
    names, parents, world = joint_tables()
    arrays = build_geometry(names)
    gltf, indices = make_gltf(arrays, names, parents, world)
    gltf.save_binary(str(output))
    loaded = GLTF2().load(str(output))
    if loaded is None:
        raise RuntimeError("Failed to reload the generated GLB")
    if not self_check(loaded, arrays, names, indices, world, output):
        raise SystemExit(1)
    print(f"Output file: {output}")


if __name__ == "__main__":
    main()
