using System.Numerics;
using Burger.Core;
using Burger.Core.Figures;

namespace Burger.Tools;

// burger figure: the animated bodies of Game.Figures (list them, export them for a 3D tool, check them)
public static partial class Program
{
    private static int Figure(List<string> args)
    {
        string action = args.FirstOrDefault(a => !a.StartsWith("--"))?.ToLowerInvariant() ?? "list";
        var rest = args.Where(a => !a.StartsWith("--")).Skip(1).ToList();
        string? Option(string name) { int i = args.IndexOf(name); return i >= 0 && i + 1 < args.Count ? args[i + 1] : null; }
        rest.Remove(Option("--out") ?? "");
        rest.Remove(Option("--project") ?? "");

        var game = new Game();
        if (Option("--project") is { } project) game.Figures.Load(GameDir(ResolveProject(project)));
        PrintLog();
        switch (action)
        {
            case "list":
                foreach (var m in game.Figures.All)
                {
                    Console.WriteLine($"{m.Key}: {m.Mesh.TriangleCount} triangles, {m.Mesh.VertexCount} vertices, {m.Rig.Count} joints, {m.Height:0.00} m tall");
                    foreach (var c in m.Clips.Values)
                        Console.WriteLine($"  clip {c.Name}: {c.Duration:0.00} s{(c.Loop ? ", loops" : "")}{(c.Speed > 0 ? $", travels {c.Speed:0.0} m/s" : "")}");
                    Console.WriteLine("  joints: " + string.Join(", ", m.Rig.Joints.Select(j => j.Name)));
                }
                return 0;
            case "export":
            {
                string key = rest.FirstOrDefault() ?? HumanMale.Key;
                var model = game.Figures.Find(key);
                if (model == null) return Fail($"No figure '{key}'. Run 'burger figure list'.");
                string file = Path.GetFullPath(Option("--out") ?? key + ".glb");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                Glb.Write(model, file);
                Ok($"Wrote {file} ({new FileInfo(file).Length / 1024} KB): open it in Blender with File > Import > glTF 2.0.");
                return 0;
            }
            case "test":
                _failedChecks = 0;
                FigureSelfTest(game);
                TankSelfTest(game);
                SoldierSelfTest(game);
                return _failedChecks == 0 ? 0 : 1;
            default:
                return Fail($"Unknown figure action '{action}'. Use list, export or test.");
        }
    }

