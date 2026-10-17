using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Society;

/// <summary>A government reform from the <c>reforms</c> block of <c>common/governments/*.txt</c>.</summary>
public sealed class ReformDef
{
    public string Key = "", Name = "", Desc = "";
    /// <summary>The government block it belongs to (default = open to every country).</summary>
    public string Government = "";
    /// <summary>Political power to adopt or drop it (0 = the rules' reform_cost).</summary>
    public double Cost;
    /// <summary>Reforms the country must hold first.</summary>
    public List<string> Requires = new();
    /// <summary>Also needs this to be true (country scope).</summary>
    public Node? Trigger;
    /// <summary>Reforms it replaces when adopted.</summary>
    public List<string> Removes = new();
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Depth in the reform tree (0 = no requirements).</summary>
    public int Tier;
    public int Order;
}

public sealed class IdeaDef
{
    public string Key = "", Name = "", Desc = "";
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A set of national ideas from <c>common/ideas/*.txt</c>, for countries, cultures or culture groups; the ideas unlock in order.</summary>
public sealed class IdeaSet
{
    public string Key = "", Name = "";
    public HashSet<string> Countries = new(StringComparer.OrdinalIgnoreCase), Cultures = new(StringComparer.OrdinalIgnoreCase), Groups = new(StringComparer.OrdinalIgnoreCase);
    public List<IdeaDef> Ideas = new();
    public bool IsGeneric => Countries.Count == 0 && Cultures.Count == 0 && Groups.Count == 0;
}

public sealed class GovState
{
    public List<string> Reforms = new();
    public int LastChangeDay = int.MinValue / 2;
    public int IdeasUnlocked;
    public string? SetKey;
    public int LastCorruptionEventDay = int.MinValue / 2;
}

/// <summary>
/// Government reforms, national ideas and corruption (government_reforms feature). A country holds a few reforms from its government's tree,
/// unlocks the seven ideas of its set one by one, and watches corruption: it grows with distant people and shrinks with centralisation.
/// Everything reaches the rest of the game as modifiers.
/// </summary>
public sealed class GovernmentSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, ReformDef> Reforms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _blockReforms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _startReforms = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<IdeaSet> IdeaSets = new();
    private readonly Dictionary<Country, GovState> _state = new();
    private readonly Dictionary<Country, Dictionary<string, double>> _mods = new();
    private readonly Dictionary<State, (float X, float Y)> _centres = new();

    public const string CorruptionKey = "corruption", IdeaPointsKey = "idea_points";

    public GovernmentSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "government_reforms", Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.GovernmentReforms);
    private SocietyRules Rules => _game.Rules.Society;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Reforms.Clear(); _blockReforms.Clear(); _startReforms.Clear(); IdeaSets.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/governments"))
            foreach (var g in root.All)
            {
                if (g.Children == null) continue;
                if (g.Get("start_reforms") != null) _startReforms[g.Key] = g.Get("start_reforms")!.Items().ToList();
                if (g.Get("reforms")?.Children == null) continue;
                if (!_blockReforms.TryGetValue(g.Key, out var list)) _blockReforms[g.Key] = list = new List<string>();
                foreach (var n in g.Get("reforms")!.All)
                {
                    if (n.Children == null) continue;
                    if (Reforms.ContainsKey(n.Key)) { Log.Error($"Reform '{n.Key}' is defined twice", n.Location); continue; }
                    Reforms[n.Key] = new ReformDef
                    {
                        Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!, Government = g.Key,
                        Cost = n.GetDouble("cost", 0), Requires = n.GetList("requires").ToList(), Removes = n.GetList("removes").ToList(),
                        Trigger = n.Get("trigger"), Modifier = SocietyData.ReadMods(n.Get("modifier")), Order = Reforms.Count,
                    };
                    list.Add(n.Key);
                }
            }
        foreach (var r in Reforms.Values)
        {
            foreach (var q in r.Requires.Concat(r.Removes))
                if (!Reforms.ContainsKey(q)) Log.Warn($"Reform '{r.Key}' refers to unknown reform '{q}'");
            r.Tier = TierOf(r, 0);
        }
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/ideas"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var set = new IdeaSet { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key) };
                foreach (var t in n.GetList("country")) set.Countries.Add(t);
                foreach (var t in n.GetList("culture")) set.Cultures.Add(t);
                foreach (var t in n.GetList("group")) set.Groups.Add(t);
                foreach (var i in (n.Get("ideas") ?? n).All)
                {
                    if (i.Children == null || i.Key is "country" or "culture" or "group" or "ideas") continue;
                    set.Ideas.Add(new IdeaDef { Key = i.Key, Name = i.GetString("name") ?? Localisation.GetOrPretty(i.Key), Desc = i.GetString("desc", "")!, Modifier = SocietyData.ReadMods(i.Get("modifier")) });
                }
                if (set.Ideas.Count == 0) { Log.Warn($"Idea set '{n.Key}' has no ideas", n.Location); continue; }
                IdeaSets.Add(set);
            }
    }

    private int TierOf(ReformDef r, int depth)
    {
        if (depth > 8 || r.Requires.Count == 0) return 0;
        return 1 + r.Requires.Where(Reforms.ContainsKey).Select(q => TierOf(Reforms[q], depth + 1)).DefaultIfEmpty(-1).Max();
    }

    // ------------------------------------------------------------------------------------- state
    public GovState Of(Country c)
    {
        if (_state.TryGetValue(c, out var s)) return s;
        s = new GovState();
        foreach (var block in BlocksOf(c))
            if (_startReforms.TryGetValue(block, out var start))
                foreach (var r in start) if (Reforms.ContainsKey(r) && !s.Reforms.Contains(r) && s.Reforms.Count < Rules.MaxReforms(_game)) s.Reforms.Add(r);
        _state[c] = s;
        return s;
    }

    /// <summary>The government blocks that give a country reforms: its government word, its ruling ideology (ruling_X) and default.</summary>
    public IEnumerable<string> BlocksOf(Country c)
    {
        if (!string.IsNullOrEmpty(c.Government)) yield return c.Government;
        if (_game.Politics.Enabled && _game.Politics.Of(c).Ruling is { Length: > 0 } r) yield return "ruling_" + r;
        yield return "default";
    }

    public List<ReformDef> AvailableReforms(Country c)
    {
        var res = new List<ReformDef>();
        foreach (var b in BlocksOf(c))
            if (_blockReforms.TryGetValue(b, out var list))
                foreach (var k in list) if (Reforms.TryGetValue(k, out var r) && !res.Contains(r)) res.Add(r);
        var held = Of(c).Reforms;
        foreach (var k in held) if (Reforms.TryGetValue(k, out var r) && !res.Contains(r)) res.Add(r);
        return res.OrderBy(r => r.Tier).ThenBy(r => r.Order).ToList();
    }

    public bool Has(Country c, string reform) => Enabled && Of(c).Reforms.Contains(reform, StringComparer.OrdinalIgnoreCase);
    public IEnumerable<ReformDef> HeldReforms(Country c) => Of(c).Reforms.Where(Reforms.ContainsKey).Select(k => Reforms[k]);

    public double Corruption(Country c) => c.Vars.Get(CorruptionKey, _game.Data.Defines.Get("corruption_start", 10));
    public void SetCorruption(Country c, double v) => c.Vars.Set(CorruptionKey, Math.Clamp(v, 0, 100));
    public double IdeaPoints(Country c) => c.Vars.Get(IdeaPointsKey, 0);
    public double CentralisationSteps(Country c) => _game.Modifiers.Sum(c, "centralisation");

    // ------------------------------------------------------------------------------------- reforms
    public int CooldownLeft(Country c) => Math.Max(0, Of(c).LastChangeDay + Rules.ReformCooldownDays(_game) - _game.World.Date.TotalDays);

    private string? CannotPay(Country c, double cost)
    {
        if (_game.Politics.Enabled && !_game.Politics.CanAfford(c, cost)) return $"Needs {cost:0} {_game.Politics.PowerName}";
        return null;
    }

    private void Pay(Country c, double cost)
    {
        if (cost > 0 && _game.Politics.Enabled && _game.Politics.PowerResource is { } r) _game.Resources.Set(c, r, _game.Resources.Get(c, r) - cost);
    }

    public string? CannotAdopt(Country c, ReformDef r)
    {
        if (!Enabled) return "Government reforms are off";
        var s = Of(c);
        if (s.Reforms.Contains(r.Key, StringComparer.OrdinalIgnoreCase)) return "Already adopted";
        if (!AvailableReforms(c).Contains(r)) return "Not open to this government";
        int cd = CooldownLeft(c);
        if (cd > 0) return $"Reform changed recently ({cd} days left)";
        foreach (var q in r.Requires) if (!s.Reforms.Contains(q, StringComparer.OrdinalIgnoreCase)) return "Needs " + (Reforms.TryGetValue(q, out var qd) ? qd.Name : q);
        if (r.Trigger != null && !_game.Script.EvalTrigger(r.Trigger, _game.Context(c))) return "Requirements not met";
        int after = s.Reforms.Count(k => !r.Removes.Contains(k, StringComparer.OrdinalIgnoreCase)) + 1;
        if (after > Rules.MaxReforms(_game)) return $"A country holds {Rules.MaxReforms(_game)} reforms at most";
        return CannotPay(c, Rules.ReformCost(_game, r));
    }

    public string? Adopt(Country c, ReformDef r)
    {
        var why = CannotAdopt(c, r);
        if (why != null) return why;
        Pay(c, Rules.ReformCost(_game, r));
        Apply(c, r);
        return null;
    }

    private void Apply(Country c, ReformDef r)
    {
        var s = Of(c);
        s.Reforms.RemoveAll(k => r.Removes.Contains(k, StringComparer.OrdinalIgnoreCase));
        s.Reforms.Add(r.Key);
        s.LastChangeDay = _game.World.Date.TotalDays;
        _mods.Remove(c);
        if (c.IsPlayer) _game.Notify($"Reform adopted: {r.Name}.", NotificationKind.Good);
        _game.Events.FireOnAction("on_reform_changed", c);
    }

    public string? CannotDrop(Country c, ReformDef r)
    {
        if (!Enabled) return "Government reforms are off";
        var s = Of(c);
        if (!s.Reforms.Contains(r.Key, StringComparer.OrdinalIgnoreCase)) return "Not adopted";
        int cd = CooldownLeft(c);
        if (cd > 0) return $"Reform changed recently ({cd} days left)";
        var need = s.Reforms.Select(k => Reforms.GetValueOrDefault(k)).FirstOrDefault(o => o != null && o != r && o.Requires.Contains(r.Key, StringComparer.OrdinalIgnoreCase));
        if (need != null) return $"{need.Name} needs it";
        return CannotPay(c, Rules.ReformCost(_game, r));
    }

    public string? Drop(Country c, ReformDef r)
    {
        var why = CannotDrop(c, r);
        if (why != null) return why;
        Pay(c, Rules.ReformCost(_game, r));
        var s = Of(c);
        s.Reforms.RemoveAll(k => k.Equals(r.Key, StringComparison.OrdinalIgnoreCase));
        s.LastChangeDay = _game.World.Date.TotalDays;
        _mods.Remove(c);
        _game.Events.FireOnAction("on_reform_changed", c);
        return null;
    }

    /// <summary>Gives or takes a reform without cost, cooldown or requirements (scripts and the console).</summary>
    public void Force(Country c, string reform, bool give)
    {
        if (!Reforms.TryGetValue(reform, out var r)) return;
        var s = Of(c);
        if (give) { s.Reforms.RemoveAll(k => r.Removes.Contains(k, StringComparer.OrdinalIgnoreCase)); if (!s.Reforms.Contains(r.Key)) s.Reforms.Add(r.Key); }
        else s.Reforms.RemoveAll(k => k.Equals(reform, StringComparison.OrdinalIgnoreCase));
        _mods.Remove(c);
    }

    // ------------------------------------------------------------------------------------- ideas
    public IdeaSet? SetOf(Country c)
    {
        var s = Of(c);
        if (s.SetKey != null && IdeaSets.FirstOrDefault(x => x.Key == s.SetKey) is { } known) return known;
        var culture = _game.Population.Enabled ? _game.Population.Of(c).Culture : null;
        var group = culture != null && _game.Population.CultureByKey.TryGetValue(culture, out var cd) ? cd.Group.Key : null;
        var set = IdeaSets.FirstOrDefault(x => x.Countries.Contains(c.Tag))
                  ?? (culture != null ? IdeaSets.FirstOrDefault(x => x.Cultures.Contains(culture)) : null)
                  ?? (group != null ? IdeaSets.FirstOrDefault(x => x.Groups.Contains(group)) : null)
                  ?? IdeaSets.FirstOrDefault(x => x.IsGeneric);
        s.SetKey = set?.Key;
        return set;
    }

    public int Unlocked(Country c) => Of(c).IdeasUnlocked;

    private Dictionary<string, double> ComputeFor(Country c)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in HeldReforms(c)) SocietyData.Add(m, r.Modifier, 1);
        if (SetOf(c) is { } set)
            for (int i = 0; i < Math.Min(Of(c).IdeasUnlocked, set.Ideas.Count); i++) SocietyData.Add(m, set.Ideas[i].Modifier, 1);
        // corruption eats the income of the state
        m["income"] = m.GetValueOrDefault("income") - Corruption(c) * Rules.CorruptionIncomePerPoint(_game);
        return m;
    }

    /// <summary>Country modifiers from reforms, ideas and corruption; the modifier system asks for it.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c || !c.Exists) return 0;
        if (!_mods.TryGetValue(c, out var m)) _mods[c] = m = ComputeFor(c);
        return m.GetValueOrDefault(key);
    }

    // ------------------------------------------------------------------------------------- monthly
    private (float X, float Y) CentreOf(State s)
    {
        if (_centres.TryGetValue(s, out var c)) return c;
        var land = s.Locations.Where(l => l.IsLand).ToList();
        if (land.Count == 0) land = s.Locations;
        return _centres[s] = land.Count == 0 ? (0, 0) : (land.Average(l => l.CenterX), land.Average(l => l.CenterY));
    }

    /// <summary>Millions of people living farther from the capital than the corruption range.</summary>
    public double MillionsBeyondRange(Country c)
    {
        if (c.Capital == null || !_game.Population.Enabled) return 0;
        double range = Rules.CorruptionRangeKm(_game), km = _game.Data.Defines.KmPerPixel;
        double people = 0;
        foreach (var s in c.OwnedStates)
        {
            if (_game.Titles.Enabled && _game.Titles.IsVassalHeld(s)) continue; // a vassal governs on the spot
            var (x, y) = CentreOf(s);
            double dx = x - c.Capital.CenterX, dy = y - c.Capital.CenterY;
            if (Math.Sqrt(dx * dx + dy * dy) * km > range) people += _game.Population.TotalOf(s);
        }
        return people / 1e6;
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        _mods.Clear();
        int today = _game.World.Date.TotalDays;
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            var s = Of(c);
            SetCorruption(c, Corruption(c) + Rules.CorruptionChange(_game, c, MillionsBeyondRange(c), CentralisationSteps(c)));
            var set = SetOf(c);
            if (set != null && s.IdeasUnlocked < Math.Min(Rules.IdeaSlots(_game), set.Ideas.Count))
            {
                double pts = IdeaPoints(c) + Rules.IdeaPointsPerMonth(_game, c);
                while (pts >= Rules.IdeaCost(_game) && s.IdeasUnlocked < Math.Min(Rules.IdeaSlots(_game), set.Ideas.Count))
                {
                    pts -= Rules.IdeaCost(_game);
                    var idea = set.Ideas[s.IdeasUnlocked++];
                    if (c.IsPlayer) _game.Notify($"National idea: {idea.Name}.", NotificationKind.Good);
                }
                c.Vars.Set(IdeaPointsKey, pts);
            }
            if (Corruption(c) >= Rules.CorruptionEventAt(_game) && today - s.LastCorruptionEventDay > 365 * 3)
            {
                s.LastCorruptionEventDay = today;
                _game.Events.FireOnAction("on_high_corruption", c);
            }
            if (!c.IsPlayer) AiReform(c);
            _mods.Remove(c); // corruption and ideas changed above
        }
    }

    private void AiReform(Country c)
    {
        if (CooldownLeft(c) > 0 || _game.Rngs.Politics.NextDouble() > 0.1) return;
        double reserve = _game.Data.Defines.Get("reform_ai_reserve", 50);
        ReformDef? best = null;
        double bestScore = 0;
        foreach (var r in AvailableReforms(c))
        {
            if (CannotAdopt(c, r) != null) continue;
            if (_game.Politics.Enabled && _game.Politics.PowerOf(c) < Rules.ReformCost(_game, r) + reserve) continue;
            double score = Score(r.Modifier) - r.Removes.Where(k => Has(c, k)).Sum(k => Score(Reforms[k].Modifier)) - r.Tier * 0.001;
            if (score > bestScore) { best = r; bestScore = score; }
        }
        if (best != null) Adopt(c, best);
    }

    private static double Score(Dictionary<string, double> m)
    {
        double v = 0;
        foreach (var kv in m)
            v += kv.Key.ToLowerInvariant() switch
            {
                "income" => kv.Value * 3, "manpower" => kv.Value * 2, "stability_target" => kv.Value / 10, "centralisation" => kv.Value * 0.3,
                "attack" or "defense" or "org_recovery" or "pop_growth" => kv.Value, "political_power_gain" => kv.Value / 10, _ => kv.Value * 0.2,
            };
        return v;
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterTrigger("has_reform", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && Has(c, n.Value ?? ""), (ctx, n) => $"Has the reform '{n.Value}'");
        s.RegisterEffect("add_reform", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) Force(c, n.Value ?? "", true); }, (ctx, n) => $"Adopts the reform '{n.Value}'");
        s.RegisterEffect("remove_reform", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) Force(c, n.Value ?? "", false); }, (ctx, n) => $"Drops the reform '{n.Value}'");
        s.RegisterValue("ideas_unlocked", (ctx, scope) => scope is Country c && Enabled ? Of(c).IdeasUnlocked : null);
        s.RegisterValue("reform_count", (ctx, scope) => scope is Country c && Enabled ? Of(c).Reforms.Count : null);
        s.RegisterValue("centralisation", (ctx, scope) => scope is Country c && Enabled ? CentralisationSteps(c) : null);
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        foreach (var kv in _state.OrderBy(k => k.Key.Tag, StringComparer.Ordinal))
        {
            var n = root.AddBlock("country");
            n.Add("tag", kv.Key.Tag);
            n.Add("reforms", string.Join(' ', kv.Value.Reforms), quoted: true);
            n.Add("changed", kv.Value.LastChangeDay.ToString(CultureInfo.InvariantCulture));
            n.Add("ideas", kv.Value.IdeasUnlocked.ToString(CultureInfo.InvariantCulture));
            if (kv.Value.SetKey != null) n.Add("set", kv.Value.SetKey);
            n.Add("event", kv.Value.LastCorruptionEventDay.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        Reset();
        foreach (var b in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(b.GetString("tag"));
            if (c == null) continue;
            _state[c] = new GovState
            {
                Reforms = (b.GetString("reforms") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(Reforms.ContainsKey).ToList(),
                LastChangeDay = (int)b.GetDouble("changed", int.MinValue / 2), IdeasUnlocked = (int)b.GetDouble("ideas"),
                SetKey = b.GetString("set"), LastCorruptionEventDay = (int)b.GetDouble("event", int.MinValue / 2),
            };
        }
    }

    private void Reset() { _state.Clear(); _mods.Clear(); }

    /// <summary>Hash of reforms, ideas and corruption for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 29;
        foreach (var c in _game.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            if (!_state.TryGetValue(c, out var s)) continue;
            h = h * 31 + s.Reforms.Count;
            h = h * 31 + s.IdeasUnlocked;
            h = h * 31 + (long)Math.Round(Corruption(c) * 100);
        }
        return h;
    }
}
