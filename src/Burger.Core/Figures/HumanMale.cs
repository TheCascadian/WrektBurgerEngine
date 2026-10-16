using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>
/// The base body every human figure in the game is built on: an unclothed, unequipped adult man, 1.80 m tall, low-poly and
/// vertex-coloured, skinned to a 26-joint rig and shipped with the idle, walk and run cycles. Everything is generated from
/// the measurements below, so the same body comes out on every machine and no asset file is needed to run the game.
/// Equipment, uniforms and other bodies attach to or reuse this rig (see <see cref="FigureSystem"/>).
/// </summary>
public static partial class HumanMale
{
    public const string Key = "human_male";
    public const string Idle = "idle", Walk = "walk", Run = "run";
    /// <summary>Joke clips. Nothing in the game plays them by itself: they are reached through the <c>soldier_gag</c> console command (and, later, secrets).</summary>
    public const string Scratch = "scratch", PickNose = "pick_nose", ScratchThenPick = "scratch_pick_nose";
    public static readonly string[] Gags = { Scratch, PickNose, ScratchThenPick };
    /// <summary>Rifle drill: shoulders the rifle, fires twice with recoil and lowers it again. Not a gag, but reached the same way (<c>soldier_gag fire</c>) and played by soldiers in battle.</summary>
    public const string Fire = "fire";
    /// <summary>The other firing drills. <see cref="Fire"/> reaches over the shoulder and swings the rifle round; <see cref="FireSide"/> (the fallback) pulls it out to the side first; <see cref="FireAggressive"/> is a low, fast, hard-braced stance with three shots.</summary>
    public const string FireSide = "fire_side", FireAggressive = "fire_aggressive";
    /// <summary>Every firing drill, from the one a country gets by default to the fallbacks.</summary>
    public static readonly FireStyle[] FireStyles =
    {
        new(Fire, 5.4f, new[] { 0.36f, 0.64f }, true, new[] { (0.085f, 0f), (0.14f, 0.20f), (0.20f, 0.78f), (0.27f, 1f) }, new[] { (0.80f, 1f), (0.86f, 0.74f), (0.92f, 0.20f), (0.965f, 0f) },
            0.60f, 0.035f, -14f, -9f, -8f, 0.15f, 0f, 1f),
        new(FireSide, 5.4f, new[] { 0.36f, 0.64f }, false, new[] { (0.085f, 0f), (0.135f, 0.22f), (0.195f, 0.80f), (0.265f, 1f) }, new[] { (0.80f, 1f), (0.86f, 0.74f), (0.92f, 0.20f), (0.965f, 0f) },
            0.52f, 0.035f, -14f, -9f, -8f, 0.15f, 0f, 1f),
        new(FireAggressive, 4.2f, new[] { 0.30f, 0.45f, 0.60f }, true, new[] { (0.06f, 0f), (0.10f, 0.22f), (0.15f, 0.80f), (0.19f, 1f) }, new[] { (0.82f, 1f), (0.87f, 0.70f), (0.93f, 0.18f), (0.97f, 0f) },
            0.60f, 0.085f, -24f, -14f, -10f, 0.22f, 7f, 1.5f),
    };
    /// <summary>Name of the effect the <see cref="Fire"/> clip raises at the muzzle on each shot, and of the clip event's attachment.</summary>
    public const string MuzzleAttach = "muzzle";
    /// <summary>Name of the joint that carries the helmet or cap (a child of the head, at the head joint).</summary>
    public const string Headgear = "headgear";
    /// <summary>The slung rifle at rest, in model space: butt, direction butt to muzzle, and the muzzle itself. The joint "rifle" carries it.</summary>
    public static readonly Vector3 RifleButt = new(0.12f, 0.80f, -0.17f);
    public static readonly Vector3 RifleAxis = Vector3.Normalize(new Vector3(-0.24f, 1.06f, -0.165f));
    internal const float RifleLength = 1.105f;
    public static Vector3 MuzzleRest => RifleButt + RifleAxis * RifleLength;

    // ---- measurements (metres; +Y up, facing +Z, left is +X) ----
    private const float HipsY = 0.95f, HipX = 0.095f, HipJointY = 0.93f, KneeX = 0.092f, KneeY = 0.49f, AnkleX = 0.088f, AnkleY = 0.075f;
    private const float BallZ = 0.13f, HeelZ = -0.045f, ToeLength = 0.08f;
    private const float ArmDrop = 42f; // rest pose: arms this many degrees below horizontal (A-pose skins the shoulder better than a T)
    private static readonly Vector3 ShoulderL = new(0.185f, 1.435f, -0.005f);
    private const float ElbowS = 0.30f, WristS = 0.555f, KnuckleS = 0.655f;
    private static readonly Vector3 ThumbBase = new(0.585f, -0.004f, 0.036f); // from the shoulder, arm stretched along +X

    private static readonly Vector3 Skin = new(0.87f, 0.68f, 0.56f);

    /// <summary>Builds the body, its rig and its three clips.</summary>
    public static FigureModel Build()
    {
        var rig = BuildRig();
        var model = new FigureModel(Key, rig, BuildMesh(rig, Ancestry.Default));
        var solver = new Solver(rig);
        foreach (var clip in new[] { solver.Idle(), solver.Locomotion(Walk, WalkGait), solver.Locomotion(Run, RunGait),
            solver.Gag(Scratch, 8f, 0.15f, 0.33f, 0, 0), solver.Gag(PickNose, 8f, 0, 0, 0.12f, 0.64f), solver.Gag(ScratchThenPick, 16f, 0.06f, 0.20f, 0.20f, 0.80f) }.Concat(FireStyles.Select(solver.FireClip)).Concat(solver.Combat()).Append(solver.SpearAttackClip()).Append(solver.SpearLungeClip()).Concat(solver.CarryClips()))
            model.Clips[clip.Name] = clip;
        return model;
    }

    // ===============================================================================================
    // Rig
    // ===============================================================================================
    private static Vector3 ArmPoint(float s, float up = 0, float front = 0)
    {
        // a point of the left arm given along a T-pose arm, lowered into the rest A-pose about the shoulder
        float a = ArmDrop * MathF.PI / 180f, c = MathF.Cos(a), n = MathF.Sin(a);
        return ShoulderL + new Vector3(s * c + up * n, -s * n + up * c, front);
    }

    private static Rig BuildRig()
    {
        var world = new List<(string Name, string? Parent, Vector3 Pos)>
        {
            ("root", null, Vector3.Zero),
            ("hips", "root", new Vector3(0, HipsY, 0)),
            ("spine", "hips", new Vector3(0, 1.08f, 0)),
            ("chest", "spine", new Vector3(0, 1.24f, 0)),
            ("neck", "chest", new Vector3(0, 1.495f, 0.004f)),
            ("head", "neck", new Vector3(0, 1.575f, 0.012f)),
        };
        foreach (int side in new[] { 1, -1 })
        {
            string x = side > 0 ? ".L" : ".R";
            Vector3 M(Vector3 p) => new(p.X * side, p.Y, p.Z);
            world.Add(("shoulder" + x, "chest", M(new Vector3(0.035f, 1.455f, 0))));
            world.Add(("upper_arm" + x, "shoulder" + x, M(ArmPoint(0))));
            world.Add(("forearm" + x, "upper_arm" + x, M(ArmPoint(ElbowS))));
            world.Add(("hand" + x, "forearm" + x, M(ArmPoint(WristS))));
            world.Add(("fingers" + x, "hand" + x, M(ArmPoint(KnuckleS))));
            world.Add(("thumb" + x, "hand" + x, M(ArmPoint(ThumbBase.X, ThumbBase.Y, ThumbBase.Z))));
            world.Add(("thigh" + x, "hips", M(new Vector3(HipX, HipJointY, 0))));
            world.Add(("shin" + x, "thigh" + x, M(new Vector3(KneeX, KneeY, 0))));
            world.Add(("foot" + x, "shin" + x, M(new Vector3(AnkleX, AnkleY, 0))));
            world.Add(("toe" + x, "foot" + x, M(new Vector3(AnkleX, 0.015f, BallZ))));
        }
        world.Add(("rifle", "chest", new Vector3(0, 1.24f, 0))); // after the body joints, so every other joint keeps its index; carries the rifle piece
        world.Add((Headgear, "head", new Vector3(0, 1.575f, 0.012f))); // carries the helmet or cap: it follows the head until a clip takes it off
        var joints = new List<Joint>();
        foreach (var (name, parent, pos) in world)
        {
            int p = parent == null ? -1 : world.FindIndex(j => j.Name == parent);
            joints.Add(new Joint(name, p, p < 0 ? pos : pos - world[p].Pos, Quaternion.Identity));
        }
        return new Rig(joints);
    }

