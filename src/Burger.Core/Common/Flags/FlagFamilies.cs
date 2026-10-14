using static Burger.Core.FlagKit;

namespace Burger.Core;

/// <summary>Per-family weights and the quality limits the evaluator holds its candidates to.</summary>
internal sealed record FamilyCfg(string Name, double Nation, double Party, int MaxColors, double EdgeLo, double EdgeHi, double PriLo, double PriHi,
    bool NeedsCharge, double MaxCharge)
{
    public double Weight(FlagKind k) => k == FlagKind.Nation ? Nation : Party;
}

/// <summary>
/// The design grammar. Each family builds a coherent flag from a small set of compatible parts:
/// which field divisions it may use, which ordinaries go with them, how big and where an emblem may sit.
/// </summary>
internal static class FlagFamilies
{
    public static readonly FamilyCfg[] All =
    {
        new("tricolour",  3.0, 1.4, 4, 0.04, 0.24, 0.20, 0.70, false, 0.12),
        new("bicolour",   1.8, 2.2, 4, 0.02, 0.20, 0.25, 0.85, false, 0.16),
        new("cross",      1.6, 0.4, 4, 0.06, 0.30, 0.15, 0.80, false, 0.10),
        new("saltire",    0.7, 0.5, 4, 0.08, 0.32, 0.15, 0.80, false, 0.10),
        new("canton",     1.1, 0.3, 4, 0.10, 0.45, 0.12, 0.75, false, 0.12),
        new("chevron",    1.3, 0.8, 4, 0.05, 0.28, 0.20, 0.80, false, 0.12),
        new("geometric",  1.2, 1.3, 4, 0.04, 0.50, 0.18, 0.80, false, 0.10),
        new("heraldic",   1.0, 0.6, 5, 0.08, 0.42, 0.15, 0.75, true, 0.45),
        new("emblematic", 1.4, 3.0, 4, 0.03, 0.34, 0.20, 0.85, true, 0.32),
    };

    public static FamilyCfg Cfg(string name) => Array.Find(All, f => f.Name == name)!;

    /// <summary>Drawing period shifts style: ornate heraldry early, plain geometry and emblems later.</summary>
    public static double EraFactor(string family, int? year)
    {
        if (year == null) return 1;
        int y = year.Value;
        double heraldic = y < 1850 ? 2.0 : y < 1920 ? 1.0 : 0.6;
        double modern = y < 1850 ? 0.5 : y < 1920 ? 1.0 : 1.3;
        return family switch
        {
            "heraldic" => heraldic,
            "geometric" or "emblematic" => modern,
            "tricolour" => y < 1789 ? 0.3 : 1,
            _ => 1,
        };
    }

    public static void Build(FlagPlan p)
    {
        switch (p.Family)
        {
            case "tricolour": Tricolour(p); break;
            case "bicolour": Bicolour(p); break;
            case "cross": Cross(p); break;
            case "saltire": Saltire(p); break;
            case "canton": Canton(p); break;
            case "chevron": Chevron(p); break;
            case "geometric": Geometric(p); break;
            case "heraldic": Heraldic(p); break;
            default: Emblematic(p); break;
        }
    }

    private static string[] SmallEmblems => new[] { "star", "roundel", "lozenge", "stars_trio", "crescent", "cog", "wheel", "flame", "rose" };

    // ---------------------------------------------------------------- families

    private static void Tricolour(FlagPlan p)
    {
        double vb = p.VerticalBias;
        bool vert = p.ChooseW("orient", new[] { "v", "h" }, new[] { vb, 1 - vb }) == "v";
        bool five = p.ChooseW("kind", new[] { "tri", "five" }, new[] { 0.88, 0.12 }) == "five";
        string seq = five
            ? p.Choose("seq5", "pspsp", "spsps", "sptps", "pstsp")
            : p.ChooseW("seq", new[] { "pst", "spt", "stp", "tsp", "sps", "psp" }, new[] { 1.0, 1, 1, 0.6, 1.4, 1.4 });
        double[] w;
        if (five) w = p.Choose("ratio5", "eq", "mid") == "mid" ? new[] { 1.0, 1, 2, 1, 1 } : new[] { 1.0, 1, 1, 1, 1 };
        else w = p.Choose("ratio", "eq", "wide", "wide", "narrow") switch { "wide" => new[] { 1.0, 2, 1 }, "narrow" => new[] { 2.0, 1, 2 }, _ => new[] { 1.0, 1, 1 } };
        var cols = seq.Select(p.Letter).ToArray();
        if (w.Length != cols.Length) w = Enumerable.Repeat(1.0, cols.Length).ToArray();
        p.F.Field = p.Bands(cols, w, vert);
        if (five) { p.Busy = true; return; }
        if (p.R.P(p.Party ? 0.5 : 0.38))
        {
            double mid = w[1] / w.Sum();
            double extent = vert ? mid * A : mid;
            double rad = Math.Min(0.3, extent * 0.42);
            p.Charge("emblem", A / 2, 0.5, rad);
        }
    }

