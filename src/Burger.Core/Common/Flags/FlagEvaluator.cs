using static Burger.Core.FlagKit;

namespace Burger.Core;

/// <summary>The result of scoring one candidate on a small raster. Higher is better; a rejected candidate scores far below any acceptable one.</summary>
internal sealed class FlagEval
{
    public double Score;
    public bool Rejected;
    public string? Reason;
    public double PrimaryShare, EdgeDensity, LowContrastEdges, ChargeArea, ChargeIllegible, DetailLoss;
    public int Colors;
    public Rgb[] Thumb = Array.Empty<Rgb>();   // ThumbW x ThumbH block means, used to compare designs
}

/// <summary>Deterministic, constraint-based scoring of flag candidates (composition, contrast, simplicity, legibility, identity fidelity, duplication).</summary>
internal static class FlagEvaluator
{
    public const int EW = 48, EH = 32, ThumbW = 12, ThumbH = 8;

    public static FlagEval Evaluate(FlagPlan plan, FamilyCfg cfg, IReadOnlyList<Rgb[]>? siblingThumbs)
    {
        var f = plan.F; var baseCol = plan.Id.Color;
        var px = new Rgb[EW * EH]; var charge = new bool[EW * EH];
        for (int y = 0; y < EH; y++)
            for (int x = 0; x < EW; x++)
            {
                int sr = 0, sg = 0, sb = 0, cm = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        double fx = (x + (i + 0.5) / 2) / EW * A, fy = (y + (j + 0.5) / 2) / EH;
                        var c = f.At(fx, fy); sr += c.R; sg += c.G; sb += c.B;
                        if (f.ChargeAt(fx, fy)) cm++;
                    }
                px[y * EW + x] = new Rgb(sr / 4, sg / 4, sb / 4);
                charge[y * EW + x] = cm >= 2;
            }

        var e = new FlagEval();
        int n = EW * EH;

        // palette: merge near colours, count those that matter
        var pal = new List<(Rgb c, int n)>();
        int near = 0;
        foreach (var c in px)
        {
            if (Dist(c, baseCol) < 45) near++;
            int k = pal.FindIndex(p => Dist(p.c, c) < 40);
            if (k < 0) pal.Add((c, 1)); else pal[k] = (pal[k].c, pal[k].n + 1);
        }
        e.Colors = pal.Count(p => p.n >= n * 0.02);
        e.PrimaryShare = near / (double)n;

        // edges and how well they read
        int edges = 0, weak = 0;
        for (int y = 0; y < EH; y++)
            for (int x = 0; x < EW; x++)
            {
                var c = px[y * EW + x];
                for (int d = 0; d < 2; d++)
                {
                    int nx = x + (d == 0 ? 1 : 0), ny = y + (d == 1 ? 1 : 0);
                    if (nx >= EW || ny >= EH) continue;
                    var o = px[ny * EW + nx];
                    if (Dist(c, o) <= 50) continue;
                    edges++;
                    if (Contrast(c, o) < 1.25) weak++;
                }
            }
        e.EdgeDensity = edges / (double)(2 * n);
        e.LowContrastEdges = edges == 0 ? 0 : weak / (double)edges;

        // charges: area, edge contact, contrast against what is beneath
        int cn = 0, cBorder = 0, cBad = 0;
        for (int y = 0; y < EH; y++)
            for (int x = 0; x < EW; x++)
            {
                if (!charge[y * EW + x]) continue;
                cn++;
                if (x == 0 || y == 0 || x == EW - 1 || y == EH - 1) cBorder++;
                if (cn % 3 == 0 && Contrast(px[y * EW + x], f.At((x + 0.5) / EW * A, (y + 0.5) / EH, true)) < 2.0) cBad++;
            }
        e.ChargeArea = cn / (double)n;
        e.ChargeIllegible = cn == 0 ? 0 : cBad / Math.Max(1.0, cn / 3.0);

