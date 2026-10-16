#!/usr/bin/env python3
"""Generate a low-poly Achaemenid Persian spear as a binary glTF 2.0 file."""
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
# The skeleton rest pose is shared with the Persian tunic, cap, and shield.
# -----------------------------------------------------------------------------
OUTPUT_NAME = "gear_spear_persian.glb"
MAX_TRIANGLES = 150

# Spear axis passes through the shared hand.R joint at rest.
SHAFT_X = 0.595
SHAFT_Z = 0.000
GRIP_CENTER_Y = 1.015
SHAFT_RADIUS = 0.0105
SHAFT_BOTTOM_Y = 0.085
SHAFT_TOP_Y = 1.690
SHAFT_SEGMENTS = 8

# Iron counterweight spike at the butt, spanning exactly Y=0.000..0.140 m.
BUTT_TIP_Y = 0.000
BUTT_BASE_Y = 0.140
BUTT_BASE_RADIUS = 0.018
BUTT_SEGMENTS = 8

# Iron socket near the spearhead. It overlaps the wooden shaft for a closed fit.
SOCKET_BOTTOM_Y = 1.610
SOCKET_TOP_Y = 1.700
SOCKET_RADIUS = 0.0145
SOCKET_SEGMENTS = 8

# Leaf-shaped spearhead outline in local X/Y, then translated to SHAFT_X.
# Total spear length from the butt point to the leaf tip is 1.800 m.
HEAD_TIP_Y = 1.800
HEAD_CENTER_Y = 1.705
HEAD_EDGE_Z_HALF_THICKNESS = 0.003
HEAD_RIDGE_Z_OFFSET = 0.006
HEAD_OUTLINE_XY = (
    (0.000, 1.800),
    (0.012, 1.770),
    (0.025, 1.735),
    (0.038, 1.710),
    (0.031, 1.687),
    (0.018, 1.655),
    (0.010, 1.625),
    (-0.010, 1.625),
    (-0.018, 1.655),
    (-0.031, 1.687),
    (-0.038, 1.710),
    (-0.025, 1.735),
    (-0.012, 1.770),
)

# Vertex colors: brown wooden shaft and grey iron head/socket/butt spike.
COLOR_WOOD = (112 / 255.0, 72 / 255.0, 42 / 255.0, 1.0)  # #70482A
COLOR_IRON = (133 / 255.0, 139 / 255.0, 143 / 255.0, 1.0)  # #858B8F

# EXACT shared rest-pose joint locations, in meters, from the other gear pieces.
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
        name: np.asarray(position, dtype=np.float64)
        for name, position in CENTRAL_JOINT_POSITIONS.items()
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

    # Guard against accidentally drifting away from the shared attachment pose.
    if not np.allclose(
        world["hand.R"], (SHAFT_X, GRIP_CENTER_Y, SHAFT_Z), atol=1e-8
    ):
        raise ValueError("Spear grip does not coincide with the shared hand.R joint")
    return names, parents, world


class MeshBuilder:
    """Triangle-soup builder with flat normals and rigid hand.R skinning."""

    def __init__(self, joint_names):
        self.joint_index = {name: i for i, name in enumerate(joint_names)}
        self.bound_joint = self.joint_index["hand.R"]
        self.positions = []
        self.normals = []
        self.colors = []
        self.joints = []
        self.weights = []
        self.triangle_count = 0

    def triangle(self, a, b, c, color, expected_normal):
        points = [np.asarray(p, dtype=np.float64) for p in (a, b, c)]
        raw = np.cross(points[1] - points[0], points[2] - points[0])
        area2 = float(np.linalg.norm(raw))
        if not math.isfinite(area2) or area2 < 1e-9:
            raise ValueError(f"Degenerate triangle detected: {points}")

        expected = normalize(np.asarray(expected_normal, dtype=np.float64))
        if float(np.dot(raw, expected)) < 0.0:
            points[1], points[2] = points[2], points[1]
            raw = np.cross(points[1] - points[0], points[2] - points[0])
            area2 = float(np.linalg.norm(raw))
            if area2 < 1e-9:
                raise ValueError("Triangle became degenerate after winding correction")

        normal = (raw / area2).astype(np.float32)
        for point in points:
            self.positions.append(point.astype(np.float32))
            self.normals.append(normal)
            self.colors.append(np.asarray(color, dtype=np.float32))
            # All four slots are valid joint indices; the first carries weight 1.
            self.joints.append((self.bound_joint,) * 4)
            self.weights.append((1.0, 0.0, 0.0, 0.0))
        self.triangle_count += 1

    def quad(self, a, b, c, d, color, expected_normal):
        self.triangle(a, b, c, color, expected_normal)
        self.triangle(a, c, d, color, expected_normal)

    def arrays(self):
        return {
            "positions": np.asarray(self.positions, dtype="<f4"),
            "normals": np.asarray(self.normals, dtype="<f4"),
            "colors": np.asarray(self.colors, dtype="<f4"),
            "joints": np.asarray(self.joints, dtype=np.uint8),
            "weights": np.asarray(self.weights, dtype="<f4"),
            # Vertices are duplicated for flat shading but the primitive is indexed.
            "indices": np.arange(len(self.positions), dtype="<u2"),
            "triangle_count": self.triangle_count,
        }