    // ===============================================================================================
    // Mesh: every part is a stack of rings lofted into a closed shell; parts overlap where limbs meet the trunk
    // ===============================================================================================
    internal readonly record struct Ring(Vector3 C, float U, float VPos, float VNeg, float Exp, (string, float)[] W, float Shade = 1f);

    internal sealed class MeshBuilder
    {
        private readonly Rig _rig;
        /// <summary>Colour of the vertices added from now on (skin until a garment or piece of gear sets its own).</summary>
        public Vector3 Base = Skin;
        private readonly List<(string Name, int Start)> _marks = new();
        public readonly List<Vector3> P = new();
        public readonly List<Vector3> Col = new();
        public readonly List<(int J, float W)[]> W = new();
        public readonly List<int> Idx = new();
        private int _partV, _partI;

        public MeshBuilder(Rig rig) { _rig = rig; }

        /// <summary>Names the vertices added from here to the next mark (or the end), so clothing can be cut from them.</summary>
        public void Mark(string name) => _marks.Add((name, P.Count));

        public Dictionary<string, (int Start, int End)> Parts()
        {
            var d = new Dictionary<string, (int, int)>();
            for (int i = 0; i < _marks.Count; i++) d[_marks[i].Name] = (_marks[i].Start, i + 1 < _marks.Count ? _marks[i + 1].Start : P.Count);
            return d;
        }

        /// <summary>A closed box from its centre and three half-extent vectors (they may be turned and need not be unit length).</summary>
        public void Box(Vector3 c, Vector3 hx, Vector3 hy, Vector3 hz, (string, float)[] w, float shade = 1f)
        {
            int b = P.Count;
            for (int i = 0; i < 8; i++)
                Add(c + hx * ((i & 1) == 0 ? -1 : 1) + hy * ((i & 2) == 0 ? -1 : 1) + hz * ((i & 4) == 0 ? -1 : 1), w, shade);
            int[][] quads = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
            foreach (var q in quads)
            {
                var centre = (P[b + q[0]] + P[b + q[1]] + P[b + q[2]] + P[b + q[3]]) / 4 - c;
                bool out_ = Vector3.Dot(Vector3.Cross(P[b + q[1]] - P[b + q[0]], P[b + q[2]] - P[b + q[0]]), centre) > 0;
                if (!out_) q.AsSpan().Reverse();
                Tri(b + q[0], b + q[1], b + q[2]); Tri(b + q[0], b + q[2], b + q[3]);
            }
            EndPart();
        }

        public int Add(Vector3 p, (string, float)[] w, float shade = 1f)
        {
            P.Add(p);
            Col.Add(Base * shade);
            float sum = w.Sum(x => x.Item2);
            W.Add(w.Select(x => (_rig.Find(x.Item1), x.Item2 / sum)).ToArray());
            return P.Count - 1;
        }

        public void Tri(int a, int b, int c) { Idx.Add(a); Idx.Add(b); Idx.Add(c); }

        /// <summary>Closes the part started at the last call: turns its triangles to face outwards.</summary>
        public void EndPart()
        {
            double vol = 0;
            for (int i = _partI; i < Idx.Count; i += 3)
                vol += Vector3.Dot(P[Idx[i]], Vector3.Cross(P[Idx[i + 1]], P[Idx[i + 2]]));
            if (vol < 0)
                for (int i = _partI; i < Idx.Count; i += 3) (Idx[i + 1], Idx[i + 2]) = (Idx[i + 2], Idx[i + 1]);
            _partV = P.Count;
            _partI = Idx.Count;
        }

        public int PartStart => _partV;

        /// <summary>A closed shell through the rings (superellipse sections in the plane of u and v), capped with a point at each end.</summary>
        public void Loft(Vector3 u, Vector3 v, int n, Ring[] rings, Vector3 capStart, Vector3 capEnd, Func<Vector3, Vector3>? xf = null)
        {
            xf ??= p => p;
            var first = new int[rings.Length];
            for (int r = 0; r < rings.Length; r++)
            {
                var ring = rings[r];
                first[r] = P.Count;
                for (int k = 0; k < n; k++)
                {
                    float a = MathF.PI * 2 * k / n, cs = MathF.Cos(a), sn = MathF.Sin(a);
                    float x = MathF.Sign(cs) * MathF.Pow(MathF.Abs(cs), 2f / ring.Exp) * ring.U;
                    float y = MathF.Sign(sn) * MathF.Pow(MathF.Abs(sn), 2f / ring.Exp) * (sn >= 0 ? ring.VPos : ring.VNeg);
                    Add(xf(ring.C + u * x + v * y), ring.W, ring.Shade);
                }
            }
            for (int r = 0; r + 1 < rings.Length; r++)
                for (int k = 0; k < n; k++)
                {
                    int a = first[r] + k, b = first[r] + (k + 1) % n, c = first[r + 1] + k, d = first[r + 1] + (k + 1) % n;
                    Tri(a, b, d);
                    Tri(a, d, c);
                }
            int s = Add(xf(capStart), rings[0].W, rings[0].Shade), e = Add(xf(capEnd), rings[^1].W, rings[^1].Shade);
            for (int k = 0; k < n; k++)
            {
                Tri(s, first[0] + (k + 1) % n, first[0] + k);
                Tri(e, first[^1] + k, first[^1] + (k + 1) % n);
            }
            EndPart();
        }

        /// <summary>A small closed spike: a base polygon joined to an apex in front and a point behind (nose, ears).</summary>
        public void Spike(Vector3[] basePts, Vector3 apex, Vector3 back, (string, float)[] w)
        {
            int b0 = P.Count;
            foreach (var p in basePts) Add(p, w);
            int a = Add(apex, w), c = Add(back, w);
            for (int k = 0; k < basePts.Length; k++)
            {
                int k1 = (k + 1) % basePts.Length;
                Tri(b0 + k, b0 + k1, a);
                Tri(b0 + k1, b0 + k, c);
            }
            EndPart();
        }
    }

    internal static (string, float)[] W(string a, float wa = 1, string? b = null, float wb = 0)
        => b == null ? new[] { (a, wa) } : new[] { (a, wa), (b, wb) };

    /// <summary>The body of <paramref name="body"/> (same rig, so every clip and piece of gear fits) with the look of a people.</summary>
    public static FigureModel BuildVariant(FigureModel body, Ancestry look)
    {
        var model = new FigureModel(body.Key + "@" + look.Key, body.Rig, BuildMesh(body.Rig, look));
        foreach (var clip in body.Clips.Values) model.Clips[clip.Name] = clip;
        return model;
    }