        // small-size legibility: how much a 4x smaller copy loses
        double loss = 0;
        for (int y = 0; y < EH; y += 2)
            for (int x = 0; x < EW; x += 2)
            {
                int r = 0, g = 0, b = 0;
                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) { var c = px[(y + j) * EW + x + i]; r += c.R; g += c.G; b += c.B; }
                var avg = new Rgb(r / 4, g / 4, b / 4);
                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) loss += Dist(avg, px[(y + j) * EW + x + i]);
            }
        e.DetailLoss = loss / n / 720.0;

        // balance of the non-primary mass
        double mx = 0, my = 0; int cnt = 0;
        for (int y = 0; y < EH; y++)
            for (int x = 0; x < EW; x++)
                if (Dist(px[y * EW + x], baseCol) >= 45) { mx += x; my += y; cnt++; }
        double offset = cnt == 0 ? 0 : Math.Sqrt(Math.Pow(mx / cnt / EW - 0.5, 2) + Math.Pow(my / cnt / EH - 0.5, 2));

        // thumbnail for duplicate comparison
        e.Thumb = new Rgb[ThumbW * ThumbH];
        int bw = EW / ThumbW, bh = EH / ThumbH;
        for (int ty = 0; ty < ThumbH; ty++)
            for (int tx = 0; tx < ThumbW; tx++)
            {
                int r = 0, g = 0, b = 0;
                for (int j = 0; j < bh; j++) for (int i = 0; i < bw; i++) { var c = px[(ty * bh + j) * EW + tx * bw + i]; r += c.R; g += c.G; b += c.B; }
                int m = bw * bh; e.Thumb[ty * ThumbW + tx] = new Rgb(r / m, g / m, b / m);
            }

        // ---- constraints and score
        double pen = 0;
        void Reject(string why) { if (!e.Rejected) { e.Rejected = true; e.Reason = why; } }

        if (e.Colors > cfg.MaxColors + 1) Reject("too many colours");
        pen += Math.Max(0, e.Colors - cfg.MaxColors) * 2.5;
        if (e.LowContrastEdges > 0.6) Reject("edges do not read");
        pen += Math.Max(0, e.LowContrastEdges - 0.25) * 8;
        if (cfg.NeedsCharge && e.ChargeArea < 0.012) Reject("missing emblem");
        if (e.ChargeArea > 0 && e.ChargeArea < 0.004) Reject("emblem too small to read");
        if (e.ChargeArea > cfg.MaxCharge) pen += (e.ChargeArea - cfg.MaxCharge) * 25;
        if (cBorder > 2) Reject("emblem cut by the flag edge");
        if (e.ChargeIllegible > 0.3) Reject("emblem low contrast");
        pen += e.ChargeIllegible * 6;
        if (e.PrimaryShare < 0.04) Reject("identity colour barely shows");
        pen += e.PrimaryShare < cfg.PriLo ? (cfg.PriLo - e.PrimaryShare) * 14 : e.PrimaryShare > cfg.PriHi ? (e.PrimaryShare - cfg.PriHi) * 10 : 0;
        pen += e.EdgeDensity < cfg.EdgeLo ? (cfg.EdgeLo - e.EdgeDensity) * 20 : e.EdgeDensity > cfg.EdgeHi ? (e.EdgeDensity - cfg.EdgeHi) * 14 : 0;
        pen += Math.Max(0, plan.F.Layers.Count - 9) * 0.3;
        pen += Math.Max(0, plan.Charges - 5) * 0.4;
        pen += e.DetailLoss * 5;
        pen += Math.Max(0, offset - 0.15) * 8;
        if (siblingThumbs != null)
            foreach (var t in siblingThumbs)
            {
                double d = 0; for (int i = 0; i < t.Length; i++) d += Dist(t[i], e.Thumb[i]);
                d /= t.Length * 720.0;
                if (d < 0.08) pen += (0.08 - d) * 60;
            }
        e.Score = 10 - pen - (e.Rejected ? 1000 : 0);
        return e;
    }
}