def point(x, y, z):
    return np.asarray((SHAFT_X + x, y, SHAFT_Z + z), dtype=np.float64)


def add_cylinder_y(builder, y0, y1, radius, segments, color):
    """Closed cylinder aligned to Y, centered on the spear shaft axis."""
    low, high = [], []
    for i in range(segments):
        angle = 2.0 * math.pi * i / segments
        x, z = radius * math.cos(angle), radius * math.sin(angle)
        low.append(point(x, y0, z))
        high.append(point(x, y1, z))

    for i in range(segments):
        j = (i + 1) % segments
        angle = 2.0 * math.pi * (i + 0.5) / segments
        builder.quad(
            low[i], low[j], high[j], high[i], color,
            (math.cos(angle), 0.0, math.sin(angle)),
        )

    bottom_center = point(0.0, y0, 0.0)
    top_center = point(0.0, y1, 0.0)
    for i in range(segments):
        j = (i + 1) % segments
        builder.triangle(
            bottom_center, low[j], low[i], color, (0.0, -1.0, 0.0)
        )
        builder.triangle(
            top_center, high[i], high[j], color, (0.0, 1.0, 0.0)
        )


def add_butt_spike(builder):
    """Closed tapered iron counterweight spike, tip down at Y=0."""
    ring = []
    for i in range(BUTT_SEGMENTS):
        angle = 2.0 * math.pi * i / BUTT_SEGMENTS
        ring.append(point(
            BUTT_BASE_RADIUS * math.cos(angle),
            BUTT_BASE_Y,
            BUTT_BASE_RADIUS * math.sin(angle),
        ))

    tip = point(0.0, BUTT_TIP_Y, 0.0)
    base_center = point(0.0, BUTT_BASE_Y, 0.0)
    for i in range(BUTT_SEGMENTS):
        j = (i + 1) % BUTT_SEGMENTS
        angle = 2.0 * math.pi * (i + 0.5) / BUTT_SEGMENTS
        builder.triangle(
            ring[i], ring[j], tip, COLOR_IRON,
            (math.cos(angle), 0.2, math.sin(angle)),
        )
        builder.triangle(
            base_center, ring[j], ring[i], COLOR_IRON, (0.0, 1.0, 0.0)
        )


def add_spearhead(builder):
    """Closed, faceted leaf blade with a raised central ridge."""
    outline = list(HEAD_OUTLINE_XY)
    front_edge = [
        point(x, y, HEAD_EDGE_Z_HALF_THICKNESS)
        for x, y in outline
    ]
    back_edge = [
        point(x, y, -HEAD_EDGE_Z_HALF_THICKNESS)
        for x, y in outline
    ]
    front_ridge = point(0.0, HEAD_CENTER_Y, HEAD_RIDGE_Z_OFFSET)
    back_ridge = point(0.0, HEAD_CENTER_Y, -HEAD_RIDGE_Z_OFFSET)

    count = len(outline)
    for i in range(count):
        j = (i + 1) % count
        # Front and back surfaces face +Z and -Z respectively.
        builder.triangle(
            front_ridge, front_edge[i], front_edge[j],
            COLOR_IRON, (0.0, 0.0, 1.0),
        )
        builder.triangle(
            back_ridge, back_edge[j], back_edge[i],
            COLOR_IRON, (0.0, 0.0, -1.0),
        )

        midpoint_x = 0.5 * (outline[i][0] + outline[j][0])
        midpoint_y = 0.5 * (outline[i][1] + outline[j][1])
        radial = (midpoint_x, midpoint_y - HEAD_CENTER_Y, 0.0)
        builder.quad(
            front_edge[i], front_edge[j], back_edge[j], back_edge[i],
            COLOR_IRON, radial,
        )


def create_mesh_arrays(joint_names):
    builder = MeshBuilder(joint_names)
    add_cylinder_y(
        builder, SHAFT_BOTTOM_Y, SHAFT_TOP_Y,
        SHAFT_RADIUS, SHAFT_SEGMENTS, COLOR_WOOD,
    )
    add_butt_spike(builder)
    add_cylinder_y(
        builder, SOCKET_BOTTOM_Y, SOCKET_TOP_Y,
        SOCKET_RADIUS, SOCKET_SEGMENTS, COLOR_IRON,
    )
    add_spearhead(builder)
    return builder.arrays()


