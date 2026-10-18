using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Mechanics;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Technology;

namespace Burger.Core.Development;

/// <summary>An institution from <c>common/institutions/*.txt</c>: a new way of doing things that appears in one state and spreads to its neighbors.</summary>
public sealed class InstitutionDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Key of the state where it appears.</summary>
    public string Origin = "";
    /// <summary>Share of the spreading neighbor's presence a state gains each month (0.02 = about 4 years from one state to the next).</summary>
    public double SpreadPerMonth = 0.02;
    /// <summary>When it appears (before the start date: the world is spread out for the years since).</summary>
    public GameDate Appears;
    /// <summary>A state takes it up only with at least this much literacy, urbanization and development.</summary>
    public double MinLiteracy, MinUrbanization, MinDevelopment;
    /// <summary>State modifiers while the state has adopted it (tax, manpower, literacy_growth, urban_growth ...).</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    public int Order;
    public Rgb Color = new(200, 200, 200);
}

/// <summary>
/// Overridable development rules (the development feature). The currency of development is a hook: the default is the politics resource
/// (political power), a game with its own administrative, diplomatic and military points overrides <see cref="Currency"/> and <see cref="Cost"/>.
/// </summary>
public class DevelopmentRules
{
    /// <summary>What development is paid with: the resource named by the define development_resource (default: the politics resource). Null means gold.</summary>
    public virtual ResourceDef? Currency(Game game, Country c)
    {
        string key = game.Data.Defines.Str("development_resource", "");
        if (key.Length > 0) return game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        return game.Politics.PowerResource;
    }

    /// <summary>Cost of the next level of a state: development_cost_base (50) + development_cost_per_level (10) times its level.</summary>
    public virtual double Cost(Game game, Country c, State s)
    {
        var d = game.Data.Defines;
        double cost = d.Get("development_cost_base", 50) + d.Get("development_cost_per_level", 10) * game.Development.Level(s);
        return Math.Max(1, cost * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "development_cost")));
    }

    public virtual int MaxLevel(Game game) => (int)game.Data.Defines.Get("development_max_level", 30);

    /// <summary>Share of tax, share of manpower and rooms (building slots) each level gained adds.</summary>
    public virtual double TaxPerLevel(Game game) => game.Data.Defines.Get("development_tax_per_level", 0.05);
    public virtual double ManpowerPerLevel(Game game) => game.Data.Defines.Get("development_manpower_per_level", 0.03);
    public virtual double RoomPerLevel(Game game) => game.Data.Defines.Get("development_room_per_level", 2);

    /// <summary>First level of a state: from its tax (a rich state is more developed).</summary>
    public virtual int StartLevel(Game game, State s) => Math.Clamp(1 + (int)Math.Round(s.Tax * 2), 1, MaxLevel(game));

    /// <summary>First literacy (0-1): scribes and priests, nobles and a few craftspeople can read.</summary>
    public virtual double StartLiteracy(Game game, State s, Func<string, double> share) =>
        Math.Clamp(0.005 + 1.0 * share("clergy") + 0.8 * share("nobles") + 0.1 * share("artisans"), 0, 1);

    /// <summary>First urbanization (0-1): craft and trade people.</summary>
    public virtual double StartUrbanization(Game game, State s, Func<string, double> share) => Math.Clamp(1.2 * share("artisans"), 0, 1);

    /// <summary>Schools: scribes, priests and nobles are enough of the people for learning to be taught (literacy rises by development_literacy_per_year, 0.002).</summary>
    public virtual bool HasSchools(Game game, State s, Func<string, double> share) => share("clergy") + 0.5 * share("nobles") >= game.Data.Defines.Get("development_school_share", 0.025);

    /// <summary>Markets: craft and trade people are enough of the people for markets (urbanization rises by development_urban_per_year, 0.001).</summary>
    public virtual bool HasMarkets(Game game, State s, Func<string, double> share) => share("artisans") >= game.Data.Defines.Get("development_market_share", 0.06);

    /// <summary>What the AI develops: the best tax for its price.</summary>
    public virtual double AiScore(Game game, Country c, State s) => (s.Tax + 0.2) / Cost(game, c, s);
}

