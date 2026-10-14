using Burger.Core.Data;
using Burger.Core.Model;

namespace Burger.Core.Geography;

/// <summary>
/// Checks the historical province layer. Structural checks need only the registry; geometric checks (atoms exist, connectivity, mixed land and
/// water, adjacency symmetry, area policy) run when a <see cref="World"/> is given.
/// </summary>
public static class GeoValidator
{
    /// <summary>A shared border shorter than this many pixels is a touching corner, not a crossing.</summary>
    public const int MinBorderPixels = 2;

    public static List<GeoIssue> Run(GeoRegistry reg, World? world = null, int? densityDay = null)
    {
        var issues = new List<GeoIssue>(reg.LoadIssues);
        if (!reg.Enabled) return issues;
        Structure(reg, issues);
        Temporal(reg, issues);
        if (world != null) Geometry(reg, world, issues);
        Density(reg, world, densityDay, issues);
        return issues;
    }

    private static void Structure(GeoRegistry reg, List<GeoIssue> issues)
    {
        foreach (var p in reg.AllProvinces)
        {
            if (p.Atoms.Length == 0) issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' has no atoms (orphan)", p.Source));
            if (p.To != HistoricalCalendar.Forever && p.Children.Count == 0)
                issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' is retired {HistoricalCalendar.Format(p.To)} without successors (territory lost)", p.Source));
            if (p.To <= p.From) issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' ends before it begins", p.Source));
            foreach (var par in p.Parents)
                if (reg.Get(par) == null) issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' names missing parent '{par}'", p.Source));
        }
        // overlap: an atom may be in two provinces only at different times
        var atoms = reg.AllProvinces.SelectMany(p => p.Atoms).Distinct(StringComparer.Ordinal);
        foreach (var a in atoms)
        {
            var list = reg.ProvincesOfAtom(a).OrderBy(p => p.From).ToList();
            for (int i = 1; i < list.Count; i++)
                if (list[i].From < list[i - 1].To)
                    issues.Add(new(GeoSeverity.Error, $"atom '{a}' is in both '{list[i - 1].Id}' and '{list[i].Id}' from {HistoricalCalendar.Format(list[i].From)} (overlap)", list[i].Source));
        }
        // each reorganization conserves territory (re-verified from the records)
        foreach (var p in reg.AllProvinces.Where(p => p.Parents.Length > 0))
        {
            var parents = p.Parents.Select(reg.Get).Where(x => x != null).Cast<GeoProvince>().ToList();
            var siblings = parents.SelectMany(x => x.Children).Distinct().Select(reg.Get).Where(x => x != null).Cast<GeoProvince>().Where(x => x.From == p.From).ToList();
            var pa = parents.SelectMany(x => x.Atoms).ToHashSet(StringComparer.Ordinal);
            var ca = siblings.SelectMany(x => x.Atoms).ToHashSet(StringComparer.Ordinal);
            if (!pa.SetEquals(ca)) issues.Add(new(GeoSeverity.Error, $"reorganization into '{p.Id}' does not conserve territory", p.Source));
        }
        foreach (var f in reg.Features)
            if (f.To <= f.From) issues.Add(new(GeoSeverity.Error, $"feature '{f.Id}' ends before it begins", f.Source));
    }

    private static void Temporal(GeoRegistry reg, List<GeoIssue> issues)
    {
        foreach (var (subject, prop, day, _, where) in reg.ExplicitValues)
        {
            var p = reg.Get(subject);
            if (p == null) continue; // atom-level track
            if (day < p.From) issues.Add(new(GeoSeverity.Error, $"{subject}.{prop} has a value on {HistoricalCalendar.Format(day)} before the province exists ({HistoricalCalendar.Format(p.From)})", where));
            if (day >= p.To) issues.Add(new(GeoSeverity.Error, $"{subject}.{prop} has a value on {HistoricalCalendar.Format(day)} after the province was retired ({HistoricalCalendar.Format(p.To)}); record it on a successor", where));
        }
    }

