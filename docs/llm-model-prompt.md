# Reusable prompt: build a model file for the game

Give everything below the line to another LLM. Fill in the three `{{ }}` blanks first (what to build, its style, its animations). The LLM needs no access to the repository.

---

You are a technical 3D artist and Python programmer. Write ONE self-contained Python script that generates a **binary glTF 2.0 file (`.glb`)** for a strategy game's engine, procedurally, with no hand-made assets and no textures. Then explain how to run it.

## What to build
{{ONE-PARAGRAPH DESCRIPTION: what the object is, its real-world size in meters, era and culture, e.g. "a Greek hoplite's bronze Corinthian helmet, 25 cm tall, 500 BCE" or "a trireme warship, 37 m long"}}

Style: {{e.g. "low poly, flat shaded, about 2,000 triangles, muted historical colors"}}

Animations needed: {{e.g. "none", or "idle (looping, 6 s), walk (looping), fire (one shot)"}}

## The engine's rules (all of them are hard requirements)

**File**
- Binary glTF 2.0 (`.glb`), one file, one scene. Units: meters. Axes: **+Y up, the object faces +Z, feet or keel on the origin (Y = 0)**.
- Apply all scale and rotation to the vertices before export. **Scale on joints/bones is ignored by the engine**; never animate scale.
- **No textures and no UVs are used.** Color comes from **vertex colors** (`COLOR_0`, RGBA, float or normalized) or from material base colors. Prefer vertex colors so one mesh can carry many colors.
- Smooth or flat normals are both fine; write `NORMAL`.

**Mesh**
- Exactly **one skinned mesh** with one primitive (triangles, indexed). Attributes: `POSITION`, `NORMAL`, `COLOR_0`, `JOINTS_0` (unsigned byte or short, **4 joints per vertex**), `WEIGHTS_0` (float, each vertex's four weights sum to 1).
- Stay under the triangle budget given above. Closed, outward-facing (counter-clockwise) triangles. No loose or degenerate triangles.
- At most **64 joints**.

**Skeleton**
- One skin, joints are glTF nodes in a hierarchy with a single `root` joint. Name joints exactly as listed in the table below when the object is a humanoid body or a piece of clothing/gear for one; otherwise use short lowercase names (`root`, `hull`, `turret`, `wheel1.L`). Left/right joints end in `.L` / `.R`.
- Provide inverse bind matrices (identity-at-rest bind pose is fine as long as they match the joint's rest world transform).

**Humanoid joint names** (use these EXACTLY for any person, armor, clothing, helmet, shield, weapon or gear piece, so it fits and animates on the game's own body; the skeleton's rest pose is a standing person 1.80 m tall facing +Z, arms slightly away from the body):
`root, hips, spine, chest, neck, head` and per side (`.L` and `.R`): `shoulder, upper_arm, forearm, hand, fingers, thumb, thigh, shin, foot, toe` (26 joints in all).
- A *gear piece* (helmet, breastplate, greaves, sword, spear, shield, cloak) is its own `.glb` containing **the full 26-joint skeleton** and a mesh skinned to it (weights only on the joints that carry the piece: a helmet is 100% `head`, a spear 100% `hand.R`, a breastplate `chest`/`spine`, etc.). Model it in the same space as the body: positioned where it would sit on a 1.80 m person standing at the origin.
- Clothing that covers the body should be built slightly **outside** the body surface (about 1-2 cm) so it does not poke through.

**Animation (only if requested)**
- Each clip is a glTF animation with a unique `name`, keyframes on joint nodes' **translation and rotation only** (quaternions), linear interpolation, **at least 30 samples per second**, durations as requested.
- Looping clips must end exactly where they begin (the last keyframe equals the first).
- A clip that travels (`walk`, `run`, `drive`) needs a custom property (glTF `extras`) on the animation: `{"speed": <meters per second>}`.
- Standard clip names: people `idle, walk, run, fire`; vehicles `idle, drive, aim, fire`; use these names for these roles.

## How to write the script
1. Use only Python 3 with `numpy` and `pygltflib` (`pip install numpy pygltflib`). Do not use Blender. If you must, say so up front and give a `bpy` version instead.
2. Build the geometry from primitives (boxes, cylinders, spheres, lathe/revolve profiles, extrusions, tapered shapes) with small helper functions; assign vertex colors per part; skin each part to its joint(s).
3. Put every tunable (sizes, colors, triangle counts, animation timings) in a constants block at the top, with comments in meters.
4. Write `out.glb` next to the script.
5. After writing the file, the script must **self-check and print**: triangle count, joint count, bounding box (min and max in meters), the joint names, the list of animation names and durations, and `OK` only if all of these hold: one mesh, one primitive, ≤ 64 joints, 4 joints per vertex, weights sum to 1 (±0.001), no NaNs, Y-up, faces +Z, lowest vertex at Y ≈ 0, triangle count within the budget.
6. Reply with: the complete script in one code block, then 5 lines at most on how to run it and what the output contains. No other commentary.

## Delivery
Name the output file `{{key}}.glb`, where `{{key}}` is a short lowercase identifier (letters, digits, underscore), e.g. `gear_helmet_corinthian`, `trireme`. If this file is an animation-only add-on for an existing model, name it `{{model}}.{{anything}}.glb` and include only the skeleton and animations (no mesh).
