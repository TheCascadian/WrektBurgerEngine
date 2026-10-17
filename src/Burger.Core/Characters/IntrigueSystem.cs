using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Diplomacy;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Characters;

/// <summary>A scheme from <c>common/schemes/*.txt</c>: something one character does against another (murder, seduction, a forged claim).</summary>
public sealed class SchemeDef
{
    public string Key = "", Name = "", Desc = "";
    /// <summary>Months it takes at normal skill.</summary>
    public double Months = 12;
    /// <summary>Character stat that speeds it up and makes it harder to find.</summary>
    public string Stat = "finesse";
    /// <summary>Chance a month that the target finds out, before skill and defences.</summary>
    public double Discovery = 0.2;
    /// <summary>Stress the owner takes on when starting.</summary>
    public double Stress = 20;
    /// <summary>Only the ruler of a country can be the target (its ruler is the face of the realm).</summary>
    public bool RulerOnly;
    /// <summary>Only a target of another country is allowed.</summary>
    public bool Foreign;
    /// <summary>May the scheme be started (ROOT = the owner's country, scope:owner, scope:target, scope:target_country).</summary>
    public Node? Allow;
    public Node? OnSuccess;
    public Node? OnDiscovered;
}

/// <summary>A scheme under way: who, against whom, and how far it has got.</summary>
public sealed class Scheme
{
    public int Id;
    public SchemeDef Def = null!;
    public Character Owner = null!, Target = null!;
    public double Progress;
    public int StartDay;
}

/// <summary>Characters who plot to overthrow the ruler of their country.</summary>
public sealed class Plot
{
    public int Id;
    public Country Country = null!;
    public Character Leader = null!;
    public List<Character> Members = new();
    public double Progress;
    public int StartDay;
    /// <summary>overthrow (take the throne) or independence (a vassal breaks away, titles feature).</summary>
    public string Goal = "overthrow";
    public int Size => Members.Count;
}

