using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Diplomacy;

/// <summary>One line of the ranking of countries by power.</summary>
public sealed class PowerEntry
{
    public Country Country = null!;
    public double Score, PreviousScore;
    public int Rank, PreviousRank;
    public bool Great;
    /// <summary>Share of each part of the score (industry, army, navy and air, people), 0-100.</summary>
    public double Industry, Army, Fleet, People;
}

/// <summary>Two great powers that both hold more than half of the influence over one country.</summary>
public sealed class SphereCrisis
{
    public Country Target = null!;
    public List<Country> Powers = new();
    public int StartDay;
}

/// <summary>A body from <c>common/bodies/*.txt</c>: a league of countries that votes on sanctions.</summary>
public sealed class BodyDef
{
    public string Key = "", Name = "", Desc = "";
    public List<string> Founders = new();
}

public sealed class Sanction
{
    public string Body = "";
    public Country Target = null!;
    public int ExpireDay;
}

/// <summary>
/// Great powers and spheres (great_powers feature): a monthly ranking by power (40% industry, 30% army, 20% navy and air, 10% people; the top eight are the great
/// powers), influence that great powers build over weaker countries until they can draw them into a sphere (opinion, trade), crises when two powers want the same
/// country, and leagues whose two-thirds votes impose sanctions.
/// </summary>
public sealed class GreatPowerSystem
{
    private readonly Game _game;
    public readonly List<PowerEntry> Ranking = new();
    private readonly Dictionary<Country, PowerEntry> _entry = new();
    private readonly Dictionary<(Country Power, Country Target), double> _influence = new();
    private readonly HashSet<(Country Power, Country Target)> _campaigns = new();
    private readonly Dictionary<Country, Country> _sphere = new();
    public readonly List<SphereCrisis> Crises = new();
    public readonly Dictionary<string, BodyDef> Bodies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Country>> _members = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<Sanction> Sanctions = new();
    /// <summary>The last votes taken: a line of text for the world panel.</summary>
    public readonly List<string> VoteLog = new();
    private readonly Dictionary<Country, Dictionary<string, double>> _mods = new();
    private int _builtMonth = int.MinValue;

    /// <summary>Counts for reports.</summary>
    public int SpheresMade, CrisesStarted, SanctionsPassed, VotesLost;

