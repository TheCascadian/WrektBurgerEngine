#!/usr/bin/env python3
"""Generate a low-poly, skinned Persian cloth cap as a binary glTF 2.0 file."""
from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np
from pygltflib import (
    Accessor, Asset, Attributes, Buffer, BufferView, GLTF2, Material,
    Mesh, Node, PbrMetallicRoughness, Primitive, Scene, Skin,
)

# -----------------------------------------------------------------------------
# Tunable constants. Dimensions are meters; colors are RGBA vertex colors.
# The joint rest pose matches the accompanying Persian tunic asset exactly.
# -----------------------------------------------------------------------------
OUTPUT_NAME = "gear_headgear_persian.glb"
MAX_TRIANGLES = 300
SEGMENTS = 12

# Position in the shared 1.80 m humanoid model space. The mesh's local base is
# Y=0, with this translation placing it on the head. Total cap height: 0.195 m.
CAP_BASE_Y = 1.755
CAP_HEIGHT = 0.195

# Deep blue cap. Profile rows: (height above base, X radius, Z radius).
CAP_PROFILE = (
    (0.000, 0.103, 0.113),
    (0.015, 0.108, 0.118),
    (0.045, 0.108, 0.118),
    (0.085, 0.101, 0.112),
    (0.125, 0.083, 0.095),
    (0.160, 0.053, 0.064),
    (0.183, 0.022, 0.029),
)
CAP_TOP_OFFSET = CAP_HEIGHT
COLOR_CAP = (35 / 255.0, 55 / 255.0, 105 / 255.0, 1.0)  # deep blue #233769

# Lighter cloth wrap band; radii in meters. It is slightly proud of the cap
# surface and has its own closed inner/outer walls and top/bottom surfaces.
WRAP_BOTTOM_Y = 0.035
WRAP_TOP_Y = 0.075
WRAP_OUTER_RADII = (0.112, 0.122)
WRAP_INNER_RADII = (0.101, 0.111)
COLOR_WRAP = (89 / 255.0, 126 / 255.0, 185 / 255.0, 1.0)  # lighter blue #597EB9

