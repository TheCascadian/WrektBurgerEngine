#!/usr/bin/env python3
"""Generate a low-poly, skinned Achaemenid Persian infantry tunic as a GLB."""
from __future__ import annotations

from pathlib import Path
import math
import sys

import numpy as np
from pygltflib import (
    Accessor,
    Asset,
    Attributes,
    Buffer,
    BufferView,
    GLTF2,
    Material,
    Mesh,
    Node,
    PbrMetallicRoughness,
    Primitive,
    Scene,
    Skin,
)

# -----------------------------------------------------------------------------
# Tunable constants. All dimensions are meters; colors are linear-ish sRGB values
# stored as glTF vertex-color RGBA. The model is authored at a 1.80 m body scale.
# -----------------------------------------------------------------------------
OUTPUT_NAME = "gear_tunic_persian.glb"
MAX_TRIANGLES = 700

COLOR_SAFFRON = (217 / 255.0, 154 / 255.0, 34 / 255.0, 1.0)  # #D99A22
COLOR_BELT = (113 / 255.0, 58 / 255.0, 43 / 255.0, 1.0)      # #713A2B

# Tunic ring profiles: (height, half-width in X, half-depth in Z), bottom to top.
# The last ring forms a narrow neckline under the character's neck.
TUNIC_RINGS = (
    (0.570, 0.285, 0.145),
    (0.700, 0.276, 0.142),
    (0.850, 0.253, 0.130),
    (0.970, 0.239, 0.123),
    (1.130, 0.270, 0.140),
    (1.300, 0.315, 0.151),
    (1.390, 0.292, 0.139),
    (1.470, 0.090, 0.076),
)
TUNIC_RING_SEGMENTS = 12

# Belt dimensions: Y limits, then outer/inner elliptical radii (half-width X, half-depth Z).
BELT_Y_BOTTOM = 0.960
BELT_Y_TOP = 1.020
BELT_OUTER_RADII = (0.257, 0.141)
BELT_INNER_RADII = (0.242, 0.126)
BELT_SEGMENTS = 16

# Sleeve rings: (absolute center X, center Y, cross-section radius 1, radius 2).
# The same pattern is mirrored for .L and .R. Ends sit at the wrists.
SLEEVE_RINGS = (
    (0.205, 1.405, 0.086, 0.076),
    (0.300, 1.310, 0.077, 0.068),
    (0.400, 1.210, 0.067, 0.060),
    (0.500, 1.110, 0.059, 0.053),
    (0.595, 1.015, 0.052, 0.048),
)
SLEEVE_SEGMENTS = 8

# Trouser rings: (height, absolute center X, half-width in X, half-depth in Z),
# top to bottom. The hem terminates close to the ankle, above the Y=0 ground plane.
TROUSER_RINGS = (
    (0.880, 0.102, 0.082, 0.088),
    (0.700, 0.108, 0.079, 0.084),
    (0.530, 0.112, 0.066, 0.070),
    (0.320, 0.116, 0.052, 0.058),
    (0.035, 0.120, 0.043, 0.048),
)
TROUSER_SEGMENTS = 8

# Skeleton rest pose positions in world/model space. Y-up, forward +Z.
# The body head top is approximately 1.80 m; this gear mesh intentionally ends
# at the neckline and ankle hems rather than modeling any body parts.
CENTRAL_JOINT_POSITIONS = {
    "root": (0.000, 0.000, 0.000),
    "hips": (0.000, 0.920, 0.000),
    "spine": (0.000, 1.140, 0.000),
    "chest": (0.000, 1.400, 0.000),
    "neck": (0.000, 1.540, 0.000),
    "head": (0.000, 1.660, 0.000),
}
# Per-side absolute X, Y, Z. X is mirrored for .L and .R.
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

# No animations are requested.
ANIMATION_NAMES = ()
ANIMATION_TIMINGS = {}  # Clip name -> duration in seconds; intentionally empty.

# glTF constants.
COMPONENT_TYPE_UNSIGNED_SHORT = 5123
COMPONENT_TYPE_UNSIGNED_BYTE = 5121
COMPONENT_TYPE_FLOAT = 5126
BUFFER_TARGET_ARRAY_BUFFER = 34962
BUFFER_TARGET_ELEMENT_ARRAY_BUFFER = 34963
PRIMITIVE_MODE_TRIANGLES = 4


