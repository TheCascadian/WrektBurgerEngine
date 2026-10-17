using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Diplomacy;

/// <summary>Opinion modifiers that a system sets once a month: the old one of the same name goes, the new one lasts a little longer than a month.</summary>
internal static class OpinionTools
{
    public static void Set(Game g, Country from, Country to, string name, double value, int days = 40)
    {
        if (from == to) return;
        var list = g.World.Relations.ModifiersOf(from, to);
        list.RemoveAll(m => m.Name == name);
        if (Math.Abs(value) > 0.01) g.World.Relations.AddOpinion(from, to, name, value, 0, g.World.Date.AddDays(days));
    }
}

/// <summary>Countries that have come together against one that grows too fast.</summary>
public sealed class Coalition
{
    public Country Target = null!;
    public List<Country> Members = new();
    public int FormedDay;
}

/// <summary>
/// Diplomatic reputation (reputation feature). Taking land builds aggressive expansion; above 10 the neighbours dislike you, fabricating claims costs more,
/// and when three countries within war reach hold you in contempt at 20 they form a coalition with a casus belli of its own. Up to three rivals can be named:
/// war goals against them are cheaper and their strength gives prestige.
/// </summary>
public sealed class ReputationSystem
{
    private readonly Game _game;
    public readonly List<Coalition> Coalitions = new();
    private readonly Dictionary<Country, List<Country>> _rivals = new();

    public const string ExpansionKey = "aggressive_expansion", PrestigeKey = "prestige", CoalitionCb = "coalition_war";

    /// <summary>Counts for reports.</summary>
    public int CoalitionsFormed;

