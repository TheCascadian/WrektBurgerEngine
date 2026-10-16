using System.Numerics;
using Burger.Core.Figures;

namespace Burger.Core.Sites;

/// <summary>One piece standing in a site: metres from the site's center, heading in radians (0 = +Z, turning towards +X, as figures).</summary>
public readonly record struct SitePlacement(string Piece, float X, float Z, float Yaw);

/// <summary>Lays out a site and bakes the pieces into one mesh. Pure functions of the kit, the score and the seed: the same site looks the same on every machine.</summary>
public static class SiteLayout
{
    /// <summary>Most vertices in a baked site (the renderer indexes with 16 bits).</summary>
    public const int MaxVertices = 60000;

    /// <summary>The placements of a site. <paramref name="radiusOf"/> is the footprint radius of a piece in metres (0 for an unknown piece, which is skipped).</summary>
    public static List<SitePlacement> Build(SiteKit kit, SiteTier tier, double score, int seed, Func<string, float> radiusOf)
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
        var gateAngles = new List<float>();
        foreach (var layer in tier.Layers.Where(l => score >= l.MinScore))
        {
            switch (layer.Kind)
            {
                case "place":
                    if (layer.Pieces.Count > 0) Add(layer.Pieces[0].Piece, layer.X, layer.Z, layer.Yaw * MathF.PI / 180f);
                    break;
                case "ring" when layer.Pieces.Count > 0:
                {
                    int n = Math.Max(6, (int)MathF.Round(MathF.Tau * layer.Radius / layer.Spacing));
                    gateAngles.Clear();
                    for (int g = 0; g < layer.Gates; g++) gateAngles.Add((layer.GateAngle * MathF.PI / 180f) + MathF.Tau * g / Math.Max(1, layer.Gates));
                    for (int i = 0; i < n; i++)
                    {
                        float a = MathF.Tau * i / n;
                        bool gate = layer.Gate.Length > 0 && gateAngles.Any(g => MathF.Abs(MathF.IEEERemainder(a - g, MathF.Tau)) < MathF.Tau / n * 0.5f);
                        float x = layer.Radius * MathF.Cos(a), z = layer.Radius * MathF.Sin(a);
                        Add(gate ? layer.Gate : layer.Pieces[0].Piece, x, z, MathF.Atan2(MathF.Cos(a), MathF.Sin(a)));
                    }
                    break;
                }
                case "street" when layer.Pieces.Count > 0:
                    foreach (float g in gateAngles)
                    {
                        float yaw = MathF.Atan2(-MathF.Sin(g), MathF.Cos(g));
                        for (float d = layer.Inner + layer.Spacing / 2; d < layer.Outer; d += layer.Spacing)
                            Add(layer.Pieces[0].Piece, d * MathF.Cos(g), d * MathF.Sin(g), yaw);
                    }
                    break;
                case "scatter" when layer.Pieces.Count > 0:
                {
                    int count = (int)MathF.Round((float)(layer.Count + layer.CountPerScore * score));
                    double total = layer.Pieces.Sum(p => p.Weight);
                    for (int placed = 0, tries = 0; placed < count && tries < count * 40; tries++)
                    {
                        double pick = rng.NextDouble() * total;
                        string piece = layer.Pieces[^1].Piece;
                        foreach (var (name, weight) in layer.Pieces) { if ((pick -= weight) <= 0) { piece = name; break; } }
                        float r = R(piece);
                        if (r <= 0) continue;
                        float a = (float)(rng.NextDouble() * MathF.Tau), d = layer.Inner + (layer.Outer - layer.Inner) * MathF.Sqrt((float)rng.NextDouble());
                        float x = d * MathF.Cos(a), z = d * MathF.Sin(a);
                        if (!Free(x, z, r)) continue;
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
    public static bool HugsGround(string piece) => piece is "path" or "plaza" or "rampart";

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
                    float w = hug ? 1f : Math.Clamp(1f - m.Positions[i].Y / 1.2f, 0f, 1f);
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