# -----------------------------------------------------------------------------
# Mesh construction helpers.
# -----------------------------------------------------------------------------
def normalize(vector: np.ndarray) -> np.ndarray:
    length = float(np.linalg.norm(vector))
    if length <= 1e-12:
        raise ValueError("Cannot normalize a zero-length vector")
    return vector / length


def make_joint_tables():
    joint_names = ["root", "hips", "spine", "chest", "neck", "head"]
    parents = {
        "root": None,
        "hips": "root",
        "spine": "hips",
        "chest": "spine",
        "neck": "chest",
        "head": "neck",
    }
    world_positions = {name: np.array(pos, dtype=np.float64)
                       for name, pos in CENTRAL_JOINT_POSITIONS.items()}

    for side in SIDES:
        sign = -1.0 if side == "L" else 1.0
        for part in ("shoulder", "upper_arm", "forearm", "hand", "fingers", "thumb",
                     "thigh", "shin", "foot", "toe"):
            name = f"{part}.{side}"
            x_abs, y, z = SIDE_JOINT_POSITIONS[part]
            world_positions[name] = np.array((sign * x_abs, y, z), dtype=np.float64)
            joint_names.append(name)

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

    return joint_names, parents, world_positions


class MeshBuilder:
    """Accumulate non-shared triangle vertices for deliberately flat normals."""
    def __init__(self, joint_index: dict[str, int]):
        self.joint_index = joint_index
        self.positions = []
        self.normals = []
        self.colors = []
        self.joints = []
        self.weights = []
        self.triangle_count = 0

    def vertex(self, position, color, influences):
        return (np.asarray(position, dtype=np.float64), tuple(color), influences)

    def pack_weights(self, influences):
        cleaned = [(name, float(weight)) for name, weight in influences if weight > 1e-8]
        if not cleaned:
            cleaned = [("root", 1.0)]
        cleaned.sort(key=lambda item: item[1], reverse=True)
        cleaned = cleaned[:4]
        total = sum(weight for _, weight in cleaned)
        if total <= 0.0:
            raise ValueError("Vertex has no positive skin weight")
        cleaned = [(name, weight / total) for name, weight in cleaned]

        indices = [self.joint_index[name] for name, _ in cleaned]
        weights = [weight for _, weight in cleaned]
        while len(indices) < 4:
            indices.append(self.joint_index["root"])
            weights.append(0.0)
        weights = np.asarray(weights, dtype=np.float64)
        weights /= float(weights.sum())
        return indices, weights

    def triangle(self, a, b, c, expected_normal):
        verts = [a, b, c]
        pa, pb, pc = (verts[0][0], verts[1][0], verts[2][0])
        raw_normal = np.cross(pb - pa, pc - pa)
        area2 = float(np.linalg.norm(raw_normal))
        if not math.isfinite(area2) or area2 < 1e-9:
            raise ValueError(f"Degenerate triangle detected at {pa}, {pb}, {pc}")

        expected = normalize(np.asarray(expected_normal, dtype=np.float64))
        if float(np.dot(raw_normal, expected)) < 0.0:
            verts[1], verts[2] = verts[2], verts[1]
            pa, pb, pc = (verts[0][0], verts[1][0], verts[2][0])
            raw_normal = np.cross(pb - pa, pc - pa)
            area2 = float(np.linalg.norm(raw_normal))
        normal = raw_normal / area2

        for position, color, influences in verts:
            joint_indices, joint_weights = self.pack_weights(influences)
            self.positions.append(position.astype(np.float32))
            self.normals.append(normal.astype(np.float32))
            self.colors.append(np.asarray(color, dtype=np.float32))
            self.joints.append(np.asarray(joint_indices, dtype=np.uint8))
            self.weights.append(joint_weights.astype(np.float32))
        self.triangle_count += 1

    def quad(self, a, b, c, d, expected_normal):
        self.triangle(a, b, c, expected_normal)
        self.triangle(a, c, d, expected_normal)

    def arrays(self):
        return {
            "positions": np.asarray(self.positions, dtype=np.float32),
            "normals": np.asarray(self.normals, dtype=np.float32),
            "colors": np.asarray(self.colors, dtype=np.float32),
            "joints": np.asarray(self.joints, dtype=np.uint8),
            "weights": np.asarray(self.weights, dtype=np.float32),
        }


