using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>
/// The base light tank every light armoured vehicle in the game is built on: a generic 1930s two-man tank, 5.1 m long with
/// its gun, 2.64 m wide and 2.2 m tall (a rifleman is 1.80 m, see <see cref="HumanMale"/>), carrying a turret with a
/// 40 mm gun. It is generated from the measurements below, so it needs no asset file. The model is rigid parts on a rig of
/// 21 joints: <c>hull</c> carries everything, the <c>turret</c> turns on it, the <c>gun</c> elevates on the turret and the
/// <c>barrel</c> slides back and forth in the gun to recoil. On each side (<c>.L</c> / <c>.R</c>) a <c>sprocket</c>, an
/// <c>idler</c> and four road wheels <c>wheel1</c>..<c>wheel4</c> (rear to front) turn, and the track shows its movement
/// with the cleats of the top and bottom run (<c>cleats_top</c>, <c>cleats_bottom</c>) sliding along. Clips: <c>idle</c>
/// (engine running), <c>drive</c> (tracks and wheels turn, travels 3.3 m/s), <c>fire</c> (one shot, the barrel recoils and
/// runs back) and <c>aim</c> (the turret sweeps and the gun rises and falls).
/// Space is glTF's: metres, +Y up, the tank faces +Z and its left side is +X, standing on the origin.
/// </summary>
public static class LightTank
{
    public const string Key = "light_tank";
    public const string Idle = "idle", Drive = "drive", Fire = "fire", Aim = "aim";

    // ---- measurements (metres) ----
    private const float TrackX = 0.98f, TrackWidth = 0.40f, TrackThickness = 0.03f, CleatHeight = 0.025f;
    private const float WheelR = 0.30f, BeltR = 0.315f;          // wheels roll on the inside of the track: BeltR is the middle of the track around them
    private const float AxleY = CleatHeight + TrackThickness / 2 + BeltR;
    private const int CleatsPerTurn = 20, CleatsOnRun = 38;
    private static readonly float Pitch = MathF.Tau * BeltR / CleatsPerTurn;     // distance between cleats along the track
    private static readonly float RunLength = CleatsOnRun * Pitch;               // sprocket to idler
    private const float CycleTime = 0.6f;                                        // drive: the track moves one full turn of a wheel
    private static readonly Vector3 HullPivot = new(0, AxleY, 0), TurretPivot = new(0, 1.42f, -0.05f), GunPivot = new(0, 1.74f, 0.70f);

    /// <summary>How fast the tracks of the <c>drive</c> clip carry the tank, in metres per second.</summary>
    public static float DriveSpeed => CleatsPerTurn * Pitch / CycleTime;

    private static readonly string[] WheelNames = { "sprocket", "wheel1", "wheel2", "wheel3", "wheel4", "idler" };

    public static FigureModel Build()
    {
        var rig = BuildRig();
        var model = new FigureModel(Key, rig, BuildMesh(rig));
        foreach (var clip in new[] { IdleClip(rig), DriveClip(rig), FireClip(rig), AimClip(rig) })
            model.Clips[clip.Name] = clip;
        return model;
    }

    // ===============================================================================================
    // Rig
    // ===============================================================================================
    private static float WheelZ(int i) => -RunLength / 2 + RunLength * i / 5;

    private static Rig BuildRig()
    {
        var world = new List<(string Name, string? Parent, Vector3 Pos)>
        {
            ("root", null, Vector3.Zero),
            ("hull", "root", HullPivot),
            ("turret", "hull", TurretPivot),
            ("gun", "turret", GunPivot),
            ("barrel", "gun", GunPivot),
        };
        foreach (int side in new[] { 1, -1 })
        {
            string x = side > 0 ? ".L" : ".R";
            for (int i = 0; i < WheelNames.Length; i++)
                world.Add((WheelNames[i] + x, "hull", new Vector3(side * TrackX, AxleY, WheelZ(i))));
            world.Add(("cleats_top" + x, "hull", new Vector3(side * TrackX, AxleY + BeltR, 0)));
            world.Add(("cleats_bottom" + x, "hull", new Vector3(side * TrackX, AxleY - BeltR, 0)));
        }
        var joints = new List<Joint>();
        foreach (var (name, parent, pos) in world)
        {
            int p = parent == null ? -1 : world.FindIndex(j => j.Name == parent);
            joints.Add(new Joint(name, p, p < 0 ? pos : pos - world[p].Pos, Quaternion.Identity));
        }
        return new Rig(joints);
    }