/// <summary>
/// Development (development feature): points spent on states, literacy and urbanization that rise on their own, institutions that spread from
/// state to state, and the technology costs they and the neighbors change.
/// </summary>
public sealed class DevelopmentSystem
{
    private readonly Game _game;
    public readonly List<InstitutionDef> Institutions = new();
    /// <summary>Technology group of each country tag (common/tech_groups): countries in a group learn from each other.</summary>
    public readonly Dictionary<string, string> GroupOf = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> GroupName = new(StringComparer.OrdinalIgnoreCase);

    private int[] _level = Array.Empty<int>(), _start = Array.Empty<int>();
    private float[] _literacy = Array.Empty<float>(), _urban = Array.Empty<float>();
    private readonly Dictionary<string, float[]> _presence = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _appeared = new(StringComparer.OrdinalIgnoreCase);
    private List<State>[]? _links;
    private int _cacheDay = int.MinValue;
    private readonly Dictionary<(Country, string), double> _shareCache = new();
    private readonly Dictionary<Country, List<Country>> _neighbourCache = new();

    public DevelopmentSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "development", Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Development);
    private DevelopmentRules Rules => _game.Rules.Development;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Institutions.Clear();
        GroupOf.Clear();
        GroupName.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/institutions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var d = new InstitutionDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Origin = n.GetString("origin_state", "")!,
                    SpreadPerMonth = n.GetDouble("spread_per_month", 0.02),
                    Appears = GameDate.Parse(n.GetString("appears") ?? "", new GameDate(1, 1, 1)),
                    Order = Institutions.Count,
                };
                if (n.Get("requires") is { Children: not null } rq)
                {
                    d.MinLiteracy = rq.GetDouble("literacy", 0);
                    d.MinUrbanization = rq.GetDouble("urbanization", 0);
                    d.MinDevelopment = rq.GetDouble("development", 0);
                }
                if (n.Get("modifier") is { Children: not null } m)
                    foreach (var kv in m.All) if (kv.Value != null) d.Modifier[kv.Key] = kv.AsDouble(0);
                if (Rgb.TryParse(n.Get("color"), out var rc)) d.Color = rc;
                Institutions.RemoveAll(x => x.Key == d.Key);
                Institutions.Add(d);
            }
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/tech_groups"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                GroupName[n.Key] = n.GetString("name") ?? Localisation.GetOrPretty(n.Key);
                foreach (var tag in n.GetList("countries")) GroupOf[tag] = n.Key;
            }
    }

    // ------------------------------------------------------------------------------------- questions
    private int N => _game.World.States.Count;
    public int Level(State s) => s.Index < _level.Length ? _level[s.Index] : 1;
    public int StartLevel(State s) => s.Index < _start.Length ? _start[s.Index] : 1;
    /// <summary>Levels spent on the state since the game began (what the bonuses are paid for).</summary>
    public int Gained(State s) => Math.Max(0, Level(s) - StartLevel(s));
    public double Literacy(State s) => s.Index < _literacy.Length ? _literacy[s.Index] : 0;
    public double Urbanization(State s) => s.Index < _urban.Length ? _urban[s.Index] : 0;
    public double Presence(InstitutionDef i, State s) => _presence.TryGetValue(i.Key, out var p) && s.Index < p.Length ? p[s.Index] : 0;
    public bool Adopted(InstitutionDef i, State s) => Presence(i, s) >= 0.999;
    public bool HasAppeared(InstitutionDef i) => _appeared.Contains(i.Key);
    public string GroupOfCountry(Country c) => GroupOf.GetValueOrDefault(c.Tag, "other");

    /// <summary>Average literacy and urbanization of a country's states, weighted by their wealth.</summary>
    public (double Literacy, double Urbanization) Averages(Country c)
    {
        double w = 0, l = 0, u = 0;
        foreach (var s in c.OwnedStates) { double x = s.Tax + 0.1; w += x; l += Literacy(s) * x; u += Urbanization(s) * x; }
        return w > 0 ? (l / w, u / w) : (0, 0);
    }

    /// <summary>Rooms (building slots) the development of a state gives.</summary>
    public double Room(State s) => Gained(s) * Rules.RoomPerLevel(_game);

    /// <summary>State modifiers from development (tax, manpower) and from the institutions the state has adopted; the modifier system asks for it.</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not State s) return 0;
        double v = 0;
        if (key.Equals("tax", StringComparison.OrdinalIgnoreCase)) v += Gained(s) * Rules.TaxPerLevel(_game);
        else if (key.Equals("manpower", StringComparison.OrdinalIgnoreCase)) v += Gained(s) * Rules.ManpowerPerLevel(_game);
        foreach (var i in Institutions)
            if (i.Modifier.TryGetValue(key, out var x) && Adopted(i, s)) v += x;
        return v;
    }

    // ------------------------------------------------------------------------------------- spending
    public ResourceDef? Currency(Country c) => Rules.Currency(_game, c);
    public double Cost(Country c, State s) => Rules.Cost(_game, c, s);

    public string? CannotDevelop(Country c, State s)
    {
        if (!Enabled) return "Development is off";
        if (s.Owner != c) return "Not your state";
        if (s.ControlledShare(c) < 0.5) return "The state is occupied";
        if (Level(s) >= Rules.MaxLevel(_game)) return "Fully developed";
        double cost = Cost(c, s);
        var cur = Currency(c);
        if (cur != null ? _game.Resources.Get(c, cur) < cost : c.Gold < cost) return $"Needs {cost:0} {(cur?.Name.ToLowerInvariant() ?? "gold")}";
        return null;
    }

    public string? Develop(Country c, State s)
    {
        var why = CannotDevelop(c, s);
        if (why != null) return why;
        double cost = Cost(c, s);
        if (Currency(c) is { } cur) _game.Resources.Set(c, cur, _game.Resources.Get(c, cur) - cost); else c.Gold -= cost;
        _level[s.Index]++;
        return null;
    }

    // ------------------------------------------------------------------------------------- technology
    private void CheckCache()
    {
        int day = _game.World.Date.TotalDays / 28;
        if (day == _cacheDay) return;
        _cacheDay = day;
        _shareCache.Clear();
        _neighbourCache.Clear();
    }

    /// <summary>Share (0-1) of a country's wealth that sits in states that have adopted the institution.</summary>
    public double AdoptedShare(Country c, InstitutionDef i)
    {
        CheckCache();
        if (_shareCache.TryGetValue((c, i.Key), out var v)) return v;
        double w = 0, a = 0;
        foreach (var s in c.OwnedStates) { double x = s.Tax + 0.1; w += x; if (Adopted(i, s)) a += x; }
        return _shareCache[(c, i.Key)] = w > 0 ? a / w : 0;
    }

    /// <summary>Countries that share a border with this one.</summary>
    public List<Country> Neighbours(Country c)
    {
        CheckCache();
        if (_neighbourCache.TryGetValue(c, out var l)) return l;
        var set = new HashSet<Country>();
        foreach (var s in c.OwnedStates)
            foreach (var n in _game.Population.NeighbourStates(s))
                if (n.Owner is { } o && o != c) set.Add(o);
        return _neighbourCache[c] = set.OrderBy(x => x.Tag, StringComparer.Ordinal).ToList();
    }

    /// <summary>Neighbors of the same technology group that know a technology.</summary>
    public int NeighboursKnowing(Country c, string tech)
    {
        string g = GroupOfCountry(c);
        return Neighbours(c).Count(n => GroupOfCountry(n) == g && _game.Tech.Has(n, tech));
    }

    /// <summary>
    /// Factor on the cost of a technology: its institution (technology key <c>institution</c>) makes it 20% cheaper where the country has adopted it
    /// (development_tech_adopted, -0.2) and 50% dearer where it has not (development_tech_missing, 0.5), by the share of the country that has; and 10%
    /// cheaper when three neighbors of its group know it (development_diffusion 0.1, development_diffusion_neighbours 3).
    /// </summary>
    public double TechCostFactor(Country c, TechDef t)
    {
        if (!Enabled) return 1;
        var d = _game.Data.Defines;
        double f = 1;
        if (t.Institution.Length > 0 && Institutions.FirstOrDefault(i => i.Key.Equals(t.Institution, StringComparison.OrdinalIgnoreCase)) is { } inst)
        {
            double share = HasAppeared(inst) ? AdoptedShare(c, inst) : 0;
            f *= 1 + d.Get("development_tech_adopted", -0.2) * share + d.Get("development_tech_missing", 0.5) * (1 - share);
        }
        if (NeighboursKnowing(c, t.Key) >= (int)d.Get("development_diffusion_neighbours", 3)) f *= 1 - d.Get("development_diffusion", 0.1);
        return f;
    }

    // ------------------------------------------------------------------------------------- start and monthly
    private List<State>[] Links()
    {
        if (_links != null && _links.Length == N) return _links;
        var links = new List<State>[N];
        foreach (var s in _game.World.States)
        {
            var set = new HashSet<State>(_game.Population.NeighbourStates(s));
            // across narrow water: coast to coast within two sea provinces
            var frontier = s.Locations.Where(l => l.IsLand).SelectMany(l => l.Neighbors).Select(a => a.To).Where(t => t.IsWater).Distinct().ToList();
            var seen = new HashSet<Location>(frontier);
            for (int depth = 0; depth < 2; depth++)
            {
                var next = new List<Location>();
                foreach (var w in frontier)
                    foreach (var a in w.Neighbors)
                    {
                        if (a.To.State is { } o && o != s && a.To.IsLand) set.Add(o);
                        else if (a.To.IsWater && seen.Add(a.To)) next.Add(a.To);
                    }
                frontier = next;
            }
            links[s.Index] = set.OrderBy(x => x.Index).ToList();
        }
        return _links = links;
    }

    private Func<string, double> Shares(State s)
    {
        var pop = _game.Population;
        if (!pop.Enabled || !pop.ClassesEnabled || !pop.HasPops(s)) return _ => 0;
        var sp = pop.Of(s);
        double total = Math.Max(1, sp.Total);
        var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in sp.Pops) if (p.Type != null) byType[p.Type] = byType.GetValueOrDefault(p.Type) + p.Size;
        return k => byType.GetValueOrDefault(k) / total;
    }

    public void OnGameStart()
    {
        Reset();
        if (!Enabled) return;
        int n = N;
        _level = new int[n]; _start = new int[n]; _literacy = new float[n]; _urban = new float[n];
        foreach (var s in _game.World.States)
        {
            var share = Shares(s);
            _start[s.Index] = _level[s.Index] = Rules.StartLevel(_game, s);
            _literacy[s.Index] = (float)Rules.StartLiteracy(_game, s, share);
            _urban[s.Index] = (float)Rules.StartUrbanization(_game, s, share);
        }
        foreach (var i in Institutions)
        {
            if (_game.World.States.All(s => !s.Key.Equals(i.Origin, StringComparison.OrdinalIgnoreCase))) { Log.Error($"Institution '{i.Key}': unknown origin_state '{i.Origin}'"); continue; }
            _presence[i.Key] = new float[n];
        }
        // the institutions that appeared before the game began have been spreading since: run the months in between
        var start = _game.World.Date;
        var spreading = Institutions.Where(i => _presence.ContainsKey(i.Key) && i.Appears <= start).ToList();
        foreach (var i in spreading) Appear(i);
        Links();   // built once, before the institutions (which are independent of each other) spread on every core
        Parallel.ForEach(spreading, i => CatchUp(i, Math.Min(2400, (start.TotalDays - i.Appears.TotalDays) / 30)));
    }

    /// <summary>
    /// The same as <paramref name="months"/> calls of <see cref="Spread"/> while nothing else changes (as at the start of a game), without looking at the states that can never take the
    /// institution, and stopping once nothing moves any more.
    /// </summary>
    private void CatchUp(InstitutionDef i, int months)
    {
        var p = _presence[i.Key];
        var links = Links();
        var idx = new List<int>();
        var rate = new List<float>();
        foreach (var s in _game.World.States)
        {
            if (_literacy[s.Index] < i.MinLiteracy || _urban[s.Index] < i.MinUrbanization || _level[s.Index] < i.MinDevelopment) continue;
            double receptive = Math.Clamp(Math.Max(_urban[s.Index] / 0.08, _level[s.Index] / 8.0), 0.1, 1.0);
            idx.Add(s.Index);
            rate.Add((float)(i.SpreadPerMonth * Math.Max(0.03, receptive)));
        }
        int n = idx.Count;
        var at = idx.ToArray();
        var rt = rate.ToArray();
        var near = new int[n][];
        for (int k = 0; k < n; k++) near[k] = links[at[k]].Select(o => o.Index).ToArray();
        var delta = new float[n];
        for (int m = 0; m < months; m++)
        {
            bool any = false;
            for (int k = 0; k < n; k++)
            {
                delta[k] = 0;
                if (p[at[k]] >= 1) continue;
                float best = 0;
                var nb = near[k];
                for (int j = 0; j < nb.Length; j++) { float v = p[nb[j]]; if (v > best) best = v; }
                if (best <= 0) continue;
                delta[k] = rt[k] * best;
                any = true;
            }
            if (!any) break;
            for (int k = 0; k < n; k++) if (delta[k] > 0) p[at[k]] = Math.Min(1f, p[at[k]] + delta[k]);
        }
    }

    private void Appear(InstitutionDef i)
    {
        _appeared.Add(i.Key);
        var o = _game.World.States.First(s => s.Key.Equals(i.Origin, StringComparison.OrdinalIgnoreCase));
        _presence[i.Key][o.Index] = 1;
    }

    /// <summary>A month of spreading: a state gains the greatest presence of its neighbors times the spread rate, when it is developed enough.</summary>
    private void Spread(InstitutionDef i)
    {
        var p = _presence[i.Key];
        var links = Links();
        var delta = new float[p.Length];
        foreach (var s in _game.World.States)
        {
            if (p[s.Index] >= 1) continue;
            float best = 0;
            foreach (var o in links[s.Index]) if (p[o.Index] > best) best = p[o.Index];
            if (best <= 0) continue;
            if (_literacy[s.Index] < i.MinLiteracy || _urban[s.Index] < i.MinUrbanization || _level[s.Index] < i.MinDevelopment) continue;
            // receptivity: towns and schooled societies adopt far faster than scattered tribes
            double receptive = Math.Clamp(Math.Max(_urban[s.Index] / 0.08, _level[s.Index] / 8.0), 0.1, 1.0);
            delta[s.Index] = (float)(i.SpreadPerMonth * best * Math.Max(0.03, receptive));
        }
        for (int k = 0; k < p.Length; k++) if (delta[k] > 0) p[k] = Math.Min(1f, p[k] + delta[k]);
    }

    public void MonthlyTick()
    {
        if (!Enabled || _level.Length != N) return;
        var d = _game.Data.Defines;
        double litYear = d.Get("development_literacy_per_year", 0.002), urbYear = d.Get("development_urban_per_year", 0.001);
        foreach (var s in _game.World.States)
        {
            var share = Shares(s);
            double lit = 0, urb = 0;
            foreach (var i in Institutions) if (Adopted(i, s)) { lit += i.Modifier.GetValueOrDefault("literacy_growth"); urb += i.Modifier.GetValueOrDefault("urban_growth"); }
            if (Rules.HasSchools(_game, s, share) && _literacy[s.Index] < 1) _literacy[s.Index] = (float)Math.Min(1, _literacy[s.Index] + litYear / 12 * (1 + lit));
            if (Rules.HasMarkets(_game, s, share) && _urban[s.Index] < 1) _urban[s.Index] = (float)Math.Min(1, _urban[s.Index] + urbYear / 12 * (1 + urb));
        }
        foreach (var i in Institutions)
        {
            if (!_presence.ContainsKey(i.Key)) continue;
            if (!_appeared.Contains(i.Key) && i.Appears <= _game.World.Date) { Appear(i); _game.NotifyPlayer($"{i.Name} appears in {_game.World.States.First(s => s.Key.Equals(i.Origin, StringComparison.OrdinalIgnoreCase)).DisplayName}.", NotificationKind.Good); }
            else if (_appeared.Contains(i.Key)) Spread(i);
        }
        foreach (var c in _game.World.Countries.Where(c => c.Exists && !c.IsPlayer)) AiSpend(c);
    }

    /// <summary>The AI develops its best state each month once it holds a reserve (development_ai_reserve, 100) beyond the price.</summary>
    private void AiSpend(Country c)
    {
        double reserve = _game.Data.Defines.Get("development_ai_reserve", 100);
        State? best = null;
        double bestScore = 0;
        foreach (var s in c.OwnedStates)
        {
            if (CannotDevelop(c, s) != null) continue;
            double score = Rules.AiScore(_game, c, s);
            if (score > bestScore) { best = s; bestScore = score; }
        }
        if (best == null) return;
        var cur = Currency(c);
        double have = cur != null ? _game.Resources.Get(c, cur) : c.Gold;
        if (have >= Cost(c, best) + reserve) Develop(c, best);
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var s in _game.World.States)
        {
            if (s.Index >= _level.Length) break;
            var n = root.AddBlock("state");
            n.Add("key", s.Key);
            n.Add("level", _level[s.Index].ToString(CultureInfo.InvariantCulture));
            n.Add("start", _start[s.Index].ToString(CultureInfo.InvariantCulture));
            n.Add("literacy", F(_literacy[s.Index]));
            n.Add("urban", F(_urban[s.Index]));
        }
        foreach (var (key, p) in _presence)
        {
            var n = root.AddBlock("institution");
            n.Add("key", key);
            n.Add("appeared", _appeared.Contains(key) ? "yes" : "no");
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < p.Length; k++) { if (k > 0) sb.Append(' '); sb.Append(F(p[k])); }
            n.Add("presence", sb.ToString(), quoted: true);
        }
    }

    private void Load(Node root)
    {
        Reset();
        int n = N;
        _level = new int[n]; _start = new int[n]; _literacy = new float[n]; _urban = new float[n];
        var byKey = _game.World.States.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var b in root.GetAll("state"))
        {
            if (!byKey.TryGetValue(b.GetString("key") ?? "", out var s)) continue;
            _level[s.Index] = (int)b.GetDouble("level", 1); _start[s.Index] = (int)b.GetDouble("start", 1);
            _literacy[s.Index] = (float)b.GetDouble("literacy"); _urban[s.Index] = (float)b.GetDouble("urban");
        }
        foreach (var b in root.GetAll("institution"))
        {
            string key = b.GetString("key") ?? "";
            var p = new float[n];
            var parts = (b.GetString("presence") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int k = 0; k < Math.Min(n, parts.Length); k++) p[k] = float.Parse(parts[k], CultureInfo.InvariantCulture);
            _presence[key] = p;
            if (b.GetString("appeared") == "yes") _appeared.Add(key);
        }
    }

    private void Reset()
    {
        _level = Array.Empty<int>(); _start = Array.Empty<int>(); _literacy = Array.Empty<float>(); _urban = Array.Empty<float>();
        _presence.Clear();
        _appeared.Clear();
        _links = null;
        _cacheDay = int.MinValue;
    }

    /// <summary>Hash of the development state for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 17;
        for (int k = 0; k < _level.Length; k++) h = h * 31 + _level[k];
        return h;
    }
}