def ellipse_ring(builder, center, basis_u, basis_v, radius_u, radius_v, segments,
                 color, influences):
    center = np.asarray(center, dtype=np.float64)
    basis_u = normalize(np.asarray(basis_u, dtype=np.float64))
    basis_v = normalize(np.asarray(basis_v, dtype=np.float64))
    ring = []
    for i in range(segments):
        angle = (2.0 * math.pi * i) / segments
        position = (center
                    + basis_u * (radius_u * math.cos(angle))
                    + basis_v * (radius_v * math.sin(angle)))
        ring.append(builder.vertex(position, color, influences))
    return ring


def add_tube(builder, rings, segments, color, cap_bottom=True, cap_top=True):
    """Add a closed tube. Each ring provides center, basis vectors, radii, weights."""
    built_rings = []
    for ring in rings:
        built_rings.append(ellipse_ring(
            builder, ring["center"], ring["u"], ring["v"], ring["r1"], ring["r2"],
            segments, color, ring["weights"],
        ))

    for ring_index in range(len(built_rings) - 1):
        low = built_rings[ring_index]
        high = built_rings[ring_index + 1]
        low_info = rings[ring_index]
        high_info = rings[ring_index + 1]
        center_low = np.asarray(low_info["center"], dtype=np.float64)
        center_high = np.asarray(high_info["center"], dtype=np.float64)
        center_axis = center_high - center_low
        for i in range(segments):
            j = (i + 1) % segments
            angle_mid = (2.0 * math.pi * (i + 0.5)) / segments
            radial = normalize(
                np.asarray(low_info["u"]) * math.cos(angle_mid)
                + np.asarray(low_info["v"]) * math.sin(angle_mid)
            )
            # Expected radial direction determines outward winding; the projection
            # along the tube axis does not alter which side is the exterior.
            if np.linalg.norm(center_axis) > 1e-9:
                radial = radial - center_axis * (np.dot(radial, center_axis) /
                                                   max(np.dot(center_axis, center_axis), 1e-12))
                if np.linalg.norm(radial) > 1e-9:
                    radial = normalize(radial)
            builder.quad(low[i], low[j], high[j], high[i], radial)

    def cap(ring_index, sign):
        ring = built_rings[ring_index]
        info = rings[ring_index]
        center = builder.vertex(info["center"], color, info["weights"])
        normal = normalize(np.asarray(info["u"]) * 0.0 + np.asarray(info["v"]) * 0.0
                           + np.asarray(info.get("cap_normal", (0.0, sign, 0.0)), dtype=np.float64))
        for i in range(segments):
            j = (i + 1) % segments
            builder.triangle(center, ring[j], ring[i], normal)

    if cap_bottom:
        cap(0, -1.0)
    if cap_top:
        cap(len(built_rings) - 1, 1.0)


def add_tunic(builder):
    rings = []
    for idx, (y, half_width, half_depth) in enumerate(TUNIC_RINGS):
        if idx <= 1:
            weights = [("hips", 0.88), ("spine", 0.12)]
        elif idx == 2:
            weights = [("hips", 0.68), ("spine", 0.32)]
        elif idx == 3:
            weights = [("hips", 0.48), ("spine", 0.52)]
        elif idx == 4:
            weights = [("spine", 0.62), ("chest", 0.38)]
        elif idx == 5:
            weights = [("spine", 0.30), ("chest", 0.70)]
        elif idx == 6:
            weights = [("chest", 0.90), ("spine", 0.10)]
        else:
            weights = [("chest", 0.80), ("neck", 0.20)]
        rings.append({
            "center": (0.0, y, 0.0),
            "u": (1.0, 0.0, 0.0),
            "v": (0.0, 0.0, 1.0),
            "r1": half_width,
            "r2": half_depth,
            "weights": weights,
            "cap_normal": (0.0, -1.0 if idx == 0 else 1.0, 0.0),
        })
    add_tube(builder, rings, TUNIC_RING_SEGMENTS, COLOR_SAFFRON, True, True)


