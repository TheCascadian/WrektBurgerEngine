namespace Burger.Core;

public enum FlagKind { Nation, Party }

/// <summary>
/// Everything the generator knows about who a flag is for. Only <see cref="Key"/> and <see cref="Color"/> are required;
/// the rest narrows the design (tradition, political family, period) or ties it to related identities (predecessor, shared motifs).
/// </summary>
public sealed record FlagIdentity
{
    public FlagIdentity(string key, Rgb color, FlagKind kind = FlagKind.Nation) { Key = key; Color = color; Kind = kind; }

    /// <summary>Tag or party key; seeds the design, so it is stable between sessions.</summary>
    public string Key { get; init; }
    /// <summary>The identity's own colour (country map colour or party colour). It is always the primary tincture.</summary>
    public Rgb Color { get; init; }
    public FlagKind Kind { get; init; }
    /// <summary>Key of a registered <see cref="FlagTradition"/> (regional or cultural design habits), e.g. "nordic". Unknown names are ignored.</summary>
    public string? Tradition { get; init; }
    /// <summary>Key of a registered <see cref="FlagPoliticalFamily"/> (e.g. "socialist", "liberal"), mostly for parties.</summary>
    public string? Family { get; init; }
    /// <summary>The year the flag is for; gates period-specific emblems and shifts style (heraldic before ~1850, geometric after ~1920).</summary>
    public int? Year { get; init; }
    /// <summary>Extra colours the identity wants to use (a party's secondary colour, a nation's traditional colours).</summary>
    public IReadOnlyList<Rgb>? Accents { get; init; }
    /// <summary>The earlier state, party or movement. The new flag keeps its family, palette and some motifs but is varied.</summary>
    public FlagIdentity? Predecessor { get; init; }
    /// <summary>Emblem or ordinary names to prefer (for example inherited from a parent movement).</summary>
    public IReadOnlyList<string>? Motifs { get; init; }
    /// <summary>Unrelated identities in the same scene (rival parties, neighbours); the flag avoids looking like them.</summary>
    public IReadOnlyList<FlagIdentity>? Distinct { get; init; }
}

/// <summary>Regional or cultural design habits as data. Weights multiply the family weights; nothing here is a claim about history.</summary>
public sealed record FlagTradition(string Name)
{
    /// <summary>Design family name to weight, e.g. "cross" 3.</summary>
    public IReadOnlyDictionary<string, double> FamilyWeights { get; init; } = new Dictionary<string, double>();
    /// <summary>Colours typical for the tradition, tried before generic ones.</summary>
    public IReadOnlyList<Rgb> Palette { get; init; } = Array.Empty<Rgb>();
    /// <summary>Emblem tags the tradition favours.</summary>
    public IReadOnlyList<string> EmblemTags { get; init; } = Array.Empty<string>();
    /// <summary>Emblems to avoid entirely.</summary>
    public IReadOnlyList<string> ExcludedEmblems { get; init; } = Array.Empty<string>();
    /// <summary>Probability that a banded design runs vertically (0.5 when unset).</summary>
    public double VerticalBias { get; init; } = 0.5;
}

/// <summary>A political family as data: which emblems and design families suit it.</summary>
public sealed record FlagPoliticalFamily(string Name)
{
    public IReadOnlyDictionary<string, double> FamilyWeights { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<string> EmblemTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PreferredEmblems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Rgb> Palette { get; init; } = Array.Empty<Rgb>();
}

/// <summary>Registry of traditions and political families. Register custom ones at startup, before generating flags.</summary>
public static class FlagTraditions
{
    private static readonly Dictionary<string, FlagTradition> _traditions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, FlagPoliticalFamily> _families = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(FlagTradition t) { lock (_traditions) _traditions[t.Name] = t; }
    public static void Register(FlagPoliticalFamily f) { lock (_families) _families[f.Name] = f; }
    public static FlagTradition? Tradition(string? name) => name != null && _traditions.TryGetValue(name, out var t) ? t : null;
    public static FlagPoliticalFamily? Family(string? name) => name != null && _families.TryGetValue(name, out var f) ? f : null;

    private static Dictionary<string, double> W(params (string, double)[] p) => p.ToDictionary(x => x.Item1, x => x.Item2);