    // ===============================================================================================
    // Mesh: closed parts, flat shaded (a part is drawn by its joint only; every vertex has weight 1)
    // ===============================================================================================
    private sealed class Builder
    {
        private readonly Rig _rig;
        private int _joint;
        private Vector3 _color;
        private readonly List<(Vector3 A, Vector3 B, Vector3 C, Vector3? Na, Vector3? Nb, Vector3? Nc)> _tris = new();
        public readonly List<Vector3> P = new(), N = new();
        public readonly List<byte> Colors = new(), Joints = new();
        public readonly List<int> Indices = new();

        public Builder(Rig rig) { _rig = rig; }

        public void Begin(string joint, Vector3 color)
        {
            _joint = _rig.Find(joint);
            if (_joint < 0) throw new InvalidOperationException($"No joint '{joint}'.");
            _color = color;
            _tris.Clear();
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c) => _tris.Add((a, b, c, null, null, null));
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc) => _tris.Add((a, b, c, na, nb, nc));
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>Closes the part: turns its triangles to face outwards, then adds them to the mesh.</summary>
        public void End()
        {
            double volume = 0;
            foreach (var t in _tris) volume += Vector3.Dot(t.A, Vector3.Cross(t.B, t.C));
            bool flip = volume < 0;
            foreach (var t in _tris)
            {
                var (a, b, c, na, nb, nc) = t;
                if (flip) (b, c, nb, nc) = (c, b, nc, nb);
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-12f) continue;
                var flat = Vector3.Normalize(cross);
                Add(a, na ?? flat); Add(b, nb ?? flat); Add(c, nc ?? flat);
            }
            _tris.Clear();
        }

        private void Add(Vector3 p, Vector3 n)
        {
            // ground dirt: everything is a little darker low down
            float dirt = 0.68f + 0.32f * Math.Clamp((p.Y - 0.05f) / 0.75f, 0, 1);
            Indices.Add(P.Count);
            P.Add(p);
            N.Add(n);
            Colors.Add((byte)Math.Round(Math.Clamp(_color.X * dirt, 0, 1) * 255));
            Colors.Add((byte)Math.Round(Math.Clamp(_color.Y * dirt, 0, 1) * 255));
            Colors.Add((byte)Math.Round(Math.Clamp(_color.Z * dirt, 0, 1) * 255));
            Colors.Add(255);
            Joints.Add((byte)_joint); Joints.Add(0); Joints.Add(0); Joints.Add(0);
        }

        /// <summary>A closed shell through rings of the same number of points (capped at the ends, or closed into a loop).</summary>
        public void Loft(IReadOnlyList<Vector3[]> rings, bool loop = false, bool caps = true)
        {
            int n = rings[0].Length, count = rings.Count;
            for (int r = 0; r + (loop ? 0 : 1) < count; r++)
            {
                var a = rings[r]; var b = rings[(r + 1) % count];
                for (int k = 0; k < n; k++)
                {
                    int k1 = (k + 1) % n;
                    Quad(a[k], a[k1], b[k1], b[k]);
                }
            }
            if (!caps || loop) return;
            var s = Centre(rings[0]); var e = Centre(rings[^1]);
            for (int k = 0; k < n; k++)
            {
                Tri(s, rings[0][(k + 1) % n], rings[0][k]);
                Tri(e, rings[^1][k], rings[^1][(k + 1) % n]);
            }
        }

        private static Vector3 Centre(Vector3[] ring)
        {
            var c = Vector3.Zero;
            foreach (var p in ring) c += p;
            return c / ring.Length;
        }

        /// <summary>A box (or a tapering one: half sizes at both ends) of the given half sizes along z, rotated and placed.</summary>
        public void Box(Vector3 centre, Vector3 half, Quaternion? rot = null, Vector2? endHalf = null)
        {
            var q = rot ?? Quaternion.Identity;
            var e = endHalf ?? new Vector2(half.X, half.Y);
            Vector3 T(float x, float y, float z) => centre + Vector3.Transform(new Vector3(x, y, z), q);
            Loft(new[]
            {
                new[] { T(-half.X, -half.Y, -half.Z), T(half.X, -half.Y, -half.Z), T(half.X, half.Y, -half.Z), T(-half.X, half.Y, -half.Z) },
                new[] { T(-e.X, -e.Y, half.Z), T(e.X, -e.Y, half.Z), T(e.X, e.Y, half.Z), T(-e.X, e.Y, half.Z) },
            });
        }

        /// <summary>
        /// A solid of revolution about <paramref name="axis"/> through <paramref name="centre"/>. The profile is (radius, distance
        /// along the axis) going round the section counter-clockwise (outwards first along the bottom), from the axis or as a closed
        /// ring. Faces are smooth where the profile bends gently and sharp where it turns hard.
        /// </summary>
        public void Lathe(Vector3 centre, Vector3 axis, (float R, float T)[] profile, int n, bool smooth = true)
        {
            axis = Vector3.Normalize(axis);
            var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var v = Vector3.Cross(axis, u);
            int segs = profile.Length - 1;
            var segN = new Vector2[segs];
            for (int i = 0; i < segs; i++)
            {
                var d = new Vector2(profile[i + 1].R - profile[i].R, profile[i + 1].T - profile[i].T);
                segN[i] = d.LengthSquared() < 1e-12f ? Vector2.Zero : Vector2.Normalize(new Vector2(d.Y, -d.X));
            }
            bool closed = Vector2.DistanceSquared(new Vector2(profile[0].R, profile[0].T), new Vector2(profile[^1].R, profile[^1].T)) < 1e-10f;
            Vector2 Soft(int seg, int other)
            {
                var a = segN[seg];
                if (!smooth) return a;
                if (closed) other = (other + segs) % segs;
                else if (other < 0 || other >= segs) return a;
                var c = segN[other];
                return c != Vector2.Zero && Vector2.Dot(a, c) > 0.766f ? Vector2.Normalize(a + c) : a;
            }
            for (int i = 0; i < segs; i++)
            {
                if (segN[i] == Vector2.Zero) continue;
                var n0 = Soft(i, i - 1); var n1 = Soft(i, i + 1);
                for (int k = 0; k < n; k++)
                {
                    float a0 = MathF.Tau * k / n, a1 = MathF.Tau * (k + 1) / n;
                    Vector3 Radial(float a) => u * MathF.Cos(a) + v * MathF.Sin(a);
                    Vector3 Pt(int p, float a) => centre + axis * profile[p].T + Radial(a) * profile[p].R;
                    Vector3 Nr(Vector2 nn, float a) => Vector3.Normalize(Radial(a) * nn.X + axis * nn.Y);
                    var p00 = Pt(i, a0); var p01 = Pt(i, a1); var p10 = Pt(i + 1, a0); var p11 = Pt(i + 1, a1);
                    if (!smooth)
                    {
                        if (profile[i].R > 0) Tri(p00, p01, p11);
                        if (profile[i + 1].R > 0) Tri(p00, p11, p10);
                        continue;
                    }
                    if (profile[i].R > 0) Tri(p00, p01, p11, Nr(n0, a0), Nr(n0, a1), Nr(n1, a1));
                    if (profile[i + 1].R > 0) Tri(p00, p11, p10, Nr(n0, a0), Nr(n1, a1), Nr(n1, a0));
                }
            }
        }
    }

    private static Vector3 Rgb(float r, float g, float b) => new(r, g, b);

    private static readonly Vector3 Olive = Rgb(0.34f, 0.37f, 0.23f), OliveDark = Rgb(0.26f, 0.29f, 0.18f), Steel = Rgb(0.30f, 0.31f, 0.30f),
        Rubber = Rgb(0.10f, 0.10f, 0.095f), Track = Rgb(0.17f, 0.16f, 0.14f), Cleat = Rgb(0.25f, 0.22f, 0.18f), Gun = Rgb(0.17f, 0.18f, 0.18f),
        Rust = Rgb(0.30f, 0.18f, 0.11f), Glass = Rgb(0.55f, 0.58f, 0.50f), Dark = Rgb(0.07f, 0.07f, 0.07f);

    // a hull section: bottom corners (w, yb), side tops (w, ym), deck corners (wt, yt)
    private static Vector3[] Section(float z, float w, float yb, float ym, float yt, float wt) => new[]
    {
        new Vector3(-w, yb, z), new Vector3(w, yb, z), new Vector3(w, ym, z), new Vector3(wt, yt, z), new Vector3(-wt, yt, z), new Vector3(-w, ym, z),
    };

    private static Vector3[] Round(Vector3 centre, float rx, float rz, int n = 12) =>
        Enumerable.Range(0, n).Select(k => centre + new Vector3(MathF.Cos(MathF.Tau * k / n) * rx, 0, MathF.Sin(MathF.Tau * k / n) * rz)).ToArray();

    private static FigureMesh BuildMesh(Rig rig)
    {
        var b = new Builder(rig);
        var X = Vector3.UnitX; var Y = Vector3.UnitY; var Z = Vector3.UnitZ;

        // ---- hull: the lower body between the tracks, the superstructure over it ----
        b.Begin("hull", OliveDark);
        b.Loft(new[]
        {
            Section(-2.30f, 0.56f, 0.50f, 0.70f, 0.98f, 0.62f),
            Section(-2.12f, 0.72f, 0.30f, 0.62f, 0.98f, 0.76f),
            Section(1.60f, 0.72f, 0.30f, 0.62f, 0.98f, 0.76f),
            Section(2.30f, 0.52f, 0.52f, 0.75f, 0.90f, 0.62f),
        });
        b.End();
        b.Begin("hull", Olive);
        b.Loft(new[]
        {
            Section(-2.28f, 0.92f, 0.90f, 1.00f, 1.06f, 0.84f),
            Section(-1.90f, 1.00f, 0.90f, 1.12f, 1.22f, 0.92f),
            Section(-0.90f, 1.00f, 0.90f, 1.12f, 1.22f, 0.92f),
            Section(-0.90f, 1.02f, 0.90f, 1.26f, 1.42f, 0.94f),
            Section(1.10f, 1.02f, 0.90f, 1.26f, 1.42f, 0.94f),
            Section(2.05f, 0.98f, 0.90f, 1.02f, 1.10f, 0.80f),
        });
        b.End();

        // turret ring, driver's hatch and visor, engine deck, lights, tow eyes, exhaust, stowage
        b.Begin("hull", Steel);
        b.Lathe(new Vector3(0, 1.40f, TurretPivot.Z), Y, new (float, float)[] { (0.60f, 0), (0.80f, 0), (0.80f, 0.06f), (0.60f, 0.06f), (0.60f, 0) }, 24, false);
        b.End();
        b.Begin("hull", OliveDark);
        b.Box(new Vector3(0.36f, 1.445f, 0.92f), new Vector3(0.20f, 0.035f, 0.17f));
        b.Box(new Vector3(0.36f, 1.28f, 1.60f), new Vector3(0.22f, 0.025f, 0.12f), Quaternion.CreateFromAxisAngle(X, 0.33f));
        b.Box(new Vector3(0, 1.235f, -1.50f), new Vector3(0.46f, 0.014f, 0.44f));
        b.End();
        b.Begin("hull", Dark);
        b.Box(new Vector3(0.36f, 1.275f, 1.665f), new Vector3(0.17f, 0.012f, 0.07f), Quaternion.CreateFromAxisAngle(X, 0.33f)); // driver's vision slit
        for (int i = 0; i < 6; i++) b.Box(new Vector3(0, 1.255f, -1.86f + i * 0.145f), new Vector3(0.40f, 0.012f, 0.028f));      // engine louvres
        b.End();
        b.Begin("hull", Steel);
        foreach (int side in new[] { 1, -1 })
        {
            // fenders over the tracks, with a tilted mud guard in front
            b.Box(new Vector3(side * 1.02f, 0.85f, -0.15f), new Vector3(0.30f, 0.016f, 2.05f));
            b.Box(new Vector3(side * 1.02f, 0.80f, 2.07f), new Vector3(0.30f, 0.016f, 0.20f), Quaternion.CreateFromAxisAngle(X, 0.38f));
            b.Box(new Vector3(side * 1.02f, 0.88f, -2.23f), new Vector3(0.30f, 0.016f, 0.10f), Quaternion.CreateFromAxisAngle(X, -0.30f));
            // tool box on the fender, tow eye on the nose
            b.Box(new Vector3(side * 1.12f, 0.94f, -0.80f), new Vector3(0.09f, 0.075f, 0.30f));
            b.Box(new Vector3(side * 0.36f, 0.60f, 2.32f), new Vector3(0.05f, 0.05f, 0.04f));
        }
        b.End();
        b.Begin("hull", Glass);
        foreach (int side in new[] { 1, -1 })
            b.Lathe(new Vector3(side * 1.15f, 0.92f, 1.70f), Z, new (float, float)[] { (0, 0), (0.055f, 0), (0.055f, 0.07f), (0.04f, 0.09f), (0, 0.09f) }, 8);
        b.End();
        b.Begin("hull", Rust);
        b.Lathe(new Vector3(-0.58f, 1.34f, -1.60f), Z, new (float, float)[] { (0, -0.05f), (0.09f, -0.05f), (0.105f, 0.05f), (0.105f, 0.60f), (0.09f, 0.70f), (0, 0.70f) }, 10);
        b.Lathe(new Vector3(-0.58f, 1.34f, -1.65f), new Vector3(0, -0.4f, -1), new (float, float)[] { (0, -0.05f), (0.04f, -0.05f), (0.04f, 0.50f), (0, 0.50f) }, 8);
        b.End();

        // ---- track: the belt around the wheels (the same on both sides) ----
        foreach (int side in new[] { 1, -1 })
        {
            b.Begin("hull", Track);
            var rings = new List<Vector3[]>();
            void Ring(float phi, float cz)
            {
                float sn = MathF.Sin(phi), cs = MathF.Cos(phi);
                var c = new Vector3(side * TrackX, AxleY + BeltR * cs, cz + BeltR * sn);
                var nrm = new Vector3(0, cs, sn);
                var o = c + nrm * TrackThickness / 2; var i = c - nrm * TrackThickness / 2;
                float hw = TrackWidth / 2;
                rings.Add(new[] { o - X * hw, o + X * hw, i + X * hw, i - X * hw });
            }
            for (int s = 0; s <= 12; s++) Ring(MathF.PI * s / 12, RunLength / 2);              // over the front wheel, top to bottom
            for (int s = 0; s <= 12; s++) Ring(MathF.PI + MathF.PI * s / 12, -RunLength / 2);  // round the rear wheel, bottom to top
            b.Loft(rings, loop: true);
            b.End();
        }

        // ---- wheels ----
        foreach (int side in new[] { 1, -1 })
        {
            string x = side > 0 ? ".L" : ".R";
            for (int i = 0; i < WheelNames.Length; i++)
            {
                string joint = WheelNames[i] + x;
                var c = new Vector3(side * TrackX, AxleY, WheelZ(i));
                var axis = new Vector3(side, 0, 0);
                bool sprocket = i == 0, idler = i == WheelNames.Length - 1;
                b.Begin(joint, sprocket ? Steel : Rubber);
                b.Lathe(c, axis, new (float, float)[] { (0.19f, -0.16f), (0.285f, -0.16f), (WheelR, -0.14f), (WheelR, 0.14f), (0.285f, 0.16f), (0.19f, 0.16f), (0.19f, -0.16f) }, 20);
                b.End();
                b.Begin(joint, sprocket ? Rust : Steel);
                b.Lathe(c, axis, new (float, float)[] { (0, -0.20f), (0.07f, -0.20f), (0.07f, -0.12f), (0.195f, -0.12f), (0.195f, 0.11f), (0.09f, 0.125f), (0, 0.125f) }, 20);
                b.End();
                b.Begin(joint, sprocket ? Rust : OliveDark);
                float hub = idler ? 0.10f : 0.075f;
                b.Lathe(c, axis, new (float, float)[] { (0, 0.115f), (hub, 0.115f), (hub * 0.85f, 0.15f), (0, 0.16f) }, 20);
                b.End();
                b.Begin(joint, Dark);
                for (int k = 0; k < 10; k++)
                {
                    float a = MathF.Tau * k / 10;
                    var at = c + axis * 0.115f + new Vector3(0, MathF.Cos(a), MathF.Sin(a)) * (sprocket ? 0.165f : 0.14f);
                    b.Lathe(at, axis, new (float, float)[] { (0, 0), (0.02f, 0), (0.02f, 0.03f), (0, 0.03f) }, 5, false);
                }
                b.End();
            }
        }

        // ---- cleats: the grousers of the track's top and bottom run ----
        foreach (int side in new[] { 1, -1 })
        {
            string x = side > 0 ? ".L" : ".R";
            float outer = BeltR + TrackThickness / 2, mid = outer + CleatHeight / 2;
            b.Begin("cleats_top" + x, Cleat);
            for (int k = 0; k < CleatsOnRun; k++) b.Box(new Vector3(side * TrackX, AxleY + mid, -RunLength / 2 + k * Pitch), new Vector3(TrackWidth / 2 - 0.02f, CleatHeight / 2, Pitch * 0.14f));
            b.End();
            b.Begin("cleats_bottom" + x, Cleat);
            for (int k = 0; k < CleatsOnRun; k++) b.Box(new Vector3(side * TrackX, AxleY - mid, -RunLength / 2 + k * Pitch), new Vector3(TrackWidth / 2 - 0.02f, CleatHeight / 2, Pitch * 0.14f));
            b.End();
        }

        // ---- turret ----
        b.Begin("turret", Olive);
        b.Loft(new[]
        {
            Round(new Vector3(0, 1.44f, -0.05f), 0.72f, 0.78f),
            Round(new Vector3(0, 1.62f, -0.05f), 0.70f, 0.76f),
            Round(new Vector3(0, 1.86f, -0.07f), 0.64f, 0.70f),
            Round(new Vector3(0, 2.02f, -0.10f), 0.54f, 0.60f),
        });
        b.End();
        b.Begin("turret", OliveDark);
        b.Box(new Vector3(0, 1.64f, -0.82f), new Vector3(0.50f, 0.20f, 0.19f), null, new Vector2(0.46f, 0.17f)); // rear bustle
        b.Box(new Vector3(0.40f, 2.07f, 0.22f), new Vector3(0.08f, 0.045f, 0.08f));                               // gunner's periscope
        b.End();
        b.Begin("turret", Dark);
        foreach (int side in new[] { 1, -1 })
            b.Box(new Vector3(side * 0.68f, 1.74f, 0.05f), new Vector3(0.012f, 0.03f, 0.14f));               // vision slits
        b.End();
        b.Begin("turret", Steel);
        b.Lathe(new Vector3(-0.14f, 2.0f, -0.18f), Y, new (float, float)[] { (0, 0), (0.25f, 0), (0.25f, 0.14f), (0.22f, 0.17f), (0, 0.17f) }, 12); // commander's cupola
        b.End();
        b.Begin("turret", OliveDark);
        b.Lathe(new Vector3(-0.14f, 2.17f, -0.18f), Y, new (float, float)[] { (0, 0), (0.20f, 0), (0.20f, 0.03f), (0, 0.03f) }, 12);                // its hatch
        b.End();

        // ---- gun: the mantlet and jacket elevate; the barrel slides in them ----
        b.Begin("gun", OliveDark);
        b.Box(new Vector3(0, GunPivot.Y, 0.61f), new Vector3(0.28f, 0.23f, 0.09f), null, new Vector2(0.20f, 0.17f));
        b.End();
        b.Begin("gun", Gun);
        b.Lathe(GunPivot, Z, new (float, float)[] { (0, 0), (0.085f, 0), (0.085f, 0.40f), (0.065f, 0.43f), (0, 0.43f) }, 12);
        b.End();
        b.Begin("barrel", Gun);
        b.Lathe(GunPivot, Z, new (float, float)[]
        {
            (0, -0.10f), (0.047f, -0.10f), (0.047f, 1.85f), (0.056f, 1.86f), (0.056f, 1.90f), (0.047f, 1.91f), (0.047f, 2.00f),
            (0.066f, 2.00f), (0.066f, 2.02f), (0.056f, 2.03f), (0.056f, 2.07f), (0.066f, 2.08f), (0.066f, 2.12f), (0.040f, 2.13f), (0, 2.13f),
        }, 12);
        b.End();

        return new FigureMesh
        {
            Positions = b.P.ToArray(),
            Normals = b.N.ToArray(),
            Colors = b.Colors.ToArray(),
            Joints = b.Joints.ToArray(),
            Weights = Enumerable.Range(0, b.P.Count * 4).Select(i => i % 4 == 0 ? 1f : 0f).ToArray(),
            Indices = b.Indices.ToArray(),
        };
    }

    // ===============================================================================================
    // Clips
    // ===============================================================================================
    private static Quaternion Rx(float rad) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, rad);
    private static Quaternion Ry(float rad) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, rad);
    private static float Rad(float degrees) => degrees * MathF.PI / 180f;

    private static JointTrack Rot(float[] times, Func<float, Quaternion> at) => new() { RotationTimes = times, Rotations = times.Select(at).ToArray() };
    private static JointTrack Pos(float[] times, Vector3 rest, Func<float, Vector3> offset) => new() { PositionTimes = times, Positions = times.Select(t => rest + offset(t)).ToArray() };
    private static float[] Times(float duration, int count) => Enumerable.Range(0, count + 1).Select(i => duration * i / count).ToArray();

    private static JointTrack?[] Empty(Rig rig) => new JointTrack?[rig.Count];

    /// <summary>The engine running: the hull shudders a little.</summary>
    private static Clip IdleClip(Rig rig)
    {
        const float duration = 1f;
        var tracks = Empty(rig);
        int hull = rig.Find("hull"), turret = rig.Find("turret");
        var t = Times(duration, 36);
        tracks[hull] = Rot(t, time => Rx(Rad(0.05f) * MathF.Sin(MathF.Tau * 6 * time / duration)) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Rad(0.04f) * MathF.Sin(MathF.Tau * 5 * time / duration)));
        tracks[hull]!.PositionTimes = t;
        tracks[hull]!.Positions = t.Select(time => rig.Joints[hull].Position + new Vector3(0, 0.0015f * MathF.Sin(MathF.Tau * 6 * time / duration + 1f), 0)).ToArray();
        return new Clip(Idle, duration, tracks);
    }

    /// <summary>Driving: wheels and cleats turn exactly as fast as the tank travels, so the tracks do not slip on the ground.</summary>
    private static Clip DriveClip(Rig rig)
    {
        var tracks = Empty(rig);
        var turn = Times(CycleTime, 16);
        int hull = rig.Find("hull");
        tracks[hull] = Rot(turn, time => Rx(Rad(-0.25f) * MathF.Sin(MathF.Tau * 2 * time / CycleTime)));
        tracks[hull]!.PositionTimes = turn;
        tracks[hull]!.Positions = turn.Select(time => rig.Joints[hull].Position + new Vector3(0, 0.003f * (1 + MathF.Sin(MathF.Tau * 3 * time / CycleTime)), 0)).ToArray();
        // the cleats slide one pitch and are then where the next cleat was: a sawtooth, 20 of them in a cycle
        var saw = new List<float>();
        for (int s = 0; s < CleatsPerTurn; s++)
        {
            saw.Add(CycleTime * s / CleatsPerTurn);
            saw.Add(CycleTime * (s + 1) / CleatsPerTurn - 1e-4f);
        }
        saw.Add(CycleTime);
        var sawTimes = saw.ToArray();
        float Slide(float time)
        {
            float slot = CycleTime / CleatsPerTurn;
            float within = time - MathF.Floor(time / slot) * slot;
            return time >= CycleTime - 1e-6f ? 0 : Pitch * Math.Clamp(within / (slot - 1e-4f), 0, 1);
        }
        foreach (string x in new[] { ".L", ".R" })
        {
            foreach (string wheel in WheelNames)
                tracks[rig.Find(wheel + x)] = Rot(turn, time => Rx(MathF.Tau * time / CycleTime)); // top of the wheel runs forward
            int top = rig.Find("cleats_top" + x), bottom = rig.Find("cleats_bottom" + x);
            tracks[top] = Pos(sawTimes, rig.Joints[top].Position, time => new Vector3(0, 0, Slide(time)));
            tracks[bottom] = Pos(sawTimes, rig.Joints[bottom].Position, time => new Vector3(0, 0, -Slide(time)));
        }
        return new Clip(Drive, CycleTime, tracks) { Speed = DriveSpeed };
    }

    /// <summary>One round: the barrel throws back and runs out again, the muzzle kicks up and the hull rocks.</summary>
    private static Clip FireClip(Rig rig)
    {
        const float duration = 1.8f;
        var tracks = Empty(rig);
        int hull = rig.Find("hull"), gun = rig.Find("gun"), barrel = rig.Find("barrel");
        var t = Times(duration, 36);
        float Recoil(float time) => time < 0.05f ? time / 0.05f : 1 - SmoothStep(0.15f, 1.2f, time);
        tracks[barrel] = Pos(t, rig.Joints[barrel].Position, time => new Vector3(0, 0, -0.36f * Recoil(time)));
        tracks[gun] = Rot(t, time => Rx(Rad(-1.2f) * Recoil(time)));
        tracks[hull] = Rot(t, time => Rx(Rad(-0.35f) * Recoil(time)));
        return new Clip(Fire, duration, tracks) { Loop = false };
    }

    /// <summary>Looking for a target: the turret sweeps left and right, the gun rises and falls.</summary>
    private static Clip AimClip(Rig rig)
    {
        const float duration = 10f;
        var tracks = Empty(rig);
        var t = Times(duration, 40);
        tracks[rig.Find("turret")] = Rot(t, time => Ry(Rad(38) * MathF.Sin(MathF.Tau * time / duration)));
        tracks[rig.Find("gun")] = Rot(t, time => Rx(Rad(-4.5f) * (1 - MathF.Cos(MathF.Tau * 2 * time / duration)) * 0.5f + Rad(1.5f)));
        return new Clip(Aim, duration, tracks);
    }

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