def add_belt(builder):
    n = BELT_SEGMENTS
    outer_bottom = ellipse_ring(
        builder, (0.0, BELT_Y_BOTTOM, 0.0), (1, 0, 0), (0, 0, 1),
        *BELT_OUTER_RADII, n, COLOR_BELT, [("hips", 0.65), ("spine", 0.35)],
    )
    outer_top = ellipse_ring(
        builder, (0.0, BELT_Y_TOP, 0.0), (1, 0, 0), (0, 0, 1),
        *BELT_OUTER_RADII, n, COLOR_BELT,
        [("spine", 0.65), ("hips", 0.20), ("chest", 0.15)],
    )
    inner_bottom = ellipse_ring(
        builder, (0.0, BELT_Y_BOTTOM, 0.0), (1, 0, 0), (0, 0, 1),
        *BELT_INNER_RADII, n, COLOR_BELT, [("hips", 0.65), ("spine", 0.35)],
    )
    inner_top = ellipse_ring(
        builder, (0.0, BELT_Y_TOP, 0.0), (1, 0, 0), (0, 0, 1),
        *BELT_INNER_RADII, n, COLOR_BELT,
        [("spine", 0.65), ("hips", 0.20), ("chest", 0.15)],
    )

    for i in range(n):
        j = (i + 1) % n
        angle_mid = (2 * math.pi * (i + 0.5)) / n
        radial = np.array((math.cos(angle_mid), 0.0, math.sin(angle_mid)))
        builder.quad(outer_bottom[i], outer_bottom[j], outer_top[j], outer_top[i], radial)
        builder.quad(inner_bottom[j], inner_bottom[i], inner_top[i], inner_top[j], -radial)
        builder.quad(outer_top[i], outer_top[j], inner_top[j], inner_top[i], (0, 1, 0))
        builder.quad(inner_bottom[i], inner_bottom[j], outer_bottom[j], outer_bottom[i], (0, -1, 0))


def add_sleeve(builder, side):
    sign = -1.0 if side == "L" else 1.0
    start = np.array((sign * SLEEVE_RINGS[0][0], SLEEVE_RINGS[0][1], 0.0))
    end = np.array((sign * SLEEVE_RINGS[-1][0], SLEEVE_RINGS[-1][1], 0.0))
    axis = normalize(end - start)
    reference = np.array((0.0, 0.0, 1.0))
    basis_u = normalize(np.cross(axis, reference))
    basis_v = normalize(np.cross(axis, basis_u))

    weight_profiles = (
        [(f"shoulder.{side}", 0.65), ("chest", 0.35)],
        [(f"upper_arm.{side}", 0.90), (f"shoulder.{side}", 0.10)],
        [(f"upper_arm.{side}", 0.65), (f"forearm.{side}", 0.35)],
        [(f"upper_arm.{side}", 0.20), (f"forearm.{side}", 0.80)],
        [(f"forearm.{side}", 0.80), (f"hand.{side}", 0.20)],
    )
    rings = []
    for idx, (x_abs, y, radius1, radius2) in enumerate(SLEEVE_RINGS):
        rings.append({
            "center": (sign * x_abs, y, 0.0),
            "u": basis_u,
            "v": basis_v,
            "r1": radius1,
            "r2": radius2,
            "weights": weight_profiles[idx],
            "cap_normal": (-axis if idx == 0 else axis),
        })
    add_tube(builder, rings, SLEEVE_SEGMENTS, COLOR_SAFFRON, True, True)


