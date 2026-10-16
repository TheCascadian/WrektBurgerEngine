using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>
/// Clothing and equipment for the human body. Every piece is a <see cref="FigureModel"/> on the body's own rig, skinned to its
/// joints (a helmet to the head, a pack to the chest, a tunic to the same joints as the skin under it), so the body's clips move
/// it with no extra work and one set of animations serves every outfit. An outfit (<see cref="Soldier"/>) is the body and a list
/// of pieces merged into one model, which is drawn in one call. A game or mod adds pieces and outfits with
/// <see cref="FigureSystem.Register"/> and <see cref="FigureSystem.DefineOutfit"/>, or replaces a piece with a .glb of its key.
/// </summary>
public static class SoldierGear
{
    public const string Soldier = "soldier";
    public const string Tunic = "gear_tunic", Trousers = "gear_trousers", Boots = "gear_boots", Belt = "gear_belt",
        Helmet = "gear_helmet", Pack = "gear_pack", Rifle = "gear_rifle", RifleSemi = "gear_rifle_semi", Puttees = "gear_puttees";

    /// <summary>The pieces of the standard infantry kit, in drawing order.</summary>
    public static readonly string[] Kit = KitFor(Uniform.Standard, "");

    /// <summary>The piece keys of a uniform's kit, in drawing order (<paramref name="suffix"/> is added to each key).</summary>
    public static string[] KitFor(Uniform u, string suffix)
    {
        var keys = new List<string> { Boots };
        if (u.Puttees != null) keys.Add(Puttees);
        keys.AddRange(new[] { Trousers, Tunic, Belt });
        if (u.Rifle != None) keys.AddRange(new[] { Pack, u.Rifle == SemiAuto ? RifleSemi : Rifle }); // no firearm or pack for ancient armies
        keys.Add(Helmet);
        return keys.Select(k => k + suffix).ToArray();
    }

    /// <summary>Value of <see cref="Uniform.Rifle"/> for the second rifle type, a self-loading rifle. Anything else (or null) is the basic bolt-action rifle.</summary>
    public const string SemiAuto = "semi";

    /// <summary>Value of <see cref="Uniform.Rifle"/> for armies without firearms: the kit has neither rifle nor pack.</summary>
    public const string None = "none";

    /// <summary>The piece an outfit falls back on when the one it asks for does not exist: the second rifle type to the basic rifle of the same country.</summary>
    public static string? Fallback(string key) => key.StartsWith(RifleSemi, StringComparison.OrdinalIgnoreCase) ? Rifle + key[RifleSemi.Length..] : null;

    private static readonly Vector3 Steel = new(0.19f, 0.20f, 0.22f), Wood = new(0.43f, 0.27f, 0.14f);

    /// <summary>Builds the standard pieces for a body (they use its rig and, for clothing, its measurements).</summary>
    public static List<FigureModel> BuildPieces(FigureModel body) => BuildPieces(body, Uniform.Standard, "");

