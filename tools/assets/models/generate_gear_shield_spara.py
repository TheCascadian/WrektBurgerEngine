#!/usr/bin/env python3
"""Generate a low-poly Achaemenid Persian wicker spara shield as a GLB."""
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
# The shared humanoid rest pose matches the accompanying tunic/headgear assets.
# -----------------------------------------------------------------------------
OUTPUT_NAME = "gear_shield_spara.glb"
MAX_TRIANGLES = 400

# The mesh is authored locally, then placed in the shared 1.80 m body space.
# The 1.0 m shield spans model-space Y=0.38..1.38 m, in front of the torso.
SHIELD_TRANSLATION = (-0.280, 0.380, 0.160)
SHIELD_WIDTH = 0.600
SHIELD_HEIGHT = 1.000
SHIELD_THICKNESS = 0.030
CURVE_BULGE = 0.025  # center projects 2.5 cm farther forward than the side edges
GRID_X_SEGMENTS = 8
GRID_Y_SEGMENTS = 10
RIM_WIDTH = 0.022
RIM_LIFT = 0.001

# Procedural wicker variation and dark wood parts; vertex colors only.
COLOR_WICKER_A = (190 / 255.0, 151 / 255.0, 96 / 255.0, 1.0)   # tan
COLOR_WICKER_B = (211 / 255.0, 174 / 255.0, 116 / 255.0, 1.0)  # lighter reed
COLOR_WICKER_C = (173 / 255.0, 133 / 255.0, 80 / 255.0, 1.0)   # darker reed
COLOR_WOOD = (91 / 255.0, 54 / 255.0, 34 / 255.0, 1.0)         # rim/grip
COLOR_BACK = (151 / 255.0, 113 / 255.0, 69 / 255.0, 1.0)

# Interior handgrip, near forearm.L/hand.L in the shared rest pose.
GRIP_X = -0.220
GRIP_Y_BOTTOM = 0.500
GRIP_Y_TOP = 0.720
GRIP_RADIUS = 0.014
GRIP_SEGMENTS = 6
GRIP_MOUNT_WIDTH = 0.060
GRIP_MOUNT_HEIGHT = 0.040
GRIP_MOUNT_DEPTH = 0.025
GRIP_MOUNT_Y = (0.520, 0.700)
GRIP_BACK_GAP = 0.0115

# EXACT shared rest-pose joint locations, in meters, from the previous pieces.
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

    return names, parents, world


class MeshBuilder:
    """Triangle-soup builder with flat normals and one rigid joint influence."""
    def __init__(self, joint_names):
        self.joint_index = {name: i for i, name in enumerate(joint_names)}
        self.bound_joint = self.joint_index["forearm.L"]
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
        normal = (raw / area2).astype(np.float32)

        for point in points:
            self.positions.append(point.astype(np.float32))
            self.normals.append(normal)
            self.colors.append(np.asarray(color, dtype=np.float32))
            # All four slots target forearm.L; the first slot has weight 1.
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
            "indices": np.arange(len(self.positions), dtype="<u2"),
            "triangle_count": self.triangle_count,
        }


def shield_point(x, y, front=True, z_offset=0.0):
    """Curved panel point. X/Y are local shield-space meters."""
    u = x / (SHIELD_WIDTH * 0.5)
    mid_z = 0.5 * SHIELD_THICKNESS + CURVE_BULGE * (1.0 - u * u)
    z = mid_z + (0.5 * SHIELD_THICKNESS if front else -0.5 * SHIELD_THICKNESS)
    return np.asarray((x, y, z + z_offset), dtype=np.float64)


def grid_x(i):
    return -0.5 * SHIELD_WIDTH + SHIELD_WIDTH * i / GRID_X_SEGMENTS


def grid_y(j):
    return SHIELD_HEIGHT * j / GRID_Y_SEGMENTS


def wicker_color(row, col):
    # An alternating reed pattern gives a woven appearance without textures.
    if (row % 2) == 0 and (col % 2) == 0:
        return COLOR_WICKER_B
    if (row % 2) != (col % 2):
        return COLOR_WICKER_A
    return COLOR_WICKER_C


