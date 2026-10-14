namespace Burger.Core;

/// <summary>Deterministic randomness, colour maths, shape primitives and the layered flag model shared by the flag generator.</summary>
internal static class FlagKit
{
    /// <summary>Flags are laid out on a canvas with x in 0..Aspect and y in 0..1.</summary>
    public const double A = 1.5;

    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
        return h;
    }

    public static ulong Mix(ulong a, ulong b)
    {
        ulong z = a + 0x9E3779B97F4A7C15UL * (b + 1);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public sealed class Rng
    {
        private ulong _s;
        public Rng(ulong seed) { _s = seed; Next(); }
        public ulong Next() => Mix(_s += 0x9E3779B97F4A7C15UL, 0);
        public double D() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        public double R(double a, double b) => a + (b - a) * D();
        public int I(int a, int b) => a + (int)(D() * (b - a + 1));
        public bool P(double p) => D() < p;
        public T Pick<T>(IList<T> l) => l[(int)(D() * l.Count)];
        public int Weighted(IReadOnlyList<double> w)
        {
            double t = 0; foreach (var x in w) t += Math.Max(0, x);
            if (t <= 0) return 0;
            double v = D() * t;
            for (int i = 0; i < w.Count; i++) { v -= Math.Max(0, w[i]); if (v < 0) return i; }
            return w.Count - 1;
        }
        public void Shuffle<T>(IList<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = (int)(D() * (i + 1)); (l[i], l[j]) = (l[j], l[i]); }
        }
    }

    // ---------------------------------------------------------------- colour

    private static double Lin(byte c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    public static double Lum(Rgb c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    public static double Contrast(Rgb a, Rgb b) { double x = Lum(a) + 0.05, y = Lum(b) + 0.05; return x > y ? x / y : y / x; }

    /// <summary>Perceptual-ish distance (redmean): 0 identical, about 720 black vs white.</summary>
    public static double Dist(Rgb a, Rgb b)
    {
        double rm = (a.R + b.R) / 2.0, dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db);
    }

    public static (double h, double s, double l) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2, d = mx - mn;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn), h;
        if (mx == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (mx == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return new Rgb((int)((r + m) * 255 + 0.5), (int)((g + m) * 255 + 0.5), (int)((b + m) * 255 + 0.5));
    }

    public static readonly Rgb White = new(244, 244, 238), Black = new(24, 24, 28), Gold = new(240, 193, 43);
    public static readonly Rgb Red = new(204, 32, 46), Blue = new(24, 64, 160), Green = new(18, 120, 66), Sky = new(90, 160, 215), Orange = new(240, 130, 30), Purple = new(100, 40, 120);
    public static readonly Rgb[] Stock = { Red, Blue, Green, Sky, Gold, Orange, Purple };

    // ---------------------------------------------------------------- shapes

    public delegate bool Hit(double x, double y);
    public delegate Rgb? Paint(double x, double y);

    public static Hit Rect(double x0, double y0, double x1, double y1) => (x, y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
    public static Hit Disc(double cx, double cy, double r) => (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    public static Hit Union(params Hit[] f) => (x, y) => { foreach (var g in f) if (g(x, y)) return true; return false; };
    public static Hit Minus(Hit a, Hit b) => (x, y) => a(x, y) && !b(x, y);

    public static Hit Poly(IReadOnlyList<(double x, double y)> p) => (x, y) =>
    {
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
        return inside;
    };

    /// <summary>A polygon given for x &gt;= 0 only, mirrored across the vertical axis.</summary>
    public static Hit SymPoly(IReadOnlyList<(double x, double y)> half)
    {
        var all = new List<(double, double)>(half);
        for (int i = half.Count - 1; i >= 0; i--) if (half[i].x != 0) all.Add((-half[i].x, half[i].y));
        return Poly(all);
    }

    /// <summary>A pointed leaf of half-length 1 and half-width <paramref name="w"/>, pointing up (-y).</summary>
    public static Hit Lens(double w = 0.38) => (x, y) => y >= -1 && y <= 1 && Math.Abs(x) <= w * Math.Pow(1 - y * y, 0.7);

    public static List<(double x, double y)> StarPoints(double cx, double cy, double R, int n, double rot)
    {
        double inner = n switch { 4 => 0.4, 5 => 0.382, 6 => 0.55, 7 => 0.5, 8 => 0.6, _ => 0.72 };
        var pts = new List<(double, double)>();
        for (int i = 0; i < n * 2; i++)
        {
            double a = rot + i * Math.PI / n, rr = i % 2 == 0 ? R : R * inner;
            pts.Add((cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
        }
        return pts;
    }

    public static Hit Star(double cx, double cy, double R, int n, double rot = -Math.PI / 2) => Poly(StarPoints(cx, cy, R, n, rot));

    /// <summary>Maps a shape drawn in a unit local space (up is -y, radius 1) onto the flag canvas.</summary>
    public static Hit Place(Hit local, double cx, double cy, double R, double rot = 0, bool mirror = false)
    {
        double c = Math.Cos(rot), s = Math.Sin(rot);
        return (x, y) =>
        {
            double dx = (x - cx) / R, dy = (y - cy) / R;
            double lx = dx * c + dy * s, ly = -dx * s + dy * c;
            return local(mirror ? -lx : lx, ly);
        };
    }

    /// <summary>Distance from (x,y) to the line through (ax,ay) and (bx,by).</summary>
    public static double LineDist(double x, double y, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        return Math.Abs(dx * (y - ay) - dy * (x - ax)) / Math.Sqrt(dx * dx + dy * dy);
    }

    // ---------------------------------------------------------------- layered flag

    public sealed class Flag
    {
        public Func<double, double, Rgb> Field = (_, _) => Black;
        public readonly List<(Paint Fn, bool Charge)> Layers = new();

        public Rgb At(double x, double y, bool skipCharges = false)
        {
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                if (skipCharges && Layers[i].Charge) continue;
                var c = Layers[i].Fn(x, y);
                if (c.HasValue) return c.Value;
            }
            return Field(x, y);
        }

        /// <summary>True when a charge layer is the top layer at the point.</summary>
        public bool ChargeAt(double x, double y)
        {
            for (int i = Layers.Count - 1; i >= 0; i--)
                if (Layers[i].Fn(x, y).HasValue) return Layers[i].Charge;
            return false;
        }

        /// <summary>The distinct colours currently showing inside a shape.</summary>
        public List<Rgb> Under(Hit hit, bool skipCharges = false)
        {
            var l = new List<Rgb>();
            for (int j = 0; j < 20; j++)
                for (int i = 0; i < 30; i++)
                {
                    double x = (i + 0.5) / 30 * A, y = (j + 0.5) / 20;
                    if (!hit(x, y)) continue;
                    var c = At(x, y, skipCharges);
                    bool seen = false;
                    foreach (var o in l) if (Dist(o, c) < 40) { seen = true; break; }
                    if (!seen) l.Add(c);
                }
            return l;
        }
    }
}