    /// <summary>Builds the pieces of one uniform; each key gets <paramref name="suffix"/> so several uniforms live side by side.</summary>
    public static List<FigureModel> BuildPieces(FigureModel body, Uniform u, string suffix)
    {
        Vector3 TunicColor = u.Tunic, TrouserColor = u.Trousers, Leather = u.Leather, Webbing = u.Webbing, Canvas = u.Canvas, Bedroll = u.Bedroll;
        var rig = body.Rig;
        var rest = new Matrix4x4[rig.Count];
        rig.WorldTransforms(rig.RestPose(), rest);
        Vector3 At(string joint) => rest[rig.Find(joint)].Translation;
        var arms = new[] { At("upper_arm.L"), At("upper_arm.R") };
        var b = body.Mesh;

        var list = new List<FigureModel>();
        FigureModel Piece(string key, FigureMesh mesh) { var m = new FigureModel(key + suffix, rig, mesh); list.Add(m); return m; }

        // ---- clothing: cut from the skin and pushed out along its normals, so it follows every curve and weights ----
        var shirt = u.ShirtKind;
        float hem = shirt switch { ShirtKind.Jacket => 0.93f, ShirtKind.Waffenrock => 0.90f, ShirtKind.Capote => 0.82f, ShirtKind.Gymnastyorka => 0.84f, ShirtKind.Cotton => 0.86f, ShirtKind.Stand => 0.88f, _ => 0.87f };
        float cloth = shirt switch { ShirtKind.Cotton => 0.0155f, ShirtKind.Capote => 0.0125f, ShirtKind.Jacket => 0.0115f, _ => 0.011f };
        float flare = shirt switch { ShirtKind.Waffenrock or ShirtKind.Gymnastyorka => 0.003f, ShirtKind.Capote => 0.012f, ShirtKind.Cotton => 0.006f, _ => 0.010f };
        float sleeve = shirt == ShirtKind.Cotton ? 0.0145f : 0.0095f;
        const float NeckSeam = 1.525f;
        var tunicParts = shirt == ShirtKind.Capote ? new[] { "trunk", "arm.L", "arm.R", "leg.L", "leg.R" } : new[] { "trunk", "arm.L", "arm.R" };
        Piece(Tunic, Cut(b, tunicParts, TunicColor, (part, p) =>
        {
            if (part == "trunk") // the skirt flares at the hem; above the chest the cloth rises over the shoulders to the neck, thinning to the collar's seam
            {
                if (p.Y < hem || p.Y > NeckSeam) return -1;
                float top = Smooth(1.46f, NeckSeam, p.Y);
                return (cloth + flare * (1 - Smooth(hem, hem + 0.12f, p.Y))) * (1 - top * 0.45f);
            }
            if (part.StartsWith("leg")) return p.Y is >= 0.58f and <= 0.90f ? 0.0125f + 0.0135f * (1 - Smooth(0.82f, 0.94f, p.Y)) + 0.004f * (1 - Smooth(0.58f, 0.82f, p.Y)) : -1; // the skirts of a greatcoat
            float d = Math.Min(Vector3.Distance(p, arms[0]), Vector3.Distance(p, arms[1]));
            return d <= 0.49f ? sleeve : -1;                                                                                       // sleeves end at the wrist
        }));
        Piece(Trousers, Cut(b, new[] { "trunk", "leg.L", "leg.R" }, TrouserColor, (part, p) =>
        {
            if (part == "trunk") return p.Y is >= 0.80f and <= 0.97f ? 0.0095f : -1;
            return p.Y >= 0.20f ? 0.0095f + 0.004f * (1 - Smooth(0.2f, 0.6f, p.Y)) : -1;                                         // baggier towards the ankle
        }));
        var boot = u.BootKind;
        float bootTop = boot switch { BootKind.Tall => 0.44f, BootKind.Soft => 0.36f, BootKind.Lace => 0.30f, BootKind.Cloth => 0.13f, BootKind.Hobnail => 0.235f, _ => 0.215f };
        float sole = boot switch { BootKind.Hobnail => 0.006f, BootKind.Heavy => 0.010f, BootKind.Cloth => -0.003f, BootKind.Soft => 0.002f, _ => 0f };
        Piece(Boots, Cut(b, new[] { "foot.L", "foot.R", "leg.L", "leg.R" }, Leather, (part, p) =>
            part.StartsWith("foot") ? (boot == BootKind.Cloth ? 0.0075f : 0.011f) + (p.Y < 0.04f ? sole : 0f) : p.Y > bootTop ? -1 : boot switch
            {
                BootKind.Tall => 0.0155f,
                BootKind.Soft => 0.0135f + 0.006f * Smooth(0.2f, 0.36f, p.Y),
                BootKind.Cloth => 0.0075f,
                _ => 0.0135f,
            }, boot == BootKind.Tall ? (part, p) => p.Y > 0.41f ? 1.18f : 1f : null, clampToGround: true));

        if (u.Puttees is { } wrap) // cloth wound round the shin, over the boot top
            Piece(Puttees, Cut(b, new[] { "leg.L", "leg.R" }, wrap, (part, p) => p.Y is >= 0.205f and <= 0.50f ? 0.0125f : -1));

        // ---- belt, and what hangs from it; the details of the tunic (collar, pockets) ride on this piece too ----
        var m = new HumanMale.MeshBuilder(rig) { Base = Leather };
        var X = Vector3.UnitX; var Z = Vector3.UnitZ; var Y = Vector3.UnitY;
        var hips = HumanMale.W("hips"); var chest = HumanMale.W("chest"); var spine = HumanMale.W("spine");
        void Tube(Vector3 pa, Vector3 pb, float r, (string, float)[] w, Vector3 colour, float taper = 0.88f)
        {
            var d = Vector3.Normalize(pb - pa); float len = Vector3.Distance(pa, pb);
            var tu = Vector3.Normalize(Vector3.Cross(d, MathF.Abs(d.Y) < 0.9f ? Y : X)); var tv = Vector3.Cross(d, tu);
            m.Base = colour;
            HumanMale.Ring Rg(float t, float k) => new(pa + d * (len * t), r * k, r * k, r * k, 2.0f, w);
            m.Loft(tu, tv, 10, new[] { Rg(0, taper), Rg(0.08f, 1), Rg(0.92f, 1), Rg(1, taper) }, pa - d * 0.002f, pb + d * 0.002f);
        }
        void Pouch(Vector3 c, float hx, float hy, float hz, (string, float)[] w, Vector3 colour) { m.Base = colour; m.Box(c, X * hx, Y * hy, Z * hz, w); }
        float BeltZ(float x) => 0.117f * MathF.Pow(MathF.Max(1 - MathF.Pow(MathF.Abs(x) / 0.186f, 2.3f), 0.01f), 1 / 2.3f);
        bool sash = u.BeltKind is BeltKind.Bandolier or BeltKind.Sam;
        m.Base = u.BeltKind is BeltKind.Web or BeltKind.Cartridge or BeltKind.Bandolier ? Webbing : Leather;
        m.Loft(X, Z, 16, new[]
        {
            new HumanMale.Ring(new Vector3(0, 0.945f, 0), 0.186f, 0.117f, 0.137f, 2.3f, hips),
            new HumanMale.Ring(new Vector3(0, sash ? 1.012f : 0.995f, 0), 0.174f, 0.116f, 0.125f, 2.3f, hips),
        }, new Vector3(0, 0.94f, 0), new Vector3(0, sash ? 1.016f : 1.0f, 0));
        var buckleBrass = new Vector3(0.62f, 0.52f, 0.2f); var bluedS = Steel * 1.2f;
        Vector3 buckle = u.BeltKind is BeltKind.Leather3 or BeltKind.Sam or BeltKind.Web ? bluedS : buckleBrass;
        float bw = u.BeltKind == BeltKind.Sam ? 0.026f : u.BeltKind == BeltKind.Box ? 0.012f : 0.02f;
        Pouch(new Vector3(0, 0.97f, 0.121f), bw, u.BeltKind == BeltKind.Box ? 0.012f : 0.017f, 0.005f, hips, buckle);
        var leather2 = Leather * 1.12f;
        foreach (int side in new[] { 1, -1 })
            switch (u.BeltKind)
            {
                case BeltKind.Web: // two large web pouches, a scabbard on the left and a water bottle on the right rear
                    Pouch(new Vector3(0.082f * side, 0.98f, 0.122f), 0.045f, 0.040f, 0.026f, hips, Webbing);
                    Pouch(new Vector3(0.082f * side, 1.014f, 0.126f), 0.047f, 0.010f, 0.027f, hips, Webbing * 0.85f);
                    break;
                case BeltKind.Cartridge: // a row of small pockets each side
                    for (int i = 0; i < 5; i++)
                    {
                        float x = side * (0.04f + i * 0.030f);
                        Pouch(new Vector3(x, 0.975f, BeltZ(x) + 0.006f), 0.0135f, 0.034f, 0.017f, hips, Webbing * 1.05f);
                        Pouch(new Vector3(x, 1.012f, BeltZ(x) + 0.007f), 0.0145f, 0.006f, 0.018f, hips, Webbing * 0.8f);
                    }
                    break;
                case BeltKind.Leather3: // two groups of three black cartridge pouches
                    for (int i = 0; i < 3; i++)
                    {
                        float x = side * (0.045f + i * 0.038f);
                        Pouch(new Vector3(x, 0.978f, BeltZ(x) + 0.010f), 0.016f, 0.034f, 0.021f, hips, leather2);
                        Pouch(new Vector3(x, 1.012f, BeltZ(x) + 0.011f), 0.017f, 0.008f, 0.022f, hips, Leather * 0.8f);
                    }
                    break;
                case BeltKind.Bidon: // two big three-lidded boxes at the front
                    Pouch(new Vector3(0.060f * side, 0.978f, BeltZ(0.06f) + 0.012f), 0.034f, 0.034f, 0.026f, hips, leather2);
                    for (int i = -1; i <= 1; i++) Pouch(new Vector3((0.060f + 0.020f * i) * side, 1.012f, BeltZ(0.06f) + 0.014f), 0.0095f, 0.008f, 0.027f, hips, Leather * 0.78f);
                    break;
                case BeltKind.Sam: // twin small pouches
                    foreach (float x in new[] { 0.070f, 0.118f })
                        Pouch(new Vector3(x * side, 0.985f, BeltZ(x) + 0.010f), 0.020f, 0.036f, 0.020f, hips, leather2);
                    break;
                case BeltKind.Box: // a pair of squat ammunition boxes
                    Pouch(new Vector3(0.062f * side, 0.975f, BeltZ(0.062f) + 0.011f), 0.026f, 0.028f, 0.022f, hips, Leather * 1.25f);
                    Pouch(new Vector3(0.062f * side, 1.0f, BeltZ(0.062f) + 0.012f), 0.027f, 0.005f, 0.023f, hips, Leather * 0.85f);
                    break;
                case BeltKind.Bandolier: break;
                default:
                    Pouch(new Vector3(0.075f * side, 0.975f, 0.118f), 0.036f, 0.036f, 0.024f, hips, leather2);
                    break;
            }
        switch (u.BeltKind) // the single items: bottles, scabbards, bags, tools
        {
            case BeltKind.Web:
                Tube(new Vector3(-0.205f, 0.975f, 0.03f), new Vector3(-0.215f, 0.77f, 0.03f), 0.013f, hips, Steel * 0.8f);
                Tube(new Vector3(0.12f, 0.79f, -0.135f), new Vector3(0.12f, 0.96f, -0.135f), 0.040f, hips, Webbing * 0.95f);
                break;
            case BeltKind.Cartridge:
                Tube(new Vector3(0.12f, 0.78f, -0.135f), new Vector3(0.12f, 0.96f, -0.135f), 0.040f, hips, Canvas * 1.05f);
                Pouch(new Vector3(-0.135f, 0.92f, -0.125f), 0.030f, 0.040f, 0.022f, hips, Webbing * 0.9f);
                break;
            case BeltKind.Leather3:
                Pouch(new Vector3(-0.10f, 0.91f, -0.150f), 0.070f, 0.058f, 0.034f, hips, Canvas * 0.85f);
                Tube(new Vector3(-0.205f, 0.975f, 0.03f), new Vector3(-0.215f, 0.76f, 0.03f), 0.012f, hips, Leather * 0.9f);
                Tube(new Vector3(0.12f, 0.80f, -0.135f), new Vector3(0.12f, 0.95f, -0.135f), 0.036f, hips, Canvas * 0.95f);
                break;
            case BeltKind.Bidon:
                Tube(new Vector3(-0.19f, 0.76f, 0.045f), new Vector3(-0.19f, 0.99f, 0.045f), 0.046f, hips, Canvas * 1.05f);
                Pouch(new Vector3(0.10f, 0.93f, -0.155f), 0.070f, 0.068f, 0.030f, hips, Canvas);
                break;
            case BeltKind.Sam:
                Pouch(new Vector3(-0.12f, 0.87f, -0.152f), 0.036f, 0.052f, 0.004f, hips, Steel * 1.1f);
                Tube(new Vector3(-0.12f, 0.92f, -0.152f), new Vector3(-0.12f, 1.03f, -0.152f), 0.010f, hips, Wood);
                Tube(new Vector3(0.12f, 0.80f, -0.135f), new Vector3(0.12f, 0.96f, -0.135f), 0.036f, hips, Canvas * 0.9f);
                break;
            case BeltKind.Box:
                Pouch(new Vector3(0, 0.97f, -0.145f), 0.045f, 0.030f, 0.024f, hips, Leather * 1.25f);
                Tube(new Vector3(0.12f, 0.80f, -0.135f), new Vector3(0.12f, 0.96f, -0.135f), 0.034f, hips, Canvas * 0.95f);
                Tube(new Vector3(-0.205f, 0.975f, 0.03f), new Vector3(-0.213f, 0.78f, 0.03f), 0.012f, hips, Steel * 0.8f);
                break;
            case BeltKind.Bandolier: // a cartridge bandolier from the right shoulder to the left hip, and a gourd
                var p0 = new Vector3(0.11f, 1.43f, 0.114f); var p1 = new Vector3(0.005f, 1.265f, 0.152f); var p2 = new Vector3(-0.10f, 1.10f, 0.108f);
                Tube(p0, p1, 0.012f, chest, Leather * 0.9f, 1f); Tube(p1, p2, 0.012f, chest, Leather * 0.9f, 1f);
                for (int i = 0; i < 6; i++)
                {
                    float t = (i + 0.5f) / 6; var c = t < 0.5f ? Vector3.Lerp(p0, p1, t * 2) : Vector3.Lerp(p1, p2, t * 2 - 1);
                    Pouch(c + new Vector3(0, 0, 0.012f), 0.014f, 0.017f, 0.012f, chest, leather2);
                }
                Tube(new Vector3(0.12f, 0.80f, -0.135f), new Vector3(0.12f, 0.95f, -0.135f), 0.036f, hips, Canvas * 1.1f);
                break;
        }

        // ---- the tunic's own details: collar and pockets ----
        void Collar(float h)
        {
            // grows out of the tunic's neck seam: the first ring is the cloth's own outline there, then it stands (and, with a fall collar, folds over)
            m.Base = TunicColor * 0.92f;
            HumanMale.Ring Cr(float y, float w, float zf, float zb, float zc, (string, float)[] wt) => new(new Vector3(0, y, zc), w, zf, zb, 2.0f, wt);
            var neck = HumanMale.W("neck");
            m.Loft(X, Z, 16, new[] { Cr(NeckSeam - 0.012f, 0.066f, 0.066f, 0.076f, 0.004f, neck), Cr(NeckSeam + 0.004f, 0.0645f, 0.0645f, 0.074f, 0.005f, neck),
                    Cr(NeckSeam + 0.020f + h, 0.0645f, 0.0635f, 0.0725f, 0.007f, neck), Cr(NeckSeam + 0.036f + h, 0.0625f, 0.0615f, 0.0705f, 0.009f, neck) },
                new Vector3(0, NeckSeam - 0.012f, 0.004f), new Vector3(0, NeckSeam + 0.037f + h, 0.009f));
        }
        void Pocket(float x, float y, float z, float hw, float hh, (string, float)[] w)
        {
            Pouch(new Vector3(x, y, z), hw, hh, 0.004f, w, TunicColor * 0.93f);
            Pouch(new Vector3(x, y + hh * 0.72f, z + 0.002f), hw * 1.06f, hh * 0.3f, 0.004f, w, TunicColor * 0.8f);
        }
        switch (shirt)
        {
            case ShirtKind.Service:
                Collar(0.006f);
                foreach (int side in new[] { 1, -1 }) Pocket(0.075f * side, 1.325f, 0.141f, 0.030f, 0.032f, chest);
                break;
            case ShirtKind.Jacket:
                Collar(0.006f);
                foreach (int side in new[] { 1, -1 }) { Pocket(0.080f * side, 1.325f, 0.142f, 0.032f, 0.034f, chest); Pocket(0.100f * side, 1.10f, 0.092f, 0.036f, 0.040f, spine); }
                break;
            case ShirtKind.Waffenrock:
                Collar(0.004f);
                foreach (int side in new[] { 1, -1 }) { Pocket(0.080f * side, 1.325f, 0.141f, 0.029f, 0.034f, chest); Pocket(0.098f * side, 1.075f, 0.087f, 0.032f, 0.036f, spine); }
                break;
            case ShirtKind.Stand:
                Collar(0.004f);
                foreach (int side in new[] { 1, -1 }) { Pocket(0.078f * side, 1.325f, 0.141f, 0.026f, 0.030f, chest); Pocket(0.098f * side, 1.085f, 0.088f, 0.030f, 0.034f, spine); }
                break;
            case ShirtKind.Gymnastyorka:
                Collar(0.008f);
                Pouch(new Vector3(0, 1.36f, 0.1385f), 0.011f, 0.115f, 0.004f, chest, TunicColor * 0.85f); // the placket
                break;
            case ShirtKind.Cotton:
                Collar(0.006f);
                for (int i = 0; i < 5; i++) Pouch(new Vector3(0, 1.44f - i * 0.058f, 0.1475f - 0.0035f * i), 0.007f, 0.007f, 0.005f, chest, TunicColor * 0.7f);
                break;
            case ShirtKind.Capote:
                Collar(0.012f);
                foreach (int side in new[] { 1, -1 })
                    for (int i = 0; i < 3; i++) Pouch(new Vector3(0.026f * side, 1.30f - i * 0.12f, 0.1475f - 0.0035f * i), 0.012f, 0.012f, 0.005f, chest, TunicColor * 0.7f); // two rows of buttons
                break;
        }
        Piece(Belt, HumanMale.Finish(m));

        Piece(Helmet, Headgear(rig, u, Leather));

        // ---- pack: the pack on the back with the blanket or greatcoat carried its own way, braced by two straps over the shoulders ----
        m = new HumanMale.MeshBuilder(rig) { Base = Canvas };
        switch (u.PackKind)
        {
            case PackKind.Light: // a short haversack, a rolled shelter half under it and the entrenching tool strapped on
                Pouch(new Vector3(0, 1.30f, -0.175f), 0.115f, 0.145f, 0.062f, chest, Canvas);
                Pouch(new Vector3(0, 1.425f, -0.18f), 0.118f, 0.030f, 0.066f, chest, Canvas * 0.88f);
                Tube(new Vector3(-0.13f, 1.14f, -0.215f), new Vector3(0.13f, 1.14f, -0.215f), 0.040f, chest, Bedroll);
                Tube(new Vector3(0.075f, 1.18f, -0.245f), new Vector3(0.075f, 1.40f, -0.245f), 0.011f, chest, Wood);
                Pouch(new Vector3(0.075f, 1.15f, -0.245f), 0.035f, 0.04f, 0.004f, chest, Steel);
                break;
            case PackKind.Tornister: // a hide box with the tent-cloth wrapped round it and the mess tin on the back
                Pouch(new Vector3(0, 1.31f, -0.168f), 0.128f, 0.135f, 0.058f, chest, Canvas * 0.7f);
                Pouch(new Vector3(0, 1.435f, -0.172f), 0.130f, 0.016f, 0.062f, chest, Canvas * 0.6f);
                Tube(new Vector3(-0.145f, 1.49f, -0.17f), new Vector3(0.145f, 1.49f, -0.17f), 0.040f, chest, Bedroll);
                foreach (int side in new[] { 1, -1 })
                    Tube(new Vector3(0.145f * side, 1.49f, -0.17f), new Vector3(0.145f * side, 1.20f, -0.17f), 0.030f, chest, Bedroll);
                Tube(new Vector3(-0.06f, 1.33f, -0.265f), new Vector3(0.06f, 1.33f, -0.265f), 0.048f, chest, Steel * 1.3f);
                break;
            case PackKind.Horseshoe: // a tall pack in a horseshoe of blanket, a cooking pot on the back
                Pouch(new Vector3(0, 1.30f, -0.19f), 0.135f, 0.150f, 0.075f, chest, Canvas);
                Tube(new Vector3(-0.175f, 1.50f, -0.19f), new Vector3(0.175f, 1.50f, -0.19f), 0.050f, chest, Bedroll);
                foreach (int side in new[] { 1, -1 })
                    Tube(new Vector3(0.175f * side, 1.50f, -0.19f), new Vector3(0.175f * side, 1.12f, -0.19f), 0.040f, chest, Bedroll);
                Tube(new Vector3(-0.06f, 1.24f, -0.285f), new Vector3(0.06f, 1.24f, -0.285f), 0.052f, chest, Steel * 1.3f);
                break;
            case PackKind.Sack: // a soft sack with the greatcoat rolled over the shoulder and across the chest
            {
                HumanMale.Ring Sk(float y, float w, float zf, float zb) => new(new Vector3(0, y, -0.186f), w, zf, zb, 2.2f, chest);
                m.Loft(X, Z, 14, new[] { Sk(1.12f, 0.060f, 0.030f, 0.040f), Sk(1.20f, 0.125f, 0.060f, 0.090f), Sk(1.29f, 0.150f, 0.068f, 0.100f), Sk(1.38f, 0.140f, 0.066f, 0.092f), Sk(1.45f, 0.100f, 0.050f, 0.068f), Sk(1.49f, 0.050f, 0.030f, 0.040f) },
                    new Vector3(0, 1.115f, -0.186f), new Vector3(0, 1.495f, -0.186f));
                var s0 = new Vector3(0.115f, 1.50f, -0.09f); var s1 = new Vector3(0.125f, 1.52f, 0.03f); var s2 = new Vector3(0.03f, 1.38f, 0.172f);
                var s3 = new Vector3(-0.10f, 1.22f, 0.148f); var s4 = new Vector3(-0.172f, 1.14f, 0.02f);
                Tube(s0, s1, 0.034f, chest, Bedroll, 0.95f); Tube(s1, s2, 0.034f, chest, Bedroll, 0.95f);
                Tube(s2, s3, 0.034f, chest, Bedroll, 0.95f); Tube(s3, s4, 0.034f, chest, Bedroll, 0.95f);
                break;
            }
            case PackKind.Diagonal: // a bedroll slung across the back and a woven hat tied on
                Tube(new Vector3(0.16f, 1.50f, -0.17f), new Vector3(-0.17f, 1.12f, -0.20f), 0.048f, chest, Bedroll);
                Tube(new Vector3(0, 1.30f, -0.232f), new Vector3(0, 1.30f, -0.219f), 0.13f, chest, Canvas * 1.25f, 0.92f);
                break;
            case PackKind.Knapsack: // a small hide knapsack with a thin blanket roll round it
                Pouch(new Vector3(0, 1.30f, -0.168f), 0.105f, 0.120f, 0.052f, chest, Canvas * 0.85f);
                Tube(new Vector3(-0.125f, 1.455f, -0.17f), new Vector3(0.125f, 1.455f, -0.17f), 0.035f, chest, Bedroll);
                foreach (int side in new[] { 1, -1 })
                    Tube(new Vector3(0.125f * side, 1.455f, -0.17f), new Vector3(0.125f * side, 1.17f, -0.17f), 0.027f, chest, Bedroll);
                Tube(new Vector3(-0.05f, 1.22f, -0.232f), new Vector3(0.05f, 1.22f, -0.232f), 0.036f, chest, Steel * 1.2f);
                break;
            default: // the big pack with a rolled blanket on top and side pockets
                m.Box(new Vector3(0, 1.285f, -0.197f), new Vector3(0.145f, 0, 0), new Vector3(0, 0.165f, 0), new Vector3(0, 0, 0.085f), chest);
                m.Base = Canvas * 0.9f;
                m.Box(new Vector3(0, 1.40f, -0.205f), new Vector3(0.152f, 0, 0), new Vector3(0, 0.048f, 0), new Vector3(0, 0, 0.092f), chest);
                m.Base = Bedroll;
                HumanMale.Ring Rb(float x, float shade = 1f) => new(new Vector3(x, 1.505f, -0.19f), 0.05f, 0.052f, 0.052f, 2.0f, chest, shade);
                m.Loft(Z, Y, 12, new[] { Rb(-0.17f, 0.8f), Rb(-0.15f), Rb(0f), Rb(0.15f), Rb(0.17f, 0.8f) }, new Vector3(-0.19f, 1.505f, -0.19f), new Vector3(0.19f, 1.505f, -0.19f));
                m.Base = Canvas * 0.8f;
                foreach (int side in new[] { 1, -1 }) // side pockets
                    m.Box(new Vector3(0.158f * side, 1.2f, -0.19f), new Vector3(0.016f, 0, 0), new Vector3(0, 0.07f, 0), new Vector3(0, 0, 0.055f), chest);
                break;
        }
        m.Base = Webbing;
        foreach (int side in new[] { 1, -1 })
        {
            m.Box(new Vector3(0.09f * side, 1.492f, -0.01f), new Vector3(0.022f, 0, 0), new Vector3(0, 0.006f, 0), new Vector3(0, 0, 0.115f), chest);
            m.Box(new Vector3(0.095f * side, 1.345f, 0.138f), new Vector3(0.022f, 0, 0), new Vector3(0, 0.135f, -0.035f), new Vector3(0, 0.0013f, 0.0055f), chest);
        }
        Piece(Pack, HumanMale.Finish(m));

        // ---- rifle: a bolt-action rifle with a full stock; slung muzzle up across the back, butt at the hip ----
        // Built along the axis from the butt (t, metres); "down" is the side of the trigger guard, "side" is the bolt handle's side.
        m = new HumanMale.MeshBuilder(rig);
        var rifleJ = HumanMale.W("rifle");
        var butt = HumanMale.RifleButt;
        var axis = HumanMale.RifleAxis;
        var ru = Vector3.Normalize(Vector3.Cross(axis, X)); var v = Vector3.Cross(axis, ru);
        Vector3 Pt(float t, float side = 0, float down = 0) => butt + axis * t + ru * side + v * down;
        // a lofted part: rings of (t, half width, extent down, extent up, centre shift down)
        void Shell(Vector3 colour, int n, float exp, params (float T, float W, float Dn, float Up, float Shift)[] rings)
        {
            m.Base = colour;
            var rs = rings.Select(r => new HumanMale.Ring(Pt(r.T, 0, r.Shift), r.W, r.Dn, r.Up, exp, rifleJ)).ToArray();
            var first = rings[0]; var last = rings[^1];
            m.Loft(ru, v, n, rs, Pt(first.T - 0.002f, 0, first.Shift), Pt(last.T + 0.002f, 0, last.Shift));
        }
        void Block(Vector3 colour, float t, float side, float down, float hw, float hh, float hl)
        {
            m.Base = colour;
            m.Box(Pt(t, side, down), ru * hw, v * hh, axis * hl, rifleJ);
        }
        var darkWood = Wood * 0.8f; var blued = Steel * 0.75f; var brass = new Vector3(0.55f, 0.43f, 0.18f);

        // stock: tall butt with a comb, a slim wrist, the fore-end running out under the barrel
        Shell(Wood, 12, 2.4f,
            (0.000f, 0.017f, 0.072f, 0.046f, 0.012f),
            (0.040f, 0.019f, 0.066f, 0.040f, 0.010f),
            (0.120f, 0.016f, 0.052f, 0.030f, 0.012f),
            (0.200f, 0.0145f, 0.043f, 0.024f, 0.008f),
            (0.270f, 0.0145f, 0.040f, 0.021f, 0.010f),
            (0.330f, 0.0155f, 0.046f, 0.024f, 0.010f),
            (0.400f, 0.0170f, 0.040f, 0.026f, 0.006f),
            (0.470f, 0.0175f, 0.032f, 0.026f, 0.003f),
            (0.600f, 0.0185f, 0.027f, 0.021f, 0.000f),
            (0.760f, 0.0180f, 0.025f, 0.019f, 0.000f),
            (0.880f, 0.0155f, 0.021f, 0.017f, 0.000f));
        // upper handguard over the barrel
        Shell(darkWood, 10, 2.4f,
            (0.560f, 0.0150f, 0.004f, 0.026f, 0.000f),
            (0.600f, 0.0160f, 0.004f, 0.030f, 0.000f),
            (0.780f, 0.0155f, 0.004f, 0.028f, 0.000f),
            (0.830f, 0.0140f, 0.004f, 0.024f, 0.000f));
        // butt plate and the heel
        Block(blued, -0.003f, 0, 0.012f, 0.0165f, 0.073f * 0.98f, 0.004f);
        Block(blued, 0.004f, 0, 0.0025f, 0.0090f, 0.011f, 0.003f); // screw boss
        // receiver, with its ejection port, tang and the rear sight block
        Shell(Steel, 10, 2.2f, (0.350f, 0.0125f, 0.004f, 0.020f, -0.012f), (0.375f, 0.0160f, 0.006f, 0.024f, -0.014f), (0.540f, 0.0160f, 0.006f, 0.024f, -0.014f), (0.575f, 0.0130f, 0.004f, 0.020f, -0.012f));
        Block(blued, 0.455f, 0.0075f, -0.030f, 0.0085f, 0.003f, 0.050f); // the port
        Block(Steel, 0.340f, 0, -0.018f, 0.005f, 0.004f, 0.020f); // tang
        // bolt body, and its handle on the side: a stem angled down and a round knob
        Shell(blued, 8, 2.0f, (0.372f, 0.0075f, 0.0075f, 0.0075f, -0.018f), (0.480f, 0.0085f, 0.0085f, 0.0085f, -0.018f), (0.495f, 0.0075f, 0.0075f, 0.0075f, -0.018f));
        m.Base = Steel;
        m.Box(Pt(0.405f, 0.020f, -0.012f), ru * 0.0155f, v * 0.003f, axis * 0.0045f, rifleJ);
        m.Box(Pt(0.405f, 0.042f, -0.004f), ru * 0.0072f, v * 0.0072f, axis * 0.0072f, rifleJ); // the knob
        // barrel with taper, bands, sights and bayonet lug
        Shell(blued, 10, 2.0f,
            (0.570f, 0.0135f, 0.0135f, 0.0135f, -0.010f),
            (0.700f, 0.0120f, 0.0120f, 0.0120f, -0.010f),
            (0.900f, 0.0108f, 0.0108f, 0.0108f, -0.010f),
            (1.060f, 0.0098f, 0.0098f, 0.0098f, -0.010f),
            (1.098f, 0.0105f, 0.0105f, 0.0105f, -0.010f),
            (1.105f, 0.0090f, 0.0090f, 0.0090f, -0.010f));
        foreach (float tb in new[] { 0.64f, 0.86f }) // barrel bands
            Shell(Steel, 10, 2.2f, (tb - 0.012f, 0.0190f, 0.0290f, 0.0140f, 0.000f), (tb + 0.012f, 0.0190f, 0.0290f, 0.0140f, 0.000f));
        Block(Steel, 0.865f, 0, 0.040f, 0.004f, 0.006f, 0.008f); // sling loop on the lower band
        Block(Steel, 1.060f, 0, -0.026f, 0.0035f, 0.007f, 0.008f); // front sight post
        Shell(Steel, 8, 2.0f, (1.045f, 0.0115f, 0.0115f, 0.0115f, -0.018f), (1.075f, 0.0115f, 0.0115f, 0.0115f, -0.018f)); // its hood
        Block(Steel, 1.020f, 0, 0.012f, 0.004f, 0.006f, 0.014f); // bayonet lug
        Block(Steel, 0.585f, 0, -0.034f, 0.0075f, 0.007f, 0.020f); // rear sight base
        m.Base = brass;
        m.Box(Pt(0.605f, 0, -0.045f), ru * 0.0045f, v * 0.0045f, axis * 0.012f, rifleJ); // sight leaf notch
        // trigger guard (bow of steel under the action), trigger and magazine floor plate
        Block(blued, 0.405f, 0, 0.061f, 0.0072f, 0.0025f, 0.050f);
        Block(blued, 0.357f, 0, 0.049f, 0.0072f, 0.012f, 0.0035f);
        Block(blued, 0.453f, 0, 0.049f, 0.0072f, 0.012f, 0.0035f);
        Block(Steel, 0.395f, 0, 0.040f, 0.0030f, 0.012f, 0.0035f); // trigger
        Block(blued, 0.510f, 0, 0.0555f, 0.0105f, 0.0040f, 0.040f); // magazine floor plate
        // cleaning rod cap, sling swivels, butt trap
        Block(Steel, 0.885f, 0, -0.001f, 0.0050f, 0.0050f, 0.006f);
        Block(Steel, 0.230f, 0, 0.056f, 0.0035f, 0.007f, 0.007f);
        Piece(Rifle, HumanMale.Finish(m));

        // ---- second rifle: a self-loading rifle on the same axis and length (so every clip fits), full wooden furniture, a gas
        // cylinder over the barrel, a charging handle where the bolt knob is, a box magazine and a flash hider ----
        m = new HumanMale.MeshBuilder(rig);
        Shell(Wood, 12, 2.4f,
            (0.000f, 0.017f, 0.076f, 0.048f, 0.014f),
            (0.050f, 0.020f, 0.068f, 0.042f, 0.012f),
            (0.130f, 0.016f, 0.054f, 0.032f, 0.012f),
            (0.215f, 0.0150f, 0.044f, 0.025f, 0.008f),
            (0.290f, 0.0150f, 0.040f, 0.022f, 0.010f),
            (0.360f, 0.0165f, 0.044f, 0.025f, 0.008f),
            (0.470f, 0.0180f, 0.034f, 0.026f, 0.003f),
            (0.700f, 0.0190f, 0.030f, 0.022f, 0.000f),
            (0.920f, 0.0170f, 0.026f, 0.020f, 0.000f));
        Shell(darkWood, 10, 2.4f, // upper handguard, longer than the basic rifle's, under the gas cylinder
            (0.520f, 0.0155f, 0.004f, 0.027f, 0.000f), (0.580f, 0.0165f, 0.004f, 0.031f, 0.000f),
            (0.880f, 0.0160f, 0.004f, 0.029f, 0.000f), (0.930f, 0.0140f, 0.004f, 0.025f, 0.000f));
        Block(blued, -0.003f, 0, 0.012f, 0.0165f, 0.075f, 0.004f); // butt plate
        Shell(Steel, 10, 2.2f, (0.345f, 0.0125f, 0.004f, 0.020f, -0.012f), (0.370f, 0.0165f, 0.006f, 0.025f, -0.014f), (0.560f, 0.0165f, 0.006f, 0.025f, -0.014f), (0.585f, 0.0130f, 0.004f, 0.020f, -0.012f)); // receiver
        Block(Steel, 0.455f, 0.0075f, -0.030f, 0.0085f, 0.003f, 0.050f);                                                // ejection port
        m.Base = Steel;
        m.Box(Pt(0.405f, 0.019f, -0.014f), ru * 0.0120f, v * 0.003f, axis * 0.0050f, rifleJ);                          // charging handle stem
        m.Box(Pt(0.405f, 0.034f, -0.012f), ru * 0.0050f, v * 0.0090f, axis * 0.0090f, rifleJ);                         // and its paddle
        Shell(blued, 10, 2.0f,
            (0.580f, 0.0130f, 0.0130f, 0.0130f, -0.010f), (0.800f, 0.0115f, 0.0115f, 0.0115f, -0.010f),
            (1.040f, 0.0100f, 0.0100f, 0.0100f, -0.010f), (1.105f, 0.0090f, 0.0090f, 0.0090f, -0.010f));                 // barrel
        Shell(blued, 10, 2.0f, (0.690f, 0.0105f, 0.0105f, 0.0105f, -0.043f), (0.710f, 0.0125f, 0.0125f, 0.0125f, -0.043f),
            (0.900f, 0.0125f, 0.0125f, 0.0125f, -0.043f), (0.915f, 0.0100f, 0.0100f, 0.0100f, -0.043f));                 // gas cylinder over the barrel
        Shell(Steel, 10, 2.0f, (1.030f, 0.0120f, 0.0120f, 0.0120f, -0.010f), (1.060f, 0.0150f, 0.0150f, 0.0150f, -0.010f),
            (1.105f, 0.0140f, 0.0140f, 0.0140f, -0.010f));                                                              // flash hider
        Block(Steel, 0.960f, 0, 0.040f, 0.004f, 0.006f, 0.008f);                                                         // sling loop
        Block(Steel, 0.985f, 0, -0.030f, 0.0035f, 0.008f, 0.008f);                                                       // front sight post
        Block(Steel, 0.600f, 0, -0.035f, 0.0075f, 0.008f, 0.020f);                                                       // rear sight
        Block(blued, 0.405f, 0, 0.061f, 0.0072f, 0.0025f, 0.050f);                                                       // trigger guard
        Block(blued, 0.357f, 0, 0.049f, 0.0072f, 0.012f, 0.0035f);
        Block(blued, 0.453f, 0, 0.049f, 0.0072f, 0.012f, 0.0035f);
        Block(Steel, 0.395f, 0, 0.040f, 0.0030f, 0.012f, 0.0035f);                                                       // trigger
        Block(blued, 0.520f, 0, 0.078f, 0.0120f, 0.030f, 0.036f);                                                        // the box magazine
        Block(blued, 0.520f, 0, 0.110f, 0.0125f, 0.003f, 0.038f);                                                        // and its floor plate
        Piece(RifleSemi, HumanMale.Finish(m));
        return list;
    }