    private static void Bicolour(FlagPlan p)
    {
        double vb = p.VerticalBias;
        bool vert = p.ChooseW("orient", new[] { "v", "h" }, new[] { vb * 0.8, 1 - vb * 0.8 }) == "v";
        string ratio = p.ChooseW("ratio", new[] { "1:1", "2:1", "1:2", "3:2" }, new[] { 3.0, 1, 1, 1 });
        bool wavy = p.ChooseW("seam", new[] { "straight", "wavy" }, new[] { 0.9, 0.1 }) == "wavy";
        double[] w = ratio switch { "2:1" => new[] { 2.0, 1 }, "1:2" => new[] { 1.0, 2 }, "3:2" => new[] { 3.0, 2 }, _ => new[] { 1.0, 1 } };
        bool baseFirst = p.R.P(0.5);
        var cols = baseFirst ? new[] { p.P.Base, p.P.Second } : new[] { p.P.Second, p.P.Base };
        p.F.Field = p.Bands(cols, w, vert, wavy ? 0.03 : 0, 2, p.R.R(0, 6.28));
        double seamAt = w[0] / w.Sum();
        string place = p.ChooseW("emblem", new[] { "center", "hoist", "seam", "none" }, new[] { p.Party ? 3.0 : 1.5, 1.2, 0.7, 1.2 });
        double rad = p.Party ? p.R.R(0.2, 0.28) : p.R.R(0.17, 0.25);
        switch (place)
        {
            case "center": p.Charge("charge", A / 2, 0.5, rad, null, null, 0, 0.7); break;
            case "hoist": p.Charge("charge", vert ? A * seamAt / 2 : A * 0.22, vert ? 0.5 : seamAt / 2, Math.Min(rad, 0.2)); break;
            case "seam": p.Charge("charge", vert ? A * seamAt : A / 2, vert ? 0.5 : seamAt, rad, null, null, 0, 0.9); break;
        }
    }

    private static void Cross(FlagPlan p)
    {
        string ground = p.ChooseW("ground", new[] { "solid", "h2", "v2", "quarters" }, p.Party ? new[] { 1.0, 0, 0, 0 } : new[] { 0.55, 0.08, 0.08, 0.29 });
        double sx = A / 2, sy = 0.5;
        string kind = p.ChooseW("cross", new[] { "greek", "nordic" }, new[] { p.Trad?.Name == "nordic" ? 0.2 : 0.5, p.Trad?.Name == "nordic" ? 0.8 : 0.5 });
        double cx = kind == "nordic" ? p.R.R(0.3, 0.4) * A : A / 2, t = p.R.R(0.09, 0.17);
        Rgb a = p.P.Base, b = p.P.Second;
        switch (ground)
        {
            case "h2": p.F.Field = p.Bands(new[] { a, b }, new[] { 1.0, 1 }, false); break;
            case "v2": p.F.Field = p.Bands(new[] { a, b }, new[] { 1.0, 1 }, true); break;
            case "quarters": sx = cx; p.F.Field = (x, y) => (x < sx) == (y < sy) ? a : b; break;
            default: p.F.Field = (_, _) => a; break;
        }
        var cross = p.CrossShape(cx, 0.5, t);
        bool fim = p.ChooseW("fimb", new[] { "no", "yes" }, new[] { 0.7, 0.3 }) == "yes";
        if (fim)
        {
            var outer = p.Ord(p.CrossShape(cx, 0.5, t + 0.07));
            p.Ord(cross, null, outer);
        }
        else p.Ord(cross);
        if (ground != "quarters")
        {
            string extra = p.ChooseW("extra", new[] { "none", "quarters", "center" }, new[] { 0.55, 0.3, 0.15 });
            if (extra == "quarters")
            {
                double[] xs = { (cx - t / 2) / 2, (cx + t / 2 + A) / 2 }, ys = { (0.5 - t / 2) / 2, (0.5 + t / 2 + 1) / 2 };
                double rad = Math.Min(Math.Min(xs[0], A - xs[1]), ys[0]) * 0.55;
                string name = p.Choose("quarter", "star", "roundel", "fleur", "crown", "stars_trio");
                foreach (var qx in xs) foreach (var qy in ys) p.Charge("quarter", qx, qy, rad, null, new[] { name });
            }
            else if (extra == "center") p.Charge("emblem", cx, 0.5, t * 0.95, null, SmallEmblems);
        }
    }