def add_trouser_leg(builder, side):
    sign = -1.0 if side == "L" else 1.0
    weight_profiles = (
        [("hips", 0.30), (f"thigh.{side}", 0.70)],
        [(f"thigh.{side}", 0.95), ("hips", 0.05)],
        [(f"thigh.{side}", 0.55), (f"shin.{side}", 0.45)],
        [(f"thigh.{side}", 0.15), (f"shin.{side}", 0.85)],
        [(f"shin.{side}", 0.85), (f"foot.{side}", 0.15)],
    )
    rings = []
    for idx, (y, x_abs, half_width, half_depth) in enumerate(TROUSER_RINGS):
        rings.append({
            "center": (sign * x_abs, y, 0.0),
            "u": (1.0, 0.0, 0.0),
            "v": (0.0, 0.0, 1.0),
            "r1": half_width,
            "r2": half_depth,
            "weights": weight_profiles[idx],
            "cap_normal": (0.0, -1.0 if idx == len(TROUSER_RINGS) - 1 else 1.0, 0.0),
        })
    # Rings are specified top-to-bottom; cap-normal metadata is corrected below
    # so top caps face upward and bottom caps face downward.
    rings[0]["cap_normal"] = (0.0, 1.0, 0.0)
    rings[-1]["cap_normal"] = (0.0, -1.0, 0.0)
    add_tube(builder, rings, TROUSER_SEGMENTS, COLOR_SAFFRON, True, True)


def create_mesh_arrays(joint_names):
    joint_index = {name: idx for idx, name in enumerate(joint_names)}
    builder = MeshBuilder(joint_index)
    add_tunic(builder)
    add_belt(builder)
    for side in SIDES:
        add_sleeve(builder, side)
    for side in SIDES:
        add_trouser_leg(builder, side)
    arrays = builder.arrays()
    arrays["indices"] = np.arange(len(arrays["positions"]), dtype=np.uint16)
    arrays["triangle_count"] = builder.triangle_count
    return arrays


# -----------------------------------------------------------------------------
# glTF assembly and file serialization.
# -----------------------------------------------------------------------------
def create_skeleton_nodes(joint_names, parents, world_positions, mesh_node_index=0):
    node_index = {name: mesh_node_index + 1 + i for i, name in enumerate(joint_names)}
    child_map = {name: [] for name in joint_names}
    for name, parent in parents.items():
        if parent is not None:
            child_map[parent].append(name)

    nodes = []
    for name in joint_names:
        parent = parents[name]
        parent_position = (np.zeros(3, dtype=np.float64) if parent is None
                           else world_positions[parent])
        local = world_positions[name] - parent_position
        nodes.append(Node(
            name=name,
            translation=[float(local[0]), float(local[1]), float(local[2])],
            children=[node_index[child] for child in child_map[name]],
        ))
    return nodes, node_index


def inverse_bind_matrices(world_positions, joint_names):
    matrices = []
    for name in joint_names:
        matrix = np.eye(4, dtype=np.float32)
        matrix[0:3, 3] = -np.asarray(world_positions[name], dtype=np.float32)
        # glTF MAT4 values are serialized column-major.
        matrices.append(matrix.T.reshape(16))
    return np.asarray(matrices, dtype=np.float32)


def append_binary_array(gltf, binary_blob, array, target=None):
    while len(binary_blob) % 4:
        binary_blob.append(0)
    offset = len(binary_blob)
    raw = np.ascontiguousarray(array).tobytes()
    binary_blob.extend(raw)
    view = BufferView(buffer=0, byteOffset=offset, byteLength=len(raw), target=target)
    gltf.bufferViews.append(view)
    return len(gltf.bufferViews) - 1


def add_accessor(gltf, buffer_view, component_type, count, accessor_type,
                 minimum=None, maximum=None, normalized=False):
    accessor = Accessor(
        bufferView=buffer_view,
        byteOffset=0,
        componentType=component_type,
        count=count,
        type=accessor_type,
        min=None if minimum is None else [float(v) for v in minimum],
        max=None if maximum is None else [float(v) for v in maximum],
        normalized=normalized,
    )
    gltf.accessors.append(accessor)
    return len(gltf.accessors) - 1


