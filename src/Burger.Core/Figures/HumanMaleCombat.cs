using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>Where the body goes while it works the rifle: on its feet, on one knee, or flat on the ground.</summary>
public enum Posture { Stand, Kneel, Prone }

// The soldier's actions beyond walking and standing: the rifle drill in parts that join end to end (draw, shoot, reload, stow), the same drill on a knee and
// flat, ducking, flinching and taking the helmet off to look at it.
public static partial class HumanMale
{
    /// <summary>The rifle drill in parts. Each starts where the one before it ends, so a fight is any run of them: <c>draw</c> (slung to shouldered), <c>shoot</c> (shouldered, the shots and the bolt worked between them), a reload, <c>stow</c> (shouldered to slung). The full drill <see cref="Fire"/> is draw, shoot and stow in one clip.</summary>
    public const string Draw = "draw", Shoot = "shoot", Stow = "stow";
    /// <summary>Reloads that start and end shouldered: of a bolt-action rifle (clip of loose rounds pressed in from the top), of a self-loading rifle (an en-bloc clip), and the generic one (swapping a box magazine) that any other rifle falls back on.</summary>
    public const string ReloadBolt = "reload_bolt", ReloadSemi = "reload_semi", Reload = "reload";
    /// <summary>The whole drill on one knee and flat on the ground (down, draw, shoot, stow, up).</summary>
    public const string FireKneel = "fire_kneel", FireProne = "fire_prone";
    /// <summary>Dropping into cover with the arms over the head, a start at something close, and taking the helmet off to look at it and putting it back.</summary>
    public const string Duck = "duck", Flinch = "flinch", HelmetInspect = "helmet_inspect";

    /// <summary>The clip of one part of a firing drill for a style: <c>draw_fire_side</c> for <see cref="Draw"/> and style <c>fire_side</c>.</summary>
    public static string PartOf(string part, string style) => part + "_" + style;

    // what one moment of a rifle drill asks of the body
    private struct Beat
    {
        public float Idle;                 // phase of the idle frame under the body (its weight shifts and breathing)
        public float PathP;                // how far along the path from slung (0) to shouldered (1) the rifle is
        public float Stance;               // 0-1 how far the body is turned into its aiming stance
        public float Kick, Bolt, Pull;     // recoil; the right hand on the bolt handle; how far back it pulls (0-1)
        public float Wind, Swing;          // the body winding back to the rifle and swinging with it as it comes off the back
        public float WR, WL;               // how much the right and the left hand are on the rifle
        public float Pouch, Load, Mag;     // the right hand at the belt pouch, over the action putting rounds in, under the rifle at the magazine well
        public float Press;                // a push of the loading hand (metres into the rifle)
        public float Port;                 // the rifle brought in low across the body, muzzle up, to be worked on (0-1)
        public float Look;                 // head bowed to the work (0-1)
        public float Down, Prone;          // how far into a kneel the body is, and from the kneel how far down to lying (0-1 each)
    }

    // the stations of a rifle's path and the posture it is worked in
    private sealed class Drill
    {
        public FireStyle Style = null!;
        public Posture Posture;
        public float Tilt;                 // degrees the rifle is turned about its butt, in chest space, against the chest lying over (prone)
        public Vector3 Butt0, Dir0, Ru0;
        public Vector3[] Butt = null!, Dir = null!, Side = null!;
    }

    private sealed partial class Solver
    {

        // Puts the headgear joint where the frame asks (chest space), blended with where the head carries it.
        private void PlaceHeadgear(Pose pose, Frame f)
        {
            var world = new Matrix4x4[_rig.Count];
            _rig.WorldTransforms(pose, world);
            Matrix4x4.Decompose(world[_chest], out _, out var chestRot, out var chestPos);
            Matrix4x4.Decompose(world[_head], out _, out var headRot, out var headPos);
            var rot = Quaternion.Normalize(f.HeadgearRot * chestRot);
            var pos = chestPos + Vector3.Transform(f.HeadgearPos, chestRot);
            float w = Math.Clamp(f.HeadgearBlend, 0, 1);
            rot = Quaternion.Slerp(headRot, rot, w); pos = Vector3.Lerp(headPos, pos, w);
            var wm = Matrix4x4.CreateFromQuaternion(rot); wm.Translation = pos;
            Matrix4x4.Invert(world[_head], out var inv);
            var local = wm * inv;
            Matrix4x4.Decompose(local, out _, out var lr, out var lp);
            pose.Rotation[_headgear] = lr; pose.Position[_headgear] = lp;
        }

