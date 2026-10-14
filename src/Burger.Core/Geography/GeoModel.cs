namespace Burger.Core.Geography;

/// <summary>How a property's value is carried across a split, merge or redraw.</summary>
public enum PropertyKind
{
    /// <summary>A label (owner, culture, name, state): the child takes the value covering most of its area.</summary>
    Categorical,
    /// <summary>A set of labels (claims, cores): the child gets every value of the parents it overlaps.</summary>
    Set,
    /// <summary>A quantity that adds up (population, buildings): split in proportion to area, never created or lost.</summary>
    Extensive,
    /// <summary>A rate or level (development, fertility): the child gets the area-weighted average.</summary>
    Intensive,
}

public sealed record PropertyDef(string Key, PropertyKind Kind, bool Integer = false);

/// <summary>
/// One playable province: a fixed set of geographic atoms (locations) that holds from <see cref="From"/> up to, not including, <see cref="To"/>.
/// A province never changes its footprint; a spatial change retires it and creates new provinces with new ids, so old records keep meaning what they meant.
/// </summary>
public sealed class GeoProvince
{
    public string Id = "";
    /// <summary>Atom keys (location keys), sorted ordinally. Never changes.</summary>
    public string[] Atoms = Array.Empty<string>();
    public int From = HistoricalCalendar.Beginning;
    /// <summary>First day the province no longer exists (<see cref="HistoricalCalendar.Forever"/> while it does).</summary>
    public int To = HistoricalCalendar.Forever;
    public string[] Parents = Array.Empty<string>();
    public List<string> Children = new();
    public string Reason = "";
    /// <summary>An island group: its atoms may be joined by straits instead of land borders.</summary>
    public bool Archipelago;
    public string Source = "";
    public bool ActiveOn(int day) => day >= From && day < To;
}

public sealed class GeoChildSpec
{
    public string Id = "";
    public string[] Atoms = Array.Empty<string>();
    /// <summary>Explicit fraction of a single parent's extensive quantities (splits only); null = by area of the atoms.</summary>
    public double? Share;
    public bool Archipelago;
}

/// <summary>A reorganization on one day: parents are retired and children created covering exactly the same atoms (split = 1:n, merge = n:1, redraw = n:m).</summary>
public sealed class GeoOperation
{
    public int Date;
    public string[] Parents = Array.Empty<string>();
    public List<GeoChildSpec> Children = new();
    public string Reason = "";
    public string Source = "";
    public string Kind => Parents.Length == 1 && Children.Count > 1 ? "split"
        : Parents.Length > 1 && Children.Count == 1 ? "merge" : "redraw";
}

public enum GeoFeatureKind { Pass, Port, Crossing, Strait, Corridor, Fortress, Other }

/// <summary>A strategic geographic feature (a pass, a port, a river crossing) tied to atoms, usable between two dates.</summary>
public sealed class GeoFeature
{
    public string Id = "";
    public GeoFeatureKind Kind;
    public string Name = "";
    public string[] Atoms = Array.Empty<string>();
    public int From = HistoricalCalendar.Beginning;
    public int To = HistoricalCalendar.Forever;
    public string Source = "";
    public bool ActiveOn(int day) => day >= From && day < To;
}

/// <summary>A density target for a group of provinces (grouped by a property, e.g. continent). Guidance for tooling, never a runtime rule.</summary>
public sealed class DensityTarget
{
    public string Group = "";
    public double Share;
    public double MinAreaKm2;
    public double MaxAreaKm2;
}

public enum GeoSeverity { Info, Warning, Error }

public readonly record struct GeoIssue(GeoSeverity Severity, string Message, string Where = "")
{
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()}: {Message}" + (Where.Length > 0 ? $" ({Where})" : "");
}
