using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>
/// Politics (HOI4 style): ideologies with a popularity in every country, a ruling party, elections with parliament
/// seats and coalitions, ideology-based opinion between countries (politics feature); laws (laws feature);
/// ministers (cabinet feature); political movements and civil wars (civil_wars feature).
/// </summary>
public sealed partial class PoliticsSystem
{
    private readonly Game _game;
    /// <summary>The ideologies in play now: the ones without an unlock and the ones unlocked so far (the list changes when one is unlocked).</summary>
    public readonly List<IdeologyDef> Ideologies = new();
    /// <summary>Every ideology defined, unlocked or not.</summary>
    public readonly List<IdeologyDef> AllIdeologies = new();
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _pendingUnlocks = new();
    public readonly Dictionary<string, IdeologyDef> IdeologyByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, GovernmentDef> Governments = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<LawGroup> LawGroups = new();
    public readonly Dictionary<string, LawDef> Laws = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<CabinetPosition> Positions = new();
    public readonly List<Movement> Movements = new();
    public readonly List<CivilWar> CivilWars = new();
    private readonly Dictionary<Country, CountryPolitics> _state = new();

    /// <summary>Statistics since the game was loaded (not saved), shown by 'burger validate'.</summary>
    public int ElectionsHeld, GovernmentChanges, CivilWarsStarted, Revolutions, Coups, LawsChanged;
    private int _monthsTicked;

