using System.Numerics;

namespace Burger.Core.Figures;

public static partial class HumanMale
{
    /// <summary>The attack of soldiers who carry a spear and no firearm: draws the spear back, lunges and thrusts it, then recovers. Plays once.</summary>
    public const string AttackSpear = "attack_spear";
    /// <summary>The family name of an attack; <see cref="Uniforms.ClipFor"/> picks the clip of the weapon the soldier carries (<c>attack_ach</c> for a country's own, else <see cref="AttackSpear"/>).</summary>
    public const string Attack = "attack";

    private sealed partial class Solver
    {
        /// <summary>A spear thrust with the right hand: wind-up with the weight back, a fast lunge on the left foot, a short hold, and back to rest.</summary>
        public Clip SpearAttackClip()
        {
            const float dur = 1.3f;
            // the right hand in chest space (the spear's butt trails behind it, so the hand stays out beside the body), and the shaft's direction
            var windHand = new Vector3(-0.30f, -0.14f, 0.08f); var windDir = Vector3.Normalize(new Vector3(0.10f, 0.40f, 0.90f));
            var thrustHand = new Vector3(-0.27f, 0.02f, 0.50f); var thrustDir = Vector3.Normalize(new Vector3(0.14f, 0.10f, 1f));
            return Bake(AttackSpear, dur, (int)(dur * 30), u =>
            {
                float t = u * dur;
                float a = Ease(t / 0.45f), th = Ease((t - 0.45f) / 0.12f), rc = Ease((t - 0.70f) / 0.60f);
                float stance = a * (1 - rc);               // 0 at rest, 1 in the fighting stance
                float wind = a * (1 - th), thr = th * (1 - rc); // how far back, how far forward
                var f = IdleFrame(0.1f + u * 0.02f);
                f.Hips += new Vector3(0, -0.07f * stance, 0.05f * thr - 0.02f * wind);
                f.Spine *= Ry(-14f * wind + 12f * thr) * Rx(5f * thr);
                f.Chest *= Ry(-10f * wind + 10f * thr);
                f.Head *= Ry(6f * wind - 6f * thr);
                f.Ankle[0] += new Vector3(0.02f * stance, 0, 0.12f * stance + 0.20f * thr);
                f.Ankle[1] += new Vector3(-0.03f * stance, 0, -0.14f * stance);
                f.FootYaw[1] += 25f * stance;
                // the shield arm comes up a little to cover
                f.ArmSwing[0] += 18f * stance; f.Elbow[0] += 35f * stance;
                // the spear hand follows the path, and the shaft turns with it
                var dir = Vector3.Normalize(Vector3.Lerp(windDir, thrustDir, th));
                var from = Vector3.Lerp(new Vector3(-0.34f, -0.30f, 0.02f), windHand, a);
                var hand = Vector3.Lerp(from, thrustHand, th);
                hand = Vector3.Lerp(hand, new Vector3(-0.34f, -0.30f, 0.02f), rc);
                f.HandTarget[1] = hand;
                f.HandRot[1] = FromTo(Vector3.UnitY, Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, dir, Math.Min(1f, stance * 1.6f))));
                f.HandBlend[1] = Math.Min(1f, stance * 2.5f);
                f.Fingers[1] = 30f * f.HandBlend[1];
                return f;
            }, 0, once: true);
        }
    }
}
