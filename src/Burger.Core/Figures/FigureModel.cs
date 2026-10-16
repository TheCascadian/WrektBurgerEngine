using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>
/// The skin of a figure: triangles whose vertices each follow up to four joints. Colour is per vertex (no textures), so a
/// figure is one draw call and can be tinted freely.
/// </summary>
public sealed class FigureMesh
{
    public Vector3[] Positions = Array.Empty<Vector3>();
    public Vector3[] Normals = Array.Empty<Vector3>();
    /// <summary>RGBA, four bytes per vertex.</summary>
    public byte[] Colors = Array.Empty<byte>();
    /// <summary>Joint indices, four per vertex.</summary>
    public byte[] Joints = Array.Empty<byte>();
    /// <summary>Joint weights, four per vertex, summing to 1.</summary>
    public float[] Weights = Array.Empty<float>();
    public int[] Indices = Array.Empty<int>();
    /// <summary>Optional names for runs of vertices (the generated body: trunk, head, leg.L, arm.R ...), used to cut clothing from it.</summary>
    public Dictionary<string, (int Start, int End)>? Parts;

    public int VertexCount => Positions.Length;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>One mesh from several that share a rig (clothing and gear on a body): vertices and indices are appended.</summary>
    public static FigureMesh Merge(IReadOnlyList<FigureMesh> meshes)
    {
        int n = meshes.Sum(m => m.VertexCount), tris = meshes.Sum(m => m.Indices.Length);
        var r = new FigureMesh
        {
            Positions = new Vector3[n], Normals = new Vector3[n], Colors = new byte[n * 4], Joints = new byte[n * 4],
            Weights = new float[n * 4], Indices = new int[tris],
        };
        int v = 0, i = 0;
        foreach (var m in meshes)
        {
            m.Positions.CopyTo(r.Positions, v); m.Normals.CopyTo(r.Normals, v);
            m.Colors.CopyTo(r.Colors, v * 4); m.Joints.CopyTo(r.Joints, v * 4); m.Weights.CopyTo(r.Weights, v * 4);
            foreach (int idx in m.Indices) r.Indices[i++] = idx + v;
            v += m.VertexCount;
        }
        return r;
    }

    /// <summary>Where the pose puts vertex <paramref name="v"/> (CPU skinning, for tests and tools; the game skins on the GPU).</summary>
    public Vector3 Skinned(int v, Matrix4x4[] skin)
    {
        var p = Vector3.Zero;
        for (int k = 0; k < 4; k++)
        {
            float w = Weights[v * 4 + k];
            if (w > 0) p += Vector3.Transform(Positions[v], skin[Joints[v * 4 + k]]) * w;
        }
        return p;
    }
}

/// <summary>A body that can be drawn and animated: a mesh, the rig it is skinned to and the clips that move the rig.</summary>
public sealed class FigureModel
{
    public string Key { get; }
    public Rig Rig { get; }
    public FigureMesh Mesh { get; }
    public Dictionary<string, Clip> Clips { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Keys of the gear pieces this model was composed from (empty for a bare body). Weapons are told apart by these names.</summary>
    public List<string> Parts { get; } = new();
    /// <summary>Height of the rest pose in metres (feet are at y = 0).</summary>
    public float Height { get; }

    public FigureModel(string key, Rig rig, FigureMesh mesh)
    {
        Key = key;
        Rig = rig;
        Mesh = mesh;
        float top = 0;
        foreach (var p in mesh.Positions) top = Math.Max(top, p.Y);
        Height = top > 0 ? top : 1;
    }
}