    public GreatPowerSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "great_powers", Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.GreatPowers);
    private int GreatCount => (int)_game.Data.Defines.Get("great_power_count", 8);

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Bodies.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/bodies"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Bodies[n.Key] = new BodyDef { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!, Founders = n.GetList("members").ToList() };
            }
    }

    // ------------------------------------------------------------------------------------- ranking
    private double IndustryOf(Country c) => _game.Production.Enabled ? _game.Production.CivilianFactories(c) + _game.Production.MilitaryFactories(c) : c.OwnedStates.Sum(s => s.Tax);

    private double FleetOf(Country c)
        => (_game.Navy.Enabled ? _game.Navy.FleetsOf(c).Sum(f => f.Ships.Sum(s => 1 + 2 * s.HpFraction)) : 0) + (_game.Air.Enabled ? _game.Air.WingsOf(c).Sum(w => w.Planes) / 10 : 0);

    /// <summary>Recomputes the ranking (once a month, or when asked).</summary>
    public void Recompute()
    {
        var live = _game.World.Countries.Where(c => c.Exists).ToList();
        var ind = live.ToDictionary(c => c, IndustryOf);
        var army = live.ToDictionary(c => c, c => _game.Diplomacy.Strength(c));
        var fleet = live.ToDictionary(c => c, FleetOf);
        var ppl = live.ToDictionary(c => c, c => _game.Population.Enabled ? _game.Population.TotalOf(c) : 0.0);
        double N(Dictionary<Country, double> d, Country c) { double m = d.Values.DefaultIfEmpty(0).Max(); return m > 0 ? 100 * d[c] / m : 0; }
        var w = new (double W, Dictionary<Country, double> D)[] { (0.4, ind), (0.3, army), (0.2, fleet), (0.1, ppl) };
        var known = w.Where(x => x.D.Values.DefaultIfEmpty(0).Max() > 0).ToList(); // a part nobody has (no navy at all) leaves the others to share its weight
        double totalW = known.Sum(x => x.W);
        var old = _entry.ToDictionary(kv => kv.Key, kv => kv.Value);
        Ranking.Clear();
        _entry.Clear();
        foreach (var c in live)
        {
            double score = totalW > 0 ? known.Sum(x => x.W * N(x.D, c)) / totalW : 0;
            score *= 1 + _game.Data.Defines.Get("rival_score", 0.05) * _game.Reputation.RivalsOf(c).Count;
            var e = new PowerEntry { Country = c, Score = score, Industry = N(ind, c), Army = N(army, c), Fleet = N(fleet, c), People = N(ppl, c) };
            if (old.TryGetValue(c, out var o)) { e.PreviousScore = o.Score; e.PreviousRank = o.Rank; }
            Ranking.Add(e);
        }
        Ranking.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Country.Tag, b.Country.Tag));
        for (int i = 0; i < Ranking.Count; i++)
        {
            Ranking[i].Rank = i + 1;
            Ranking[i].Great = i < GreatCount;
            if (Ranking[i].PreviousRank == 0) Ranking[i].PreviousRank = Ranking[i].Rank;
            _entry[Ranking[i].Country] = Ranking[i];
        }
        _builtMonth = _game.World.Date.Year * 12 + _game.World.Date.Month;
    }

    private void Ensure()
    {
        if (_builtMonth != _game.World.Date.Year * 12 + _game.World.Date.Month) Recompute();
    }

    public PowerEntry? EntryOf(Country c) { Ensure(); return _entry.GetValueOrDefault(c); }
    public double Score(Country c) => EntryOf(c)?.Score ?? 0;
    public int RankOf(Country c) => EntryOf(c)?.Rank ?? int.MaxValue;
    public bool IsGreat(Country c) => EntryOf(c)?.Great == true;
    public IEnumerable<Country> Greats => Ranking.Where(e => e.Great).Select(e => e.Country);

    // ------------------------------------------------------------------------------------- spheres
    public double Influence(Country power, Country target) => _influence.GetValueOrDefault((power, target));
    public bool Campaigning(Country power, Country target) => _campaigns.Contains((power, target));
    public Country? SphereOf(Country target) => _sphere.GetValueOrDefault(target);
    public IEnumerable<Country> SphereMembers(Country power) => _sphere.Where(kv => kv.Value == power).Select(kv => kv.Key);
    public IEnumerable<Country> Campaigns(Country power) => _campaigns.Where(k => k.Power == power).Select(k => k.Target);
    public int MaxCampaigns(Country power) => _game.Envoys.Enabled ? Math.Max(1, _game.Envoys.Total(power) - 1) : (int)_game.Data.Defines.Get("influence_campaigns", 3);

    public string? CannotCampaign(Country power, Country target)
    {
        if (!Enabled) return "Spheres are not used in this game";
        if (!IsGreat(power)) return "Only a great power builds influence";
        if (power == target || !target.Exists) return "Not a country";
        if (IsGreat(target)) return "A great power cannot be drawn into a sphere";
        if (_game.World.Relations.OverlordOf(target) is not null) return "A subject has no foreign policy of its own";
        if (SphereOf(target) == power) return "Already in our sphere";
        if (Campaigning(power, target)) return "Already building influence there";
        if (_campaigns.Count(k => k.Power == power) >= MaxCampaigns(power)) return "No more campaigns at once";
        return null;
    }

    public string? StartCampaign(Country power, Country target)
    {
        var why = CannotCampaign(power, target);
        if (why != null) return why;
        _campaigns.Add((power, target));
        return null;
    }

    public void StopCampaign(Country power, Country target) => _campaigns.Remove((power, target));

    public string? CannotAddToSphere(Country power, Country target)
    {
        if (!Enabled) return "Spheres are not used in this game";
        if (!IsGreat(power)) return "Only a great power has a sphere";
        if (SphereOf(target) == power) return "Already in our sphere";
        if (SphereOf(target) != null) return $"Already in the sphere of {SphereOf(target)!.Name}";
        double need = _game.Data.Defines.Get("sphere_influence", 50);
        if (Influence(power, target) <= need) return $"Needs more than {need:0} influence";
        if (Crises.Any(c => c.Target == target)) return "The country is contested by another great power";
        return null;
    }

    public string? AddToSphere(Country power, Country target)
    {
        var why = CannotAddToSphere(power, target);
        if (why != null) return why;
        _sphere[target] = power;
        _campaigns.Remove((power, target));
        SpheresMade++;
        _mods.Clear();
        _game.NotifyPlayer($"{target.Name} enters the sphere of {power.Name}.", NotificationKind.Diplomacy, power, target);
        _game.Events.FireOnAction("on_sphere_joined", target, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["power"] = power });
        return null;
    }

    public void ReleaseFromSphere(Country target)
    {
        if (_sphere.Remove(target)) _mods.Clear();
    }

    /// <summary>Country modifiers from spheres: a member trades 10% better, its power 1% a member (at most 8%).</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c || !key.Equals("income", StringComparison.OrdinalIgnoreCase)) return 0;
        double v = 0;
        if (SphereOf(c) != null) v += _game.Data.Defines.Get("sphere_trade", 0.10);
        v += Math.Min(0.08, 0.01 * _sphere.Count(kv => kv.Value == c));
        v -= Math.Min(0.15, 0.05 * Sanctions.Count(s => s.Target == c));
        return v;
    }

    // ------------------------------------------------------------------------------------- bodies
    public IReadOnlyCollection<Country> MembersOf(string body) => _members.TryGetValue(body, out var m) ? m : (IReadOnlyCollection<Country>)Array.Empty<Country>();
    public bool IsMember(string body, Country c) => _members.TryGetValue(body, out var m) && m.Contains(c);
    public IEnumerable<BodyDef> BodiesOf(Country c) => Bodies.Values.Where(b => IsMember(b.Key, c));
    public Country? LeaderOf(string body) => MembersOf(body).Where(m => m.Exists).OrderByDescending(Score).ThenBy(m => m.Tag, StringComparer.Ordinal).FirstOrDefault();

    public string? Join(string body, Country c)
    {
        if (!Bodies.ContainsKey(body)) return "No such body";
        if (!c.Exists) return "Not a country";
        if (IsMember(body, c)) return "Already a member";
        if (!_members.TryGetValue(body, out var m)) _members[body] = m = new HashSet<Country>();
        m.Add(c);
        _game.NotifyPlayer($"{c.Name} joins the {Bodies[body].Name}.", NotificationKind.Diplomacy, c);
        return null;
    }

    public void Leave(string body, Country c)
    {
        if (_members.TryGetValue(body, out var m)) m.Remove(c);
    }

    public bool IsSanctioned(Country buyer, Country seller) => Enabled && Sanctions.Any(s => s.Target == buyer && IsMember(s.Body, seller) || s.Target == seller && IsMember(s.Body, buyer));

    /// <summary>How a member would vote on sanctions against a country: yes unless it likes the target or is tied to it.</summary>
    public bool VotesYes(Country member, Country target)
    {
        var r = _game.World.Relations;
        if (r.AreAllied(member, target) || r.AreOverlordAndSubject(member, target) || SphereOf(member) == target || SphereOf(target) == member) return false;
        return _game.Diplomacy.Opinion(member, target) < 0;
    }

    public string? CannotPropose(string body, Country proposer, Country target)
    {
        if (!Enabled) return "Leagues are not used in this game";
        if (!IsMember(body, proposer)) return "Not a member";
        if (IsMember(body, target)) return "A member cannot be sanctioned";
        if (MembersOf(body).Count < 3) return "A league needs three members to vote";
        if (Sanctions.Any(s => s.Body == body && s.Target == target)) return "Already under sanctions";
        return null;
    }

    /// <summary>Puts sanctions to the vote: they pass with two thirds of the members.</summary>
    public (bool Passed, int Yes, int Total) Propose(string body, Country proposer, Country target)
    {
        var members = MembersOf(body).Where(m => m.Exists).ToList();
        int yes = members.Count(m => m == proposer || VotesYes(m, target));
        bool pass = yes * 3 >= members.Count * 2;
        VoteLog.Add($"{_game.World.Date.ToDisplay()}: {Bodies[body].Name} - sanctions on {target.Name}: {yes} of {members.Count} for, {(pass ? "passed" : "failed")}");
        if (VoteLog.Count > 12) VoteLog.RemoveAt(0);
        if (pass)
        {
            Sanctions.Add(new Sanction { Body = body, Target = target, ExpireDay = _game.World.Date.TotalDays + (int)(_game.Data.Defines.Get("sanction_years", 5) * 365) });
            SanctionsPassed++;
            _mods.Clear();
            _game.NotifyPlayer($"The {Bodies[body].Name} votes sanctions on {target.Name} ({yes} of {members.Count}).", NotificationKind.War, members.Append(target).ToArray());
        }
        else VotesLost++;
        return (pass, yes, members.Count);
    }

    // ------------------------------------------------------------------------------------- monthly
    public void OnGameStart()
    {
        if (!Enabled) return;
        Recompute();
        foreach (var b in Bodies.Values)
            foreach (var t in b.Founders)
                if (_game.World.GetCountry(t) is { Exists: true } c) Join(b.Key, c);
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        Recompute();
        var d = _game.Data.Defines;
        double gain = d.Get("influence_gain", 1), decay = d.Get("influence_decay", 0.5);
        _mods.Clear();
        // campaigns of powers that lost their rank or whose target became a power end
        foreach (var k in _campaigns.ToList()) if (!k.Power.Exists || !k.Target.Exists || !IsGreat(k.Power) || IsGreat(k.Target)) _campaigns.Remove(k);
        foreach (var kv in _sphere.ToList())
            if (!kv.Key.Exists || !kv.Value.Exists || !IsGreat(kv.Value) || IsGreat(kv.Key) || _game.World.AtWar(kv.Key, kv.Value) || Influence(kv.Value, kv.Key) < d.Get("sphere_keep", 25)) _sphere.Remove(kv.Key);
        foreach (var k in _influence.Keys.ToList())
        {
            double v = _influence[k];
            v = _campaigns.Contains(k) ? Math.Min(100, v + gain) : Math.Max(0, v - decay);
            if (_sphere.TryGetValue(k.Target, out var holder) && holder == k.Power) v = Math.Max(v, d.Get("sphere_keep", 25) + 5);
            if (v <= 0) _influence.Remove(k); else _influence[k] = v;
        }
        foreach (var k in _campaigns) if (!_influence.ContainsKey(k)) _influence[k] = gain;
        // members are fond of their power
        foreach (var (target, power) in _sphere) OpinionTools.Set(_game, target, power, "In our sphere", d.Get("sphere_opinion", 20));
        // two powers with more than half of the influence: a crisis
        double need = d.Get("sphere_influence", 50);
        foreach (var grp in _influence.Where(kv => kv.Value > need && IsGreat(kv.Key.Power)).GroupBy(kv => kv.Key.Target).Where(g => g.Count() >= 2))
        {
            if (Crises.Any(c => c.Target == grp.Key) || !grp.Key.Exists) continue;
            var cr = new SphereCrisis { Target = grp.Key, Powers = grp.Select(x => x.Key.Power).ToList(), StartDay = _game.World.Date.TotalDays };
            Crises.Add(cr);
            CrisesStarted++;
            _game.NotifyPlayer($"Crisis: {string.Join(" and ", cr.Powers.Select(p => p.Name))} both claim influence over {cr.Target.Name}.", NotificationKind.War, cr.Powers.Append(cr.Target).ToArray());
            _game.Events.FireOnAction("on_sphere_crisis", cr.Target);
        }
        int crisisDays = (int)(d.Get("crisis_months", 6) * 30);
        foreach (var cr in Crises.ToList())
        {
            if (!cr.Target.Exists) { Crises.Remove(cr); continue; }
            var rival = cr.Powers.Where(p => p.Exists && Influence(p, cr.Target) > need).ToList();
            if (rival.Count < 2) { Crises.Remove(cr); continue; }
            if (_game.World.Date.TotalDays - cr.StartDay < crisisDays) continue;
            // the weaker power backs down
            foreach (var weaker in rival.OrderBy(Score).Take(rival.Count - 1))
            {
                _influence[(weaker, cr.Target)] = need * 0.5;
                _campaigns.Remove((weaker, cr.Target));
                _game.NotifyPlayer($"{weaker.Name} backs down over {cr.Target.Name}.", NotificationKind.Diplomacy, weaker, cr.Target);
            }
            Crises.Remove(cr);
        }
        Sanctions.RemoveAll(s => s.ExpireDay <= _game.World.Date.TotalDays || !s.Target.Exists);
        foreach (var s in Sanctions)
            foreach (var m in MembersOf(s.Body).Where(m => m.Exists)) OpinionTools.Set(_game, m, s.Target, "League sanctions", -d.Get("sanction_opinion", 20));
        foreach (var b in _members.Values) b.RemoveWhere(m => !m.Exists);
        AiMonthly();
    }

    private void AiMonthly()
    {
        if (!_game.Ai.Enabled) return;
        var rng = _game.Rngs.Diplomacy;
        foreach (var power in Greats.Where(p => !p.IsPlayer).ToList())
        {
            // build influence where it is cheap and nobody else holds it
            if (_campaigns.Count(k => k.Power == power) < Math.Min(2, MaxCampaigns(power)) && rng.NextDouble() < 0.3)
            {
                var pick = Burger.Core.Scripting.Builtins.NeighborCountries(_game, power).Where(t => CannotCampaign(power, t) == null && _game.Diplomacy.Opinion(t, power) > -40)
                    .OrderByDescending(t => Score(t)).FirstOrDefault();
                if (pick != null) StartCampaign(power, pick);
            }
            foreach (var t in _campaigns.Where(k => k.Power == power).Select(k => k.Target).ToList())
                if (CannotAddToSphere(power, t) == null) AddToSphere(power, t);
        }
        // countries join the league of a friendly leader
        foreach (var body in Bodies.Values)
        {
            var leader = LeaderOf(body.Key);
            if (leader == null || MembersOf(body.Key).Count == 0) continue;
            foreach (var c in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer && !IsMember(body.Key, x) && x != leader).ToList())
                if (rng.NextDouble() < 0.02 && _game.Diplomacy.Opinion(c, leader) >= 30 && Burger.Core.Scripting.Builtins.NeighborCountries(_game, leader).Contains(c)) Join(body.Key, c);
            // the leader proposes sanctions on the member of nobody it hates most
            if (!leader.IsPlayer && MembersOf(body.Key).Count >= 3 && rng.NextDouble() < 0.1)
            {
                var foe = _game.World.Countries.Where(x => x.Exists && !IsMember(body.Key, x) && CannotPropose(body.Key, leader, x) == null && _game.Diplomacy.Opinion(leader, x) <= -40 && _game.Rules.Diplomacy.HasWarReach(_game, leader, x))
                    .OrderBy(x => _game.Diplomacy.Opinion(leader, x)).FirstOrDefault();
                if (foe != null) Propose(body.Key, leader, foe);
            }
        }
    }

    // ------------------------------------------------------------------------------------- script
    public void RegisterScript()
    {
        var s = _game.Script;
        s.RegisterTrigger("is_great_power", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && IsGreat(c), (ctx, n) => "Is a great power");
        s.RegisterTrigger("in_sphere_of", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && SphereOf(a) == b, (ctx, n) => "Is in that power's sphere");
        s.RegisterTrigger("can_campaign_influence", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && CannotCampaign(a, b) == null, (ctx, n) => "Can build influence there");
        s.RegisterTrigger("is_campaigning_influence", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && Campaigning(a, b), (ctx, n) => "Is building influence there");
        s.RegisterTrigger("can_add_to_sphere", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && CannotAddToSphere(a, b) == null, (ctx, n) => "Can draw that country into its sphere");
        s.RegisterTrigger("in_body", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } c && IsMember(n.Value ?? "", c), (ctx, n) => $"Is a member of {n.Value}");
        s.RegisterTrigger("shares_body_with", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && BodiesOf(a).Any(x => IsMember(x.Key, b)), (ctx, n) => "Shares a league with that country");
        s.RegisterTrigger("can_propose_sanctions", (ctx, n) => ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && BodiesOf(a).Any(x => CannotPropose(x.Key, a, b) == null), (ctx, n) => "Can put sanctions on that country to a vote");
        s.RegisterEffect("start_influence", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) StartCampaign(a, b); }, (ctx, n) => "Starts building influence there");
        s.RegisterEffect("stop_influence", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) StopCampaign(a, b); }, (ctx, n) => "Stops building influence there");
        s.RegisterEffect("add_to_sphere", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) AddToSphere(a, b); }, (ctx, n) => "Draws that country into its sphere");
        s.RegisterEffect("release_from_sphere", (ctx, n) => { if (s.ArgCountry(n, ctx) is { } b) ReleaseFromSphere(b); }, (ctx, n) => "Lets that country leave the sphere");
        s.RegisterEffect("join_body", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) Join(n.Value ?? "", c); }, (ctx, n) => $"Joins the {n.Value}");
        s.RegisterEffect("leave_body", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) Leave(n.Value ?? "", c); }, (ctx, n) => $"Leaves the {n.Value}");
        s.RegisterEffect("join_bodies_of", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c && s.ArgCountry(n, ctx) is { } leader) foreach (var b in BodiesOf(leader).ToList()) Join(b.Key, c); }, (ctx, n) => "Joins that country's leagues");
        s.RegisterEffect("leave_all_bodies", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } c) foreach (var b in BodiesOf(c).ToList()) Leave(b.Key, c); }, (ctx, n) => "Leaves its leagues");
        s.RegisterEffect("propose_sanctions", (ctx, n) => { if (ScopeHelpers.CountryOf(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b) foreach (var body in BodiesOf(a).ToList()) if (CannotPropose(body.Key, a, b) == null) { Propose(body.Key, a, b); break; } }, (ctx, n) => "Puts sanctions on that country to a vote");
        s.RegisterValue("power_rank", (ctx, scope) => scope is Country c && Enabled ? RankOf(c) : null);
        s.RegisterValue("power_score", (ctx, scope) => scope is Country c && Enabled ? Score(c) : null);
        s.RegisterValue("sphere_size", (ctx, scope) => scope is Country c && Enabled ? _sphere.Count(kv => kv.Value == c) : null);
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        // the ranking is worked out once a month from the state of the first moment it is asked for: a loaded game must not work it out again from the state of the save
        root.Add("built_month", _builtMonth.ToString(CultureInfo.InvariantCulture));
        foreach (var e in Ranking)
        {
            var n = root.AddBlock("entry");
            n.Add("tag", e.Country.Tag); n.Add("score", F(e.Score)); n.Add("previous_score", F(e.PreviousScore));
            n.Add("rank", e.Rank.ToString(CultureInfo.InvariantCulture)); n.Add("previous_rank", e.PreviousRank.ToString(CultureInfo.InvariantCulture));
            n.Add("industry", F(e.Industry)); n.Add("army", F(e.Army)); n.Add("fleet", F(e.Fleet)); n.Add("people", F(e.People));
            if (e.Great) n.Add("great", "yes");
        }
        foreach (var kv in _influence.OrderBy(k => k.Key.Power.Tag, StringComparer.Ordinal).ThenBy(k => k.Key.Target.Tag, StringComparer.Ordinal))
        {
            var n = root.AddBlock("influence");
            n.Add("power", kv.Key.Power.Tag); n.Add("target", kv.Key.Target.Tag); n.Add("value", F(kv.Value));
            if (_campaigns.Contains(kv.Key)) n.Add("campaign", "yes");
        }
        // a campaign can run before it has any influence to show: it has no entry above then
        foreach (var k in _campaigns.Where(k => !_influence.ContainsKey(k)).OrderBy(k => k.Power.Tag, StringComparer.Ordinal).ThenBy(k => k.Target.Tag, StringComparer.Ordinal))
        { var n = root.AddBlock("campaign"); n.Add("power", k.Power.Tag); n.Add("target", k.Target.Tag); }
        foreach (var kv in _sphere) { var n = root.AddBlock("sphere"); n.Add("target", kv.Key.Tag); n.Add("power", kv.Value.Tag); }
        foreach (var cr in Crises) { var n = root.AddBlock("crisis"); n.Add("target", cr.Target.Tag); n.Add("day", cr.StartDay.ToString(CultureInfo.InvariantCulture)); n.Add("powers", string.Join(' ', cr.Powers.Select(p => p.Tag)), quoted: true); }
        foreach (var kv in _members) { var n = root.AddBlock("body"); n.Add("key", kv.Key); n.Add("members", string.Join(' ', kv.Value.OrderBy(m => m.Tag, StringComparer.Ordinal).Select(m => m.Tag)), quoted: true); }
        foreach (var s in Sanctions) { var n = root.AddBlock("sanction"); n.Add("body", s.Body); n.Add("target", s.Target.Tag); n.Add("expire", s.ExpireDay.ToString(CultureInfo.InvariantCulture)); }
        foreach (var line in VoteLog) root.AddBlock("vote").Add("text", line, quoted: true);
    }

    private void Load(Node root)
    {
        Reset();
        var w = _game.World;
        foreach (var b in root.GetAll("entry"))
            if (w.GetCountry(b.GetString("tag")) is { } ec)
            {
                var e = new PowerEntry { Country = ec, Score = b.GetDouble("score"), PreviousScore = b.GetDouble("previous_score"), Rank = (int)b.GetDouble("rank"), PreviousRank = (int)b.GetDouble("previous_rank"),
                    Industry = b.GetDouble("industry"), Army = b.GetDouble("army"), Fleet = b.GetDouble("fleet"), People = b.GetDouble("people"), Great = b.GetBool("great") };
                Ranking.Add(e); _entry[ec] = e;
            }
        Ranking.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        if (Ranking.Count > 0) _builtMonth = (int)root.GetDouble("built_month");
        foreach (var b in root.GetAll("influence"))
            if (w.GetCountry(b.GetString("power")) is { } p && w.GetCountry(b.GetString("target")) is { } t)
            {
                _influence[(p, t)] = b.GetDouble("value");
                if (b.GetBool("campaign")) _campaigns.Add((p, t));
            }
        foreach (var b in root.GetAll("campaign"))
            if (w.GetCountry(b.GetString("power")) is { } cp && w.GetCountry(b.GetString("target")) is { } ct) _campaigns.Add((cp, ct));
        foreach (var b in root.GetAll("sphere"))
            if (w.GetCountry(b.GetString("target")) is { } t && w.GetCountry(b.GetString("power")) is { } p) _sphere[t] = p;
        foreach (var b in root.GetAll("crisis"))
            if (w.GetCountry(b.GetString("target")) is { } t)
                Crises.Add(new SphereCrisis { Target = t, StartDay = (int)b.GetDouble("day"), Powers = (b.GetString("powers") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => w.GetCountry(x)).Where(x => x != null).Cast<Country>().ToList() });
        foreach (var b in root.GetAll("body"))
            _members[b.GetString("key") ?? ""] = (b.GetString("members") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => w.GetCountry(x)).Where(x => x != null).Cast<Country>().ToHashSet();
        foreach (var b in root.GetAll("sanction"))
            if (w.GetCountry(b.GetString("target")) is { } t) Sanctions.Add(new Sanction { Body = b.GetString("body") ?? "", Target = t, ExpireDay = (int)b.GetDouble("expire") });
        foreach (var b in root.GetAll("vote")) if (b.GetString("text") is { } txt) VoteLog.Add(txt);
    }

    private void Reset()
    {
        _influence.Clear(); _campaigns.Clear(); _sphere.Clear(); Crises.Clear(); _members.Clear(); Sanctions.Clear(); VoteLog.Clear(); _mods.Clear();
        Ranking.Clear(); _entry.Clear(); _builtMonth = int.MinValue;
    }

    /// <summary>Hash for multiplayer checks.</summary>
    public long Checksum()
    {
        long h = 47;
        foreach (var kv in _sphere.OrderBy(k => k.Key.Tag, StringComparer.Ordinal)) h = h * 31 + kv.Key.Index * 7 + kv.Value.Index;
        foreach (var kv in _influence) h += (long)Math.Round(kv.Value * 10);
        return h * 31 + Sanctions.Count;
    }
}
