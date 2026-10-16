using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Politics;

public sealed partial class PoliticsSystem
{
    // =============================================================================================
    // Movements (Victoria 3 style): supporters of a shut-out ideology build up to a revolt
    // =============================================================================================

    public IEnumerable<Movement> MovementsOf(Country c) => Movements.Where(m => m.Country == c);

    /// <summary>What a shut-out ideology demands, or null if it can get its way legally (it can win elections).</summary>
    private (MovementDemand Demand, string? Law)? DemandOf(Country c, CountryPolitics p, string ideology)
    {
        if (InGovernment(c, ideology)) return null;
        if (IsBanned(c, p, ideology))
        {
            // the law it wants: the first law of the banning group that does not ban it
            foreach (var kv in p.Laws)
            {
                if (!Laws.TryGetValue(kv.Value, out var current) || !(current.Bans.Contains(ideology) || current.SingleParty)) continue;
                var target = current.Group.Laws.FirstOrDefault(l => !l.Bans.Contains(ideology) && !l.SuspendsElections)
                             ?? current.Group.Laws.FirstOrDefault(l => !l.Bans.Contains(ideology) && !l.SingleParty);
                if (target != null) return (MovementDemand.Legalize, target.Key);
            }
            return (MovementDemand.Power, null);
        }
        if (HoldsElections(c, p) && !Rules.NeedsAssent(_game, c, Ideology(ideology)!)) return null; // free elections: it can win seats
        return (MovementDemand.Power, null);
    }

    private void SpawnGroups(Movement m, IdeologyDef ideology)
    {
        m.Groups.Clear();
        foreach (var (key, strength) in Rules.MovementInterestGroups(_game, m.Country, ideology))
        {
            if (m.Groups.Any(g => g.Key == key)) continue;
            m.Groups.Add(new InterestGroup { Key = key, Strength = Math.Clamp(strength, 5, 100), Satisfaction = Rules.GroupSatisfaction(_game, m.Country, m, key) });
        }
    }

    public static string GroupName(string key) => key switch { "freedom" => "Freedom", "security" => "Security", "army" => "A larger army", "independence" => "Independence", _ => key };

    public static string GroupWants(string key) => key switch
    {
        "freedom" => "open politics, no repression, a say in government",
        "security" => "order on the streets: a stable state that keeps the peace",
        "army" => "a bigger standing army (about 0.6 divisions per state)",
        "independence" => "an end to foreign rule: lower liberty desire",
        _ => key,
    };

    /// <summary>Groups drift toward what the state really gives them, grow while denied and shrink when satisfied; the movement turns violent in proportion.</summary>
    private void UpdateGroups(Country c, Movement m, IdeologyDef ideology)
    {
        var d = _game.Data.Defines;
        m.Heat *= 1 - d.Get("movement_heat_decay", 0.25);
        foreach (var g in m.Groups)
        {
            g.Satisfaction += (Rules.GroupSatisfaction(_game, c, m, g.Key) - g.Satisfaction) * d.Get("interest_satisfaction_rate", 0.2);
            g.Strength = Math.Clamp(g.Strength + (0.5 - g.Satisfaction) * d.Get("interest_strength_rate", 4), 5, 100);
        }
        double chance = Rules.MovementIncidentChance(_game, c, m);
        if (chance > 0 && _game.Rngs.Politics.NextDouble() < chance) Incident(c, m, ideology);
    }

    /// <summary>A violent act from the group that is denied most: protests for freedom, riots and bombings for security, a mutiny for the army.</summary>
    private void Incident(Country c, Movement m, IdeologyDef ideology)
    {
        var weights = m.Groups.Select(g => g.Strength * (1 - g.Satisfaction) + 1).ToList();
        double roll = _game.Rngs.Politics.NextDouble() * weights.Sum();
        int pick = 0;
        for (; pick < weights.Count - 1 && roll >= weights[pick]; pick++) roll -= weights[pick];
        var group = m.Groups[pick];
        string adj = ideology.Adjective.ToLowerInvariant();
        string what;
        switch (group.Key)
        {
            case "freedom": m.Heat = Math.Min(100, m.Heat + 15); m.Progress = Math.Min(100, m.Progress + 5); m.Organization = Math.Min(100, m.Organization + 1); what = $"Mass {adj} protests for freedom"; break;
            case "independence": m.Heat = Math.Min(100, m.Heat + 15); m.Progress = Math.Min(100, m.Progress + 6); what = $"Nationalist {adj} demonstrations against foreign rule"; break;
            case "security": m.Heat = Math.Min(100, m.Heat + 30); m.Progress = Math.Min(100, m.Progress + 3); what = $"{ideology.Adjective} riots and bombings"; break;
            default: m.Heat = Math.Min(100, m.Heat + 20); m.Organization = Math.Min(100, m.Organization + 4); what = $"Officers sympathetic to the {adj} cause mutiny"; break;
        }
        _modsDirty.Add(c);
        if (c.IsPlayer) _game.Notify($"{what}: they want {GroupName(group.Key).ToLowerInvariant()}.", NotificationKind.Bad);
    }