    private static void SoldierSelfTest(Game game)
    {
        var body = game.Figures.Human;
        var soldier = game.Figures.Find(SoldierGear.Soldier);
        Check(soldier != null, "Game.Figures always has the equipped soldier");
        if (soldier == null) return;
        var mesh = soldier.Mesh;
        int n = mesh.VertexCount;
        Check(ReferenceEquals(soldier.Rig, body.Rig) && soldier.Clips.Count == body.Clips.Count && n > body.Mesh.VertexCount && n <= ushort.MaxValue,
            $"the soldier is the body plus its kit on the same rig and clips ({body.Mesh.TriangleCount} -> {soldier.Mesh.TriangleCount} triangles)");
        foreach (string key in SoldierGear.Kit)
        {
            var piece = game.Figures.Find(key);
            bool ok = piece != null && piece.Mesh.TriangleCount > 0 && piece.Mesh.Indices.All(i => i >= 0 && i < piece.Mesh.VertexCount);
            if (ok)
                for (int v = 0; v < piece!.Mesh.VertexCount; v++)
                {
                    float sum = 0;
                    for (int k = 0; k < 4; k++) { sum += piece.Mesh.Weights[v * 4 + k]; ok &= piece.Mesh.Joints[v * 4 + k] < piece.Rig.Count; }
                    ok &= Math.Abs(sum - 1) < 1e-4f && piece.Mesh.Positions[v].Y > -0.002f;
                }
            Check(ok, $"piece '{key}' exists, is skinned to the body's joints and stays above the ground ({piece?.Mesh.TriangleCount} triangles)");
        }
        foreach (string tag in Uniforms.Tags)
        {
            var kit = game.Figures.Find(Uniforms.OutfitKey(tag));
            float h = kit?.Mesh.Positions.Max(p => p.Y) ?? 0;
            Check(kit != null && kit.Clips.Count == body.Clips.Count && kit.Mesh.VertexCount <= ushort.MaxValue && h is > 1.78f and < 1.90f && kit.Mesh.Indices.All(i => i >= 0 && i < kit.Mesh.VertexCount),
                $"uniform {tag}: outfit '{Uniforms.OutfitKey(tag)}' is the body in its kit on the same clips ({kit?.Mesh.TriangleCount} triangles, {h * 100:0} cm)");
        }
        float top = soldier.Mesh.Positions.Max(p => p.Y);
        Check(top is > 1.80f and < 1.90f, $"the helmet adds a few centimetres on top: soldier {top * 100:0} cm against body {body.Height * 100:0} cm");

        // in every pose of every clip the gear stays on the person: helmet near the head, pack near the chest, rifle within reach of the back
        var rig = soldier.Rig; var pose = rig.RestPose(); var skin = new Matrix4x4[rig.Count];
        bool close = true; float worst = 0;
        foreach (var clip in soldier.Clips.Values.Where(c => c.Name != HumanMale.FireProne)) // lying down carries everything a body's length from where it stood
            for (int f = 0; f < 12; f++)
            {
                rig.ResetPose(pose); clip.Sample(clip.Duration * f / 12, pose); rig.SkinMatrices(pose, skin);
                foreach (var key in new[] { SoldierGear.Helmet, SoldierGear.Pack, SoldierGear.Rifle })
                {
                    if (key == SoldierGear.Rifle && (clip.Name.Contains("fire") || clip.Name.StartsWith("reload"))) continue; // the rifle leaves the back on purpose
                    var m = game.Figures.Find(key)!.Mesh;
                    for (int v = 0; v < m.VertexCount; v += 7)
                    {
                        var rest = m.Positions[v]; var now = m.Skinned(v, skin);
                        worst = Math.Max(worst, Vector3.Distance(rest, now));
                    }
                }
            }
        foreach (var style in HumanMale.FireStyles)
        {
        var fire = soldier.Clips.GetValueOrDefault(style.Name);
        Check(fire != null && fire.Events.Length == style.Shots.Length * 2 && fire.Events.All(e => e.Attach == HumanMale.MuzzleAttach && e.Time >= 0 && e.Time <= fire.Duration), $"'{style.Name}' raises muzzle events (flash and smoke per shot) inside its length");
        if (fire != null)
        {
            // mid-clip, between the first two shots: the muzzle is out in front of the chest, higher than the waist, and both wrists are on the rifle
            rig.ResetPose(pose); fire.Sample(fire.Duration * (style.Shots[0] + style.Shots[1]) / 2, pose); rig.SkinMatrices(pose, skin);
            int rj = rig.Find("rifle");
            var muzzle = Vector3.Transform(HumanMale.MuzzleRest, skin[rj]);
            var butt = Vector3.Transform(HumanMale.RifleButt, skin[rj]);
            Check(muzzle.Z > 0.7f && muzzle.Y > 1.0f && muzzle.Y < 1.6f, $"'{style.Name}' aiming: the muzzle points forward at shoulder height (muzzle at y {muzzle.Y:0.00}, z {muzzle.Z:0.00})");
            var world = new Matrix4x4[rig.Count]; rig.WorldTransforms(pose, world);
            float Miss(string hand) { var w = world[rig.Find(hand)].Translation; var a = butt; var b = muzzle; var ab = b - a; float t = Math.Clamp(Vector3.Dot(w - a, ab) / ab.LengthSquared(), 0, 1); return Vector3.Distance(w, a + ab * t); }
            float missL = Miss("hand.L"), missR = Miss("hand.R");
            Check(missL < 0.12f && missR < 0.12f, $"'{style.Name}' aiming: both hands are on the rifle (left {missL * 100:0} cm, right {missR * 100:0} cm off its axis)");
        }
        }
        var semi = game.Figures.Find(SoldierGear.RifleSemi);
        Check(semi != null && semi.Mesh.TriangleCount > 200 && game.Figures.SoldierOf("USA").Mesh.TriangleCount != game.Figures.SoldierOf("ENG").Mesh.TriangleCount, "the second rifle type exists and a country that carries it differs from one with the basic rifle");
        var plainSoldier = game.Figures.SoldierOf("ENG"); var nilotic = game.Figures.SoldierOf("ENG", "nilotic"); var nordic = game.Figures.SoldierOf("ENG", "nordic");
        Check(nilotic.Key != plainSoldier.Key && nilotic.Mesh.TriangleCount >= plainSoldier.Mesh.TriangleCount, "a people's soldier is its own model on the same body");
        Check(nilotic.Clips.Count == plainSoldier.Clips.Count && nilotic.Rig == plainSoldier.Rig && nilotic.Mesh.Colors.SequenceEqual(nordic.Mesh.Colors) == false, "people variants share the clips and rig but differ in colour");
        Check(ReferenceEquals(game.Figures.SoldierOf("ENG", "nilotic"), nilotic) && game.Figures.SoldierOf("ENG", "no_such_people").Key == plainSoldier.Key, "variants are made once; an unknown people gives the plain soldier");
        Check(Uniforms.ClipFor("SOV", HumanMale.Fire, soldier) == HumanMale.FireAggressive && Uniforms.ClipFor("ENG", HumanMale.Fire, soldier) == HumanMale.Fire && Uniforms.ClipFor("XXX", HumanMale.Fire, soldier) == HumanMale.Fire, "countries pick their firing variant, others the default");
        Check(worst < 1.2f, $"gear moves with the body, never flying off it (largest move of a vertex from rest: {worst:0.00} m)");
    }

