using Burger.Core.Map;
using Burger.Core.Model;

namespace Burger.Core.Data;

/// <summary>Everything that changes during a game: ownership, characters, armies, wars, the date...</summary>
public sealed class World
{
    public MapData Map = null!;
    public GameDate Date;

    public readonly List<Location> Locations = new();
    public readonly Dictionary<string, Location> LocationByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<int, Location> LocationByColor = new();

    public readonly List<State> States = new();
    public readonly Dictionary<string, State> StateByKey = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<Country> Countries = new();
    public readonly Dictionary<string, Country> CountryByTag = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<Character> Characters = new();
    public readonly Dictionary<string, Character> CharacterByKey = new(StringComparer.OrdinalIgnoreCase);
    public int NextCharacterId = 1;

    /// <summary>Data-defined and runtime-created scopes, separate from gameplay relationships.</summary>
    public readonly List<GenericScope> GenericScopes = new();
    public readonly Dictionary<string, GenericScope> GenericScopeByKey = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Explicit organizational links among scopes; ownership and authority are not inferred from it.</summary>
    public ScopeHierarchy ScopeHierarchy { get; } = new();

    public readonly List<Division> Divisions = new();
    public readonly List<Army> Armies = new();
    public readonly List<Battle> Battles = new();
    public readonly List<War> Wars = new();
    public int NextDivisionId = 1, NextArmyId = 1, NextBattleId = 1, NextWarId = 1;

    public readonly Diplomacy.Relations Relations = new();
    public readonly VarStore Global = new();
    public readonly HashSet<string> FiredOnceEvents = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Last day each country used each diplomatic action, for cooldowns. Key: "TAG:action".</summary>
    public readonly Dictionary<string, int> ActionCooldowns = new(StringComparer.OrdinalIgnoreCase);

    public Country? Player;

    public Country? GetCountry(string? tag) => tag != null && CountryByTag.TryGetValue(tag, out var c) ? c : null;

    /// <summary>Creates a country during the game (it has no land until states are transferred to it).</summary>
    public Country CreateCountry(string tag, string name, Rgb color, string flagPath, string government)
    {
        if (CountryByTag.ContainsKey(tag)) throw new InvalidOperationException($"Tag {tag} already exists");
        var c = new Country { Tag = tag, Index = Countries.Count, Government = government, Dynamic = true, Gold = 0, Manpower = 0 };
        c.DefaultName = name;
        c.Name = name;
        c.Color = c.DefaultColor = color;
        c.DefaultFlagPath = c.FlagPath = flagPath;
        Countries.Add(c);
        CountryByTag[tag] = c;
        return c;
    }
    public State? GetState(string? key) => key != null && StateByKey.TryGetValue(key, out var s) ? s : null;
    public Location? GetLocation(string? key) => key != null && LocationByKey.TryGetValue(key, out var l) ? l : null;
    public Character? GetCharacter(string? key) => key != null && CharacterByKey.TryGetValue(key, out var c) ? c : null;

    public GenericScope? GetGenericScope(string? key) => key != null && GenericScopeByKey.TryGetValue(key, out var s) ? s : null;

    /// <summary>Looks up an existing entity with a stable typed reference such as <c>country:GER</c> or <c>generic:org</c>.</summary>
    public IScope? ResolveScopeReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        int colon = reference.IndexOf(':');
        if (colon <= 0) return Country.IsValidTag(reference) ? GetCountry(reference) : null;
        string prefix = reference[..colon].ToLowerInvariant();
        string key = reference[(colon + 1)..];
        return prefix switch
        {
            "c" or "country" or "tag" => GetCountry(key),
            "s" or "state" => GetState(key),
            "l" or "location" or "loc" or "p" or "province" => GetLocation(key),
            "char" or "character" => GetCharacter(key),
            "generic" or "generic_scope" or "scope_id" => GetGenericScope(key),
            "war" => Wars.FirstOrDefault(w => string.Equals(w.Id.ToString(), key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.ScopeKey, key, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
    }

    /// <summary>Registers all currently existing world entities as hierarchy nodes without adding inferred links.</summary>
    public void RegisterScopesInHierarchy()
    {
        foreach (var scope in Countries.Cast<IScope>().Concat(Characters).Concat(States).Concat(Locations).Concat(Wars).Concat(GenericScopes))
            ScopeHierarchy.Register(scope);
    }

    /// <summary>Creates one generic scope, preserving its entity identity and rejecting duplicate identifiers.</summary>
    public bool TryCreateGenericScope(string key, string kind, string name, bool dynamic, out GenericScope? scope, out string error)
    {
        scope = null;
        if (string.IsNullOrWhiteSpace(key)) { error = "A generic scope needs a non-empty id"; return false; }
        if (GenericScopeByKey.ContainsKey(key)) { error = $"Generic scope id '{key}' is already defined"; return false; }
        scope = new GenericScope { Key = key, Kind = string.IsNullOrWhiteSpace(kind) ? "generic" : kind, Name = name, Dynamic = dynamic };
        GenericScopeByKey.Add(key, scope);
        GenericScopes.Add(scope);
        ScopeHierarchy.Register(scope);
        error = "";
        return true;
    }

    public Location? LocationAtPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Map.Width || y >= Map.Height) return null;
        int idx = Map.PixelLocation[y * Map.Width + x];
        return idx >= 0 ? Locations[idx] : null;
    }

    public IEnumerable<Character> LivingCharacters => Characters.Where(c => c.Alive);
    public IEnumerable<Character> CharactersOf(Country c) => Characters.Where(ch => ch.Alive && ch.Country == c);
    public IEnumerable<Division> DivisionsOf(Country c) => Divisions.Where(d => d.Owner == c);
    public IEnumerable<Army> ArmiesOf(Country c) => Armies.Where(a => a.Owner == c);
    public IEnumerable<War> WarsOf(Country c) => Wars.Where(w => w.Involves(c));

    public bool AtWar(Country a, Country b)
    {
        foreach (var w in Wars)
            if ((w.IsAttacker(a) && w.IsDefender(b)) || (w.IsDefender(a) && w.IsAttacker(b))) return true;
        return false;
    }

    public War? WarBetween(Country a, Country b)
    {
        foreach (var w in Wars)
            if ((w.IsAttacker(a) && w.IsDefender(b)) || (w.IsDefender(a) && w.IsAttacker(b))) return w;
        return null;
    }

    /// <summary>Rebuilds <see cref="Country.OwnedStates"/> caches. Call after changing state owners.</summary>
    public void RefreshOwnership()
    {
        foreach (var c in Countries) c.OwnedStates.Clear();
        foreach (var s in States) s.Owner?.OwnedStates.Add(s);
    }
}
