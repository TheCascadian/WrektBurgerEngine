using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Diplomacy;

// =================================================================================================
// Envoys
// =================================================================================================

/// <summary>
/// Envoys (expanded_diplomacy feature): every diplomatic action ties up an envoy for 30 days. A country has 3, one more for every two great-power ranks, one more at
/// 25%, 50% and 75% of the technologies, and never more than 8.
/// </summary>
public sealed class EnvoySystem
{
    private readonly Game _game;
    private readonly Dictionary<Country, List<int>> _locks = new();
    private readonly Dictionary<Country, (int Day, int Total)> _total = new();

    public EnvoySystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "envoys", Write = Save, Read = Load, Reset = () => { _locks.Clear(); _total.Clear(); } });
        game.Diplomacy.ExtraRequirements.Add((a, actor, recipient) =>
            Enabled && a.Envoy && a.TargetType == ActionTarget.Country && recipient is Country ? ($"An envoy is free ({Free(actor)} of {Total(actor)})", Free(actor) > 0) : null);
        game.Diplomacy.ActionUsed += (a, actor, recipient) => { if (Enabled && a.Envoy && a.TargetType == ActionTarget.Country) Lock(actor); };
    }

    public bool Enabled => _game.Features.Has(FeatureSet.ExpandedDiplomacy);

    /// <summary>How many envoys a country has (rule Envoys).</summary>
    public int Total(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_total.TryGetValue(c, out var t) && t.Day == today) return t.Total;
        int n = _game.Rules.Diplomacy.EnvoyLimit(_game, c);
        _total[c] = (today, n);
        return n;
    }

    private List<int> Locks(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (!_locks.TryGetValue(c, out var l)) _locks[c] = l = new List<int>();
        l.RemoveAll(d => d <= today);
        return l;
    }

    public int Free(Country c) => Math.Max(0, Total(c) - Locks(c).Count);
    public int Busy(Country c) => Locks(c).Count;
    /// <summary>Days until the next envoy comes home (0 when none is out).</summary>
    public int NextBackIn(Country c) => Locks(c).Count == 0 ? 0 : Locks(c).Min() - _game.World.Date.TotalDays;

    public void Lock(Country c) => Locks(c).Add(_game.World.Date.TotalDays + (int)_game.Data.Defines.Get("envoy_days", 30));

    public void RegisterScript()
    {
        _game.Script.RegisterValue("envoys_free", (ctx, scope) => scope is Country c && Enabled ? Free(c) : null);
        _game.Script.RegisterValue("envoys_total", (ctx, scope) => scope is Country c && Enabled ? Total(c) : null);
    }

    private void Save(Node root)
    {
        foreach (var kv in _locks.OrderBy(k => k.Key.Tag, StringComparer.Ordinal).Where(k => k.Value.Count > 0))
        {
            var n = root.AddBlock("country");
            n.Add("tag", kv.Key.Tag);
            n.Add("until", string.Join(' ', kv.Value), quoted: true);
        }
    }

    private void Load(Node root)
    {
        _locks.Clear(); _total.Clear();
        foreach (var b in root.GetAll("country"))
            if (_game.World.GetCountry(b.GetString("tag")) is { } c)
                _locks[c] = (b.GetString("until") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();
    }

    public long Checksum() => _game.World.Countries.Sum(c => (long)Busy(c) * (c.Index + 1));
}

// =================================================================================================
// Peace conference
// =================================================================================================

public sealed class ConferenceTurn
{
    public Country Winner = null!;
    public double Points;
    public int Day;
    public bool Done;
    public string Result = "";
}

public sealed class Conference
{
    public War War = null!;
    public Country Leader = null!, Loser = null!;
    public List<ConferenceTurn> Turns = new();
    public int OpenDay, StartDay;
    public bool Started => false;
}

