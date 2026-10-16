using System.Numerics;

namespace Burger.Core.Figures;

public static partial class HumanMale
{
    /// <summary>The attack of soldiers who carry a spear and no firearm: draws the spear back, lunges and thrusts it, then recovers. Plays once.</summary>
    public const string AttackSpear = "attack_spear";
    /// <summary>A second, fiercer thrust: a long wind-up with the weight right back, a deep lunge with the whole body behind the spear, and a slower recovery.</summary>
    public const string AttackSpearLunge = "attack_spear_lunge";
    /// <summary>The family name of an attack; <see cref="Uniforms.ClipFor"/> picks the clip of the weapon the soldier carries (<c>attack_ach</c> for a country's own, else <see cref="AttackSpear"/>).</summary>
    public const string Attack = "attack";

    /// <summary>The idle, walk and run of a soldier carrying a shield and spear; <see cref="SoldierGear.Compose"/> swaps them in for models with such gear.</summary>
    public const string CarrySuffix = "_carry";

    private sealed partial class Solver
    {
        public IEnumerable<Clip> CarryClips()
        {
            Carry = ApplyCarry;
            var clips = new[] { Idle(HumanMale.Idle + CarrySuffix), Locomotion(Walk + CarrySuffix, WalkGait), Locomotion(Run + CarrySuffix, RunGait) };
            Carry = null;
            return clips;
        }

        public Clip SpearAttackClip() => SpearAttack(AttackSpear, 1.3f, 0.45f, 0.12f, 0.70f, 1f);

        /// <summary>The aggressive thrust: more twist, a deeper crouch and stride, the spear drawn right back and driven far forward.</summary>
        public Clip SpearLungeClip() => SpearAttack(AttackSpearLunge, 1.9f, 0.70f, 0.09f, 1.05f, 1.9f);

        /// <summary>A spear thrust with the right hand: wind-up with the weight back, a fast lunge on the left foot, a short hold, and back to rest. <paramref name="power"/> scales every motion.</summary>
        private Clip SpearAttack(string name, float dur, float windEnd, float thrustTime, float recoverAt, float power)
        {
            bool big = power > 1f;
            // the right hand in chest space (the spear's butt trails behind it, so the hand stays out beside the body), and the shaft's direction
            var windHand = big ? new Vector3(-0.32f, -0.24f, -0.10f) : new Vector3(-0.30f, -0.14f, 0.08f);
            var windDir = Vector3.Normalize(big ? new Vector3(0.12f, 0.55f, 0.85f) : new Vector3(0.10f, 0.40f, 0.90f));
            var thrustHand = big ? new Vector3(-0.22f, 0.04f, 0.54f) : new Vector3(-0.27f, 0.02f, 0.50f);
            var thrustDir = Vector3.Normalize(big ? new Vector3(0.16f, 0.06f, 1f) : new Vector3(0.14f, 0.10f, 1f));
            return Bake(name, dur, (int)(dur * 30), u =>
            {
                float t = u * dur;
                float a = Ease(t / windEnd), th = Ease((t - windEnd) / thrustTime), rc = Ease((t - recoverAt) / (dur - recoverAt));
                float stance = a * (1 - rc);               // 0 at rest, 1 in the fighting stance
                float wind = a * (1 - th), thr = th * (1 - rc); // how far back, how far forward
                var f = ApplyCarry(IdleFrame(0.1f + u * 0.02f));
                f.Hips += new Vector3(0, -0.07f * power * stance - 0.06f * (power - 1f) * thr, (0.05f * thr - 0.02f * wind) * power + 0.10f * (power - 1f) * thr);
                f.Spine *= Ry((-14f * wind + 12f * thr) * power) * Rx(5f * power * thr);
                f.Chest *= Ry((-10f * wind + 10f * thr) * power) * Rx(4f * (power - 1f) * thr);
                f.Head *= Ry(6f * wind - 6f * thr);
                f.Ankle[0] += new Vector3(0.02f * stance, 0, 0.12f * stance + 0.20f * power * thr + 0.18f * (power - 1f) * thr);
                f.Ankle[1] += new Vector3(-0.03f * stance, 0, -0.14f * power * stance - 0.10f * (power - 1f) * thr);
                f.FootYaw[1] += 25f * stance;
                // the shield arm comes up a little to cover
                f.ArmSwing[0] += 6f * stance;
                // the spear hand follows the path, and the shaft turns with it
                var dir = Vector3.Normalize(Vector3.Lerp(windDir, thrustDir, th));
                var from = Vector3.Lerp(new Vector3(-0.34f, -0.30f, 0.02f), windHand, a);
                var hand = Vector3.Lerp(from, thrustHand, th);
                hand = Vector3.Lerp(hand, new Vector3(-0.34f, -0.30f, 0.02f), rc);
                f.HandTarget[1] = hand;
                f.HandRot[1] = FromTo(Vector3.UnitY, Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, dir, Math.Min(1f, stance * 1.6f))));
                f.HandRotBlend[1] = 1f; f.HandBlend[1] = Math.Min(1f, stance * 2.5f);
                return f;
            }, 0, once: true);
        }
    }
}