    /// <summary>Why nothing more can be done for one group now, or null.</summary>
    public string? CannotMeet(Movement m, string key)
        => m.Groups.All(g => g.Key != key) ? "No such group" : CanAfford(m.Country, MeetCost) ? null : $"Needs {MeetCost:0} {PowerName}";

    public double MeetCost => ConcedeCost * _game.Data.Defines.Get("meet_group_cost_share", 0.5);

    public List<string> ExplainGroup(Movement m, InterestGroup g)
    {
        var lines = new List<string>
        {
            $"{GroupName(g.Key)}: wants {GroupWants(g.Key)}",
            $"Strength {g.Strength:0} of 100; the state gives it {g.Satisfaction:P0} of what it wants",
            g.Satisfaction < 0.5 ? "Denied, it grows and pushes the movement to violence" : "Satisfied, it fades and the movement calms",
        };
        lines.Add($"Click to meet its demands: {MeetCost:0} {PowerName}, it loses strength");
        return lines;
    }

    /// <summary>Gives one interest group something now: it loses strength and is better satisfied, and the movement is a little calmer. Cheaper than a full concession.</summary>
    public string? MeetGroup(Movement m, string key)
    {
        var reason = CannotMeet(m, key);
        if (reason != null) return reason;
        var g = m.Groups.First(x => x.Key == key);
        var d = _game.Data.Defines;
        Pay(m.Country, MeetCost);
        g.Strength = Math.Max(5, g.Strength - d.Get("meet_group_strength", 30));
        g.Satisfaction = Math.Min(1, g.Satisfaction + 0.25);
        m.Grievance = Math.Max(0, m.Grievance - d.Get("meet_group_grievance", 8));
        m.Concessions += 0.5;
        ApplyMovementReaction(m.Country, false, 0.5);
        _modsDirty.Add(m.Country);
        if (m.Country.IsPlayer) _game.Notify($"We met part of the {GroupName(key).ToLowerInvariant()} demand of the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement.", NotificationKind.Info);
        return null;
    }

    public string DemandText(Movement m)
    {
        var i = Ideology(m.Ideology);
        return m.Demand == MovementDemand.Legalize && m.TargetLaw != null && Laws.TryGetValue(m.TargetLaw, out var l)
            ? $"Legalise the {PartyName(m.Country, m.Ideology)}: enact {l.Name}"
            : $"Put the {PartyName(m.Country, m.Ideology)} ({i?.Name}) in power";
    }

    public Movement? MovementOf(Country c, string ideology) => Movements.FirstOrDefault(x => x.Country == c && x.Ideology == ideology);

    /// <summary>Why a movement's grievance is where it is: causes with their weight (for tooltips).</summary>
    public List<(string Cause, double Value)> MovementCauses(Movement m) => Rules.MovementGrievanceSources(_game, m.Country, m);

    /// <summary>The stage a movement has reached, from what it can actually do (not from support).</summary>
    public string MovementStage(Movement m)
    {
        if (m.Progress < 15 && m.Organization < 25) return "Sympathisers";
        if (m.Progress < 30) return m.Organization >= 40 ? "Organising" : "Agitating";
        if (m.Progress < 70) return "Strikes and marches";
        return Rules.CanRevolt(_game, m.Country, m) ? "Ready to rise" : "Mobilised but blocked";
    }

