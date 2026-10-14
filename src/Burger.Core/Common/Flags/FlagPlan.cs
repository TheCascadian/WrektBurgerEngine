using static Burger.Core.FlagKit;

namespace Burger.Core;

/// <summary>The colours of one flag: the identity's own colour first, then partners picked for hue distance and contrast.</summary>
internal sealed class FlagPalette
{
    public readonly Rgb Base, Second, Third;
    private readonly List<Rgb> _pool = new();
    private readonly Rng _r;

    public FlagPalette(Rng r, FlagIdentity id, FlagTradition? trad, FlagPoliticalFamily? fam, IReadOnlyList<Rgb>? inherited)
    {
        _r = r; Base = id.Color;
        var (h, s, l) = ToHsl(Base);
        // preferred colours, most specific first: a predecessor's, the identity's own, its political family, its tradition
        var pref = new List<Rgb>();
        if (inherited != null) pref.AddRange(inherited);
        if (id.Accents != null) pref.AddRange(id.Accents);
        if (fam != null) pref.AddRange(fam.Palette);
        if (trad != null) pref.AddRange(trad.Palette);
        bool party = id.Kind == FlagKind.Party;
        double roll = r.D();
        if (pref.Count >= 2 && (inherited != null || roll < 0.75))
        {
            _pool.AddRange(pref);
        }
        else if (roll < (party ? 0.30 : 0.50))
        {
            var st = Stock.Where(c => Dist(c, Base) >= 130).ToList();
            r.Shuffle(st);
            _pool.AddRange(st.Take(3)); _pool.Add(r.P(0.5) ? White : Black);
        }
        else if (roll < (party ? 0.70 : 0.85))
        {
            _pool.Add(White); _pool.Add(r.P(0.55) ? Gold : Black);
            if (r.P(0.3)) _pool.Add(r.P(0.5) ? Black : Gold);
        }
        else
        {
            double sat = Math.Clamp(s, 0.35, 0.85);
            _pool.Add(FromHsl(h, sat, Math.Min(0.88, l + 0.30)));
            _pool.Add(FromHsl(h, sat, Math.Max(0.10, l - 0.26)));
            _pool.Add(FromHsl(h + (r.P(0.5) ? 180 : 150), sat, 0.42));
            _pool.Add(White);
        }
        Second = PickField(Base, null);
        Third = PickField(Base, Second);
    }

    public IReadOnlyList<Rgb> Colors => new[] { Base, Second, Third };

    private Rgb PickField(Rgb a, Rgb? b)
    {
        var c = _pool.Where(x => Dist(x, a) >= 110 && (b == null || Dist(x, b.Value) >= 110)).ToList();
        if (c.Count > 0) return c[Math.Min(c.Count - 1, (int)(_r.D() * _r.D() * c.Count))];   // earlier (more specific) entries are likelier
        foreach (var n in new[] { White, Black, Gold })
            if (Dist(n, a) >= 140 && (b == null || Dist(n, b.Value) >= 110)) return n;
        return Lum(a) > 0.4 ? Black : White;
    }

    /// <summary>A colour that reads on every colour in <paramref name="under"/>: contrast first, then the tincture rule and palette fit.</summary>
    public Rgb Against(IReadOnlyList<Rgb> under)
    {
        if (under.Count == 0) under = new[] { Base };
        Rgb best = White; double bestScore = double.MinValue;
        void Try(Rgb c, double bonus)
        {
            double minContrast = double.MaxValue; int ruleOk = 0;
            foreach (var u in under)
            {
                minContrast = Math.Min(minContrast, Contrast(c, u));
                if ((Lum(c) > 0.4) != (Lum(u) > 0.4)) ruleOk++;
            }
            double score = Math.Min(minContrast, 4.5) * 2 + bonus + (ruleOk == under.Count ? 1.2 : 0) + _r.D() * 1.6;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        foreach (var c in _pool) Try(c, 0.8);
        Try(Base, 0.4); Try(White, 0); Try(Black, 0);
        return best;
    }
}

/// <summary>
/// The planning context for one candidate: it owns the layered flag being built and records every design decision by slot name,
/// so a related identity can be handed the same decisions as hints and keep a family resemblance while varying the rest.
/// </summary>
internal sealed class FlagPlan
{
    public readonly Rng R;
    public readonly FlagIdentity Id;
    public readonly FlagPalette P;
    public readonly Flag F = new();
    public readonly Dictionary<string, string> Choices = new();
    public readonly IReadOnlyDictionary<string, string>? Hints;
    public readonly FlagTradition? Trad;
    public readonly FlagPoliticalFamily? Fam;
    public bool Busy, NoCharge;
    public int Charges;
    public string Family = "";
    private const double Keep = 0.65;

    public FlagPlan(Rng r, FlagIdentity id, FlagPalette p, IReadOnlyDictionary<string, string>? hints)
    {
        R = r; Id = id; P = p; Hints = hints;
        Trad = FlagTraditions.Tradition(id.Tradition); Fam = FlagTraditions.Family(id.Family);
    }

    public bool Party => Id.Kind == FlagKind.Party;
    public double VerticalBias => Trad?.VerticalBias ?? 0.5;