def make_nodes_and_indices(joint_names, parents, world_positions):
    node_index = {name: i + 1 for i, name in enumerate(joint_names)}
    children = {name: [] for name in joint_names}
    for name, parent in parents.items():
        if parent is not None:
            children[parent].append(name)

    # Mesh vertices already use the shared body's model-space coordinates.
    nodes = [Node(name="gear_spear_persian_mesh", mesh=0, skin=0)]
    for name in joint_names:
        parent = parents[name]
        parent_pos = (
            np.zeros(3, dtype=np.float64)
            if parent is None else world_positions[parent]
        )
        local = world_positions[name] - parent_pos
        nodes.append(Node(
            name=name,
            translation=[float(x) for x in local],
            children=[node_index[child] for child in children[name]],
        ))
    return nodes, node_index


def inverse_bind_matrices(joint_names, world_positions):
    # Mesh node has identity transform and vertices are in model space.
    # Thus each inverse bind matrix is the inverse joint rest-world transform.
    matrices = []
    for name in joint_names:
        matrix = np.eye(4, dtype=np.float64)
        matrix[:3, 3] = -world_positions[name]
        matrices.append(matrix.reshape(16, order="F"))
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


def add_accessor(
    gltf, view, component_type, count, kind,
    minimum=None, maximum=None, normalized=False,
):
    gltf.accessors.append(Accessor(
        bufferView=view,
        byteOffset=0,
        componentType=component_type,
        count=count,
        type=kind,
        min=None if minimum is None else [float(v) for v in minimum],
        max=None if maximum is None else [float(v) for v in maximum],
        normalized=normalized,
    ))
    return len(gltf.accessors) - 1