    public ReputationSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "reputation", Write = Save, Read = Load, Reset = () => { Coalitions.Clear(); _rivals.Clear(); } });
        game.Diplomacy.CostFactors.Add((a, c, _) => Enabled && a.Tags.Contains("fabricate") && Expansion(c) >= _game.Data.Defines.Get("ae_fabricate_threshold", 10) ? 1 + _game.Data.Defines.Get("ae_fabricate_cost", 0.5) : 1);
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Reputation);
    private DiplomacyRules Rules => _game.Rules.Diplomacy;

    // ------------------------------------------------------------------------------------- aggressive expansion
    public double Expansion(Country c) => c.Vars.Get(ExpansionKey, 0);
    public void SetExpansion(Country c, double v) => c.Vars.Set(ExpansionKey, Math.Clamp(v, 0, 100));

    /// <summary>Aggressive expansion a state taken in a war adds: its development (1 a level, half for a core or a claim).</summary>
    public double ExpansionOf(Country taker, State s)
    {
        double dev = _game.Development.Enabled ? _game.Development.Level(s) : 1 + Math.Round(s.Tax * 2);
        return dev * (s.Cores.Contains(taker) || s.Claims.Contains(taker) ? _game.Data.Defines.Get("ae_claimed_factor", 0.5) : 1) * _game.Data.Defines.Get("ae_per_level", 1);
    }

    /// <summary>A state was taken by the winner of a war.</summary>
    public void OnTaken(Country taker, State s)
    {
        if (!Enabled) return;
        double add = ExpansionOf(taker, s);
        SetExpansion(taker, Expansion(taker) + add);
        if (taker.IsPlayer) _game.Notify($"Aggressive expansion +{add:0.#} for taking {s.DisplayName}.", NotificationKind.Bad);
    }

    // ------------------------------------------------------------------------------------- rivals
    public IReadOnlyList<Country> RivalsOf(Country c) => _rivals.TryGetValue(c, out var l) ? l : (IReadOnlyList<Country>)Array.Empty<Country>();
    public bool IsRival(Country c, Country other) => RivalsOf(c).Contains(other);
    public int RivalSlots => (int)_game.Data.Defines.Get("rival_slots", 3);

    public string? CannotAddRival(Country c, Country other)
    {
        if (!Enabled) return "Rivalry is not used in this game";
        if (c == other || !other.Exists) return "Not a country";
        if (IsRival(c, other)) return "Already a rival";
        if (RivalsOf(c).Count >= RivalSlots) return $"Only {RivalSlots} rivals at a time";
        if (_game.World.Relations.AreAllied(c, other) || _game.World.Relations.AreOverlordAndSubject(c, other)) return "An ally or subject cannot be a rival";
        return null;
    }

    public string? AddRival(Country c, Country other)
    {
        var why = CannotAddRival(c, other);
        if (why != null) return why;
        if (!_rivals.TryGetValue(c, out var l)) _rivals[c] = l = new List<Country>();
        l.Add(other);
        if (c.IsPlayer) _game.Notify($"{other.Name} is now our rival.", NotificationKind.Diplomacy);
        return null;
    }

    public void RemoveRival(Country c, Country other)
    {
        if (_rivals.TryGetValue(c, out var l)) l.Remove(other);
    }

    /// <summary>War goals against a rival cost 10% less (rival_goal_discount).</summary>
    public double RivalCostFactor(Country winner, Country loser)
        => Enabled && IsRival(winner, loser) ? 1 - _game.Data.Defines.Get("rival_goal_discount", 0.10) : 1;

    // ------------------------------------------------------------------------------------- coalitions
    public Coalition? CoalitionAgainst(Country target) => Coalitions.FirstOrDefault(c => c.Target == target);
    public bool InCoalitionAgainst(Country c, Country target) => CoalitionAgainst(target)?.Members.Contains(c) == true;

    private bool Hostile(Country c, Country target)
        => c != target && c.Exists && !_game.World.Relations.AreAllied(c, target) && !_game.World.Relations.AreOverlordAndSubject(c, target)
           && Rules.HasWarReach(_game, c, target) && _game.Diplomacy.Opinion(c, target) <= _game.Data.Defines.Get("coalition_opinion", -50);

    public void MonthlyTick()
    {
        if (!Enabled) return;
        var d = _game.Data.Defines;
        double decay = d.Get("ae_decay", 0.1), start = d.Get("ae_opinion_threshold", 10), perPoint = d.Get("ae_opinion_per_point", 2);
        var live = _game.World.Countries.Where(c => c.Exists).ToList();
        foreach (var c in live)
        {
            double ae = Expansion(c);
            if (ae > 0) { ae = Math.Max(0, ae - decay); SetExpansion(c, ae); }
            double penalty = ae >= start ? -perPoint * (ae - start) : 0;
            foreach (var n in Builtins.NeighborCountries(_game, c))
                OpinionTools.Set(_game, n, c, "Aggressive expansion", penalty);
        }
        // coalitions form against those who grew too fast, and break up when they have calmed down
        double formAt = d.Get("coalition_ae", 20), endAt = d.Get("coalition_end_ae", 10);
        int need = (int)d.Get("coalition_members", 3);
        foreach (var co in Coalitions.ToList())
        {
            if (!co.Target.Exists || Expansion(co.Target) < endAt) { Dissolve(co); continue; }
            co.Members.RemoveAll(m => !Hostile(m, co.Target));
            if (co.Members.Count < need) Dissolve(co);
        }
        foreach (var t in live.Where(x => Expansion(x) >= formAt && CoalitionAgainst(x) == null))
        {
            var members = live.Where(m => Hostile(m, t)).ToList();
            if (members.Count < need) continue;
            Coalitions.Add(new Coalition { Target = t, Members = members, FormedDay = _game.World.Date.TotalDays });
            CoalitionsFormed++;
            _game.NotifyPlayer($"A coalition of {members.Count} countries forms against {t.Name}.", NotificationKind.War, members.Append(t).ToArray());
            _game.Events.FireOnAction("on_coalition_formed", t);
        }
        // prestige from the strength of the rivals
        if (_game.Resources.Defs.Any(r => r.Key == PrestigeKey))
            foreach (var c in live)
            {
                double own = Math.Max(1, Rules.PowerScore(_game, c));
                double gain = RivalsOf(c).Where(r => r.Exists).Sum(r => Math.Clamp(Rules.PowerScore(_game, r) / own, 0, 2)) * d.Get("rival_prestige", 0.25);
                if (gain > 0) c.Vars.Set(PrestigeKey, c.Vars.Get(PrestigeKey) + gain);
            }
    }

    private void Dissolve(Coalition co)
    {
        Coalitions.Remove(co);
        _game.NotifyPlayer($"The coalition against {co.Target.Name} breaks up.", NotificationKind.Diplomacy, co.Members.Append(co.Target).ToArray());
    }

    /// <summary>Called when the game starts: the AI profile's rivals become real rivals (the first three that exist).</summary>
    public void OnGameStart()
    {
        if (!Enabled || !_game.AiProfiles.Enabled) return;
        foreach (var c in _game.World.Countries.Where(c => c.Exists && !c.IsPlayer))
        {
            if (_game.AiProfiles.PersonalityOf(c).Profile is not { } prof) continue;
            foreach (var kv in prof.Rivals.OrderByDescending(k => k.Value))
            {
                if (RivalsOf(c).Count >= RivalSlots) break;
                if (_game.World.GetCountry(kv.Key) is { Exists: true } other) AddRival(c, other);
            }
        }
    }

    internal void Subscribe(GameEvents hub) => hub.WarDeclared += OnWarDeclared;

    /// <summary>A member that declares the coalition's war calls the others to arms.</summary>
    private void OnWarDeclared(Country attacker, Country defender, War war)
    {
        if (!Enabled || war.CasusBelli?.Key != CoalitionCb || CoalitionAgainst(defender) is not { } co || !co.Members.Contains(attacker)) return;
        foreach (var m in co.Members.Where(m => m != attacker && m.Exists && !war.Involves(m) && !_game.World.AtWar(m, defender)).ToList())
        {
            if (!m.IsPlayer) { _game.Diplomacy.JoinWar(m, war, true); continue; }
            _game.PendingProposals.Add(new Proposal { Kind = ProposalKind.CallToArms, From = attacker, To = m, War = war, Date = _game.World.Date, Title = "Coalition war", Text = $"{attacker.Name} calls the coalition against {defender.Name} to arms." });
        }
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterTrigger("in_coalition_against", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && InCoalitionAgainst(a, b), (ctx, n) => "Is in a coalition against that country");
        s.RegisterTrigger("has_rival", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && IsRival(a, b), (ctx, n) => "Has that country as a rival");
        s.RegisterTrigger("can_add_rival", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && CannotAddRival(a, b) == null, (ctx, n) => "Can name that country a rival");
        s.RegisterEffect("add_rival", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) AddRival(a, b); }, (ctx, n) => "Names that country a rival");
        s.RegisterEffect("remove_rival", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) RemoveRival(a, b); }, (ctx, n) => "Ends the rivalry");
        s.RegisterValue("rival_count", (ctx, scope) => scope is Country c && Enabled ? RivalsOf(c).Count : null);
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        foreach (var kv in _rivals.OrderBy(k => k.Key.Tag, StringComparer.Ordinal).Where(k => k.Value.Count > 0))
        {
            var rn = root.AddBlock("rivals");
            rn.Add("tag", kv.Key.Tag);
            rn.Add("list", string.Join(' ', kv.Value.Select(r => r.Tag)), quoted: true);
        }
        foreach (var co in Coalitions)
        {
            var n = root.AddBlock("coalition");
            n.Add("target", co.Target.Tag);
            n.Add("day", co.FormedDay.ToString(CultureInfo.InvariantCulture));
            n.Add("members", string.Join(' ', co.Members.Select(m => m.Tag)), quoted: true);
        }
    }

    private void Load(Node root)
    {
        Coalitions.Clear(); _rivals.Clear();
        foreach (var b in root.GetAll("rivals"))
            if (_game.World.GetCountry(b.GetString("tag")) is { } c)
                _rivals[c] = (b.GetString("list") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => _game.World.GetCountry(t)).Where(x => x != null).Cast<Country>().ToList();
        foreach (var b in root.GetAll("coalition"))
            if (_game.World.GetCountry(b.GetString("target")) is { } t)
                Coalitions.Add(new Coalition { Target = t, FormedDay = (int)b.GetDouble("day"), Members = (b.GetString("members") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => _game.World.GetCountry(x)).Where(x => x != null).Cast<Country>().ToList() });
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 43;
        foreach (var c in _game.World.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal)) h = h * 31 + (long)Math.Round(Expansion(c) * 10) + RivalsOf(c).Count;
        return h * 31 + Coalitions.Count;
    }
}
