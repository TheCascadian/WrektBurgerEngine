using Burger.Core.Script;

namespace Burger.Core.Geography;

/// <summary>The control levels a claim may state, from firmest to weakest knowledge.</summary>
public static class BorderControl
{
    public static readonly string[] Levels = { "core", "direct", "tributary", "claimed", "contested", "unknown" };
    public static bool IsValid(string c) => Array.IndexOf(Levels, c.ToLowerInvariant()) >= 0;
    /// <summary>Viewers hatch every level except <c>core</c> and <c>direct</c>.</summary>
    public static bool Hatched(string? c) => c is not ("core" or "direct");
}

/// <summary>One dated statement from a <c>history/borders/&lt;TAG&gt;.txt</c> file: a polity gains or loses areas.</summary>
public sealed class BorderClaim
{
    public string Tag = "";
    public int Day;
    public List<string> Add = new(), Remove = new();
    public string Control = "";
    public string Source = "";
    public string Note = "";
    public string Where = "";
}

/// <summary>Areas (named atom groups) and claims read from a project, in load order.</summary>
public sealed class BorderSet
{
    /// <summary>The source id that marks a terrain-driven placeholder rather than attested history.</summary>
    public const string Generated = "generated";

    public readonly Dictionary<string, string[]> Areas = new(StringComparer.Ordinal);
    public readonly List<string> AreaOrder = new();
    public readonly Dictionary<string, string> AreaWhere = new(StringComparer.Ordinal);
    /// <summary>Claims in file order, then block order inside the file.</summary>
    public readonly List<BorderClaim> Claims = new();
    public readonly List<GeoIssue> Issues = new();
    /// <summary>Ledger ids a claim may cite (status APPROVED). <c>generated</c> is always allowed.</summary>
    public HashSet<string> ApprovedSources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Words that must never appear in a source id (from the provenance denylist).</summary>
    public List<string> Denied = new();

    public void Error(string message, string where) => Issues.Add(new GeoIssue(GeoSeverity.Error, message, where));
}