def add_shield_shell(builder):
    # Convex front grid with alternating vertex-colored wicker cells.
    for row in range(GRID_Y_SEGMENTS):
        y0, y1 = grid_y(row), grid_y(row + 1)
        for col in range(GRID_X_SEGMENTS):
            x0, x1 = grid_x(col), grid_x(col + 1)
            a = shield_point(x0, y0, True)
            b = shield_point(x1, y0, True)
            c = shield_point(x1, y1, True)
            d = shield_point(x0, y1, True)
            builder.quad(a, b, c, d, wicker_color(row, col), (0, 0, 1))

    # The gently curved back is a low-poly fan around its center point.
    perimeter = []
    for i in range(GRID_X_SEGMENTS + 1):
        perimeter.append((grid_x(i), 0.0))
    for j in range(1, GRID_Y_SEGMENTS + 1):
        perimeter.append((SHIELD_WIDTH * 0.5, grid_y(j)))
    for i in range(GRID_X_SEGMENTS - 1, -1, -1):
        perimeter.append((grid_x(i), SHIELD_HEIGHT))
    for j in range(GRID_Y_SEGMENTS - 1, 0, -1):
        perimeter.append((-SHIELD_WIDTH * 0.5, grid_y(j)))

    back_ring = [shield_point(x, y, False) for x, y in perimeter]
    center = shield_point(0.0, SHIELD_HEIGHT * 0.5, False)
    for i, point in enumerate(back_ring):
        nxt = back_ring[(i + 1) % len(back_ring)]
        builder.triangle(center, nxt, point, COLOR_BACK, (0, 0, -1))

    # Close all four edges between the front and back perimeter loops.
    front_ring = [shield_point(x, y, True) for x, y in perimeter]
    count = len(perimeter)
    for i in range(count):
        j = (i + 1) % count
        xmid = 0.5 * (perimeter[i][0] + perimeter[j][0])
        ymid = 0.5 * (perimeter[i][1] + perimeter[j][1])
        if ymid < 1e-8:
            outward = (0, -1, 0)
        elif ymid > SHIELD_HEIGHT - 1e-8:
            outward = (0, 1, 0)
        elif xmid > 0:
            outward = (1, 0, 0)
        else:
            outward = (-1, 0, 0)
        builder.quad(
            front_ring[i], front_ring[j], back_ring[j], back_ring[i],
            COLOR_WOOD, outward,
        )


def add_front_rim(builder):
    """Add a narrow, raised dark wood frame along the visible face perimeter."""
    lift = RIM_LIFT
    half = SHIELD_WIDTH * 0.5

    # Top and bottom rim rails, divided to follow the subtle horizontal curve.
    for i in range(GRID_X_SEGMENTS):
        x0, x1 = grid_x(i), grid_x(i + 1)
        for y0, y1 in (
            (0.0, RIM_WIDTH),
            (SHIELD_HEIGHT - RIM_WIDTH, SHIELD_HEIGHT),
        ):
            builder.quad(
                shield_point(x0, y0, True, lift),
                shield_point(x1, y0, True, lift),
                shield_point(x1, y1, True, lift),
                shield_point(x0, y1, True, lift),
                COLOR_WOOD, (0, 0, 1),
            )

    # Left and right vertical rim rails.
    for j in range(GRID_Y_SEGMENTS):
        y0, y1 = grid_y(j), grid_y(j + 1)
        for x0, x1 in (
            (-half, -half + RIM_WIDTH),
            (half - RIM_WIDTH, half),
        ):
            builder.quad(
                shield_point(x0, y0, True, lift),
                shield_point(x1, y0, True, lift),
                shield_point(x1, y1, True, lift),
                shield_point(x0, y1, True, lift),
                COLOR_WOOD, (0, 0, 1),
            )


def add_box(builder, center, size, color):
    cx, cy, cz = center
    sx, sy, sz = (0.5 * v for v in size)
    v = {
        "lbf": (cx-sx, cy-sy, cz-sz), "rbf": (cx+sx, cy-sy, cz-sz),
        "rtf": (cx+sx, cy+sy, cz-sz), "ltf": (cx-sx, cy+sy, cz-sz),
        "lbb": (cx-sx, cy-sy, cz+sz), "rbb": (cx+sx, cy-sy, cz+sz),
        "rtb": (cx+sx, cy+sy, cz+sz), "ltb": (cx-sx, cy+sy, cz+sz),
    }
    # The names f/b are not semantically relied on; expected normals orient faces.
    faces = (
        (("lbf", "rbf", "rtf", "ltf"), (0, 0, -1)),
        (("lbb", "ltb", "rtb", "rbb"), (0, 0, 1)),
        (("lbf", "ltf", "ltb", "lbb"), (-1, 0, 0)),
        (("rbf", "rbb", "rtb", "rtf"), (1, 0, 0)),
        (("lbf", "lbb", "rbb", "rbf"), (0, -1, 0)),
        (("ltf", "rtf", "rtb", "ltb"), (0, 1, 0)),
    )
    for keys, normal in faces:
        builder.quad(*(v[k] for k in keys), color, normal)