/// <summary>
/// Intrigue and loyalty (intrigue feature). Characters scheme against each other (murder, seduction, a forged claim): a scheme has progress and a chance to be found
/// out; the owner takes on stress. Loyalty drifts with the ruler, the stability of the country and traits; an ambitious character with
/// low loyalty joins a plot against the ruler, which needs three members and time to succeed, and is discovered or not.
/// </summary>
public sealed class IntrigueSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, SchemeDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<Scheme> Schemes = new();
    public readonly List<Plot> Plots = new();
    private int _nextId = 1;

    public const string StressKey = "stress";

    /// <summary>A plot reached its goal (the titles feature settles vassal plots).</summary>
    public event Action<Plot>? PlotSucceeded;

    /// <summary>Counts for reports.</summary>
    public int SchemesStarted, SchemesDone, SchemesFound, PlotsStarted, Coups, PlotsFound;

    public IntrigueSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "intrigue", Write = Save, Read = Load, Reset = () => { Schemes.Clear(); Plots.Clear(); } });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Intrigue) && _game.Features.Has(FeatureSet.Characters);

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Defs.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/schemes"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Defs[n.Key] = new SchemeDef
                {
                    Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!,
                    Months = Math.Max(1, n.GetDouble("months", 12)), Stat = n.GetString("stat", "finesse")!, Discovery = n.GetDouble("discovery", 0.2),
                    Stress = n.GetDouble("stress", 20), RulerOnly = n.GetBool("ruler_only"), Foreign = n.GetBool("foreign"),
                    Allow = n.Get("allow"), OnSuccess = n.Get("on_success"), OnDiscovered = n.Get("on_discovered"),
                };
            }
    }

    // ------------------------------------------------------------------------------------- loyalty, ambition, stress
    public double Stress(Character c) => c.Vars.Get(StressKey, 0);
    public void AddStress(Character c, double v) => c.Vars.Set(StressKey, Math.Clamp(Stress(c) + v, 0, 100));

    /// <summary>How much a character wants more than they have, 0-100: 30 and what their traits add (the key 'ambition'), more for an heir.</summary>
    public double Ambition(Character c) => Math.Clamp(30 + c.TraitModifier("ambition") + (c.IsHeir ? 10 : 0), 0, 100);

    /// <summary>The loyalty a character settles at: the ruler's charisma, the stability of the country, the family, rank and traits.</summary>
    public double LoyaltyTarget(Character c)
    {
        var country = c.Country;
        if (country?.Ruler == null) return 50;
        var ruler = country.Governing ?? country.Ruler;
        double stab = _game.Politics.Enabled ? _game.Rules.Politics.StabilityOf(_game, country) : 50;
        double v = 50 + (ruler.GetStat("charisma") - 5) * 3 + (stab - 50) / 4 + c.TraitModifier("loyalty") - (Ambition(c) - 50) / 5;
        if (c.Family.Length > 0 && c.Family == country.Ruler.Family) v += 10;
        if (c.IsHeir) v += 10;
        if (_game.Titles.Enabled) v += _game.Titles.LoyaltyAdjust(c);
        return Math.Clamp(v, 0, 100);
    }

    /// <summary>What a character thinks of the ruler of their country, -100..100.</summary>
    public double OpinionOfRuler(Character c) => (c.Loyalty - 50) * 2;

    // ------------------------------------------------------------------------------------- schemes
    public IEnumerable<Scheme> SchemesOf(Character owner) => Schemes.Where(s => s.Owner == owner);
    public IEnumerable<Scheme> SchemesAgainst(Character target) => Schemes.Where(s => s.Target == target);
    public int MaxSchemes => (int)_game.Data.Defines.Get("max_schemes", 2);

    private ScriptContext ContextOf(Scheme s)
    {
        var scopes = new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["owner"] = s.Owner, ["target"] = s.Target, ["target_country"] = s.Target.Country };
        return _game.Context(s.Owner.Country, scopes);
    }

    public string? CannotStart(Character owner, SchemeDef def, Character target)
    {
        if (!Enabled) return "Intrigue is not used in this game";
        if (!owner.Alive || !target.Alive || owner == target) return "No such target";
        if (owner.Country == null) return "Only a character with a country schemes";
        if (owner.Age(_game.World.Date) < _game.Data.Defines.AdultAge) return "Too young";
        if (SchemesOf(owner).Count() >= MaxSchemes) return $"Already busy with {MaxSchemes} schemes";
        if (Schemes.Any(s => s.Owner == owner && s.Target == target && s.Def == def)) return "Already under way";
        if (def.RulerOnly && !target.IsRuler) return "Only against a ruler";
        if (def.Foreign && (target.Country == null || target.Country == owner.Country)) return "Only against a foreign character";
        if (def.Allow != null)
        {
            var scopes = new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["owner"] = owner, ["target"] = target, ["target_country"] = target.Country };
            if (!_game.Script.EvalTrigger(def.Allow, _game.Context(owner.Country, scopes))) return "Requirements not met";
        }
        return null;
    }

    public string? Start(Character owner, SchemeDef def, Character target)
    {
        var why = CannotStart(owner, def, target);
        if (why != null) return why;
        var s = new Scheme { Id = _nextId++, Def = def, Owner = owner, Target = target, StartDay = _game.World.Date.TotalDays };
        Schemes.Add(s);
        AddStress(owner, def.Stress);
        SchemesStarted++;
        return null;
    }

    public void Cancel(Scheme s) => Schemes.Remove(s);

    /// <summary>Progress a month: 100 over the scheme's months, faster with skill (6% a point above 5), slower against a defended target.</summary>
    public double ProgressPerMonth(Scheme s)
    {
        double skill = s.Owner.GetStat(s.Def.Stat);
        double speed = 1 + (skill - 5) * 0.06;
        return 100 / s.Def.Months * Math.Max(0.25, speed);
    }

    /// <summary>Chance a month that the target finds out: the scheme's base, less the owner's skill, more with the target country's spies (modifier scheme_defense).</summary>
    public double DiscoveryChance(Scheme s)
    {
        double skill = s.Owner.GetStat(s.Def.Stat);
        double defence = s.Target.Country != null ? _game.Modifiers.Sum(s.Target.Country, "scheme_defense") : 0;
        return Math.Clamp(s.Def.Discovery - (skill - 5) * 0.02 + defence, 0.02, 0.8);
    }

    /// <summary>Chance of being found before the end: what is left of the scheme, month by month.</summary>
    public double DiscoveryOverall(Scheme s)
    {
        double months = Math.Max(0, (100 - s.Progress) / ProgressPerMonth(s));
        return 1 - Math.Pow(1 - DiscoveryChance(s), months);
    }

    private void Finish(Scheme s, bool found)
    {
        Schemes.Remove(s);
        var country = s.Owner.Country;
        if (found)
        {
            SchemesFound++;
            AddStress(s.Owner, 20);
            s.Owner.Loyalty = Math.Clamp(s.Owner.Loyalty - 15, 0, 100);
            if (s.Target.Country is { } tc && country != null && tc != country)
                _game.Diplomacy.AddOpinion(tc, country, "Discovered scheme", -40, 1);
            else if (s.Target.Country == country && s.Target.IsRuler && _game.Rngs.Characters.NextDouble() < 0.3) _game.Characters.Kill(s.Owner, "executed for a plot");
            if (s.Def.OnDiscovered != null) _game.Script.Execute(s.Def.OnDiscovered, ContextOf(s));
            _game.NotifyPlayer($"{s.Owner.DisplayName}'s scheme to {s.Def.Name.ToLowerInvariant()} {s.Target.DisplayName} has been discovered.", NotificationKind.Bad, country, s.Target.Country);
        }
        else
        {
            SchemesDone++;
            AddStress(s.Owner, -10);
            if (s.Def.OnSuccess != null) _game.Script.Execute(s.Def.OnSuccess, ContextOf(s));
            _game.NotifyPlayer($"{s.Owner.DisplayName}'s scheme to {s.Def.Name.ToLowerInvariant()} {s.Target.DisplayName} succeeds.", NotificationKind.Good, country);
        }
    }

    // ------------------------------------------------------------------------------------- plots
    public Plot? PlotOf(Country c, string goal = "overthrow") => Plots.FirstOrDefault(p => p.Country == c && p.Goal == goal);
    public IEnumerable<Plot> PlotsInvolving(Character ch) => Plots.Where(p => p.Leader == ch || p.Members.Contains(ch));

    /// <summary>Starts a plot against the ruler of a country with a leader and members.</summary>
    public Plot StartPlot(Country country, Character leader, string goal = "overthrow", IEnumerable<Character>? members = null)
    {
        var p = new Plot { Id = _nextId++, Country = country, Leader = leader, Goal = goal, StartDay = _game.World.Date.TotalDays };
        p.Members.Add(leader);
        if (members != null) foreach (var m in members) if (!p.Members.Contains(m)) p.Members.Add(m);
        Plots.Add(p);
        PlotsStarted++;
        _game.NotifyPlayer($"Trouble at court: someone plots against {country.Ruler?.DisplayName ?? "the ruler"}.", NotificationKind.Bad, country);
        return p;
    }

    /// <summary>Plot strength a month: grows with the members and the leader's skill.</summary>
    public double PlotRate(Plot p) => (3 + p.Members.Sum(m => m.GetStat("finesse") * 0.25 + 1)) * _game.Data.Defines.Get("plot_speed", 1);

    public double PlotDiscovery(Plot p)
    {
        double defence = _game.Modifiers.Sum(p.Country, "scheme_defense");
        return Math.Clamp(0.04 + 0.01 * p.Size + defence, 0.02, 0.6);
    }

    private void RunPlot(Plot p)
    {
        var rng = _game.Rngs.Characters;
        p.Members.RemoveAll(m => !m.Alive || m.Country != p.Country);
        if (!p.Leader.Alive || p.Leader.Country != p.Country || p.Members.Count == 0 || p.Country.Ruler == null) { Plots.Remove(p); return; }
        if (!p.Members.Contains(p.Leader)) p.Leader = p.Members[0];
        p.Progress += PlotRate(p);
        foreach (var m in p.Members) AddStress(m, 2);
        if (rng.NextDouble() < PlotDiscovery(p))
        {
            Plots.Remove(p);
            PlotsFound++;
            var leader = p.Leader;
            foreach (var m in p.Members) m.Loyalty = Math.Clamp(Math.Max(m.Loyalty, 40), 0, 100);
            if (rng.NextDouble() < 0.5) _game.Characters.Kill(leader, "executed for a plot"); else { leader.Country = null; leader.Loyalty = 10; }
            _game.NotifyPlayer($"The plot against {p.Country.Ruler.DisplayName} is uncovered; {leader.DisplayName} is punished.", NotificationKind.Good, p.Country);
            return;
        }
        if (p.Progress < 100 || p.Members.Count < (int)_game.Data.Defines.Get("plot_members", 3)) return;
        Plots.Remove(p);
        Coups++;
        if (p.Goal == "overthrow")
        {
            var old = p.Country.Ruler;
            old.Country = null;
            old.Loyalty = 10;
            p.Country.Ruler = null;
            _game.Characters.SetRuler(p.Country, p.Leader);
            foreach (var m in p.Members) m.Loyalty = Math.Clamp(m.Loyalty + 20, 0, 100);
            if (_game.Resources.Defs.FirstOrDefault(r => r.Key == "stability") is { } st) _game.Resources.Set(p.Country, st, _game.Resources.Get(p.Country, st) - 15);
            _game.NotifyPlayer($"{p.Leader.DisplayName} and their allies overthrow {old.DisplayName}!", NotificationKind.Bad, p.Country);
            _game.Events.FireOnAction("on_court_coup", p.Country);
        }
        PlotSucceeded?.Invoke(p);
    }

    // ------------------------------------------------------------------------------------- monthly
    public void MonthlyTick()
    {
        if (!Enabled) return;
        var rng = _game.Rngs.Characters;
        int adult = _game.Data.Defines.AdultAge;
        var now = _game.World.Date;
        double step = _game.Data.Defines.Get("loyalty_step", 0.1);
        var alive = _game.World.Characters.Where(c => c.Alive && c.Country is { Exists: true } && c.Age(now) >= adult).ToList();
        foreach (var c in alive)
        {
            if (!c.IsRuler) c.Loyalty = Math.Clamp(c.Loyalty + (LoyaltyTarget(c) - c.Loyalty) * step, 0, 100);
            double stress = Stress(c);
            if (stress > 0)
            {
                stress = Math.Max(0, stress - _game.Data.Defines.Get("stress_decay", 2));
                if (stress >= 100) { c.Health = Math.Max(1, c.Health - 20); stress = 50; c.Loyalty = Math.Max(0, c.Loyalty - 10); }
                c.Vars.Set(StressKey, stress);
            }
        }
        foreach (var s in Schemes.ToList())
        {
            if (!s.Owner.Alive || !s.Target.Alive || s.Owner.Country == null) { Schemes.Remove(s); continue; }
            if (rng.NextDouble() < DiscoveryChance(s)) { Finish(s, found: true); continue; }
            s.Progress += ProgressPerMonth(s);
            if (s.Progress >= 100) Finish(s, found: false);
        }
        foreach (var p in Plots.ToList()) RunPlot(p);
        // the discontented join or start plots against their ruler
        double joinChance = _game.Data.Defines.Get("plot_join_chance", 0.1);
        foreach (var grp in alive.Where(c => !c.IsRuler && c.Loyalty < _game.Data.Defines.Get("plot_loyalty", 35) && Ambition(c) > _game.Data.Defines.Get("plot_ambition", 50)).GroupBy(c => c.Country!))
        {
            var country = grp.Key;
            if (country.Ruler == null) continue;
            var plot = PlotOf(country);
            foreach (var c in grp.OrderByDescending(Ambition))
            {
                if (rng.NextDouble() > joinChance || Plots.Any(p => p.Members.Contains(c))) continue;
                if (plot == null) plot = StartPlot(country, c);
                else { plot.Members.Add(c); AddStress(c, 5); }
            }
        }
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterEffect("add_stress", (ctx, n) => { if (ctx.This is Character c) AddStress(c, s.ResolveNumber(n.Value, ctx)); }, (ctx, n) => $"Stress: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("start_plot", (ctx, n) => { if (ctx.This is Character c && c.Country is { } k && PlotOf(k) == null) StartPlot(k, c); }, (ctx, n) => "Starts a plot against the ruler");
        s.RegisterTrigger("is_plotting", (ctx, n) => ctx.This is Character c && PlotsInvolving(c).Any(), (ctx, n) => "Is in a plot");
        s.RegisterTrigger("is_scheming", (ctx, n) => ctx.This is Character c && SchemesOf(c).Any(), (ctx, n) => "Is scheming");
        s.RegisterValue("stress", (ctx, scope) => scope is Character c ? Stress(c) : null);
        s.RegisterValue("ambition", (ctx, scope) => scope is Character c ? Ambition(c) : null);
        s.RegisterValue("plot_members", (ctx, scope) => scope is Country c && Enabled ? PlotOf(c)?.Size ?? 0 : null);
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        root.Add("next_id", _nextId.ToString(CultureInfo.InvariantCulture));
        foreach (var s in Schemes)
        {
            var n = root.AddBlock("scheme");
            n.Add("id", s.Id.ToString(CultureInfo.InvariantCulture)); n.Add("def", s.Def.Key);
            n.Add("owner", s.Owner.Key, quoted: true); n.Add("target", s.Target.Key, quoted: true);
            n.Add("progress", F(s.Progress)); n.Add("start", s.StartDay.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var p in Plots)
        {
            var n = root.AddBlock("plot");
            n.Add("id", p.Id.ToString(CultureInfo.InvariantCulture)); n.Add("country", p.Country.Tag); n.Add("leader", p.Leader.Key, quoted: true);
            n.Add("members", string.Join(' ', p.Members.Select(m => m.Key.Replace(' ', '_'))), quoted: true);
            n.Add("progress", F(p.Progress)); n.Add("start", p.StartDay.ToString(CultureInfo.InvariantCulture)); n.Add("goal", p.Goal);
        }
    }

    private void Load(Node root)
    {
        Schemes.Clear(); Plots.Clear();
        _nextId = Math.Max(1, (int)root.GetDouble("next_id", 1));
        var w = _game.World;
        foreach (var b in root.GetAll("scheme"))
            if (Defs.TryGetValue(b.GetString("def") ?? "", out var def) && w.GetCharacter(b.GetString("owner")) is { } o && w.GetCharacter(b.GetString("target")) is { } t)
                Schemes.Add(new Scheme { Id = (int)b.GetDouble("id"), Def = def, Owner = o, Target = t, Progress = b.GetDouble("progress"), StartDay = (int)b.GetDouble("start") });
        foreach (var b in root.GetAll("plot"))
            if (w.GetCountry(b.GetString("country")) is { } c && w.GetCharacter(b.GetString("leader")) is { } l)
            {
                var p = new Plot { Id = (int)b.GetDouble("id"), Country = c, Leader = l, Progress = b.GetDouble("progress"), StartDay = (int)b.GetDouble("start"), Goal = b.GetString("goal") ?? "overthrow" };
                foreach (var key in (b.GetString("members") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (w.Characters.FirstOrDefault(x => x.Key.Replace(' ', '_') == key) is { } m) p.Members.Add(m);
                Plots.Add(p);
            }
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 53;
        foreach (var s in Schemes) h = h * 31 + (long)Math.Round(s.Progress) + s.Id;
        foreach (var p in Plots) h = h * 31 + (long)Math.Round(p.Progress) + p.Members.Count;
        return h;
    }
}
