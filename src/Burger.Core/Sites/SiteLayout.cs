using System.Numerics;
using Burger.Core.Figures;

namespace Burger.Core.Sites;

/// <summary>One piece standing in a site: metres from the site's center, heading in radians (0 = +Z, turning towards +X, as figures).</summary>
public readonly record struct SitePlacement(string Piece, float X, float Z, float Yaw);

/// <summary>What a layout may know of the ground: which points (metres from the site center) are on the site's land, and how high they are above the center.</summary>
public sealed class SiteTerrain
{
    public Func<float, float, bool> Inside = (_, _) => true;
    public Func<float, float, float> Height = (_, _) => 0;
    private const int Rays = 72;
    private float[]? _reach;

    /// <summary>Metres from the center to the edge of the land in a direction (capped at 150), looked up from 72 rays.</summary>
    public float Reach(float angle)
    {
        if (_reach == null)
        {
            _reach = new float[Rays];
            for (int i = 0; i < Rays; i++)
            {
                float a = MathF.Tau * i / Rays, t = 0;
                while (t < 150 && Inside((t + 2) * MathF.Cos(a), (t + 2) * MathF.Sin(a))) t += 2;
                _reach[i] = t;
            }
        }
        float f = (angle / MathF.Tau % 1f + 1f) % 1f * Rays;
        int i0 = (int)f % Rays, i1 = (i0 + 1) % Rays;
        return _reach[i0] + (_reach[i1] - _reach[i0]) * (f - MathF.Floor(f));
    }
}

/// <summary>Lays out a site and bakes the pieces into one mesh. Pure functions of the kit, the score and the seed: the same site looks the same on every machine.</summary>
public static class SiteLayout
{
    /// <summary>Most vertices in a baked site (the renderer indexes with 16 bits).</summary>
    public const int MaxVertices = 60000;