    private static void Saltire(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second;
        bool per = p.ChooseW("ground", new[] { "solid", "per" }, new[] { 0.6, 0.4 }) == "per";
        p.F.Field = per ? (x, y) => (y * A > x) == (y * A + x > A) ? a : b : (_, _) => a;
        double t = p.R.R(0.07, 0.14);
        var sal = p.SaltireShape(t);
        if (p.ChooseW("fimb", new[] { "no", "yes" }, new[] { 0.75, 0.25 }) == "yes")
        {
            var outer = p.Ord(p.SaltireShape(t + 0.05));
            p.Ord(sal, null, outer);
        }
        else p.Ord(sal);
        string extra = p.ChooseW("extra", new[] { "none", "center", "arms" }, new[] { 0.4, 0.4, 0.2 });
        if (extra == "center") p.Charge("emblem", A / 2, 0.5, 0.17, null, SmallEmblems);
        else if (extra == "arms")
            foreach (var (x, y) in new[] { (A / 2, 0.17), (A / 2, 0.83), (A * 0.13, 0.5), (A * 0.87, 0.5) }) p.Charge("arm", x, y, 0.09, null, new[] { "star" });
    }

    private static void Canton(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second, c = p.P.Third;
        string field = p.ChooseW("field", new[] { "barry", "tri", "solid" }, new[] { 0.55, 0.3, 0.15 });
        int n = 1;
        if (field == "barry")
        {
            n = int.Parse(p.Choose("rows", "5", "7", "9", "11", "13"));
            var cols = new Rgb[n]; for (int i = 0; i < n; i++) cols[i] = i % 2 == 0 ? a : b;
            p.F.Field = p.Bands(cols, Enumerable.Repeat(1.0, n).ToArray(), false);
        }
        else if (field == "tri") p.F.Field = p.Bands(p.Choose("seq", "pst", "spt", "sps").Select(p.Letter).ToArray(), new[] { 1.0, 1, 1 }, false);
        else p.F.Field = (_, _) => a;
        double cw = p.R.R(0.5, 0.72), ch = p.R.R(0.46, 0.62);
        if (field == "barry") ch = Math.Max(2, Math.Round(ch * n)) / n;
        var box = Rect(0, 0, cw, ch);
        var fill = p.Ord(box);
        string contents = p.ChooseW("contents", new[] { "stars", "cross", "saltire", "union", "emblem" }, new[] { 2.0, 1.5, 0.7, 1.2, 1.0 });
        double cx = cw / 2, cy = ch / 2, m = Math.Min(cw, ch);
        switch (contents)
        {
            case "stars":
            {
                string arr = p.Choose("arrangement", "single", "ring", "grid");
                var starCol = P(p, fill);
                if (arr == "single") p.Mark(Star(cx, cy, m * 0.36, 5), starCol);
                else if (arr == "ring")
                {
                    int k = int.Parse(p.Choose("count", "5", "7", "9", "12"));
                    for (int i = 0; i < k; i++)
                    {
                        double ang = -Math.PI / 2 + i * 2 * Math.PI / k;
                        p.Mark(Star(cx + m * 0.33 * Math.Cos(ang), cy + m * 0.33 * Math.Sin(ang), m * 0.09, 5), starCol);
                    }
                }
                else
                {
                    int rows = int.Parse(p.Choose("rows", "3", "4", "5")), cols = rows == 3 ? 4 : rows == 4 ? 5 : 6;
                    for (int j = 0; j < rows; j++)
                        for (int i = 0; i < cols - (j % 2); i++)
                        {
                            double sx = cw * (i + 0.5 + (j % 2) * 0.5) / cols, sy = ch * (j + 0.5) / rows;
                            p.Mark(Star(sx, sy, Math.Min(cw / cols, ch / rows) * 0.42, 5), starCol);
                        }
                }
                break;
            }
            case "cross":
                p.Ord(Intersect(box, p.CrossShape(cx, cy, ch * 0.2)), P(p, fill));
                break;
            case "saltire":
                p.Ord(Intersect(box, ShiftedSaltire(cx, cy, cw, ch, ch * 0.13)), P(p, fill));
                break;
            case "union":
            {
                var sc = P(p, fill);
                p.Ord(Intersect(box, ShiftedSaltire(cx, cy, cw, ch, ch * 0.16)), sc);
                var cc = P(p, fill, sc);
                p.Ord(Intersect(box, p.CrossShape(cx, cy, ch * 0.28)), sc);
                p.Ord(Intersect(box, p.CrossShape(cx, cy, ch * 0.16)), cc);
                break;
            }
            default:
                p.Charge("emblem", cx, cy, m * 0.36, null, SmallEmblems);
                break;
        }
    }