    /// <summary>What is likely to happen next, in plain words, and what is holding it back.</summary>
    public List<string> MovementForecast(Movement m)
    {
        var c = m.Country;
        var d = _game.Data.Defines;
        var lines = new List<string>();
        double orgNeed = d.Get("movement_revolt_organization", 55), grNeed = d.Get("movement_revolt_grievance", 40), opening = Rules.MovementOpening(_game, c);
        if (m.Organization < orgNeed) lines.Add($"Too loosely organised to rise ({m.Organization:0} of {orgNeed:0})");
        if (m.Grievance < grNeed) lines.Add($"Its supporters are not angry enough yet ({m.Grievance:0} of {grNeed:0})");
        if (opening < d.Get("movement_revolt_opening", 0.2)) lines.Add("The regime is too strong for it: no opening");
        if (lines.Count == 0) lines.Add(m.Progress >= 100 ? "It has everything it needs: a revolt can come any month" : "It is organised, angry and the regime is weak: it will rise once mobilised");
        foreach (var g in m.Groups.Where(g => g.Satisfaction < 0.4 && g.Strength >= 25).OrderByDescending(g => g.Strength * (1 - g.Satisfaction)))
            lines.Add($"{GroupName(g.Key)} is denied ({g.Satisfaction:P0} met): it presses the movement toward violence");
        if (m.Heat > 5) lines.Add($"Recent riots and mutinies cost {m.Heat / 100 * _game.Data.Defines.Get("movement_heat_stability", 3):0.0} stability a month");
        double strikes = Rules.MovementDisruption(_game, c, m);
        if (strikes < -0.05) lines.Add($"Strikes cost {-strikes:0.0} stability a month");
        var summary = _game.Population.Enabled ? PopSummaryOf(c) : null;
        if (summary != null && summary.Stronghold.TryGetValue(m.Ideology, out var sh) && sh.Share > 0)
            lines.Add(sh.Share >= 0.45 ? $"Concentrated in {StateName(sh.State)} ({sh.Share:P0} of its backers): a civil war has a base" : $"Scattered across the country (largest share {sh.Share:P0}): a civil war has no base");
        return lines;
    }

    private string StateName(string key) => _game.World.GetState(key)?.DisplayName ?? key;

    private void MonthlyUnrest()
    {
        var d = _game.Data.Defines;
        foreach (var c in _game.World.Countries.ToList()) // civil wars add countries
        {
            if (!c.Exists || CivilWars.Any(cw => cw.Origin == c || cw.Rebel == c)) continue;
            var p = Of(c);
            bool calm = _game.World.Date.TotalDays < p.CalmUntilDay;
            foreach (var i in Ideologies)
            {
                double pop = Rules.MovementSupport(_game, c, i);
                var m = MovementOf(c, i.Key);
                var demand = DemandOf(c, p, i.Key);
                if (m == null)
                {
                    if (demand == null) continue;
                    // a new movement starts from the grievance its backers already have
                    double start = _game.Population.Enabled && PopSummaryOf(c).BackerGrievance.TryGetValue(i.Key, out var bg) ? bg : 0;
                    if (!Rules.MovementForms(_game, c, i, pop, start)) continue;
                    m = new Movement { Country = c, Ideology = i.Key, Demand = demand.Value.Demand, TargetLaw = demand.Value.Law, Started = _game.World.Date, Grievance = start };
                    m.Organization = Math.Min(20, pop * 40);
                    SpawnGroups(m, i);
                    Movements.Add(m);
                    if (c.IsPlayer) _game.Notify($"A {i.Adjective.ToLowerInvariant()} movement has formed: {DemandText(m)}.", NotificationKind.Bad);
                    _game.Events.FireOnAction("on_movement_formed", c);
                }
                else if (demand == null) { Movements.Remove(m); continue; } // its demand was met
                else { m.Demand = demand.Value.Demand; m.TargetLaw = demand.Value.Law; }

                // grievance and organisation move slowly (they persist), memories of crackdowns and concessions fade
                double gTarget = Math.Clamp(Rules.MovementGrievanceSources(_game, c, m).Sum(x => x.Value), 0, 100);
                m.Grievance = Math.Clamp(m.Grievance + (gTarget - m.Grievance) * d.Get("movement_grievance_rate", 0.1), 0, 100);
                double oTarget = Rules.MovementOrganizationTarget(_game, c, m, pop);
                m.Organization = Math.Clamp(m.Organization + (oTarget - m.Organization) * d.Get("movement_organization_rate", 0.08), 0, 100);
                if (m.Groups.Count == 0) SpawnGroups(m, i); // (a movement from an older save gets its groups now)
                UpdateGroups(c, m, i);
                m.Crackdowns *= 1 - d.Get("movement_memory_decay", 0.03);
                m.Concessions *= 1 - d.Get("movement_memory_decay", 0.03);
                m.Progress = Math.Clamp(m.Progress + Rules.MovementProgress(_game, c, m, pop), 0, 100);
                _modsDirty.Add(c);
                if (m.Progress >= 70 && !m.Warned && c.IsPlayer && m.Demand == MovementDemand.Power)
                {
                    m.Warned = true;
                    c.Vars.SetFlag("ultimatum:" + m.Ideology);
                    _game.Events.FireOnAction("on_movement_ultimatum", c);
                    c.Vars.ClearFlag("ultimatum:" + m.Ideology);
                }
                // a movement that has lost its support, its organisation and its anger fades away
                if (m.Progress <= 0.5 && m.Organization < 8 && pop < d.Get("movement_threshold", 0.25) * 0.6) { Movements.Remove(m); continue; }
                if (m.Progress >= 100 && !calm)
                {
                    if (Rules.CanRevolt(_game, c, m))
                    {
                        Movements.Remove(m);
                        Revolt(c, i.Key, pop, "revolution");
                        break; // one revolt per country per month
                    }
                    if (m.Stalled++ == 0 && c.IsPlayer) _game.Notify($"The {i.Adjective.ToLowerInvariant()} movement has mobilised but cannot yet rise: {MovementForecast(m)[0].ToLowerInvariant()}.", NotificationKind.Info);
                }
                else m.Stalled = 0;
            }
            if (!c.Exists || CivilWars.Any(cw => cw.Origin == c) || calm || _game.World.Date.TotalDays < p.CalmUntilDay) continue;
            // a popular shut-out ideology may seize power in a coup, or (HOI4 style) start a civil war
            foreach (var i in Ideologies.OrderByDescending(i => p.Popularity.GetValueOrDefault(i.Key)))
            {
                if (InGovernment(c, i.Key)) continue;
                double pop = Rules.MovementSupport(_game, c, i);
                // it can simply win the next election - unless the democracy is falling apart (rule CanSeizePower)
                if (DemandOf(c, p, i.Key) == null && !Rules.CanSeizePower(_game, c, i)) continue;
                // (the civil war first: in a poor, unstable or war-weary country it needs less support than a coup)
                double chance = Rules.CivilWarChance(_game, c, i, pop);
                if (chance > 0 && _game.Rngs.Politics.NextDouble() < chance) { Revolt(c, i.Key, pop, "civil war"); break; }
                double coup = Rules.CoupChance(_game, c, i, pop);
                if (coup > 0 && _game.Rngs.Politics.NextDouble() < coup) { Coup(c, i.Key); break; }
            }
        }
    }