def build_gltf(arrays, joint_names, parents, world_positions):
    gltf = GLTF2()
    gltf.asset = Asset(
        version="2.0",
        generator="Procedural Achaemenid Persian Spear Generator",
    )
    gltf.asset.extras = {
        "units": "meters",
        "upAxis": "Y",
        "forwardAxis": "+Z",
        "gripJoint": "hand.R",
        "animationRequested": "none",
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
    indices_view = append_array(
        gltf, blob, arrays["indices"], ELEMENT_ARRAY_BUFFER
    )
    ibm_view = append_array(
        gltf, blob, inverse_bind_matrices(joint_names, world_positions)
    )
    while len(blob) % 4:
        blob.append(0)

    vertex_count = len(arrays["positions"])
    pos_acc = add_accessor(
        gltf, pos_view, COMPONENT_FLOAT, vertex_count, "VEC3",
        arrays["positions"].min(axis=0), arrays["positions"].max(axis=0),
    )
    norm_acc = add_accessor(
        gltf, norm_view, COMPONENT_FLOAT, vertex_count, "VEC3"
    )
    color_acc = add_accessor(
        gltf, color_view, COMPONENT_FLOAT, vertex_count, "VEC4"
    )
    joints_acc = add_accessor(
        gltf, joints_view, COMPONENT_UNSIGNED_BYTE, vertex_count, "VEC4"
    )
    weights_acc = add_accessor(
        gltf, weights_view, COMPONENT_FLOAT, vertex_count, "VEC4"
    )
    indices_acc = add_accessor(
        gltf, indices_view, COMPONENT_UNSIGNED_SHORT,
        len(arrays["indices"]), "SCALAR",
        [0], [int(arrays["indices"].max())],
    )
    ibm_acc = add_accessor(
        gltf, ibm_view, COMPONENT_FLOAT, len(joint_names), "MAT4"
    )

    gltf.materials = [Material(
        name="wood_and_iron_vertex_colors",
        pbrMetallicRoughness=PbrMetallicRoughness(
            baseColorFactor=[1.0, 1.0, 1.0, 1.0],
            metallicFactor=0.0,
            roughnessFactor=1.0,
        ),
        doubleSided=False,
    )]
    gltf.meshes = [Mesh(
        name="gear_spear_persian",
        primitives=[Primitive(
            attributes=Attributes(
                POSITION=pos_acc,
                NORMAL=norm_acc,
                COLOR_0=color_acc,
                JOINTS_0=joints_acc,
                WEIGHTS_0=weights_acc,
            ),
            indices=indices_acc,
            material=0,
            mode=TRIANGLES,
        )],
    )]
    gltf.skins = [Skin(
        name="humanoid_26_joint_skin",
        inverseBindMatrices=ibm_acc,
        skeleton=node_index["root"],
        joints=[node_index[name] for name in joint_names],
    )]
    gltf.animations = []
    gltf.buffers = [Buffer(byteLength=len(blob))]
    gltf.set_binary_blob(bytes(blob))
    return gltf, node_index


def self_check(gltf, arrays, joint_names, node_index, output_path):
    positions = arrays["positions"]
    weights, joints = arrays["weights"], arrays["joints"]
    triangle_count = int(arrays["triangle_count"])
    low, high = positions.min(axis=0), positions.max(axis=0)
    primitive = gltf.meshes[0].primitives[0] if gltf.meshes else None
    attrs = primitive.attributes if primitive is not None else None
    hand_index = joint_names.index("hand.R")

    tri_points = positions.reshape(-1, 3, 3)
    area2 = np.linalg.norm(
        np.cross(
            tri_points[:, 1] - tri_points[:, 0],
            tri_points[:, 2] - tri_points[:, 0],
        ), axis=1,
    )
    normals = arrays["normals"]
    head_vertices = positions[:, 1] >= 1.625

    checks = {
        "one_mesh": len(gltf.meshes) == 1,
        "one_primitive": (
            len(gltf.meshes) == 1 and len(gltf.meshes[0].primitives) == 1
        ),
        "one_scene": len(gltf.scenes) == 1 and gltf.scene == 0,
        "joint_limit": len(gltf.skins[0].joints) <= 64,
        "exact_26_joint_names": (
            len(joint_names) == 26
            and len(set(joint_names)) == 26
            and all(gltf.nodes[node_index[name]].name == name for name in joint_names)
        ),
        "four_joint_slots": (
            joints.shape == (len(positions), 4) and weights.shape == joints.shape
        ),
        "100_percent_hand_R_weight": (
            np.all(joints == hand_index)
            and np.all(np.abs(weights[:, 0] - 1.0) <= 1e-6)
            and np.all(np.abs(weights[:, 1:]) <= 1e-6)
        ),
        "weights_sum_to_one": bool(
            np.all(np.abs(weights.sum(axis=1) - 1.0) <= 0.001)
        ),
        "no_nans": all(np.isfinite(arrays[key]).all() for key in (
            "positions", "normals", "colors", "joints", "weights", "indices"
        )),
        "normals_present": attrs is not None and attrs.NORMAL is not None,
        "no_textures_or_uvs": (
            not getattr(gltf, "textures", None)
            and not getattr(gltf, "images", None)
            and attrs is not None
            and getattr(attrs, "TEXCOORD_0", None) is None
        ),
        "indexed_triangles": (
            primitive is not None
            and primitive.indices is not None
            and primitive.mode == TRIANGLES
            and len(arrays["indices"]) % 3 == 0
            and len(arrays["indices"]) // 3 == triangle_count
        ),
        "no_degenerate_triangles": bool(np.all(area2 > 1e-9)),
        "y_up": gltf.asset.extras.get("upAxis") == "Y",
        "faces_positive_z": (
            gltf.asset.extras.get("forwardAxis") == "+Z"
            and np.any(head_vertices & (normals[:, 2] > 0.1))
        ),
        "lowest_vertex_at_y_zero": abs(float(low[1]) - 0.0) <= 1e-5,
        "spear_length_1_8m": abs(float(high[1] - low[1]) - 1.8) <= 1e-4,
        "grip_at_hand_R": (
            abs(SHAFT_X - SIDE_JOINT_POSITIONS["hand"][0]) < 1e-6
            and abs(GRIP_CENTER_Y - SIDE_JOINT_POSITIONS["hand"][1]) < 1e-6
            and abs(SHAFT_Z - SIDE_JOINT_POSITIONS["hand"][2]) < 1e-6
        ),
        "triangle_budget": triangle_count <= MAX_TRIANGLES,
        "no_animations": len(gltf.animations) == 0,
        "file_written": output_path.exists() and output_path.stat().st_size > 0,
    }

    print(f"Triangle count: {triangle_count}")
    print(f"Joint count: {len(gltf.skins[0].joints)}")
    print("Bounding box min (m): " + np.array2string(low, precision=4, separator=", "))
    print("Bounding box max (m): " + np.array2string(high, precision=4, separator=", "))
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
    joint_names, parents, world_positions = make_joint_tables()
    arrays = create_mesh_arrays(joint_names)
    gltf, node_index = build_gltf(
        arrays, joint_names, parents, world_positions
    )
    gltf.save_binary(str(output_path))

    # Reload the file so checks exercise the serialized GLB, not just source data.
    reloaded = GLTF2().load(str(output_path))
    if reloaded is None:
        raise RuntimeError("Failed to reload the generated GLB")
    if not self_check(reloaded, arrays, joint_names, node_index, output_path):
        raise SystemExit(1)
    print(f"Output file: {output_path}")


if __name__ == "__main__":
    main()
