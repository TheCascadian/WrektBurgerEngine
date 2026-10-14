using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Burger.Core.Figures;

/// <summary>
/// Figures as binary glTF 2.0 (.glb), the format Blender and every other 3D tool reads and writes. Writing gives the built-in
/// body to artists as a starting point; reading brings their models and animations back (see <see cref="FigureSystem"/>).
/// Supported on reading: one skinned mesh (all its primitives are merged), vertex colours, up to four joints per vertex,
/// rotation and translation animation. Textures, morph targets and scale are ignored.
/// </summary>
public static class Glb
{
    private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;
    private const int U8 = 5121, U16 = 5123, U32 = 5125, F32 = 5126;

    // ===============================================================================================
    // Writing
    // ===============================================================================================
    public static void Write(FigureModel model, string path) => File.WriteAllBytes(path, Write(model));

    public static byte[] Write(FigureModel model)
    {
        var bin = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        var mesh = model.Mesh;
        var rig = model.Rig;
        int n = mesh.VertexCount;

        int View(ReadOnlySpan<byte> data, int? target = null)
        {
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = bin.Length, ["byteLength"] = data.Length };
            if (target != null) view["target"] = target.Value;
            bin.Write(data);
            views.Add(view);
            return views.Count - 1;
        }
        int Accessor(int view, int component, string type, int count, bool normalized = false, float[]? min = null, float[]? max = null)
        {
            var a = new JsonObject { ["bufferView"] = view, ["componentType"] = component, ["count"] = count, ["type"] = type };
            if (normalized) a["normalized"] = true;
            if (min != null) a["min"] = new JsonArray(min.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            if (max != null) a["max"] = new JsonArray(max.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
            accessors.Add(a);
            return accessors.Count - 1;
        }
        int FloatAccessor(float[] data, string type, int count, bool bounds = false, int? target = null)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            float[]? min = null, max = null;
            if (bounds)
            {
                int comps = data.Length / Math.Max(count, 1);
                min = Enumerable.Repeat(float.MaxValue, comps).ToArray();
                max = Enumerable.Repeat(float.MinValue, comps).ToArray();
                for (int i = 0; i < data.Length; i++)
                {
                    min[i % comps] = Math.Min(min[i % comps], data[i]);
                    max[i % comps] = Math.Max(max[i % comps], data[i]);
                }
            }
            return Accessor(View(bytes, target), F32, type, count, false, min, max);
        }
        static float[] Flat(Vector3[] v)
        {
            var f = new float[v.Length * 3];
            for (int i = 0; i < v.Length; i++) { f[i * 3] = v[i].X; f[i * 3 + 1] = v[i].Y; f[i * 3 + 2] = v[i].Z; }
            return f;
        }
        static float[] FlatQ(Quaternion[] q)
        {
            var f = new float[q.Length * 4];
            for (int i = 0; i < q.Length; i++) { f[i * 4] = q[i].X; f[i * 4 + 1] = q[i].Y; f[i * 4 + 2] = q[i].Z; f[i * 4 + 3] = q[i].W; }
            return f;
        }

        // ---- mesh ----
        const int arrayBuffer = 34962, elementBuffer = 34963;
        // glTF vertex colours are linear; the game's are display colours
        var linear = new ushort[mesh.Colors.Length];
        for (int i = 0; i < linear.Length; i++)
            linear[i] = (ushort)(i % 4 == 3 ? mesh.Colors[i] * 257 : Math.Clamp(MathF.Pow(mesh.Colors[i] / 255f, 2.2f) * 65535f + 0.5f, 0, 65535));
        var attributes = new JsonObject
        {
            ["POSITION"] = FloatAccessor(Flat(mesh.Positions), "VEC3", n, true, arrayBuffer),
            ["NORMAL"] = FloatAccessor(Flat(mesh.Normals), "VEC3", n, false, arrayBuffer),
            ["COLOR_0"] = Accessor(View(System.Runtime.InteropServices.MemoryMarshal.AsBytes(linear.AsSpan()), arrayBuffer), U16, "VEC4", n, true),
            ["JOINTS_0"] = Accessor(View(mesh.Joints, arrayBuffer), U8, "VEC4", n),
            ["WEIGHTS_0"] = FloatAccessor(mesh.Weights, "VEC4", n, false, arrayBuffer),
        };
        int indices;
        if (n <= ushort.MaxValue)
        {
            var bytes = new byte[mesh.Indices.Length * 2];
            for (int i = 0; i < mesh.Indices.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), (ushort)mesh.Indices[i]);
            indices = Accessor(View(bytes, elementBuffer), U16, "SCALAR", mesh.Indices.Length);
        }
        else
        {
            var bytes = new byte[mesh.Indices.Length * 4];
            Buffer.BlockCopy(mesh.Indices, 0, bytes, 0, bytes.Length);
            indices = Accessor(View(bytes, elementBuffer), U32, "SCALAR", mesh.Indices.Length);
        }