    private static Rgb P(FlagPlan p, Rgb on, params Rgb[] also) => p.P.Against(new[] { on }.Concat(also).ToArray());

    private static Hit Intersect(Hit a, Hit b) => (x, y) => a(x, y) && b(x, y);

    /// <summary>A saltire running corner to corner of a canton box that starts at the origin.</summary>
    private static Hit ShiftedSaltire(double cx, double cy, double w, double h, double t)
    {
        double nrm = Math.Sqrt(w * w + h * h);
        return (x, y) => Math.Abs(x * h - y * w) / nrm < t || Math.Abs(x * h + y * w - w * h) / nrm < t;
    }

    private static void Chevron(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second;
        string bands = p.ChooseW("bands", new[] { "tri", "bi", "solid" }, new[] { 0.5, 0.3, 0.2 });
        p.F.Field = bands switch
        {
            "tri" => p.Bands(p.Choose("seq", "pst", "sps", "psp", "spt").Select(p.Letter).ToArray(), new[] { 1.0, 1, 1 }, false),
            "bi" => p.Bands(new[] { a, b }, new[] { 1.0, 1 }, false),
            _ => (_, _) => a,
        };
        double w = p.R.R(0.5, 0.9);
        string shape = p.ChooseW("shape", new[] { "triangle", "double", "band" }, new[] { 0.6, 0.2, 0.2 });
        if (shape == "band")
        {
            double k = p.R.R(0.14, 0.26);
            p.Ord(Poly(new[] { (0.0, 0.0), (w, 0.5), (0.0, 1.0), (0.0, 1 - k), (w * (1 - 2 * k), 0.5), (0.0, k) }));
            if (p.R.P(0.4)) p.Charge("emblem", A * 0.66, 0.5, 0.2, null, SmallEmblems);
            return;
        }
        var tri = Poly(new[] { (0.0, 0.0), (w, 0.5), (0.0, 1.0) });
        var outer = p.Ord(tri);
        double rad = Math.Min(0.2, w * 0.24);
        if (shape == "double")
        {
            double s = 0.64;
            p.Ord(Poly(new[] { (0.0, 0.5 - 0.5 * s), (w * s, 0.5), (0.0, 0.5 + 0.5 * s) }), null, outer);
            rad *= 0.8;
        }
        if (p.R.P(p.Party ? 0.6 : 0.5)) p.Charge("emblem", w * 0.28, 0.5, rad, null, SmallEmblems);
    }