    /// <summary>Gives the movement what it wants (law change or new government) and dissolves it.</summary>
    public void GiveIn(Movement m)
    {
        Movements.Remove(m);
        var c = m.Country;
        if (m.Demand == MovementDemand.Legalize && m.TargetLaw != null && Laws.TryGetValue(m.TargetLaw, out var law))
            Enact(c, law, free: true);
        else SetRulingParty(c, m.Ideology, "coup");
        if (c.IsPlayer) _game.Notify($"We gave in to the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement.", NotificationKind.Info);
    }

    public double SuppressCost => _game.Data.Defines.Get("suppress_movement_cost", 50);
    public double ConcedeCost => _game.Data.Defines.Get("concede_movement_cost", 35);

    public string? CannotSuppress(Movement m) => CanAfford(m.Country, SuppressCost) ? null : $"Needs {SuppressCost:0} {PowerName}";

    public string? CannotConcede(Movement m)
    {
        if (m.Demand == MovementDemand.Power && m.TargetLaw == null && m.Concessions >= 3) return "It has been conceded to enough: it wants power, not more favours";
        return CanAfford(m.Country, ConcedeCost) ? null : $"Needs {ConcedeCost:0} {PowerName}";
    }

    /// <summary>What a crackdown would cost and how likely it is to work, for the button tooltip.</summary>
    public List<string> ExplainSuppress(Movement m)
    {
        var (success, cost) = Rules.CrackdownOdds(_game, m.Country, m);
        var lines = new List<string> { $"Costs {SuppressCost:0} {PowerName} and about {cost:0.0} stability", $"Chance it breaks their organisation: {success:P0}" };
        lines.Add("Success: mobilisation and organisation fall; a failure emboldens them");
        lines.Add("Either way grievance is remembered and martyrs gain it support");
        lines.AddRange(FactionReaction(m.Country, true).Take(3));
        return lines;
    }

    public List<string> ExplainConcede(Movement m)
    {
        var lines = new List<string> { $"Costs {ConcedeCost:0} {PowerName}", "Eases grievance and mobilisation now", "It grows stronger on the back of a win, and the ruling parties' hardliners resent it" };
        lines.AddRange(FactionReaction(m.Country, false).Take(3));
        return lines;
    }

    /// <summary>Governing factions that would applaud or resent a crackdown (hardline stance > 0.5) or a concession.</summary>
    private List<string> FactionReaction(Country c, bool crackdown)
    {
        var list = new List<string>();
        foreach (var f in GoverningFactions(c).OrderByDescending(FactionInfluence))
        {
            double stance = (FactionHardline(f) - 0.5) * 2 * (crackdown ? 1 : -1);
            if (Math.Abs(stance) < 0.25) continue;
            list.Add($"{f.DisplayName} {(stance > 0 ? "approves" : "objects")}");
        }
        return list;
    }