    public PoliticsSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "politics", Write = Save, Read = LoadSave, Reset = ResetRuntime });
        game.SaveSections.Add(new SaveSection { Name = "pop_politics", Write = SavePopMoods, Read = LoadPopMoods, Reset = () => { _moods.Clear(); _popSummary.Clear(); } });
        game.SaveSections.Add(new SaveSection { Name = "parliament", Write = SaveParliaments, Read = LoadParliaments, Reset = () => { _parliaments.Clear(); _former.Clear(); _retired.Clear(); _pinned = null; } });
        game.SaveSections.Add(new SaveSection { Name = "ideology_unlocks", Early = true, Write = WriteUnlocks, Read = ReadUnlocks, Reset = () => { _unlocked.Clear(); RebuildActive(); } });
        RegisterScript();
    }

    private void WriteUnlocks(Node n)
    {
        foreach (var k in _unlocked.OrderBy(x => x, StringComparer.Ordinal)) n.AddBlock("unlocked").Add("key", k);
    }

    private void ReadUnlocks(Node n)
    {
        _unlocked.Clear();
        foreach (var b in n.GetAll("unlocked"))
            if (b.GetString("key") is { } k && (IdeologyByKey.ContainsKey(k) || k.StartsWith("law:"))) _unlocked.Add(k);
        RebuildActive();
    }

    private PoliticsRules Rules => _game.Rules.Politics;

    /// <summary>The politics feature is installed and at least one ideology is defined.</summary>
    public bool Enabled => _game.Features.Has(FeatureSet.Politics) && Ideologies.Count > 0;
    public bool LawsEnabled => Enabled && _game.Features.Has(FeatureSet.Laws) && LawGroups.Count > 0;
    public bool CabinetEnabled => Enabled && _game.Features.Has(FeatureSet.Cabinet) && _game.Features.Has(FeatureSet.Characters) && Positions.Count > 0;
    public bool CivilWarsEnabled => Enabled && _game.Features.Has(FeatureSet.CivilWars);

    public IdeologyDef? Ideology(string? key) => key != null && IdeologyByKey.TryGetValue(key, out var i) ? i : null;

    // =============================================================================================
    // Definitions
    // =============================================================================================

    public void Load(GameData data)
    {
        Ideologies.Clear(); AllIdeologies.Clear(); IdeologyByKey.Clear(); _unlocked.Clear(); _pendingUnlocks.Clear(); Governments.Clear(); LawGroups.Clear(); Laws.Clear(); Positions.Clear();
        _modifierKeys.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/ideologies"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var i = new IdeologyDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Elections = n.GetBool("elections"),
                    Extremist = n.GetBool("extremist"),
                    WeakDemocracyDrift = n.GetDouble("weak_democracy_drift"),
                    Assembly = n.GetBool("assembly"),
                    ChamberSeats = n.GetInt("chamber_seats", 0),
                    ColorBlendAmount = n.Get("country_color_blend")?.GetDouble("amount", 0.5) ?? 0,
                    Government = n.GetString("government"),
                    Order = Ideologies.Count,
                    Definition = n,
                };
                if (n.Get("country_color_blend")?.Get("color") is { } blend && Rgb.TryParse(blend, out var blendColor)) i.ColorBlend = blendColor;
                i.PeacePuppets = n.Get("peace_puppets") != null ? n.GetBool("peace_puppets") : i.Elections;
                i.Adjective = n.GetString("adjective") ?? i.Name;
                foreach (var f in n.Get("country_names")?.All ?? Enumerable.Empty<Node>()) i.CountryNames.Add(f.Value ?? f.Key);
                i.PartyName = n.GetString("party_name") ?? i.Name;
                if (Rgb.TryParse(n.Get("color"), out var col)) i.Color = col;
                foreach (var k in n.GetList("coalition")) i.Coalition.Add(k);
                ReadNumbers(n.Get("opinion"), i.Opinion);
                ReadNumbers(n.Get("modifier"), i.Modifier);
                if (IdeologyByKey.ContainsKey(i.Key)) { Log.Error($"Ideology '{i.Key}' is defined twice", n.Location); continue; }
                if (n.Get("unlock") is { } unlock)
                {
                    i.HasUnlock = true;
                    if (unlock.Has("year")) i.UnlockYear = unlock.GetInt("year");
                    i.UnlockTech = unlock.GetString("technology");
                    i.StartPopularity = unlock.GetDouble("popularity", i.StartPopularity);
                }
                foreach (var k in n.GetList("chamber")) i.Chamber.Add(k);
                AllIdeologies.Add(i);
                IdeologyByKey[i.Key] = i;
            }
        RebuildActive();
        foreach (var i in AllIdeologies)
        {
            var key = i.Key;
            _game.Script.RegisterValue("popularity_" + key, (ctx, scope) => scope is Country c && Enabled ? PopularityOf(c, key) : null);
            _game.Script.RegisterValue("seats_" + key, (ctx, scope) => scope is Country c && Enabled ? Of(c).Seats.GetValueOrDefault(key) : null);
            foreach (var other in i.Coalition.Concat(i.Opinion.Keys))
                if (!IdeologyByKey.ContainsKey(other)) Log.Warn($"Ideology '{i.Key}' refers to unknown ideology '{other}'", i.Definition.Location);
        }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/governments"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                // a block with only reforms (government_reforms feature) adds to a government without replacing its starting politics
                if (n.Get("ruling_party") == null && n.Get("popularity") == null && n.Get("laws") == null && !n.Has("seats") && (n.Get("reforms") != null || n.Get("start_reforms") != null)) continue;
                var g = new GovernmentDef { Key = n.Key, Ruling = n.GetString("ruling_party") };
                ReadNumbers(n.Get("popularity"), g.Popularity);
                foreach (var l in n.Get("laws")?.All ?? Enumerable.Empty<Node>()) if (l.Value != null) g.Laws[l.Key] = l.Value;
                if (n.Has("seats")) g.Seats = n.GetInt("seats");
                if (g.Ruling != null && !IdeologyByKey.ContainsKey(g.Ruling)) Log.Error($"Government '{g.Key}': unknown ideology '{g.Ruling}'", n.Location);
                Governments[g.Key] = g;
            }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/laws"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var grp = new LawGroup { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!, Order = LawGroups.Count, Potential = n.Get("potential") };
                if (n.Get("unlock") is { } gu) { grp.HasUnlock = true; if (gu.Has("year")) grp.UnlockYear = gu.GetInt("year"); grp.UnlockTech = gu.GetString("technology"); }
                foreach (var ln in n.All)
                {
                    if (ln.Children == null || ln.Key is "name" or "desc" or "potential") continue;
                    var law = new LawDef
                    {
                        Key = ln.Key,
                        Name = ln.GetString("name") ?? Localisation.GetOrPretty(ln.Key),
                        Desc = ln.GetString("desc", "")!,
                        Icon = ln.GetString("icon"),
                        Group = grp,
                        Cost = ln.GetDouble("cost", data.Defines.Get("law_cost", 100)),
                        Potential = ln.Get("potential"),
                        Allow = ln.Get("allow"),
                        AiWillDo = ln.Get("ai_will_do"),
                        OnEnact = ln.Get("on_enact"),
                        SuspendsElections = ln.GetBool("suspends_elections") || ln.GetBool("single_party"),
                        SingleParty = ln.GetBool("single_party"),
                        Definition = ln,
                    };
                    if (ln.Get("unlock") is { } lu) { law.HasUnlock = true; if (lu.Has("year")) law.UnlockYear = lu.GetInt("year"); law.UnlockTech = lu.GetString("technology"); }
                    ReadNumbers(ln.Get("modifier"), law.Modifier);
                    foreach (var b in ln.GetList("bans")) law.Bans.Add(b);
                    if (Laws.ContainsKey(law.Key)) { Log.Error($"Law '{law.Key}' is defined twice (law keys must be unique across groups)", ln.Location); continue; }
                    grp.Laws.Add(law);
                    Laws[law.Key] = law;
                }
                if (grp.Laws.Count == 0) { Log.Warn($"Law group '{grp.Key}' has no laws", n.Location); continue; }
                grp.Default = n.GetString("default") ?? grp.Laws[0].Key;
                if (!grp.Laws.Any(l => l.Key == grp.Default)) { Log.Error($"Law group '{grp.Key}': default '{grp.Default}' is not one of its laws", n.Location); grp.Default = grp.Laws[0].Key; }
                LawGroups.Add(grp);
            }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/cabinet"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var p = new CabinetPosition
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Stat = n.GetString("stat", "")!,
                    Cost = n.GetDouble("cost", data.Defines.Get("minister_cost", 50)),
                    Order = Positions.Count,
                };
                ReadNumbers(n.Get("per_point"), p.PerPoint);
                ReadNumbers(n.Get("modifier"), p.Modifier);
                Positions.Add(p);
            }

        // modifier keys this system can provide (so Sum() skips the lookup for every other key)
        foreach (var i in AllIdeologies) foreach (var k in i.Modifier.Keys) _modifierKeys.Add(k);
        _modifierKeys.Add("stability_target"); // rule StabilitySources
        _modifierKeys.Add("parliament_term"); // rule ParliamentTermModifiers
        foreach (var l in Laws.Values) foreach (var k in l.Modifier.Keys) _modifierKeys.Add(k);
        foreach (var p in Positions) { foreach (var k in p.Modifier.Keys) _modifierKeys.Add(k); foreach (var k in p.PerPoint.Keys) _modifierKeys.Add(k); }
    }

    private static void ReadNumbers(Node? block, Dictionary<string, double> into)
    {
        if (block?.Children == null) return;
        foreach (var c in block.All)
            if (c.Value != null && Node.TryParseDouble(c.Value, out var v)) into[c.Key] = v;
    }

    // =============================================================================================
    // Country state
    // =============================================================================================

    /// <summary>The politics of a country (created with defaults on first use).</summary>
    public CountryPolitics Of(Country c)
    {
        if (_state.TryGetValue(c, out var p)) return p;
        p = new CountryPolitics();
        _state[c] = p;
        Setup(c, p, null);
        return p;
    }

    public bool HasState(Country c) => _state.ContainsKey(c);

    /// <summary>Starting politics: history/politics entry, else the country's government type, else 'default'.</summary>
    private void Setup(Country c, CountryPolitics p, Node? history)
    {
        if (Ideologies.Count == 0) return;
        var g = (c.Government.Length > 0 ? Governments.GetValueOrDefault(c.Government) : null) ?? Governments.GetValueOrDefault("default");
        p.Popularity.Clear();
        if (g != null) foreach (var kv in g.Popularity) if (IdeologyByKey.ContainsKey(kv.Key)) p.Popularity[kv.Key] = kv.Value;
        p.Ruling = g?.Ruling ?? "";
        p.TotalSeats = g?.Seats ?? (int)_game.Data.Defines.Get("parliament_seats", 100);
        foreach (var grp in LawGroups) p.Laws[grp.Key] = g != null && g.Laws.TryGetValue(grp.Key, out var l) && Laws.ContainsKey(l) ? l : grp.Default;
        if (history != null)
        {
            if (history.GetString("ruling_party") is { } r) p.Ruling = r;
            var pop = history.Get("popularity");
            if (pop != null) { p.Popularity.Clear(); ReadNumbers(pop, p.Popularity); }
            if (history.Has("seats")) p.TotalSeats = Math.Max(1, history.GetInt("seats"));
            foreach (var l in history.Get("laws")?.All ?? Enumerable.Empty<Node>())
            {
                if (l.Value == null) continue;
                if (Laws.TryGetValue(l.Value, out var law)) p.Laws[law.Group.Key] = law.Key;
                else Log.Error($"{c.Tag}: unknown law '{l.Value}'", l.Location);
            }
            foreach (var pn in history.Get("parties")?.All ?? Enumerable.Empty<Node>()) if (pn.Value != null) p.PartyNames[pn.Key] = pn.Value;
            if (GameDate.TryParse(history.GetString("last_election"), out var le)) p.LastElection = le;
        }
        foreach (var k in p.Popularity.Keys.Where(k => !Ideologies.Any(i => i.Key.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList()) p.Popularity.Remove(k);
        if (!Ideologies.Any(i => i.Key.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase))) p.Ruling = p.Popularity.Count > 0 ? p.Popularity.MaxBy(kv => kv.Value).Key : Ideologies[0].Key;
        foreach (var i in Ideologies) if (!p.Popularity.ContainsKey(i.Key)) p.Popularity[i.Key] = 0;
        if (p.Popularity.Values.Sum() <= 0) p.Popularity[p.Ruling] = 1;
        if (p.Popularity[p.Ruling] <= 0) p.Popularity[p.Ruling] = 0.3;
        Normalize(p);
        if (c.Government.Length == 0 && Ideology(p.Ruling)?.Government is { } word0) c.Government = word0; // a country with no government word gets its ruler's
        p.Government = new List<string> { p.Ruling };
        // Split the existing ideology support into independent named parties, then elect each organization separately.
        InitializePartyPopularity(c, p);
        p.RulingOrganization = PartiesOfIdeology(c, p.Ruling)
            .OrderByDescending(party => p.PartyPopularity.GetValueOrDefault(party.Key)).Select(party => party.Key).FirstOrDefault() ?? p.Ruling;
        p.GovernmentOrganizations = new List<string> { p.RulingOrganization };
        if (Ideology(p.Ruling) is { ChamberSeats: > 0 } closed && history?.Has("seats") != true) p.TotalSeats = closed.ChamberSeats;
        p.PartySeats = PartySeatsFrom(c, p, new Dictionary<string, double>(p.PartyPopularity, StringComparer.OrdinalIgnoreCase));
        p.Seats = AggregatePartySeats(c, p.PartySeats);
        AssignGovernmentOrganizations(c, p);
        if (HoldsElections(c, p))
        {
            var coalition = FormCoalition(c, p, p.Ruling);
            if (coalition != null) p.Government = coalition;
            AssignGovernmentOrganizations(c, p);
            int freq = ElectionMonths();
            var now = _game.World.Date;
            if (p.LastElection is { } last)
            {
                var next = last.AddMonths(freq);
                while (next <= now) next = next.AddMonths(freq);
                p.NextElection = next;
            }
            else p.NextElection = now.AddDays(30 + (c.Index * 97 + 13) % Math.Max(1, freq * 30 - 30)); // spread elections out
        }
        _modsDirty.Add(c);
    }

    /// <summary>Reads history/politics (called once the countries exist).</summary>
    public void LoadHistory()
    {
        _state.Clear();
        CheckUnlocks(silent: true);
        if (Ideologies.Count == 0) return;
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/politics"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var c = _game.World.GetCountry(n.Key);
                if (c == null) { Log.Error($"history/politics: unknown country '{n.Key}'", n.Location); continue; }
                var p = new CountryPolitics();
                _state[c] = p;
                Setup(c, p, n);
            }
    }

    private void ResetRuntime()
    {
        _unlocked.Clear(); _pendingUnlocks.Clear(); RebuildActive();
        lock (_wealth) { _wealth.Clear(); _wealthDay = int.MinValue; }
        _state.Clear();
        Movements.Clear();
        CivilWars.Clear();
        _revolts.Clear();
        _modsDirty.Clear();
        _mods.Clear();
    }

    private static void Normalize(CountryPolitics p)
    {
        foreach (var k in p.Popularity.Keys.ToList()) if (p.Popularity[k] < 0 || double.IsNaN(p.Popularity[k])) p.Popularity[k] = 0;
        double sum = p.Popularity.Values.Sum();
        if (sum <= 0 || Math.Abs(sum - 1) < 1e-12) return; // already 100%: dividing again only shifts the last bits (and a reloaded save would then differ from the game it came from)
        foreach (var k in p.Popularity.Keys.ToList()) p.Popularity[k] /= sum;
    }

    public IdeologyDef? RulingIdeology(Country c) => Enabled ? Ideology(Of(c).Ruling) : null;

    public double PopularityOf(Country c, string ideology) => Of(c).Popularity.GetValueOrDefault(ideology);

    /// <summary>Adds popularity (a fraction, e.g. 0.05 = 5 points) to an ideology; the others shrink to keep 100%.</summary>
    public void AddPopularity(Country c, string ideology, double amount)
    {
        var p = Of(c);
        if (!p.Popularity.ContainsKey(ideology)) return;
        double cur = p.Popularity[ideology];
        double target = Math.Clamp(cur + amount, 0, 1);
        double others = 1 - cur;
        double scale = others > 1e-9 ? (1 - target) / others : 0;
        foreach (var k in p.Popularity.Keys.ToList()) p.Popularity[k] = k.Equals(ideology, StringComparison.OrdinalIgnoreCase) ? target : p.Popularity[k] * scale;
        if (others <= 1e-9 && target < 1) // everybody supported this ideology: spread the rest evenly
            foreach (var k in p.Popularity.Keys.ToList()) if (!k.Equals(ideology, StringComparison.OrdinalIgnoreCase)) p.Popularity[k] = (1 - target) / Math.Max(1, p.Popularity.Count - 1);
        Normalize(p);
        SyncPartyPopularityToIdeologies(c, p);
    }

    public string PartyName(Country c, string ideologyOrParty)
    {
        if (_game.World.GetGenericScope(ideologyOrParty) is { } named && IsParty(named) && PartyCountry(named) == c)
            return named.DisplayName;
        if (IsMinorityParty(ideologyOrParty)) return MinorityPartyName(ideologyOrParty);
        var state = Of(c);
        if (state.PartyNames.TryGetValue(ideologyOrParty, out var n)) return Localisation.Get(n);
        if (string.Equals(state.Ruling, ideologyOrParty, StringComparison.OrdinalIgnoreCase)
            && _game.World.GetGenericScope(state.RulingOrganization) is { } ruling && IsParty(ruling)
            && string.Equals(IdeologyOfParty(ruling), ideologyOrParty, StringComparison.OrdinalIgnoreCase)) return ruling.DisplayName;
        var candidates = PartiesOfIdeology(c, ideologyOrParty).ToList();
        if (candidates.Count == 1) return candidates[0].DisplayName;
        if (candidates.Count > 1)
            return candidates.OrderByDescending(p => state.PartyPopularity.GetValueOrDefault(p.Key))
                .ThenByDescending(p => state.PartySeats.GetValueOrDefault(p.Key)).First().DisplayName;
        return Ideology(ideologyOrParty)?.PartyName ?? ideologyOrParty;
    }

    // ------------------------------------------------------------------------------------- minority parties
    // Parties of a people or a faith (population features): "culture:KEY" and "religion:KEY" seats in parliament.
    // They win seats but never govern; separatist seats speed up independence movements.

    public static bool IsMinorityParty(string key) => key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("religion:", StringComparison.OrdinalIgnoreCase);

    public string MinorityPartyName(string key)
    {
        string k = key[(key.IndexOf(':') + 1)..];
        return key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase)
            ? $"{_game.Population.Culture(k)?.Name ?? Localisation.GetOrPretty(k)} National Party"
            : $"{_game.Population.Religion(k)?.Name ?? Localisation.GetOrPretty(k)} Party";
    }

    /// <summary>Color of a party: its ideology, or the culture/religion of a minority party.</summary>
    public Rgb PartyColor(string key)
    {
        if (_game.World.GetGenericScope(key) is { } party && IsParty(party))
        {
            // a party may carry its own colour (a splinter party does, so its flag differs from the party it left)
            if (party.Attributes.TryGetValue("color", out var raw)
                && raw.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 3 } rgb
                && int.TryParse(rgb[0], out var cr) && int.TryParse(rgb[1], out var cg) && int.TryParse(rgb[2], out var cb))
                return new Rgb(cr, cg, cb);
            if (Ideology(IdeologyOfParty(party)) is { } partyIdeology) return partyIdeology.Color;
        }
        if (Ideology(key) is { } i) return i.Color;
        string k = key[(key.IndexOf(':') + 1)..];
        if (key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase)) return _game.Population.Culture(k)?.Color ?? Rgb.Gray;
        if (key.StartsWith("religion:", StringComparison.OrdinalIgnoreCase)) return _game.Population.Religion(k)?.Color ?? Rgb.Gray;
        return Rgb.Gray;
    }

    /// <summary>Short label of a party for lists: ideology name, or the minority party's name.</summary>
    public string PartyLabel(string key)
        => _game.World.GetGenericScope(key) is { } party && IsParty(party) ? party.DisplayName
            : Ideology(key)?.Name ?? (IsMinorityParty(key) ? MinorityPartyName(key) : key);

    /// <summary>Share of the seats held by a named party, ideology bucket, or minority party (0-1).</summary>
    public double SeatShare(Country c, string key)
    {
        var p = Of(c);
        int total = p.PartySeats.Values.Sum();
        bool namedParty = _game.World.GetGenericScope(key) is { } scope && IsParty(scope) && PartyCountry(scope) == c;
        int seats = namedParty ? p.PartySeats.GetValueOrDefault(key) : p.Seats.GetValueOrDefault(key);
        return total <= 0 ? 0 : seats / (double)total;
    }

    public bool IsBanned(Country c, string ideology) => IsBanned(c, Of(c), ideology);

    /// <summary>True if the ruling ideology keeps a closed chamber that this current may not enter (a council of nobles, an assembly of estates).</summary>
    private bool ShutOutOfChamber(CountryPolitics p, string ideology)
        => Ideology(p.Ruling) is { Chamber.Count: > 0 } rule && !ideology.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase) && !rule.Chamber.Contains(ideology);

    private bool IsBanned(Country c, CountryPolitics p, string ideology)
    {
        if (ShutOutOfChamber(p, ideology)) return true;
        if (!LawsEnabled) return false;
        foreach (var kv in p.Laws)
            if (Laws.TryGetValue(kv.Value, out var l) && (l.Bans.Contains(ideology) || l.SingleParty && !ideology.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    public bool InGovernment(Country c, string ideology) => Of(c).Government.Contains(ideology, StringComparer.OrdinalIgnoreCase);

    /// <summary>True if the country holds elections: its ruling ideology has elections and no law suspends them.</summary>
    public bool HoldsElections(Country c) => Enabled && HoldsElections(c, Of(c));

    private bool HoldsElections(Country c, CountryPolitics p)
    {
        if (Ideology(p.Ruling)?.Elections != true) return false;
        if (LawsEnabled)
            foreach (var kv in p.Laws)
                if (Laws.TryGetValue(kv.Value, out var l) && l.SuspendsElections) return false;
        return true;
    }

    private int ElectionMonths() => Math.Max(1, (int)_game.Data.Defines.Get("election_frequency_months", 48));

    // =============================================================================================
    // Elections and governments
    // =============================================================================================

    /// <summary>Seats per ideology by the D'Hondt method (banned parties and parties under the threshold get none).</summary>
    private Dictionary<string, int> SeatsFrom(Country c, CountryPolitics p, Dictionary<string, double> votes)
    {
        double threshold = _game.Data.Defines.Get("election_threshold", 0.05);
        var eligible = votes.Where(kv => kv.Value >= threshold && !IsBanned(c, p, kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in eligible.Keys) seats[k] = 0;
        if (eligible.Count == 0) { seats[p.Ruling] = p.TotalSeats; return seats; }
        for (int s = 0; s < p.TotalSeats; s++)
        {
            var best = eligible.MaxBy(kv => kv.Value / (seats[kv.Key] + 1)).Key;
            seats[best]++;
        }
        return seats;
    }

    /// <summary>Parties that govern together with <paramref name="leader"/> for a majority (null if no majority is possible).</summary>
    private List<string>? FormCoalition(Country c, CountryPolitics p, string leader)
    {
        var li = Ideology(leader);
        if (li == null) return null;
        int total = Math.Max(1, p.Seats.Values.Sum());
        var gov = new List<string> { leader };
        int seats = p.Seats.GetValueOrDefault(leader);
        foreach (var kv in p.Seats.Where(kv => kv.Key != leader && kv.Value > 0).OrderByDescending(kv => kv.Value))
        {
            if (seats * 2 > total) break;
            var other = Ideology(kv.Key);
            if (other == null || !gov.All(g => Ideology(g) is { } gi && Rules.WillGovernWith(_game, c, gi, other))) continue;
            gov.Add(kv.Key);
            seats += kv.Value;
        }
        return seats * 2 > total ? gov : null;
    }

    /// <summary>Holds an election now: new seats, then the biggest party that can find a majority governs.</summary>
    public void HoldElection(Country c)
    {
        if (!Enabled) return;
        var p = Of(c);
        var votes = PartyVotes(c);
        // parties of minorities and faiths take their voters' share; the ideologies share the rest
        var minority = _game.Population.Enabled ? Rules.MinorityVotes(_game, c) : new Dictionary<string, double>();
        double minShare = Math.Min(0.6, minority.Values.Sum());
        if (minShare > 0)
        {
            foreach (var k in votes.Keys.ToList()) votes[k] *= 1 - minShare;
            double scale = minShare / Math.Max(1e-9, minority.Values.Sum());
            foreach (var kv in minority) votes[kv.Key] = kv.Value * scale;
        }
        // what the people lean to (culture, religion, class, mood) shifts each party's vote
        var popMult = PopVoteMultipliers(c, votes.Keys);
        foreach (var kv in popMult) if (votes.ContainsKey(kv.Key)) votes[kv.Key] *= kv.Value;
        double voteSum = votes.Values.Sum();
        // the governing parties keep a small lead (incumbency), so near-ties do not flip the government back and forth
        double incumbent = Def("incumbent_bonus", 1.08);
        foreach (var k in p.GovernmentOrganizations) if (votes.ContainsKey(k)) votes[k] *= incumbent;
        voteSum = votes.Values.Sum();
        if (voteSum > 0) foreach (var k in votes.Keys.ToList()) votes[k] /= voteSum;
        p.PartySeats = PartySeatsFrom(c, p, votes);
        p.Seats = AggregatePartySeats(c, p.PartySeats);
        p.LastElection = _game.World.Date;
        p.NextElection = _game.World.Date.AddMonths(ElectionMonths());
        ElectionsHeld++;
        ParliamentElection(c);
        var before0 = p.Government.ToList();
        // (an ideology that needs assent - see PoliticsRules.NeedsAssent - cannot take government by the vote; it becomes a movement instead)
        bool Allowed(string k) => before0.Contains(k, StringComparer.OrdinalIgnoreCase) || Ideology(k) is not { } ik || !Rules.NeedsAssent(_game, c, ik);
        bool PartyAllowed(string party) => IdeologyKeyOfParty(party) is not { } ik || Allowed(ik);
        List<string>? organizationCoalition = null;
        foreach (var leader in p.PartySeats.Where(kv => kv.Value > 0 && !IsMinorityParty(kv.Key) && PartyAllowed(kv.Key))
                     .OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
        {
            organizationCoalition = FormPartyCoalition(c, p, leader)?.Where(PartyAllowed).ToList();
            if (organizationCoalition != null && organizationCoalition.Count > 0) break;
            organizationCoalition = null;
        }
        // If no coalition can reach a majority, the largest eligible organization forms a minority government alone.
        organizationCoalition ??= new List<string> { p.PartySeats.Where(kv => kv.Value > 0 && !IsMinorityParty(kv.Key)
                && IdeologyKeyOfParty(kv.Key) != null && PartyAllowed(kv.Key)).OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key).FirstOrDefault() ?? (PartyOf(c, p.Ruling)?.Key ?? p.Ruling) };
        var gov = organizationCoalition.Select(IdeologyKeyOfParty).Where(key => key != null).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (gov.Count == 0) gov.Add(p.Ruling);
        string before = p.Ruling;
        string beforeOrganization = p.RulingOrganization;
        SetGovernment(c, gov, "election", organizationCoalition);
        if (p.PartySeats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() is { } topParty) _game.Hub.RaiseElectionHeld(c, topParty);
        if (c.IsPlayer || before != p.Ruling || !beforeOrganization.Equals(p.RulingOrganization, StringComparison.OrdinalIgnoreCase))
        {
            string result = string.Join(", ", p.PartySeats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{PartyName(c, kv.Key)} {kv.Value}"));
            string text = $"Election in {c.Name}: {result}. " + (p.GovernmentOrganizations.Count > 1 ? $"Coalition of {string.Join(" and ", p.GovernmentOrganizations.Select(g => PartyName(c, g)))}." : $"{PartyName(c, p.RulingOrganization)} governs.");
            if (c.IsPlayer) _game.Notify(text, NotificationKind.Diplomacy);
        }
        _game.Events.FireOnAction("on_election", c);
    }

    /// <summary>Puts an ideology's party in power alone (coups, revolutions, scripts).</summary>
    /// <summary>
    /// Reaction to a country becoming (or staying) a subject: puts the overlord's ideology in power as far as the subject type
    /// allows (<see cref="PoliticsRules.SubjectGovernment"/>), with some extra support for it.
    /// </summary>
    public void FollowOverlord(Country subject, Country overlord, double popularity)
    {
        var rel = _game.World.Relations;
        if (!Enabled || rel.SubjectTypeOf(subject) is not { } type) return;
        var policy = Rules.SubjectGovernment(_game, subject, overlord, type);
        if (policy == SubjectControl.Free) return;
        if (InRegimeLock(subject) && Of(subject).Ruling.Length > 0) { if (popularity > 0) AddPopularity(subject, Of(overlord).Ruling, popularity); return; }
        string want = Of(overlord).Ruling;
        if (string.IsNullOrEmpty(want) || string.Equals(Of(subject).Ruling, want, StringComparison.OrdinalIgnoreCase)) return;
        if (policy == SubjectControl.Imposed) SetRulingParty(subject, want, "overlord");
        if (popularity > 0) AddPopularity(subject, want, popularity);
    }

    /// <summary>Did the ruling ideology change less than regime_lock_months ago? Snap elections and an overlord's pressure wait it out.</summary>
    public bool InRegimeLock(Country c) => _game.World.Date.TotalDays - Of(c).RegimeChangedDay < Def("regime_lock_months", 24) * 30;

    public void SetRulingParty(Country c, string ideology, string reason = "")
    {
        if (!IdeologyByKey.ContainsKey(ideology)) return;
        SetGovernment(c, new List<string> { ideology }, reason);
        var p = Of(c);
        if (HoldsElections(c, p) && p.NextElection == null) p.NextElection = _game.World.Date.AddMonths(ElectionMonths());
    }

    /// <summary>A new ruling ideology with (or after) a closed chamber seats the chamber again: its size, and only the currents it lets in.</summary>
    private void RebuildChamber(Country c, CountryPolitics p)
    {
        p.TotalSeats = Ideology(p.Ruling) is { ChamberSeats: > 0 } rule ? rule.ChamberSeats : (int)_game.Data.Defines.Get("parliament_seats", 100);
        p.PartySeats = PartySeatsFrom(c, p, new Dictionary<string, double>(p.PartyPopularity, StringComparer.OrdinalIgnoreCase));
        p.Seats = AggregatePartySeats(c, p.PartySeats);
        AssignGovernmentOrganizations(c, p);
    }

    private void SetGovernment(Country c, List<string> gov, string reason, List<string>? governmentOrganizations = null)
    {
        var p = Of(c);
        string old = p.Ruling;
        string oldOrganization = p.RulingOrganization;
        var oldOrganizations = p.GovernmentOrganizations.ToList();
        p.Government = gov.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.Ruling = p.Government[0];
        if (governmentOrganizations is { Count: > 0 })
        {
            p.GovernmentOrganizations = governmentOrganizations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            p.RulingOrganization = p.GovernmentOrganizations[0];
        }
        else
        {
            // A coup or scripted government change can replace the ruling ideology. Under a single-party law,
            // identify its replacement organization before filtering the other organizations out of government.
            if (IdeologyKeyOfParty(p.RulingOrganization) is not { } oldIdeology
                || !oldIdeology.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase)
                || IsPartyBanned(c, p, p.RulingOrganization))
                p.RulingOrganization = PartiesOfIdeology(c, p.Ruling)
                    .OrderByDescending(party => p.PartyPopularity.GetValueOrDefault(party.Key)).Select(party => party.Key).FirstOrDefault() ?? p.Ruling;
            AssignGovernmentOrganizations(c, p);
        }
        _modsDirty.Add(c);
        bool ideologyChanged = !old.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase);
        bool organizationChanged = !oldOrganization.Equals(p.RulingOrganization, StringComparison.OrdinalIgnoreCase)
            || !oldOrganizations.SequenceEqual(p.GovernmentOrganizations, StringComparer.OrdinalIgnoreCase);
        if (!ideologyChanged && !organizationChanged) return;
        if (Environment.GetEnvironmentVariable("BURGER_GOV_LOG") is { } gl && gl.Equals(c.Tag, StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"GOV {_game.World.Date.ToDisplay()} {c.Tag}: {old} -> {p.Ruling} ({reason}) stab {Rules.StabilityOf(_game, c):0} pop {string.Join("/", p.Popularity.Select(kv => $"{kv.Value:P0}"))}");
        if (ideologyChanged && (Ideology(old)?.Chamber.Count > 0 || Ideology(p.Ruling)?.Chamber.Count > 0)) RebuildChamber(c, p);
        if (ideologyChanged) { GovernmentChanges++; p.RegimeChangedDay = _game.World.Date.TotalDays; }
        if (organizationChanged) _game.Hub.RaiseRulingPartyChanged(c, oldOrganization, p.RulingOrganization, reason);
        // the new ruling party's leader takes over as the country's ruler
        if (organizationChanged && _game.World.GetGenericScope(p.RulingOrganization) is { } rulingScope
            && LeaderOf(rulingScope) is { Alive: true } leader && (leader.Country == c || leader.Country == null))
            _game.Characters.SetRuler(c, leader);
        var ideology = Ideology(p.Ruling)!;
        // the government word follows the ruling ideology, unless the current one already is a form of that ideology's rule
        // (a constitutional monarchy stays one while the democrats govern; a 'republic' does not outlive its fascist takeover)
        if (ideology.Government != null && !(Governments.GetValueOrDefault(c.Government) is { } word
                && string.Equals(word.Ruling, p.Ruling, StringComparison.OrdinalIgnoreCase)))
            c.Government = ideology.Government;
        _game.Hub.RaiseGovernmentChanged(c, old); // (country_names follow the government)
        if (!HoldsElections(c, p)) p.NextElection = null;
        else p.NextElection ??= _game.World.Date.AddMonths(ElectionMonths());
        // ministers of parties that left the government step down
        foreach (var kv in p.Ministers.ToList())
            if (!PartyInGovernmentOf(c, kv.Value)) p.Ministers.Remove(kv.Key);
        if (_game.World.Player != null)
        {
            string how = reason switch { "election" => "after an election", "revolution" => "after a revolution", "coup" => "in a coup", "overlord" => "backed by its overlord", _ => "" };
            _game.Notify($"{c.Name}: the {PartyName(c, p.RulingOrganization)} ({ideology.Name}) took power {how}.".Replace("  ", " ").Replace(" .", "."), c.IsPlayer ? NotificationKind.Diplomacy : NotificationKind.Info);
        }
        _game.Events.FireOnAction("on_ruling_party_changed", c);
        _game.RaiseMapChanged(); // ideology map mode colors
    }

    // =============================================================================================
    // Effects on the rest of the game
    // =============================================================================================

    /// <summary>Opinion <paramref name="from"/> has of <paramref name="to"/> because of their ruling ideologies.</summary>
    public double IdeologyOpinion(Country from, Country to)
    {
        if (!Enabled || from == to) return 0;
        var a = Ideology(Of(from).Ruling);
        if (a == null) return 0;
        return a.Opinion.GetValueOrDefault(Of(to).Ruling);
    }

    // modifier values from the ruling ideology, laws and ministers, cached per country
    private readonly HashSet<string> _modifierKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, Dictionary<string, double>> _mods = new();
    private readonly HashSet<Country> _modsDirty = new();
    private long _modsStamp = long.MinValue;

    /// <summary>Modifier total this system adds to a scope (used by <see cref="Mechanics.ModifierSystem.Sum"/>).</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (scope is not Country c || !_modifierKeys.Contains(key) || !Enabled) return 0;
        // The cache is rebuilt every game hour, so it is a pure function of the state: a game loaded from a save (empty cache) and the game that was saved agree.
        // The lock is re-entrant: the AI ticks run on several threads and a computation can ask for other modifiers.
        lock (_mods)
        {
            long stamp = _game.World.Date.TotalHours;
            if (stamp != _modsStamp) { _mods.Clear(); _modsDirty.Clear(); _modsStamp = stamp; }
            if (_modsDirty.Remove(c) || !_mods.TryGetValue(c, out var m)) _mods[c] = m = ComputeModifiers(c);
            return m.GetValueOrDefault(key);
        }
    }

    /// <summary>Where a country's political modifiers come from (for tooltips): (source, key, value).</summary>
    public List<(string Source, string Key, double Value)> ModifierSources(Country c)
    {
        var list = new List<(string, string, double)>();
        var p = Of(c);
        if (Ideology(p.Ruling) is { } i) foreach (var kv in i.Modifier) list.Add(($"{i.Name} government", kv.Key, kv.Value));
        if (LawsEnabled)
            foreach (var kv in p.Laws)
                if (Laws.TryGetValue(kv.Value, out var l)) foreach (var m in l.Modifier) list.Add((l.Name, m.Key, m.Value));
        foreach (var source in FactionModifierSources(c)) list.Add(source);
        if (CabinetEnabled)
            foreach (var pos in Positions)
            {
                if (!p.Ministers.TryGetValue(pos.Key, out var ch) || !ch.Alive) continue;
                foreach (var m in pos.Modifier) list.Add((pos.Name, m.Key, m.Value));
                double stat = pos.Stat.Length > 0 ? ch.GetStat(pos.Stat) : 0;
                foreach (var m in pos.PerPoint) list.Add(($"{pos.Name} ({ch.DisplayName})", m.Key, m.Value * stat));
            }
        // poverty, a government without support, lost land (rule StabilitySources)
        foreach (var (source, value) in Rules.StabilitySources(_game, c))
            if (Math.Abs(value) > 0.01) list.Add((source, "stability_target", value));
        return list;
    }

    private readonly Dictionary<Country, double> _wealth = new();
    private int _wealthDay = int.MinValue;

    /// <summary>
    /// How wealthy a country is compared to the world: its wealth per person (rule WealthPerPerson) divided by the
    /// median of all countries (1 = typical, 0.3 = poor, 2 = rich; recounted daily).
    /// </summary>
    public double WealthIndex(Country c)
    {
        int today = _game.World.Date.TotalDays;
        lock (_wealth)
        {
        if (_wealthDay != today) // recounted daily (a pure function of the state, so a loaded save agrees with the game it came from)
        {
            _wealth.Clear();
            _wealthDay = today;
            var all = _game.World.Countries.Where(x => x.Exists).Select(x => (Country: x, Wealth: Rules.WealthPerPerson(_game, x))).ToList();
            var sorted = all.Select(x => x.Wealth).OrderBy(v => v).ToList();
            double median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;
            foreach (var (country, wealth) in all) _wealth[country] = median > 0 ? Math.Clamp(wealth / median, 0, 3) : 1;
        }
        return _wealth.GetValueOrDefault(c, 1);
        }
    }

    private Dictionary<string, double> ComputeModifiers(Country c)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, key, value) in ModifierSources(c)) m[key] = m.GetValueOrDefault(key) + value;
        return m;
    }

    /// <summary>Call after something changes a country's laws, ministers or government.</summary>
    public void Invalidate(Country c) => _modsDirty.Add(c);

    // =============================================================================================
    // Ticks
    // =============================================================================================

    public void DailyTick()
    {
        if (!Enabled) return;
        var today = _game.World.Date;
        foreach (var c in _game.World.Countries.ToList()) // scripts may add countries (start_civil_war)
        {
            if (!c.Exists) continue;
            var p = Of(c);
            if (p.NextElection is { } next && HoldsElections(c, p))
            {
                if (next <= today) HoldElection(c);
                else if (next.TotalDays - today.TotalDays == (int)_game.Data.Defines.Get("election_campaign_days", 60))
                    _game.Events.FireOnAction("on_election_campaign", c); // campaign events (politics feature)
            }
        }
        if (CivilWarsEnabled) CheckCivilWars();
    }

    // =============================================================================================
    // Unlocking ideologies
    // =============================================================================================

    /// <summary>True if the ideology is in play: it has no unlock, or it was unlocked.</summary>
    public bool IsUnlocked(string key) => !IdeologyByKey.TryGetValue(key, out var d) || !d.HasUnlock || _unlocked.Contains(key);

    private void RebuildActive()
    {
        Ideologies.Clear();
        Ideologies.AddRange(AllIdeologies.Where(i => !i.HasUnlock || _unlocked.Contains(i.Key)));
    }

    /// <summary>Unlocks an ideology at the next month (scripts call this). Returns false for an unknown key or one already unlocked.</summary>
    public bool Unlock(string key)
    {
        if (IdeologyByKey.TryGetValue(key, out var d))
        {
            if (IsUnlocked(key) || _pendingUnlocks.Contains(d.Key)) return false;
            _pendingUnlocks.Add(d.Key);
            return true;
        }
        string lk = "law:" + key;
        bool known = Laws.TryGetValue(key, out var law) ? !IsLawUnlocked(law) : LawGroups.FirstOrDefault(g => g.Key == key) is { } grp && !IsGroupUnlocked(grp);
        if (!known || _pendingUnlocks.Contains(lk)) return false;
        _pendingUnlocks.Add(lk);
        return true;
    }

    /// <summary>Unlocks what its year or technology allows; silent at the start of a game.</summary>
    private void CheckUnlocks(bool silent = false)
    {
        var date = _game.World.Date;
        bool Reached(int? year, string? tech) => year is { } y && date.Year >= y
            || tech != null && _game.Tech.Defs.ContainsKey(tech) && _game.World.Countries.Any(c => c.Exists && _game.Tech.Has(c, tech));
        foreach (var g in LawGroups)
            if (g.HasUnlock && !_unlocked.Contains("law:" + g.Key) && Reached(g.UnlockYear, g.UnlockTech)) ApplyLawUnlock("law:" + g.Key, g.Name, silent);
        foreach (var l in Laws.Values)
            if (l.HasUnlock && !_unlocked.Contains("law:" + l.Key) && Reached(l.UnlockYear, l.UnlockTech)) ApplyLawUnlock("law:" + l.Key, l.Name, silent);
        foreach (var d in AllIdeologies)
        {
            if (!d.HasUnlock || _unlocked.Contains(d.Key)) continue;
            bool yes = d.UnlockYear is { } y && date.Year >= y
                       || d.UnlockTech != null && _game.Tech.Defs.ContainsKey(d.UnlockTech) && _game.World.Countries.Any(c => c.Exists && _game.Tech.Has(c, d.UnlockTech));
            if (yes) ApplyUnlock(d, silent);
        }
        foreach (var key in _pendingUnlocks.ToList())
        {
            if (IdeologyByKey.TryGetValue(key, out var d) && !_unlocked.Contains(key)) ApplyUnlock(d, silent);
            else if (key.StartsWith("law:") && !_unlocked.Contains(key)) ApplyLawUnlock(key, Laws.TryGetValue(key[4..], out var lw) ? lw.Name : LawGroups.FirstOrDefault(x => x.Key == key[4..])?.Name ?? key[4..], silent);
        }
        _pendingUnlocks.Clear();
    }

    private void ApplyLawUnlock(string key, string name, bool silent)
    {
        _unlocked.Add(key);
        foreach (var c in _state.Keys) _modsDirty.Add(c);
        if (!silent) _game.Notify($"A new law becomes possible: {name}.", NotificationKind.Info);
    }

    public bool IsGroupUnlocked(LawGroup g) => !g.HasUnlock || _unlocked.Contains("law:" + g.Key);
    public bool IsLawUnlocked(LawDef l) => IsGroupUnlocked(l.Group) && (!l.HasUnlock || _unlocked.Contains("law:" + l.Key));

    private void ApplyUnlock(IdeologyDef d, bool silent)
    {
        _unlocked.Add(d.Key);
        RebuildActive();
        foreach (var (c, p) in _state)
        {
            p.Popularity[d.Key] = silent ? 0 : d.StartPopularity;
            Normalize(p);
            _modsDirty.Add(c);
        }
        if (!silent) _game.Notify($"A new political current appears in the world: {d.Name}.", NotificationKind.Info);
    }

    /// <summary>Does the country have an assembly or council now (parliament, parties): it holds elections, or its ruling ideology keeps an assembly.</summary>
    public bool HasAssembly(Country c) => Enabled && (HoldsElections(c) || Ideology(Of(c).Ruling)?.Assembly == true);

    public void MonthlyTick()
    {
        if (!Enabled) return;
        CheckUnlocks();
        if (!_aiCalled && _monthsTicked > 0 && _game.Features.Has(FeatureSet.Ai) && _game.Ai.Enabled)
            foreach (var ac in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer).ToList()) AiMonthly(ac);
        _aiCalled = false;
        _monthsTicked++;
        _mods.Clear(); // wealth, government support and lost land move stability (rule StabilitySources)
        foreach (var c in _game.World.Countries.ToList()) // scripts may add countries (start_civil_war)
        {
            if (!c.Exists) continue;
            var p = Of(c);
            var drift = Rules.MonthlyDrift(_game, c);
            foreach (var kv in drift)
                if (p.Popularity.ContainsKey(kv.Key)) p.Popularity[kv.Key] = Math.Max(0, p.Popularity[kv.Key] + kv.Value / 100.0);
            Normalize(p);
            SyncPartyPopularityToIdeologies(c, p);
            FactionMonthly(c);
            // a government whose party has torn itself apart goes back to the voters (after the usual snap-election interval)
            bool collapsed = GovernmentCollapsed(c) && !InRegimeLock(c) && (p.LastElection is not { } lastVote
                || _game.World.Date.TotalDays - lastVote.TotalDays >= Def("snap_election_months", 12) * 30);
            if (HoldsElections(c, p) && !InCivilWar(c) && (collapsed || (!InRegimeLock(c) && Rules.CallsSnapElection(_game, c)))) HoldElection(c);
            PopReactionMonthly(c);
            ParliamentMonthly(c);
            // ministers who died or left the country leave the cabinet
            foreach (var kv in p.Ministers.ToList())
                if (!kv.Value.Alive || kv.Value.Country != c) { p.Ministers.Remove(kv.Key); _modsDirty.Add(c); }
        }
        FlushRetired();
        if (CivilWarsEnabled) MonthlyUnrest();
    }

    // =============================================================================================
    // Save
    // =============================================================================================

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        // the daily wealth ranking is counted at the first request of a day, so a loaded game would count it at another hour: keep it
        lock (_wealth)
            if (_wealthDay != int.MinValue)
            {
                var wn = root.AddBlock("wealth");
                wn.Add("day", _wealthDay.ToString(CultureInfo.InvariantCulture));
                var items = wn.AddBlock("values");
                foreach (var (wc, wv) in _wealth.OrderBy(k => k.Key.Tag, StringComparer.Ordinal)) { items.AddItem(wc.Tag); items.AddItem(F(wv)); }
            }
        foreach (var (c, p) in _state)
        {
            if (!c.Exists && !c.Dynamic) continue;
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            n.Add("ruling_party", p.Ruling);
            if (!string.IsNullOrWhiteSpace(p.RulingOrganization)) n.Add("ruling_organization", p.RulingOrganization);
            var gov = n.AddBlock("government");
            foreach (var g in p.Government) gov.AddItem(g);
            var govOrgs = n.AddBlock("government_organizations");
            foreach (var g in p.GovernmentOrganizations) govOrgs.AddItem(g);
            var partyPop = n.AddBlock("party_popularity");
            foreach (var kv in p.PartyPopularity) partyPop.Add(kv.Key, F(kv.Value));
            var partySeats = n.AddBlock("party_seats");
            foreach (var kv in p.PartySeats) partySeats.Add(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            var pop = n.AddBlock("popularity");
            foreach (var kv in p.Popularity) pop.Add(kv.Key, F(kv.Value));
            var seats = n.AddBlock("seats");
            foreach (var kv in p.Seats) seats.Add(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            n.Add("total_seats", p.TotalSeats.ToString(CultureInfo.InvariantCulture));
            if (p.LastElection is { } le) n.Add("last_election", le.ToScript());
            if (p.NextElection is { } ne) n.Add("next_election", ne.ToScript());
            if (p.CalmUntilDay != int.MinValue) n.Add("calm_until", p.CalmUntilDay.ToString(CultureInfo.InvariantCulture));
            var laws = n.AddBlock("laws");
            foreach (var kv in p.Laws) laws.Add(kv.Key, kv.Value);
            var changed = n.AddBlock("law_changed");
            foreach (var kv in p.LawChangedDay) changed.Add(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            var min = n.AddBlock("ministers");
            foreach (var kv in p.Ministers) min.Add(kv.Key, kv.Value.Key);
            var parties = n.AddBlock("parties");
            foreach (var kv in p.PartyNames) parties.Add(kv.Key, kv.Value, true);
        }
        foreach (var m in Movements)
        {
            var n = root.AddBlock("movement");
            n.Add("country", m.Country.Tag);
            n.Add("ideology", m.Ideology);
            n.Add("demand", m.Demand.ToString().ToLowerInvariant());
            if (m.TargetLaw != null) n.Add("law", m.TargetLaw);
            n.Add("progress", F(m.Progress));
            n.Add("started", m.Started.ToScript());
            if (m.Warned) n.Add("warned", "yes");
            n.Add("organization", F(m.Organization));
            n.Add("grievance", F(m.Grievance));
            if (m.Concessions > 0.01) n.Add("concessions", F(m.Concessions));
            if (m.Crackdowns > 0.01) n.Add("crackdowns", F(m.Crackdowns));
            if (m.Heat > 0.01) n.Add("heat", F(m.Heat));
            foreach (var g in m.Groups)
            {
                var gn = n.AddBlock("group");
                gn.Add("key", g.Key); gn.Add("strength", F(g.Strength)); gn.Add("satisfaction", F(g.Satisfaction));
            }
        }
        var rv = root.AddBlock("revolts");
        foreach (var kv in _revolts) rv.Add(kv.Key.Tag, kv.Value.ToString(CultureInfo.InvariantCulture));
        foreach (var cw in CivilWars)
        {
            var n = root.AddBlock("civil_war");
            n.Add("war", cw.War.Id.ToString(CultureInfo.InvariantCulture));
            n.Add("origin", cw.Origin.Tag);
            n.Add("rebel", cw.Rebel.Tag);
            n.Add("ideology", cw.Ideology);
        }
    }

    private void LoadSave(Node root)
    {
        var w = _game.World;
        lock (_wealth)
            if (root.Get("wealth") is { } wn)
            {
                _wealth.Clear();
                _wealthDay = wn.GetInt("day", int.MinValue);
                var vals = wn.GetList("values");
                for (int i = 0; i + 1 < vals.Count; i += 2)
                    if (w.GetCountry(vals[i]) is { } wc && double.TryParse(vals[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var wv)) _wealth[wc] = wv;
            }
        foreach (var n in root.GetAll("country"))
        {
            var c = w.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var p = new CountryPolitics { Ruling = n.GetString("ruling_party", "")!, TotalSeats = Math.Max(1, n.GetInt("total_seats", 100)) };
            p.Government.AddRange(n.GetList("government").Where(IdeologyByKey.ContainsKey));
            if (!IdeologyByKey.ContainsKey(p.Ruling)) p.Ruling = Ideologies.FirstOrDefault()?.Key ?? "";
            if (p.Government.Count == 0 || p.Government[0] != p.Ruling) { p.Government.Remove(p.Ruling); p.Government.Insert(0, p.Ruling); }
            ReadNumbers(n.Get("popularity"), p.Popularity);
            foreach (var i in Ideologies) if (!p.Popularity.ContainsKey(i.Key)) p.Popularity[i.Key] = 0;
            foreach (var k in p.Popularity.Keys.Where(k => !Ideologies.Any(i => i.Key.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList()) p.Popularity.Remove(k);
            // a save already holds normalized numbers: dividing again would change the last bits and a loaded game would drift from the one that was saved
            if (Math.Abs(p.Popularity.Values.Sum() - 1) > 1e-6) Normalize(p);
            foreach (var s in n.Get("seats")?.All ?? Enumerable.Empty<Node>()) if (IdeologyByKey.ContainsKey(s.Key) || IsMinorityParty(s.Key)) p.Seats[s.Key] = (int)s.AsDouble();
            if (GameDate.TryParse(n.GetString("last_election"), out var le)) p.LastElection = le;
            if (GameDate.TryParse(n.GetString("next_election"), out var ne)) p.NextElection = ne;
            if (n.Get("calm_until") != null) p.CalmUntilDay = n.GetInt("calm_until", int.MinValue);
            foreach (var grp in LawGroups) p.Laws[grp.Key] = grp.Default;
            foreach (var l in n.Get("laws")?.All ?? Enumerable.Empty<Node>())
                if (l.Value != null && Laws.TryGetValue(l.Value, out var law)) p.Laws[law.Group.Key] = law.Key;
            foreach (var l in n.Get("law_changed")?.All ?? Enumerable.Empty<Node>()) p.LawChangedDay[l.Key] = (int)l.AsDouble();
            foreach (var m in n.Get("ministers")?.All ?? Enumerable.Empty<Node>())
                if (w.GetCharacter(m.Value) is { } ch) p.Ministers[m.Key] = ch;
            foreach (var pn in n.Get("parties")?.All ?? Enumerable.Empty<Node>()) if (pn.Value != null) p.PartyNames[pn.Key] = pn.Value;
            ReadNumbers(n.Get("party_popularity"), p.PartyPopularity);
            foreach (var item in n.Get("party_seats")?.All ?? Enumerable.Empty<Node>()) p.PartySeats[item.Key] = (int)item.AsDouble();
            p.RulingOrganization = n.GetString("ruling_organization", "")!;
            p.GovernmentOrganizations.AddRange(n.GetList("government_organizations"));
            if (p.PartyPopularity.Count == 0) InitializePartyPopularity(c, p);
            else if (n.Get("popularity") == null) RebuildIdeologyPopularityFromParties(c, p); // a save holds both sets of numbers exactly: rebuilding would shift the last bits
            if (p.PartySeats.Count == 0) p.PartySeats = DistributeLegacySeats(c, p, p.Seats);
            if (p.GovernmentOrganizations.Count == 0 || string.IsNullOrWhiteSpace(p.RulingOrganization)) AssignGovernmentOrganizations(c, p);
            _state[c] = p;
            _modsDirty.Add(c);
        }
        foreach (var n in root.GetAll("movement"))
        {
            var c = w.GetCountry(n.GetString("country"));
            string ideology = n.GetString("ideology", "")!;
            if (c == null || !IdeologyByKey.ContainsKey(ideology)) continue;
            Movements.Add(new Movement
            {
                Country = c,
                Ideology = ideology,
                Demand = string.Equals(n.GetString("demand"), "legalize", StringComparison.OrdinalIgnoreCase) ? MovementDemand.Legalize : MovementDemand.Power,
                TargetLaw = n.GetString("law"),
                Progress = n.GetDouble("progress"),
                Started = GameDate.Parse(n.GetString("started") ?? "", w.Date),
                Warned = n.GetBool("warned"),
                // (saves from before movements had organisation and grievance: what the old progress implied)
                Organization = n.Has("organization") ? n.GetDouble("organization") : Math.Min(100, n.GetDouble("progress") * 0.8 + 20),
                Grievance = n.Has("grievance") ? n.GetDouble("grievance") : Math.Min(100, n.GetDouble("progress") * 0.6 + 25),
                Concessions = n.GetDouble("concessions"),
                Crackdowns = n.GetDouble("crackdowns"),
                Heat = n.GetDouble("heat"),
                Groups = n.GetAll("group").Select(gn => new InterestGroup { Key = gn.GetString("key", "")!, Strength = gn.GetDouble("strength"), Satisfaction = gn.Has("satisfaction") ? gn.GetDouble("satisfaction") : 0.5 })
                    .Where(g => g.Key.Length > 0).ToList(),
            });
        }
        foreach (var rv in root.Get("revolts")?.All ?? Enumerable.Empty<Node>())
            if (w.GetCountry(rv.Key) is { } rc) _revolts[rc] = (int)rv.AsDouble();
        foreach (var n in root.GetAll("civil_war"))
        {
            int id = n.GetInt("war", -1);
            var war = w.Wars.FirstOrDefault(x => x.Id == id);
            var o = w.GetCountry(n.GetString("origin"));
            var r = w.GetCountry(n.GetString("rebel"));
            if (war == null || o == null || r == null) continue;
            CivilWars.Add(new CivilWar { War = war, Origin = o, Rebel = r, Ideology = n.GetString("ideology", "")! });
        }
    }
}
