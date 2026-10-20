using System.Globalization;
using System.Text.RegularExpressions;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Characters;

public enum TitleTier { County, Duchy, Kingdom, Empire }

/// <summary>A title in the hierarchy: a county (a state), a duchy (a region of states), a kingdom or an empire.</summary>
public sealed class Title
{
    public string Key = "", Name = "";
    public TitleTier Tier;
    /// <summary>The state a county is.</summary>
    public State? State;
    /// <summary>The state the title is named after and held from.</summary>
    public State? Capital;
    public Title? Parent;
    public List<Title> Children = new();
    /// <summary>A duchy, kingdom or empire has been created (a county always is).</summary>
    public bool Created;
    /// <summary>Was it created at the start of the game (saves keep only the changes).</summary>
    public bool StartCreated;
    /// <summary>The country a title belonged to at the start of the game (its de jure realm).</summary>
    public string StartOwner = "";

    /// <summary>Every county below this title (or the county itself).</summary>
    public IEnumerable<Title> CountiesBelow() => Tier == TitleTier.County ? new[] { this } : Children.SelectMany(c => c.CountiesBelow());
    public IEnumerable<State> StatesBelow() => CountiesBelow().Where(c => c.State != null).Select(c => c.State!);
}

/// <summary>A governor of a duchy and since when.</summary>
public sealed class Governorship
{
    public Country Country = null!;
    public Title Duchy = null!;
    public Character Governor = null!;
    public int Since;
}