    private static void Geometric(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second, c = p.P.Third;
        string sub = p.ChooseW("sub", new[] { "bend", "diag3", "saltire4", "quarters", "pall", "gyronny", "checky", "lozenge" },
            new[] { 1.5, 1.2, 1.0, 1.4, 0.7, 0.3, 0.2, 1.2 });
        switch (sub)
        {
            case "bend":
                p.F.Field = p.R.P(0.5) ? (x, y) => y * A + x > A ? a : b : (x, y) => y * A > x ? a : b;
                if (p.R.P(0.35)) p.Charge("emblem", A / 2, 0.5, 0.17, null, new[] { "star", "roundel" });
                break;
            case "diag3":
            {
                var bands = p.Bands(p.Choose("seq", "pst", "sps", "spt", "psp").Select(p.Letter).ToArray(), p.Choose("ratio", "eq", "wide") == "eq" ? new[] { 1.0, 1, 1 } : new[] { 1.0, 2, 1 }, true);
                p.F.Field = p.R.P(0.5) ? (x, y) => bands((x / A + y) / 2 * A, 0) : (x, y) => bands((x / A - y + 1) / 2 * A, 0);
                break;
            }
            case "saltire4":
                p.F.Field = (x, y) => (y * A > x) == (y * A + x > A) ? a : b;
                if (p.R.P(0.4)) p.Charge("emblem", A / 2, 0.5, 0.15, null, SmallEmblems);
                break;
            case "quarters":
            {
                double sx = p.R.P(0.4) ? p.R.R(0.35, 0.55) * A : A / 2, sy = p.R.P(0.3) ? p.R.R(0.4, 0.6) : 0.5;
                bool three = p.R.P(0.4);
                p.F.Field = (x, y) => (x < sx) == (y < sy) ? a : (three && x >= sx ? c : b);
                if (p.R.P(0.3)) p.Charge("emblem", sx, sy, 0.14, null, new[] { "star", "roundel" });
                break;
            }
            case "pall":
            {
                double cx = p.R.R(0.35, 0.6) * A;
                p.F.Field = (x, y) =>
                {
                    if (x >= cx) return y < 0.5 ? a : c;
                    double up = 0.5 * x / cx;
                    return y > up && y < 1 - up ? b : y < 0.5 ? a : c;
                };
                break;
            }
            case "gyronny":
                p.F.Field = (x, y) =>
                {
                    double dx = (x - A / 2) / (A / 2), dy = (y - 0.5) / 0.5;
                    return (int)Math.Floor((Math.Atan2(dy, dx) + Math.PI) / (Math.PI / 4)) % 2 == 0 ? a : b;
                };
                p.Busy = true;
                break;
            case "checky":
            {
                int cols = p.Choose("cols", "6", "8", "10") is var cs ? int.Parse(cs) : 8, rows = cols == 6 ? 4 : cols == 8 ? 6 : 7;
                p.F.Field = (x, y) => ((int)(x / A * cols) + (int)(y * rows)) % 2 == 0 ? a : b;
                p.Busy = true;
                break;
            }
            default:
            {
                p.F.Field = (_, _) => a;
                double s = p.R.R(0.7, 0.88);
                var loz = p.Ord((x, y) => Math.Abs(x - A / 2) / (A / 2) + Math.Abs(y - 0.5) / 0.5 <= s);
                var disc = p.Ord(Disc(A / 2, 0.5, 0.2), null, loz);
                if (p.R.P(0.5)) p.Charge("emblem", A / 2, 0.5, 0.13, null, new[] { "star" });
                break;
            }
        }
    }