        // ---- skeleton: node i is joint i, the last node carries the mesh ----
        var nodes = new JsonArray();
        var sceneNodes = new JsonArray();
        for (int j = 0; j < rig.Count; j++)
        {
            var joint = rig.Joints[j];
            var node = new JsonObject
            {
                ["name"] = joint.Name,
                ["translation"] = new JsonArray(joint.Position.X, joint.Position.Y, joint.Position.Z),
                ["rotation"] = new JsonArray(joint.Rotation.X, joint.Rotation.Y, joint.Rotation.Z, joint.Rotation.W),
            };
            var children = new JsonArray();
            for (int c = j + 1; c < rig.Count; c++)
                if (rig.Joints[c].Parent == j) children.Add(c);
            if (children.Count > 0) node["children"] = children;
            nodes.Add(node);
            if (joint.Parent < 0) sceneNodes.Add(j);
        }
        nodes.Add(new JsonObject { ["name"] = model.Key, ["mesh"] = 0, ["skin"] = 0 });
        sceneNodes.Add(rig.Count);

        var ibm = new float[rig.Count * 16];
        for (int j = 0; j < rig.Count; j++) CopyMatrix(rig.InverseBind[j], ibm, j * 16);
        var skin = new JsonObject
        {
            ["name"] = model.Key,
            ["joints"] = new JsonArray(Enumerable.Range(0, rig.Count).Select(i => (JsonNode)JsonValue.Create(i)).ToArray()),
            ["inverseBindMatrices"] = FloatAccessor(ibm, "MAT4", rig.Count),
            ["skeleton"] = 0,
        };