    static FlagTraditions()
    {
        var white = FlagKit.White; var black = FlagKit.Black;
        Register(new FlagTradition("nordic") { FamilyWeights = W(("cross", 6), ("tricolour", 0.4), ("canton", 0.2), ("chevron", 0.1)), Palette = new[] { FlagKit.Blue, FlagKit.Red, white, FlagKit.Gold } });
        Register(new FlagTradition("western_european") { FamilyWeights = W(("tricolour", 2.5), ("heraldic", 1.4), ("bicolour", 1), ("cross", 0.8)), Palette = new[] { FlagKit.Blue, white, FlagKit.Red, FlagKit.Gold }, EmblemTags = new[] { "heraldic" }, VerticalBias = 0.6 });
        Register(new FlagTradition("slavic") { FamilyWeights = W(("tricolour", 3), ("bicolour", 1.5), ("heraldic", 1.2), ("chevron", 1)), Palette = new[] { white, FlagKit.Blue, FlagKit.Red }, EmblemTags = new[] { "heraldic" }, VerticalBias = 0.15 });
        Register(new FlagTradition("anglo") { FamilyWeights = W(("canton", 4), ("cross", 1.5), ("saltire", 1.2), ("tricolour", 0.5)), Palette = new[] { FlagKit.Blue, FlagKit.Red, white }, EmblemTags = new[] { "cosmic" } });
        Register(new FlagTradition("islamic") { FamilyWeights = W(("tricolour", 2), ("chevron", 2), ("emblematic", 1.5), ("bicolour", 1)), Palette = new[] { FlagKit.Green, white, black, FlagKit.Red }, EmblemTags = new[] { "religious" }, ExcludedEmblems = new[] { "cross_pattee", "cross_greek", "fleur", "eagle" }, VerticalBias = 0.15 });
        Register(new FlagTradition("east_asian") { FamilyWeights = W(("emblematic", 4), ("bicolour", 1), ("geometric", 0.8), ("tricolour", 0.5)), Palette = new[] { white, FlagKit.Red, FlagKit.Blue, black, FlagKit.Gold }, EmblemTags = new[] { "cosmic" } });
        Register(new FlagTradition("latin_american") { FamilyWeights = W(("tricolour", 3), ("emblematic", 1.5), ("heraldic", 1.3), ("chevron", 1), ("bicolour", 1)), Palette = new[] { FlagKit.Sky, white, FlagKit.Gold, FlagKit.Red, FlagKit.Green }, EmblemTags = new[] { "cosmic", "national" } });
        Register(new FlagTradition("african") { FamilyWeights = W(("tricolour", 3), ("chevron", 2), ("geometric", 1.2), ("emblematic", 1.2)), Palette = new[] { FlagKit.Green, FlagKit.Gold, FlagKit.Red, black }, EmblemTags = new[] { "cosmic" } });
        Register(new FlagTradition("imperial") { FamilyWeights = W(("heraldic", 4), ("bicolour", 1), ("tricolour", 1), ("cross", 0.6)), Palette = new[] { black, FlagKit.Gold, white, FlagKit.Red }, EmblemTags = new[] { "heraldic", "monarchic" } });

        Register(new FlagPoliticalFamily("socialist") { FamilyWeights = W(("emblematic", 2.5), ("bicolour", 1.5), ("geometric", 1), ("tricolour", 0.6), ("heraldic", 0.1)), EmblemTags = new[] { "socialist" }, PreferredEmblems = new[] { "rose", "cog", "star" }, Palette = new[] { FlagKit.Red, white, black } });
        Register(new FlagPoliticalFamily("communist") { FamilyWeights = W(("emblematic", 3), ("bicolour", 1.5), ("canton", 0.8), ("heraldic", 0.1)), EmblemTags = new[] { "socialist", "communist" }, PreferredEmblems = new[] { "hammer_sickle", "star", "cog" }, Palette = new[] { FlagKit.Red, FlagKit.Gold, white } });
        Register(new FlagPoliticalFamily("liberal") { FamilyWeights = W(("tricolour", 2), ("bicolour", 1.5), ("emblematic", 1.5), ("geometric", 1)), EmblemTags = new[] { "liberal" }, PreferredEmblems = new[] { "flame", "star", "sunburst" }, Palette = new[] { FlagKit.Gold, FlagKit.Blue, FlagKit.Orange, white } });
        Register(new FlagPoliticalFamily("conservative") { FamilyWeights = W(("heraldic", 3), ("cross", 1.5), ("tricolour", 1.5), ("bicolour", 1)), EmblemTags = new[] { "heraldic" }, PreferredEmblems = new[] { "crown", "castle", "cross_pattee" }, Palette = new[] { FlagKit.Blue, white, FlagKit.Red } });
        Register(new FlagPoliticalFamily("agrarian") { FamilyWeights = W(("emblematic", 2), ("bicolour", 2), ("tricolour", 1.5), ("cross", 0.5)), EmblemTags = new[] { "agrarian" }, PreferredEmblems = new[] { "wheat", "sunburst", "wheel" }, Palette = new[] { FlagKit.Green, FlagKit.Gold, white } });
        Register(new FlagPoliticalFamily("nationalist") { FamilyWeights = W(("emblematic", 2), ("geometric", 1.5), ("tricolour", 1.5), ("heraldic", 1.2)), EmblemTags = new[] { "nationalist" }, PreferredEmblems = new[] { "eagle", "sword", "flame" }, Palette = new[] { black, FlagKit.Red, white } });
        Register(new FlagPoliticalFamily("monarchist") { FamilyWeights = W(("heraldic", 4), ("bicolour", 1), ("cross", 1)), EmblemTags = new[] { "monarchic", "heraldic" }, PreferredEmblems = new[] { "crown", "fleur", "eagle" }, Palette = new[] { FlagKit.Gold, black, white, FlagKit.Red } });
        Register(new FlagPoliticalFamily("clerical") { FamilyWeights = W(("cross", 3), ("heraldic", 2), ("bicolour", 1)), EmblemTags = new[] { "religious" }, PreferredEmblems = new[] { "cross_pattee", "cross_greek", "crescent" }, Palette = new[] { white, FlagKit.Gold, FlagKit.Blue } });
    }
}