def add_back_grip(builder):
    # The grip sits on the shield's rear (-Z) side and aligns with hand.L.
    u = GRIP_X / (SHIELD_WIDTH * 0.5)
    back_surface_z = (
        0.5 * SHIELD_THICKNESS
        + CURVE_BULGE * (1.0 - u * u)
        - 0.5 * SHIELD_THICKNESS
    )
    mount_center_z = back_surface_z - GRIP_BACK_GAP

    for y in GRIP_MOUNT_Y:
        add_box(
            builder,
            (GRIP_X, y, mount_center_z),
            (GRIP_MOUNT_WIDTH, GRIP_MOUNT_HEIGHT, GRIP_MOUNT_DEPTH),
            COLOR_WOOD,
        )

    # A hexagonal vertical grip cylinder, joined visually to its brackets.
    z_center = back_surface_z - 0.023
    lower, upper = GRIP_Y_BOTTOM, GRIP_Y_TOP
    ring0, ring1 = [], []
    for i in range(GRIP_SEGMENTS):
        angle = 2.0 * math.pi * i / GRIP_SEGMENTS
        x = GRIP_X + GRIP_RADIUS * math.cos(angle)
        z = z_center + GRIP_RADIUS * math.sin(angle)
        ring0.append(np.asarray((x, lower, z), dtype=np.float64))
        ring1.append(np.asarray((x, upper, z), dtype=np.float64))

    for i in range(GRIP_SEGMENTS):
        j = (i + 1) % GRIP_SEGMENTS
        angle = 2.0 * math.pi * (i + 0.5) / GRIP_SEGMENTS
        radial = (math.cos(angle), 0.0, math.sin(angle))
        builder.quad(
            ring0[i], ring0[j], ring1[j], ring1[i], COLOR_WOOD, radial
        )

    bottom_center = np.asarray((GRIP_X, lower, z_center), dtype=np.float64)
    top_center = np.asarray((GRIP_X, upper, z_center), dtype=np.float64)
    for i in range(GRIP_SEGMENTS):
        j = (i + 1) % GRIP_SEGMENTS
        builder.triangle(
            bottom_center, ring0[j], ring0[i], COLOR_WOOD, (0, -1, 0)
        )
        builder.triangle(
            top_center, ring1[i], ring1[j], COLOR_WOOD, (0, 1, 0)
        )


def create_mesh_arrays(joint_names):
    builder = MeshBuilder(joint_names)
    add_shield_shell(builder)
    add_front_rim(builder)
    add_back_grip(builder)
    return builder.arrays()


def make_nodes_and_indices(joint_names, parents, world_positions):
    node_index = {name: i + 1 for i, name in enumerate(joint_names)}
    children = {name: [] for name in joint_names}
    for name, parent in parents.items():
        if parent is not None:
            children[parent].append(name)

    nodes = [Node(
        name="gear_shield_spara_mesh",
        mesh=0,
        skin=0,
        translation=[float(x) for x in SHIELD_TRANSLATION],
    )]
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
            children=[node_index[ch] for ch in children[name]],
        ))
    return nodes, node_index


def inverse_bind_matrices(joint_names, world_positions):
    mesh_world = np.eye(4, dtype=np.float64)
    mesh_world[:3, 3] = np.asarray(SHIELD_TRANSLATION, dtype=np.float64)
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
        bufferView=view, byteOffset=0,
        componentType=component_type, count=count, type=kind,
        min=None if minimum is None else [float(v) for v in minimum],
        max=None if maximum is None else [float(v) for v in maximum],
        normalized=normalized,
    ))
    return len(gltf.accessors) - 1


