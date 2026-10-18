using Burger.Core.Data;
using Burger.Core.Model;

namespace Burger.Core.Geography;

/// <summary>Percent of land weight on one date, by how well the claims back it.</summary>
public readonly record struct BorderCoverage(int Day, double AttestedPct, double GeneratedPct, double UnknownPct);

public static class BordersValidator
{
    public static List<GeoIssue> Run(BorderSet set, BorderTimeline tl, World? world = null)
    {
        var issues = new List<GeoIssue>(set.Issues);
        issues.AddRange(tl.Issues);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in set.AreaOrder)
            foreach (var a in set.Areas[name])
            {
                if (seen.TryGetValue(a, out var other) && other != name)
                    issues.Add(new(GeoSeverity.Error, $"atom '{a}' is in both area '{other}' and area '{name}'; areas must not overlap", set.AreaWhere[name]));
                seen[a] = name;
                if (world != null && world.GetLocation(a) == null)
                    issues.Add(new(GeoSeverity.Error, $"area '{name}' uses unknown location '{a}'", set.AreaWhere[name]));
            }
        foreach (var c in set.Claims)
        {
            if (c.Source.Length == 0) issues.Add(new(GeoSeverity.Error, $"{c.Tag} claim has no source", c.Where));
            foreach (var a in c.Add.Concat(c.Remove))
                if (!set.Areas.ContainsKey(a)) issues.Add(new(GeoSeverity.Error, $"claim names '{a}', which is in no area", c.Where));
        }
        if (world != null)
        {
            int loose = world.Locations.Count(l => !l.IsWater && !seen.ContainsKey(l.Key));
            if (loose > 0) issues.Add(new(GeoSeverity.Warning, $"{loose} land locations are in no area"));
        }
        return issues;
    }

    /// <summary>
    /// Coverage on a date. <paramref name="weight"/> gives an atom's area (see <see cref="GeoMetrics"/>); <paramref name="landAtoms"/> lists every land atom,
    /// so land that no area covers counts as unknown. Without it only atoms inside areas are counted.
    /// </summary>
    public static BorderCoverage Coverage(BorderSet set, BorderTimeline tl, int day, Func<string, double> weight, IEnumerable<string>? landAtoms = null)
    {
        double attested = 0, generated = 0, unknown = 0;
        var inArea = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in set.AreaOrder)
        {
            var e = tl.At(name, day);
            double w = set.Areas[name].Sum(weight);
            foreach (var a in set.Areas[name]) inArea.Add(a);
            if (e == null || e.Owner == BorderTimeline.None) unknown += w;
            else if (string.Equals(e.Source, BorderSet.Generated, StringComparison.OrdinalIgnoreCase)) generated += w;
            else attested += w;
        }
        if (landAtoms != null) foreach (var a in landAtoms) if (!inArea.Contains(a)) unknown += weight(a);
        double total = attested + generated + unknown;
        if (total <= 0) return new BorderCoverage(day, 0, 0, 0);
        return new BorderCoverage(day, 100 * attested / total, 100 * generated / total, 100 * unknown / total);
    }
}
