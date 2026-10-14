using static Burger.Core.FlagKit;

namespace Burger.Core;

/// <summary>
/// One emblem: a silhouette drawn in a unit local space (radius 1, up is -y) so it can be placed, scaled, rotated and mirrored.
/// Tags link emblems to political families and traditions; MinYear/MaxYear gate them by period; MinRadius keeps detailed ones off tiny spots.
/// </summary>
public sealed record EmblemDef(string Name, string[] Tags, Func<ulong, Func<double, double, bool>> Make, double MinRadius = 0.0,
    int MinYear = int.MinValue, int MaxYear = int.MaxValue, double DefaultRotation = 0, double Weight = 1.0, bool Symmetric = true);

/// <summary>The emblem library. Register more at startup (before generating flags) to extend it.</summary>
public static class FlagEmblems
{
    private static readonly Dictionary<string, EmblemDef> _defs = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<EmblemDef> All => _defs.Values;
    public static EmblemDef? Get(string name) => _defs.TryGetValue(name, out var d) ? d : null;
    public static void Register(EmblemDef d) { lock (_defs) _defs[d.Name] = d; }

    private static void Add(string name, string[] tags, Func<Rng, Hit> make, double minR = 0, int minYear = int.MinValue, int maxYear = int.MaxValue, double rot = 0, double weight = 1, bool sym = true)
        => _defs[name] = new EmblemDef(name, tags, o => { var h = make(new Rng(o)); return (x, y) => h(x, y); }, minR, minYear, maxYear, rot, weight, sym);

    static FlagEmblems()
    {
        // ---- cosmic / national
        Add("star", new[] { "cosmic", "national", "socialist", "liberal", "military", "small" }, r => Star(0, 0, 1, r.Pick(new[] { 5, 5, 5, 5, 6, 7, 8 })), 0, weight: 3);
        Add("star4", new[] { "cosmic", "small" }, r => Star(0, 0, 1, 4), weight: 0.3);
        Add("sunburst", new[] { "cosmic", "national", "liberal", "agrarian" }, r =>
        {
            int n = r.Pick(new[] { 8, 12, 16 }); double core = 0.42;
            return (x, y) =>
            {
                double d = Math.Sqrt(x * x + y * y);
                if (d <= core) return true;
                if (d > 1) return false;
                double sector = (Math.Atan2(y, x) / (2 * Math.PI) * n) % 1; if (sector < 0) sector += 1;
                double half = 0.5 * (1 - (d - core * 1.25) / (1 - core * 1.25)) * 0.7;
                return d >= core * 1.25 && Math.Abs(sector - 0.5) < half;
            };
        }, 0.16, weight: 1.5);
        Add("rising_sun", new[] { "cosmic", "national", "liberal", "agrarian" }, r =>
        {
            int n = r.Pick(new[] { 7, 9, 11 });
            return (x, y) =>
            {
                double dy = y - 0.45;
                if (dy > 0) return false;
                double d = Math.Sqrt(x * x + dy * dy);
                if (d <= 0.42) return true;
                if (d > 1.0 || d < 0.55) return false;
                double ang = Math.Atan2(dy, x) + Math.PI;   // 0..pi over the upper half
                double sector = ang / Math.PI * n % 1;
                return Math.Abs(sector - 0.5) < 0.5 * (1 - (d - 0.55) / 0.45) * 0.75 + 0.05;
            };
        }, 0.2, weight: 0.8);
        Add("roundel", new[] { "cosmic", "national", "military", "small" }, r =>
        {
            bool ring = r.P(0.4);
            return ring ? (x, y) => { double d2 = x * x + y * y; return d2 <= 1 && d2 >= 0.36; } : Disc(0, 0, 0.88);
        }, weight: 1.5);
        Add("lozenge", new[] { "heraldic", "small" }, r => (x, y) => Math.Abs(x) / 0.8 + Math.Abs(y) <= 1, weight: 0.5);
        Add("triangle", new[] { "cosmic", "military" }, r => Poly(new[] { (0.0, -0.95), (0.95, 0.75), (-0.95, 0.75) }), weight: 0.4);
        Add("stars_ring", new[] { "cosmic", "national" }, r =>
        {
            int n = r.Pick(new[] { 5, 7, 9, 12 }); var stars = new List<Hit>();
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + i * 2 * Math.PI / n;
                stars.Add(Star(0.78 * Math.Cos(a), 0.78 * Math.Sin(a), n > 9 ? 0.2 : 0.26, 5));
            }
            return Union(stars.ToArray());
        }, 0.2, weight: 1.2);
        Add("stars_trio", new[] { "cosmic", "national", "military" }, r =>
        {
            var s = new List<Hit>();
            for (int i = -1; i <= 1; i++) s.Add(Star(i * 0.74, Math.Abs(i) * 0.2 - 0.1, 0.34, 5));
            return Union(s.ToArray());
        }, 0.12, weight: 0.8);
        Add("crescent", new[] { "cosmic", "national", "religious" }, r =>
        {
            double off = r.R(0.3, 0.42), inR = r.R(0.72, 0.82);
            var outer = Disc(0, 0, 0.95); var inner = Disc(off, 0, inR);
            return Minus(outer, inner);
        }, 0.1, weight: 1.2, sym: false);
        Add("crescent_star", new[] { "cosmic", "national", "religious" }, r =>
        {
            double off = r.R(0.32, 0.42), inR = r.R(0.72, 0.8);
            var crescent = Minus(Disc(-0.05, 0, 0.95), Disc(off, 0, inR));
            var star = Star(0.45, 0, 0.3, 5, 0);
            return Union(crescent, star);
        }, 0.18, weight: 1.2, sym: false);