/// <summary>
/// Titles and courts (titles feature). Every state is a county; counties make duchies (regions) and duchies kingdoms (the realms of the start of the game), and
/// explicit titles in <c>common/titles</c> add empires and more. A county is held by the ruler or by a vassal, who keeps a share of its tax and levies by contract and has an
/// opinion of the ruler; vassals that dislike the ruler plot. A ruler may create a duchy of which they hold every county, appoints a council (cabinet posts with council = yes)
/// and one governor for each duchy.
/// </summary>
public sealed class TitleSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, Title> Titles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<State, Title> _county = new();
    private readonly Dictionary<State, Character> _vassalOf = new();
    public readonly List<Governorship> Governors = new();
    private readonly List<Node> _explicit = new();
    private bool _built;
    private readonly Dictionary<State, (float X, float Y)> _centres = new();

    /// <summary>Counts for reports.</summary>
    public int Granted, Revoked, Created, Breakaways;

    public TitleSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "titles", Write = Save, Read = Load, Reset = () => { _vassalOf.Clear(); Governors.Clear(); foreach (var t in Titles.Values.Where(t => t.Tier != TitleTier.County)) t.Created = t.StartCreated; } });
        game.Diplomacy.CostFactors.Add((a, actor, recipient) => Enabled && a.Tags.Contains("fabricate") && recipient is Country rc && HoldsDeJureOver(actor, rc) ? 1 - _game.Data.Defines.Get("de_jure_claim_discount", 0.5) : 1);
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Titles);

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        _explicit.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/titles"))
            foreach (var n in root.All)
                if (n.Children != null) _explicit.Add(n);
    }

    private static string RegionOf(string key) => Regex.Replace(key, @"_\d+$", "");

    /// <summary>Builds the tree from the world at the start of the game: counties from states, duchies from regions, kingdoms from the realms of the start, then the explicit titles.</summary>
    public void Build()
    {
        Titles.Clear(); _county.Clear(); _vassalOf.Clear(); Governors.Clear(); _built = true;
        if (!Enabled) return;
        foreach (var s in _game.World.States)
        {
            var t = new Title { Key = s.Key, Name = s.DisplayName, Tier = TitleTier.County, State = s, Capital = s, Created = true, StartOwner = s.Owner?.Tag ?? "" };
            Titles[t.Key] = t;
            _county[s] = t;
        }
        // duchies: the states of one region
        foreach (var grp in _game.World.States.GroupBy(s => RegionOf(s.Key)))
        {
            var states = grp.ToList();
            var d = new Title { Key = "d_" + grp.Key, Name = Localisation.GetOrPretty(grp.Key), Tier = TitleTier.Duchy, Capital = states.MaxBy(s => s.Tax) ?? states[0] };
            d.StartOwner = states.Where(s => s.Owner != null).GroupBy(s => s.Owner!.Tag).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
            foreach (var s in states) { var c = _county[s]; c.Parent = d; d.Children.Add(c); }
            d.Created = states.All(s => s.Owner != null && s.Owner.Tag == d.StartOwner);
            d.StartCreated = d.Created;
            Titles[d.Key] = d;
        }
        // kingdoms: the countries of the start, from the duchies most of whose states were theirs
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            var duchies = Titles.Values.Where(t => t.Tier == TitleTier.Duchy && t.StartOwner == c.Tag).ToList();
            if (duchies.Count == 0) continue;
            var k = new Title { Key = "k_" + c.Tag, Name = c.Name, Tier = TitleTier.Kingdom, StartOwner = c.Tag, Created = true, StartCreated = true };
            k.Capital = c.Capital?.State ?? duchies.MaxBy(d => d.Capital!.Tax)!.Capital;
            foreach (var d in duchies) { d.Parent = k; k.Children.Add(d); }
            Titles[k.Key] = k;
        }
        // explicit titles
        foreach (var n in _explicit)
        {
            if (!Enum.TryParse<TitleTier>(n.GetString("tier", "kingdom"), true, out var tier)) { Log.Error($"Title '{n.Key}': unknown tier", n.Location); continue; }
            if (tier == TitleTier.County) { Log.Error($"Title '{n.Key}': counties are the states, they are not defined here", n.Location); continue; }
            if (!Titles.TryGetValue(n.Key, out var t))
                Titles[n.Key] = t = new Title { Key = n.Key, Tier = tier };
            t.Tier = tier;
            t.Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key);
            if (n.GetString("capital") is { } cap && _game.World.GetState(cap) is { } cs) t.Capital = cs;
            if (n.GetString("de_jure_parent") is { } pk && Titles.TryGetValue(pk, out var parent) && parent != t) { t.Parent?.Children.Remove(t); t.Parent = parent; parent.Children.Add(t); }
            foreach (var childKey in n.GetList("parts")) if (Titles.TryGetValue(childKey, out var ch) && ch != t) { ch.Parent?.Children.Remove(ch); ch.Parent = t; t.Children.Add(ch); }
            if (n.GetString("holder") is { } holderTag && _game.World.GetCountry(holderTag) is { } hc) { t.Created = true; t.StartOwner = hc.Tag; t.StartCreated = true; t.Capital ??= hc.Capital?.State; }
        }
    }

    /// <summary>A state was divided: the new state is a county of its parent's duchy.</summary>
    public void OnStateSplit(State parent, State child, double share)
    {
        if (!_built || !_county.TryGetValue(parent, out var pc)) return;
        var t = new Title { Key = child.Key, Name = child.DisplayName, Tier = TitleTier.County, State = child, Capital = child, Created = true, StartOwner = pc.StartOwner, Parent = pc.Parent };
        Titles[t.Key] = t;
        _county[child] = t;
        pc.Parent?.Children.Add(t);
    }

    /// <summary>States were fused into others: their counties go, and a capital that was one of them moves to the state it joined.</summary>
    public void OnStatesFused(HashSet<State> gone)
    {
        if (!_built) return;
        foreach (var o in gone)
        {
            if (!_county.Remove(o, out var t)) continue;
            Titles.Remove(t.Key);
            t.Parent?.Children.Remove(t);
            _vassalOf.Remove(o);
        }
        foreach (var t in Titles.Values)
            if (t.Capital != null && gone.Contains(t.Capital) && _game.World.StateByKey.TryGetValue(t.Capital.Key, out var keep)) t.Capital = keep;
    }

    public void OnStateUnsplit(State parent, State child)
    {
        if (!_county.Remove(child, out var t)) return;
        Titles.Remove(t.Key);
        t.Parent?.Children.Remove(t);
        _vassalOf.Remove(child);
    }

    private void EnsureBuilt() { if (!_built) Build(); }

    // ------------------------------------------------------------------------------------- the tree
    public Title? CountyOf(State s) { EnsureBuilt(); return _county.GetValueOrDefault(s); }
    public Title? DuchyOf(State s) => CountyOf(s)?.Parent;
    public Title? KingdomOf(State s) => DuchyOf(s)?.Parent;
    public IEnumerable<Title> TitlesOfTier(TitleTier tier) { EnsureBuilt(); return Titles.Values.Where(t => t.Tier == tier); }

    /// <summary>Who holds a title now: a county the vassal or the ruler of its owner; a higher title the holder of its capital county, once it has been created.</summary>
    public Character? HolderOf(Title t)
    {
        EnsureBuilt();
        if (t.Tier == TitleTier.County) return t.State == null ? null : HolderOf(t.State);
        if (!t.Created || t.Capital == null) return null;
        return t.Capital.Owner?.Ruler;
    }

    public Character? HolderOf(State s) => _vassalOf.TryGetValue(s, out var v) && v.Alive && v.Country == s.Owner ? v : s.Owner?.Ruler;
    public Character? VassalOf(State s) => _vassalOf.TryGetValue(s, out var v) && v.Alive && v.Country == s.Owner ? v : null;
    public bool IsVassalHeld(State s) => VassalOf(s) != null;

    /// <summary>The titles a character holds: counties as a vassal or ruler, and the higher titles whose capital they hold.</summary>
    public IEnumerable<Title> TitlesHeldBy(Character ch)
    {
        EnsureBuilt();
        if (ch.Country == null) yield break;
        foreach (var s in ch.Country.OwnedStates)
            if (HolderOf(s) == ch && _county.TryGetValue(s, out var c)) yield return c;
        if (ch.IsRuler)
            foreach (var t in Titles.Values.Where(t => t.Tier != TitleTier.County && t.Created && t.Capital?.Owner == ch.Country)) yield return t;
    }

    public IEnumerable<State> StatesOfVassal(Character v) => v.Country == null ? Enumerable.Empty<State>() : v.Country.OwnedStates.Where(s => VassalOf(s) == v);

    /// <summary>The vassals of a country and the counties each holds.</summary>
    public List<(Character Vassal, List<State> States)> VassalsOf(Country c)
        => c.OwnedStates.Where(IsVassalHeld).GroupBy(s => VassalOf(s)!).Select(g => (g.Key, g.OrderBy(s => s.Key, StringComparer.Ordinal).ToList())).OrderByDescending(x => x.Item2.Count).ToList();

    /// <summary>Counties the ruler holds directly.</summary>
    public IEnumerable<State> DirectStates(Country c) => c.OwnedStates.Where(s => !IsVassalHeld(s));

    // ------------------------------------------------------------------------------------- de jure
    /// <summary>The holder has a title over land that belongs to another country: de jure claims against that country cost less.</summary>
    public bool HoldsDeJureOver(Country holder, Country other)
    {
        EnsureBuilt();
        foreach (var s in other.OwnedStates)
        {
            var d = DuchyOf(s);
            if (d == null) continue;
            if (d.Created && d.Capital?.Owner == holder) return true;
            if (d.Parent is { Created: true } k && k.Capital?.Owner == holder) return true;
        }
        return false;
    }

    public string? CannotCreate(Country c, Title t)
    {
        EnsureBuilt();
        if (!Enabled) return "Titles are not used in this game";
        if (t.Created) return "Already created";
        if (t.Tier == TitleTier.County) return "A county always exists";
        if (c.Ruler == null) return "No ruler";
        if (t.Tier == TitleTier.Duchy)
        {
            if (!t.StatesBelow().All(s => s.Owner == c)) return "Needs every county of the duchy";
        }
        else if (!t.Children.All(ch => ch.Created && ch.Capital?.Owner == c)) return $"Needs every {(t.Tier == TitleTier.Kingdom ? "duchy" : "kingdom")} of it, created and held";
        if (_game.Politics.Enabled && !_game.Politics.CanAfford(c, CreateCost)) return $"Needs {CreateCost:0} {_game.Politics.PowerName}";
        return null;
    }

    public double CreateCost => _game.Data.Defines.Get("title_create_cost", 50);

    public string? Create(Country c, Title t)
    {
        var why = CannotCreate(c, t);
        if (why != null) return why;
        Pay(c, CreateCost);
        t.Created = true;
        Created++;
        _game.NotifyPlayer($"{c.Ruler!.DisplayName} creates the {t.Tier.ToString().ToLowerInvariant()} of {t.Name}.", NotificationKind.Good, c);
        return null;
    }

    private void Pay(Country c, double cost)
    {
        if (cost > 0 && _game.Politics.Enabled && _game.Politics.PowerResource is { } r) _game.Resources.Set(c, r, _game.Resources.Get(c, r) - cost);
    }

    // ------------------------------------------------------------------------------------- vassals
    public double TaxShare(Character v) => v.Vars.Get("vassal_tax", 0.2);
    public double LevyShare(Character v) => v.Vars.Get("vassal_levy", 0.2);
    public double GrantCost => _game.Data.Defines.Get("title_grant_cost", 25);
    public double RevokeCost => _game.Data.Defines.Get("title_revoke_cost", 50);

    public string? CannotGrant(Country c, State s, Character ch)
    {
        EnsureBuilt();
        if (!Enabled) return "Titles are not used in this game";
        if (s.Owner != c) return "Not our county";
        if (IsVassalHeld(s)) return "Already held by a vassal";
        if (!ch.Alive || ch.Country != c) return "Not one of our characters";
        if (ch.IsRuler || c.Regent == ch) return "The ruler holds the realm";
        if (ch.Age(_game.World.Date) < _game.Data.Defines.AdultAge) return "Too young";
        if (s == c.Capital?.State) return "The capital stays with the ruler";
        if (_game.Politics.Enabled && !_game.Politics.CanAfford(c, GrantCost)) return $"Needs {GrantCost:0} {_game.Politics.PowerName}";
        return null;
    }

    /// <summary>Gives a county to a character: they keep 100% less the shares of tax and levy (10-30%) the contract gives the ruler.</summary>
    public string? Grant(Country c, State s, Character ch, double tax = 0.2, double levy = 0.2)
    {
        var why = CannotGrant(c, s, ch);
        if (why != null) return why;
        Pay(c, GrantCost);
        ch.Vars.Set("vassal_tax", Math.Clamp(tax, 0.1, 0.3));
        ch.Vars.Set("vassal_levy", Math.Clamp(levy, 0.1, 0.3));
        _vassalOf[s] = ch;
        ch.Loyalty = Math.Clamp(ch.Loyalty + 10, 0, 100);
        Granted++;
        return null;
    }

    public string? CannotRevoke(Country c, State s)
    {
        if (!Enabled) return "Titles are not used in this game";
        if (!IsVassalHeld(s) || s.Owner != c) return "Not held by a vassal";
        if (_game.Politics.Enabled && !_game.Politics.CanAfford(c, RevokeCost)) return $"Needs {RevokeCost:0} {_game.Politics.PowerName}";
        return null;
    }

    /// <summary>Takes a county back: its vassal loses 30 loyalty and the other vassals 5.</summary>
    public string? Revoke(Country c, State s)
    {
        var why = CannotRevoke(c, s);
        if (why != null) return why;
        var v = VassalOf(s)!;
        Pay(c, RevokeCost);
        _vassalOf.Remove(s);
        v.Loyalty = Math.Clamp(v.Loyalty - 30, 0, 100);
        foreach (var (other, _) in VassalsOf(c)) if (other != v) other.Loyalty = Math.Clamp(other.Loyalty - 5, 0, 100);
        Revoked++;
        return null;
    }

    /// <summary>What holding land does to a character's loyalty: vassals are content with more counties and a light contract.</summary>
    public double LoyaltyAdjust(Character ch)
    {
        if (ch.Country == null || _vassalOf.Count == 0) return 0;
        int n = StatesOfVassal(ch).Count();
        if (n == 0) return 0;
        return 5 + 3 * Math.Min(3, n) - (TaxShare(ch) - 0.2) * 50 - (LevyShare(ch) - 0.2) * 50;
    }

    /// <summary>State modifiers: a vassal-held county pays the ruler only its share of tax and levies; a governor adds 1% of tax per point of skill.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not State s || (_vassalOf.Count == 0 && Governors.Count == 0)) return 0;
        double v = 0;
        bool tax = key.Equals("tax", StringComparison.OrdinalIgnoreCase), men = key.Equals("manpower", StringComparison.OrdinalIgnoreCase);
        if (!tax && !men) return 0;
        if (VassalOf(s) is { } vas) v -= 1 - (tax ? TaxShare(vas) : LevyShare(vas));
        if (tax && s.Owner != null && DuchyOf(s) is { } duchy && Governors.FirstOrDefault(g => g.Country == s.Owner && g.Duchy == duchy) is { } gov && gov.Governor.Alive)
            v += gov.Governor.GetStat("finesse") * _game.Data.Defines.Get("governor_tax", 0.01);
        return v;
    }

    // ------------------------------------------------------------------------------------- governors
    public Governorship? GovernorOf(Country c, Title duchy) => Governors.FirstOrDefault(g => g.Country == c && g.Duchy == duchy && g.Governor.Alive);
    public double GovernorCost => _game.Data.Defines.Get("governor_cost", 50);

    /// <summary>The duchies a country has land in (one governor each).</summary>
    public List<Title> RegionsOf(Country c)
        => c.OwnedStates.Select(DuchyOf).Where(d => d != null).Cast<Title>().Distinct().OrderBy(d => d.Key, StringComparer.Ordinal).ToList();

    public string? CannotAppointGovernor(Country c, Title duchy, Character ch)
    {
        EnsureBuilt();
        if (!Enabled) return "Titles are not used in this game";
        if (duchy.Tier != TitleTier.Duchy || !duchy.StatesBelow().Any(s => s.Owner == c)) return "We have no land there";
        if (!ch.Alive || ch.Country != c || ch.IsRuler) return "Not a character who can govern";
        if (ch.Age(_game.World.Date) < _game.Data.Defines.AdultAge) return "Too young";
        if (Governors.Any(g => g.Governor == ch && g.Country == c && g.Duchy != duchy)) return "Governs another region";
        if (GovernorOf(c, duchy)?.Governor == ch) return "Already governs it";
        if (_game.Politics.Enabled && !_game.Politics.CanAfford(c, GovernorCost)) return $"Needs {GovernorCost:0} {_game.Politics.PowerName}";
        return null;
    }

    public string? AppointGovernor(Country c, Title duchy, Character ch)
    {
        var why = CannotAppointGovernor(c, duchy, ch);
        if (why != null) return why;
        Pay(c, GovernorCost);
        Governors.RemoveAll(g => g.Country == c && g.Duchy == duchy);
        Governors.Add(new Governorship { Country = c, Duchy = duchy, Governor = ch, Since = _game.World.Date.TotalDays });
        return null;
    }

    /// <summary>Fills the empty regions of a country with its ablest stewards, free of cost (at the start of a game, and every month for the AI).</summary>
    public void AutoGovernors(Country c)
    {
        EnsureBuilt();
        if (!c.Exists || c.Ruler == null) return;
        foreach (var duchy in RegionsOf(c))
        {
            if (GovernorOf(c, duchy) != null) continue;
            var pick = _game.World.CharactersOf(c).Where(ch => CannotAppointGovernorFree(c, duchy, ch) == null).OrderByDescending(ch => ch.GetStat("finesse")).FirstOrDefault();
            if (pick != null) Governors.Add(new Governorship { Country = c, Duchy = duchy, Governor = pick, Since = _game.World.Date.TotalDays });
        }
    }

    private string? CannotAppointGovernorFree(Country c, Title duchy, Character ch)
    {
        if (!ch.Alive || ch.Country != c || ch.IsRuler || c.Regent == ch || ch.IsHeir) return "no";
        if (ch.Age(_game.World.Date) < _game.Data.Defines.AdultAge) return "young";
        if (Governors.Any(g => g.Governor == ch && g.Country == c)) return "busy";
        return null;
    }

    public void OnGameStart()
    {
        if (!Enabled) return;
        foreach (var c in _game.World.Countries.Where(c => c.Exists)) AutoGovernors(c);
    }

    public void DismissGovernor(Country c, Title duchy) => Governors.RemoveAll(g => g.Country == c && g.Duchy == duchy);

    // ------------------------------------------------------------------------------------- monthly
    private (float X, float Y) CentreOf(State s)
    {
        if (_centres.TryGetValue(s, out var c)) return c;
        var land = s.Locations.Where(l => l.IsLand).ToList();
        if (land.Count == 0) land = s.Locations;
        return _centres[s] = land.Count == 0 ? (0, 0) : (land.Average(l => l.CenterX), land.Average(l => l.CenterY));
    }

    private double DistanceKm(Country c, State s)
    {
        if (c.Capital == null) return 0;
        var (x, y) = CentreOf(s);
        double dx = x - c.Capital.CenterX, dy = y - c.Capital.CenterY;
        return Math.Sqrt(dx * dx + dy * dy) * _game.Data.Defines.KmPerPixel;
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        EnsureBuilt();
        var rng = _game.Rngs.Characters;
        var now = _game.World.Date;
        foreach (var kv in _vassalOf.Where(kv => !kv.Value.Alive || kv.Key.Owner == null || kv.Value.Country != kv.Key.Owner).ToList()) _vassalOf.Remove(kv.Key);
        Governors.RemoveAll(g => !g.Governor.Alive || g.Governor.Country != g.Country || !g.Country.Exists);
        foreach (var c in _game.World.Countries.Where(c => c.Exists && c != _game.World.Player)) AutoGovernors(c);
        // a governor who stays too long grows too fond of the region: -1 loyalty a year after five years
        foreach (var g in Governors)
            if ((now.TotalDays - g.Since) / 365.0 > _game.Data.Defines.Get("governor_years", 5)) g.Governor.Loyalty = Math.Clamp(g.Governor.Loyalty - 1 / 12.0, 0, 100);
        // vassals that think ill of the ruler plot to break away
        if (_game.Intrigue.Enabled)
            foreach (var c in _game.World.Countries.Where(c => c.Exists && c.Ruler != null).ToList())
            {
                var unhappy = VassalsOf(c).Where(v => _game.Intrigue.OpinionOfRuler(v.Vassal) < _game.Data.Defines.Get("vassal_plot_opinion", -50)).Select(v => v.Vassal).ToList();
                if (unhappy.Count == 0) continue;
                var plot = _game.Intrigue.PlotOf(c, "independence");
                foreach (var v in unhappy)
                {
                    if (rng.NextDouble() > _game.Data.Defines.Get("vassal_plot_chance", 0.1) || _game.Intrigue.Plots.Any(p => p.Members.Contains(v))) continue;
                    if (plot == null) plot = _game.Intrigue.StartPlot(c, v, "independence");
                    else plot.Members.Add(v);
                }
            }
        if (_game.Ai.Enabled) AiMonthly(rng);
    }

    private void AiMonthly(Random rng)
    {
        int max = (int)_game.Data.Defines.Get("ai_direct_counties", 10);
        foreach (var c in _game.World.Countries.Where(c => c.Exists && !c.IsPlayer && c.Ruler != null).ToList())
        {
            var direct = DirectStates(c).Where(s => s != c.Capital?.State).ToList();
            if (direct.Count <= max || rng.NextDouble() > 0.2) continue;
            var cand = _game.World.CharactersOf(c).Where(ch => ch.Alive && !ch.IsRuler && c.Regent != ch && ch.Age(_game.World.Date) >= _game.Data.Defines.AdultAge && ch.Loyalty >= 45)
                .OrderBy(ch => StatesOfVassal(ch).Count()).ThenByDescending(ch => ch.Loyalty).FirstOrDefault();
            if (cand == null) continue;
            // the counties farthest from the court go first, from one region
            var first = direct.OrderByDescending(s => DistanceKm(c, s)).First();
            foreach (var s in direct.Where(s => DuchyOf(s) == DuchyOf(first)).OrderByDescending(s => DistanceKm(c, s)).Take(3))
                if (CannotGrantFree(c, s, cand) == null) { _vassalOf[s] = cand; cand.Vars.Set("vassal_tax", 0.2); cand.Vars.Set("vassal_levy", 0.2); Granted++; }
        }
    }

    private string? CannotGrantFree(Country c, State s, Character ch) => s.Owner != c || IsVassalHeld(s) || !ch.Alive || ch.Country != c || ch.IsRuler ? "no" : null;

    // ------------------------------------------------------------------------------------- independence
    internal void OnPlotSucceeded(Plot p)
    {
        if (!Enabled || p.Goal != "independence") return;
        var states = StatesOfVassal(p.Leader).ToList();
        if (states.Count == 0 || p.Country.OwnedStates.Count <= states.Count) return;
        foreach (var s in states) _vassalOf.Remove(s);
        var realm = _game.Succession.CreateRealm(p.Country, p.Leader, states, "independence");
        Breakaways++;
        _game.NotifyPlayer($"{p.Leader.DisplayName} breaks away from {p.Country.Name} with {states.Count} counties and declares war for independence.", NotificationKind.War, p.Country);
        var cb = _game.Data.CasusBelli.Values.FirstOrDefault(x => x.WarGoal == WarGoalType.Independence);
        _game.Diplomacy.DeclareWar(realm, p.Country, cb, null, true);
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterValue("vassal_count", (ctx, scope) => scope is Country c && Enabled ? VassalsOf(c).Count : null);
        s.RegisterValue("direct_counties", (ctx, scope) => scope is Country c && Enabled ? DirectStates(c).Count() : null);
        s.RegisterTrigger("has_vassals", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && VassalsOf(c).Count > 0, (ctx, n) => "Has vassals");
        s.RegisterTrigger("is_vassal", (ctx, n) => ctx.This is Character ch && StatesOfVassal(ch).Any(), (ctx, n) => "Holds land as a vassal");
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        foreach (var kv in _vassalOf.OrderBy(k => k.Key.Key, StringComparer.Ordinal))
        {
            var n = root.AddBlock("vassal");
            n.Add("state", kv.Key.Key); n.Add("character", kv.Value.Key, quoted: true);
        }
        foreach (var t in Titles.Values.Where(t => t.Tier != TitleTier.County && t.Created != t.StartCreated).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var n = root.AddBlock("title");
            n.Add("key", t.Key, quoted: true); n.Add("created", t.Created ? "yes" : "no");
        }
        foreach (var g in Governors)
        {
            var n = root.AddBlock("governor");
            n.Add("country", g.Country.Tag); n.Add("duchy", g.Duchy.Key, quoted: true); n.Add("character", g.Governor.Key, quoted: true); n.Add("since", g.Since.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        EnsureBuilt();
        _vassalOf.Clear(); Governors.Clear();
        foreach (var t in Titles.Values.Where(t => t.Tier != TitleTier.County)) t.Created = t.StartCreated;
        foreach (var b in root.GetAll("vassal"))
            if (_game.World.GetState(b.GetString("state")) is { } s && _game.World.GetCharacter(b.GetString("character")) is { } ch) _vassalOf[s] = ch;
        foreach (var b in root.GetAll("title"))
            if (Titles.TryGetValue(b.GetString("key") ?? "", out var t)) t.Created = b.GetBool("created");
        foreach (var b in root.GetAll("governor"))
            if (_game.World.GetCountry(b.GetString("country")) is { } c && Titles.TryGetValue(b.GetString("duchy") ?? "", out var d) && _game.World.GetCharacter(b.GetString("character")) is { } ch)
                Governors.Add(new Governorship { Country = c, Duchy = d, Governor = ch, Since = (int)b.GetDouble("since") });
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 59;
        foreach (var kv in _vassalOf) h += kv.Key.Index * 31L + kv.Value.Id;
        foreach (var g in Governors) h = h * 31 + g.Governor.Id;
        return h;
    }
}