    private static FigureMesh Headgear(Rig rig, Uniform u, Vector3 leather)
    {
        var m = new HumanMale.MeshBuilder(rig) { Base = u.Headgear };
        var X = Vector3.UnitX; var Z = Vector3.UnitZ;
        var head = HumanMale.W(rig.Find(HumanMale.Headgear) >= 0 ? HumanMale.Headgear : "head"); // the headgear joint, so a clip can take it off
        const float hz = 0.014f;
        HumanMale.Ring R(float y, float w, float zf, float zb, float e = 2.2f, float shade = 1f) => new(new Vector3(0, y, hz), w, zf, zb, e, head, shade);
        void Loft(float y0, float y1, params HumanMale.Ring[] rings) => m.Loft(X, Z, 20, rings, new Vector3(0, y0, hz), new Vector3(0, y1, hz - 0.002f));
        void LoftX(float y0, float y1, Func<Vector3, Vector3> xf, params HumanMale.Ring[] rings) => m.Loft(X, Z, 28, rings, new Vector3(0, y0, hz), new Vector3(0, y1, hz - 0.002f), xf);
        // flat plate hanging at an angle: centre, half width, half thickness, half length (tilted by the second z component)
        void Plate(Vector3 c, float hw, float ht, float hl, float tilt) => m.Box(c, new Vector3(hw, 0, 0), new Vector3(0, ht, 0), new Vector3(0, tilt, hl), head);
        // a visor / brim drawn out of the rim of the shell itself (as on the M35): the front of the lowest rings is pulled forward and down,
        // narrowed to half-width hw, and the rear is pulled back for a short brim; nothing is a separate box so there is no seam
        Func<Vector3, Vector3> Peak(float yTop, float reach, float drop, float hw, float rear = 0, float rearDrop = 0)
        {
            return p =>
            {
                float z = p.Z - hz, tv = Math.Clamp((yTop - p.Y) / 0.016f, 0, 1);
                tv = tv * tv * (3 - 2 * tv);
                float xm = 1 - Smooth(hw * 0.55f, hw, MathF.Abs(p.X));
                float front = Smooth(0.03f, 0.10f, z) * xm, back = Smooth(0.03f, 0.10f, -z);
                p.Z += tv * reach * front - tv * rear * back;
                p.Y -= tv * drop * front + tv * rearDrop * back;
                return p;
            };
        }
        switch (u.Style)
        {
            case HeadgearStyle.Brodie:
                Loft(1.690f, 1.805f, R(1.686f, 0.152f, 0.166f, 0.162f, 2.0f, 0.8f), R(1.698f, 0.154f, 0.168f, 0.164f, 2.0f, 0.9f),
                    R(1.712f, 0.100f, 0.112f, 0.116f), R(1.735f, 0.092f, 0.101f, 0.106f), R(1.765f, 0.078f, 0.086f, 0.092f),
                    R(1.788f, 0.055f, 0.060f, 0.066f), R(1.803f, 0.030f, 0.033f, 0.037f));
                break;
            case HeadgearStyle.Stahlhelm: // the M35: a tall dome with near-upright sides, a flared skirt, a visor drawn out of the front of the same shell, a deep notch over each ear and a long neck guard
                Vector3 Skirt(Vector3 p)
                {
                    float t = Math.Clamp((1.750f - p.Y) / 0.075f, 0, 1), z = p.Z - hz;
                    float tb = Math.Clamp((1.712f - p.Y) / 0.040f, 0, 1), tv = Math.Clamp((1.702f - p.Y) / 0.012f, 0, 1);
                    float back = Smooth(0.01f, 0.13f, -z), front = Smooth(0.06f, 0.13f, z); // a narrow bill
                    float ear = MathF.Exp(-((z + 0.005f) * (z + 0.005f)) / 0.0032f) * Math.Clamp((MathF.Abs(p.X) - 0.05f) / 0.05f, 0, 1);
                    float bill = Math.Clamp((p.Y - 1.676f) / 0.016f, 0, 1) * (1 - Math.Clamp((p.Y - 1.692f) / 0.024f, 0, 1)); // drops the ring above the rim so the end of the bill lies flat
                    p.Y += t * 0.046f * ear - tb * 0.052f * back - bill * 0.012f * front;
                    p.Z += -tb * 0.030f * back + tv * 0.026f * front; // the visor is the front of the shell itself: short, sloping sharply off the dome and flat at its end
                    p.X *= 1 + 0.05f * tb * back;
                    return p;
                }
                m.Loft(X, Z, 28, new[]
                {
                    R(1.676f, 0.131f, 0.144f, 0.150f, 2.1f, 0.75f), R(1.692f, 0.128f, 0.141f, 0.137f, 2.1f, 0.85f),
                    R(1.716f, 0.123f, 0.133f, 0.130f), R(1.744f, 0.112f, 0.123f, 0.127f), R(1.776f, 0.106f, 0.117f, 0.126f, 2.4f),
                    R(1.808f, 0.092f, 0.103f, 0.116f, 2.4f), R(1.832f, 0.064f, 0.074f, 0.088f), R(1.848f, 0.032f, 0.038f, 0.048f),
                    R(1.856f, 0.010f, 0.012f, 0.016f),
                }, new Vector3(0, 1.676f, hz), new Vector3(0, 1.856f, hz - 0.002f), Skirt);
                m.Base = Steel;
                foreach (int side in new[] { 1, -1 })
                    m.Box(new Vector3(0.113f * side, 1.745f, hz + 0.050f), new Vector3(0.007f, 0, 0), new Vector3(0, 0.008f, 0), new Vector3(0, 0, 0.008f), head); // vent rivet
                m.Base = u.Headgear;
                break;
            case HeadgearStyle.Adrian:
                LoftX(1.690f, 1.822f, Peak(1.716f, 0.052f, 0.012f, 0.080f, 0.030f, 0.004f), R(1.688f, 0.126f, 0.142f, 0.140f, 2.0f, 0.8f), R(1.698f, 0.128f, 0.144f, 0.142f, 2.0f, 0.9f),
                    R(1.714f, 0.099f, 0.112f, 0.120f), R(1.740f, 0.092f, 0.102f, 0.114f), R(1.770f, 0.083f, 0.092f, 0.106f),
                    R(1.800f, 0.066f, 0.072f, 0.088f), R(1.820f, 0.040f, 0.044f, 0.056f));
                Plate(new Vector3(0, 1.808f, hz - 0.002f), 0.007f, 0.013f, 0.088f, 0f);          // crest along the top
                break;
            case HeadgearStyle.PeakedCap:
                LoftX(1.690f, 1.818f, Peak(1.722f, 0.078f, 0.016f, 0.066f), R(1.688f, 0.099f, 0.109f, 0.113f, 2.0f, 0.75f), R(1.704f, 0.100f, 0.110f, 0.114f, 2.0f, 0.8f),
                    R(1.735f, 0.106f, 0.118f, 0.122f), R(1.775f, 0.110f, 0.122f, 0.126f), R(1.800f, 0.104f, 0.116f, 0.120f),
                    R(1.814f, 0.090f, 0.100f, 0.104f), R(1.818f, 0.050f, 0.055f, 0.058f));
                break;
            case HeadgearStyle.SoftCap:
                LoftX(1.690f, 1.816f, Peak(1.722f, 0.066f, 0.014f, 0.058f), R(1.688f, 0.098f, 0.108f, 0.112f, 2.0f, 0.75f), R(1.706f, 0.099f, 0.109f, 0.113f, 2.0f, 0.85f),
                    R(1.735f, 0.104f, 0.116f, 0.120f), R(1.772f, 0.104f, 0.116f, 0.120f), R(1.797f, 0.092f, 0.102f, 0.106f),
                    R(1.811f, 0.062f, 0.068f, 0.072f), R(1.816f, 0.032f, 0.036f, 0.038f));
                break;
            case HeadgearStyle.FlapCap: // the neck curtain and ear flaps are the lower rim of the shell itself, hung down and drawn in
            {
                var peak = Peak(1.722f, 0.062f, 0.013f, 0.054f);
                Vector3 Flaps(Vector3 p)
                {
                    float z = p.Z - hz, t = Math.Clamp((1.716f - p.Y) / 0.030f, 0, 1);
                    float back = Smooth(0.02f, 0.10f, -z);
                    float ear = MathF.Exp(-((z + 0.005f) * (z + 0.005f)) / 0.0035f) * Math.Clamp((MathF.Abs(p.X) - 0.06f) / 0.04f, 0, 1);
                    p = peak(p);
                    p.Y -= t * 0.078f * back + t * 0.052f * ear;
                    p.Z = hz + (p.Z - hz) * (1 - 0.30f * t * back);
                    p.X *= 1 - 0.20f * t * back + 0.04f * t * ear;
                    return p;
                }
                LoftX(1.690f, 1.818f, Flaps, R(1.688f, 0.099f, 0.109f, 0.113f, 2.0f, 0.75f), R(1.704f, 0.100f, 0.110f, 0.114f, 2.0f, 0.8f),
                    R(1.735f, 0.105f, 0.117f, 0.121f), R(1.775f, 0.106f, 0.118f, 0.122f), R(1.800f, 0.098f, 0.108f, 0.112f),
                    R(1.814f, 0.080f, 0.088f, 0.092f), R(1.818f, 0.046f, 0.050f, 0.054f));
                break;
            }
            default: // the pot
                Loft(1.690f, 1.830f, R(1.688f, 0.124f, 0.140f, 0.136f, 2.0f, 0.8f), R(1.698f, 0.126f, 0.142f, 0.138f, 2.0f, 0.9f),
                    R(1.712f, 0.097f, 0.110f, 0.118f), R(1.735f, 0.090f, 0.100f, 0.112f), R(1.765f, 0.082f, 0.090f, 0.104f),
                    R(1.795f, 0.068f, 0.073f, 0.087f), R(1.818f, 0.044f, 0.048f, 0.060f));
                break;
        }
        m.Base = leather; // chin strap, hanging from the brim past the ear
        foreach (int side in new[] { 1, -1 })
            m.Box(new Vector3(0.081f * side, 1.64f, 0.045f), new Vector3(0.0035f, 0, 0), new Vector3(0, 0.055f, 0), new Vector3(0, 0, 0.011f), head);
        return HumanMale.Finish(m);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A garment cut from the body: the triangles of the named parts for which <paramref name="thickness"/> returns 0 or more at
    /// all three corners, each corner pushed out that far along its normal. Colour is <paramref name="colour"/> with the body's shading.
    /// </summary>
    private static FigureMesh Cut(FigureMesh body, string[] parts, Vector3 colour, Func<string, Vector3, float> thickness,
        Func<string, Vector3, float>? shade = null, bool clampToGround = false)
    {
        int n = body.VertexCount;
        var partOf = new string?[n];
        foreach (string name in parts)
            if (body.Parts != null && body.Parts.TryGetValue(name, out var r))
                for (int i = r.Start; i < r.End; i++) partOf[i] = name;
        var map = new int[n];
        Array.Fill(map, -1);
        var verts = new List<int>();
        var idx = new List<int>();
        var t = new float[n];
        for (int i = 0; i < n; i++) t[i] = partOf[i] == null ? -1 : thickness(partOf[i]!, body.Positions[i]);
        for (int i = 0; i < body.Indices.Length; i += 3)
        {
            int a = body.Indices[i], b = body.Indices[i + 1], c = body.Indices[i + 2];
            if (partOf[a] == null || partOf[a] != partOf[b] || partOf[a] != partOf[c] || t[a] < 0 || t[b] < 0 || t[c] < 0) continue;
            foreach (int k in new[] { a, b, c })
            {
                if (map[k] < 0) { map[k] = verts.Count; verts.Add(k); }
                idx.Add(map[k]);
            }
        }
        var mesh = new FigureMesh
        {
            Positions = new Vector3[verts.Count], Normals = new Vector3[verts.Count], Colors = new byte[verts.Count * 4],
            Joints = new byte[verts.Count * 4], Weights = new float[verts.Count * 4], Indices = idx.ToArray(),
        };
        for (int i = 0; i < verts.Count; i++)
        {
            int k = verts[i];
            var p = body.Positions[k] + body.Normals[k] * t[k];
            if (clampToGround) p.Y = Math.Max(p.Y, 0);
            mesh.Positions[i] = p;
            mesh.Normals[i] = body.Normals[k];
            // the body's own shading (shaded creases) carries over as a brightness
            float s = shade?.Invoke(partOf[k]!, body.Positions[k]) ?? 1f;
            mesh.Colors[i * 4] = (byte)Math.Clamp(colour.X * s * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 1] = (byte)Math.Clamp(colour.Y * s * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 2] = (byte)Math.Clamp(colour.Z * s * 255f + 0.5f, 0, 255);
            mesh.Colors[i * 4 + 3] = 255;
            for (int q = 0; q < 4; q++) { mesh.Joints[i * 4 + q] = body.Joints[k * 4 + q]; mesh.Weights[i * 4 + q] = body.Weights[k * 4 + q]; }
        }
        return mesh;
    }

    /// <summary>The body with pieces on it as one model, sharing the body's clips. Pieces on another skeleton are left out.</summary>
    public static FigureModel Compose(string key, FigureModel body, IEnumerable<FigureModel> pieces)
    {
        var meshes = new List<FigureMesh> { body.Mesh };
        var parts = new List<string>();
        foreach (var piece in pieces)
            if (SameRig(body.Rig, piece.Rig)) { meshes.Add(piece.Mesh); parts.Add(piece.Key); }
        var model = new FigureModel(key, body.Rig, FigureMesh.Merge(meshes));
        model.Parts.AddRange(parts);
        foreach (var clip in body.Clips.Values) model.Clips[clip.Name] = clip;
        return model;
    }

    private static bool SameRig(Rig a, Rig b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a.Joints[i].Name, b.Joints[i].Name, StringComparison.OrdinalIgnoreCase) || a.Joints[i].Parent != b.Joints[i].Parent) return false;
        return true;
    }
}