def build_gltf(arrays, joint_names, parents, world_positions):
    gltf = GLTF2()
    gltf.asset = Asset(
        version="2.0", generator="Procedural Achaemenid Spara Generator"
    )
    gltf.asset.extras = {
        "units": "meters",
        "upAxis": "Y",
        "forwardAxis": "+Z",
        "shieldTranslation": list(SHIELD_TRANSLATION),
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
    indices_view = append_array(gltf, blob, arrays["indices"], ELEMENT_ARRAY_BUFFER)
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
    norm_acc = add_accessor(gltf, norm_view, COMPONENT_FLOAT, vertex_count, "VEC3")
    color_acc = add_accessor(gltf, color_view, COMPONENT_FLOAT, vertex_count, "VEC4")
    joints_acc = add_accessor(
        gltf, joints_view, COMPONENT_UNSIGNED_BYTE, vertex_count, "VEC4"
    )
    weights_acc = add_accessor(gltf, weights_view, COMPONENT_FLOAT, vertex_count, "VEC4")
    indices_acc = add_accessor(
        gltf, indices_view, COMPONENT_UNSIGNED_SHORT, len(arrays["indices"]),
        "SCALAR", [0], [int(arrays["indices"].max())],
    )
    ibm_acc = add_accessor(
        gltf, ibm_view, COMPONENT_FLOAT, len(joint_names), "MAT4"
    )

    gltf.materials = [Material(
        name="wicker_and_wood_vertex_colors",
        pbrMetallicRoughness=PbrMetallicRoughness(
            baseColorFactor=[1.0, 1.0, 1.0, 1.0],
            metallicFactor=0.0,
            roughnessFactor=1.0,
        ),
        doubleSided=False,
    )]
    gltf.meshes = [Mesh(
        name="gear_shield_spara",
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
    local = arrays["positions"]
    translation = np.asarray(SHIELD_TRANSLATION, dtype=np.float32)
    world = local + translation
    local_min, local_max = local.min(axis=0), local.max(axis=0)
    world_min, world_max = world.min(axis=0), world.max(axis=0)
    joints, weights = arrays["joints"], arrays["weights"]
    triangle_count = int(arrays["triangle_count"])
    primitive = gltf.meshes[0].primitives[0] if gltf.meshes else None
    attrs = primitive.attributes if primitive is not None else None
    forearm_index = joint_names.index("forearm.L")

    tri_points = arrays["positions"].reshape(-1, 3, 3)
    tri_areas2 = np.linalg.norm(
        np.cross(tri_points[:, 1] - tri_points[:, 0], tri_points[:, 2] - tri_points[:, 0]),
        axis=1,
    )

    checks = {
        "one_mesh": len(gltf.meshes) == 1,
        "one_primitive": len(gltf.meshes) == 1 and len(gltf.meshes[0].primitives) == 1,
        "one_scene": len(gltf.scenes) == 1 and gltf.scene == 0,
        "joint_limit": len(gltf.skins[0].joints) <= 64,
        "exact_26_joint_names": (
            len(joint_names) == 26 and len(set(joint_names)) == 26
            and all(gltf.nodes[node_index[n]].name == n for n in joint_names)
        ),
        "four_joint_slots": (
            joints.shape == (len(local), 4) and weights.shape == joints.shape
        ),
        "100_percent_forearm_L_weight": (
            np.all(joints == forearm_index)
            and np.all(np.abs(weights[:, 0] - 1.0) <= 1e-6)
            and np.all(np.abs(weights[:, 1:]) <= 1e-6)
        ),
        "weights_sum_to_one": bool(np.all(np.abs(weights.sum(axis=1) - 1.0) <= 0.001)),
        "no_nans": all(np.isfinite(arrays[key]).all() for key in (
            "positions", "normals", "colors", "joints", "weights", "indices"
        )),
        "no_degenerate_triangles": bool(np.all(tri_areas2 > 1e-9)),
        "normals_present": attrs is not None and attrs.NORMAL is not None,
        "no_textures_or_uvs": (
            not getattr(gltf, "textures", None)
            and not getattr(gltf, "images", None)
            and attrs is not None and getattr(attrs, "TEXCOORD_0", None) is None
        ),
        "indexed_triangles": (
            primitive is not None and primitive.indices is not None
            and primitive.mode == TRIANGLES
            and len(arrays["indices"]) % 3 == 0
            and len(arrays["indices"]) // 3 == triangle_count
        ),
        "y_up": gltf.asset.extras.get("upAxis") == "Y",
        "faces_positive_z": gltf.asset.extras.get("forwardAxis") == "+Z",
        # For a gear item the mesh origin is local Y=0; its node places it on the body.
        "local_lowest_vertex_at_zero": abs(float(local_min[1])) <= 1e-5,
        "body_space_placement": (
            np.allclose(world_min, local_min + translation, atol=1e-5)
            and np.allclose(world_max, local_max + translation, atol=1e-5)
            and abs(float(world_min[1]) - 0.380) <= 1e-4
            and abs(float(world_max[1]) - 1.380) <= 1e-4
        ),
        "triangle_budget": triangle_count <= MAX_TRIANGLES,
        "no_animations": len(gltf.animations) == 0,
        "file_written": output_path.exists() and output_path.stat().st_size > 0,
    }

    print(f"Triangle count: {triangle_count}")
    print(f"Joint count: {len(gltf.skins[0].joints)}")
    print("Bounding box min (model-space m): " + np.array2string(world_min, precision=4, separator=", "))
    print("Bounding box max (model-space m): " + np.array2string(world_max, precision=4, separator=", "))
    print("Mesh-local min (m): " + np.array2string(local_min, precision=4, separator=", "))
    print("Mesh-local max (m): " + np.array2string(local_max, precision=4, separator=", "))
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
    gltf, node_index = build_gltf(arrays, joint_names, parents, world_positions)
    gltf.save_binary(str(output_path))
    reloaded = GLTF2().load(str(output_path))
    if reloaded is None:
        raise RuntimeError("Failed to reload generated GLB")
    if not self_check(reloaded, arrays, joint_names, node_index, output_path):
        raise SystemExit(1)
    print(f"Output file: {output_path}")


if __name__ == "__main__":
    main()