    /// <summary>The placements of a site. <paramref name="radiusOf"/> is the footprint radius of a piece in metres (0 for an unknown piece, which is skipped).</summary>
    public static List<SitePlacement> Build(SiteKit kit, SiteTier tier, double score, int seed, Func<string, float> radiusOf, SiteTerrain? terrain = null)
    {
        var rng = new Random(seed);
        var result = new List<SitePlacement>();
        var taken = new List<(float X, float Z, float R)>();
        float R(string piece) => radiusOf(kit.ModelKey(piece));
        void Add(string piece, float x, float z, float yaw, bool reserve = true)
        {
            float r = R(piece);
            if (r <= 0) return;
            result.Add(new(piece, x, z, yaw));
            if (reserve) taken.Add((x, z, r));
        }
        bool Free(float x, float z, float r)
        {
            foreach (var t in taken)
            {
                float dx = t.X - x, dz = t.Z - z, min = t.R + r;
                if (dx * dx + dz * dz < min * min) return false;
            }
            return true;
        }
        var gates = new List<(float A, float R)>();        // where the streets leave: angle and the wall radius there
        float[]? wall = null;                              // the radius of an outline wall at 72 angles, when there is one
        float Limit(float a)
        {
            if (wall != null) { float f = (a / MathF.Tau % 1f + 1f) % 1f * wall.Length; int i0 = (int)f % wall.Length; return wall[i0] + (wall[(i0 + 1) % wall.Length] - wall[i0]) * (f - MathF.Floor(f)); }
            return terrain == null ? float.MaxValue : terrain.Reach(a) - 2f;
        }
        bool Allowed(float x, float z, float r) => MathF.Sqrt(x * x + z * z) + r <= Limit(MathF.Atan2(z, x));
        foreach (var layer in tier.Layers.Where(l => score >= l.MinScore))
        {
            switch (layer.Kind)
            {
                case "place":
                    if (layer.Pieces.Count > 0)
                    {
                        float px = layer.X, pz = layer.Z, pr = R(layer.Pieces[0].Piece);
                        for (int k = 0; k < 10 && !Allowed(px, pz, pr * 0.5f); k++) { px *= 0.85f; pz *= 0.85f; }   // fixed spots slide in until they stand on the land
                        Add(layer.Pieces[0].Piece, px, pz, layer.Yaw * MathF.PI / 180f);
                    }
                    break;
                case "ring" when layer.Pieces.Count > 0:
                {
                    int n = Math.Max(6, (int)MathF.Round(MathF.Tau * layer.Radius / layer.Spacing));
                    gates.Clear();
                    for (int g = 0; g < layer.Gates; g++) gates.Add(((layer.GateAngle * MathF.PI / 180f) + MathF.Tau * g / Math.Max(1, layer.Gates), layer.Radius));
                    for (int i = 0; i < n; i++)
                    {
                        float a = MathF.Tau * i / n;
                        bool gate = layer.Gate.Length > 0 && gates.Any(g => MathF.Abs(MathF.IEEERemainder(a - g.A, MathF.Tau)) < MathF.Tau / n * 0.5f);
                        float x = layer.Radius * MathF.Cos(a), z = layer.Radius * MathF.Sin(a);
                        Add(gate ? layer.Gate : layer.Pieces[0].Piece, x, z, MathF.Atan2(MathF.Cos(a), MathF.Sin(a)));
                    }
                    break;
                }
                case "outline" when layer.Pieces.Count > 0:
                {
                    const int N = 72;
                    var r = new float[N];
                    for (int i = 0; i < N; i++)
                        r[i] = MathF.Max(12f, MathF.Min(layer.Radius, (terrain?.Reach(MathF.Tau * i / N) ?? 999f) - layer.Margin));
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var c = (float[])r.Clone();
                        for (int i = 0; i < N; i++) r[i] = (c[(i + N - 1) % N] + 2 * c[i] + c[(i + 1) % N]) / 4f;
                    }
                    wall = r;
                    var pts = new Vector2[N];
                    for (int i = 0; i < N; i++) pts[i] = new Vector2(r[i] * MathF.Cos(MathF.Tau * i / N), r[i] * MathF.Sin(MathF.Tau * i / N));
                    float length = 0;
                    for (int i = 0; i < N; i++) length += Vector2.Distance(pts[i], pts[(i + 1) % N]);
                    int n = Math.Max(6, (int)MathF.Round(length / layer.Spacing));
                    gates.Clear();
                    for (int g = 0; g < layer.Gates; g++) gates.Add((layer.GateAngle * MathF.PI / 180f + MathF.Tau * g / Math.Max(1, layer.Gates), 0));
                    var gateAt = new Dictionary<int, int>();
                    var spots = new List<(Vector2 P, float Yaw, float A)>();
                    for (int k = 0; k < n; k++)
                    {
                        float s0 = length * k / n, walked = 0;
                        for (int i = 0; i < N; i++)
                        {
                            var p0 = pts[i]; var p1 = pts[(i + 1) % N]; float seg = Vector2.Distance(p0, p1);
                            if (walked + seg < s0 && i < N - 1) { walked += seg; continue; }
                            float t = seg > 0 ? Math.Clamp((s0 - walked) / seg, 0, 1) : 0;
                            var pos = Vector2.Lerp(p0, p1, t); var tan = seg > 0 ? (p1 - p0) / seg : Vector2.UnitX;
                            var normal = new Vector2(tan.Y, -tan.X);
                            if (Vector2.Dot(normal, pos) < 0) normal = -normal;
                            spots.Add((pos, MathF.Atan2(normal.X, normal.Y), MathF.Atan2(pos.Y, pos.X)));
                            break;
                        }
                    }
                    for (int g = 0; g < gates.Count; g++)
                    {
                        int best = 0; float bd = float.MaxValue;
                        for (int k = 0; k < spots.Count; k++) { float d = MathF.Abs(MathF.IEEERemainder(spots[k].A - gates[g].A, MathF.Tau)); if (d < bd) { bd = d; best = k; } }
                        gateAt[best] = g;
                        gates[g] = (gates[g].A, spots[best].P.Length());
                    }
                    for (int k = 0; k < spots.Count; k++)
                        Add(gateAt.ContainsKey(k) && layer.Gate.Length > 0 ? layer.Gate : layer.Pieces[0].Piece, spots[k].P.X, spots[k].P.Y, spots[k].Yaw);
                    break;
                }
                case "terrace" when layer.Pieces.Count > 0:
                {
                    string piece = layer.Pieces[0].Piece; float pr = R(piece), bx = layer.X, bz = layer.Z;
                    if (terrain != null)
                    {
                        float best = float.MinValue;
                        for (int ai = 0; ai < 24; ai++)
                            for (float d = layer.Inner; d <= layer.Radius; d += 6f)
                            {
                                float a = MathF.Tau * ai / 24, x = d * MathF.Cos(a), z = d * MathF.Sin(a);
                                if (!Allowed(x, z, pr) || !Free(x, z, pr)) continue;
                                float h = terrain.Height(x, z), slope = 0;
                                for (int k = 0; k < 4; k++) slope = MathF.Max(slope, MathF.Abs(terrain.Height(x + pr * MathF.Cos(k * MathF.PI / 2), z + pr * MathF.Sin(k * MathF.PI / 2)) - h));
                                float sc = h - 0.6f * slope;
                                if (sc > best) { best = sc; bx = x; bz = z; }
                            }
                    }
                    Add(piece, bx, bz, MathF.Atan2(-bx, -bz) + layer.Yaw * MathF.PI / 180f);
                    break;
                }
                case "street" when layer.Pieces.Count > 0:
                {
                    var starts = gates.Count > 0 ? gates.ToList()
                        : Enumerable.Range(0, layer.Gates).Select(g => (layer.GateAngle * MathF.PI / 180f + MathF.Tau * g / Math.Max(1, layer.Gates), layer.Outer + 3f)).ToList();
                    foreach (var (g, gr) in starts)
                    {
                        float heading = g, limit = MathF.Min(layer.Outer, gr - 3f);
                        var at = new Vector2(layer.Inner * MathF.Cos(g), layer.Inner * MathF.Sin(g));
                        for (int guard = 0; guard < 200 && at.Length() < limit; guard++)
                        {
                            var dir = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                            var mid = at + dir * layer.Spacing / 2;
                            if (terrain != null && !Allowed(mid.X, mid.Y, 2f)) break;
                            Add(layer.Pieces[0].Piece, mid.X, mid.Y, MathF.Atan2(-dir.Y, dir.X));
                            at += dir * layer.Spacing;
                            if (layer.Meander > 0) heading += ((float)rng.NextDouble() - 0.5f) * 2f * layer.Meander * MathF.PI / 180f;
                        }
                    }
                    break;
                }
                case "scatter" when layer.Pieces.Count > 0:
                {
                    int count = (int)MathF.Round((float)(layer.Count + layer.CountPerScore * score));
                    double total = layer.Pieces.Sum(p => p.Weight);
                    bool beyondWall = wall != null && layer.Inner >= wall.Min();   // trees and the like may stand outside the wall, on any land
                    for (int placed = 0, tries = 0; placed < count && tries < count * 40; tries++)
                    {
                        double pick = rng.NextDouble() * total;
                        string piece = layer.Pieces[^1].Piece;
                        foreach (var (name, weight) in layer.Pieces) { if ((pick -= weight) <= 0) { piece = name; break; } }
                        float r = R(piece);
                        if (r <= 0) continue;
                        float a = (float)(rng.NextDouble() * MathF.Tau), u = MathF.Pow((float)rng.NextDouble(), layer.Falloff);
                        float d = layer.Inner + (layer.Outer - layer.Inner) * MathF.Sqrt(u);
                        float x = d * MathF.Cos(a), z = d * MathF.Sin(a);
                        if (!Free(x, z, r)) continue;
                        if (beyondWall ? terrain != null && MathF.Sqrt(x * x + z * z) + r > terrain.Reach(MathF.Atan2(z, x)) - 2f : !Allowed(x, z, r)) continue;
                        float yaw = layer.Face switch
                        {
                            "random" => (float)(rng.NextDouble() * MathF.Tau),
                            "radial" => MathF.Atan2(x, z),
                            _ => MathF.Atan2(-x, -z) + (float)(rng.NextDouble() - 0.5) * 0.5f,
                        };
                        Add(piece, x, z, yaw);
                        placed++;
                    }
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>The footprint radius of a model: its largest horizontal reach, trimmed a little since corners rarely touch.</summary>
    public static float RadiusOf(FigureModel model)
    {
        float r = 0;
        foreach (var p in model.Mesh.Positions) r = MathF.Max(r, MathF.Sqrt(p.X * p.X + p.Z * p.Z));
        return r * 0.8f;
    }

    /// <summary>Pieces that lie on the ground and follow it vertex by vertex (the generator cuts them into small cells); all others keep their shape and only their foot follows.</summary>
    public static bool HugsGround(string piece) => piece is "path" or "plaza" or "rampart" or "wall" or "garden";

    /// <summary>How high above a piece's base the terrain still bends it (metres): a big stone platform stands on a slope, a house only needs its feet fitted.</summary>
    public static float FootHeight(string piece) => piece switch { "palace" => 3.4f, "tower_shrine" => 1.8f, "temple_tuscan" => 2.6f, _ => 1.2f };

    /// <summary>One model of all the placements, every position multiplied by <paramref name="scale"/> first (a site shrunk to fit its location).
    /// <paramref name="ground"/> gives the height in metres of the terrain at an offset from the center (null: flat). Ground pieces follow it at every vertex;
    /// other pieces follow it at their feet (the first 1.2 m of height) and sink a little, so none floats on a slope. Each house gets its own slight tint.</summary>
    public static FigureModel Bake(string key, IReadOnlyList<SitePlacement> placements, Func<string, FigureModel?> piece, Func<float, float, float>? ground = null, float scale = 1f)
    {
        var parts = new List<FigureMesh>();
        Rig? rig = null;
        int vertices = 0, n = 0;
        foreach (var p in placements)
        {
            n++;
            var model = piece(p.Piece);
            if (model == null) continue;
            if (vertices + model.Mesh.VertexCount > MaxVertices) break;
            rig ??= model.Rig;
            var m = model.Mesh;
            float cx = p.X * scale, cz = p.Z * scale;
            float centerLift = ground?.Invoke(cx, cz) ?? 0;
            bool hug = HugsGround(p.Piece);
            var xf = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(p.Yaw) * Matrix4x4.CreateTranslation(cx, 0, cz);
            // a stable tint per placement: brightness and a warm or cool lean
            int h = Seed(p.Piece, n) & 0xffff;
            float bright = hug ? 1f : 0.88f + (h % 100) / 100f * 0.22f, warm = hug ? 0f : ((h / 100) % 21 - 10) / 10f * 0.05f;
            var colors = (byte[])m.Colors.Clone();
            if (!hug)
                for (int c = 0; c < colors.Length; c += 4)
                {
                    colors[c] = (byte)Math.Clamp(colors[c] * (bright + warm), 0, 255);
                    colors[c + 1] = (byte)Math.Clamp(colors[c + 1] * bright, 0, 255);
                    colors[c + 2] = (byte)Math.Clamp(colors[c + 2] * (bright - warm), 0, 255);
                }
            var baked = new FigureMesh
            {
                Positions = new Vector3[m.VertexCount], Normals = new Vector3[m.VertexCount], Colors = colors, Weights = m.Weights,
                Joints = m.Joints, Indices = m.Indices,
            };
            for (int i = 0; i < m.VertexCount; i++)
            {
                var v = Vector3.Transform(m.Positions[i], xf);
                float lift = 0;
                if (ground != null)
                {
                    float own = ground(v.X, v.Z);
                    float w = hug ? 1f : Math.Clamp(1f - m.Positions[i].Y / FootHeight(p.Piece), 0f, 1f);
                    lift = centerLift + (own - centerLift) * w - (hug ? 0f : 0.12f);
                }
                baked.Positions[i] = new Vector3(v.X, v.Y + lift, v.Z);
                baked.Normals[i] = Vector3.TransformNormal(m.Normals[i], xf);
            }
            parts.Add(baked);
            vertices += m.VertexCount;
        }
        if (rig == null) throw new InvalidOperationException("No site pieces to bake.");
        return new FigureModel(key, rig, FigureMesh.Merge(parts));
    }

    private static int Seed(string piece, int n) => (int)(SiteSystem.Seed(piece) * 31u + (uint)n * 2654435761u);
}