    private void ApplyMovementReaction(Country c, bool crackdown, double scale)
    {
        double shock = Def("faction_movement_grievance", 12) * scale;
        foreach (var f in GoverningFactions(c))
        {
            double stance = (FactionHardline(f) - 0.5) * 2 * (crackdown ? 1 : -1);
            if (Math.Abs(stance) > 0.05) AddGrievance(f, -stance * shock);
        }
        _modsDirty.Add(c);
    }

    private void AddStability(Country c, double delta)
    {
        var stab = _game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
        if (stab != null) _game.Resources.Set(c, stab, _game.Resources.Get(c, stab) + delta);
    }

    /// <summary>
    /// A crackdown. It can fail: the odds come from the regime's strength against the movement's organisation. Success breaks cells and demobilises
    /// it; failure emboldens it. Either way grievance and the memory of repression build, martyrs add support, hardline factions in the
    /// government approve and moderate ones resent it, and a state that holds free elections pays more stability.
    /// </summary>
    public string? Suppress(Movement m)
    {
        var reason = CannotSuppress(m);
        if (reason != null) return reason;
        var c = m.Country;
        var d = _game.Data.Defines;
        var (success, cost) = Rules.CrackdownOdds(_game, c, m);
        Pay(c, SuppressCost);
        AddStability(c, -cost);
        bool worked = _game.Rngs.Politics.NextDouble() < success;
        if (worked)
        {
            m.Progress = Math.Max(0, m.Progress - d.Get("suppress_movement_progress", 25));
            m.Organization = Math.Max(0, m.Organization - d.Get("suppress_movement_organization", 15));
        }
        else m.Progress = Math.Min(100, m.Progress + d.Get("suppress_failure_progress", 6));
        m.Crackdowns += worked ? 1 : 1.5;
        m.Grievance = Math.Min(100, m.Grievance + d.Get("suppress_movement_grievance", 8));
        AddPopularity(c, m.Ideology, 0.005 + m.Grievance / 100 * 0.01);
        ApplyMovementReaction(c, true, 1);
        if (c.IsPlayer) _game.Notify(worked ? $"The crackdown on the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement broke up its cells, but it will be remembered." : $"The crackdown on the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement failed and made martyrs.", worked ? NotificationKind.Info : NotificationKind.Bad);
        return null;
    }

    /// <summary>
    /// A partial concession (a reform, talks, an amnesty) that does not hand over power: it eases grievance and mobilisation now, but the
    /// movement is emboldened (organisation and a little support), the memory of the concession is kept and hardliners resent it.
    /// </summary>
    public string? Concede(Movement m)
    {
        var reason = CannotConcede(m);
        if (reason != null) return reason;
        var c = m.Country;
        var d = _game.Data.Defines;
        Pay(c, ConcedeCost);
        m.Grievance = Math.Max(0, m.Grievance - d.Get("concede_movement_grievance", 20));
        m.Progress = Math.Max(0, m.Progress - d.Get("concede_movement_progress", 20));
        m.Organization = Math.Min(100, m.Organization + d.Get("concede_movement_organization", 5));
        m.Concessions += 1;
        AddPopularity(c, m.Ideology, 0.005);
        ApplyMovementReaction(c, false, 1);
        if (c.IsPlayer) _game.Notify($"Concessions eased the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement for now.", NotificationKind.Info);
        return null;
    }

    private void AiMovements(Country c)
    {
        foreach (var m in MovementsOf(c).ToList())
        {
            if (m.Progress < 40 && m.Grievance < 60) continue;
            // give in to legalisation; a government that could work with the movement may hand it power, the rest fight it
            if (m.Demand == MovementDemand.Legalize && _game.Rngs.Politics.NextDouble() < 0.5) GiveIn(m);
            else if (_game.Rngs.Politics.NextDouble() < Rules.AiGivesIn(_game, c, m, PopularityOf(c, m.Ideology))) GiveIn(m);
            else if (m.Progress >= 40 && m.Grievance >= 40 && m.Crackdowns < 3 && CannotConcede(m) == null && _game.Rngs.Politics.NextDouble() < 0.25) Concede(m);
            else if (m.Progress >= 60 && CannotSuppress(m) == null) Suppress(m);
        }
    }

    // =============================================================================================
    // Civil wars
    // =============================================================================================

    public bool InCivilWar(Country c) => CivilWars.Any(cw => cw.Origin == c || cw.Rebel == c);

    /// <summary>A revolt: a civil war if the game has armies and wars, otherwise a coup.</summary>
    private void Revolt(Country c, string ideology, double popularity, string kind)
    {
        bool war = _game.Features.Has(FeatureSet.Military) && _game.Features.Has(FeatureSet.Diplomacy) && c.OwnedStates.Count >= 2
                   && Rules.RevoltIsCivilWar(_game, c, ideology, popularity);
        if (war && StartCivilWar(c, ideology, Rules.CivilWarShare(_game, c, ideology, popularity)) != null) return;
        Coup(c, ideology);
    }

