using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Population;
using Burger.Core.Script;

namespace Burger.Core.Society;

/// <summary>A tradition from <c>common/traditions/*.txt</c>: a lasting trait of a culture that changes how its people fight and rule.</summary>
public sealed class TraditionDef
{
    public string Key = "", Name = "", Desc = "";
    public string? Icon;
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What one culture has: traditions with a weight (1 = whole, 0.5 = inherited from one parent).</summary>
public sealed class CultureState
{
    public string Key = "";
    public Dictionary<string, double> Traditions = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>For a culture that grew out of two others: its parents and the day it appeared.</summary>
    public string? ParentA, ParentB;
    public int EvolvedDay;
    public bool IsHybrid => ParentA != null;
}

/// <summary>
/// Cultural traditions (culture_traditions feature). Every culture has three traditions;
/// their modifiers reach a country in proportion to the people of that culture in it. Two cultures that share a state for 50 years become a new culture.
/// </summary>
public sealed class CultureSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, TraditionDef> Traditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CultureState> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _baseTraditions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _mixMonths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, Dictionary<string, double>> _mods = new();
    /// <summary>Cultures that appeared during the game, newest last (for the culture card).</summary>
    public readonly List<string> Evolved = new();

    public CultureSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "culture_traditions", Early = true, Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.CultureTraditions) && _game.Population.Enabled;
    private SocietyRules Rules => _game.Rules.Society;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Traditions.Clear(); _baseTraditions.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/traditions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Traditions[n.Key] = new TraditionDef
                {
                    Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!,
                    Icon = n.GetString("icon"), Modifier = SocietyData.ReadMods(n.Get("modifier")),
                };
            }
        // each culture names its three traditions; a culture group may name defaults for the cultures that do not
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/cultures"))
            foreach (var g in root.All)
            {
                if (g.Children == null) continue;
                var groupDefault = g.Get("traditions")?.Items().ToList();
                foreach (var c in g.All)
                {
                    if (c.Children == null || c.Key is "color" or "traditions") continue;
                    var list = c.Get("traditions")?.Items().ToList() ?? groupDefault;
                    if (list != null) _baseTraditions[c.Key] = list;
                }
            }
        foreach (var kv in _baseTraditions)
            foreach (var t in kv.Value)
                if (!Traditions.ContainsKey(t)) Log.Warn($"Culture '{kv.Key}' has unknown tradition '{t}'");
    }

    // ------------------------------------------------------------------------------------- state
    public CultureState Of(string cultureKey)
    {
        if (_state.TryGetValue(cultureKey, out var s)) return s;
        s = new CultureState { Key = cultureKey };
        if (_baseTraditions.TryGetValue(cultureKey, out var list))
            foreach (var t in list) if (Traditions.ContainsKey(t)) s.Traditions[t] = 1;
        _state[cultureKey] = s;
        return s;
    }

    public IEnumerable<TraditionDef> TraditionsOf(string cultureKey) => Of(cultureKey).Traditions.Keys.Where(Traditions.ContainsKey).Select(k => Traditions[k]);
    public string? PrimaryCulture(Country c) => _game.Population.Of(c).Culture;

    // ------------------------------------------------------------------------------------- modifiers
    /// <summary>People of a country by culture, as shares (home states; colonies count too when it has no home).</summary>
    public Dictionary<string, double> Shares(Country c)
    {
        var res = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double total = 0;
        foreach (var s in c.OwnedStates)
        {
            if (!_game.Population.HasPops(s)) continue;
            foreach (var p in _game.Population.Of(s).Pops) { res[p.Culture] = res.GetValueOrDefault(p.Culture) + p.Size; total += p.Size; }
        }
        if (total <= 0)
        {
            var own = PrimaryCulture(c);
            if (own != null) res[own] = 1;
            return res;
        }
        foreach (var k in res.Keys.ToList()) res[k] /= total;
        return res;
    }

    /// <summary>What the culture gives alone: the modifiers of its traditions and innovations at their weights.</summary>
    public Dictionary<string, double> ModifiersOf(string cultureKey)
    {
        var s = Of(cultureKey);
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in s.Traditions) if (Traditions.TryGetValue(kv.Key, out var t)) SocietyData.Add(m, t.Modifier, kv.Value);
        return m;
    }

    private Dictionary<string, double> ComputeFor(Country c)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (culture, share) in Shares(c)) SocietyData.Add(m, ModifiersOf(culture), share);
        return m;
    }

    /// <summary>Country modifiers from the cultures of its people; the modifier system asks for it.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c || !c.Exists) return 0;
        if (!_mods.TryGetValue(c, out var m)) _mods[c] = m = ComputeFor(c);
        return m.GetValueOrDefault(key);
    }

    // ------------------------------------------------------------------------------------- monthly
    public void MonthlyTick()
    {
        if (!Enabled) return;
        _mods.Clear();
        TrackMixing();
    }

    /// <summary>Counts how long each state keeps two cultures that each hold the evolution share; at the limit they become one new culture.</summary>
    private void TrackMixing()
    {
        var pop = _game.Population;
        double need = Rules.EvolutionShare(_game);
        int limit = Rules.EvolutionMonths(_game);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var due = new List<(State, string, string)>();
        foreach (var s in _game.World.States)
        {
            if (s.Owner == null || !pop.HasPops(s)) continue;
            var by = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double total = 0;
            foreach (var p in pop.Of(s).Pops) { by[p.Culture] = by.GetValueOrDefault(p.Culture) + p.Size; total += p.Size; }
            if (total <= 0 || by.Count < 2) continue;
            var top = by.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(2).ToList();
            if (top[1].Value / total < need) continue;
            string a = top[0].Key, b = top[1].Key;
            if (string.Compare(a, b, StringComparison.Ordinal) > 0) (a, b) = (b, a);
            string key = s.Key + "|" + a + "|" + b;
            seen.Add(key);
            int months = _mixMonths.GetValueOrDefault(key) + 1;
            _mixMonths[key] = months;
            if (months >= limit) due.Add((s, top[0].Key, top[1].Key));
        }
        foreach (var k in _mixMonths.Keys.Where(k => !seen.Contains(k)).ToList()) _mixMonths.Remove(k);
        foreach (var (s, major, minor) in due) Evolve(s, major, minor);
    }

    /// <summary>Months a state has kept its two main cultures mixed (for the culture card).</summary>
    public int MixingMonths(State s, string a, string b)
    {
        if (string.Compare(a, b, StringComparison.Ordinal) > 0) (a, b) = (b, a);
        return _mixMonths.GetValueOrDefault(s.Key + "|" + a + "|" + b);
    }

    /// <summary>Months to the evolution of the two main cultures of a state (null when they are not mixing).</summary>
    public (string A, string B, int Months)? MixingIn(State s)
    {
        var pop = _game.Population;
        if (!pop.HasPops(s)) return null;
        var by = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double total = 0;
        foreach (var p in pop.Of(s).Pops) { by[p.Culture] = by.GetValueOrDefault(p.Culture) + p.Size; total += p.Size; }
        if (total <= 0 || by.Count < 2) return null;
        var top = by.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(2).ToList();
        if (top[1].Value / total < Rules.EvolutionShare(_game)) return null;
        return (top[0].Key, top[1].Key, MixingMonths(s, top[0].Key, top[1].Key));
    }

    /// <summary>Makes (or finds) the culture of two others and turns their people in the state into it.</summary>
    private void Evolve(State s, string major, string minor)
    {
        var pop = _game.Population;
        if (!pop.CultureByKey.TryGetValue(major, out var a) || !pop.CultureByKey.TryGetValue(minor, out var b)) return;
        bool isNew = !pop.CultureByKey.ContainsKey(HybridKey(a.Key, b.Key));
        var hybrid = Hybrid(a, b);
        foreach (var p in pop.Of(s).Pops)
            if (p.Culture.Equals(a.Key, StringComparison.OrdinalIgnoreCase) || p.Culture.Equals(b.Key, StringComparison.OrdinalIgnoreCase)) p.Culture = hybrid.Key;
        pop.InvalidateMajorities();
        pop.Invalidate();
        _mods.Clear();
        foreach (var k in _mixMonths.Keys.Where(k => k.StartsWith(s.Key + "|", StringComparison.OrdinalIgnoreCase)).ToList()) _mixMonths.Remove(k);
        _game.NotifyPlayer(isNew ? $"A new culture is born in {s.DisplayName}: the {hybrid.Name}." : $"{hybrid.Name} spreads in {s.DisplayName}.", NotificationKind.Good, s.Owner);
        _game.Events.FireOnAction("on_culture_evolved", s.Owner, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["state"] = s });
        _game.RaiseMapChanged();
    }

    private static string HybridKey(string a, string b) => string.Compare(a, b, StringComparison.Ordinal) <= 0 ? a + "_" + b : b + "_" + a;

    private CultureDef Hybrid(CultureDef a, CultureDef b)
    {
        var pop = _game.Population;
        string key = HybridKey(a.Key, b.Key);
        if (pop.CultureByKey.TryGetValue(key, out var existing)) return existing;
        var def = new CultureDef
        {
            Key = key, Name = $"{a.Name}-{b.Name}", Group = a.Group, Color = a.Color.Lerp(b.Color, 0.5), Religion = a.Religion ?? b.Religion,
        };
        RegisterHybrid(def, a.Key, b.Key, _game.World.Date.TotalDays);
        return def;
    }

    private void RegisterHybrid(CultureDef def, string a, string b, int day)
    {
        var pop = _game.Population;
        def.Group.Cultures.Add(def);
        pop.CultureByKey[def.Key] = def;
        double w = Rules.ParentWeight(_game);
        var s = new CultureState { Key = def.Key, ParentA = a, ParentB = b, EvolvedDay = day };
        foreach (var parent in new[] { a, b })
        {
            var ps = Of(parent);
            foreach (var kv in ps.Traditions) s.Traditions[kv.Key] = s.Traditions.GetValueOrDefault(kv.Key) + kv.Value * w;
        }
        _state[def.Key] = s;
        Evolved.Add(def.Key);
        // countries of either parent culture accept the new one
        foreach (var c in _game.World.Countries)
        {
            var cp = pop.Of(c);
            if (cp.Culture != null && (cp.Culture.Equals(a, StringComparison.OrdinalIgnoreCase) || cp.Culture.Equals(b, StringComparison.OrdinalIgnoreCase))) cp.Accepted.Add(def.Key);
        }
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var s in _state.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var n = root.AddBlock("culture");
            n.Add("key", s.Key);
            n.Add("traditions", string.Join(' ', s.Traditions.Select(kv => kv.Key + ":" + F(kv.Value))));
            if (s.IsHybrid && _game.Population.CultureByKey.TryGetValue(s.Key, out var d))
            {
                n.Add("parent_a", s.ParentA!); n.Add("parent_b", s.ParentB!);
                n.Add("day", s.EvolvedDay.ToString(CultureInfo.InvariantCulture));
                n.Add("name", d.Name, quoted: true);
                n.Add("color", $"{d.Color.R} {d.Color.G} {d.Color.B}", quoted: true);
                n.Add("group", d.Group.Key);
                if (d.Religion != null) n.Add("religion", d.Religion);
            }
        }
        foreach (var kv in _mixMonths)
        {
            var n = root.AddBlock("mix");
            n.Add("key", kv.Key, quoted: true);
            n.Add("months", kv.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static Dictionary<string, double> ReadWeights(string? text)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = part.LastIndexOf(':');
            if (at > 0 && double.TryParse(part[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) d[part[..at]] = w;
        }
        return d;
    }

    private void Load(Node root)
    {
        Reset();
        var pop = _game.Population;
        foreach (var b in root.GetAll("culture"))
        {
            string key = b.GetString("key") ?? "";
            if (key.Length == 0) continue;
            if (b.Has("parent_a") && !pop.CultureByKey.ContainsKey(key))
            {
                var grp = pop.CultureGroups.FirstOrDefault(g => g.Key == b.GetString("group")) ?? pop.CultureGroups.FirstOrDefault();
                if (grp != null)
                {
                    var def = new CultureDef { Key = key, Name = b.GetString("name") ?? key, Group = grp, Religion = b.GetString("religion") };
                    var cp = (b.GetString("color") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    def.Color = cp.Length == 3 && int.TryParse(cp[0], out var cr) && int.TryParse(cp[1], out var cg) && int.TryParse(cp[2], out var cb) ? new Rgb(cr, cg, cb) : grp.Color;
                    grp.Cultures.Add(def);
                    pop.CultureByKey[key] = def;
                }
            }
            var s = new CultureState { Key = key, ParentA = b.GetString("parent_a"), ParentB = b.GetString("parent_b"), EvolvedDay = (int)b.GetDouble("day") };
            s.Traditions = ReadWeights(b.GetString("traditions"));
            _state[key] = s;
            if (s.IsHybrid) Evolved.Add(key);
        }
        foreach (var b in root.GetAll("mix")) _mixMonths[b.GetString("key") ?? ""] = (int)b.GetDouble("months");
    }

    private void Reset()
    {
        // cultures made during a game go with it; the ones from the data files stay
        var pop = _game.Population;
        foreach (var key in Evolved)
            if (pop.CultureByKey.Remove(key, out var def)) def.Group.Cultures.Remove(def);
        Evolved.Clear();
        _state.Clear(); _mixMonths.Clear(); _mods.Clear();
    }

    /// <summary>Hash of the culture state for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 23;
        foreach (var s in _state.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            h = h * 31 + s.Traditions.Count;
        }
        return h;
    }
}