        private static float Ease(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

        // scalar keys (time, value) joined by a smooth curve that does not stop at the keys
        private static float Keys(float x, params (float T, float V)[] k)
        {
            if (x <= k[0].T) return k[0].V;
            if (x >= k[^1].T) return k[^1].V;
            int i = 0; while (x > k[i + 1].T) i++;
            float Tan(int j) => j == 0 || j == k.Length - 1 ? 0 : (k[j + 1].V - k[j - 1].V) / (k[j + 1].T - k[j - 1].T);
            float h = k[i + 1].T - k[i].T, t = (x - k[i].T) / h, t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * k[i].V + (t3 - 2 * t2 + t) * h * Tan(i) + (-2 * t3 + 3 * t2) * k[i + 1].V + (t3 - t2) * h * Tan(i + 1);
        }

        private static Vector3 Spline(Vector3[] k, float p)
        {
            float x = Math.Clamp(p, 0, 1) * (k.Length - 1);
            int i = Math.Min((int)x, k.Length - 2); float t = x - i;
            var p0 = k[Math.Max(i - 1, 0)]; var p1 = k[i]; var p2 = k[i + 1]; var p3 = k[Math.Min(i + 2, k.Length - 1)];
            return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
        }

        // 0 -> 1 -> 0 over [a, b] with ramps of length r
        private static float Hump(float t, float a, float b, float r) => Env(t, a, b, r);

        private Drill MakeDrill(FireStyle st, Posture posture)
        {
            var chestPos = _rig.Joints[_chest].Position + _rig.Joints[_spine].Position + _rig.Joints[_hips].Position;
            var butt0 = HumanMale.RifleButt - chestPos;
            var dir0 = HumanMale.RifleAxis;
            var ru0 = Vector3.Normalize(Vector3.Cross(dir0, Vector3.UnitX));
            // stations of the path, chest space: butt, direction butt to muzzle, the side the bolt handle is on. Out to the right of the body and round in front of it, never through the pack or the chest
            Vector3[] sideButt =
            {
                butt0,
                new(0.06f, -0.30f, -0.36f),     // pulled clear of the pack, muzzle going up over the right shoulder
                new(-0.34f, -0.34f, 0.08f),     // swung out to the right of the body, upright
                new(-0.20f, -0.10f, 0.16f),     // brought across in front, tilting forward
                new(-0.105f, 0.13f, 0.045f),    // butt in the shoulder pocket
            };
            Vector3[] sideDir =
            {
                dir0,
                Vector3.Normalize(new Vector3(-0.30f, 0.93f, -0.02f)),
                Vector3.Normalize(new Vector3(-0.05f, 0.94f, 0.34f)),
                Vector3.Normalize(new Vector3(0.12f, 0.55f, 0.83f)),
                Vector3.Normalize(new Vector3(0.50f, 0.015f, 1f)),
            };
            Vector3[] sideSide = { ru0, Vector3.Normalize(new Vector3(-0.6f, 0.1f, -0.8f)), -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX };
            // over the shoulder: the right hand goes up behind the shoulder, takes the rifle high on the handguard, lifts it off the back
            // and swings it about the hand in a plane beside the head, butt dropping to the shoulder as the muzzle comes forward
            Vector3[] overButt =
            {
                butt0,
                new(-0.07f, -0.32f, -0.34f),    // lifted off the pack, upright
                new(-0.22f, -0.30f, -0.14f),    // out beside the shoulder, muzzle swinging over it
                new(-0.22f, -0.10f, 0.06f),     // butt coming up, muzzle forward
                new(-0.105f, 0.13f, 0.045f),
            };
            Vector3[] overDir =
            {
                dir0,
                Vector3.Normalize(new Vector3(-0.12f, 0.98f, -0.12f)),
                Vector3.Normalize(new Vector3(-0.10f, 0.80f, 0.60f)),
                Vector3.Normalize(new Vector3(0.02f, 0.30f, 0.95f)),
                Vector3.Normalize(new Vector3(0.50f, 0.015f, 1f)),
            };
            Vector3[] overSide = { ru0, Vector3.Normalize(new Vector3(-0.7f, 0.1f, -0.7f)), -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX };
            return new Drill
            {
                Style = st, Posture = posture, Tilt = posture == Posture.Prone ? 70f : 0f,
                Butt0 = butt0, Dir0 = dir0, Ru0 = ru0,
                Butt = st.Over ? overButt : sideButt, Dir = st.Over ? overDir : sideDir, Side = st.Over ? overSide : sideSide,
            };
        }

        // ------------------------------------------------------------------------------------------- the firing drill as beats
        /// <summary>What the full drill asks at a fraction of its length: the rifle comes off the back, is shouldered, fires at each shot with the bolt worked between them, then is lowered and slung again.</summary>
        private static Beat FireBeat(FireStyle st, float phase)
        {
            float dur = st.Dur, t = phase * dur;
            var b = new Beat { Idle = 0.05f + phase * 0.02f };
            // how far along the path the rifle is. Not an even glide: a short beat while the hand takes hold, a pull that starts
            // slowly, a fast swing across and a settle into the shoulder; lowering is quicker and softer.
            float pathP = Keys(phase, phase < 0.5f ? st.Up : st.Down);
            float r0 = st.Up[0].T - 0.065f; // the hand starts going back a little before the rifle moves
            float aimed = Ease((pathP - 0.55f) / 0.45f);
            float kick = 0;
            foreach (float sp in st.Shots)
            {
                float dt = (phase - sp) * dur;
                if (dt >= -0.03f) kick += dt < 0 ? 1 + dt / 0.03f : MathF.Exp(-dt / 0.20f);
            }
            float bolt = 0, pull = 0; // the bolt is worked in every gap between shots
            for (int g = 0; g + 1 < st.Shots.Length; g++)
            {
                float gap = st.Shots[g + 1] - st.Shots[g];
                bolt = Math.Max(bolt, Env(phase, st.Shots[g] + gap * 0.18f, st.Shots[g + 1] - gap * 0.143f, 0.025f * Math.Min(1f, gap / 0.28f)));
                float u = Math.Clamp((phase - (st.Shots[g] + gap * 0.25f)) / (gap * 0.55f), 0, 1);
                pull = Math.Max(pull, MathF.Sin(MathF.PI * u) * (u < 0.5f ? 1f : 0.85f));
            }
            b.PathP = pathP;
            b.Stance = Env(phase, 0.0f, 0.98f, 0.14f);
            b.Kick = Math.Min(kick, 1f) * st.Kick;
            b.Bolt = bolt; b.Pull = pull;
            // the whole body works the pull: it winds back to the rifle, then unwinds into the swing and sinks a little with the weight
            b.Wind = Ease((phase - r0) / 0.07f) * (1 - Ease((pathP - 0.15f) / 0.5f)) * (phase < 0.5f ? 1 : 0);
            b.Swing = MathF.Sin(MathF.PI * Math.Clamp((pathP - 0.2f) / 0.7f, 0, 1)) * (phase < 0.5f ? 1 : 0.6f);
            // the right hand reaches back before the rifle moves and lets go only once the left has the fore-end, regripping at the wrist
            b.WR = phase < 0.5f ? Ease((phase - r0) / 0.065f) : 1 - Ease((phase - 0.955f) / 0.035f);
            b.WL = Ease((pathP - 0.35f) / 0.30f);
            _ = aimed; _ = t;
            return b;
        }

        // ------------------------------------------------------------------------------------------- the rifle and the body that works it
        private Frame RifleFrame(Drill rd, in Beat b, float t)
        {
            var st = rd.Style;
            var f = IdleFrame(b.Idle); // nearly still: the idle's weight shift is not wanted while aiming
            float pathP = b.PathP;
            float aimed = Ease((pathP - 0.55f) / 0.45f);
            float sway = MathF.Sin(t * 1.9f) * 0.004f * aimed;
            float kick = b.Kick, bolt = b.Bolt, pull = b.Pull;

            // the body turns side-on to the target, left foot forward, knees soft, head laid against the stock
            float se = Math.Min(b.Stance, 1f);
            f.HipsRot *= Ry(st.HipYaw * se);
            f.Spine *= Ry(st.SpineYaw * se) * Rx((2f + st.Lean) * se);
            f.Chest *= Ry(st.ChestYaw * se) * Rx((1f + st.Lean * 0.6f) * se);
            f.Hips += new Vector3(0, -st.Crouch * se, (0.01f + st.Lean * 0.004f) * se);
            f.Ankle[0] = new Vector3(0.125f + st.Crouch * 0.4f * se, AnkleY, st.FootFwd * se + 0.012f);
            f.Ankle[1] = new Vector3(-0.115f - st.Crouch * 0.4f * se, AnkleY, -(st.FootFwd - 0.02f) * se - 0.012f);
            f.FootYaw[0] = 10f + 12f * se; f.FootYaw[1] = 10f + 40f * se;
            f.Neck *= Ry(24f * aimed) * Rz(-4f * aimed);
            f.Head *= Ry(8f * aimed) * Rz(-7f * aimed) * Rx(2f * aimed);
            // a look at the rifle while it is brought off the back
            float glance = MathF.Sin(MathF.PI * Math.Clamp(pathP * 2f, 0, 1)) * (1 - aimed);
            f.Head *= Ry(-10f * glance) * Rx(5f * glance);
            f.Chest *= Rz(-3f * glance); // leaning away from the arm that reaches back
            f.Spine *= Ry(10f * b.Wind - 4f * b.Swing) * Rz(-2f * b.Wind);
            f.Chest *= Ry(12f * b.Wind - 5f * b.Swing) * Rx(-3f * b.Wind);
            f.Hips += new Vector3(0, -0.02f * b.Swing, 0);
            if (b.Look > 0) { f.Head *= Rx(16f * b.Look); f.Neck *= Rx(6f * b.Look); }
            if (rd.Posture != Posture.Stand) ApplyPosture(f, b.Down, b.Prone, st);

            // the rifle along its path
            var butt = Spline(rd.Butt, pathP) - new Vector3(0, -0.004f, 0.075f) * kick * aimed;
            var d = Vector3.Normalize(Spline(rd.Dir, pathP) + new Vector3(sway, 0.14f * kick + sway, 0) * aimed);
            var side = Spline(rd.Side, pathP);
            if (b.Port > 0) // held low and close, in front of the chest
            {
                butt = Vector3.Lerp(butt, new Vector3(-0.10f, -0.13f, 0.09f), b.Port);
                d = Vector3.Normalize(Vector3.Lerp(d, Vector3.Normalize(new Vector3(0.38f, 0.62f, 0.68f)), b.Port));
                side = Vector3.Lerp(side, -Vector3.UnitX, b.Port);
            }
            if (rd.Tilt > 0 && b.Prone > 0) // lying on the chest the rifle still points along the ground: it is turned against the chest's tilt
            {
                var tilt = Rx(-rd.Tilt * Ease(b.Prone) * Ease(pathP * 3f));
                d = Rot(d, tilt); side = Rot(side, tilt);
            }
            var q = FromTo(rd.Dir0, d);
            var ruT = Vector3.Normalize(side - d * Vector3.Dot(side, d));
            var ru1 = Rot(rd.Ru0, q);
            float roll = MathF.Atan2(Vector3.Dot(d, Vector3.Cross(ru1, ruT)), Vector3.Dot(ru1, ruT));
            q = Quaternion.CreateFromAxisAngle(d, roll) * q;
            f.RifleRot = q;
            f.RiflePos = butt - Rot(rd.Butt0, q);
            var right = ruT; var downv = Vector3.Normalize(Vector3.Cross(d, ruT)); var left = -ruT;

            // hands. The right takes the rifle by the upper handguard while it is on the back, then slides down to the grip at the wrist
            // of the stock as it comes round; the left comes in under the fore-end late in the move.
            float gripT = st.Grip + (0.27f - st.Grip) * Ease((pathP - 0.62f) / 0.18f);
            for (int s = 0; s < 2; s++)
            {
                Vector3 axisPt, N, F; float curl;
                if (s == 1)
                {
                    axisPt = butt + d * gripT;
                    N = -(right * 0.6f + downv * 0.8f);
                    F = left * 0.8f + downv * 0.6f + d * 0.15f;
                    curl = 75f;
                }
                else
                {
                    axisPt = butt + d * 0.72f;
                    N = -(left * 0.5f + downv * 0.85f);
                    F = right * 0.85f + downv * 0.5f + d * 0.15f;
                    curl = 70f;
                }
                F = Vector3.Normalize(F); N = Vector3.Normalize(N);
                var grip = HandRotation(s, F, N);
                var wrist = axisPt - F * 0.05f - N * 0.04f;
                float fingers = curl;
                float w = s == 1 ? b.WR : b.WL;
                if (s == 1 && bolt > 0) // to the bolt handle: pinch it, pull back, push home
                {
                    var handle = butt + d * 0.405f + ruT * 0.042f;
                    var bF = Vector3.Normalize(d * 0.55f + Vector3.UnitY * 0.6f + right * 0.2f); var bN = left;
                    var hold = handle - bF * 0.05f - bN * 0.04f - d * 0.085f * b.Pull;
                    wrist = Vector3.Lerp(wrist, hold, bolt);
                    grip = Quaternion.Slerp(grip, HandRotation(s, bF, bN), bolt);
                    fingers = curl + (50f - curl) * bolt;
                }
                if (s == 1 && b.Pouch > 0) // to the pouch on the belt, in front of the right hip
                {
                    var pF = Vector3.Normalize(new Vector3(0.15f, -0.55f, 0.6f)); var pN = new Vector3(0.9f, 0.1f, -0.2f);
                    var pouch = new Vector3(-0.15f, -0.23f, 0.15f) - pF * 0.05f - pN * 0.04f;
                    wrist = Vector3.Lerp(wrist, pouch, b.Pouch);
                    grip = Quaternion.Slerp(grip, HandRotation(s, pF, pN), b.Pouch);
                    fingers = fingers + (35f - fingers) * b.Pouch;
                }
                if (s == 1 && b.Load > 0) // over the action: the clip held between thumb and fingers and pressed down into it
                {
                    var lF = Vector3.Normalize(downv * 0.85f + d * 0.45f); var lN = left * 0.7f + d * 0.4f;
                    var at = butt + d * 0.405f - downv * 0.075f + ruT * 0.005f + downv * b.Press;
                    var load = at - lF * 0.05f - lN * 0.04f;
                    wrist = Vector3.Lerp(wrist, load, b.Load);
                    grip = Quaternion.Slerp(grip, HandRotation(s, lF, lN), b.Load);
                    fingers = fingers + (45f - fingers) * b.Load;
                }
                if (s == 1 && b.Mag > 0) // under the rifle at the magazine well, pushing the magazine up into it
                {
                    var mF = Vector3.Normalize(-downv * 0.8f + d * 0.3f); var mN = d * 0.9f - downv * 0.2f;
                    var at = butt + d * 0.52f + downv * (0.115f - b.Press);
                    var mag = at - mF * 0.05f - mN * 0.04f;
                    wrist = Vector3.Lerp(wrist, mag, b.Mag);
                    grip = Quaternion.Slerp(grip, HandRotation(s, mF, mN), b.Mag);
                    fingers = fingers + (60f - fingers) * b.Mag;
                }
                f.HandTarget[s] = wrist;
                f.HandRot[s] = grip;
                f.HandBlend[s] = w;
                f.Fingers[s] = 28 + (fingers - 28) * w;
            }
            if (rd.Posture == Posture.Prone) PlantHands(f, b);
            f.Chest *= Rx(-3.5f * kick) * Rz(1.5f * kick);
            f.Spine *= Rx(-2f * kick);
            f.Hips += new Vector3(0, 0, -0.012f * kick);
            f.Head *= Rx(-4f * kick * aimed);
            return f;
        }


        // Going down to the ground and getting up from it: both hands go out in front and take the weight. They go out as the knee goes down,
        // carry the body down on bending arms and stay planted, propping the chest, for as long as the body lies with the rifle on its back;
        // they let go only to take the rifle up, and are back on the ground when it is put away, before the body gets up.
        private void PlantHands(Frame f, in Beat b)
        {
            float plant = Ease((b.Down - 0.45f) / 0.55f) * (1 - Ease(b.PathP * 5f)); // hands stay on the ground, taking the weight, until the rifle is taken up, and go back to it when it is put away
            if (plant <= 0) return;
            var hipsRot = f.HipsRot;
            var chestRot = Quaternion.Normalize(f.Chest * f.Spine * hipsRot);
            var chestPos = f.Hips + Rot(_rig.Joints[_spine].Position, hipsRot) + Rot(_rig.Joints[_chest].Position, Quaternion.Normalize(f.Spine * hipsRot));
            var inv = Quaternion.Inverse(chestRot);
            float reach = 0.52f - 0.10f * Ease(b.Prone); // the hands stay where they were put while the shoulders come down over them
            for (int s = 0; s < 2; s++)
            {
                float sg = s == 0 ? 1f : -1f;
                var palm = new Vector3(0.21f * sg, 0.045f, reach);       // on the ground, a shoulder's width apart, level with the knee
                var F = Vector3.Normalize(new Vector3(-0.15f * sg, 0f, 1f)); var N = new Vector3(0, -1, 0);
                var Fc = Rot(F, inv); var Nc = Rot(N, inv);
                var wrist = Rot(palm - F * 0.05f - N * 0.04f - chestPos, inv);
                f.HandTarget[s] = Vector3.Lerp(f.HandTarget[s].GetValueOrDefault(), wrist, plant);
                f.HandRot[s] = Quaternion.Slerp(f.HandRot[s].GetValueOrDefault(Quaternion.Identity), HandRotation(s, Fc, Nc), plant);
                f.HandBlend[s] = f.HandBlend[s] + (1 - f.HandBlend[s]) * plant;
                f.Fingers[s] = f.Fingers[s] + (15f - f.Fingers[s]) * plant;
            }
        }

        /// <summary>Takes the frame into a kneel (<paramref name="kneel"/> 0-1), and from the kneel on down to lying (<paramref name="prone"/> 0-1): the weight goes forward over the front foot, the back knee goes down, the chest folds over and lowers as the legs stretch out behind, and the front foot comes back last.</summary>
        private static void ApplyPosture(Frame f, float kneel, float prone, FireStyle st)
        {
            float k = Ease(kneel);
            if (k > 0)
            {
                f.Hips = Vector3.Lerp(f.Hips, new Vector3(0.01f, 0.53f, 0.05f), k);
                f.HipsRot = Quaternion.Slerp(f.HipsRot, Ry(st.HipYaw) * Rx(6f), k);
                f.Ankle[0] = Vector3.Lerp(f.Ankle[0], new Vector3(0.17f, AnkleY, 0.36f), k);
                f.Ankle[1] = Vector3.Lerp(f.Ankle[1], new Vector3(-0.13f, 0.10f, -0.34f), k);
                f.FootPitch[1] += 45f * k;
                f.Spine *= Rx(4f * k);
            }
            if (prone <= 0) return;
            float p = Math.Clamp(prone, 0, 1);
            float down = Ease(p), legs = Ease(p / 0.65f), front = Ease((p - 0.45f) / 0.55f), rot = Ease((p - 0.15f) / 0.85f);
            f.Hips = Vector3.Lerp(f.Hips, new Vector3(0f, 0.17f, -0.10f), down) + new Vector3(0, 0, 0.14f * MathF.Sin(MathF.PI * down)); // the body goes forward over the hands as it lowers
            f.HipsRot = Quaternion.Slerp(f.HipsRot, Rx(82f), rot);
            f.Ankle[1] = Vector3.Lerp(f.Ankle[1], new Vector3(-0.14f, 0.11f, -0.80f), legs);   // the back leg stretches out first
            f.Ankle[0] = Vector3.Lerp(f.Ankle[0], new Vector3(0.14f, 0.11f, -0.80f), front);   // the front foot stays planted until the body is nearly down
            f.FootPitch[0] += 150f * front; f.FootPitch[1] += 105f * legs;
            f.Spine *= Rx(-4f * p + 22f * MathF.Sin(MathF.PI * p));
            f.Chest *= Rx(-8f * p + 10f * MathF.Sin(MathF.PI * p));
            f.Neck *= Rx(-30f * rot); f.Head *= Rx(-25f * rot);
        }

        // ------------------------------------------------------------------------------------------- the clips
        /// <summary>The full drill: unslings the rifle, shoulders it, fires at the style's shots (each raises "muzzle" events for the particle system), works the bolt between them, then lowers and slings it again.</summary>
        public Clip FireClip(FireStyle st) => FireClip(st, Posture.Stand, st.Name);

        private Clip FireClip(FireStyle st, Posture posture, string name)
        {
            var rd = MakeDrill(st, posture);
            // a kneel or a lying drill starts and ends on its feet: the way down and up is part of the clip, the drill itself runs in the middle of it
            float lead = posture == Posture.Prone ? 1.7f : posture == Posture.Kneel ? 0.9f : 0f, total = st.Dur + 2 * lead;
            var clip = Bake(name, total, (int)(total * 30), u =>
            {
                float t = u * total, phase = Math.Clamp((t - lead) / st.Dur, 0, 1);
                var b = FireBeat(st, phase);
                if (posture == Posture.Kneel) b.Down = Math.Min(Ease(t / lead), 1 - Ease((t - lead - st.Dur) / lead));
                else if (posture == Posture.Prone)
                {
                    b.Down = Math.Min(Ease(t / 0.7f), 1 - Ease((t - (total - 0.7f)) / 0.7f));
                    b.Prone = Math.Min(Ease((t - 0.6f) / 1.0f), 1 - Ease((t - (total - 1.7f)) / 1.0f));
                }
                return RifleFrame(rd, b, t);
            }, 0);
            clip.Events = ShotEvents(st, 0, 1, lead);
            return clip;
        }

        private static ClipEvent[] ShotEvents(FireStyle st, float p0, float p1, float t0)
            => st.Shots.Where(s => s >= p0 && s <= p1).Select(s => (s - p0) * st.Dur + t0)
                .SelectMany(t => new[] { new ClipEvent(t, "muzzle_flash", HumanMale.MuzzleAttach), new ClipEvent(t, "muzzle_smoke", HumanMale.MuzzleAttach) }).OrderBy(e => e.Time).ToArray();

        // a stretch [p0, p1] of the full drill as a clip of its own that plays once
        private Clip Part(FireStyle st, string name, float p0, float p1)
        {
            var rd = MakeDrill(st, Posture.Stand);
            float len = (p1 - p0) * st.Dur;
            var clip = Bake(name, len, Math.Max(2, (int)(len * 30)), u => { float phase = p0 + (p1 - p0) * u; return RifleFrame(rd, FireBeat(st, phase), phase * st.Dur); }, 0, once: true);
            clip.Events = ShotEvents(st, p0, p1, 0);
            return clip;
        }

        /// <summary>A reload that starts and ends with the rifle shouldered, from a script of beats over time.</summary>
        private Clip ReloadClip(string name, float dur, Func<float, Beat> script)
        {
            var rd = MakeDrill(FireStyles[0], Posture.Stand);
            return Bake(name, dur, (int)(dur * 30), u => { float t = u * dur; return RifleFrame(rd, script(t), t); }, 0, once: true);
        }

        private static Beat Ready(float t) => new() { Idle = 0.06f, PathP = 1f, Stance = 1f, WR = 1f, WL = 1f };

        /// <summary>The reloads: the bolt-action rifle's clip of loose rounds, the self-loading rifle's en-bloc clip, and the generic magazine swap.</summary>
        private IEnumerable<Clip> Reloads()
        {
            // bolt action: the rifle comes down a little, the bolt is thrown open, a clip taken from the pouch and pressed into the action, the bolt closed
            yield return ReloadClip(ReloadBolt, 6.6f, t =>
            {
                var b = Ready(t);
                b.Port = Keys(t, (0f, 0f), (0.8f, 1f), (5.4f, 1f), (6.4f, 0f));
                b.Look = 0.6f * Hump(t, 0.6f, 5.6f, 0.8f);
                b.Bolt = Math.Max(Hump(t, 0.7f, 2.0f, 0.4f), Hump(t, 4.6f, 5.8f, 0.4f));
                b.Pull = Math.Clamp(Keys(t, (1.0f, 0f), (1.4f, 1f), (4.9f, 1f), (5.4f, 0f)), 0, 1);
                b.Pouch = Hump(t, 2.0f, 3.2f, 0.55f);
                b.Load = Hump(t, 3.1f, 4.6f, 0.55f);
                b.Press = 0.03f * Hump(t, 3.7f, 4.1f, 0.2f);
                return b;
            });
            // self-loading: a clip pressed in from the top, the action closing on it by itself
            yield return ReloadClip(ReloadSemi, 4.6f, t =>
            {
                var b = Ready(t);
                b.Port = Keys(t, (0f, 0f), (0.7f, 1f), (3.7f, 1f), (4.5f, 0f));
                b.Look = 0.6f * Hump(t, 0.5f, 4.0f, 0.7f);
                b.Pouch = Hump(t, 0.9f, 2.0f, 0.5f);
                b.Load = Hump(t, 1.9f, 3.5f, 0.5f);
                b.Press = 0.035f * Hump(t, 2.5f, 3.0f, 0.25f);
                return b;
            });
            // generic: the empty magazine dropped, a fresh one taken from the pouch and pushed up into the well from below
            yield return ReloadClip(Reload, 4.4f, t =>
            {
                var b = Ready(t);
                b.Port = Keys(t, (0f, 0f), (0.7f, 1f), (3.5f, 1f), (4.3f, 0f));
                b.Look = 0.6f * Hump(t, 0.5f, 3.8f, 0.7f);
                b.Pouch = Hump(t, 0.9f, 2.0f, 0.5f);
                b.Mag = Hump(t, 1.9f, 3.4f, 0.5f);
                b.Press = 0.04f * Hump(t, 2.5f, 2.9f, 0.2f);
                return b;
            });
        }


        // both hands on the head and the body folded over it
        private Clip DuckClip()
        {
            const float dur = 1.5f;
            return Bake(Duck, dur, (int)(dur * 30), phase =>
            {
                float t = phase * dur;
                float d = Math.Min(Ease(t / 0.28f), 1 - Ease((t - 1.15f) / 0.35f));
                var f = IdleFrame(0.1f);
                f.Hips += new Vector3(0, -0.34f * d, 0.05f * d);
                f.Spine *= Rx(24f * d); f.Chest *= Rx(14f * d); f.Neck *= Rx(8f * d); f.Head *= Rx(6f * d);
                f.Ankle[0] = new Vector3(0.17f, AnkleY, 0.05f * d); f.Ankle[1] = new Vector3(-0.17f, AnkleY, -0.05f * d);
                for (int s = 0; s < 2; s++)
                {
                    float sg = s == 0 ? 1f : -1f;
                    var F = Vector3.Normalize(new Vector3(-0.4f * sg, 0f, -0.9f)); var N = new Vector3(0, -1, 0); // palms down on the crown, fingers back: the forearms wrap round the front of the head
                    f.HandTarget[s] = new Vector3(0.085f * sg, 0.53f, 0.07f) - F * 0.05f - N * 0.04f;
                    f.HandRot[s] = HandRotation(s, F, N);
                    f.HandBlend[s] = Ease(d * 1.4f); f.Fingers[s] = 30f;
                }
                return f;
            }, 0, once: true);
        }

        // a start at something close: the body jerks back, the arms fly up and the head turns away
        private Clip FlinchClip()
        {
            const float dur = 0.9f;
            return Bake(Flinch, dur, (int)(dur * 30), phase =>
            {
                float t = phase * dur;
                float j = Math.Min(Ease(t / 0.08f), 1) * MathF.Exp(-Math.Max(0, t - 0.08f) / 0.25f);
                var f = IdleFrame(0.1f + phase * 0.05f);
                f.Hips += new Vector3(0, -0.06f * j, -0.04f * j);
                f.Spine *= Rx(-8f * j) * Ry(-12f * j); f.Chest *= Rx(-6f * j); f.Head *= Ry(-22f * j) * Rx(-8f * j);
                f.ArmSwing[0] += 35f * j; f.ArmSwing[1] += 35f * j; f.Elbow[0] += 50f * j; f.Elbow[1] += 50f * j; f.Shrug[0] += 0.03f * j; f.Shrug[1] += 0.03f * j;
                return f;
            }, 0, once: true);
        }

        // the helmet comes off, is turned over in both hands and put back on
        private Clip HelmetClip()
        {
            const float dur = 5.6f;
            var centre0 = new Vector3(0, 0.445f, 0.022f);   // the helmet's middle on the head (chest space)
            var centre1 = new Vector3(0, 0.36f, 0.36f);     // held out in front of the face
            var offset = new Vector3(0, 0.11f, 0.01f);      // from the headgear joint to the helmet's middle
            return Bake(HelmetInspect, dur, (int)(dur * 30), phase =>
            {
                float t = phase * dur;
                float reach = Ease(t / 0.9f) * (1 - Ease((t - 4.7f) / 0.8f));
                // 0 on the head, 1 held out: straight up off the head first, then forward and down to the face
                float a = Math.Min(Ease((t - 0.8f) / 1.3f), 1 - Ease((t - 3.5f) / 1.3f));
                float up = Ease(a / 0.4f), fwd = Ease((a - 0.3f) / 0.7f);
                var centre = new Vector3(0, centre0.Y + 0.13f * up * (1 - fwd) + (centre1.Y - centre0.Y) * fwd, centre0.Z + (centre1.Z - centre0.Z) * fwd);
                float turn = MathF.Sin(Math.Clamp((t - 2.0f) / 1.6f, 0, 1) * MathF.PI * 2f); // looks one way, then the other
                var rot = Rx(-50f * fwd) * Ry(40f * turn * fwd);
                var f = IdleFrame(0.12f + phase * 0.04f);
                f.HeadgearPos = centre - Rot(offset, rot); f.HeadgearRot = rot; f.HeadgearBlend = a > 0.001f ? 1f : 0f;
                f.Head *= Rx(12f * fwd); f.Neck *= Rx(5f * fwd);
                for (int s = 0; s < 2; s++)
                {
                    float sg = s == 0 ? 1f : -1f;
                    // thumbs to the front, fingers over the rim; the palms press in from either side
                    var F = Vector3.Normalize(Rot(new Vector3(-0.2f * sg, 0.25f, 0.95f), rot)); var N = Rot(new Vector3(-sg, 0, 0), rot);
                    var pt = centre + Rot(new Vector3(0.125f * sg, -0.03f, 0), rot);
                    // the hand goes up to the helmet by way of the side, with the elbow out, and not across the body
                    var goal = pt - F * 0.05f - N * 0.04f; var start = new Vector3(0.24f * sg, -0.22f, 0.02f);
                    float arc = MathF.Sin(MathF.PI * reach);
                    f.HandTarget[s] = Vector3.Lerp(start, goal, Ease(reach)) + new Vector3(0.11f * sg, 0.02f, 0.06f) * arc;
                    f.HandRot[s] = HandRotation(s, F, N);
                    f.HandBlend[s] = Math.Min(1f, reach * 6f); f.Fingers[s] = 40f;
                    f.ArmOut[s] += 25f * arc;
                }
                return f;
            }, 0, once: true);
        }

        /// <summary>Every clip made here: the parts of each firing drill, the reloads, the drill on a knee and flat, ducking, a start, and the helmet.</summary>
        public IEnumerable<Clip> Combat()
        {
            foreach (var st in FireStyles)
            {
                yield return Part(st, PartOf(Draw, st.Name), 0f, st.Up[^1].T);
                yield return Part(st, PartOf(Shoot, st.Name), st.Up[^1].T, st.Down[0].T);
                yield return Part(st, PartOf(Stow, st.Name), st.Down[0].T, 1f);
            }
            foreach (var clip in Reloads()) yield return clip;
            yield return FireClip(FireStyles[0], Posture.Kneel, FireKneel);
            yield return FireClip(FireStyles[0], Posture.Prone, FireProne);
            yield return DuckClip();
            yield return FlinchClip();
            yield return HelmetClip();
        }
    }
}