    /// <summary>The ideology takes power without a war; its supporters are heartened and things calm down for a while.</summary>
    public void Coup(Country c, string ideology)
    {
        Movements.RemoveAll(m => m.Country == c && m.Ideology == ideology);
        SetRulingParty(c, ideology, "coup");
        AddPopularity(c, ideology, 0.05);
        Calm(c);
        Revolutions++;
        Coups++;
        CountRevolt(c);
    }

    private readonly Dictionary<Country, int> _revolts = new();

    /// <summary>Coups and civil wars a country has had in this game (for the Ledger; saved).</summary>
    public int RevoltsOf(Country c) => _revolts.GetValueOrDefault(c);

    private void CountRevolt(Country c) => _revolts[c] = _revolts.GetValueOrDefault(c) + 1;

    private void Calm(Country c) => Of(c).CalmUntilDay = _game.World.Date.TotalDays + (int)(Rules.CalmYears(_game, c) * 365);

    /// <summary>
    /// Splits <paramref name="c"/>: a share of its home states and divisions forms a new country ruled by
    /// <paramref name="ideology"/>, at war with it. If the rebels win they take over the country (it keeps its tag
    /// and gets their government); if they lose they are annexed. Returns the rebel country or null.
    /// </summary>
    public Country? StartCivilWar(Country c, string ideology, double share)
    {
        var ide = Ideology(ideology);
        if (ide == null || !c.Exists || InCivilWar(c) || c.Capital == null) return null;
        var w = _game.World;
        var p = Of(c);
        // the home block: states connected to the capital by land
        var capitalState = c.Capital.State;
        var home = HomeStates(c);
        if (home.Count < 2 || capitalState == null) return null;
        int count = Math.Clamp((int)Math.Round(home.Count * Math.Clamp(share, 0.05, 0.95)), 1, home.Count - 1);
        // grow the rebel area from the state farthest from the capital
        var (cx, cy) = Center(capitalState);
        var seed = home.Where(s => s != capitalState).MaxBy(s => { var (x, y) = Center(s); return (x - cx) * (x - cx) + (y - cy) * (y - cy); })!;
        var rebelStates = new List<State> { seed };
        var taken = new HashSet<State> { seed };
        var frontier = new Queue<State>();
        frontier.Enqueue(seed);
        while (rebelStates.Count < count && frontier.Count > 0)
        {
            var s = frontier.Dequeue();
            foreach (var n in StateNeighbours(s).OrderBy(x => x.Key))
            {
                if (rebelStates.Count >= count) break;
                if (n == capitalState || n.Owner != c || !home.Contains(n) || !taken.Add(n)) continue;
                rebelStates.Add(n);
                frontier.Enqueue(n);
            }
        }

        // the new country
        string tag = FreeTag();
        var col = Mix(c.Color, ide.Color, 0.45);
        var rebel = w.CreateCountry(tag, _game.Population.Enabled ? _game.Population.NationName(_game.Population.MajorityCulture(rebelStates.MaxBy(_game.Population.TotalOf)!), ideology, rebelStates) : $"{ide.Adjective} {c.Name}", col, $"gfx/flags/{tag}.png", ide.Government ?? c.Government);
        rebel.Origin = c.Tag;
        rebel.Names.Male.AddRange(c.Names.Male);
        rebel.Names.Female.AddRange(c.Names.Female);
        rebel.Names.Family.AddRange(c.Names.Family);
        rebel.Playable = false;
        var rp = new CountryPolitics { TotalSeats = p.TotalSeats };
        foreach (var kv in p.Popularity) rp.Popularity[kv.Key] = kv.Value;
        foreach (var kv in p.PartyNames) rp.PartyNames[kv.Key] = kv.Value;
        foreach (var kv in p.Laws) rp.Laws[kv.Key] = kv.Value;
        // the rebels do not ban themselves
        foreach (var grp in LawGroups)
            if (Laws.TryGetValue(rp.Laws.GetValueOrDefault(grp.Key) ?? "", out var l) && l.Bans.Contains(ideology))
                rp.Laws[grp.Key] = grp.Laws.FirstOrDefault(x => !x.Bans.Contains(ideology))?.Key ?? grp.Default;
        _state[rebel] = rp;
        rp.Ruling = ideology;
        rp.Government = new List<string> { ideology };
        rp.Seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [ideology] = rp.TotalSeats };
        InitializePartyPopularity(rebel, rp);
        rp.RulingOrganization = PartiesOfIdeology(rebel, ideology)
            .OrderByDescending(party => rp.PartyPopularity.GetValueOrDefault(party.Key)).Select(party => party.Key).FirstOrDefault() ?? ideology;
        rp.GovernmentOrganizations = new List<string> { rp.RulingOrganization };
        rp.PartySeats = DistributeLegacySeats(rebel, rp, rp.Seats);
        rp.Seats = AggregatePartySeats(rebel, rp.PartySeats);
        _modsDirty.Add(rebel);
        AddPopularity(rebel, ideology, Math.Max(0, 0.65 - rp.Popularity.GetValueOrDefault(ideology)));
        // the supporters who left take part of the ideology's popularity with them
        AddPopularity(c, ideology, -p.Popularity.GetValueOrDefault(ideology) * 0.5);
        Movements.RemoveAll(m => m.Country == c && m.Ideology == ideology);

