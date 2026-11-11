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
    private long _modsHour = long.MinValue;
    // What each culture gives alone, kept for as long as _mods: traditions change only when a culture is made, and many countries share a culture.
    private readonly Dictionary<string, Dictionary<string, double>> _cultureMods = new(StringComparer.OrdinalIgnoreCase);
    // What each culture gave the last time it was counted, with a number that goes up whenever that changes (kept across hours, unlike _cultureMods),
    // and what it was counted from: its traditions and weights and the values of their modifiers, in the order they were read.
    private sealed class CultureRecord
    {
        public Dictionary<string, double> Mods = null!;
        public int Version;
        public readonly List<(string Key, long Weight, TraditionDef? Def)> Read = new();
        public readonly List<(string Key, long Value)> Values = new();
    }
    private readonly Dictionary<string, CultureRecord> _cultureRecords = new(StringComparer.OrdinalIgnoreCase);

    // The values of _mods belong to _shareCache (ShareSnapshot.Result), so they are not emptied here.
    private void ClearMods()
    {
        _mods.Clear();
        _cultureMods.Clear();
    }
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
        Traditions.Clear(); _baseTraditions.Clear(); _modsHour = long.MinValue; _shareCache.Clear(); _cultureRecords.Clear();
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
        foreach (var s in c.OwnedStates.OrderBy(x => x.Key, StringComparer.Ordinal))
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

    // ComputeFor runs for every country every game hour; these keep it from allocating. Per thread, as the AI may run on several.
    [ThreadStatic] private static Dictionary<string, double>? _sharesScratch;
    [ThreadStatic] private static List<(State State, int At)>? _statesScratch;
    [ThreadStatic] private static List<string>? _culturesScratch;
    private static readonly Comparison<(State State, int At)> ByKeyStable = (a, b) =>
    {
        int k = string.CompareOrdinal(a.State.Key, b.State.Key);
        return k != 0 ? k : a.At.CompareTo(b.At);
    };

    /// <summary>
    /// The sum of <see cref="ModifiersOf"/> over <see cref="Shares"/>, in the same order and with the same arithmetic as
    /// those two, so the result is identical; only the temporary collections are reused.
    /// </summary>
    private Dictionary<string, double> ComputeFor(Country c)
    {
        if (!_shareCache.TryGetValue(c, out var k)) _shareCache[c] = k = new ShareSnapshot();
        bool same = k.Valid && SharesUnchanged(c, k);
        if (!same)
        {
            var shares = _sharesScratch ??= new(StringComparer.OrdinalIgnoreCase);
            var cultures = _culturesScratch ??= new();
            SharesInto(c, shares, cultures, k);
            cultures.Sort(StringComparer.Ordinal);
            k.Cultures.Clear(); k.Shares.Clear();
            foreach (var culture in cultures) { k.Cultures.Add(culture); k.Shares.Add(shares[culture]); }
        }
        // the same shares of cultures that each give the same as when the result was made: the result is the same
        same &= k.ResultVersions.Count == k.Cultures.Count;
        for (int i = 0; i < k.Cultures.Count; i++)
        {
            int version = CultureModsOf(k.Cultures[i]).Version;
            if (same && k.ResultVersions[i] != version) same = false;
        }
        if (same) return k.Result;
        var m = k.Result;
        m.Clear(); k.ResultVersions.Clear();
        for (int i = 0; i < k.Cultures.Count; i++)
        {
            var (cultureMods, version) = CultureModsOf(k.Cultures[i]);
            SocietyData.Add(m, cultureMods, k.Shares[i]);
            k.ResultVersions.Add(version);
        }
        return m;
    }

    // What a culture gives alone this hour (counted once an hour), and its version: when it reads the same traditions, weights and values as last
    // time, the sums are the same and last time's dictionary is kept.
    private (Dictionary<string, double> Mods, int Version) CultureModsOf(string culture)
    {
        if (_cultureMods.TryGetValue(culture, out var counted)) return (counted, _cultureRecords[culture].Version);
        var traditions = Of(culture).Traditions;
        if (!_cultureRecords.TryGetValue(culture, out var r)) _cultureRecords[culture] = r = new CultureRecord();
        if (r.Mods == null || !SameTraditions(traditions, r))
        {
            var cultureMods = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            r.Read.Clear(); r.Values.Clear();
            foreach (var kv in traditions)
            {
                Traditions.TryGetValue(kv.Key, out var t);
                r.Read.Add((kv.Key, BitConverter.DoubleToInt64Bits(kv.Value), t));
                if (t == null) continue;
                foreach (var x in t.Modifier) r.Values.Add((x.Key, BitConverter.DoubleToInt64Bits(x.Value)));
                SocietyData.Add(cultureMods, t.Modifier, kv.Value);
            }
            if (r.Mods == null || !SameValues(r.Mods, cultureMods)) { r.Mods = cultureMods; r.Version++; }
        }
        _cultureMods[culture] = r.Mods;
        return (r.Mods, r.Version);
    }

    private bool SameTraditions(Dictionary<string, double> traditions, CultureRecord r)
    {
        if (traditions.Count != r.Read.Count) return false;
        int i = 0, v = 0;
        foreach (var kv in traditions)
        {
            var (key, weight, def) = r.Read[i++];
            if (!ReferenceEquals(kv.Key, key) || BitConverter.DoubleToInt64Bits(kv.Value) != weight) return false;
            Traditions.TryGetValue(kv.Key, out var t);
            if (!ReferenceEquals(t, def)) return false;
            if (t == null) continue;
            foreach (var x in t.Modifier)
            {
                if (v >= r.Values.Count) return false;
                var (mk, mv) = r.Values[v++];
                if (!ReferenceEquals(x.Key, mk) || BitConverter.DoubleToInt64Bits(x.Value) != mv) return false;
            }
        }
        return v == r.Values.Count;
    }

    private static bool SameValues(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || BitConverter.DoubleToInt64Bits(v) != BitConverter.DoubleToInt64Bits(kv.Value)) return false;
        return true;
    }

    // What SharesInto read for a country (its states in list and key order, and the culture and size of every pop it counted) and what it
    // gave, sorted by culture. When everything it reads is the same again the shares are the same, so they are not added up again.
    private sealed class ShareSnapshot
    {
        public bool Valid;
        public readonly List<State> Owned = new();
        public readonly List<string> OwnedKeys = new();
        public readonly List<int> Order = new();
        public readonly List<int> PopCount = new(); // per state in Order; -1 = no pops
        public readonly List<string> PopCulture = new();
        public readonly List<long> PopSize = new(); // the bits of each size, so only an identical value matches
        public readonly List<string> Cultures = new();
        public readonly List<double> Shares = new();
        // the modifiers made from Shares, and the version of each culture's modifiers they were made with
        public readonly Dictionary<string, double> Result = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<int> ResultVersions = new();
    }
    private readonly Dictionary<Country, ShareSnapshot> _shareCache = new();

    private bool SharesUnchanged(Country c, ShareSnapshot k)
    {
        var owned = c.OwnedStates;
        if (owned.Count != k.Owned.Count) return false;
        for (int i = 0; i < owned.Count; i++)
            if (!ReferenceEquals(owned[i], k.Owned[i]) || !ReferenceEquals(owned[i].Key, k.OwnedKeys[i])) return false;
        int p = 0;
        for (int j = 0; j < k.Order.Count; j++)
        {
            var s = owned[k.Order[j]];
            bool has = _game.Population.HasPops(s);
            if (has != (k.PopCount[j] >= 0)) return false;
            if (!has) continue;
            var pops = _game.Population.Of(s).Pops;
            if (pops.Count != k.PopCount[j]) return false;
            foreach (var pop in pops)
            {
                if (!ReferenceEquals(pop.Culture, k.PopCulture[p]) || BitConverter.DoubleToInt64Bits(pop.Size) != k.PopSize[p]) return false;
                p++;
            }
        }
        return true;
    }

    /// <summary><see cref="Shares"/> into <paramref name="res"/>, with its keys in <paramref name="keys"/>; what it reads goes into <paramref name="record"/>.</summary>
    private void SharesInto(Country c, Dictionary<string, double> res, List<string> keys, ShareSnapshot record)
    {
        res.Clear();
        keys.Clear();
        record.Valid = false;
        record.Owned.Clear(); record.OwnedKeys.Clear(); record.Order.Clear(); record.PopCount.Clear(); record.PopCulture.Clear(); record.PopSize.Clear();
        double total = 0;
        var states = _statesScratch ??= new();
        states.Clear();
        for (int i = 0; i < c.OwnedStates.Count; i++) { states.Add((c.OwnedStates[i], i)); record.Owned.Add(c.OwnedStates[i]); record.OwnedKeys.Add(c.OwnedStates[i].Key); }
        states.Sort(ByKeyStable); // the order OrderBy(Key, Ordinal) gives
        foreach (var (s, at) in states)
        {
            record.Order.Add(at);
            if (!_game.Population.HasPops(s)) { record.PopCount.Add(-1); continue; }
            var pops = _game.Population.Of(s).Pops;
            record.PopCount.Add(pops.Count);
            foreach (var p in pops)
            {
                res[p.Culture] = res.GetValueOrDefault(p.Culture) + p.Size; total += p.Size;
                record.PopCulture.Add(p.Culture); record.PopSize.Add(BitConverter.DoubleToInt64Bits(p.Size));
            }
        }
        if (total <= 0)
        {
            var own = PrimaryCulture(c); // depends on more than the pops: never reused
            if (own != null) res[own] = 1;
            keys.AddRange(res.Keys);
            return;
        }
        keys.AddRange(res.Keys);
        foreach (var k in keys) res[k] /= total;
        record.Valid = true;
    }

    /// <summary>Country modifiers from the cultures of its people; the modifier system asks for it.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c || !c.Exists) return 0;
        long now = _game.World.Date.TotalHours;
        if (now != _modsHour) // per hour, so a reloaded game sees what the original did
        {
            ClearMods(); _modsHour = now;
        }
        if (!_mods.TryGetValue(c, out var m)) _mods[c] = m = ComputeFor(c);
        return m.GetValueOrDefault(key);
    }

    // ------------------------------------------------------------------------------------- monthly
    public void MonthlyTick()
    {
        if (!Enabled) return;
        ClearMods();
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
        ClearMods();
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
        _state.Clear(); _mixMonths.Clear(); ClearMods(); _shareCache.Clear(); _cultureRecords.Clear();
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