/// <summary>
/// The conference at the end of a big war (expanded_diplomacy feature): the war is under an armistice, and 7 days later every other winner takes a turn of 3 days, the
/// largest first, to put its demands to the losers with its share of the war score. Then the war ends.
/// </summary>
public sealed class ConferenceSystem
{
    private readonly Game _game;
    public readonly List<Conference> Active = new();

    /// <summary>Counts for reports.</summary>
    public int Held, Turns;

    public ConferenceSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "conferences", Write = Save, Read = Load, Reset = () => Active.Clear() });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.ExpandedDiplomacy);
    public Conference? Of(War war) => Active.FirstOrDefault(c => c.War == war);
    public Conference? Involving(Country c) => Active.FirstOrDefault(x => x.War.Involves(c));

    /// <summary>Is the war big enough for a conference: five countries or more, a total war, or two great powers.</summary>
    public bool IsBig(War war)
    {
        var d = _game.Data.Defines;
        int countries = war.Attackers.Count + war.Defenders.Count;
        if (countries >= (int)d.Get("conference_countries", 5)) return true;
        if (_game.Diplomacy.IsTotal(war)) return true;
        return _game.GreatPowers.Enabled && war.Attackers.Concat(war.Defenders).Count(_game.GreatPowers.IsGreat) >= 2;
    }

    /// <summary>The leaders have made peace: opens a conference for the other winners (true), or lets the war end at once (false).</summary>
    public bool TryOpen(War war, Country winner, Country loser)
    {
        if (!Enabled) return false;
        if (Of(war) != null) return true;
        if (!IsBig(war)) return false;
        var others = war.SideOf(winner).Where(c => c != winner && c.Exists).ToList();
        if (others.Count == 0) return false;
        double pool = Math.Clamp(war.ScoreFor(winner), 20, 100);
        double total = Math.Max(1, war.SideOf(winner).Where(c => c.Exists).Sum(c => _game.Diplomacy.Strength(c)));
        int today = _game.World.Date.TotalDays;
        int start = today + (int)_game.Data.Defines.Get("conference_delay_days", 7), step = (int)_game.Data.Defines.Get("conference_turn_days", 3);
        var conf = new Conference { War = war, Leader = winner, Loser = loser, OpenDay = today, StartDay = start };
        foreach (var (c, i) in others.OrderByDescending(c => _game.Diplomacy.Strength(c)).ThenBy(c => c.Tag, StringComparer.Ordinal).Select((c, i) => (c, i)))
            conf.Turns.Add(new ConferenceTurn { Winner = c, Points = pool * _game.Diplomacy.Strength(c) / total, Day = start + i * step });
        war.Vars.SetFlag("armistice");
        foreach (var a in war.SideOf(winner)) foreach (var b in war.SideOf(loser)) _game.World.Relations.AddTruce(a, b, _game.World.Date.AddYears(_game.Data.Defines.TruceYears));
        Active.Add(conf);
        Held++;
        _game.NotifyPlayer($"A peace conference of the {war.Name} opens in {_game.Data.Defines.Get("conference_delay_days", 7):0} days.", NotificationKind.War, war.Attackers.Concat(war.Defenders).ToArray());
        return true;
    }

    public ConferenceTurn? Current(Conference c) => c.Turns.FirstOrDefault(t => !t.Done);
    public int DaysToNext(Conference c) => Current(c) is { } t ? Math.Max(0, t.Day - _game.World.Date.TotalDays) : 0;

    /// <summary>The winner puts its demands now (the player on its turn; the AI does it on its own).</summary>
    public string? Demand(Conference c, ConferenceTurn t)
    {
        if (t.Done) return "Already taken";
        var war = c.War;
        var losers = war.SideOf(c.Loser).Where(x => x.Exists && x.OwnedStates.Count > 0).OrderByDescending(x => x == c.Loser).ThenByDescending(x => x.OwnedStates.Count).ToList();
        foreach (var l in losers)
        {
            var offer = _game.Ai.BuildDemands(war, t.Winner, l, t.Points);
            if (offer.Terms.Count == 0) continue;
            offer.NoEnd = true;
            _game.Diplomacy.ApplyPeace(offer);
            t.Result = string.Join(", ", offer.Terms) + $" from {l.Name}";
            break;
        }
        if (t.Result.Length == 0) t.Result = "no demands";
        t.Done = true;
        Turns++;
        return null;
    }

    public void Pass(Conference c, ConferenceTurn t)
    {
        if (t.Done) return;
        t.Done = true;
        t.Result = "passed";
    }

    public void DailyTick()
    {
        if (!Enabled || Active.Count == 0) return;
        int today = _game.World.Date.TotalDays;
        foreach (var c in Active.ToList())
        {
            if (!_game.World.Wars.Contains(c.War)) { Active.Remove(c); continue; }
            var t = Current(c);
            if (t != null && today >= t.Day)
            {
                // the player has the whole turn to decide; then the secretaries do it
                bool human = t.Winner.IsPlayer;
                if (!human || today >= t.Day + (int)_game.Data.Defines.Get("conference_turn_days", 3)) Demand(c, t);
            }
            if (Current(c) == null)
            {
                Active.Remove(c);
                c.War.Vars.ClearFlag("armistice");
                _game.Diplomacy.EndWar(c.War);
            }
        }
    }

    private void Save(Node root)
    {
        foreach (var c in Active)
        {
            var n = root.AddBlock("conference");
            n.Add("war", c.War.Id.ToString(CultureInfo.InvariantCulture));
            n.Add("leader", c.Leader.Tag); n.Add("loser", c.Loser.Tag);
            n.Add("open", c.OpenDay.ToString(CultureInfo.InvariantCulture)); n.Add("start", c.StartDay.ToString(CultureInfo.InvariantCulture));
            foreach (var t in c.Turns)
            {
                var tn = n.AddBlock("turn");
                tn.Add("winner", t.Winner.Tag); tn.Add("points", t.Points.ToString("R", CultureInfo.InvariantCulture)); tn.Add("day", t.Day.ToString(CultureInfo.InvariantCulture));
                tn.Add("done", t.Done ? "yes" : "no"); tn.Add("result", t.Result, quoted: true);
            }
        }
    }

    private void Load(Node root)
    {
        Active.Clear();
        foreach (var b in root.GetAll("conference"))
        {
            var war = _game.World.Wars.FirstOrDefault(w => w.Id.ToString(CultureInfo.InvariantCulture) == b.GetString("war"));
            if (war == null || _game.World.GetCountry(b.GetString("leader")) is not { } lead || _game.World.GetCountry(b.GetString("loser")) is not { } lose) continue;
            var c = new Conference { War = war, Leader = lead, Loser = lose, OpenDay = (int)b.GetDouble("open"), StartDay = (int)b.GetDouble("start") };
            foreach (var tn in b.GetAll("turn"))
                if (_game.World.GetCountry(tn.GetString("winner")) is { } wn)
                    c.Turns.Add(new ConferenceTurn { Winner = wn, Points = tn.GetDouble("points"), Day = (int)tn.GetDouble("day"), Done = tn.GetBool("done"), Result = tn.GetString("result") ?? "" });
            Active.Add(c);
        }
    }

    public long Checksum() => Active.Sum(c => c.Turns.Count(t => t.Done) * 31L + c.War.Id);
}