        // land
        foreach (var s in rebelStates) _game.Diplomacy.TransferState(s, rebel);
        var capState = rebelStates.MaxBy(s => s.Tax + s.Locations.Count * 0.01)!;
        rebel.Capital = capState.Locations.Where(l => l.IsLand && l.IsPassable).OrderBy(l => l.Key).FirstOrDefault() ?? rebel.Capital;
        // treasury, manpower and equipment are split
        rebel.Gold = c.Gold * share; c.Gold -= rebel.Gold;
        rebel.Manpower = c.Manpower * share; c.Manpower -= rebel.Manpower;
        foreach (var k in c.Stockpile.Keys.ToList())
        {
            double part = Math.Floor(c.Stockpile[k] * share);
            c.Stockpile[k] -= part;
            rebel.Stockpile[k] = part;
        }
        // divisions in rebel land defect; more join until the rebels have their share
        if (_game.Features.Has(FeatureSet.Military))
        {
            var rebelLand = rebelStates.SelectMany(s => s.Locations).ToHashSet();
            var mine = w.Divisions.Where(d => d.Owner == c && d.VolunteerFrom == null).ToList();
            int want = (int)Math.Round(mine.Count * share);
            int defected = 0;
            // divisions in rebel land defect first; loyal ones left there fall back to the capital
            foreach (var d in mine.Where(d => rebelLand.Contains(d.Location)).OrderBy(d => d.Id).ToList())
            {
                if (defected < want) { Defect(d, rebel, false); defected++; }
                else if (d.Battle == null && c.Capital != null) { _game.Warfare.StopDivision(d); _game.Warfare.Teleport(d, c.Capital); }
            }
            // then others, spread over the army, until the rebels have their share
            var rest = mine.Where(d => d.Owner == c && !d.InBattle).OrderBy(d => d.Id).ToList();
            for (int k = 0; defected < want && k < rest.Count; k++)
            {
                if (rest.Count - k > want - defected && _game.Rngs.Politics.NextDouble() < 0.5) continue;
                Defect(rest[k], rebel, true);
                defected++;
            }
        }