    private static void Heraldic(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second;
        string ground = p.ChooseW("ground", new[] { "solid", "perpale", "quarterly" }, new[] { 0.5, 0.25, 0.25 });
        p.F.Field = ground switch
        {
            "perpale" => p.Bands(new[] { a, b }, new[] { 1.0, 1 }, true),
            "quarterly" => (x, y) => (x < A / 2) == (y < 0.5) ? a : b,
            _ => (_, _) => a,
        };
        double border = 0;
        if (p.ChooseW("bordure", new[] { "no", "yes" }, new[] { 0.55, 0.45 }) == "yes")
        {
            border = p.R.R(0.05, 0.08);
            var inner = Rect(border * 0.85, border, A - border * 0.85, 1 - border);
            p.Ord((x, y) => !inner(x, y));
        }
        var tags = new[] { "heraldic", "monarchic" };
        string layout = p.ChooseW("layout", new[] { "single", "quartered", "trio" }, ground == "quarterly" ? new[] { 0.3, 0.5, 0.2 } : new[] { 0.6, 0.2, 0.2 });
        if (layout == "quartered")
        {
            double rad = 0.19 - border * 0.5;
            var d1 = p.PickEmblem("e1", rad, tags, null); var d2 = p.PickEmblem("e2", rad, tags, null);
            if (d1 == null) return;
            string n1 = d1.Name, n2 = (d2 ?? d1).Name;
            double[] xs = { A * 0.25, A * 0.75 }, ys = { 0.27, 0.73 };
            for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) p.Charge("q", xs[i], ys[j], rad, null, new[] { (i + j) % 2 == 0 ? n1 : n2 });
        }
        else if (layout == "trio")
        {
            double rad = 0.17;
            var d1 = p.PickEmblem("e1", rad, tags, null);
            if (d1 == null) return;
            foreach (var (x, y) in new[] { (A / 2, 0.3), (A / 2 - 0.34, 0.68), (A / 2 + 0.34, 0.68) }) p.Charge("t", x, y, rad, null, new[] { d1.Name });
        }
        else p.Charge("emblem", A / 2, 0.5, p.R.R(0.27, 0.33) - border * 0.5, tags);
    }

    private static void Emblematic(FlagPlan p)
    {
        Rgb a = p.P.Base, b = p.P.Second;
        string ground = p.ChooseW("ground", new[] { "solid", "h2", "v2", "hoistbar", "edges" }, new[] { 0.45, 0.15, 0.1, 0.12, 0.18 });
        double cx = A / 2;
        switch (ground)
        {
            case "h2": p.F.Field = p.Bands(new[] { a, b }, new[] { 1.0, 1 }, false); break;
            case "v2": p.F.Field = p.Bands(new[] { a, b }, new[] { 1.0, 1 }, true); break;
            case "hoistbar":
            {
                double w = p.R.R(0.2, 0.34);
                p.F.Field = (x, y) => x < w ? b : a;
                cx = (A + w) / 2;
                break;
            }
            case "edges":
            {
                double t = p.R.R(0.07, 0.11);
                p.F.Field = (_, _) => a;
                var stripe = p.Ord((x, y) => y < t || y > 1 - t);
                break;
            }
            default: p.F.Field = (_, _) => a; break;
        }
        double rad = p.R.R(0.26, 0.34);
        string[]? want = p.Fam?.EmblemTags.Count > 0 ? p.Fam.EmblemTags.Concat(new[] { "cosmic" }).ToArray() : p.Trad?.EmblemTags.Count > 0 ? p.Trad.EmblemTags.Concat(new[] { "cosmic", "national" }).ToArray() : new[] { "cosmic", "national", "heraldic", "socialist" };
        string surround = p.ChooseW("surround", new[] { "none", "disc", "wreath", "ring" }, new[] { 0.5, 0.25, 0.15, 0.1 });
        if (surround == "disc")
        {
            var disc = Disc(cx, 0.5, rad * 1.22);
            var col = p.Ord(disc);
            var def = p.PickEmblem("emblem", rad, want, null);
            if (def != null) p.Mark(Place(new Hit(def.Make(p.R.Next())), cx, 0.5, rad, def.DefaultRotation), P(p, col));
        }
        else if (surround == "wreath" && rad >= 0.27)
        {
            var under = p.F.Under(Disc(cx, 0.5, rad * 1.2));
            var col = p.P.Against(under);
            p.Mark(Place(new Hit(FlagEmblems.Wreath.Make(p.R.Next())), cx, 0.5, rad * 1.22, 0), col);
            p.Charge("emblem", cx, 0.5, rad * 0.6, want, null, 0, 0);
        }
        else if (surround == "ring")
        {
            var under = p.F.Under(Disc(cx, 0.5, rad * 1.3));
            var col = p.P.Against(under);
            double ro = rad * 1.25, ri = rad * 1.12;
            p.Mark((x, y) => { double d2 = (x - cx) * (x - cx) + (y - 0.5) * (y - 0.5); return d2 <= ro * ro && d2 >= ri * ri; }, col);
            p.Charge("emblem", cx, 0.5, rad * 0.95, want, null, 0, 0);
        }
        else p.Charge("emblem", cx, 0.5, rad, want, null, 0, 0.6);
    }
}
