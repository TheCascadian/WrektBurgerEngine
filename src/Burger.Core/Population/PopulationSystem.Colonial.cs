using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Data;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Population;

/// <summary>
/// A colonial region (common/colonial_regions/*.txt): land that is settled as a unit. Colonists settle there under a per-region limit; once an
/// overlord holds enough colonized states in it, they form a colonial nation: a subject that pays the region's tax share.
/// </summary>
public sealed class ColonialRegion
{
    public string Key = "";
    public string Name = "";
    /// <summary>States named one by one, and states whose key starts with one of the prefixes.</summary>
    public List<string> StateKeys = new();
    public List<string> Prefixes = new();
    /// <summary>Most colonists one country can have settling in the region at once (0 = the country's own limit).</summary>
    public int MaxColonies;
    /// <summary>Share (0-1) of its income the colonial nation pays its overlord.</summary>
    public double TaxShare = 0.15;
    public List<State> States = new();
}

public sealed partial class PopulationSystem
{
    public readonly List<ColonialRegion> ColonialRegions = new();
    private Dictionary<State, ColonialRegion>? _regionOf;

    /// <summary>A state was divided: the people split by area (whole people by largest remainder), the base values likewise; the new state starts empty of settlers.</summary>
    public void OnStateSplit(State parent, State child, double share)
    {
        _neighbours.Clear(); _regionOf = null; _home.Clear(); InvalidateMajorities();
        if (!_states.TryGetValue(parent, out var pp)) return;
        var cp = new StatePopulation { OwnedSinceDay = pp.OwnedSinceDay, Owner = pp.Owner };
        foreach (var pop in pp.Pops)
        {
            int part = StateSplitSystem.SplitInt((int)Math.Round(pop.Size), share);
            if (part > 0) cp.Pops.Add(new Pop { Culture = pop.Culture, Religion = pop.Religion, Type = pop.Type, Size = part });
            pop.Size = Math.Max(0, Math.Round(pop.Size) - part);
        }
        pp.Pops.RemoveAll(p => p.Size <= 0);
        cp.InitialTotal = pp.InitialTotal * share; pp.InitialTotal -= cp.InitialTotal;
        cp.BaseTax = pp.BaseTax * share; pp.BaseTax -= cp.BaseTax;
        cp.BaseManpower = pp.BaseManpower * share; pp.BaseManpower -= cp.BaseManpower;
        _states[child] = cp;
        ApplyOutput(parent, pp);
        ApplyOutput(child, cp);
    }

    public void OnStateUnsplit(State parent, State child)
    {
        _neighbours.Clear(); _regionOf = null; _home.Clear(); InvalidateMajorities();
        if (_states.Remove(child, out var cp) && _states.TryGetValue(parent, out var pp))
            foreach (var pop in cp.Pops)
            {
                var same = pp.Pops.FirstOrDefault(x => x.Same(pop.Culture, pop.Religion, pop.Type));
                if (same != null) same.Size += pop.Size; else pp.Pops.Add(pop);
            }
    }