    private static FigureMesh BuildMesh(Rig rig, Ancestry look)
    {
        var m = new MeshBuilder(rig) { Base = look.Skin };
        m.Mark("trunk");
        Vector3 X = Vector3.UnitX, Y = Vector3.UnitY, Z = Vector3.UnitZ;

        // ---- trunk and neck: crotch to the base of the skull (u = width, v = depth: front / back) ----
        // a woman's build: narrower shoulders, a slightly narrower waist, wider hips, a little more depth at the chest
        float Fw(float y) => !look.Female ? 1f : 1f - 0.07f * Smooth(1.22f, 1.31f, y) * (1 - Smooth(1.45f, 1.49f, y)) - 0.04f * Smooth(1.05f, 1.15f, y) * (1 - Smooth(1.18f, 1.24f, y)) + 0.06f * Smooth(0.84f, 0.90f, y) * (1 - Smooth(0.98f, 1.06f, y));
        float Fd(float y) => !look.Female ? 1f : 1f + 0.06f * Smooth(1.22f, 1.30f, y) * (1 - Smooth(1.40f, 1.46f, y));
        Ring T(float y, float w, float zf, float zb, float e, (string, float)[] wt, float zc = 0) => new(new Vector3(0, y, zc), w * Fw(y), zf * Fd(y), zb, e, wt);
        m.Loft(X, Z, 16, new[]
        {
            T(0.815f, 0.050f, 0.050f, 0.055f, 2.0f, W("hips")),
            T(0.845f, 0.120f, 0.085f, 0.100f, 2.2f, W("hips")),
            T(0.900f, 0.166f, 0.100f, 0.126f, 2.3f, W("hips")),
            T(0.960f, 0.168f, 0.100f, 0.120f, 2.3f, W("hips")),
            T(1.020f, 0.150f, 0.100f, 0.098f, 2.3f, W("hips", 0.6f, "spine", 0.4f)),
            T(1.080f, 0.136f, 0.098f, 0.085f, 2.3f, W("spine", 0.7f, "hips", 0.3f)),
            T(1.150f, 0.141f, 0.102f, 0.088f, 2.4f, W("spine")),
            T(1.220f, 0.158f, 0.108f, 0.098f, 2.5f, W("spine", 0.5f, "chest", 0.5f)),
            T(1.290f, 0.177f, 0.128f, 0.108f, 2.6f, W("chest")),
            T(1.350f, 0.187f, 0.130f, 0.112f, 2.6f, W("chest")),
            T(1.410f, 0.182f, 0.110f, 0.108f, 2.5f, W("chest")),
            T(1.455f, 0.160f, 0.085f, 0.095f, 2.3f, W("chest")),
            T(1.485f, 0.095f, 0.062f, 0.078f, 2.1f, W("chest", 0.6f, "neck", 0.4f)),
            T(1.510f, 0.057f, 0.056f, 0.066f, 2.0f, W("neck", 0.7f, "chest", 0.3f), 0.004f),
            T(1.545f, 0.052f, 0.055f, 0.060f, 2.0f, W("neck"), 0.010f),
            T(1.585f, 0.050f, 0.052f, 0.058f, 2.0f, W("neck", 0.5f, "head", 0.5f), 0.014f),
        }, new Vector3(0, 0.800f, 0), new Vector3(0, 1.600f, 0.014f));

        // ---- head (u = width, v = depth: face / back of the skull) ----
        const float hz = 0.014f;
        Ring H(float y, float w, float zf, float zb, float e = 2.1f) => new(new Vector3(0, y, hz), w, zf, zb, e, W("head"));
        m.Mark("head");
        int headStart = m.PartStart;
        m.Loft(X, Z, 12, new[]
        {
            H(1.560f, 0.040f, 0.050f, 0.050f),
            H(1.578f, 0.050f * look.Jaw * (look.Female ? 0.95f : 1f), 0.078f, 0.066f, 1.9f), // chin
            H(1.602f, 0.064f * look.Jaw, 0.086f * look.Face, 0.078f, 2.0f), // mouth, angle of the jaw
            H(1.632f, 0.072f * look.Cheek, 0.087f * look.Face, 0.088f),       // cheeks
            H(1.662f, 0.075f * look.Cheek, 0.079f * look.Face, 0.096f),       // eyes sit back under the brow
            H(1.690f, 0.077f, 0.089f, 0.099f),       // brow
            H(1.725f, 0.076f, 0.086f * look.Forehead, 0.098f),       // forehead
            H(1.760f, 0.068f, 0.074f * look.Forehead, 0.088f),
            H(1.786f, 0.048f, 0.050f, 0.062f),
        }, new Vector3(0, 1.552f, hz), new Vector3(0, 1.800f, hz - 0.004f));
        m.Spike(new[] { new Vector3(0, 1.692f, 0.100f), new Vector3(0.017f * look.NoseWidth, 1.630f, 0.096f), new Vector3(0, 1.625f, 0.098f), new Vector3(-0.017f * look.NoseWidth, 1.630f, 0.096f) },
            new Vector3(0, 1.640f, 0.098f + 0.030f * look.Nose), new Vector3(0, 1.655f, 0.080f), W("head")); // nose
        foreach (int side in new[] { 1, -1 })
            m.Spike(new[] { new Vector3(0.070f * side, 1.690f, 0.016f), new Vector3(0.070f * side, 1.672f, -0.012f), new Vector3(0.070f * side, 1.640f, 0.000f), new Vector3(0.070f * side, 1.650f, 0.018f) },
                new Vector3(0.094f * side, 1.674f, -0.008f), new Vector3(0.060f * side, 1.665f, 0.004f), W("head")); // ears
        // cropped hair is only a darker scalp: nothing sits on the head
        for (int i = headStart; i < m.P.Count; i++)
        {
            var p = m.P[i];
            float top = Smooth(1.735f, 1.765f, p.Y), back = Smooth(1.615f, 1.660f, p.Y) * Smooth(0.020f, -0.030f, p.Z);
            m.Col[i] = Vector3.Lerp(m.Col[i], look.Hair, Math.Max(top, back) * (look.Style == HairStyle.Cropped ? 0.7f : 0.9f));
        }
        if (look.Style != HairStyle.Cropped) // hair: a shell over the crown and the back of the head, left open at the face
        {
            float t = look.Style switch { HairStyle.Short => 0.0045f, HairStyle.Curly => 0.014f, HairStyle.Full => 0.010f, HairStyle.Bob or HairStyle.Long => 0.011f, _ => 0.008f };
            m.Base = look.Hair;
            Ring Hr(float y, float w, float zf, float zb, float shade) => new(new Vector3(0, y, hz), w + t, zf + t, zb + t, 2.1f, W("head"), shade);
            Vector3 Nape(Vector3 p) // the lower edge runs down the back of the head and over the ears
            {
                float z = p.Z - hz, low = Math.Clamp((1.730f - p.Y) / 0.030f, 0, 1);
                p.Y -= low * (0.075f * Smooth(0.012f, 0.075f, -z) + 0.030f * Smooth(0.060f, 0.078f, MathF.Abs(p.X)) * (1 - Smooth(0.0f, 0.04f, z)));
                return p;
            }
            m.Loft(X, Z, 20, new[] { Hr(1.704f, 0.0765f, 0.0875f, 0.0985f, 0.75f), Hr(1.730f, 0.076f, 0.086f, 0.098f, 0.9f), Hr(1.760f, 0.068f, 0.074f, 0.088f, 1f), Hr(1.786f, 0.048f, 0.050f, 0.062f, 1f) },
                new Vector3(0, 1.702f, hz), new Vector3(0, 1.800f + t, hz - 0.004f), Nape);
            // hair that hangs or is gathered behind the head (it follows the head, so it stays clear of the pack by ending above the shoulders' top)
            Ring Hk(float y, float zc, float w, float d, float shade = 1f) => new(new Vector3(0, y, zc), w, d, d * 1.2f, 2.0f, W("head"), shade);
            switch (look.Style)
            {
                case HairStyle.Bob:
                    m.Loft(X, Z, 16, new[] { Hk(1.730f, hz - 0.060f, 0.082f, 0.040f, 0.9f), Hk(1.660f, hz - 0.070f, 0.086f, 0.034f), Hk(1.600f, hz - 0.068f, 0.080f, 0.028f, 0.85f) },
                        new Vector3(0, 1.735f, hz - 0.060f), new Vector3(0, 1.595f, hz - 0.068f));
                    break;
                case HairStyle.Long:
                    m.Loft(X, Z, 16, new[] { Hk(1.730f, hz - 0.060f, 0.082f, 0.040f, 0.9f), Hk(1.640f, hz - 0.075f, 0.088f, 0.034f), Hk(1.560f, hz - 0.080f, 0.086f, 0.030f), Hk(1.505f, hz - 0.078f, 0.074f, 0.026f, 0.8f) },
                        new Vector3(0, 1.735f, hz - 0.060f), new Vector3(0, 1.500f, hz - 0.078f));
                    break;
                case HairStyle.Bun:
                    m.Loft(X, Z, 12, new[] { Hk(1.712f, hz - 0.093f, 0.020f, 0.020f), Hk(1.742f, hz - 0.098f, 0.036f, 0.036f), Hk(1.775f, hz - 0.098f, 0.034f, 0.034f), Hk(1.797f, hz - 0.094f, 0.014f, 0.014f) },
                        new Vector3(0, 1.706f, hz - 0.092f), new Vector3(0, 1.802f, hz - 0.094f));
                    break;
                case HairStyle.Ponytail:
                    m.Loft(X, Z, 10, new[] { Hk(1.735f, hz - 0.095f, 0.026f, 0.026f), Hk(1.690f, hz - 0.118f, 0.034f, 0.032f), Hk(1.620f, hz - 0.128f, 0.030f, 0.028f), Hk(1.540f, hz - 0.126f, 0.020f, 0.020f), Hk(1.500f, hz - 0.124f, 0.008f, 0.008f) },
                        new Vector3(0, 1.740f, hz - 0.095f), new Vector3(0, 1.495f, hz - 0.124f));
                    break;
                case HairStyle.Braid:
                    m.Loft(X, Z, 10, new[] { Hk(1.735f, hz - 0.095f, 0.022f, 0.022f), Hk(1.690f, hz - 0.115f, 0.026f, 0.026f), Hk(1.650f, hz - 0.122f, 0.024f, 0.024f, 0.85f), Hk(1.610f, hz - 0.125f, 0.026f, 0.026f),
                        Hk(1.570f, hz - 0.125f, 0.024f, 0.024f, 0.85f), Hk(1.530f, hz - 0.124f, 0.024f, 0.024f), Hk(1.495f, hz - 0.122f, 0.010f, 0.010f, 0.85f) },
                        new Vector3(0, 1.740f, hz - 0.095f), new Vector3(0, 1.490f, hz - 0.122f));
                    break;
            }
            m.Base = look.Skin;
        }

        foreach (int side in new[] { 1, -1 })
        {
            string x = side > 0 ? ".L" : ".R";
            Vector3 Mirror(Vector3 p) => new(p.X * side, p.Y, p.Z);

            m.Mark("leg" + x);
            // ---- leg: hip to ankle (u = width, v = depth) ----
            Ring L(float y, float cx, float w, float zf, float zb, (string, float)[] wt, float shade = 1f) => new(new Vector3(cx, y, 0), w, zf, zb, 2.2f, wt, shade);
            m.Loft(X, Z, 12, new[]
            {
                L(0.970f, 0.098f, 0.060f, 0.065f, 0.070f, W("hips", 0.7f, "thigh" + x, 0.3f)),
                L(0.900f, 0.098f, 0.082f, 0.088f, 0.092f, W("hips", 0.4f, "thigh" + x, 0.6f)),
                L(0.840f, 0.097f, 0.088f, 0.092f, 0.090f, W("thigh" + x, 0.85f, "hips", 0.15f)),
                L(0.740f, 0.095f, 0.080f, 0.090f, 0.082f, W("thigh" + x)),
                L(0.640f, 0.093f, 0.068f, 0.078f, 0.070f, W("thigh" + x)),
                L(0.560f, 0.092f, 0.058f, 0.064f, 0.060f, W("thigh" + x)),
                L(0.520f, 0.092f, 0.054f, 0.060f, 0.056f, W("thigh" + x, 0.75f, "shin" + x, 0.25f)),
                L(0.490f, 0.092f, 0.053f, 0.064f, 0.052f, W("thigh" + x, 0.5f, "shin" + x, 0.5f), 0.97f), // kneecap
                L(0.455f, 0.091f, 0.049f, 0.054f, 0.054f, W("shin" + x, 0.75f, "thigh" + x, 0.25f)),
                L(0.400f, 0.090f, 0.052f, 0.050f, 0.066f, W("shin" + x)),                                   // calf
                L(0.340f, 0.090f, 0.050f, 0.046f, 0.066f, W("shin" + x)),
                L(0.250f, 0.089f, 0.040f, 0.040f, 0.050f, W("shin" + x)),
                L(0.160f, 0.088f, 0.032f, 0.034f, 0.038f, W("shin" + x)),
                L(0.100f, 0.088f, 0.031f, 0.034f, 0.036f, W("shin" + x, 0.6f, "foot" + x, 0.4f)),
                L(0.060f, 0.088f, 0.033f, 0.038f, 0.040f, W("foot" + x)),
            }, new Vector3(HipX, 1.000f, 0), new Vector3(AnkleX, 0.030f, 0), Mirror);

            m.Mark("foot" + x);
            // ---- foot: heel to toes (u = width, v = height above / below the section centre; the sole is flattened on the ground) ----
            Ring F(float z, float cy, float w, float up, float dn, (string, float)[] wt) => new(new Vector3(AnkleX, cy, z), w, up, dn, 2.6f, wt);
            m.Loft(X, Y, 8, new[]
            {
                F(-0.050f, 0.032f, 0.027f, 0.030f, 0.040f, W("foot" + x)),
                F(-0.015f, 0.040f, 0.033f, 0.042f, 0.048f, W("foot" + x)),
                F(0.035f, 0.036f, 0.038f, 0.040f, 0.044f, W("foot" + x)),
                F(0.085f, 0.028f, 0.044f, 0.030f, 0.036f, W("foot" + x)),
                F(0.130f, 0.020f, 0.049f, 0.020f, 0.028f, W("foot" + x, 0.5f, "toe" + x, 0.5f)),
                F(0.170f, 0.014f, 0.047f, 0.014f, 0.022f, W("toe" + x)),
                F(0.198f, 0.011f, 0.040f, 0.011f, 0.019f, W("toe" + x)),
            }, new Vector3(AnkleX, 0.028f, -0.062f), new Vector3(AnkleX, 0.010f, 0.212f), p => Mirror(new Vector3(p.X, Math.Max(p.Y, 0), p.Z)));

            m.Mark("arm" + x);
            // ---- arm and hand: built stretched along +X, then lowered into the A-pose (u = depth, v = up / down) ----
            Vector3 Arm(Vector3 p) => Mirror(ArmPoint(p.X, p.Y, p.Z));
            Ring A(float s, float rz, float up, float dn, (string, float)[] wt, float e = 2.2f, float shade = 1f) => new(new Vector3(s, 0, 0), rz, up, dn, e, wt, shade);
            m.Loft(Z, Y, 8, new[]
            {
                A(-0.045f, 0.045f, 0.040f, 0.045f, W("shoulder" + x)),
                A(0.000f, 0.060f, 0.060f, 0.060f, W("shoulder" + x, 0.5f, "upper_arm" + x, 0.5f)),
                A(0.050f, 0.064f, 0.063f, 0.058f, W("upper_arm" + x, 0.85f, "shoulder" + x, 0.15f)), // deltoid
                A(0.110f, 0.052f, 0.050f, 0.050f, W("upper_arm" + x)),
                A(0.170f, 0.049f, 0.046f, 0.046f, W("upper_arm" + x)),
                A(0.240f, 0.043f, 0.040f, 0.040f, W("upper_arm" + x)),
                A(0.280f, 0.040f, 0.037f, 0.039f, W("upper_arm" + x, 0.7f, "forearm" + x, 0.3f)),
                A(0.300f, 0.040f, 0.037f, 0.040f, W("upper_arm" + x, 0.5f, "forearm" + x, 0.5f), shade: 0.97f),
                A(0.325f, 0.042f, 0.039f, 0.040f, W("forearm" + x, 0.75f, "upper_arm" + x, 0.25f)),
                A(0.370f, 0.044f, 0.041f, 0.040f, W("forearm" + x)),
                A(0.440f, 0.037f, 0.034f, 0.034f, W("forearm" + x)),
                A(0.510f, 0.030f, 0.025f, 0.025f, W("forearm" + x)),
                A(0.555f, 0.028f, 0.019f, 0.019f, W("forearm" + x, 0.5f, "hand" + x, 0.5f)),
                A(0.590f, 0.037f, 0.015f, 0.016f, W("hand" + x), 3f),
                A(0.635f, 0.042f, 0.013f, 0.014f, W("hand" + x), 3f),
                A(0.655f, 0.042f, 0.012f, 0.013f, W("hand" + x, 0.5f, "fingers" + x, 0.5f), 3f),
                A(0.700f, 0.039f, 0.010f, 0.011f, W("fingers" + x), 3f),
                A(0.735f, 0.033f, 0.008f, 0.009f, W("fingers" + x), 3f),
            }, new Vector3(-0.070f, 0, 0), new Vector3(0.748f, 0, 0), Arm);

            m.Mark("thumb" + x);
            // thumb: off the front edge of the palm
            var dir = Vector3.Normalize(new Vector3(0.55f, -0.25f, 0.80f));
            var tu = Vector3.Normalize(Vector3.Cross(dir, Y));
            var tv = Vector3.Cross(tu, dir);
            Ring Th(float t, float r, (string, float)[] wt) => new(ThumbBase + dir * t, r, r * 0.85f, r * 0.85f, 2.2f, wt);
            m.Loft(tu, tv, 5, new[]
            {
                Th(0.000f, 0.014f, W("hand" + x, 0.5f, "thumb" + x, 0.5f)),
                Th(0.030f, 0.012f, W("thumb" + x)),
                Th(0.056f, 0.010f, W("thumb" + x)),
            }, ThumbBase - dir * 0.012f, ThumbBase + dir * 0.068f, Arm);
        }

        return Finish(m);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal static FigureMesh Finish(MeshBuilder m)
    {
        int n = m.P.Count;
        var mesh = new FigureMesh
        {
            Parts = m.Parts(),
            Positions = m.P.ToArray(),
            Normals = new Vector3[n],
            Colors = new byte[n * 4],
            Joints = new byte[n * 4],
            Weights = new float[n * 4],
            Indices = m.Idx.ToArray(),
        };
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            var face = Vector3.Cross(m.P[b] - m.P[a], m.P[c] - m.P[a]); // length = twice the area, so large faces count for more
            mesh.Normals[a] += face; mesh.Normals[b] += face; mesh.Normals[c] += face;
        }
        for (int i = 0; i < n; i++)
        {
            mesh.Normals[i] = mesh.Normals[i].LengthSquared() > 1e-16f ? Vector3.Normalize(mesh.Normals[i]) : Vector3.UnitY;
            mesh.Colors[i * 4] = (byte)Math.Clamp(m.Col[i].X * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 1] = (byte)Math.Clamp(m.Col[i].Y * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 2] = (byte)Math.Clamp(m.Col[i].Z * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 3] = 255;
            for (int k = 0; k < m.W[i].Length && k < 4; k++)
            {
                mesh.Joints[i * 4 + k] = (byte)m.W[i][k].J;
                mesh.Weights[i * 4 + k] = m.W[i][k].W;
            }
        }
        return mesh;
    }