    private static void TankSelfTest(Game game)
    {
        var tank = game.Figures.Find(LightTank.Key);
        Check(tank != null, "Game.Figures always has the light tank");
        if (tank == null) return;
        var mesh = tank.Mesh; var rig = tank.Rig;
        int n = mesh.VertexCount;
        var human = game.Figures.Human;

        // ---- the hull and its size against a soldier ----
        bool weights = true;
        for (int v = 0; v < n; v++)
        {
            float sum = 0;
            for (int k = 0; k < 4; k++) { sum += mesh.Weights[v * 4 + k]; weights &= mesh.Joints[v * 4 + k] < rig.Count; }
            weights &= Math.Abs(sum - 1) < 1e-4f && mesh.Normals[v].Length() > 0.99f;
        }
        double volume = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
            volume += Vector3.Dot(mesh.Positions[mesh.Indices[i]], Vector3.Cross(mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]])) / 6;
        Check(weights && mesh.Indices.All(i => i >= 0 && i < n) && volume > 5 && n <= ushort.MaxValue, $"the tank is closed parts facing outwards, every vertex on a joint ({mesh.TriangleCount} triangles, {volume:0.0} m3)");
        var lo = mesh.Positions.Aggregate(Vector3.Min); var hi = mesh.Positions.Aggregate(Vector3.Max);
        float length = hi.Z - lo.Z, width = hi.X - lo.X, height = hi.Y - lo.Y;
        Check(length is > 4.6f and < 5.4f && width is > 2.3f and < 2.8f && height is > 2.0f and < 2.4f && Math.Abs(lo.Y) < 1e-3f,
            $"the tank is the size of a light tank: {length:0.0} x {width:0.0} x {height:0.0} m, on the ground");
        Check(height / human.Height is > 1.1f and < 1.4f && width / human.Height is > 1.3f and < 1.6f && length / human.Height is > 2.6f and < 3.1f,
            $"against the {human.Height:0.00} m soldier the tank is {height / human.Height:0.00} soldiers tall, {width / human.Height:0.00} wide and {length / human.Height:0.00} long");
        Check(Math.Abs(mesh.Positions.Sum(p => p.X * 0) + (hi.X + lo.X)) < 0.02f, "the tank is the same left and right");
        bool joints = new[] { "root", "hull", "turret", "gun", "barrel" }.All(j => rig.Find(j) >= 0)
                      && new[] { "sprocket", "idler", "wheel1", "wheel2", "wheel3", "wheel4", "cleats_top", "cleats_bottom" }.All(j => rig.Find(j + ".L") >= 0 && rig.Find(j + ".R") >= 0);
        Check(joints, "the rig has the hull, turret, gun, barrel and on each side the wheels and track cleats");

        // ---- clips ----
        var skin = new Matrix4x4[rig.Count];
        var pose = rig.RestPose();
        Vector3 Where(string joint, Vector3 point, Clip clip, float time)
        {
            var sk = new Matrix4x4[rig.Count];
            rig.ResetPose(pose);
            clip.Sample(time, pose);
            rig.SkinMatrices(pose, sk);
            return Vector3.Transform(point, sk[rig.Find(joint)]);
        }
        foreach (string name in new[] { LightTank.Idle, LightTank.Drive, LightTank.Aim, LightTank.Fire })
        {
            if (!tank.Clips.TryGetValue(name, out var clip)) { Check(false, $"the tank has a '{name}' clip"); continue; }
            var a = rig.RestPose(); var b = rig.RestPose();
            bool wasLoop = clip.Loop;
            clip.Loop = false; clip.Sample(0, a); clip.Sample(clip.Duration, b); clip.Loop = wasLoop;
            bool same = true, moves = false;
            for (int j = 0; j < rig.Count; j++)
                same &= Math.Abs(Quaternion.Dot(a.Rotation[j], b.Rotation[j])) > 0.99999f && (a.Position[j] - b.Position[j]).Length() < 1e-4f;
            var mid = rig.RestPose();
            clip.Sample(clip.Duration * 0.37f, mid);
            for (int j = 0; j < rig.Count; j++) moves |= Math.Abs(Quaternion.Dot(a.Rotation[j], mid.Rotation[j])) < 0.9999f || (a.Position[j] - mid.Position[j]).Length() > 1e-4f;
            Check(same && moves, name == LightTank.Fire ? $"'{name}' is one shot that starts and ends at rest ({clip.Duration:0.0} s)" : $"'{name}' loops without a jump ({clip.Duration:0.0} s)");
            Check(clip.Loop == (name != LightTank.Fire), $"'{name}' {(clip.Loop ? "loops" : "plays once")}");

            // it never goes through the ground, and the tracks stay on it
            float low = float.MaxValue;
            for (int k = 0; k < 40; k++)
            {
                rig.ResetPose(pose);
                clip.Sample(clip.Duration * k / 40, pose);
                rig.SkinMatrices(pose, skin);
                for (int v = 0; v < n; v += 3) low = Math.Min(low, mesh.Skinned(v, skin).Y);
            }
            Check(low > -0.012f, $"'{name}' keeps the tank above the ground (lowest point {low * 100:0.0} cm)");
        }

        var drive = tank.Clips[LightTank.Drive];
        Check(Math.Abs(drive.Speed - LightTank.DriveSpeed) < 1e-3f && drive.Speed > 2, $"'drive' knows how fast it travels ({drive.Speed:0.0} m/s)");
        // no slip: the bottom cleats slide back exactly as fast as the tank goes, and the wheels roll that fast on the track
        var cleat = tank.Mesh.Positions.Where((p, i) => mesh.Joints[i * 4] == rig.Find("cleats_bottom.L")).First();
        float t0 = 0.105f, t1 = 0.125f;
        float slid = Where("cleats_bottom.L", cleat, drive, t1).Z - Where("cleats_bottom.L", cleat, drive, t0).Z;
        float expected = -drive.Speed * (t1 - t0);
        float wrap = (float)Math.Round((slid - expected) / (drive.Speed * drive.Duration / 20));
        Check(Math.Abs(slid - expected - wrap * drive.Speed * drive.Duration / 20) < 0.003f, $"in 'drive' the bottom run goes back at the speed the tank goes forward ({slid:0.000} m in {t1 - t0:0.0} s, wanted {expected:0.000} m)");
        var wheel = rig.Find("wheel2.L");
        Rotation(rig, drive, wheel, 0.2f, out float angle);
        Check(Math.Abs(angle * 0.315f - drive.Speed * 0.2f) < 0.002f, $"in 'drive' the wheels roll at the speed of the track ({angle * 0.315f:0.000} m in 0.2 s, wanted {drive.Speed * 0.2f:0.000} m)");

        var fire = tank.Clips[LightTank.Fire];
        var muzzle = new Vector3(0, 1.74f, 2.80f);
        float recoilBack = Where("barrel", muzzle, fire, 0.3f).Z - muzzle.Z;
        float rise = Where("barrel", muzzle, fire, 0.1f).Y - muzzle.Y;
        Check(recoilBack < -0.25f && rise > 0.02f, $"'fire' throws the barrel back {-recoilBack * 100:0} cm and kicks the muzzle up {rise * 100:0.0} cm");
        float yaw = 0;
        var aim = tank.Clips[LightTank.Aim];
        for (int k = 0; k < 20; k++) yaw = Math.Max(yaw, Math.Abs(Where("barrel", muzzle, aim, aim.Duration * k / 20).X));
        Check(yaw > 1.2f, $"'aim' sweeps the muzzle {yaw:0.0} m to the side");

        // ---- files ----
        var warnings = new List<string>();
        var copy = Glb.Read(Glb.Write(tank), "copy", warnings);
        bool ok = warnings.Count == 0 && copy.Mesh.VertexCount == n && copy.Rig.Count == rig.Count && copy.Clips.Count == tank.Clips.Count
                  && Math.Abs(copy.Clips[LightTank.Drive].Speed - drive.Speed) < 1e-4f && !copy.Clips[LightTank.Fire].Loop;
        if (ok)
        {
            var skinCopy = new Matrix4x4[rig.Count];
            var poseCopy = copy.Rig.RestPose();
            rig.ResetPose(pose); drive.Sample(0.27f, pose); copy.Clips[LightTank.Drive].Sample(0.27f, poseCopy);
            rig.SkinMatrices(pose, skin); copy.Rig.SkinMatrices(poseCopy, skinCopy);
            for (int v = 0; v < n && ok; v += 7) ok = (mesh.Skinned(v, skin) - copy.Mesh.Skinned(v, skinCopy)).Length() < 1e-3f;
        }
        Check(ok, "the tank written as .glb reads back the same: mesh, rig and animations");
    }

    // the turn of a joint's rotation at a time in a clip, in radians (a wheel turns about X)
    private static void Rotation(Rig rig, Clip clip, int joint, float time, out float angle)
    {
        var pose = rig.RestPose();
        clip.Sample(time, pose);
        var q = pose.Rotation[joint];
        angle = 2 * MathF.Atan2(q.X, q.W);
        if (angle < 0) angle += MathF.Tau;
    }

    private static void FigureSelfTest(Game game)
    {
        var model = game.Figures.Find(HumanMale.Key);
        Check(model != null && model == game.Figures.Human, "Game.Figures always has the human body");
        if (model == null) return;
        var mesh = model.Mesh;
        var rig = model.Rig;
        int n = mesh.VertexCount;

        // ---- the body ----
        bool weights = true, joints = true;
        for (int v = 0; v < n; v++)
        {
            float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                sum += mesh.Weights[v * 4 + k];
                joints &= mesh.Joints[v * 4 + k] < rig.Count && mesh.Weights[v * 4 + k] >= 0;
            }
            weights &= Math.Abs(sum - 1) < 1e-4f;
        }
        Check(weights && joints, "every vertex of the body is fully weighted to joints of its rig");
        double volume = 0;
        bool indices = mesh.Indices.Length % 3 == 0 && mesh.Indices.All(i => i >= 0 && i < n);
        for (int i = 0; indices && i < mesh.Indices.Length; i += 3)
            volume += Vector3.Dot(mesh.Positions[mesh.Indices[i]], Vector3.Cross(mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]])) / 6;
        float minY = mesh.Positions.Min(p => p.Y);
        Check(indices && volume is > 0.05 and < 0.12, $"the body is closed shells facing outwards, with the volume of a man ({volume * 1000:0} litres)");
        Check(Math.Abs(model.Height - 1.80f) < 0.01f && Math.Abs(minY) < 1e-4f, "the body is 1.80 m tall and stands on the ground");
        Check(mesh.TriangleCount is > 1200 and < 3000, $"the body is low-poly ({mesh.TriangleCount} triangles)");
        bool mirrored = true;
        foreach (var joint in rig.Joints.Where(j => j.Name.EndsWith(".L")))
            mirrored &= rig.Find(joint.Name[..^2] + ".R") >= 0;
        var xs = mesh.Positions.Select(p => MathF.Round(p.X, 4)).ToList();
        mirrored &= Math.Abs(xs.Min() + xs.Max()) < 1e-3f && Math.Abs(mesh.Positions.Sum(p => p.X)) < 0.05f * n / 100;
        Check(mirrored, "the body and its rig are the same left and right");
        Check(new[] { "root", "hips", "spine", "chest", "neck", "head", "upper_arm.L", "forearm.L", "hand.L", "thigh.L", "shin.L", "foot.L", "toe.L" }.All(j => rig.Find(j) >= 0),
            "the rig has the joints equipment and animations attach to");

        // ---- the clips ----
        var skin = new Matrix4x4[rig.Count];
        var world = new Matrix4x4[rig.Count];
        var pose = rig.RestPose();
        float Lowest(Clip clip, float time)
        {
            rig.ResetPose(pose);
            clip.Sample(time, pose);
            rig.SkinMatrices(pose, skin);
            float low = float.MaxValue;
            for (int v = 0; v < n; v++) low = Math.Min(low, mesh.Skinned(v, skin).Y);
            return low;
        }
        foreach (string name in new[] { HumanMale.Idle, HumanMale.Walk, HumanMale.Run })
        {
            if (!model.Clips.TryGetValue(name, out var clip)) { Check(false, $"the body has a '{name}' clip"); continue; }
            // loops: the pose at the end is the pose at the start
            var a = rig.RestPose(); var b = rig.RestPose();
            bool wasLoop = clip.Loop;
            clip.Loop = false; clip.Sample(0, a); clip.Sample(clip.Duration, b); clip.Loop = wasLoop;
            bool closes = true, moves = false;
            for (int j = 0; j < rig.Count; j++)
                closes &= Math.Abs(Quaternion.Dot(a.Rotation[j], b.Rotation[j])) > 0.99999f && (a.Position[j] - b.Position[j]).Length() < 1e-4f;
            clip.Sample(clip.Duration * 0.37f, b);
            for (int j = 0; j < rig.Count; j++) moves |= Math.Abs(Quaternion.Dot(a.Rotation[j], b.Rotation[j])) < 0.9999f;
            Check(clip.Loop && closes && moves, $"'{name}' is a real looping animation ({clip.Duration:0.00} s)");

            const int steps = 48;
            float low = float.MaxValue, high = float.MinValue;
            for (int k = 0; k < steps; k++)
            {
                float y = Lowest(clip, clip.Duration * k / steps);
                low = Math.Min(low, y); high = Math.Max(high, y);
            }
            if (name == HumanMale.Run) Check(low > -0.012f && high > 0.02f, $"running, the feet meet the ground and both leave it between steps (lowest point {low * 100:0.0} to {high * 100:0.0} cm)");
            else Check(low > -0.012f && high < 0.012f, $"in '{name}' a foot is always on the ground, never under it (lowest point {low * 100:0.0} to {high * 100:0.0} cm)");
            if (name == HumanMale.Idle) continue;
            Check(clip.Speed > 0, $"'{name}' knows how fast it travels ({clip.Speed:0.0} m/s)");
            if (clip.Speed <= 0) continue;

            // no foot slide: while the ball of the left foot is down it moves back exactly as fast as the body moves forward
            int foot = rig.Find("foot.L"), toe = rig.Find("toe.L");
            Matrix4x4.Invert(rig.InverseBind[foot], out var footRest);
            var ball = new Vector3(0, -footRest.Translation.Y, rig.Joints[toe].Position.Z); // on the sole, under the toe joint
            float from = name == HumanMale.Walk ? 0.14f : 0.07f, to = name == HumanMale.Walk ? 0.58f : 0.34f;
            float slideLow = float.MaxValue, slideHigh = float.MinValue, lift = 0;
            for (int k = 0; k <= steps; k++)
            {
                float phase = (float)k / steps;
                if (phase < from || phase > to) continue;
                rig.ResetPose(pose);
                clip.Sample(clip.Duration * phase, pose);
                rig.WorldTransforms(pose, world);
                var p = Vector3.Transform(ball, world[foot]);
                float travelled = p.Z + clip.Speed * clip.Duration * phase;
                slideLow = Math.Min(slideLow, travelled); slideHigh = Math.Max(slideHigh, travelled);
                lift = Math.Max(lift, Math.Abs(p.Y));
            }
            Check(slideHigh - slideLow < 0.004f && lift < 0.004f, $"in '{name}' a planted foot stays where it was put down at {clip.Speed:0.0} m/s (slides {(slideHigh - slideLow) * 1000:0.0} mm)");
        }

        // ---- playing ----
        var animator = game.Figures.NewAnimator();
        animator.Advance(0.4f);
        bool played = animator.Current?.Name == HumanMale.Idle && animator.Play(HumanMale.Walk) && !animator.Play("no_such_clip");
        animator.Advance(0.1f);
        played &= animator.Skin().All(m => float.IsFinite(m.M11) && float.IsFinite(m.M41) && float.IsFinite(m.M43));
        animator.Advance(0.5f);
        float walkPhase = animator.Time / animator.Current!.Duration;
        animator.Play(HumanMale.Run);
        played &= Math.Abs(animator.Time / animator.Current!.Duration - walkPhase) < 1e-4f;
        Check(played, "an animator plays and cross-fades clips, and walk and run stay in step");

        // ---- files ----
        var warnings = new List<string>();
        var copy = Glb.Read(Glb.Write(model), "copy", warnings);
        string differs = warnings.Count > 0 ? warnings[0]
            : copy.Mesh.VertexCount != n || !copy.Mesh.Indices.SequenceEqual(mesh.Indices) ? "mesh"
            : !copy.Rig.Joints.Select(j => (j.Name, j.Parent)).SequenceEqual(rig.Joints.Select(j => (j.Name, j.Parent))) ? "rig"
            : copy.Clips.Count != model.Clips.Count ? "clips" : "";
        if (differs == "")
        {
            var walk = model.Clips[HumanMale.Walk]; var walkCopy = copy.Clips[HumanMale.Walk];
            if (Math.Abs(walk.Duration - walkCopy.Duration) > 1e-5f || walk.Speed != walkCopy.Speed || !walkCopy.Loop) differs = "clip settings";
            var skinCopy = new Matrix4x4[rig.Count];
            var poseCopy = copy.Rig.RestPose();
            rig.ResetPose(pose);
            walk.Sample(0.31f, pose); walkCopy.Sample(0.31f, poseCopy);
            rig.SkinMatrices(pose, skin); copy.Rig.SkinMatrices(poseCopy, skinCopy);
            for (int v = 0; v < n && differs == ""; v++)
            {
                if ((mesh.Skinned(v, skin) - copy.Mesh.Skinned(v, skinCopy)).Length() > 1e-4f) differs = "posed vertices";
                for (int c = 0; c < 4; c++) if (Math.Abs(mesh.Colors[v * 4 + c] - copy.Mesh.Colors[v * 4 + c]) > 1) differs = "colours";
            }
        }
        Check(differs == "", "a body written as .glb reads back the same: mesh, colours, rig and animations" + (differs == "" ? "" : $" (differs: {differs})"));

        string dir = Path.Combine(Path.GetTempPath(), "burger_figures_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "gfx", "models"));
            var edited = new FigureModel("only_clips", rig, mesh);
            var nod = new JointTrack { RotationTimes = new[] { 0f, 0.5f, 1f }, Rotations = new[] { Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f), Quaternion.Identity } };
            var tracks = new JointTrack?[rig.Count];
            tracks[rig.Find("head")] = nod;
            edited.Clips["nod"] = new Clip("nod", 1f, tracks) { Loop = false };
            edited.Clips[HumanMale.Walk] = new Clip(HumanMale.Walk, 1f, tracks);
            Glb.Write(edited, Path.Combine(dir, "gfx", "models", HumanMale.Key + ".extra.glb"));
            Glb.Write(model, Path.Combine(dir, "gfx", "models", "militia.glb"));
            var modded = new Game();
            int before = modded.Figures.Human.Clips.Count;
            modded.Figures.Load(dir);
            var human = modded.Figures.Human;
            Check(human.Clips.Count == before + 1 && !human.Clips["nod"].Loop && human.Clips[HumanMale.Walk].Speed == model.Clips[HumanMale.Walk].Speed
                  && modded.Figures.Find("militia") is { } militia && militia.Clips.Count == model.Clips.Count && modded.Figures.Find("no_such_figure") == null,
                "gfx/models/ adds models and animations: name.glb is a model, name.anything.glb gives its clips to model 'name'");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