    private static void Geometry(GeoRegistry reg, World world, List<GeoIssue> issues)
    {
        var knownStates = world.States.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in reg.AllProvinces)
        {
            var locs = new List<Location>();
            foreach (var a in p.Atoms)
            {
                var l = world.GetLocation(a);
                if (l == null) issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' uses unknown location '{a}'", p.Source));
                else locs.Add(l);
            }
            if (locs.Count == 0) continue;
            int water = locs.Count(l => l.IsWater);
            if (water != 0 && water != locs.Count) issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' mixes land and water locations", p.Source));
            // connectivity through real borders (corner contacts do not count)
            var set = locs.ToHashSet();
            var seen = new HashSet<Location> { locs[0] };
            var queue = new Queue<Location>();
            queue.Enqueue(locs[0]);
            while (queue.Count > 0)
            {
                var l = queue.Dequeue();
                foreach (var adj in l.Neighbors)
                {
                    if (!set.Contains(adj.To) || seen.Contains(adj.To)) continue;
                    bool ok = adj.Kind == AdjacencyKind.Strait ? p.Archipelago : adj.BorderPixels >= MinBorderPixels;
                    if (ok) { seen.Add(adj.To); queue.Enqueue(adj.To); }
                }
            }
            if (seen.Count != set.Count)
                issues.Add(new(GeoSeverity.Error, $"province '{p.Id}' is not contiguous: {set.Count - seen.Count} of {set.Count} locations are cut off" + (p.Archipelago ? "" : " (set archipelago = yes for island groups)"), p.Source));
        }
        // adjacency symmetry for atoms in use
        foreach (var a in reg.AllProvinces.SelectMany(p => p.Atoms).Distinct(StringComparer.Ordinal))
        {
            var l = world.GetLocation(a);
            if (l == null) continue;
            foreach (var adj in l.Neighbors)
                if (adj.To.AdjacencyTo(l) == null)
                    issues.Add(new(GeoSeverity.Error, $"adjacency {l.Key} -> {adj.To.Key} has no reverse link"));
        }
        foreach (var (subject, prop, _, value, where) in reg.ExplicitValues)
            if (prop == "state" && !knownStates.Contains(value) && world.States.Count > 0)
                issues.Add(new(GeoSeverity.Warning, $"{subject}.state names '{value}', which is not a state in map/states", where));
        foreach (var f in reg.Features)
            foreach (var a in f.Atoms)
                if (world.GetLocation(a) == null) issues.Add(new(GeoSeverity.Error, $"feature '{f.Id}' uses unknown location '{a}'", f.Source));
        if (reg.RequireFullCoverage)
        {
            int missing = 0; string first = "";
            foreach (var l in world.Locations)
            {
                if (l.IsWater) continue;
                if (reg.ProvinceOfAtom(l.Key, reg.CoverageDay) == null) { if (missing++ == 0) first = l.Key; }
            }
            if (missing > 0) issues.Add(new(GeoSeverity.Error, $"{missing} land locations are not in any province on {HistoricalCalendar.Format(reg.CoverageDay)} (first: {first})"));
        }
    }

    private static void Density(GeoRegistry reg, World? world, int? day, List<GeoIssue> issues)
    {
        if (reg.DensityTargets.Count == 0 || world == null) return;
        foreach (var r in DensityReport(reg, world, day ?? reg.CoverageDay))
            foreach (var w in r.Warnings) issues.Add(new(GeoSeverity.Warning, w));
    }

    public sealed record DensityRow(string Group, int Provinces, double Share, double TargetShare, double AreaKm2, double MeanAreaKm2, List<string> Warnings);

    /// <summary>Province count and size per group (property <see cref="GeoRegistry.DensityProperty"/>) against the configured targets. Guidance only.</summary>
    public static List<DensityRow> DensityReport(GeoRegistry reg, World world, int day)
    {
        var areas = GeoMetrics.LocationAreasKm2(world);
        var snap = reg.Snapshot(day);
        var rows = new Dictionary<string, (int N, double Area, List<string> Small, List<string> Big)>(StringComparer.OrdinalIgnoreCase);
        var targets = reg.DensityTargets.ToDictionary(t => t.Group, StringComparer.OrdinalIgnoreCase);
        foreach (var p in snap.Provinces)
        {
            string group = reg.Value(p.Id, reg.DensityProperty, day) ?? "(none)";
            double area = p.Atoms.Sum(a => world.GetLocation(a) is { } l && l.Index < areas.Length ? areas[l.Index] : 0);
            if (!rows.TryGetValue(group, out var r)) rows[group] = r = (0, 0, new List<string>(), new List<string>());
            r.N++; r.Area += area;
            if (targets.TryGetValue(group, out var t))
            {
                if (t.MinAreaKm2 > 0 && area < t.MinAreaKm2) r.Small.Add(p.Id);
                if (t.MaxAreaKm2 > 0 && area > t.MaxAreaKm2) r.Big.Add(p.Id);
            }
            rows[group] = r;
        }
        int total = Math.Max(1, snap.Provinces.Count);
        var result = new List<DensityRow>();
        foreach (var (g, r) in rows.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var w = new List<string>();
            targets.TryGetValue(g, out var t);
            if (r.Small.Count > 0) w.Add($"density: {r.Small.Count} {g} province(s) are under {t!.MinAreaKm2:0} km2, e.g. {r.Small[0]} (below the minimum usable size)");
            if (r.Big.Count > 0) w.Add($"density: {r.Big.Count} {g} province(s) are over {t!.MaxAreaKm2:0} km2, e.g. {r.Big[0]}");
            result.Add(new DensityRow(g, r.N, r.N / (double)total, t?.Share ?? 0, r.Area, r.N == 0 ? 0 : r.Area / r.N, w));
        }
        return result;
    }
}