    // ===============================================================================================
    // Animation: a few numbers per frame (where the pelvis and the feet are, where the arms point) are solved into joint
    // rotations, with the legs placed by two-bone IK so a planted foot really stays where it was put down.
    // ===============================================================================================
    private static Quaternion Rx(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg * MathF.PI / 180f);
    private static Quaternion Ry(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg * MathF.PI / 180f);
    private static Quaternion Rz(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg * MathF.PI / 180f);
    private static Vector3 Rot(Vector3 v, Quaternion q) => Vector3.Transform(v, q);

    /// <summary>The smallest rotation that turns direction <paramref name="a"/> into <paramref name="b"/>.</summary>
    private static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        a = Vector3.Normalize(a); b = Vector3.Normalize(b);
        float d = Vector3.Dot(a, b);
        if (d > 0.999999f) return Quaternion.Identity;
        if (d < -0.999999f)
        {
            var side = Vector3.Cross(a, Vector3.UnitX);
            if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(a, Vector3.UnitZ);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(side), MathF.PI);
        }
        var q = new Quaternion(Vector3.Cross(a, b), 1 + d);
        return Quaternion.Normalize(q);
    }

    /// <summary>What one moment of a clip asks of the body. Sides are indexed 0 = left, 1 = right.</summary>
    private sealed class Frame
    {
        public Vector3 Hips = new(0, HipsY, 0);
        public Quaternion HipsRot = Quaternion.Identity, Spine = Quaternion.Identity, Chest = Quaternion.Identity, Neck = Quaternion.Identity, Head = Quaternion.Identity;
        public readonly Vector3[] Ankle = new Vector3[2];
        /// <summary>Degrees: foot pitch (positive lifts the heel), outward turn, toes bent up.</summary>
        public readonly float[] FootPitch = new float[2], FootYaw = new float[2], Toe = new float[2];
        /// <summary>Degrees: arm swing forward, arm held out from the side, elbow bend, forearm carried across the body (0-1), shoulder raise, finger curl.</summary>
        /// <summary>The rifle joint: where it sits (offset in chest space) and how it is turned. Rest: slung on the back.</summary>
        public Vector3 RiflePos; public Quaternion RifleRot = Quaternion.Identity;
        /// <summary>Where the headgear joint is and how it is turned in chest space while it is off the head, and how much of that applies (0 keeps it on the head, 1 puts it there).</summary>
        public Vector3 HeadgearPos; public Quaternion HeadgearRot = Quaternion.Identity; public float HeadgearBlend;
        /// <summary>Wrist targets in chest space (offset from the chest joint) for a two-bone reach; null leaves the arm to its angles. Elbow hints bend it.</summary>
        public readonly Vector3?[] HandTarget = new Vector3?[2];
        /// <summary>Hand orientation in chest space (see <c>Solver.HandRotation</c>); null keeps the hand in line with the forearm, turned by HandRoll.</summary>
        public readonly Quaternion?[] HandRot = new Quaternion?[2];
        /// <summary>How much of the reach (HandTarget and HandRot) applies, 0-1: the arm and hand blend from their own angles to the reach.</summary>
        public readonly float[] HandBlend = { 1f, 1f };
        /// <summary>How much of HandRot applies when that differs from HandBlend (a spear held upright while the arm is not reaching); negative uses HandBlend.</summary>
        public readonly float[] HandRotBlend = { -1f, -1f };
        /// <summary>Degrees the forearm is turned about its own axis (what a shield on it faces).</summary>
        public readonly float[] ForearmRoll = new float[2];
        public readonly float[] HandRoll = new float[2], ArmSwing = new float[2], ArmOut = new float[2], Elbow = new float[2], ForearmIn = new float[2], Shrug = new float[2], Fingers = new float[2];
    }

    private sealed partial class Solver
    {
        private readonly Rig _rig;
        private readonly int _hips, _spine, _chest, _neck, _head;
        private readonly int[] _shoulder = new int[2], _upper = new int[2], _fore = new int[2], _hand = new int[2], _fingers = new int[2], _thumb = new int[2];
        private readonly int[] _thigh = new int[2], _shin = new int[2], _foot = new int[2], _toe = new int[2];
        private readonly Vector3[] _handAxis = new Vector3[2], _hipOffset = new Vector3[2], _thighDir = new Vector3[2], _shinDir = new Vector3[2], _armDir = new Vector3[2];
        private readonly float _thighLen, _shinLen;
        private readonly int _rifle, _headgear;
        private readonly Vector3[] _shoulderPos = new Vector3[2]; // upper-arm joints in chest space

        public Solver(Rig rig)
        {
            _rig = rig;
            _hips = rig.Find("hips"); _spine = rig.Find("spine"); _chest = rig.Find("chest"); _neck = rig.Find("neck"); _head = rig.Find("head");
            for (int s = 0; s < 2; s++)
            {
                string x = s == 0 ? ".L" : ".R";
                _shoulder[s] = rig.Find("shoulder" + x); _upper[s] = rig.Find("upper_arm" + x); _fore[s] = rig.Find("forearm" + x);
                _hand[s] = rig.Find("hand" + x); _fingers[s] = rig.Find("fingers" + x); _thumb[s] = rig.Find("thumb" + x);
                _thigh[s] = rig.Find("thigh" + x); _shin[s] = rig.Find("shin" + x); _foot[s] = rig.Find("foot" + x); _toe[s] = rig.Find("toe" + x);
                _hipOffset[s] = rig.Joints[_thigh[s]].Position;
                _thighDir[s] = Vector3.Normalize(rig.Joints[_shin[s]].Position);
                _shinDir[s] = Vector3.Normalize(rig.Joints[_foot[s]].Position);
                _armDir[s] = Vector3.Normalize(rig.Joints[_fore[s]].Position);
                _handAxis[s] = Vector3.Normalize(rig.Joints[_fingers[s]].Position);
            }
            _rifle = rig.Find("rifle"); _headgear = rig.Find(Headgear);
            var chestPos = rig.Joints[_chest].Position + rig.Joints[_spine].Position + rig.Joints[_hips].Position;
            for (int s = 0; s < 2; s++)
                _shoulderPos[s] = rig.Joints[_shoulder[s]].Position + rig.Joints[_upper[s]].Position;
            _thighLen = rig.Joints[_shin[0]].Position.Length();
            _shinLen = rig.Joints[_foot[0]].Position.Length();
        }

        /// <summary>How far the leg is from fully stretched when asked to reach the ankle (negative: it cannot reach). For the self-test.</summary>
        public float Slack(Frame f, int s) => _thighLen + _shinLen - ((f.Ankle[s] - (f.Hips + Rot(_hipOffset[s], f.HipsRot))).Length());

        public Pose Solve(Frame f)
        {
            var pose = _rig.RestPose();
            var rot = pose.Rotation;
            pose.Position[_hips] = f.Hips;
            rot[_hips] = f.HipsRot;
            rot[_spine] = f.Spine; rot[_chest] = f.Chest; rot[_neck] = f.Neck; rot[_head] = f.Head;
            var wHips = f.HipsRot;
            var wChest = wHips * f.Spine * f.Chest;
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1 : -1;

                // leg: two-bone IK from the hip joint to the ankle, knee pointing forward
                var hip = f.Hips + Rot(_hipOffset[s], wHips);
                var to = f.Ankle[s] - hip;
                float d = Math.Min(to.Length(), (_thighLen + _shinLen) * 0.9995f);
                var dir = Vector3.Normalize(to);
                var pole = Vector3.Normalize(new Vector3(0.04f * sign, 0, 1));
                pole = Vector3.Normalize(pole - dir * Vector3.Dot(pole, dir));
                float along = (_thighLen * _thighLen - _shinLen * _shinLen + d * d) / (2 * d);
                float off = MathF.Sqrt(Math.Max(0, _thighLen * _thighLen - along * along));
                var knee = hip + dir * along + pole * off;
                var wThigh = FromTo(_thighDir[s], knee - hip);
                var wShin = FromTo(Rot(_shinDir[s], wThigh), hip + dir * d - knee) * wThigh;
                var wFoot = Ry(f.FootYaw[s] * sign) * Rx(f.FootPitch[s]);
                rot[_thigh[s]] = Quaternion.Conjugate(wHips) * wThigh;
                rot[_shin[s]] = Quaternion.Conjugate(wThigh) * wShin;
                rot[_foot[s]] = Quaternion.Conjugate(wShin) * wFoot;
                rot[_toe[s]] = Rx(-f.Toe[s]);

                // arm: the upper arm and forearm are aimed, each turning as little as it can from its parent, so nothing twists
                float swing = f.ArmSwing[s] * MathF.PI / 180f, outA = f.ArmOut[s] * MathF.PI / 180f, bend = swing + f.Elbow[s] * MathF.PI / 180f;
                var upperDir = new Vector3(sign * MathF.Sin(outA), -MathF.Cos(swing) * MathF.Cos(outA), MathF.Sin(swing) * MathF.Cos(outA));
                var foreDir = new Vector3(sign * (MathF.Sin(outA) - f.ForearmIn[s]), -MathF.Cos(bend), MathF.Sin(bend));
                if (f.HandTarget[s] is { } hand) // reach: two-bone solve in chest space, elbow towards down and out
                {
                    const float up = ElbowS, lo = WristS - ElbowS;
                    var sh = _shoulderPos[s];
                    hand = Vector3.Lerp(sh + upperDir * up + foreDir * lo, hand, f.HandBlend[s]); // from where the angles put the wrist
                    var toH = hand - sh;
                    float len = Math.Clamp(toH.Length(), 0.05f, (up + lo) * 0.999f);
                    var dh = Vector3.Normalize(toH);
                    // reaching behind the shoulder a person's elbow rides up and out (arm raised, forearm dropping to the hand), not down and back
                    float behind = Math.Clamp((sh.Z - hand.Z) / 0.22f, 0f, 1f); behind *= behind * (3 - 2 * behind);
                    var hint = Vector3.Normalize(Vector3.Lerp(new Vector3(sign * 0.4f, -1f, -0.1f), new Vector3(sign * 0.9f, 0.55f, -0.15f), behind));
                    hint = Vector3.Normalize(hint - dh * Vector3.Dot(hint, dh));
                    float reach = (up * up - lo * lo + len * len) / (2 * len);
                    var elbow = sh + dh * reach + hint * MathF.Sqrt(Math.Max(0, up * up - reach * reach));
                    upperDir = Vector3.Normalize(elbow - sh);
                    foreDir = Vector3.Normalize(sh + dh * len - elbow);
                }
                var shoulderLocal = Rz(f.Shrug[s] * sign);
                var wShoulder = wChest * shoulderLocal;
                var wUpper = FromTo(Rot(_armDir[s], wShoulder), Rot(upperDir, wChest)) * wShoulder;
                var wFore = FromTo(Rot(_armDir[s], wUpper), Rot(foreDir, wChest)) * wUpper;
                rot[_shoulder[s]] = shoulderLocal;
                rot[_upper[s]] = Quaternion.Conjugate(wShoulder) * wUpper;
                if (f.ForearmRoll[s] != 0) wFore = Quaternion.CreateFromAxisAngle(Rot(_armDir[s], wFore), f.ForearmRoll[s] * MathF.PI / 180f) * wFore;
                rot[_fore[s]] = Quaternion.Conjugate(wUpper) * wFore;
                var handFree = Quaternion.CreateFromAxisAngle(_handAxis[s], f.HandRoll[s] * MathF.PI / 180f);
                rot[_hand[s]] = f.HandRot[s] is { } hr ? Quaternion.Slerp(handFree, Quaternion.Conjugate(wFore) * wChest * hr, f.HandRotBlend[s] >= 0 ? f.HandRotBlend[s] : f.HandBlend[s]) : handFree;
                rot[_fingers[s]] = Rz(-f.Fingers[s] * sign);
                rot[_thumb[s]] = Rz(-f.Fingers[s] * 0.35f * sign);
            }
            rot[_rifle] = f.RifleRot;
            pose.Position[_rifle] = f.RiflePos;
            if (f.HeadgearBlend > 0 && _headgear >= 0) PlaceHeadgear(pose, f);
            for (int i = 0; i < rot.Length; i++) rot[i] = Quaternion.Normalize(rot[i]);
            return pose;
        }

        /// <summary>Samples a looping motion into a clip: every joint gets a rotation key per sample, the hips a position key too.</summary>
        public Clip Bake(string name, float duration, int samples, Func<float, Frame> at, float speed, bool once = false)
        {
            int n = _rig.Count;
            var times = new float[samples + 1];
            var tracks = new JointTrack?[n];
            for (int j = 1; j < n; j++) tracks[j] = new JointTrack { RotationTimes = times, Rotations = new Quaternion[samples + 1] };
            tracks[_hips]!.PositionTimes = times;
            tracks[_hips]!.Positions = new Vector3[samples + 1];
            tracks[_rifle]!.PositionTimes = times;
            tracks[_rifle]!.Positions = new Vector3[samples + 1];
            if (_headgear >= 0) { tracks[_headgear]!.PositionTimes = times; tracks[_headgear]!.Positions = new Vector3[samples + 1]; }
            for (int k = 0; k <= samples; k++)
            {
                times[k] = duration * k / samples;
                var frame = at(k == samples && !once ? 0 : (float)k / samples);
                if (Carry != null) frame = Carry(frame);
                var pose = Solve(frame); // in a loop the last key is the first again, so it closes exactly
                for (int j = 1; j < n; j++)
                {
                    var q = pose.Rotation[j];
                    if (k > 0 && Quaternion.Dot(q, tracks[j]!.Rotations![k - 1]) < 0) q = Quaternion.Negate(q);
                    tracks[j]!.Rotations![k] = q;
                }
                tracks[_hips]!.Positions![k] = pose.Position[_hips];
                tracks[_rifle]!.Positions![k] = pose.Position[_rifle];
                if (_headgear >= 0) tracks[_headgear]!.Positions![k] = pose.Position[_headgear];
            }
            return new Clip(name, duration, tracks) { Loop = !once, Speed = speed };
        }

        // ------------------------------------------------------------------------------------------- idle
        /// <summary>Standing at ease: two slow breaths while the weight drifts onto one leg and back and the head looks about.</summary>
        public Clip Idle(string name = HumanMale.Idle) => Bake(name, 8f, 72, IdleFrame, 0);

        /// <summary>When set, every frame baked passes through it (the carry pose of a soldier with a shield and spear).</summary>
        public Func<Frame, Frame>? Carry;

        /// <summary>
        /// A soldier under arms: the left forearm stands upright in front of the body with the shield on it, facing forward and out, and the
        /// right hand keeps the spear upright whatever the arm does. The right arm swings less so the spear does not wave.
        /// </summary>
        public static Frame ApplyCarry(Frame f)
        {
            f.ArmSwing[0] = 10f; f.ArmOut[0] = 24f; f.Elbow[0] = 160f; f.ForearmIn[0] = 0.30f; f.ForearmRoll[0] = 90f; f.Fingers[0] = 25f; f.HandTarget[0] = null; f.HandRot[0] = null;
            f.ArmSwing[1] *= 0.35f;
            f.HandRot[1] = Quaternion.Identity; f.HandRotBlend[1] = 1f; f.Fingers[1] = 30f;
            return f;
        }

        private static float Env(float p, float a, float b, float ramp)
        {
            float up = Math.Clamp((p - a) / ramp, 0f, 1f), down = Math.Clamp((b - p) / ramp, 0, 1), t = Math.Min(up, down);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Standing at ease with the right hand busy: first to the groin for a scratch, then up to the nose for a pick, each between the
        /// given fractions of the clip (0 to 0 for none). The rest of the body is the idle's.
        /// </summary>
        public Clip Gag(string name, float duration, float scratchFrom, float scratchTo, float pickFrom, float pickTo)
            => Bake(name, duration, (int)(duration * 24), phase =>
            {
                var f = IdleFrame(duration > 8f ? phase * 2 % 1f : phase);
                float t = phase * duration;
                const int s = 1; // the right hand
                void Arm(float w, float swing, float outA, float elbow, float inward, float fingers, float roll = 0)
                {
                    f.HandRoll[s] += (roll - f.HandRoll[s]) * w;
                    f.ArmSwing[s] += (swing - f.ArmSwing[s]) * w; f.ArmOut[s] += (outA - f.ArmOut[s]) * w; f.Elbow[s] += (elbow - f.Elbow[s]) * w;
                    f.ForearmIn[s] += (inward - f.ForearmIn[s]) * w; f.Fingers[s] += (fingers - f.Fingers[s]) * w;
                }
                float ws = scratchTo > 0 ? Env(phase, scratchFrom, scratchTo, 0.045f) : 0, wn = pickTo > 0 ? Env(phase, pickFrom, pickTo, 0.045f) : 0;
                if (ws > 0)
                {
                    float rub = MathF.Sin(t * 14f); // a few light strokes
                    Arm(ws, 8f, 6f, 12f + 2f * rub * ws, 0.62f + 0.04f * rub * ws, 15f, -90f); // an open hand, palm to the body
                    f.Head *= Rx(8f * ws); // a glance down
                }
                if (wn > 0)
                {
                    float dig = MathF.Sin(t * 16f);
                    Arm(wn, 85f, 8f, 130f + 2.5f * dig * wn, 0.9f + 0.03f * dig * wn, 75f);
                    f.Head *= Rx(3f * wn);
                }
                return f;
            }, 0);


        /// <summary>
        /// A hand turned so its fingers point along <paramref name="fingers"/> with the palm facing <paramref name="palm"/> (both in chest
        /// space; the palm is made square to the fingers). The resting hand has its palm down and thumb forward.
        /// </summary>
        public Quaternion HandRotation(int s, Vector3 fingers, Vector3 palm)
        {
            float sign = s == 0 ? 1 : -1;
            var a = _handAxis[s];
            var p0 = new Vector3(sign * a.Y, -sign * a.X, 0);
            fingers = Vector3.Normalize(fingers);
            var q1 = FromTo(a, fingers);
            var p1 = Rot(p0, q1);
            var np = palm - fingers * Vector3.Dot(palm, fingers);
            np = Vector3.Normalize(np);
            float roll = MathF.Atan2(Vector3.Dot(fingers, Vector3.Cross(p1, np)), Vector3.Dot(p1, np));
            return Quaternion.CreateFromAxisAngle(fingers, roll) * q1;
        }

        private Frame IdleFrame(float phase)
        {
            {
                float breath = MathF.Sin(phase * MathF.PI * 4), shift = MathF.Sin(phase * MathF.PI * 2);
                // glances left, then right, with a pause between
                float look = MathF.Sin(phase * MathF.PI * 2) * MathF.Abs(MathF.Sin(phase * MathF.PI * 2 - 0.4f));
                float late = MathF.Sin(phase * MathF.PI * 4 - 0.9f); // the belly and arms follow the chest a little behind
                var f = new Frame
                {
                    Hips = new Vector3(0.03f * shift, HipsY - 0.012f + 0.005f * breath - 0.003f * shift * shift, -0.004f),
                    HipsRot = Rz(3.5f * shift) * Rx(1.0f),
                    Spine = Rz(-3.0f * shift) * Ry(5f * look) * Rx(1.2f * breath + 0.5f),
                    Chest = Rz(-1.2f * shift) * Ry(6f * look) * Rx(-2.5f * breath - 0.5f),
                    Neck = Rx(0.8f * breath + 1.5f) * Ry(9f * look),
                    Head = Ry(16f * look) * Rx(2.0f * MathF.Sin(phase * MathF.PI * 4 + 0.5f) - 1.0f),
                };
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? 1 : -1;
                    f.Ankle[s] = new Vector3(0.105f * sign, AnkleY, 0.012f * sign);
                    f.FootYaw[s] = 10f;
                    f.ArmSwing[s] = 2f + 1.6f * late + 1.5f * shift * sign;
                    f.ArmOut[s] = 8.5f + 1.2f * breath;
                    f.Elbow[s] = 13f + 1.5f * late;
                    f.ForearmIn[s] = 0.03f;
                    f.Shrug[s] = 1.6f * breath + 0.4f;
                    f.Fingers[s] = 28f + 3f * late;
                }
                return f;
            }
        }

        // ------------------------------------------------------------------------------------------- walk and run
        /// <summary>
        /// One stride of a gait. Each foot is on the ground for <see cref="Gait.Stance"/> of the cycle, where its contact point
        /// (the heel while the toes are up, then the ball) travels straight back at the gait's speed, then swings forward.
        /// </summary>
        public Clip Locomotion(string name, Gait g)
        {
            float stride = g.Speed * g.Duration;
            var pitch = new Curve(g.FootPitch);
            return Bake(name, g.Duration, 48, phase =>
            {
                float c = MathF.Cos(phase * MathF.PI * 2);
                float bob = MathF.Cos(MathF.PI * 4 * (phase - g.BobPhase));
                float lateral = MathF.Sin(MathF.PI * 2 * (phase - 0.05f)); // +1 when the weight is over the left foot
                var f = new Frame
                {
                    Hips = new Vector3(g.Sway * lateral, g.HipHeight + g.Bob * bob, 0),
                    HipsRot = Ry(-g.PelvisTurn * c) * Rz(g.PelvisList * lateral) * Rx(g.Lean * 0.5f),
                    Spine = Ry(g.ChestTurn * 0.5f * c) * Rz(-g.PelvisList * 0.7f * lateral) * Rx(g.Lean * 0.3f),
                    Chest = Ry(g.ChestTurn * 0.5f * c) * Rz(-g.PelvisList * 0.3f * lateral) * Rx(g.Lean * 0.2f),
                };
                // the head keeps looking where the body is going
                float chestYaw = (g.ChestTurn - g.PelvisTurn) * c;
                f.Neck = Ry(-chestYaw * 0.5f) * Rx(-g.Lean * 0.4f);
                f.Head = Ry(-chestYaw * 0.5f) * Rx(-g.Lean * 0.4f);
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? 1 : -1;
                    float p = phase + (s == 0 ? 0 : 0.5f);
                    p -= MathF.Floor(p);
                    PlaceFoot(f, s, g, pitch, stride, p);
                    // arms swing against the legs; the elbow opens behind the body and closes in front of it
                    float arm = -MathF.Cos(MathF.PI * 2 * (p - g.ArmLag));
                    f.ArmSwing[s] = g.ArmCentre + g.ArmSwing * arm;
                    f.ArmOut[s] = g.ArmOut;
                    f.Elbow[s] = g.Elbow + g.ElbowSwing * arm;
                    f.ForearmIn[s] = g.ForearmIn * (0.5f + 0.5f * arm);
                    f.Shrug[s] = g.Shrug;
                    f.Fingers[s] = g.Fingers;
                }
                return f;
            }, g.Speed);
        }

        private static readonly Vector3 HeelPoint = new(0, -AnkleY, HeelZ), BallPoint = new(0, -AnkleY, BallZ);

        private void PlaceFoot(Frame f, int s, Gait g, Curve pitch, float stride, float p)
        {
            float sign = s == 0 ? 1 : -1;
            float x = g.FootSpread * sign;
            f.FootYaw[s] = g.FootYaw;
            var yaw = Ry(g.FootYaw * sign);

            // where the ankle is when the foot is on the ground at phase q: the contact point slides back at ground speed and
            // the foot rolls over it, on the heel while the toes are up and on the ball once the heel lifts
            Vector3 Planted(float q, float deg)
            {
                var foot = yaw * Rx(deg);
                var heel = new Vector3(x, 0, g.Reach + HeelZ - stride * q);
                return deg < 0 ? heel - Rot(HeelPoint, foot) : heel + Rot(BallPoint - HeelPoint, yaw) - Rot(BallPoint, foot);
            }

            float deg = pitch.At(p);
            f.FootPitch[s] = deg;
            if (p < g.Stance)
            {
                f.Ankle[s] = Planted(p, deg);
                f.Toe[s] = Math.Max(0, deg);
                return;
            }
            // swing: from where the toes left the ground to where the heel lands, carried clear of the ground
            float u = (p - g.Stance) / (1 - g.Stance);
            float offDeg = pitch.At(g.Stance), onDeg = pitch.At(0);
            Vector3 from = Planted(g.Stance, offDeg), to = Planted(0, onDeg);
            float m = -stride * (1 - g.Stance); // the foot leaves and lands moving back with the ground
            float u2 = u * u, u3 = u2 * u;
            float z = (2 * u3 - 3 * u2 + 1) * from.Z + (u3 - 2 * u2 + u) * m + (-2 * u3 + 3 * u2) * to.Z + (u3 - u2) * m;
            float toe = Math.Max(0, offDeg) * (1 - Smooth(0, 0.3f, u));
            f.Toe[s] = toe;
            var footRot = yaw * Rx(deg);
            var tip = BallPoint + Rot(new Vector3(0, 0, ToeLength), Rx(-toe));
            float lowest = Math.Min(Rot(HeelPoint, footRot).Y, Math.Min(Rot(BallPoint, footRot).Y, Rot(tip, footRot).Y));
            float lift = MathF.Sin(MathF.PI * MathF.Pow(u, g.LiftSkew));
            f.Ankle[s] = new Vector3(x, -lowest + g.Clearance * MathF.Sin(MathF.PI * u) + g.HeelLift * lift * lift, z);
        }
    }

    /// <summary>A looping curve through (phase, value) keys that never overshoots: flat stretches stay flat.</summary>
    private sealed class Curve
    {
        private readonly float[] _t, _v, _m;

        public Curve((float T, float V)[] keys)
        {
            // keys cover phase 0..1; the last key repeats the first one cycle later
            int n = keys.Length;
            _t = keys.Select(k => k.T).ToArray();
            _v = keys.Select(k => k.V).ToArray();
            _m = new float[n];
            for (int i = 0; i < n; i++)
            {
                // slopes either side of the key, wrapping round the loop (the first and last key are the same moment)
                int prev = i == 0 ? n - 2 : i - 1, next = i == n - 1 ? 1 : i + 1;
                float dtPrev = i == 0 ? _t[n - 1] - _t[n - 2] : _t[i] - _t[i - 1];
                float dtNext = i == n - 1 ? _t[1] - _t[0] : _t[i + 1] - _t[i];
                float a = (_v[i] - _v[prev]) / dtPrev, b = (_v[next] - _v[i]) / dtNext;
                _m[i] = a * b <= 0 ? 0 : 2 * a * b / (a + b);
            }
        }

        public float At(float phase)
        {
            int i = 0;
            while (i + 2 < _t.Length && phase >= _t[i + 1]) i++;
            float h = _t[i + 1] - _t[i], u = Math.Clamp((phase - _t[i]) / h, 0, 1), u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * _v[i] + (u3 - 2 * u2 + u) * h * _m[i] + (-2 * u3 + 3 * u2) * _v[i + 1] + (u3 - u2) * h * _m[i + 1];
        }
    }

    /// <summary>The numbers that make a gait. Angles in degrees, lengths in metres, phases as a fraction of the stride.</summary>
    private sealed class Gait
    {
        public float Duration, Speed;
        /// <summary>Fraction of the stride a foot spends on the ground (above 0.5 both feet overlap: a walk; below, there is a moment in the air: a run).</summary>
        public float Stance;
        /// <summary>How far ahead of the hips the foot lands (measured at the ankle of a flat foot).</summary>
        public float Reach;
        public float HipHeight, Bob, BobPhase, Sway, PelvisTurn, PelvisList, ChestTurn, Lean;
        public float FootSpread, FootYaw, Clearance, HeelLift, LiftSkew = 1;
        public float ArmCentre, ArmSwing, ArmOut, Elbow, ElbowSwing, ForearmIn, ArmLag, Shrug, Fingers;
        /// <summary>Foot pitch through the stride, starting when the foot lands (negative: toes up; positive: heel up).</summary>
        public (float, float)[] FootPitch = Array.Empty<(float, float)>();
    }

    // 1.3 m/s at 120 steps a minute: heel strike, roll over the flat foot, push off from the toes
    private static readonly Gait WalkGait = new()
    {
        Duration = 1.0f, Speed = 1.3f, Stance = 0.60f, Reach = 0.35f,
        HipHeight = 0.895f, Bob = 0.020f, BobPhase = 0.30f, Sway = 0.018f, PelvisTurn = 6f, PelvisList = 3f, ChestTurn = 11f, Lean = 4f,
        FootSpread = 0.075f, FootYaw = 6f, Clearance = 0.022f, HeelLift = 0.02f,
        ArmCentre = 3f, ArmSwing = 22f, ArmOut = 7.5f, Elbow = 20f, ElbowSwing = 12f, ForearmIn = 0.10f, ArmLag = 0.03f, Shrug = 0.5f, Fingers = 32f,
        FootPitch = new[] { (0f, -18f), (0.10f, 0f), (0.38f, 0f), (0.60f, 50f), (0.70f, 32f), (0.86f, -10f), (1f, -18f) },
    };

    // 3 m/s: a short ground contact, a moment with both feet in the air, the heel carried up high behind
    private static readonly Gait RunGait = new()
    {
        Duration = 0.68f, Speed = 3.0f, Stance = 0.36f, Reach = 0.22f,
        HipHeight = 0.872f, Bob = -0.028f, BobPhase = 0.18f, Sway = 0.008f, PelvisTurn = 9f, PelvisList = 4f, ChestTurn = 20f, Lean = 11f,
        FootSpread = 0.055f, FootYaw = 3f, Clearance = 0.03f, HeelLift = 0.30f, LiftSkew = 0.8f,
        ArmCentre = 6f, ArmSwing = 38f, ArmOut = 11f, Elbow = 88f, ElbowSwing = 12f, ForearmIn = 0.30f, ArmLag = 0.02f, Shrug = 1.5f, Fingers = 62f,
        FootPitch = new[] { (0f, -6f), (0.05f, 0f), (0.14f, 0f), (0.36f, 55f), (0.50f, 64f), (0.78f, 15f), (0.93f, -8f), (1f, -6f) },
    };
}

/// <summary>One way of unslinging, shouldering and firing the rifle (see <see cref="HumanMale.FireStyles"/>): its length, shot times (fractions of it),
/// whether the right hand reaches over the shoulder, when the rifle moves along its path, where the right hand first grips it and the stance.</summary>
public sealed record FireStyle(string Name, float Dur, float[] Shots, bool Over, (float T, float V)[] Up, (float T, float V)[] Down, float Grip,
    float Crouch, float HipYaw, float SpineYaw, float ChestYaw, float FootFwd, float Lean, float Kick);