    private void LoadColonialRegions(GameData data)
    {
        ColonialRegions.Clear();
        _regionOf = null;
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/colonial_regions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var r = new ColonialRegion
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    MaxColonies = n.GetInt("max_colonies", 0),
                    TaxShare = Math.Clamp(n.GetDouble("tax_share", 0.15), 0, 1),
                };
                foreach (var x in n.Get("states")?.All ?? Enumerable.Empty<Node>()) r.StateKeys.Add(x.Value ?? x.Key);
                foreach (var x in n.Get("prefixes")?.All ?? Enumerable.Empty<Node>()) r.Prefixes.Add(x.Value ?? x.Key);
                ColonialRegions.Add(r);
            }
    }

    /// <summary>The colonial region a state lies in (null = none).</summary>
    public ColonialRegion? RegionOf(State s)
    {
        if (_regionOf == null)
        {
            _regionOf = new Dictionary<State, ColonialRegion>();
            foreach (var r in ColonialRegions)
            {
                r.States.Clear();
                foreach (var s2 in _game.World.States)
                    if (!_regionOf.ContainsKey(s2) && (r.StateKeys.Contains(s2.Key, StringComparer.OrdinalIgnoreCase)
                        || r.Prefixes.Any(p => s2.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
                    { _regionOf[s2] = r; r.States.Add(s2); }
            }
        }
        return _regionOf.GetValueOrDefault(s);
    }

    /// <summary>Colonists the country has settling in a region (null = states outside every region).</summary>
    public int ColoniesInRegion(Country c, ColonialRegion? region) => ColoniesOf(c).Count(s => RegionOf(s) == region);

    /// <summary>How many colonists the country can have in one region at once: its own limit, or the region's if that is lower.</summary>
    public int MaxColoniesIn(Country c, ColonialRegion? region)
        => region is { MaxColonies: > 0 } ? Math.Min(MaxColonies(c), region.MaxColonies) : MaxColonies(c);

    public bool IsColonialNation(Country c) => _game.World.Relations.SubjectTypeOf(c) is { } t && t.Key.StartsWith("colonial_nation", StringComparison.OrdinalIgnoreCase);

    /// <summary>The region a colonial nation grew from (its capital's).</summary>
    public ColonialRegion? RegionOfNation(Country c) => c.Capital?.State is { } s ? RegionOf(s) : null;

    /// <summary>After a colony is founded: enough colonized states of one overlord in a region become (or join) its colonial nation.</summary>
    private void CheckColonialNation(Country overlord, State founded)
    {
        if (RegionOf(founded) is not { } region) return;
        if (IsColonialNation(overlord) && RegionOfNation(overlord) == region) return; // a nation's own colonies simply stay its states
        int need = (int)_game.Data.Defines.Get("colonial_nation_states", 5);
        var mine = overlord.OwnedStates.Where(s => s.Category == "colony" && RegionOf(s) == region).ToList();
        var rel = _game.World.Relations;
        var nation = rel.SubjectsOf(overlord).FirstOrDefault(x => x.Exists && IsColonialNation(x) && RegionOfNation(x) == region);
        if (nation == null)
        {
            if (mine.Count < need) return;
            nation = CreateColonialNation(overlord, region, mine);
        }
        else
            foreach (var s in mine) _game.Diplomacy.TransferState(s, nation);
        _home.Remove(overlord);
        _home.Remove(nation);
        _countryModsDirty = true;
    }

    private string FreeColonialTag()
    {
        for (char a = 'R'; a <= 'W'; a++)
            for (int i = 0; i < 100; i++)
            {
                string t = $"{a}{i:00}";
                if (_game.World.GetCountry(t) == null) return t;
            }
        return FreeIndependenceTag();
    }

    private Country CreateColonialNation(Country overlord, ColonialRegion region, List<State> states)
    {
        var w = _game.World;
        string tag = FreeColonialTag();
        string name = $"{region.Name} Colonies";
        if (w.Countries.Any(x => x.Exists && x.Name == name)) name = $"{overlord.Name} {region.Name} Colonies";
        // the overlord's colors, lightened, and its flag
        var nation = w.CreateCountry(tag, name, overlord.DefaultColor.Lerp(new Rgb(235, 235, 235), 0.3), overlord.FlagPath, "colonial_government");
        nation.Playable = false;
        var op = Of(overlord);
        _countries[nation] = new CountryPopulation { Culture = op.Culture, Religion = op.Religion };
        foreach (var s in states) _game.Diplomacy.TransferState(s, nation);
        var cap = states.MaxBy(TotalOf)!;
        nation.Capital = cap.Locations.Where(l => l.IsLand && l.IsPassable).OrderBy(l => l.Key).FirstOrDefault() ?? cap.Locations[0];
        nation.Gold = _game.Data.Defines.Get("colonial_nation_gold", 50);
        if (_game.Politics.Enabled)
        {
            var np = _game.Politics.Of(nation);
            foreach (var kv in _game.Politics.Of(overlord).Popularity) np.Popularity[kv.Key] = kv.Value;
        }
        // one subject type per region, paying the region's tax share
        string typeKey = "colonial_nation_" + region.Key;
        var types = _game.Data.SubjectTypes;
        if (!types.TryGetValue(typeKey, out var type))
        {
            types.TryGetValue("colonial_nation", out var baseType);
            type = new SubjectType
            {
                Key = typeKey, Name = baseType?.Name ?? "Colonial Nation", IncomeShare = region.TaxShare, JoinsOverlordWars = baseType?.JoinsOverlordWars ?? true,
                CanDeclareWar = baseType?.CanDeclareWar ?? false, CountsAsRelation = baseType?.CountsAsRelation ?? false,
                LibertyDesire = baseType?.LibertyDesire ?? -20, Government = baseType?.Government ?? "imposed", ManpowerShare = baseType?.ManpowerShare ?? 0,
                Definition = baseType?.Definition ?? Node.Root(""),
            };
            types[typeKey] = type;
        }
        _game.Diplomacy.MakeSubject(nation, overlord, type);
        ColonialNationsFormed++;
        _game.Notify($"The colonies of {overlord.Name} in {region.Name} became a colonial nation: {nation.Name}.", overlord.IsPlayer ? NotificationKind.Good : NotificationKind.Info);
        _game.Events.FireOnAction("on_colonial_nation_formed", nation, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["overlord"] = overlord });
        _game.RaiseMapChanged();
        return nation;
    }

    public int ColonialNationsFormed;
}