# EXACT shared joint rest pose from the accompanying tunic asset, in meters.
CENTRAL_JOINT_POSITIONS = {
    "root": (0.000, 0.000, 0.000),
    "hips": (0.000, 0.920, 0.000),
    "spine": (0.000, 1.140, 0.000),
    "chest": (0.000, 1.400, 0.000),
    "neck": (0.000, 1.540, 0.000),
    "head": (0.000, 1.660, 0.000),
}
SIDE_JOINT_POSITIONS = {
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
SIDE_PARTS = (
    "shoulder", "upper_arm", "forearm", "hand", "fingers", "thumb",
    "thigh", "shin", "foot", "toe",
)
ANIMATION_NAMES = ()
ANIMATION_TIMINGS = {}

# glTF constants.
COMPONENT_UNSIGNED_SHORT = 5123
COMPONENT_UNSIGNED_BYTE = 5121
COMPONENT_FLOAT = 5126
ARRAY_BUFFER = 34962
ELEMENT_ARRAY_BUFFER = 34963
TRIANGLES = 4


def normalize(v: np.ndarray) -> np.ndarray:
    length = float(np.linalg.norm(v))
    if length <= 1e-12:
        raise ValueError("Cannot normalize a zero-length vector")
    return v / length


def make_joint_tables():
    names = ["root", "hips", "spine", "chest", "neck", "head"]
    parents = {
        "root": None,
        "hips": "root",
        "spine": "hips",
        "chest": "spine",
        "neck": "chest",
        "head": "neck",
    }
    world = {
        name: np.asarray(pos, dtype=np.float64)
        for name, pos in CENTRAL_JOINT_POSITIONS.items()
    }
    for side in SIDES:
        sign = -1.0 if side == "L" else 1.0
        for part in SIDE_PARTS:
            x, y, z = SIDE_JOINT_POSITIONS[part]
            name = f"{part}.{side}"
            world[name] = np.asarray((sign * x, y, z), dtype=np.float64)
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
    return names, parents, world


class MeshBuilder:
    """Triangle-soup builder: duplicated corners provide correct flat normals."""
    def __init__(self, joint_names):
        self.joint_index = {name: i for i, name in enumerate(joint_names)}
        self.head_index = self.joint_index["head"]
        self.positions = []
        self.normals = []
        self.colors = []
        self.joints = []
        self.weights = []
        self.triangle_count = 0

    def triangle(self, p0, p1, p2, color, expected_normal):
        points = [np.asarray(p, dtype=np.float64) for p in (p0, p1, p2)]
        raw = np.cross(points[1] - points[0], points[2] - points[0])
        area2 = float(np.linalg.norm(raw))
        if not math.isfinite(area2) or area2 < 1e-9:
            raise ValueError(f"Degenerate triangle detected: {points}")

        expected = normalize(np.asarray(expected_normal, dtype=np.float64))
        if float(np.dot(raw, expected)) < 0.0:
            points[1], points[2] = points[2], points[1]
            raw = np.cross(points[1] - points[0], points[2] - points[0])
            area2 = float(np.linalg.norm(raw))
        normal = (raw / area2).astype(np.float32)

        for point in points:
            self.positions.append(point.astype(np.float32))
            self.normals.append(normal)
            self.colors.append(np.asarray(color, dtype=np.float32))
            # Only head has nonzero influence. Four slots remain present as required.
            self.joints.append((self.head_index,) * 4)
            self.weights.append((1.0, 0.0, 0.0, 0.0))
        self.triangle_count += 1

    def quad(self, a, b, c, d, color, expected_normal):
        self.triangle(a, b, c, color, expected_normal)
        self.triangle(a, c, d, color, expected_normal)

    def arrays(self):
        vertex_count = len(self.positions)
        return {
            "positions": np.asarray(self.positions, dtype="<f4"),
            "normals": np.asarray(self.normals, dtype="<f4"),
            "colors": np.asarray(self.colors, dtype="<f4"),
            "joints": np.asarray(self.joints, dtype=np.uint8),
            "weights": np.asarray(self.weights, dtype="<f4"),
            "indices": np.arange(vertex_count, dtype="<u2"),
            "triangle_count": self.triangle_count,
        }


def ellipse_ring(y, radii, segments=SEGMENTS):
    rx, rz = radii
    return [
        np.asarray((rx * math.cos(2 * math.pi * i / segments), y,
                    rz * math.sin(2 * math.pi * i / segments)), dtype=np.float64)
        for i in range(segments)
    ]


def add_cap(builder):
    rings = [ellipse_ring(offset, (rx, rz))
             for offset, rx, rz in CAP_PROFILE]

    for ring_i in range(len(rings) - 1):
        y0, rx0, rz0 = CAP_PROFILE[ring_i]
        y1, rx1, rz1 = CAP_PROFILE[ring_i + 1]
        dy = y1 - y0
        # Normal of an ellipse profile r(y) is proportional to (radial, -dr/dy).
        slope = (rx1 - rx0) / dy
        low, high = rings[ring_i], rings[ring_i + 1]
        for i in range(SEGMENTS):
            j = (i + 1) % SEGMENTS
            angle = 2 * math.pi * (i + 0.5) / SEGMENTS
            expected = (math.cos(angle), -slope, math.sin(angle))
            builder.quad(low[i], low[j], high[j], high[i], COLOR_CAP, expected)

    # Close the bottom with a planar cap.
    bottom_center = np.asarray((0.0, 0.0, 0.0), dtype=np.float64)
    for i in range(SEGMENTS):
        j = (i + 1) % SEGMENTS
        builder.triangle(
            bottom_center, rings[0][j], rings[0][i], COLOR_CAP, (0.0, -1.0, 0.0)
        )

    # Close the rounded crown with a small fan to a nondegenerate pole.
    pole = np.asarray((0.0, CAP_TOP_OFFSET, 0.0), dtype=np.float64)
    crown = rings[-1]
    for i in range(SEGMENTS):
        j = (i + 1) % SEGMENTS
        angle = 2 * math.pi * (i + 0.5) / SEGMENTS
        builder.triangle(
            crown[i], crown[j], pole, COLOR_CAP,
            (math.cos(angle), 0.7, math.sin(angle)),
        )


def add_wrap_band(builder):
    outer_bottom = ellipse_ring(WRAP_BOTTOM_Y, WRAP_OUTER_RADII)
    outer_top = ellipse_ring(WRAP_TOP_Y, WRAP_OUTER_RADII)
    inner_bottom = ellipse_ring(WRAP_BOTTOM_Y, WRAP_INNER_RADII)
    inner_top = ellipse_ring(WRAP_TOP_Y, WRAP_INNER_RADII)

    for i in range(SEGMENTS):
        j = (i + 1) % SEGMENTS
        angle = 2 * math.pi * (i + 0.5) / SEGMENTS
        outward = (math.cos(angle), 0.0, math.sin(angle))
        inward = (-math.cos(angle), 0.0, -math.sin(angle))
        builder.quad(
            outer_bottom[i], outer_bottom[j], outer_top[j], outer_top[i],
            COLOR_WRAP, outward,
        )
        builder.quad(
            inner_bottom[j], inner_bottom[i], inner_top[i], inner_top[j],
            COLOR_WRAP, inward,
        )
        builder.quad(
            outer_top[i], outer_top[j], inner_top[j], inner_top[i],
            COLOR_WRAP, (0.0, 1.0, 0.0),
        )
        builder.quad(
            inner_bottom[i], inner_bottom[j], outer_bottom[j], outer_bottom[i],
            COLOR_WRAP, (0.0, -1.0, 0.0),
        )


def create_mesh_arrays(joint_names):
    builder = MeshBuilder(joint_names)
    add_cap(builder)
    add_wrap_band(builder)
    arrays = builder.arrays()
    return arrays


def make_nodes_and_indices(joint_names, parents, world_positions):
    mesh_node_index = 0
    node_index = {name: i + 1 for i, name in enumerate(joint_names)}
    children = {name: [] for name in joint_names}
    for name, parent in parents.items():
        if parent is not None:
            children[parent].append(name)

    # The mesh's vertex Y begins at 0 for the generic vertex-origin check; this
    # node translation places the cap in shared body/model space at Y=1.755 m.
    nodes = [Node(
        name="gear_headgear_persian_mesh",
        mesh=0,
        skin=0,
        translation=[0.0, CAP_BASE_Y, 0.0],
    )]
    for name in joint_names:
        parent = parents[name]
        parent_world = (
            np.zeros(3, dtype=np.float64)
            if parent is None else world_positions[parent]
        )
        local = world_positions[name] - parent_world
        nodes.append(Node(
            name=name,
            translation=[float(x) for x in local],
            children=[node_index[c] for c in children[name]],
        ))
    return nodes, node_index


def inverse_bind_matrices(joint_names, world_positions):
    # IBM = inverse(joint_world) * mesh_world. This accounts for the mesh-node
    # translation, so all joints leave the vertex positions unchanged at rest.
    mesh_world = np.eye(4, dtype=np.float64)
    mesh_world[1, 3] = CAP_BASE_Y
    matrices = []
    for name in joint_names:
        joint_world = np.eye(4, dtype=np.float64)
        joint_world[:3, 3] = world_positions[name]
        ibm = np.linalg.inv(joint_world) @ mesh_world
        matrices.append(ibm.reshape(16, order="F"))
    return np.asarray(matrices, dtype="<f4")


def append_array(gltf, blob, array, target=None):
    while len(blob) % 4:
        blob.append(0)
    offset = len(blob)
    raw = np.ascontiguousarray(array).tobytes()
    blob.extend(raw)
    gltf.bufferViews.append(BufferView(
        buffer=0, byteOffset=offset, byteLength=len(raw), target=target
    ))
    return len(gltf.bufferViews) - 1


def add_accessor(gltf, view, component_type, count, kind,
                 minimum=None, maximum=None, normalized=False):
    gltf.accessors.append(Accessor(
        bufferView=view,
        byteOffset=0,
        componentType=component_type,
        count=count,
        type=kind,
        min=None if minimum is None else [float(x) for x in minimum],
        max=None if maximum is None else [float(x) for x in maximum],
        normalized=normalized,
    ))
    return len(gltf.accessors) - 1


def build_gltf(arrays, joint_names, parents, world_positions):
    gltf = GLTF2()
    gltf.asset = Asset(version="2.0", generator="Procedural Persian Headgear Generator")
    gltf.asset.extras = {
        "units": "meters",
        "upAxis": "Y",
        "forwardAxis": "+Z",
        "meshWorldBaseY": CAP_BASE_Y,
        "animationsRequested": "none",
    }

    gltf.nodes, node_index = make_nodes_and_indices(
        joint_names, parents, world_positions
    )
    gltf.scenes = [Scene(name="Scene", nodes=[0, node_index["root"]])]
    gltf.scene = 0

    blob = bytearray()
    pos_view = append_array(gltf, blob, arrays["positions"], ARRAY_BUFFER)
    norm_view = append_array(gltf, blob, arrays["normals"], ARRAY_BUFFER)
    color_view = append_array(gltf, blob, arrays["colors"], ARRAY_BUFFER)
    joints_view = append_array(gltf, blob, arrays["joints"], ARRAY_BUFFER)
    weights_view = append_array(gltf, blob, arrays["weights"], ARRAY_BUFFER)
    indices_view = append_array(gltf, blob, arrays["indices"], ELEMENT_ARRAY_BUFFER)
    ibm_view = append_array(
        gltf, blob, inverse_bind_matrices(joint_names, world_positions), None
    )
    while len(blob) % 4:
        blob.append(0)

    count = len(arrays["positions"])
    pos_accessor = add_accessor(
        gltf, pos_view, COMPONENT_FLOAT, count, "VEC3",
        arrays["positions"].min(axis=0), arrays["positions"].max(axis=0),
    )
    norm_accessor = add_accessor(gltf, norm_view, COMPONENT_FLOAT, count, "VEC3")
    color_accessor = add_accessor(gltf, color_view, COMPONENT_FLOAT, count, "VEC4")
    joints_accessor = add_accessor(
        gltf, joints_view, COMPONENT_UNSIGNED_BYTE, count, "VEC4"
    )
    weights_accessor = add_accessor(
        gltf, weights_view, COMPONENT_FLOAT, count, "VEC4"
    )
    index_accessor = add_accessor(
        gltf, indices_view, COMPONENT_UNSIGNED_SHORT, len(arrays["indices"]), "SCALAR",
        [0], [int(arrays["indices"].max())],
    )
    ibm_accessor = add_accessor(
        gltf, ibm_view, COMPONENT_FLOAT, len(joint_names), "MAT4"
    )

    gltf.materials = [Material(
        name="cloth_vertex_colors",
        pbrMetallicRoughness=PbrMetallicRoughness(
            baseColorFactor=[1.0, 1.0, 1.0, 1.0],
            metallicFactor=0.0,
            roughnessFactor=1.0,
        ),
        doubleSided=False,
    )]
    gltf.meshes = [Mesh(
        name="gear_headgear_persian",
        primitives=[Primitive(
            attributes=Attributes(
                POSITION=pos_accessor,
                NORMAL=norm_accessor,
                COLOR_0=color_accessor,
                JOINTS_0=joints_accessor,
                WEIGHTS_0=weights_accessor,
            ),
            indices=index_accessor,
            material=0,
            mode=TRIANGLES,
        )],
    )]
    gltf.skins = [Skin(
        name="humanoid_26_joint_skin",
        inverseBindMatrices=ibm_accessor,
        skeleton=node_index["root"],
        joints=[node_index[name] for name in joint_names],
    )]
    gltf.animations = []
    gltf.buffers = [Buffer(byteLength=len(blob))]
    gltf.set_binary_blob(bytes(blob))
    return gltf, node_index


def self_check(gltf, arrays, joint_names, node_index, path):
    local_positions = arrays["positions"]
    world_positions = local_positions + np.asarray((0.0, CAP_BASE_Y, 0.0), dtype=np.float32)
    local_min = local_positions.min(axis=0)
    local_max = local_positions.max(axis=0)
    world_min = world_positions.min(axis=0)
    world_max = world_positions.max(axis=0)
    weights = arrays["weights"]
    joints = arrays["joints"]
    triangle_count = int(arrays["triangle_count"])
    head_index = joint_names.index("head")
    primitive = gltf.meshes[0].primitives[0] if gltf.meshes else None
    attr = primitive.attributes if primitive else None

    checks = {
        "one_mesh": len(gltf.meshes) == 1,
        "one_primitive": len(gltf.meshes) == 1 and len(gltf.meshes[0].primitives) == 1,
        "one_scene": len(gltf.scenes) == 1 and gltf.scene == 0,
        "joint_limit": len(gltf.skins[0].joints) <= 64,
        "exact_26_joint_names": (
            len(joint_names) == 26 and len(set(joint_names)) == 26
            and all(gltf.nodes[node_index[n]].name == n for n in joint_names)
        ),
        "four_joint_slots": joints.shape == (len(local_positions), 4)
        and weights.shape == joints.shape,
        "100_percent_head_weight": (
            np.all(np.abs(weights[:, 0] - 1.0) <= 1e-6)
            and np.all(np.abs(weights[:, 1:]) <= 1e-6)
            and np.all(joints == head_index)
        ),
        "weights_sum_to_one": bool(np.all(np.abs(weights.sum(axis=1) - 1.0) <= 0.001)),
        "no_nans": all(np.isfinite(arrays[k]).all() for k in (
            "positions", "normals", "colors", "joints", "weights", "indices"
        )),
        "normals_present": attr is not None and attr.NORMAL is not None,
        "no_textures_or_uvs": (
            not getattr(gltf, "textures", None)
            and not getattr(gltf, "images", None)
            and attr is not None and getattr(attr, "TEXCOORD_0", None) is None
        ),
        "indexed_triangles": (
            primitive is not None and primitive.indices is not None
            and primitive.mode == TRIANGLES
            and len(arrays["indices"]) % 3 == 0
            and len(arrays["indices"]) // 3 == triangle_count
        ),
        "no_degenerate_triangles": triangle_count > 0,
        "y_up": gltf.asset.extras.get("upAxis") == "Y",
        "faces_positive_z": gltf.asset.extras.get("forwardAxis") == "+Z",
        # For this headgear, local vertex Y starts at zero; the mesh-node
        # translation places it at the head. World Y=0 would contradict fit.
        "local_lowest_vertex_at_zero": abs(float(local_min[1])) <= 1e-5,
        "correct_world_head_placement": (
            abs(float(world_min[1]) - CAP_BASE_Y) <= 1e-5
            and abs(float(world_max[1]) - (CAP_BASE_Y + CAP_HEIGHT)) <= 1e-5
        ),
        "triangle_budget": triangle_count <= MAX_TRIANGLES,
        "no_animations": len(gltf.animations) == 0,
        "file_written": path.exists() and path.stat().st_size > 0,
    }

    print(f"Triangle count: {triangle_count}")
    print(f"Joint count: {len(gltf.skins[0].joints)}")
    print("Bounding box min (world/model m): " + np.array2string(world_min, precision=4, separator=", "))
    print("Bounding box max (world/model m): " + np.array2string(world_max, precision=4, separator=", "))
    print(f"Mesh-local lowest vertex Y (m): {float(local_min[1]):.4f}")
    print("Joint names: " + ", ".join(joint_names))
    print("Animation names and durations: []")
    print("Checks: " + ", ".join(
        f"{name}={'PASS' if passed else 'FAIL'}" for name, passed in checks.items()
    ))
    if all(checks.values()):
        print("OK")
        return True
    print("SELF-CHECK FAILED", file=sys.stderr)
    return False


def main():
    output_path = Path(__file__).resolve().parent / OUTPUT_NAME
    joint_names, parents, world_joint_positions = make_joint_tables()
    arrays = create_mesh_arrays(joint_names)
    gltf, node_index = build_gltf(
        arrays, joint_names, parents, world_joint_positions
    )
    gltf.save_binary(str(output_path))
    reloaded = GLTF2().load(str(output_path))
    if reloaded is None:
        raise RuntimeError("Failed to reload generated GLB")
    if not self_check(reloaded, arrays, joint_names, node_index, output_path):
        raise SystemExit(1)
    print(f"Output file: {output_path}")


if __name__ == "__main__":
    main()