        // ---- heraldic
        Add("cross_pattee", new[] { "heraldic", "religious", "military", "national" }, r =>
        {
            var arms = new List<Hit> { Rect(-0.22, -0.22, 0.22, 0.22) };
            for (int k = 0; k < 4; k++)
            {
                double c = Math.Cos(k * Math.PI / 2), s = Math.Sin(k * Math.PI / 2);
                (double x, double y)[] arm = { (-0.2, -0.15), (-0.45, -1), (0.45, -1), (0.2, -0.15) };
                arms.Add(Poly(arm.Select(p => (p.x * c - p.y * s, p.x * s + p.y * c)).ToArray()));
            }
            return Union(arms.ToArray());
        }, 0.14, weight: 1.2);
        Add("cross_greek", new[] { "heraldic", "religious" }, r => Union(Rect(-0.2, -1, 0.2, 1), Rect(-1, -0.2, 1, 0.2)), 0.1, weight: 0.5);
        Add("crown", new[] { "heraldic", "monarchic", "national" }, r =>
        {
            var body = Poly(new[] { (-0.75, 0.3), (-0.75, -0.4), (-0.38, 0.0), (0.0, -0.55), (0.38, 0.0), (0.75, -0.4), (0.75, 0.3) });
            var band = Rect(-0.75, 0.3, 0.75, 0.6);
            return Union(body, band, Disc(-0.75, -0.46, 0.11), Disc(0, -0.62, 0.11), Disc(0.75, -0.46, 0.11));
        }, 0.2, weight: 1.4);
        Add("fleur", new[] { "heraldic", "monarchic", "national" }, r =>
        {
            var lens = Lens(0.4);
            return Union(Place(lens, 0, -0.4, 0.6), Place(lens, -0.46, -0.05, 0.46, -0.75), Place(lens, 0.46, -0.05, 0.46, 0.75),
                Rect(-0.52, 0.16, 0.52, 0.3), Place(lens, 0, 0.62, 0.36, Math.PI));
        }, 0.2, weight: 1.2);
        Add("castle", new[] { "heraldic", "national", "military" }, r =>
        {
            var body = Rect(-0.7, -0.15, 0.7, 0.65);
            var merlons = Union(Rect(-0.7, -0.45, -0.4, -0.15), Rect(-0.15, -0.45, 0.15, -0.15), Rect(0.4, -0.45, 0.7, -0.15));
            var door = Union(Rect(-0.2, 0.25, 0.2, 0.65), Disc(0, 0.25, 0.2));
            return Minus(Union(body, merlons), door);
        }, 0.16, weight: 1.2);
        Add("sword", new[] { "heraldic", "military", "nationalist" }, r =>
            Union(Poly(new[] { (0.0, -1.0), (0.1, -0.8), (0.1, 0.3), (-0.1, 0.3), (-0.1, -0.8) }), Rect(-0.42, 0.3, 0.42, 0.42), Rect(-0.06, 0.42, 0.06, 0.78), Disc(0, 0.86, 0.12)), 0.2, weight: 1);
        Add("eagle", new[] { "heraldic", "military", "nationalist", "national", "monarchic" }, r =>
        {
            var wings = SymPoly(new (double, double)[]
            {
                (0.0, -0.5), (0.16, -0.46), (0.5, -0.78), (0.96, -0.8), (0.9, -0.56), (1.0, -0.46), (0.88, -0.28), (0.97, -0.12),
                (0.74, 0.0), (0.74, 0.2), (0.42, 0.18), (0.3, 0.55), (0.46, 0.8), (0.18, 0.96), (0.0, 0.72),
            });
            return Union(wings, Disc(0, -0.7, 0.2), Poly(new[] { (0.08, -0.74), (0.34, -0.62), (0.1, -0.6) }));
        }, 0.26, weight: 1.3, sym: false);
        Add("laurel", new[] { "wreath" }, r =>
        {
            var lens = Lens(0.42); var leaves = new List<Hit>();
            for (int i = 0; i < 9; i++)
            {
                double t = 0.12 + i * 0.1;   // angle from the bottom going up each side
                double a = Math.PI / 2 + t * Math.PI * 1.7;
                double px = 0.8 * Math.Cos(a), py = 0.8 * Math.Sin(a), rot = a + Math.PI / 2 + 0.5;
                leaves.Add(Place(lens, px, py, 0.2, rot));
                leaves.Add(Place(lens, -px, py, 0.2, -rot));
            }
            return Union(leaves.ToArray());
        }, 0.3, weight: 0);