    // ---------------------------------------------------------------- recorded choices

    public string Choose(string slot, params string[] options) => ChooseW(slot, options, null);

    public string ChooseW(string slot, string[] options, double[]? weights)
    {
        string key = Family + "." + slot;
        string pick;
        if (Hints != null && Hints.TryGetValue(key, out var h) && Array.IndexOf(options, h) >= 0 && R.P(Keep)) pick = h;
        else pick = weights == null ? R.Pick(options) : options[R.Weighted(weights)];
        Choices[key] = pick;
        return pick;
    }

    // ---------------------------------------------------------------- layers

    public Rgb Ink(Hit hit, params Rgb[] also)
    {
        var under = F.Under(hit);
        under.AddRange(also);
        return P.Against(under);
    }

    /// <summary>Adds an ordinary: a flat shape in a colour that reads on everything beneath it.</summary>
    public Rgb Ord(Hit hit, Rgb? color = null, params Rgb[] also)
    {
        var col = color ?? Ink(hit, also);
        F.Layers.Add(((Paint)((x, y) => hit(x, y) ? col : null), false));
        return col;
    }

    public Rgb Mark(Hit hit, Rgb? color = null, params Rgb[] also)
    {
        var col = color ?? Ink(hit, also);
        F.Layers.Add(((Paint)((x, y) => hit(x, y) ? col : null), true));
        Charges++;
        return col;
    }

    public EmblemDef? PickEmblem(string slot, double radius, string[]? require, string[]? allowed)
    {
        var excluded = Trad?.ExcludedEmblems;
        var cands = FlagEmblems.Available(Id.Year)
            .Where(d => radius >= d.MinRadius && (allowed == null || allowed.Contains(d.Name)) && (excluded == null || !excluded.Contains(d.Name))
                        && (require == null || d.Tags.Any(t => require.Contains(t))))
            .ToList();
        if (cands.Count == 0) return null;
        string key = Family + "." + slot;
        if (Hints != null && Hints.TryGetValue(key, out var h) && R.P(Keep))
        {
            var hd = cands.FirstOrDefault(d => d.Name == h);
            if (hd != null) { Choices[key] = hd.Name; return hd; }
        }
        var w = new double[cands.Count];
        for (int i = 0; i < cands.Count; i++)
        {
            var d = cands[i]; double x = d.Weight;
            if (Fam != null && d.Tags.Any(t => Fam.EmblemTags.Contains(t))) x *= 3;
            if (Fam != null && Fam.PreferredEmblems.Contains(d.Name)) x *= 4;
            if (Trad != null && d.Tags.Any(t => Trad.EmblemTags.Contains(t))) x *= 2;
            if (Id.Motifs != null && Id.Motifs.Contains(d.Name)) x *= 8;
            w[i] = x;
        }
        var pick = cands[R.Weighted(w)];
        Choices[key] = pick.Name;
        return pick;
    }

    /// <summary>Places one emblem at (x,y) with radius <paramref name="rad"/>. Busy spots get a backing roundel first.</summary>
    public bool Charge(string slot, double x, double y, double rad, string[]? require = null, string[]? allowed = null, double rot = 0, double backingP = 0.5)
    {
        if (NoCharge) return false;
        var def = PickEmblem(slot, rad, require, allowed);
        if (def == null) return false;
        var local = new Hit(def.Make(R.Next()));
        var shape = Place(local, x, y, rad, def.DefaultRotation + rot);
        var under = F.Under(Disc(x, y, rad * 1.1));
        if (under.Count > 2 || (under.Count > 1 && R.P(backingP)))
        {
            var back = Disc(x, y, rad * 1.14);
            var backCol = P.Against(under);
            Ord(back, backCol);
            Mark(shape, P.Against(new[] { backCol }));
            Charges--;   // the backing is not a separate charge
        }
        else Mark(shape);
        return true;
    }

    // ---------------------------------------------------------------- shared builders

    public Func<double, double, Rgb> Bands(Rgb[] cols, double[] weights, bool vertical, double wave = 0, double waves = 3, double phase = 0)
    {
        double total = weights.Sum(); var edge = new double[cols.Length]; double acc = 0;
        for (int i = 0; i < cols.Length; i++) { acc += weights[i] / total; edge[i] = acc; }
        return (x, y) =>
        {
            double t = vertical ? x / A : y;
            if (wave > 0) t += wave * Math.Sin(2 * Math.PI * waves * (vertical ? y : x / A) + phase);
            for (int i = 0; i < cols.Length; i++) if (t < edge[i]) return cols[i];
            return cols[^1];
        };
    }

    public Rgb Letter(char c) => c switch { 'p' => P.Base, 's' => P.Second, _ => P.Third };

    public Hit CrossShape(double cx, double cy, double t) => Union(Rect(cx - t / 2, 0, cx + t / 2, 1), Rect(0, cy - t / 2, A, cy + t / 2));
    public Hit SaltireShape(double t)
    {
        double nrm = Math.Sqrt(A * A + 1);
        return (x, y) => Math.Abs(x - y * A) / nrm < t || Math.Abs(x + y * A - A) / nrm < t;
    }
}