def build_gltf(arrays, joint_names, parents, world_positions):
    gltf = GLTF2()
    gltf.asset = Asset(version="2.0", generator="Procedural Persian Tunic Generator")
    gltf.asset.extras = {
        "units": "meters",
        "upAxis": "Y",
        "forwardAxis": "+Z",
        "animationsRequested": "none",
    }
    gltf.scene = 0
    gltf.scenes = [Scene(name="Scene", nodes=[0, 1])]

    skeleton_nodes, node_index = create_skeleton_nodes(
        joint_names, parents, world_positions, mesh_node_index=0
    )
    gltf.nodes = [Node(name="gear_tunic_persian_mesh", mesh=0, skin=0)] + skeleton_nodes

    # Binary data is stored in independent, 4-byte-aligned buffer views.
    binary_blob = bytearray()
    position_view = append_binary_array(gltf, binary_blob, arrays["positions"],
                                        BUFFER_TARGET_ARRAY_BUFFER)
    normal_view = append_binary_array(gltf, binary_blob, arrays["normals"],
                                      BUFFER_TARGET_ARRAY_BUFFER)
    color_view = append_binary_array(gltf, binary_blob, arrays["colors"],
                                     BUFFER_TARGET_ARRAY_BUFFER)
    joints_view = append_binary_array(gltf, binary_blob, arrays["joints"],
                                      BUFFER_TARGET_ARRAY_BUFFER)
    weights_view = append_binary_array(gltf, binary_blob, arrays["weights"],
                                       BUFFER_TARGET_ARRAY_BUFFER)
    indices_view = append_binary_array(gltf, binary_blob, arrays["indices"],
                                       BUFFER_TARGET_ELEMENT_ARRAY_BUFFER)
    ibm_array = inverse_bind_matrices(world_positions, joint_names)
    ibm_view = append_binary_array(gltf, binary_blob, ibm_array, None)
    while len(binary_blob) % 4:
        binary_blob.append(0)

    vertex_count = len(arrays["positions"])
    index_count = len(arrays["indices"])
    position_accessor = add_accessor(
        gltf, position_view, COMPONENT_TYPE_FLOAT, vertex_count, "VEC3",
        np.min(arrays["positions"], axis=0), np.max(arrays["positions"], axis=0),
    )
    normal_accessor = add_accessor(
        gltf, normal_view, COMPONENT_TYPE_FLOAT, vertex_count, "VEC3"
    )
    color_accessor = add_accessor(
        gltf, color_view, COMPONENT_TYPE_FLOAT, vertex_count, "VEC4"
    )
    joints_accessor = add_accessor(
        gltf, joints_view, COMPONENT_TYPE_UNSIGNED_BYTE, vertex_count, "VEC4"
    )
    weights_accessor = add_accessor(
        gltf, weights_view, COMPONENT_TYPE_FLOAT, vertex_count, "VEC4"
    )
    indices_accessor = add_accessor(
        gltf, indices_view, COMPONENT_TYPE_UNSIGNED_SHORT, index_count, "SCALAR",
        [int(np.min(arrays["indices"]))], [int(np.max(arrays["indices"]))],
    )
    ibm_accessor = add_accessor(
        gltf, ibm_view, COMPONENT_TYPE_FLOAT, len(joint_names), "MAT4"
    )

    gltf.materials = [Material(
        name="vertex_color_fabric",
        pbrMetallicRoughness=PbrMetallicRoughness(
            baseColorFactor=[1.0, 1.0, 1.0, 1.0],
            metallicFactor=0.0,
            roughnessFactor=1.0,
        ),
        doubleSided=False,
    )]
    gltf.meshes = [Mesh(name="gear_tunic_persian", primitives=[Primitive(
        attributes=Attributes(
            POSITION=position_accessor,
            NORMAL=normal_accessor,
            COLOR_0=color_accessor,
            JOINTS_0=joints_accessor,
            WEIGHTS_0=weights_accessor,
        ),
        indices=indices_accessor,
        material=0,
        mode=PRIMITIVE_MODE_TRIANGLES,
    )])]
    gltf.skins = [Skin(
        name="humanoid_26_joint_skin",
        inverseBindMatrices=ibm_accessor,
        skeleton=node_index["root"],
        joints=[node_index[name] for name in joint_names],
    )]
    gltf.animations = []
    gltf.buffers = [Buffer(byteLength=len(binary_blob))]
    gltf.set_binary_blob(bytes(binary_blob))
    return gltf, node_index


