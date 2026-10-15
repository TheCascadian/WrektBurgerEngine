using System.Globalization;
using System.Text;
using Burger.Core;
using Burger.Core.Data;
using Burger.Core.Geography;
using Burger.Core.Model;

namespace Burger.Tools;

/// <summary>
/// burger geo generate: builds <c>history/geography</c> for a project from its own map, states and starting data. Deterministic: the same project
/// and options give byte-identical files. Policy (how many provinces per continent) is the <see cref="GeoGenOptions"/>, never the engine.
/// </summary>
public sealed class GeoGenOptions
{
    /// <summary>Total playable provinces wanted (a target; tiny islands and one-location components can push the count up).</summary>
    public int Provinces = 3000;
    /// <summary>Start date of the generated provinces, in the engine's date text.</summary>
    public string Start = "500BCE.1.1";
    /// <summary>Initial tuning reference for the share of provinces per continent (guidance, not a rule).</summary>
    public Dictionary<string, double> Shares = new()
    {
        ["asia"] = 0.34, ["europe"] = 0.22, ["africa"] = 0.19, ["north_america"] = 0.11, ["south_america"] = 0.09, ["oceania"] = 0.05,
    };
    /// <summary>Writes 99_synthetic_test.txt: made-up reorganizations and owner changes that exercise the system. Never historical.</summary>
    public bool Synthetic;
    public int SyntheticSplits = 40, SyntheticMerges = 20, SyntheticRedraws = 10, SyntheticTransfers = 40;
    public double MinAreaKm2 = 500;
}

public sealed class GeoGenResult
{
    public int Provinces, Components, Features, SyntheticOps;
    public Dictionary<string, (int N, double Area)> Groups = new();
    public List<string> Files = new();
}

public static class GeoGenerator
{
    /// <summary>
    /// Coarse continent of a point, for density grouping only. A box approximation, not a sourced boundary dataset; a project that needs exact
    /// continents can replace the 'continent' track values without touching the engine.
    /// </summary>
    public static string Continent(double lon, double lat)
    {
        if (lon >= 165 || lon <= -140 && lat < 30) return "oceania";
        if (lat >= 63 && lat <= 67.5 && lon >= -25 && lon <= -13) return "europe";
        if (lon <= -30 && lat >= 7 || lon <= -10 && lat >= 58 && lon >= -75) return "north_america";
        if (lon <= -30) return "south_america";
        if (lon <= -120 && lat < 25) return "oceania";
        if (lon >= 140 && lat < 5 || lat < -10 && lon >= 112) return "oceania";
        if (lon >= -25 && lon <= 40 && lat >= 35 && !(lon >= 26 && lat < 42)) return "europe";
        if (lon > 40 && lon <= 60 && lat >= 50) return "europe";
        if (lon >= -20 && lon <= 34.5 && lat <= 37.5) return "africa";
        if (lon > 34.5 && lon <= 52 && lat < 12.5) return "africa";
        if (lon >= 42 && lon <= 51 && lat < -10) return "africa";
        return "asia";
    }

