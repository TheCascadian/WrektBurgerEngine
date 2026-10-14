using static Burger.Core.FlagKit;

namespace Burger.Core;

/// <summary>
/// Procedural flags for countries and parties that have no image file. The same identity always gives the same flag.
/// <list type="number">
/// <item>A design grammar builds several candidates, each from one design family (tricolour, bicolour, cross, saltire, canton, chevron, geometric, heraldic, emblematic).</item>
/// <item>Each candidate is scored on a small raster against that family's limits (see <see cref="FlagEvaluator"/>) and the best survives.</item>
/// <item>The winner is rasterised with supersampling. Predecessors pass their palette, family and motifs on as hints.</item>
/// </list>
/// Pure managed code that returns RGBA bytes, so it can be tested without a window.
/// </summary>
public static class FlagGenerator
{
    public const int Width = 96, Height = 64;
    private const int Samples = 3;       // Samples x Samples supersampling per pixel
    private const int Candidates = 8;    // designs scored per flag (the last one is a plain fallback)
    private const int MaxDepth = 3;      // predecessor chain followed at most this far
    private const int MaxSiblings = 6;

    /// <summary>Renders a Width x Height RGBA image.</summary>
    public static byte[] Render(string key, Rgb color, FlagKind kind = FlagKind.Nation) => Render(new FlagIdentity(key, color, kind));

    /// <summary>Renders a Width x Height RGBA image for an identity with tradition, political family, period and relations.</summary>
    public static byte[] Render(FlagIdentity id) => Rasterize(Generate(id, 0).Flag);

    /// <summary>Generates the design only (no final raster): chosen family, colours, decisions and score. Used by tests and tools.</summary>
    public static FlagDesign Design(FlagIdentity id) { var r = Generate(id, 0); return new FlagDesign(r.Family, r.Palette, r.Choices, r.Eval.Score, r.Eval.Rejected, r.Eval.ChargeArea, r.Eval.Colors, r.Eval.PrimaryShare); }

    internal sealed record Result(Flag Flag, string Family, Rgb[] Palette, IReadOnlyDictionary<string, string> Choices, FlagEval Eval);

    private static Result Generate(FlagIdentity id, int depth)
    {
        ulong seed = Hash(id.Kind + ":" + id.Key);
        var trad = FlagTraditions.Tradition(id.Tradition); var fam = FlagTraditions.Family(id.Family);

        Result? parent = id.Predecessor != null && depth < MaxDepth ? Generate(id.Predecessor with { Distinct = null }, depth + 1) : null;
        List<Rgb[]>? sibThumbs = null;
        if (id.Distinct != null && depth == 0)
        {
            sibThumbs = new();
            foreach (var s in id.Distinct.Take(MaxSiblings)) sibThumbs.Add(Generate(s with { Distinct = null }, depth + 1).Eval.Thumb);
        }

        Result? best = null;
        for (int i = 0; i < Candidates; i++)
        {
            var rng = new Rng(Mix(seed, (ulong)i));
            bool safe = i == Candidates - 1;
            string family = safe ? (id.Kind == FlagKind.Nation ? "tricolour" : "bicolour") : PickFamily(rng, id, trad, fam, parent);
            var palette = new FlagPalette(rng, id, trad, fam, parent?.Palette);
            var plan = new FlagPlan(rng, id, palette, parent != null && family == parent.Family ? parent.Choices : null) { Family = family, NoCharge = safe };
            FlagFamilies.Build(plan);
            var eval = FlagEvaluator.Evaluate(plan, FlagFamilies.Cfg(family), sibThumbs);
            if (best == null || eval.Score > best.Eval.Score)
                best = new Result(plan.F, family, plan.P.Colors.ToArray(), plan.Choices, eval);
        }
        return best!;
    }

    private static string PickFamily(Rng rng, FlagIdentity id, FlagTradition? trad, FlagPoliticalFamily? fam, Result? parent)
    {
        if (parent != null && rng.P(0.75)) return parent.Family;
        var w = new double[FlagFamilies.All.Length];
        for (int i = 0; i < w.Length; i++)
        {
            var c = FlagFamilies.All[i];
            double x = c.Weight(id.Kind) * FlagFamilies.EraFactor(c.Name, id.Year);
            if (trad != null) x *= trad.FamilyWeights.TryGetValue(c.Name, out var tw) ? tw : 1;
            if (fam != null) x *= fam.FamilyWeights.TryGetValue(c.Name, out var fw) ? fw : 1;
            w[i] = x;
        }
        return FlagFamilies.All[rng.Weighted(w)].Name;
    }

    private static byte[] Rasterize(Flag f)
    {
        var px = new byte[Width * Height * 4];
        int n = Samples * Samples;
        for (int py = 0; py < Height; py++)
            for (int pxl = 0; pxl < Width; pxl++)
            {
                int sr = 0, sg = 0, sb = 0;
                for (int j = 0; j < Samples; j++)
                    for (int i = 0; i < Samples; i++)
                    {
                        var c = f.At((pxl + (i + 0.5) / Samples) / Width * A, (py + (j + 0.5) / Samples) / Height);
                        sr += c.R; sg += c.G; sb += c.B;
                    }
                int o = (py * Width + pxl) * 4;
                px[o] = (byte)(sr / n); px[o + 1] = (byte)(sg / n); px[o + 2] = (byte)(sb / n); px[o + 3] = 255;
            }
        return px;
    }
}

/// <summary>What the generator decided for an identity (for tests, tools and tuning).</summary>
public sealed record FlagDesign(string Family, IReadOnlyList<Rgb> Palette, IReadOnlyDictionary<string, string> Choices, double Score, bool Rejected, double ChargeArea, int Colors, double PrimaryShare);