# -----------------------------------------------------------------------------
# Self-checks.
# -----------------------------------------------------------------------------
def self_check(gltf, arrays, joint_names, node_index, output_path):
    positions = arrays["positions"]
    joints = arrays["joints"]
    weights = arrays["weights"]
    triangles = int(arrays["triangle_count"])
    low = np.min(positions, axis=0)
    high = np.max(positions, axis=0)
    finite_arrays = (positions, arrays["normals"], arrays["colors"], joints,
                     weights, arrays["indices"])

    primitive = gltf.meshes[0].primitives[0] if gltf.meshes and gltf.meshes[0].primitives else None
    attributes = primitive.attributes if primitive is not None else None
    indices_accessor = (gltf.accessors[primitive.indices]
                        if primitive is not None and primitive.indices is not None else None)
    checks = {
        "one_mesh": len(gltf.meshes) == 1,
        "one_primitive": len(gltf.meshes) == 1 and len(gltf.meshes[0].primitives) == 1,
        "skinned_mesh": len(gltf.nodes) > 0 and gltf.nodes[0].mesh == 0
                         and gltf.nodes[0].skin == 0 and len(gltf.skins) == 1,
        "joint_limit": len(gltf.skins[0].joints) <= 64,
        "four_joint_slots": joints.ndim == 2 and joints.shape[1] == 4
                            and weights.shape == joints.shape,
        "weights_sum_to_one": bool(np.all(np.abs(weights.sum(axis=1) - 1.0) <= 0.001)),
        "no_nans": all(bool(np.isfinite(array).all()) for array in finite_arrays),
        "normals_present": attributes is not None and getattr(attributes, "NORMAL", None) is not None,
        "no_textures_or_uvs": not getattr(gltf, "textures", None)
                               and not getattr(gltf, "images", None)
                               and attributes is not None
                               and getattr(attributes, "TEXCOORD_0", None) is None,
        "indexed_triangles": primitive is not None and primitive.indices is not None
                             and primitive.mode == PRIMITIVE_MODE_TRIANGLES
                             and indices_accessor is not None
                             and indices_accessor.count % 3 == 0
                             and indices_accessor.count // 3 == triangles,
        "y_up": gltf.asset.extras.get("upAxis") == "Y",
        "faces_positive_z": gltf.asset.extras.get("forwardAxis") == "+Z"
                            and float(high[2]) > 0.0 and float(low[2]) < 0.0,
        "ground_origin": float(low[1]) >= -1e-5 and float(low[1]) <= 0.08,
        "triangle_budget": triangles <= MAX_TRIANGLES,
        "one_scene": len(gltf.scenes) == 1 and gltf.scene == 0,
        "no_animations": len(gltf.animations) == 0,
        "all_26_named_joints": len(joint_names) == 26
                               and len(set(joint_names)) == 26
                               and [gltf.nodes[node_index[name]].name for name in joint_names]
                               == joint_names,
        "file_written": output_path.exists() and output_path.stat().st_size > 0,
    }

    print(f"Triangle count: {triangles}")
    print(f"Joint count: {len(joint_names)}")
    print("Bounding box min (m): " + np.array2string(low, precision=4, separator=", "))
    print("Bounding box max (m): " + np.array2string(high, precision=4, separator=", "))
    print("Joint names: " + ", ".join(joint_names))
    print("Animation names and durations: []")
    print("Checks: " + ", ".join(f"{name}={'PASS' if passed else 'FAIL'}"
                               for name, passed in checks.items()))
    if all(checks.values()):
        print("OK")
        return True
    print("SELF-CHECK FAILED", file=sys.stderr)
    return False


def main():
    output_path = Path(__file__).resolve().parent / OUTPUT_NAME
    joint_names, parents, world_positions = make_joint_tables()
    arrays = create_mesh_arrays(joint_names)
    gltf, node_index = build_gltf(arrays, joint_names, parents, world_positions)
    gltf.save_binary(str(output_path))

    # Reload the written GLB to confirm that the binary container parses.
    reloaded = GLTF2().load(str(output_path))
    if reloaded is None:
        raise RuntimeError("pygltflib could not reload the generated GLB")
    success = self_check(reloaded, arrays, joint_names, node_index, output_path)
    print(f"Output file: {output_path}")
    if not success:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