        // ---- political and social
        Add("rose", new[] { "socialist", "liberal", "heraldic" }, r =>
        {
            var petals = new List<Hit>();
            for (int i = 0; i < 5; i++) { double a = -Math.PI / 2 + i * 2 * Math.PI / 5; petals.Add(Disc(0.58 * Math.Cos(a), 0.58 * Math.Sin(a), 0.4)); }
            var all = Union(petals.ToArray());
            return (x, y) => (all(x, y) && x * x + y * y >= 0.07) || x * x + y * y <= 0.012;
        }, 0.18, weight: 1.2);
        Add("hammer_sickle", new[] { "socialist", "communist" }, r =>
        {
            var sickle = Union(Minus(Disc(-0.05, 0.1, 0.85), Disc(0.28, -0.02, 0.7)), Place(Rect(-0.07, 0, 0.07, 1), -0.45, 0.65, 0.5, 0.6));
            var hammer = Union(Place(Rect(-0.07, -1, 0.07, 1), 0.05, 0.1, 0.9, -0.75), Place(Rect(-0.3, -0.12, 0.3, 0.12), 0.45, -0.52, 1, -0.75));
            return Union(sickle, hammer);
        }, 0.24, minYear: 1917, weight: 1.5, sym: false);
        Add("cog", new[] { "socialist", "labour", "industrial" }, r =>
        {
            int n = r.Pick(new[] { 8, 10, 12 });
            return (x, y) =>
            {
                double d = Math.Sqrt(x * x + y * y);
                if (d < 0.32) return false;
                if (d <= 0.72) return true;
                if (d > 1) return false;
                double sector = (Math.Atan2(y, x) / (2 * Math.PI) * n) % 1; if (sector < 0) sector += 1;
                return Math.Abs(sector - 0.5) < 0.2;
            };
        }, 0.18, weight: 1.2);
        Add("wheel", new[] { "labour", "agrarian", "cosmic" }, r =>
        {
            int spokes = r.Pick(new[] { 6, 8, 12 });
            return (x, y) =>
            {
                double d2 = x * x + y * y;
                if (d2 > 1) return false;
                if (d2 >= 0.6 || d2 <= 0.03) return true;
                double step = Math.PI / spokes * 2, ang = ((Math.Atan2(y, x) % step) + step) % step;
                return Math.Min(ang, step - ang) * Math.Sqrt(d2) < 0.08;
            };
        }, 0.14, weight: 1.2);
        Add("flame", new[] { "liberal", "nationalist", "revolutionary" }, r =>
            Union(Place(Lens(0.55), 0, -0.2, 0.85), Disc(0, 0.38, 0.5)), 0.14, weight: 1.2);
        Add("arrows", new[] { "socialist", "revolutionary" }, r =>
        {
            Hit arrow(double dx) => Union(Rect(dx - 0.07, -0.55, dx + 0.07, 0.9), Poly(new[] { (dx, -1.0), (dx - 0.32, -0.5), (dx + 0.32, -0.5) }));
            return Union(arrow(-0.62), arrow(0), arrow(0.62));
        }, 0.24, minYear: 1931, rot: Math.PI * 0.75, weight: 1);
        Add("wheat", new[] { "agrarian" }, r =>
        {
            var lens = Lens(0.45); var parts = new List<Hit> { Rect(-0.04, -0.2, 0.04, 1.0), Place(lens, 0, -0.78, 0.3) };
            for (int i = 0; i < 4; i++)
            {
                double y = -0.45 + i * 0.23;
                parts.Add(Place(lens, -0.2, y, 0.26, -0.7)); parts.Add(Place(lens, 0.2, y, 0.26, 0.7));
            }
            return Union(parts.ToArray());
        }, 0.2, weight: 1.2);

        // ---- legacy tag aliases map onto these: see FlagTraditions.FamilyEmblems
    }

    internal static IEnumerable<EmblemDef> Available(int? year) =>
        _defs.Values.Where(d => d.Weight > 0 && (year == null || (year >= d.MinYear && year <= d.MaxYear)));

    /// <summary>The wreath that may surround a large emblem.</summary>
    internal static EmblemDef Wreath => _defs["laurel"];
}