        // war
        var war = _game.Diplomacy.DeclareWar(rebel, c, null, null, force: true);
        if (war == null) { Log.Warn($"Civil war in {c.Tag}: the war could not start"); return rebel; }
        war.Name = $"{c.Name} Civil War";
        war.Vars.SetFlag("civil_war");
        CivilWars.Add(new CivilWar { War = war, Origin = c, Rebel = rebel, Ideology = ideology });
        CivilWarsStarted++;
        CountRevolt(c);
        _game.Notify($"Civil war in {c.Name}! The {PartyName(c, ideology)} rose up and formed {rebel.Name}.", c.IsPlayer ? NotificationKind.War : NotificationKind.Info);
        _game.Events.FireOnAction("on_civil_war_started", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["rebel"] = rebel });
        _game.RaiseMapChanged();
        return rebel;
    }

    private void Defect(Division d, Country to, bool teleport)
    {
        var wf = _game.Warfare;
        wf.StopDivision(d);
        wf.AssignToArmy(d, null);
        d.FrontPost = null;
        d.Owner = to;
        if (teleport && to.Capital != null && d.Battle == null) wf.Teleport(d, to.Capital);
    }

    /// <summary>Ends civil wars when one side is beaten (it lost its capital and most of its land, or all of it).</summary>
    private void CheckCivilWars()
    {
        foreach (var cw in CivilWars.ToList())
        {
            if (!_game.World.Wars.Contains(cw.War)) { CivilWars.Remove(cw); continue; }
            bool originBeaten = Beaten(cw.Origin), rebelBeaten = Beaten(cw.Rebel);
            if (Rules.CivilWarVerdict(_game, cw, originBeaten, rebelBeaten) is { } originWon) EndCivilWar(cw, originWon);
        }
    }

    private bool Beaten(Country c)
    {
        if (!c.Exists) return true;
        var land = c.OwnedStates.SelectMany(s => s.Locations).Where(l => l.IsLand).ToList();
        if (land.Count == 0) return true;
        double held = land.Count(l => l.Controller == c) / (double)land.Count;
        bool capitalLost = c.Capital == null || c.Capital.Controller != c;
        return held <= 0.05 || (capitalLost && held <= _game.Data.Defines.Get("civil_war_defeat_share", 0.3));
    }

    private void EndCivilWar(CivilWar cw, bool originWon)
    {
        CivilWars.Remove(cw);
        var origin = cw.Origin;
        var rebel = cw.Rebel;
        _game.Diplomacy.EndWar(cw.War);
        if (originWon)
        {
            _game.Diplomacy.Annex(origin, rebel);
            // the rebels' cause is discredited for a while
            var op0 = Of(origin);
            AddPopularity(origin, cw.Ideology, -op0.Popularity.GetValueOrDefault(cw.Ideology) * 0.5);
            AddPopularity(origin, op0.Ruling, 0.1);
            _game.Notify($"The {origin.Name} Civil War is over: {origin.Name} crushed the {rebel.Name} rebels.", origin.IsPlayer ? NotificationKind.Good : NotificationKind.Info);
        }
        else
        {
            // the revolution won: the country keeps its name and tag but gets the rebels' government, laws and army
            var w = _game.World;
            foreach (var d in w.Divisions.Where(d => d.Owner == origin && d.VolunteerFrom == null).ToList()) _game.Warfare.DestroyDivision(d);
            foreach (var d in w.Divisions.Where(d => d.Owner == rebel).ToList()) Defect(d, origin, false);
            origin.Gold += rebel.Gold;
            origin.Manpower += rebel.Manpower;
            foreach (var kv in rebel.Stockpile) origin.Stockpile[kv.Key] = origin.Stockpile.GetValueOrDefault(kv.Key) + kv.Value;
            var capital = rebel.Capital;
            _game.Diplomacy.Annex(origin, rebel);
            var op = Of(origin);
            var rp = Of(rebel);
            op.Popularity.Clear();
            foreach (var kv in rp.Popularity) op.Popularity[kv.Key] = kv.Value;
            foreach (var kv in rp.Laws) op.Laws[kv.Key] = kv.Value;
            op.Ministers.Clear();
            // Rebuild party-level state from the victorious ideology mix while preserving the parliament's size.
            InitializePartyPopularity(origin, op);
            op.Seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [cw.Ideology] = op.TotalSeats };
            op.PartySeats = DistributeLegacySeats(origin, op, op.Seats);
            SetRulingParty(origin, cw.Ideology, "revolution");
            op.Seats = AggregatePartySeats(origin, op.PartySeats);
            if (capital != null && capital.Controller == origin && (origin.Capital == null || origin.Capital.Controller != origin)) origin.Capital = capital;
            Revolutions++;
            _game.Notify($"The {origin.Name} Civil War is over: the revolution won and the {PartyName(origin, cw.Ideology)} rules {origin.Name}.", origin.IsPlayer ? NotificationKind.War : NotificationKind.Info);
        }
        Movements.RemoveAll(m => m.Country == rebel);
        Calm(origin);
        _game.Events.FireOnAction("on_civil_war_ended", origin);
        _game.RaiseMapChanged();
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>The owner's states connected by land to its capital state.</summary>
    private HashSet<State> HomeStates(Country c)
    {
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start == null || start.Owner != c) return result;
        var q = new Queue<State>();
        result.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
            foreach (var n in StateNeighbours(q.Dequeue()))
                if (n.Owner == c && result.Add(n)) q.Enqueue(n);
        return result;
    }

    private static IEnumerable<State> StateNeighbours(State s)
    {
        var seen = new HashSet<State>();
        foreach (var l in s.Locations)
            foreach (var a in l.Neighbors)
                if (a.Kind != AdjacencyKind.Sea && a.To.State is { } o && o != s && seen.Add(o)) yield return o;
    }

    private static (double X, double Y) Center(State s)
    {
        double x = 0, y = 0;
        int n = 0;
        foreach (var l in s.Locations) { x += l.CenterX; y += l.CenterY; n++; }
        return n == 0 ? (0, 0) : (x / n, y / n);
    }

    private string FreeTag()
    {
        for (char a = 'R'; a <= 'Z'; a++)
            for (int i = 0; i < 100; i++)
            {
                string t = $"{a}{i:00}";
                if (_game.World.GetCountry(t) == null) return t;
            }
        for (int i = 0; i < 10000; i++) { string t = $"X{i % 36:X1}{i / 36 % 36:X1}"; if (_game.World.GetCountry(t) == null) return t; }
        return "XXX";
    }

    private static Rgb Mix(Rgb a, Rgb b, double t) => a.Lerp(b, t);
}