    private static double D2(Location a, Location b) => (a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (a.CenterY - b.CenterY) * (a.CenterY - b.CenterY);

    public static bool RealBorder(Adjacency a) => a.Kind == AdjacencyKind.Land && a.To.IsLand && a.BorderPixels >= GeoValidator.MinBorderPixels;

    /// <summary>Connected components of the given land locations through real borders (corner contacts do not connect).</summary>
    public static List<List<Location>> Components(IEnumerable<Location> locs)
    {
        var set = locs.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        var inSet = new HashSet<Location>(set);
        var seen = new HashSet<Location>();
        var result = new List<List<Location>>();
        foreach (var start in set)
        {
            if (!seen.Add(start)) continue;
            var comp = new List<Location> { start };
            var q = new Queue<Location>(); q.Enqueue(start);
            while (q.Count > 0)
            {
                var l = q.Dequeue();
                foreach (var a in l.Neighbors)
                    if (RealBorder(a) && inSet.Contains(a.To) && seen.Add(a.To)) { comp.Add(a.To); q.Enqueue(a.To); }
            }
            comp.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            result.Add(comp);
        }
        return result;
    }

    /// <summary>Splits a connected set into k contiguous, roughly equal-area parts: farthest-point seeds, then the smallest part keeps growing.</summary>
    public static List<List<Location>> Partition(List<Location> comp, int k, double[] area, int salt = 0)
    {
        if (k <= 1 || comp.Count <= 1) return new() { comp };
        k = Math.Min(k, comp.Count);
        var ordered = comp.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        var seeds = new List<Location> { ordered[(salt & int.MaxValue) % ordered.Count] };
        var minD = ordered.Select(l => D2(l, seeds[0])).ToArray();
        while (seeds.Count < k)
        {
            int best = 0; double bd = -1;
            for (int i = 0; i < ordered.Count; i++) if (minD[i] > bd) { bd = minD[i]; best = i; }
            seeds.Add(ordered[best]);
            for (int i = 0; i < ordered.Count; i++) minD[i] = Math.Min(minD[i], D2(ordered[i], ordered[best]));
        }
        var set = new HashSet<Location>(ordered);
        var owner = new Dictionary<Location, int>();
        var regions = new List<List<Location>>();
        var regionArea = new double[k];
        var frontier = new PriorityQueue<Location, (double, int)>[k];
        var turn = new PriorityQueue<int, (double, int)>();
        void Claim(int r, Location l)
        {
            owner[l] = r; regions[r].Add(l); regionArea[r] += Math.Max(area[l.Index], 1e-6);
            foreach (var a in l.Neighbors)
                if (RealBorder(a) && set.Contains(a.To) && !owner.ContainsKey(a.To)) frontier[r].Enqueue(a.To, (D2(a.To, seeds[r]), a.To.Index));
        }
        for (int r = 0; r < k; r++) { regions.Add(new()); frontier[r] = new(); }
        for (int r = 0; r < k; r++) Claim(r, seeds[r]);
        for (int r = 0; r < k; r++) turn.Enqueue(r, (regionArea[r], r));
        while (turn.Count > 0)
        {
            int r = turn.Dequeue();
            Location? next = null;
            while (frontier[r].Count > 0) { var c = frontier[r].Dequeue(); if (!owner.ContainsKey(c)) { next = c; break; } }
            if (next == null) continue;
            Claim(r, next);
            turn.Enqueue(r, (regionArea[r], r));
        }
        foreach (var r in regions) r.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
        return regions.Where(r => r.Count > 0).ToList();
    }

    private static string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Atoms(IEnumerable<Location> ls) => "{ " + string.Join(" ", ls.Select(l => l.Key)) + " }";

    private sealed class Prov
    {
        public string Id = "", Name = "", Continent = "", State = "", Owner = "";
        public List<Location> Locs = new();
        public double Area;
        public long Pop;
        public string[] Cores = Array.Empty<string>(), Claims = Array.Empty<string>();
        public bool Touched;
    }

    public static GeoGenResult Generate(Game game, string outDir, GeoGenOptions opt)
    {
        var world = game.World;
        var map = world.Map!;
        var areas = GeoMetrics.LocationAreasKm2(world);
        double Lon(Location l) => l.CenterX / map.Width * 360.0 - 180.0;
        double Lat(Location l) => map.Settings.NorthLat - (map.Settings.NorthLat - map.Settings.SouthLat) * l.CenterY / map.Height;
        string ContinentOf(IEnumerable<Location> ls)
        {
            double w = 0, sx = 0, sy = 0;
            foreach (var l in ls) { double a = Math.Max(areas[l.Index], 1e-6); w += a; sx += Lon(l) * a; sy += Lat(l) * a; }
            return Continent(sx / w, sy / w);
        }

        // ---- units to partition: the land of each state, split into its connected pieces
        var units = new List<(string State, State? S, List<Location> Comp, double Area, string Group)>();
        var byState = world.Locations.Where(l => l.IsLand).GroupBy(l => l.State?.Key ?? "unassigned").OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var g in byState)
        {
            var st = g.First().State;
            foreach (var comp in Components(g))
            {
                double a = comp.Sum(l => areas[l.Index]);
                var groupArea = comp.GroupBy(l => Continent(Lon(l), Lat(l))).Select(x => (x.Key, A: x.Sum(l => areas[l.Index]))).OrderByDescending(x => x.A).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
                units.Add((g.Key, st, comp, a, groupArea));
            }
        }

        // ---- density policy: area per province for each continent, tuned so the counts approach the target shares
        var groups = units.Select(u => u.Group).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        double shareSum = groups.Sum(g => opt.Shares.GetValueOrDefault(g, 0.02));
        var target = groups.ToDictionary(g => g, g => opt.Shares.GetValueOrDefault(g, 0.02) / shareSum * opt.Provinces);
        var landArea = groups.ToDictionary(g => g, g => units.Where(u => u.Group == g).Sum(u => u.Area));
        var perProv = groups.ToDictionary(g => g, g => Math.Max(landArea[g] / Math.Max(1, target[g]), 1.0));
        int K(double area, string g, int n) => Math.Clamp((int)Math.Round(area / perProv[g]), 1, n);
        for (int it = 0; it < 12; it++)
            foreach (var g in groups)
            {
                double count = units.Where(u => u.Group == g).Sum(u => K(u.Area, g, u.Comp.Count));
                perProv[g] = Math.Clamp(perProv[g] * Math.Pow(count / Math.Max(1, target[g]), 0.7), 50, 5e6);
            }

        // ---- partition
        var provs = new List<Prov>();
        foreach (var grp in units.GroupBy(u => u.State).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var st = grp.First().S;
            var parts = new List<List<Location>>();
            foreach (var u in grp) parts.AddRange(Partition(u.Comp, K(u.Area, u.Group, u.Comp.Count), areas));
            parts = parts.OrderBy(p => p[0].Key, StringComparer.Ordinal).ToList();
            double stateArea = parts.Sum(p => p.Sum(l => areas[l.Index]));
            long total = 0;
            if (st != null) { try { total = (long)Math.Round(game.Population.Of(st).Total); } catch { total = 0; } }
            string baseName = st?.DisplayName ?? "Unassigned land";
            var quota = parts.Select(p => total * p.Sum(l => areas[l.Index]) / Math.Max(stateArea, 1e-9)).ToArray();
            var share = quota.Select(x => (long)Math.Floor(x)).ToArray();
            long rest = total - share.Sum();
            foreach (int i in Enumerable.Range(0, parts.Count).OrderByDescending(i => quota[i] - share[i]).ThenBy(i => i).Take((int)rest)) share[i]++;
            for (int i = 0; i < parts.Count; i++)
            {
                provs.Add(new Prov
                {
                    Id = $"{grp.Key}_p{i + 1:00}", Name = parts.Count > 1 ? $"{baseName} ({i + 1})" : baseName, State = grp.Key,
                    Locs = parts[i], Area = parts[i].Sum(l => areas[l.Index]), Pop = share[i], Continent = ContinentOf(parts[i]),
                    Owner = st?.Owner?.Tag ?? "",
                    Cores = st?.Cores.Select(c => c.Tag).OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
                    Claims = st?.Claims.Select(c => c.Tag).OrderBy(x => x, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
                });
            }
        }

        var res = new GeoGenResult { Provinces = provs.Count, Components = units.Count };
        foreach (var g in provs.GroupBy(p => p.Continent)) res.Groups[g.Key] = (g.Count(), g.Sum(p => p.Area));
        Directory.CreateDirectory(outDir);
        foreach (var old in Directory.GetFiles(outDir, "*.txt")) File.Delete(old);

        // ---- 00_provinces.txt
        var sb = new StringBuilder();
        sb.AppendLine("# GENERATED by 'burger geo generate'. Do not edit by hand; change the options and regenerate.");
        sb.AppendLine("# Provinces are contiguous groups of locations inside one state piece (a state's land split by real borders). Locations stay the atoms.");
        sb.AppendLine("# Sources: map/provinces.png and map/states (political start), history/pops (design estimates). Continent = coarse box approximation for density grouping only.");
        sb.AppendLine($"# {provs.Count} provinces. Province count targets (guidance): " + string.Join(", ", groups.Select(g => $"{g} {target[g]:0}")));
        sb.AppendLine($"settings = {{ coverage = full coverage_date = {opt.Start} density_property = continent }}");
        foreach (var g in groups)
        {
            double mean = perProv[g];
            sb.AppendLine($"density = {{ group = {g} share = {N(opt.Shares.GetValueOrDefault(g, 0.02) / shareSum)} min_area_km2 = {N(opt.MinAreaKm2)} max_area_km2 = {N(Math.Round(mean * 8))} }}");
        }
        sb.AppendLine("property = { key = population kind = extensive integer = yes }");
        sb.AppendLine("property = { key = cores kind = set }");
        sb.AppendLine("property = { key = claims kind = set }");
        sb.AppendLine("property = { key = continent kind = categorical }");
        foreach (var p in provs) sb.AppendLine($"province = {{ id = {p.Id} atoms = {Atoms(p.Locs)} from = {opt.Start} }}");
        foreach (var p in provs)
        {
            sb.AppendLine($"track = {{ subject = {p.Id} property = name {opt.Start} = {Q(p.Name)} }}");
            if (p.State != "unassigned") sb.AppendLine($"track = {{ subject = {p.Id} property = state {opt.Start} = {p.State} }}");
            sb.AppendLine($"track = {{ subject = {p.Id} property = continent {opt.Start} = {p.Continent} }}");
            if (p.Owner.Length > 0)
            {
                sb.AppendLine($"track = {{ subject = {p.Id} property = owner {opt.Start} = {p.Owner} }}");
                sb.AppendLine($"track = {{ subject = {p.Id} property = controller {opt.Start} = {p.Owner} }}");
            }
            sb.AppendLine($"track = {{ subject = {p.Id} property = population {opt.Start} = {p.Pop} }}");
            if (p.Cores.Length > 0) sb.AppendLine($"track = {{ subject = {p.Id} property = cores {opt.Start} = {{ {string.Join(" ", p.Cores)} }} }}");
            if (p.Claims.Length > 0) sb.AppendLine($"track = {{ subject = {p.Id} property = claims {opt.Start} = {{ {string.Join(" ", p.Claims)} }} }}");
        }
        File.WriteAllText(Path.Combine(outDir, "00_provinces.txt"), sb.ToString());
        res.Files.Add("00_provinces.txt");

        // ---- 01_features.txt: physical features read from the map (straits and river crossings between provinces)
        var provOf = new Dictionary<string, Prov>(StringComparer.Ordinal);
        foreach (var p in provs) foreach (var l in p.Locs) provOf[l.Key] = p;
        sb.Clear();
        sb.AppendLine("# GENERATED. Physical features derived from the map: strait links from adjacencies, and river borders between two provinces (rivers.png).");
        sb.AppendLine("# Mountain passes, ports and historical corridors need sourced data and are not generated.");
        int fn = 0;
        foreach (var l in world.Locations.Where(l => l.IsLand).OrderBy(l => l.Key, StringComparer.Ordinal))
            foreach (var a in l.Neighbors.OrderBy(a => a.To.Key, StringComparer.Ordinal))
            {
                if (string.CompareOrdinal(l.Key, a.To.Key) >= 0 || !a.To.IsLand) continue;
                if (a.Kind == AdjacencyKind.Strait)
                { sb.AppendLine($"feature = {{ id = strait_{++fn:00000} kind = strait atoms = {{ {l.Key} {a.To.Key} }} }}"); }
                else if (a.Kind == AdjacencyKind.Land && a.River && a.BorderPixels >= GeoValidator.MinBorderPixels
                         && provOf.TryGetValue(l.Key, out var pa) && provOf.TryGetValue(a.To.Key, out var pb) && pa != pb)
                { sb.AppendLine($"feature = {{ id = crossing_{++fn:00000} kind = crossing atoms = {{ {l.Key} {a.To.Key} }} }}"); }
            }
        res.Features = fn;
        if (fn > 0) { File.WriteAllText(Path.Combine(outDir, "01_features.txt"), sb.ToString()); res.Files.Add("01_features.txt"); }

        if (opt.Synthetic) res.SyntheticOps = WriteSynthetic(outDir, provs, provOf, world, areas, opt, res);

        File.WriteAllText(Path.Combine(outDir, "PROVENANCE.md"),
            "# Provenance of history/geography\n\n" +
            "Generated by `burger geo generate`. Province geometry is derived from this project's own map and states; owners, cores, claims and populations are copied from its starting data " +
            "(the World500BCE populations are design estimates, not census facts). Continents are a coarse box approximation used only for density grouping.\n\n" +
            (opt.Synthetic ? "`99_synthetic_test.txt` is SYNTHETIC: invented splits, merges, redraws and owner changes that exercise the engine. It is not history and must not ship in a scenario.\n" : "No synthetic test data was written.\n") +
            "No historical border, name or population after the start date has been invented in the other files.\n");
        return res;
    }

    private static int WriteSynthetic(string outDir, List<Prov> provs, Dictionary<string, Prov> provOf, World world, double[] areas, GeoGenOptions opt, GeoGenResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SYNTHETIC TEST DATA - NOT HISTORY. Generated to exercise splits, merges, redraws, BCE/CE crossings and owner changes.");
        int ops = 0;
        var untouched = provs.Where(p => p.Locs.Count >= 2).OrderByDescending(p => p.Area).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        string[] splitDates = { "200BCE.1.1", "1CE.1.1", "500CE.1.1", "1200.1.1" };
        var used = new HashSet<string>(StringComparer.Ordinal);
        // splits: spread over the largest provinces
        int step = Math.Max(1, untouched.Count / Math.Max(1, opt.SyntheticSplits * 2));
        int n = 0;
        for (int i = 0; i < untouched.Count && n < opt.SyntheticSplits; i += step)
        {
            var p = untouched[i];
            if (p.Locs.Count < 2 || used.Contains(p.Id)) continue;
            var parts = Partition(p.Locs, 2, areas);
            if (parts.Count != 2) continue;
            used.Add(p.Id); n++; ops++;
            string date = splitDates[n % splitDates.Length];
            sb.AppendLine($"split = {{ date = {date} parent = {p.Id} reason = {Q("SYNTHETIC test split")}");
            sb.AppendLine($"  child = {{ id = syn_split_{n:000}_a atoms = {Atoms(parts[0])} }}");
            sb.AppendLine($"  child = {{ id = syn_split_{n:000}_b atoms = {Atoms(parts[1])} }} }}");
            sb.AppendLine($"track = {{ subject = syn_split_{n:000}_a property = name {date} = {Q(p.Name + " North")} }}");
        }
        // merges: adjacent untouched pairs inside one state
        int m = 0;
        foreach (var p in provs.Where(p => !used.Contains(p.Id)).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (m >= opt.SyntheticMerges) break;
            if (used.Contains(p.Id)) continue;
            var q = NeighborsOf(p, provOf).Where(x => x.State == p.State && !used.Contains(x.Id) && x != p).OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (q == null) continue;
            used.Add(p.Id); used.Add(q.Id); m++; ops++;
            sb.AppendLine($"merge = {{ date = 800CE.1.1 parents = {{ {p.Id} {q.Id} }} reason = {Q("SYNTHETIC test merge")} into = {{ id = syn_merge_{m:000} atoms = {Atoms(p.Locs.Concat(q.Locs))} }} }}");
        }
        // redraws: re-cut two adjacent untouched provinces of one state
        int rd = 0;
        foreach (var p in provs.Where(p => !used.Contains(p.Id) && p.Locs.Count >= 3).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (rd >= opt.SyntheticRedraws) break;
            if (used.Contains(p.Id)) continue;
            var q = NeighborsOf(p, provOf).Where(x => x.State == p.State && !used.Contains(x.Id) && x.Locs.Count >= 2 && x != p).OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (q == null) continue;
            var union = p.Locs.Concat(q.Locs).ToList();
            List<List<Location>>? cut = null;
            foreach (int salt in Enumerable.Range(1, 6))
            {
                var c = Partition(union, 2, areas, salt * 7 + 3);
                if (c.Count == 2 && !SameSet(c[0], p.Locs) && !SameSet(c[0], q.Locs)) { cut = c; break; }
            }
            if (cut == null) continue;
            used.Add(p.Id); used.Add(q.Id); rd++; ops++;
            sb.AppendLine($"reorganize = {{ date = 1500.1.1 parents = {{ {p.Id} {q.Id} }} reason = {Q("SYNTHETIC test redraw")}");
            sb.AppendLine($"  child = {{ id = syn_redraw_{rd:000}_a atoms = {Atoms(cut[0])} }}");
            sb.AppendLine($"  child = {{ id = syn_redraw_{rd:000}_b atoms = {Atoms(cut[1])} }} }}");
        }
        // owner changes: a province is occupied, then ceded to a neighbor's owner across the BCE/CE boundary
        int t = 0;
        foreach (var p in provs.Where(p => !used.Contains(p.Id) && p.Owner.Length > 0).OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (t >= opt.SyntheticTransfers) break;
            var q = NeighborsOf(p, provOf).Where(x => x.Owner.Length > 0 && x.Owner != p.Owner).OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (q == null) continue;
            used.Add(p.Id); t++;
            sb.AppendLine($"track = {{ subject = {p.Id} property = controller 20BCE.1.1 = {q.Owner} }}");
            sb.AppendLine($"track = {{ subject = {p.Id} property = owner 1CE.1.1 = {q.Owner} }}");
            sb.AppendLine($"track = {{ subject = {p.Id} property = controller 1CE.1.1 = {q.Owner} }}");
        }
        File.WriteAllText(Path.Combine(outDir, "99_synthetic_test.txt"), sb.ToString());
        res.Files.Add("99_synthetic_test.txt");
        return ops + t;
    }

    private static bool SameSet(List<Location> a, List<Location> b) => a.Count == b.Count && a.Select(x => x.Key).ToHashSet().SetEquals(b.Select(x => x.Key));

    private static IEnumerable<Prov> NeighborsOf(Prov p, Dictionary<string, Prov> provOf)
    {
        var seen = new HashSet<Prov>();
        foreach (var l in p.Locs)
            foreach (var a in l.Neighbors)
                if (RealBorder(a) && provOf.TryGetValue(a.To.Key, out var q) && q != p && seen.Add(q)) yield return q;
    }
}