        // ---- animations ----
        var animations = new JsonArray();
        foreach (var clip in model.Clips.Values)
        {
            var samplers = new JsonArray();
            var channels = new JsonArray();
            var timeAccessor = new Dictionary<float[], int>(); // tracks baked together share one time array
            int Times(float[] t) => timeAccessor.TryGetValue(t, out int a) ? a : timeAccessor[t] = FloatAccessor(t, "SCALAR", t.Length, true);
            for (int j = 0; j < clip.Tracks.Length && j < rig.Count; j++)
            {
                var track = clip.Tracks[j];
                if (track == null) continue;
                if (track.Rotations is { Length: > 0 } && track.RotationTimes != null)
                {
                    samplers.Add(new JsonObject { ["input"] = Times(track.RotationTimes), ["output"] = FloatAccessor(FlatQ(track.Rotations), "VEC4", track.Rotations.Length), ["interpolation"] = "LINEAR" });
                    channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = j, ["path"] = "rotation" } });
                }
                if (track.Positions is { Length: > 0 } && track.PositionTimes != null)
                {
                    samplers.Add(new JsonObject { ["input"] = Times(track.PositionTimes), ["output"] = FloatAccessor(Flat(track.Positions), "VEC3", track.Positions.Length), ["interpolation"] = "LINEAR" });
                    channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = j, ["path"] = "translation" } });
                }
            }
            if (channels.Count == 0) continue;
            animations.Add(new JsonObject
            {
                ["name"] = clip.Name,
                ["samplers"] = samplers,
                ["channels"] = channels,
                ["extras"] = new JsonObject { ["loop"] = clip.Loop, ["speed"] = clip.Speed },
            });
        }

        while (bin.Length % 4 != 0) bin.WriteByte(0);
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "BurgerEngine figures" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["name"] = model.Key, ["nodes"] = sceneNodes }),
            ["nodes"] = nodes,
            ["meshes"] = new JsonArray(new JsonObject
            {
                ["name"] = model.Key,
                ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = attributes, ["indices"] = indices, ["material"] = 0, ["mode"] = 4 }),
            }),
            // vertex colours carry the look: a plain rough white material lets them through
            ["materials"] = new JsonArray(new JsonObject
            {
                ["name"] = "skin",
                ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = new JsonArray(1, 1, 1, 1), ["metallicFactor"] = 0, ["roughnessFactor"] = 0.85 },
            }),
            ["skins"] = new JsonArray(skin),
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bin.Length }),
        };
        if (animations.Count > 0) root["animations"] = animations;

        var json = new List<byte>(Encoding.UTF8.GetBytes(root.ToJsonString()));
        while (json.Count % 4 != 0) json.Add((byte)' ');
        var output = new MemoryStream();
        using (var w = new BinaryWriter(output, Encoding.UTF8, true))
        {
            w.Write(Magic); w.Write(2u); w.Write((uint)(12 + 8 + json.Count + 8 + bin.Length));
            w.Write((uint)json.Count); w.Write(JsonChunk); w.Write(json.ToArray());
            w.Write((uint)bin.Length); w.Write(BinChunk); w.Write(bin.ToArray());
        }
        return output.ToArray();
    }

    // System.Numerics matrices (row vectors, translation in the fourth row) have the same memory layout as glTF's
    // (column vectors, stored column by column), so they are copied element for element.
    private static void CopyMatrix(Matrix4x4 m, float[] to, int at)
    {
        to[at] = m.M11; to[at + 1] = m.M12; to[at + 2] = m.M13; to[at + 3] = m.M14;
        to[at + 4] = m.M21; to[at + 5] = m.M22; to[at + 6] = m.M23; to[at + 7] = m.M24;
        to[at + 8] = m.M31; to[at + 9] = m.M32; to[at + 10] = m.M33; to[at + 11] = m.M34;
        to[at + 12] = m.M41; to[at + 13] = m.M42; to[at + 14] = m.M43; to[at + 15] = m.M44;
    }

    private static Matrix4x4 ToMatrix(float[] f, int at) => new(
        f[at], f[at + 1], f[at + 2], f[at + 3], f[at + 4], f[at + 5], f[at + 6], f[at + 7],
        f[at + 8], f[at + 9], f[at + 10], f[at + 11], f[at + 12], f[at + 13], f[at + 14], f[at + 15]);

    // ===============================================================================================
    // Reading
    // ===============================================================================================
    /// <summary>Reads the skinned model of a .glb together with the animations in it. Throws <see cref="InvalidDataException"/> if the file has none; <paramref name="warnings"/> receives what was ignored or looked wrong.</summary>
    public static FigureModel Read(string path, string key, List<string>? warnings = null) => Read(File.ReadAllBytes(path), key, warnings);

    public static FigureModel Read(byte[] data, string key, List<string>? warnings = null)
    {
        var doc = new Doc(data, warnings);
        var model = doc.Model(key);
        foreach (var clip in doc.Clips(model.Rig)) model.Clips[clip.Name] = clip;
        return model;
    }

    /// <summary>Reads only the animations of a .glb and binds them to a rig by joint name (joints the rig lacks are skipped).</summary>
    public static List<Clip> ReadClips(string path, Rig rig, List<string>? warnings = null) => new Doc(File.ReadAllBytes(path), warnings).Clips(rig);

    private sealed class Doc
    {
        private readonly JsonElement _root;
        private readonly byte[] _bin = Array.Empty<byte>();
        private readonly List<string>? _warnings;
        private readonly int[] _parent;
        /// <summary>Per joint name: the transform of the non-joint nodes above a root joint (an armature object, say), folded into it.</summary>
        private readonly Dictionary<string, Matrix4x4> _prefix = new(StringComparer.OrdinalIgnoreCase);

        public Doc(byte[] data, List<string>? warnings)
        {
            _warnings = warnings;
            if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != Magic) throw new InvalidDataException("Not a binary glTF (.glb) file.");
            if (BitConverter.ToUInt32(data, 4) != 2) throw new InvalidDataException("Only glTF 2.0 is supported.");
            int at = 12;
            string? json = null;
            while (at + 8 <= data.Length)
            {
                int length = (int)BitConverter.ToUInt32(data, at);
                uint type = BitConverter.ToUInt32(data, at + 4);
                at += 8;
                if (length < 0 || at + length > data.Length) throw new InvalidDataException("Truncated .glb file.");
                if (type == JsonChunk) json = Encoding.UTF8.GetString(data, at, length);
                else if (type == BinChunk && _bin.Length == 0) _bin = data.AsSpan(at, length).ToArray();
                at += length;
            }
            if (json == null) throw new InvalidDataException("The .glb file has no JSON chunk.");
            _root = JsonDocument.Parse(json).RootElement;
            int nodes = Count("nodes");
            _parent = new int[nodes];
            Array.Fill(_parent, -1);
            for (int i = 0; i < nodes; i++)
                if (_root.GetProperty("nodes")[i].TryGetProperty("children", out var children))
                    foreach (var c in children.EnumerateArray()) _parent[c.GetInt32()] = i;
        }

        private void Warn(string message) => _warnings?.Add(message);
        private int Count(string array) => _root.TryGetProperty(array, out var a) && a.ValueKind == JsonValueKind.Array ? a.GetArrayLength() : 0;
        private static int Int(JsonElement e, string name, int fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
        private string NodeName(int node) => _root.GetProperty("nodes")[node].TryGetProperty("name", out var v) ? v.GetString() ?? $"node{node}" : $"node{node}";

        // ---- accessors ----
        /// <summary>An accessor as floats (normalized integers become 0-1), <paramref name="components"/> per element.</summary>
        private float[] Floats(int accessor, out int components)
        {
            var a = _root.GetProperty("accessors")[accessor];
            if (a.TryGetProperty("sparse", out _)) Warn("Sparse accessors are not supported; the base data is used.");
            int count = Int(a, "count"), component = Int(a, "componentType");
            bool normalized = a.TryGetProperty("normalized", out var nrm) && nrm.ValueKind == JsonValueKind.True;
            components = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, var t => throw new InvalidDataException($"Unsupported accessor type '{t}'.") };
            int size = component switch { 5120 or U8 => 1, 5122 or U16 => 2, U32 or F32 => 4, _ => throw new InvalidDataException($"Unsupported component type {component}.") };
            var result = new float[count * components];
            if (!a.TryGetProperty("bufferView", out var viewIndex)) return result;
            var view = _root.GetProperty("bufferViews")[viewIndex.GetInt32()];
            if (Int(view, "buffer") != 0 || _bin.Length == 0) throw new InvalidDataException("Only data embedded in the .glb is supported (export as 'glTF Binary').");
            int start = Int(view, "byteOffset") + Int(a, "byteOffset");
            int stride = Int(view, "byteStride", size * components);
            if (count > 0 && start + stride * (count - 1) + size * components > _bin.Length) throw new InvalidDataException("An accessor points outside the file.");
            for (int i = 0; i < count; i++)
                for (int c = 0; c < components; c++)
                {
                    int at = start + i * stride + c * size;
                    result[i * components + c] = component switch
                    {
                        F32 => BitConverter.ToSingle(_bin, at),
                        U8 => normalized ? _bin[at] / 255f : _bin[at],
                        5120 => normalized ? Math.Max((sbyte)_bin[at] / 127f, -1) : (sbyte)_bin[at],
                        U16 => normalized ? BitConverter.ToUInt16(_bin, at) / 65535f : BitConverter.ToUInt16(_bin, at),
                        5122 => normalized ? Math.Max(BitConverter.ToInt16(_bin, at) / 32767f, -1) : BitConverter.ToInt16(_bin, at),
                        _ => BitConverter.ToUInt32(_bin, at),
                    };
                }
            return result;
        }

        private Matrix4x4 Local(int node)
        {
            var e = _root.GetProperty("nodes")[node];
            if (e.TryGetProperty("matrix", out var m))
            {
                var f = m.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                return ToMatrix(f, 0);
            }
            float[] Arr(string name, float[] fallback) => e.TryGetProperty(name, out var v) ? v.EnumerateArray().Select(x => x.GetSingle()).ToArray() : fallback;
            var t = Arr("translation", new float[] { 0, 0, 0 });
            var r = Arr("rotation", new float[] { 0, 0, 0, 1 });
            var s = Arr("scale", new float[] { 1, 1, 1 });
            return Matrix4x4.CreateScale(s[0], s[1], s[2]) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(r[0], r[1], r[2], r[3]))) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
        }

        // ---- model ----
        public FigureModel Model(string key)
        {
            int meshNode = -1;
            for (int i = 0; i < _parent.Length && meshNode < 0; i++)
            {
                var e = _root.GetProperty("nodes")[i];
                if (e.TryGetProperty("mesh", out _) && e.TryGetProperty("skin", out _)) meshNode = i;
            }
            if (meshNode < 0) throw new InvalidDataException("The file has no skinned mesh (a mesh bound to an armature).");
            var node = _root.GetProperty("nodes")[meshNode];
            var skin = _root.GetProperty("skins")[Int(node, "skin")];
            var jointNodes = skin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
            if (jointNodes.Length > Rig.MaxJoints) throw new InvalidDataException($"The skeleton has {jointNodes.Length} joints; at most {Rig.MaxJoints} are supported.");

            // joints must come parent first: the file's order is kept when it already is, otherwise they are sorted by depth.
            // remap[file joint] = rig joint
            int Depth(int n) { int d = 0; while (_parent[n] >= 0) { n = _parent[n]; d++; } return d; }
            var seen = new HashSet<int>();
            bool ordered = true;
            foreach (int jn in jointNodes)
            {
                for (int p = _parent[jn]; p >= 0 && ordered; p = _parent[p])
                    if (Array.IndexOf(jointNodes, p) >= 0) { ordered = seen.Contains(p); break; }
                seen.Add(jn);
            }
            var order = ordered ? Enumerable.Range(0, jointNodes.Length).ToArray() : Enumerable.Range(0, jointNodes.Length).OrderBy(i => Depth(jointNodes[i])).ToArray();
            var remap = new int[jointNodes.Length];
            for (int i = 0; i < order.Length; i++) remap[order[i]] = i;
            var nodeToJoint = new Dictionary<int, int>();
            for (int i = 0; i < jointNodes.Length; i++) nodeToJoint[jointNodes[i]] = remap[i];

            float[]? ibm = skin.TryGetProperty("inverseBindMatrices", out var ibmAccessor) ? Floats(ibmAccessor.GetInt32(), out _) : null;
            var joints = new Joint[jointNodes.Length];
            var inverseBind = ibm != null ? new Matrix4x4[jointNodes.Length] : null;
            bool scaled = false;
            for (int i = 0; i < jointNodes.Length; i++)
            {
                int n = jointNodes[i], parent = -1;
                var local = Local(n);
                // nodes between this joint and the next joint above it are folded into the joint
                var above = Matrix4x4.Identity;
                for (int p = _parent[n]; p >= 0; p = _parent[p])
                {
                    if (nodeToJoint.TryGetValue(p, out parent)) break;
                    parent = -1;
                    above *= Local(p);
                }
                string name = NodeName(n);
                if (!above.IsIdentity) _prefix[name] = above;
                Matrix4x4.Decompose(local * above, out var scale, out var rotation, out var translation);
                if ((scale - Vector3.One).Length() > 1e-3f) scaled = true;
                joints[remap[i]] = new Joint(name, parent, translation, Quaternion.Normalize(rotation));
                if (inverseBind != null) inverseBind[remap[i]] = ToMatrix(ibm!, i * 16);
            }
            if (scaled) Warn("The skeleton has scaled bones or a scaled armature; scale is ignored (apply scale before exporting).");
            var rig = new Rig(joints, inverseBind);

            // ---- geometry: every triangle primitive of the mesh, merged ----
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<byte>();
            var jointIndices = new List<byte>();
            var weights = new List<float>();
            var indices = new List<int>();
            bool missingNormals = false;
            foreach (var prim in _root.GetProperty("meshes")[Int(node, "mesh")].GetProperty("primitives").EnumerateArray())
            {
                if (Int(prim, "mode", 4) != 4) { Warn("A primitive that is not a triangle list was skipped."); continue; }
                var attr = prim.GetProperty("attributes");
                if (!attr.TryGetProperty("POSITION", out var posAccessor)) continue;
                int first = positions.Count;
                var pos = Floats(posAccessor.GetInt32(), out _);
                int count = pos.Length / 3;
                for (int i = 0; i < count; i++) positions.Add(new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]));

                if (attr.TryGetProperty("NORMAL", out var normalAccessor))
                {
                    var nrm = Floats(normalAccessor.GetInt32(), out _);
                    for (int i = 0; i < count; i++) normals.Add(new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]));
                }
                else { missingNormals = true; for (int i = 0; i < count; i++) normals.Add(Vector3.Zero); }

                // without vertex colours the material's base colour is used
                float[] baseColor = { 1, 1, 1, 1 };
                if (prim.TryGetProperty("material", out var material) && _root.TryGetProperty("materials", out var materials)
                    && materials[material.GetInt32()].TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.TryGetProperty("baseColorFactor", out var factor))
                    baseColor = factor.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                float[]? col = null;
                int colComps = 4;
                if (attr.TryGetProperty("COLOR_0", out var colorAccessor)) col = Floats(colorAccessor.GetInt32(), out colComps);
                for (int i = 0; i < count; i++)
                    for (int c = 0; c < 4; c++)
                    {
                        float v = baseColor[c] * (col == null ? 1 : c < colComps ? col[i * colComps + c] : 1);
                        if (c < 3) v = MathF.Pow(Math.Clamp(v, 0, 1), 1 / 2.2f); // glTF colours are linear, the game's are display colours
                        colors.Add((byte)Math.Clamp(v * 255f + 0.5f, 0, 255));
                    }

                if (!attr.TryGetProperty("JOINTS_0", out var jointAccessor) || !attr.TryGetProperty("WEIGHTS_0", out var weightAccessor))
                    throw new InvalidDataException("The mesh is not weighted to the skeleton (no JOINTS_0 / WEIGHTS_0).");
                var jf = Floats(jointAccessor.GetInt32(), out _);
                var wf = Floats(weightAccessor.GetInt32(), out _);
                for (int i = 0; i < count; i++)
                {
                    float sum = wf[i * 4] + wf[i * 4 + 1] + wf[i * 4 + 2] + wf[i * 4 + 3];
                    for (int c = 0; c < 4; c++)
                    {
                        float w = sum > 1e-6f ? wf[i * 4 + c] / sum : c == 0 ? 1 : 0;
                        int j = (int)jf[i * 4 + c];
                        jointIndices.Add((byte)(w > 0 && j >= 0 && j < remap.Length ? remap[j] : 0));
                        weights.Add(w);
                    }
                }

                if (prim.TryGetProperty("indices", out var indexAccessor))
                    foreach (float f in Floats(indexAccessor.GetInt32(), out _)) indices.Add(first + (int)f);
                else
                    for (int i = 0; i + 2 < count; i += 3) { indices.Add(first + i); indices.Add(first + i + 1); indices.Add(first + i + 2); }
            }
            if (positions.Count == 0 || indices.Count == 0) throw new InvalidDataException("The skinned mesh has no triangles.");
            if (missingNormals)
            {
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                    normals[a] += face; normals[b] += face; normals[c] += face;
                }
            }
            for (int i = 0; i < normals.Count; i++) normals[i] = normals[i].LengthSquared() > 1e-16f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;

            return new FigureModel(key, rig, new FigureMesh
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Colors = colors.ToArray(),
                Joints = jointIndices.ToArray(),
                Weights = weights.ToArray(),
                Indices = indices.ToArray(),
            });
        }

        // ---- animations ----
        public List<Clip> Clips(Rig rig)
        {
            var clips = new List<Clip>();
            if (!_root.TryGetProperty("animations", out var animations)) return clips;
            int unnamed = 0;
            foreach (var anim in animations.EnumerateArray())
            {
                string name = anim.TryGetProperty("name", out var nameValue) && !string.IsNullOrWhiteSpace(nameValue.GetString()) ? nameValue.GetString()! : $"clip{unnamed++}";
                var tracks = new JointTrack?[rig.Count];
                float duration = 0;
                bool any = false, skippedScale = false;
                var samplers = anim.GetProperty("samplers");
                foreach (var channel in anim.GetProperty("channels").EnumerateArray())
                {
                    var target = channel.GetProperty("target");
                    if (!target.TryGetProperty("node", out var nodeValue)) continue;
                    string path = target.GetProperty("path").GetString() ?? "";
                    string jointName = NodeName(nodeValue.GetInt32());
                    int joint = rig.Find(jointName);
                    if (joint < 0) continue;
                    if (path == "scale") { skippedScale = true; continue; }
                    if (path != "rotation" && path != "translation") continue;
                    var sampler = samplers[Int(channel, "sampler")];
                    var times = Floats(Int(sampler, "input"), out _);
                    var values = Floats(Int(sampler, "output"), out int comps);
                    if (times.Length == 0) continue;
                    // cubic splines store (in-tangent, value, out-tangent) per key: the values alone are close enough at export sample rates
                    bool cubic = sampler.TryGetProperty("interpolation", out var interp) && interp.GetString() == "CUBICSPLINE";
                    int step = cubic ? 3 : 1, offset = cubic ? 1 : 0;
                    if (values.Length < times.Length * comps * step) continue;
                    _prefix.TryGetValue(jointName, out var above);
                    bool prefixed = _prefix.ContainsKey(jointName);
                    Quaternion aboveRotation = Quaternion.Identity;
                    if (prefixed) { Matrix4x4.Decompose(above, out _, out aboveRotation, out _); aboveRotation = Quaternion.Normalize(aboveRotation); }
                    var track = tracks[joint] ??= new JointTrack();
                    if (path == "rotation")
                    {
                        track.RotationTimes = times;
                        track.Rotations = new Quaternion[times.Length];
                        for (int k = 0; k < times.Length; k++)
                        {
                            int at = (k * step + offset) * 4;
                            var q = Quaternion.Normalize(new Quaternion(values[at], values[at + 1], values[at + 2], values[at + 3]));
                            if (prefixed) q = aboveRotation * q;
                            if (k > 0 && Quaternion.Dot(q, track.Rotations[k - 1]) < 0) q = Quaternion.Negate(q);
                            track.Rotations[k] = q;
                        }
                    }
                    else
                    {
                        track.PositionTimes = times;
                        track.Positions = new Vector3[times.Length];
                        for (int k = 0; k < times.Length; k++)
                        {
                            int at = (k * step + offset) * 3;
                            var p = new Vector3(values[at], values[at + 1], values[at + 2]);
                            track.Positions[k] = prefixed ? Vector3.Transform(p, above) : p;
                        }
                    }
                    duration = Math.Max(duration, times[^1]);
                    any = true;
                }
                if (skippedScale) Warn($"Animation '{name}' animates scale, which is ignored.");
                if (!any) { Warn($"Animation '{name}' moves no joint of the rig (joints are matched by name)."); continue; }
                var clip = new Clip(name, duration, tracks);
                if (anim.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
                {
                    if (extras.TryGetProperty("loop", out var loop))
                        clip.Loop = loop.ValueKind == JsonValueKind.True || (loop.ValueKind == JsonValueKind.Number && loop.GetDouble() != 0);
                    if (extras.TryGetProperty("speed", out var speed) && speed.ValueKind == JsonValueKind.Number) clip.Speed = speed.GetSingle();
                }
                clips.Add(clip);
            }
            return clips;
        }
    }
}