// =================================================================================================
// Factions (alliance blocs) and their war goals
// =================================================================================================

public sealed class FactionGoal
{
    public Country Leader = null!, Target = null!;
    public int SetDay;
}

/// <summary>
/// Factions (expanded_diplomacy feature): the allies of a country form a faction led by the strongest of them. The leader sets one war goal; members that are not at war
/// with its target after 30 days lose the leader's regard (-25) and may leave.
/// </summary>
public sealed class FactionSystem
{
    private readonly Game _game;
    public readonly Dictionary<Country, FactionGoal> Goals = new();

    /// <summary>Counts for reports.</summary>
    public int GoalsSet, MembersRefused, MembersLeft;

    public FactionSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "faction_goals", Write = Save, Read = Load, Reset = () => Goals.Clear() });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.ExpandedDiplomacy);

    /// <summary>The countries allied to each other, directly or through others.</summary>
    public List<Country> FactionOf(Country c)
    {
        var seen = new HashSet<Country> { c };
        var queue = new Queue<Country>();
        queue.Enqueue(c);
        while (queue.Count > 0)
            foreach (var a in _game.World.Relations.AlliesOf(queue.Dequeue()))
                if (a.Exists && seen.Add(a)) queue.Enqueue(a);
        return seen.OrderBy(x => x.Tag, StringComparer.Ordinal).ToList();
    }

    public Country LeaderOf(Country c)
    {
        var members = FactionOf(c);
        return members.OrderByDescending(m => _game.Diplomacy.Strength(m)).ThenBy(m => m.Tag, StringComparer.Ordinal).First();
    }

    public bool IsLeader(Country c) => FactionOf(c).Count > 1 && LeaderOf(c) == c;

    public string? CannotSetGoal(Country leader, Country target)
    {
        if (!Enabled) return "Factions are not used in this game";
        if (!IsLeader(leader)) return "Only the leader of a faction sets its goal";
        if (!target.Exists || target == leader) return "Not a country";
        if (FactionOf(leader).Contains(target)) return "A member of the faction cannot be its goal";
        if (Goals.ContainsKey(leader)) return "The faction already has a goal";
        if (_game.World.AtWar(leader, target)) return "Already at war with them";
        return null;
    }

    public string? SetGoal(Country leader, Country target)
    {
        var why = CannotSetGoal(leader, target);
        if (why != null) return why;
        Goals[leader] = new FactionGoal { Leader = leader, Target = target, SetDay = _game.World.Date.TotalDays };
        GoalsSet++;
        foreach (var m in FactionOf(leader).Where(m => m != leader))
            _game.NotifyPlayer($"{leader.Name}, leader of the faction, sets its goal: war on {target.Name}. Join within 30 days.", NotificationKind.Diplomacy, m);
        return null;
    }

    public void ClearGoal(Country leader) => Goals.Remove(leader);

    /// <summary>A member supports the goal when it is at war with its target.</summary>
    public bool Supports(Country member, FactionGoal g) => member == g.Leader || _game.World.AtWar(member, g.Target);

    public int DaysLeft(FactionGoal g) => Math.Max(0, g.SetDay + (int)_game.Data.Defines.Get("faction_goal_days", 30) - _game.World.Date.TotalDays);

    public void MonthlyTick()
    {
        if (!Enabled) return;
        var d = _game.Data.Defines;
        var rng = _game.Rngs.Diplomacy;
        foreach (var g in Goals.Values.ToList())
        {
            if (!g.Leader.Exists || !g.Target.Exists || FactionOf(g.Leader).Count < 2) { Goals.Remove(g.Leader); continue; }
            if (DaysLeft(g) > 0) continue;
            foreach (var m in FactionOf(g.Leader).Where(m => m != g.Leader && !Supports(m, g)).ToList())
            {
                MembersRefused++;
                _game.Diplomacy.AddOpinion(g.Leader, m, "Refused the faction's goal", -d.Get("faction_refusal_opinion", 25), 1);
                _game.NotifyPlayer($"{m.Name} did not join the war on {g.Target.Name} and loses the regard of {g.Leader.Name}.", NotificationKind.Bad, m, g.Leader);
                // a member the leader no longer values may leave the alliance
                if (_game.Diplomacy.Opinion(m, g.Leader) < 0 && rng.NextDouble() < 0.5)
                {
                    _game.World.Relations.SetAlliance(m, g.Leader, false);
                    MembersLeft++;
                    _game.NotifyPlayer($"{m.Name} leaves the faction of {g.Leader.Name}.", NotificationKind.Bad, m, g.Leader);
                }
            }
            Goals.Remove(g.Leader);
        }
        // an AI leader at peace sometimes names a goal: a neighbour it could fight
        foreach (var c in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer && IsLeader(x) && !Goals.ContainsKey(x) && !_game.World.WarsOf(x).Any()).ToList())
        {
            if (rng.NextDouble() > d.Get("faction_goal_chance", 0.01)) continue;
            var cb = (Func<Country, bool>)(t => _game.Diplomacy.BestCasusBelli(c, t) is { } best && _game.Rules.Diplomacy.CannotDeclareWarReason(_game, c, t, best) == null);
            var target = Builtins.NeighborCountries(_game, c).Where(t => !FactionOf(c).Contains(t) && cb(t) && _game.Diplomacy.Strength(t) < _game.Diplomacy.Strength(c)).OrderBy(t => _game.Diplomacy.Opinion(c, t)).FirstOrDefault();
            if (target != null && SetGoal(c, target) == null) _game.Diplomacy.DeclareWar(c, target);
        }
    }

    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterTrigger("is_faction_leader", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && IsLeader(c), (ctx, n) => "Leads a faction");
        s.RegisterTrigger("can_set_faction_goal", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && CannotSetGoal(a, b) == null, (ctx, n) => "Can make that country the faction's goal");
        s.RegisterEffect("set_faction_goal", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) SetGoal(a, b); }, (ctx, n) => "Names that country as the faction's war goal");
    }

    private void Save(Node root)
    {
        foreach (var g in Goals.Values.OrderBy(x => x.Leader.Tag, StringComparer.Ordinal))
        {
            var n = root.AddBlock("goal");
            n.Add("leader", g.Leader.Tag); n.Add("target", g.Target.Tag); n.Add("day", g.SetDay.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        Goals.Clear();
        foreach (var b in root.GetAll("goal"))
            if (_game.World.GetCountry(b.GetString("leader")) is { } l && _game.World.GetCountry(b.GetString("target")) is { } t)
                Goals[l] = new FactionGoal { Leader = l, Target = t, SetDay = (int)b.GetDouble("day") };
    }

    public long Checksum() => Goals.Values.Sum(g => g.Leader.Index * 31L + g.Target.Index + g.SetDay);
}

// =================================================================================================
// Royal marriages
// =================================================================================================

/// <summary>A marriage between two ruling houses: the husband and wife and the countries they come from.</summary>
public sealed class RoyalMarriage
{
    public Country A = null!, B = null!;
    public Character Spouse1 = null!, Spouse2 = null!;
    public int Day;
    public bool Involves(Country c) => A == c || B == c;
    public Country Other(Country c) => A == c ? B : A;
    public Character SpouseOf(Country c) => A == c ? Spouse1 : Spouse2;
}

/// <summary>
/// Royal marriages (expanded_diplomacy feature): the ruling houses of two countries marry a prince to a princess. The countries think the better of each other (+50)
/// while both live. When one ruler dies with no heir, the other house may claim its land (20%), or, when its people are accepted there, the crowns are joined in a
/// personal union.
/// </summary>
public sealed class MarriageSystem
{
    private readonly Game _game;
    public readonly List<RoyalMarriage> Marriages = new();
    private readonly Dictionary<Country, Country> _pendingUnion = new();
    private readonly HashSet<Country> _heirless = new();

    /// <summary>Counts for reports.</summary>
    public int Made, Claims, Unions;

    public MarriageSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "royal_marriages", Write = Save, Read = Load, Reset = () => Marriages.Clear() });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.ExpandedDiplomacy) && _game.Features.Has(FeatureSet.Characters);

    internal void Subscribe(GameEvents hub)
    {
        hub.CharacterDied += OnDied;
        hub.RulerChanged += OnRulerChanged;
    }

    public RoyalMarriage? Between(Country a, Country b) => Marriages.FirstOrDefault(m => m.Involves(a) && m.Involves(b));
    public IEnumerable<RoyalMarriage> Of(Country c) => Marriages.Where(m => m.Involves(c));

    private bool Free(Character ch, int adult) => ch.Alive && ch.Spouse == null && ch.Age(_game.World.Date) >= adult;

    /// <summary>The prince or princess of a house who could marry: the ruler or the heir first, then the ruler's unmarried kin.</summary>
    public Character? Candidate(Country c, bool? female)
    {
        int adult = _game.Data.Defines.AdultAge;
        var pool = new List<Character>();
        if (c.Ruler != null) pool.Add(c.Ruler);
        if (c.Heir != null) pool.Add(c.Heir);
        if (c.Ruler != null)
        {
            pool.AddRange(c.Ruler.Children.Where(k => k.Alive && k.Country == c));
            foreach (var p in new[] { c.Ruler.Father, c.Ruler.Mother }.Where(p => p != null)) pool.AddRange(p!.Children.Where(k => k.Alive && k.Country == c));
        }
        return pool.Distinct().Where(ch => Free(ch, adult) && ch.Country == c && (female == null || ch.Female == female)).OrderBy(ch => ch.IsRuler ? 1 : 0).ThenByDescending(ch => ch.Age(_game.World.Date) < 40 ? 1 : 0).FirstOrDefault();
    }

    public string? CannotMarry(Country a, Country b)
    {
        if (!Enabled) return "Marriages are not used in this game";
        if (a == b || !a.Exists || !b.Exists) return "Not a country";
        if (Between(a, b) != null) return "The houses are already joined";
        if (_game.World.AtWar(a, b)) return "At war";
        if (a.Ruler == null || b.Ruler == null) return "A house without a ruler";
        if (Candidate(a, null) == null) return "No one of our house can marry";
        return null;
    }

    /// <summary>The marriage: picks a groom and bride (a relative of the ruler is made up if the house has no one free), marries them and joins the houses.</summary>
    public string? Marry(Country a, Country b)
    {
        var why = CannotMarry(a, b);
        if (why != null) return why;
        var s1 = Candidate(a, null)!;
        var s2 = Candidate(b, !s1.Female) ?? Invent(b, !s1.Female);
        if (s2 == null) return "No one of their house can marry";
        _game.Characters.Marry(s1, s2);
        Marriages.Add(new RoyalMarriage { A = a, B = b, Spouse1 = s1, Spouse2 = s2, Day = _game.World.Date.TotalDays });
        Made++;
        _game.NotifyPlayer($"{s1.DisplayName} of {a.Name} marries {s2.DisplayName} of {b.Name}.", NotificationKind.Diplomacy, a, b);
        return null;
    }

    private Character? Invent(Country c, bool female)
    {
        if (c.Ruler == null) return null;
        var rng = _game.Rngs.Characters;
        int age = _game.Data.Defines.AdultAge + 2 + rng.Next(8);
        var ch = _game.Characters.Generate(c, female, age, c.Ruler.Family);
        // a child of the ruler when the ruler is old enough, else a brother or sister
        var parent = c.Ruler.Age(_game.World.Date) >= age + 16 ? c.Ruler : c.Ruler.Father ?? c.Ruler;
        ch.Father = parent.Female ? parent.Spouse : parent;
        ch.Mother = parent.Female ? parent : parent.Spouse;
        parent.Children.Add(ch);
        return ch;
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var m in Marriages.ToList())
        {
            if (!m.Spouse1.Alive || !m.Spouse2.Alive || !m.A.Exists || !m.B.Exists || m.Spouse1.Spouse != m.Spouse2) { Marriages.Remove(m); continue; }
            double v = _game.Data.Defines.Get("marriage_opinion", 50);
            OpinionTools.Set(_game, m.A, m.B, "Royal marriage", v);
            OpinionTools.Set(_game, m.B, m.A, "Royal marriage", v);
        }
    }

    /// <summary>A ruler dies: if the house is left without an heir, the marriage may bring the other house a claim or the crown.</summary>
    private void OnDied(Character ch, string reason)
    {
        if (!Enabled || ch.Country is not { } c || c.Ruler != ch) return;
        bool heirs = ch.Children.Any(k => k.Alive && k.Country == c) || c.Heir is { Alive: true } h && h != ch && h.Family == ch.Family;
        if (heirs || Of(c).FirstOrDefault() is not { } m) return;
        var other = m.Other(c);
        if (!other.Exists) return;
        if (_game.Population.Enabled && _game.Population.Of(other).Culture is { } people && _game.Population.IsAccepted(c, people)) _pendingUnion[c] = other;
        else if (_game.Rngs.Characters.NextDouble() < _game.Data.Defines.Get("marriage_claim_chance", 0.20)) _heirless.Add(c);
        _pendingClaimBy[c] = other;
    }

    private readonly Dictionary<Country, Country> _pendingClaimBy = new();

    private void OnRulerChanged(Country c, Character? old, Character now)
    {
        if (_pendingUnion.Remove(c, out var holder))
        {
            _pendingClaimBy.Remove(c);
            var m = Between(c, holder);
            var type = _game.Data.SubjectTypes.TryGetValue("personal_union", out var u) ? u : _game.Data.SubjectTypes.Values.First();
            _game.Diplomacy.MakeSubject(c, holder, type);
            if (m != null && m.SpouseOf(holder) is { Alive: true } prince && prince != now) { prince.Country = c; _game.Characters.SetRuler(c, prince); }
            Unions++;
            _game.NotifyPlayer($"{c.Name} has no heir: its crown passes to the house of {holder.Name} in a personal union.", NotificationKind.Diplomacy, c, holder);
            return;
        }
        if (_heirless.Remove(c) && _pendingClaimBy.Remove(c, out var claimant))
        {
            foreach (var s in c.OwnedStates) s.Claims.Add(claimant);
            Claims++;
            _game.NotifyPlayer($"The house of {claimant.Name} claims the land of {c.Name}, which has no heir.", NotificationKind.Diplomacy, c, claimant);
        }
        else _pendingClaimBy.Remove(c);
    }

    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterTrigger("can_marry", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && CannotMarry(a, b) == null, (ctx, n) => "Can marry into that house");
        s.RegisterTrigger("has_marriage_with", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && Between(a, b) != null, (ctx, n) => "Is married into that house");
        s.RegisterEffect("royal_marriage", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) Marry(a, b); }, (ctx, n) => "The two ruling houses marry");
    }

    private void Save(Node root)
    {
        foreach (var m in Marriages)
        {
            var n = root.AddBlock("marriage");
            n.Add("a", m.A.Tag); n.Add("b", m.B.Tag);
            n.Add("spouse1", m.Spouse1.Key, quoted: true); n.Add("spouse2", m.Spouse2.Key, quoted: true);
            n.Add("day", m.Day.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        Marriages.Clear();
        foreach (var b in root.GetAll("marriage"))
            if (_game.World.GetCountry(b.GetString("a")) is { } a && _game.World.GetCountry(b.GetString("b")) is { } c
                && _game.World.GetCharacter(b.GetString("spouse1")) is { } s1 && _game.World.GetCharacter(b.GetString("spouse2")) is { } s2)
                Marriages.Add(new RoyalMarriage { A = a, B = c, Spouse1 = s1, Spouse2 = s2, Day = (int)b.GetDouble("day") });
    }

    public long Checksum() => Marriages.Sum(m => m.A.Index * 31L + m.B.Index);
}
