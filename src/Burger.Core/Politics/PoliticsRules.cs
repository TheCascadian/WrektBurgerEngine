using Burger.Core.Model;
using Burger.Core.Population;
using Burger.Core.Modding;

namespace Burger.Core.Politics;

/// <summary>Overridable rules for politics: how popularity drifts, who forms coalitions, how movements grow and when civil wars break out. Subclass and assign to <c>Game.Rules.Politics</c> (the politics feature puts a copy in backend/Politics.cs).</summary>
public class PoliticsRules
{
    /// <summary>
    /// Monthly change of each ideology's popularity, in percentage points (they are normalised to 100% afterwards).
    /// Sources: modifiers named <c>IDEOLOGY_drift</c> (laws, ministers, events), the ruling party's propaganda,
    /// lost wars, war exhaustion, lost core land, low stability, ideologies ruling the neighbours, political crises and a
    /// little chance. No ideology takes a country over for good: a dominant one loses support and small ones keep a following.
    /// </summary>
    public virtual Dictionary<string, double> MonthlyDrift(Game game, Country c)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        var d = game.Data.Defines;
        var drift = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in pol.Ideologies) drift[i.Key] = game.Modifiers.Sum(c, i.Key + "_drift");
        void Add(string ideology, double v) { if (drift.ContainsKey(ideology)) drift[ideology] += v; }

        // propaganda: a ruling party that holds no elections keeps itself popular (plus modifiers named ruling_party_drift)
        if (!pol.HoldsElections(c)) Add(p.Ruling, d.Get("ruling_party_drift", 0.4));
        Add(p.Ruling, game.Modifiers.Sum(c, "ruling_party_drift"));
        // the ruling party is blamed for lost wars; extremists profit
        foreach (var war in game.World.WarsOf(c))
            if (war.ScoreFor(c) < -20)
            {
                Add(p.Ruling, -d.Get("war_loss_drift", 1.0));
                foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling)) Add(i.Key, d.Get("war_loss_drift", 1.0) / 2);
            }
        // unrest: low stability helps extremists, high stability helps the government
        double stability = StabilityOf(game, c);
        if (stability < 30)
        {
            Add(p.Ruling, -0.4);
            foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling)) Add(i.Key, 0.4);
        }
        else if (stability > 70) Add(p.Ruling, 0.2);
        // weak democracies: where people vote but stability is low (under weak_democracy_stability, 40) or the parties in
        // government have little support (under weak_democracy_support, 35%), they look for a strong hand: ideologies
        // with weak_democracy_drift (fascists) gain, the more the weaker the democracy is
        if (pol.HoldsElections(c))
        {
            // (support only counts as weak where the government is not the clear plurality: a divided opposition does not unseat it)
            double gov = GovernmentSupport(game, c);
            double rival = p.PartyPopularity.Where(kv => !p.GovernmentOrganizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                .Select(kv => kv.Value).DefaultIfEmpty(0).Max();
            double plurality = Math.Clamp((gov - rival * 1.25) / 0.15, 0, 1);
            double weak = Math.Max(Math.Clamp((d.Get("weak_democracy_stability", 40) - stability) / 30, 0, 1),
                                   Math.Clamp((d.Get("weak_democracy_support", 0.35) - gov) / 0.2, 0, 1) * (1 - plurality));
            // (an old parliamentary tradition - the profile trait democratic_roots, 0-1 - resists the pull of the strong hand)
            if (game.AiProfiles.Enabled) weak *= 1 - Math.Clamp(game.AiProfiles.PersonalityOf(c).Trait("democratic_roots", 0), 0, 1);
            if (weak > 0)
                foreach (var i in pol.Ideologies.Where(i => i.WeakDemocracyDrift > 0 && !p.Government.Contains(i.Key))) Add(i.Key, i.WeakDemocracyDrift * weak);
        }
        // and the other way round: where nobody votes, a country wealthier than most wants a say (liberalisation_drift for
        // the ideologies that hold elections, growing with its wealth above the world's typical) - poor dictatorships last
        else
        {
            double want = d.Get("liberalisation_drift", 0.1) * Math.Clamp(game.Politics.WealthIndex(c) - 1, 0, 1.5);
            if (want > 0) foreach (var i in pol.Ideologies.Where(i => i.Elections && i.Key != p.Ruling)) Add(i.Key, want);
        }
        // a long war wears the government down, whoever rules
        if (game.Features.Has(FeatureSet.Diplomacy))
        {
            double tired = game.Diplomacy.WarExhaustion(c) * d.Get("war_exhaustion_drift", 0.05);
            if (tired > 0)
            {
                Add(p.Ruling, -tired);
                foreach (var i in pol.Ideologies.Where(i => i.Key != p.Ruling)) Add(i.Key, tired / Math.Max(1, pol.Ideologies.Count - 1));
            }
        }
        // revanchism: a country whose core land is held by others listens to extremists
        if (pol.Ideology(p.Ruling) is not { Extremist: true } && game.World.States.Any(s => s.Owner != null && s.Owner != c && s.Cores.Contains(c)))
            foreach (var i in pol.Ideologies.Where(i => i.WeakDemocracyDrift > 0 && i.Key != p.Ruling)) Add(i.Key, d.Get("revanchism_drift", 0.15) * 3); // lost land feeds the nationalists
        // ideas spread from neighbours (stronger neighbours spread more)
        {
            double own = Math.Max(1, game.Diplomacy.Strength(c));
            var fromNeighbours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in Burger.Core.Scripting.Builtins.NeighborCountries(game, c))
            {
                string r = pol.Of(n).Ruling;
                if (r == p.Ruling || r.Length == 0) continue;
                double power = Math.Max(1, game.Diplomacy.Strength(n));
                fromNeighbours[r] = fromNeighbours.GetValueOrDefault(r) + d.Get("neighbour_drift", 0.1) * Math.Min(2, power / own);
            }
            // (an idea spreads into a country where it is new; where four in ten already hold it, the neighbours add nothing)
            foreach (var kv in fromNeighbours)
                Add(kv.Key, Math.Min(0.6, kv.Value) * Math.Clamp(1 - p.Popularity.GetValueOrDefault(kv.Key) / d.Get("neighbour_drift_cap", 0.4), 0, 1));
        }
        // pluralism: no ideology takes a country over for good. The further one is above dominance_threshold (55%), the
        // faster it loses support (people tire of it, it splits); one below ideology_floor (5%) wins some back.
        {
            double top = d.Get("dominance_threshold", 0.55), decay = d.Get("dominance_decay", 0.04), low = d.Get("ideology_floor", 0.05);
            foreach (var i in pol.Ideologies)
            {
                double cur = p.Popularity.GetValueOrDefault(i.Key);
                if (cur > top) Add(i.Key, -(cur - top) * 100 * decay);
                else if (cur < low) Add(i.Key, (low - cur) * 100 * 0.1);
            }
        }
        // political crises: now and then (political_crisis_chance, about once in eight years) a scandal, a slump or a
        // strike wave turns people away from the government and towards one of the others (extremists when things are bad)
        if (pol.Ideologies.Count > 1 && game.Rngs.Politics.NextDouble() < d.Get("political_crisis_chance", 0.01))
        {
            var others = pol.Ideologies.Where(i => i.Key != p.Ruling).ToList();
            double Weight(IdeologyDef i) => 0.5 + p.Popularity.GetValueOrDefault(i.Key) + (i.Extremist && stability < 50 ? 1 : 0);
            double pick = game.Rngs.Politics.NextDouble() * others.Sum(Weight);
            var gainer = others[^1];
            foreach (var i in others) { pick -= Weight(i); if (pick <= 0) { gainer = i; break; } }
            double swing = d.Get("political_crisis_swing", 10);
            Add(p.Ruling, -swing / 2);
            Add(gainer.Key, swing);
            var stab = game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
            if (stab != null) game.Resources.Set(c, stab, game.Resources.Get(c, stab) - d.Get("political_crisis_stability", 10));
            game.NotifyPlayer($"A political crisis shakes {c.Name}: many turn to the {gainer.Name} cause.", NotificationKind.Info, c);
        }
        // a ruling party that holds no elections keeps a core of support (repression, patronage, the army)
        if (!pol.HoldsElections(c))
        {
            double floor = d.Get("ruling_party_floor", 0.15), cur = p.Popularity.GetValueOrDefault(p.Ruling);
            if (cur < floor) Add(p.Ruling, (floor - cur) * 100 * 0.15);
        }
        // chance
        foreach (var i in pol.Ideologies) Add(i.Key, (game.Rngs.Politics.NextDouble() - 0.5) * d.Get("random_drift", 0.4));
        // an old parliamentary tradition (profile trait democratic_roots) holds the extremists back, whatever feeds them: war, neighbours, lost land
        if (game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("democratic_roots", 0) is var roots && roots > 0)
            foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling))
                if (drift[i.Key] > 0) drift[i.Key] *= 1 - 0.85 * Math.Clamp(roots, 0, 1);
        return drift;
    }

    /// <summary>Stability of a country, 0-100 (the 'stability' resource; 50 if the game has none).</summary>
    public virtual double StabilityOf(Game game, Country c)
    {
        var def = game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
        return def == null ? 50 : game.Resources.Get(c, def);
    }

    /// <summary>
    /// Does a government that has lost the country fall into a snap election? When the most popular party is out of government with a clear
    /// lead (snap_election_lead, 8 points) and the last vote was over snap_election_months (12) ago: coalitions collapse, the president dissolves
    /// parliament, the people are asked again.
    /// </summary>
    public virtual bool CallsSnapElection(Game game, Country c)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        var d = game.Data.Defines;
        if (p.LastElection is { } last && game.World.Date.TotalDays - last.TotalDays < d.Get("snap_election_months", 12) * 30) return false;
        var top = p.PartyPopularity.OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(top.Key))
        {
            var ideologicalTop = p.Popularity.OrderByDescending(kv => kv.Value).First();
            if (p.Government.Contains(ideologicalTop.Key)) return false;
            return ideologicalTop.Value - p.Popularity.GetValueOrDefault(p.Ruling) >= d.Get("snap_election_lead", 0.08);
        }
        if (p.GovernmentOrganizations.Contains(top.Key, StringComparer.OrdinalIgnoreCase)) return false;
        return top.Value - p.PartyPopularity.GetValueOrDefault(p.RulingOrganization) >= d.Get("snap_election_lead", 0.08);
    }

    /// <summary>Support for organizations actually represented in government (0-1), with an ideology fallback for legacy saves.</summary>
    public virtual double GovernmentSupport(Game game, Country c)
    {
        var p = game.Politics.Of(c);
        // Named organizations can share an ideology while competing for votes. Count only organizations in the actual cabinet.
        if (p.GovernmentOrganizations.Count > 0)
            return Math.Clamp(p.GovernmentOrganizations.Sum(key => p.PartyPopularity.GetValueOrDefault(key)), 0, 1);
        return p.Government.Count > 0 ? p.Government.Sum(g => p.Popularity.GetValueOrDefault(g)) : p.Popularity.GetValueOrDefault(p.Ruling);
    }

    /// <summary>
    /// Wealth per person, compared between countries (<see cref="PoliticsSystem.WealthIndex"/>): monthly income per head
    /// with the economy, else the tax value of its states per head.
    /// </summary>
    public virtual double WealthPerPerson(Game game, Country c)
    {
        double people = game.Population.Enabled ? game.Population.TotalOf(c) : c.OwnedStates.Count;
        if (people <= 0) return 0;
        double wealth = game.Features.Has(FeatureSet.Economy) && c.LastIncome > 0 ? c.LastIncome : c.OwnedStates.Sum(s => s.Tax);
        return Math.Max(0, wealth) / people;
    }

    /// <summary>
    /// What moves a country's stability besides laws, ministers and buildings (points of stability_target, with the name
    /// shown in tooltips): poverty (poor countries are restless: poverty_stability, up to -25), prosperity, a government
    /// whose parties have under government_support_needed (40%) support, and core land held by others.
    /// </summary>
    public virtual List<(string Source, double Value)> StabilitySources(Game game, Country c)
    {
        var d = game.Data.Defines;
        var list = new List<(string, double)>();
        double wealth = game.Politics.WealthIndex(c);
        if (wealth < 1) list.Add(("Poverty", -(1 - wealth) * d.Get("poverty_stability", 25)));
        else list.Add(("Prosperity", Math.Min(d.Get("prosperity_stability", 5), (wealth - 1) * 5)));
        double need = d.Get("government_support_needed", 0.4), support = GovernmentSupport(game, c);
        if (support < need) list.Add(("Unpopular government", -(need - support) * 100 * d.Get("unpopular_government_stability", 0.6)));
        if (game.World.States.Any(s => s.Owner != null && s.Owner != c && s.Cores.Contains(c)))
            list.Add(("Core land in foreign hands", -d.Get("lost_cores_stability", 5)));
        double strikes = game.Politics.MovementsOf(c).Sum(m => MovementDisruption(game, c, m));
        if (strikes < -0.01) list.Add(("Strikes and protest", strikes));
        if (game.Population.Enabled && game.Politics.PopOpinionStability(c) is var popOpinion && Math.Abs(popOpinion) > 0.01)
            list.Add(("Popular opinion", popOpinion));
        return list;
    }

    /// <summary>
    /// How close a country is to breaking apart (0-1): low stability, worse in a poor country, or really high war
    /// exhaustion (above civil_war_exhaustion, 8 of 20). Makes civil wars likelier and coups rarer.
    /// </summary>
    public virtual double UnrestPressure(Game game, Country c)
    {
        double unstable = Math.Clamp((50 - StabilityOf(game, c)) / 50, 0, 1);
        double poor = Math.Clamp(1 - game.Politics.WealthIndex(c), 0, 1);
        double tired = game.Features.Has(FeatureSet.Diplomacy)
            ? Math.Clamp((game.Diplomacy.WarExhaustion(c) - game.Data.Defines.Get("civil_war_exhaustion", 8)) / 8, 0, 1) : 0;
        return Math.Clamp(Math.Max(unstable * (0.5 + poor), tired), 0, 1);
    }

    /// <summary>
    /// Can an ideology that could win elections still take power by force (coup or civil war)? By default extremists,
    /// in a democracy whose stability has fallen under democracy_coup_stability (25).
    /// </summary>
    public virtual bool CanSeizePower(Game game, Country c, IdeologyDef ideology)
        => ideology.Extremist && StabilityOf(game, c) < game.Data.Defines.Get("democracy_coup_stability", 25);

    /// <summary>
    /// Can this ideology take government by winning an election? By default yes; with the define extremists_need_assent (1) the extremist ones cannot:
    /// a fascist or communist party that wins the vote becomes a movement that demands power, and the ruling parties either give in (a coalition that
    /// "appoints" it), suppress it, or face a revolt - a coup or a civil war.
    /// </summary>
    public virtual bool NeedsAssent(Game game, Country c, IdeologyDef ideology)
        => game.Data.Defines.Get("extremists_need_assent", 0) > 0
           && (ideology.Extremist || game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("civil_war_prone", 0) > 0.5); // (in a country split into two camps nobody takes the government from the other by the vote)

    /// <summary>
    /// Monthly chance an AI government gives in to a movement that demands power. Parties that would govern with it (WillGovernWith: the conservatives
    /// and the fascists in Germany 1933, the king and the Blackshirts in Italy 1922) give in far more readily than the rest, which fight it; the more
    /// popular the movement against the government, and the shakier the country, the likelier the surrender.
    /// </summary>
    public virtual double AiGivesIn(Game game, Country c, Movement m, double popularity)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        var ide = pol.Ideology(m.Ideology);
        if (ide == null) return 0;
        bool kin = p.Government.Any(g => pol.Ideology(g) is { } gi && WillGovernWith(game, c, gi, ide));
        double govSupport = GovernmentSupport(game, c);
        double chance = kin ? 0.30 : 0.03;
        chance *= 0.5 + Math.Clamp((popularity - govSupport) * 3, 0, 1.5);
        chance *= StabilityOf(game, c) < 40 ? 1.5 : 1;
        chance *= 0.6 + 0.8 * Math.Clamp(m.Organization / 100, 0, 1); // (an organised movement cannot be waved away; a loose one can)
        if (p.Ruling == "democratic") chance *= game.Data.Defines.Get("democracy_resilience", 1); // (the parliamentary democracies of the west did not hand over power; the profile trait appease overrides it)
        if (game.AiProfiles.Enabled) chance *= game.AiProfiles.PersonalityOf(c).Trait("appease"); // (profile trait appease: 1 = as usual, near 0 = never gives in)
        return Math.Clamp(chance, 0, 0.8);
    }

    /// <summary>
    /// The support a shut-out ideology can call on for a movement or a revolt: its own popularity plus that of the parties out of government that
    /// would govern with it (the monarchists, the army and the Falange against the Republic). Only where the define movement_blocs is 1.
    /// </summary>
    public virtual double MovementSupport(Game game, Country c, IdeologyDef ideology)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        double v = p.Popularity.GetValueOrDefault(ideology.Key);
        // people whose preferred party is banned keep backing it in secret
        v = Math.Min(1, v + game.Politics.UndergroundSupport(c, ideology.Key));
        // (only in a country split into two camps - the profile trait civil_war_prone - do the right's parties rise together)
        if (game.Data.Defines.Get("movement_blocs", 0) <= 0 || !ideology.Extremist || !game.AiProfiles.Enabled || game.AiProfiles.PersonalityOf(c).Trait("civil_war_prone", 0) < 0.5) return v;
        foreach (var o in pol.Ideologies)
            if (o.Key != ideology.Key && !o.Extremist && !p.Government.Contains(o.Key) && o.Key != p.Ruling && WillGovernWith(game, c, o, ideology))
                v += p.Popularity.GetValueOrDefault(o.Key);
        return Math.Min(1, v);
    }

    /// <summary>Share of the vote of each ideology in an election (popularity with a little chance).</summary>
    public virtual Dictionary<string, double> Votes(Game game, Country c)
    {
        var p = game.Politics.Of(c);
        var votes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double noise = game.Data.Defines.Get("election_noise", 0.03);
        foreach (var kv in p.Popularity) votes[kv.Key] = Math.Max(0, kv.Value + (game.Rngs.Politics.NextDouble() * 2 - 1) * noise);
        double sum = votes.Values.Sum();
        if (sum > 0) foreach (var k in votes.Keys.ToList()) votes[k] /= sum;
        return votes;
    }

    /// <summary>
    /// Votes of minority parties (population features): people of an unaccepted culture vote for its national party
    /// (minority_party_turnout, 70%, of them) and people of a faith other than the state religion that make up at least
    /// religious_party_min_share (10%) vote for a religious party (religious_party_turnout, 40%). Only people outside
    /// colonies vote. Keys: culture:KEY and religion:KEY; values are shares of all votes.
    /// </summary>
    public virtual Dictionary<string, double> MinorityVotes(Game game, Country c)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var pop = game.Population;
        var d = game.Data.Defines;
        var voters = c.OwnedStates.Where(s => pop.HasPops(s) && !pop.IsColony(s)).ToList();
        double total = voters.Sum(pop.TotalOf);
        if (total <= 0) return result;
        double minShare = d.Get("minority_party_min_share", 0.03);
        foreach (var (culture, size) in pop.Breakdown(voters, p => p.Culture))
        {
            double share = size / total;
            if (share >= minShare && !pop.IsAccepted(c, culture)) result["culture:" + culture] = share * d.Get("minority_party_turnout", 0.7);
        }
        if (pop.ReligionEnabled && pop.Of(c).Religion is { } state)
            foreach (var (religion, size) in pop.Breakdown(voters, p => p.Religion))
            {
                double share = size / total;
                if (religion != state && share >= d.Get("religious_party_min_share", 0.1)) result["religion:" + religion] = share * d.Get("religious_party_turnout", 0.4);
            }
        return result;
    }

    /// <summary>True if the parties of these two ideologies govern together (both must accept each other).</summary>
    public virtual bool WillGovernWith(Game game, Country c, IdeologyDef a, IdeologyDef b)
        => a.Key == b.Key || (a.Coalition.Contains(b.Key) && b.Coalition.Contains(a.Key));

    /// <summary>
    /// Do this ideology's supporters organise into a movement? Support alone is not enough or necessary on its own: a large but contented
    /// following stays loose, a smaller, aggrieved or shut-out one forms cells earlier (movement_threshold 25% at neutral grievance,
    /// from about 15% when aggrieved to 35% when content).
    /// </summary>
    public virtual bool MovementForms(Game game, Country c, IdeologyDef ideology, double support, double grievance)
        => support >= game.Data.Defines.Get("movement_threshold", 0.25) * (1.4 - 0.8 * Math.Clamp(grievance / 100, 0, 1));

    /// <summary>
    /// Where a movement's grievance is heading (0-100), by cause: exclusion from power or a ban, the hardship of its backers (regional and
    /// class), national instability, the memory of crackdowns, less what concessions have won. Grievance moves slowly toward this, so it persists.
    /// </summary>
    public virtual List<(string Cause, double Value)> MovementGrievanceSources(Game game, Country c, Movement m)
    {
        var d = game.Data.Defines;
        var pol = game.Politics;
        var list = new List<(string, double)>();
        list.Add((m.Demand == MovementDemand.Legalize ? "Banned from politics" : "Shut out of power", m.Demand == MovementDemand.Legalize ? d.Get("grievance_banned", 30) : d.Get("grievance_excluded", 20)));
        if (game.Population.Enabled && pol.PopSummaryOf(c).BackerGrievance.TryGetValue(m.Ideology, out var backers) && backers > 0)
            list.Add(("Its supporters' own discontent", backers * d.Get("grievance_backers", 0.6)));
        double poor = Math.Clamp(1 - pol.WealthIndex(c), 0, 1);
        if (poor > 0.02) list.Add(("Poverty", poor * d.Get("grievance_poverty", 25)));
        double unstable = Math.Clamp((50 - StabilityOf(game, c)) / 50, 0, 1);
        if (unstable > 0.02) list.Add(("National instability", unstable * d.Get("grievance_instability", 20)));
        if (m.Crackdowns > 0.05) list.Add(("Memory of crackdowns", m.Crackdowns * d.Get("grievance_crackdown", 10)));
        if (m.Concessions > 0.05) list.Add(("Concessions won", -m.Concessions * d.Get("grievance_concession", 12)));
        double pressure = MovementPressure(game, c, m);
        if (pressure > 0.05) list.Add(("Interest groups' unmet demands", pressure * d.Get("grievance_interest", 15)));
        list.AddRange(SubjectGrievanceSources(game, c));
        return list;
    }

    /// <summary>
    /// How much of a movement's support is organised (0-100): it builds toward a level set by its support and its grievance (a banned movement goes
    /// underground and organises at a fraction of that, a free one organises openly), minus what crackdowns have broken up.
    /// </summary>
    public virtual double MovementOrganizationTarget(Game game, Country c, Movement m, double support)
    {
        var d = game.Data.Defines;
        double target = Math.Min(100, support * 100 * d.Get("organization_per_support", 1.8) * (0.4 + Math.Clamp(m.Grievance / 100, 0, 1)));
        if (m.Demand == MovementDemand.Legalize) target *= d.Get("underground_organization", 0.7);
        return Math.Max(0, target - m.Crackdowns * d.Get("organization_crackdown", 8));
    }

    /// <summary>
    /// The state's openings (0-1): how little the regime can resist. Instability and war weariness, a government nobody backs, and a country in
    /// the middle of a war open the way; a regime that holds the army and the police closes it (profile trait regime_hold).
    /// </summary>
    public virtual double MovementOpening(Game game, Country c)
    {
        var d = game.Data.Defines;
        double weak = UnrestPressure(game, c);
        double lonely = Math.Clamp((d.Get("government_support_needed", 0.4) + 0.1 - GovernmentSupport(game, c)) / 0.5, 0, 1);
        double hold = game.AiProfiles.Enabled && game.Politics.Ideology(game.Politics.Of(c).Ruling)?.Extremist == true ? game.AiProfiles.PersonalityOf(c).Trait("regime_hold", 0) : 0;
        // (a government nobody backs is still safe while it keeps order: its unpopularity only opens the way as stability slips)
        double order = Math.Clamp((80 - StabilityOf(game, c)) / 50, 0.15, 1);
        return Math.Clamp((0.55 * weak + 0.45 * lonely * order) * (1 - 0.8 * hold), 0, 1);
    }

    /// <summary>
    /// Monthly mobilisation change of a movement (points of 100; strikes, marches, drilling). It grows with organisation and grievance together, is
    /// slowed by a stable state, and when either is lacking it ebbs in proportion to what it has, not by a fixed amount.
    /// </summary>
    public virtual double MovementProgress(Game game, Country c, Movement m, double popularity)
    {
        var d = game.Data.Defines;
        double org = m.Organization / 100, grievance = m.Grievance / 100;
        double drive = org * grievance;
        double stability = StabilityOf(game, c);
        double growth = d.Get("movement_speed", 0.5) * drive * 30 * (1.5 - stability / 100.0) * (1 + MovementPressure(game, c, m) * d.Get("interest_progress_push", 0.8));
        double ebb = m.Progress * d.Get("movement_ebb", 0.06) * (1 - Math.Min(1, drive * 2.5)) + (drive < 0.05 ? 0.5 : 0);
        return growth - ebb;
    }

    /// <summary>
    /// Can a movement that has mobilised fully actually rise? It needs a real organisation (movement_revolt_organization, 55), grievance behind it
    /// (movement_revolt_grievance, 40) and an opening in the regime (movement_revolt_opening, 0.2). Without all three it stays in the streets as
    /// strikes and protest.
    /// </summary>
    public virtual bool CanRevolt(Game game, Country c, Movement m)
    {
        var d = game.Data.Defines;
        return m.Organization >= d.Get("movement_revolt_organization", 55) && m.Grievance >= d.Get("movement_revolt_grievance", 40)
               && MovementOpening(game, c) >= d.Get("movement_revolt_opening", 0.2);
    }

    /// <summary>
    /// Stability the movement's strikes and protests cost every month (0 or negative), growing with how mobilised and organised it is.
    /// Nothing below movement_strike_start (30) mobilisation.
    /// </summary>
    public virtual double MovementDisruption(Game game, Country c, Movement m)
    {
        var d = game.Data.Defines;
        double start = d.Get("movement_strike_start", 30);
        double heat = -m.Heat / 100 * d.Get("movement_heat_stability", 3);
        if (m.Progress <= start) return heat;
        return heat - (m.Progress - start) / (100 - start) * (0.3 + 0.7 * m.Organization / 100) * d.Get("movement_strike_stability", 5);
    }

    /// <summary>
    /// Who decides a subject's government. Imposed (the default of a subject type) forces the overlord's ideology on it every month; Preferred only
    /// favours it with support; Free leaves it alone. A subject in a civil war or with an organised, mobilised movement cannot be held in line.
    /// </summary>
    public virtual SubjectControl SubjectGovernment(Game game, Country subject, Country overlord, SubjectType type)
    {
        var policy = type.Government.ToLowerInvariant() switch { "free" => SubjectControl.Free, "preferred" => SubjectControl.Preferred, _ => SubjectControl.Imposed };
        if (policy != SubjectControl.Imposed) return policy;
        var d = game.Data.Defines;
        if (game.Politics.InCivilWar(subject)) return SubjectControl.Preferred;
        double start = d.Get("movement_strike_start", 30), org = d.Get("subject_revolt_organization", 50);
        return game.Politics.Movements.Any(m => m.Country == subject && m.Progress >= start && m.Organization >= org) ? SubjectControl.Preferred : policy;
    }

    /// <summary>How foreign rule feeds a movement's grievance: (cause, value) pairs, empty for a country that is nobody's subject.</summary>
    public virtual List<(string Cause, double Value)> SubjectGrievanceSources(Game game, Country c)
    {
        var list = new List<(string, double)>();
        if (!game.Features.Has(FeatureSet.Diplomacy) || game.World.Relations.SubjectTypeOf(c) is not { } type) return list;
        var d = game.Data.Defines;
        double liberty = game.Diplomacy.LibertyDesire(c);
        if (liberty > 5) list.Add(("Foreign rule", liberty / 100 * d.Get("grievance_foreign", 20)));
        if (type.Government.Equals("imposed", StringComparison.OrdinalIgnoreCase)) list.Add(("Puppet regime", d.Get("grievance_puppet", 8)));
        return list;
    }

    /// <summary>The interest groups that come out with a new movement (key, starting strength 0-100). They want freedom, security or a larger army, never laws.</summary>
    public virtual List<(string Key, double Strength)> MovementInterestGroups(Game game, Country c, IdeologyDef ideology)
    {
        var list = new List<(string, double)>();
        if (ideology.Extremist) { list.Add(("security", 60)); list.Add(("army", 45)); }
        else if (ideology.Elections) { list.Add(("freedom", 65)); list.Add(("security", 20)); }
        else { list.Add(("security", 45)); list.Add(("army", 30)); }
        if (game.Features.Has(FeatureSet.Diplomacy) && game.Diplomacy.WarExhaustion(c) > 4) list.Add(("freedom", 25));
        if (game.Features.Has(FeatureSet.Diplomacy) && game.Diplomacy.LibertyDesire(c) is var liberty && liberty >= 20) list.Add(("independence", liberty * 0.8));
        return list;
    }

    /// <summary>How far the state gives a group what it wants (0-1): freedom from open politics and no repression, security from order, army from the size of the forces.</summary>
    public virtual double GroupSatisfaction(Game game, Country c, Movement m, string group)
    {
        var d = game.Data.Defines;
        switch (group)
        {
            case "freedom":
                return Math.Clamp((game.Politics.HoldsElections(c) ? 0.55 : 0.1) + (m.Demand == MovementDemand.Legalize ? 0 : 0.25) - m.Crackdowns * 0.12 + m.Concessions * 0.08, 0, 1);
            case "security":
                return Math.Clamp(StabilityOf(game, c) / 90 - m.Heat / 150, 0, 1);
            case "army":
                double want = Math.Max(1, c.OwnedStates.Count * d.Get("interest_army_per_state", 0.6));
                return Math.Clamp(game.World.Divisions.Count(x => x.Owner == c) / want, 0, 1);
            case "independence":
                return Math.Clamp(1 - game.Diplomacy.LibertyDesire(c) / 100, 0, 1);
            default: return 0.5;
        }
    }

    /// <summary>How hard its unmet interest groups push a movement (0-1+): each group's strength times how much it is denied.</summary>
    public virtual double MovementPressure(Game game, Country c, Movement m)
        => m.Groups.Sum(g => g.Strength / 100 * (1 - g.Satisfaction));

    /// <summary>Monthly chance of a violent act (a riot, a bombing, a mutiny): it needs organisation and anger, and the groups' unmet demands drive it. This is what makes movements aggressive.</summary>
    public virtual double MovementIncidentChance(Game game, Country c, Movement m)
    {
        var d = game.Data.Defines;
        if (m.Progress < d.Get("movement_strike_start", 30) || m.Groups.Count == 0) return 0;
        double drive = m.Organization / 100 * m.Grievance / 100;
        return Math.Clamp(drive * (0.15 + MovementPressure(game, c, m)) * d.Get("movement_incident_rate", 0.6), 0, 0.6);
    }

    /// <summary>
    /// How ready a movement is to take power by force (0-1), for coups and civil wars: its organisation and the openings, with a floor for
    /// the loose sympathisers a collapsing democracy can still call on. No organisation, no coup.
    /// </summary>
    public virtual double MovementReadiness(Game game, Country c, Movement? m)
    {
        double org = m == null ? game.Data.Defines.Get("unorganized_readiness", 0.35) : Math.Clamp(m.Organization / 100, 0, 1);
        return org * (0.1 + 0.9 * MovementOpening(game, c));
    }

    /// <summary>
    /// A crackdown's chance to break the movement: the regime's strength (stability, the support it has) against the movement's organisation.
    /// Democracies find it harder to hit hard without losing legitimacy, so it also returns the stability it will cost.
    /// </summary>
    public virtual (double Success, double StabilityCost) CrackdownOdds(Game game, Country c, Movement m)
    {
        var d = game.Data.Defines;
        double stab = StabilityOf(game, c) / 100;
        double strength = 0.25 + 0.4 * stab + 0.35 * Math.Clamp(GovernmentSupport(game, c), 0, 1);
        double success = Math.Clamp(strength * (1.3 - m.Organization / 100 * 0.7), 0.1, 0.9);
        bool open = game.Politics.HoldsElections(c);
        double cost = d.Get("suppress_movement_stability", 5) * (0.6 + Math.Clamp(m.Progress / 100, 0, 1)) * (open ? 1.4 : 0.8);
        return (success, cost);
    }

    /// <summary>
    /// Monthly chance that a shut-out ideology with this much support takes power in a coup (no war).
    /// Likelier the more popular it is, the less popular the government is and the lower stability is.
    /// </summary>
    public virtual double CoupChance(Game game, Country c, IdeologyDef ideology, double popularity)
    {
        var d = game.Data.Defines;
        if (game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("civil_war_prone", 0) > 0.5) return 0; // (a country split in two camps is not toppled by a coup: it fights)
        // (a dictatorship that holds the army and the police - the profile trait regime_hold, 0-1 - is not toppled by a coup)
        double hold = game.AiProfiles.Enabled && game.Politics.Ideology(game.Politics.Of(c).Ruling)?.Extremist == true ? game.AiProfiles.PersonalityOf(c).Trait("regime_hold", 0) : 0;
        if (hold >= 1) return 0;
        // a country in turmoil is toppled by less (UnrestPressure lowers the support a coup needs by up to 20 points)
        double threshold = d.Get("coup_threshold", 0.4) - 0.2 * UnrestPressure(game, c);
        if (popularity < threshold) return 0;
        double ruling = game.Politics.Of(c).Popularity.GetValueOrDefault(game.Politics.Of(c).Ruling);
        double stability = StabilityOf(game, c);
        double readiness = MovementReadiness(game, c, game.Politics.MovementOf(c, ideology.Key));
        // (where the country is poor and unstable, or worn out by war, the struggle is fought out in a civil war instead)
        return (1 - hold) * d.Get("coup_chance", 0.06) * (1 + (popularity - threshold) * 5) * (1.5 - ruling) * (1.5 - stability / 100.0) * (1 - 0.7 * UnrestPressure(game, c)) * readiness * readiness * 5;
    }

    /// <summary>
    /// Monthly chance that a shut-out ideology with this much support starts a civil war (HOI4 style). Under pressure
    /// (<see cref="UnrestPressure"/>: unstable and poor, or worn out by war) it needs less support (civil_war_threshold
    /// 50%, down to 20%) and strikes far more often.
    /// </summary>
    public virtual double CivilWarChance(Game game, Country c, IdeologyDef ideology, double popularity)
    {
        double pressure = UnrestPressure(game, c);
        double threshold = game.Data.Defines.Get("civil_war_threshold", 0.5) - 0.3 * pressure;
        if (popularity < threshold) return 0;
        // (a civil war needs an organisation to fight it and a region to fight from: scattered support cannot raise an army)
        var summary = game.Population.Enabled ? game.Politics.PopSummaryOf(c) : null;
        double region = summary != null && summary.Stronghold.TryGetValue(ideology.Key, out var sh) ? 0.5 + sh.Share : 1;
        return game.Data.Defines.Get("civil_war_chance", 0.08) * (0.5 + (popularity - threshold) * 2) * (0.5 + 3 * pressure)
               * Math.Pow(MovementReadiness(game, c, game.Politics.MovementOf(c, ideology.Key)), 2) * 5 * Math.Min(1.5, region);
    }

    /// <summary>
    /// True if a revolt turns into a civil war, false if it is a coup: a government that still has real support
    /// (civil_war_loyal_share, 20%) fights; an unpopular one is simply pushed aside.
    /// </summary>
    public virtual bool RevoltIsCivilWar(Game game, Country c, string ideology, double popularity)
    {
        var p = game.Politics.Of(c);
        double loyal = p.Government.Sum(g => p.Popularity.GetValueOrDefault(g));
        // (a country split down the middle - the profile trait civil_war_prone - fights it out as long as the government keeps some of the country behind it)
        if (game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("civil_war_prone", 0) > 0.5 && loyal >= 0.15) return true;
        // (in a country whose history was one of coups, not wars - the profile trait coup_only - the army simply changes sides)
        if (game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("coup_only", 0) > 0.5) return false;
        // (a movement that has clearly outgrown the government - 40% and a third more than it - does not need a war: the army and the officials go over to it)
        if (popularity >= game.Data.Defines.Get("coup_outgrown_share", 0.5) && popularity > loyal * game.Data.Defines.Get("coup_outgrown_ratio", 1.6)) return false;
        return loyal >= game.Data.Defines.Get("civil_war_loyal_share", 0.2) && popularity < game.Data.Defines.Get("coup_above", 0.75);
    }

    /// <summary>Share of the country (states, divisions, treasury) that joins the rebels: the ideology's support.</summary>
    public virtual double CivilWarShare(Game game, Country c, string ideology, double popularity)
        => Math.Clamp(popularity * (game.AiProfiles.Enabled && game.Politics.Ideology(ideology)?.Extremist == true ? game.AiProfiles.PersonalityOf(c).Trait("rebel_share", 1) : 1), 0.1, 0.9); // (the profile trait rebel_share: the army sided with the rebels)

    /// <summary>
    /// Who has won a civil war: true = the old government, false = the rebels, null = not decided. A side that is beaten
    /// (lost its capital and most of its land) has lost; otherwise a side far ahead in war score for civil_war_decisive_months (3)
    /// wins (score gap civil_war_decisive_score, 40), a side clearly ahead after civil_war_stalemate_months (18) wins (gap 15),
    /// and no civil war goes on longer than civil_war_max_months (36): the side ahead takes the country, the government if even.
    /// </summary>
    public virtual bool? CivilWarVerdict(Game game, CivilWar cw, bool originBeaten, bool rebelBeaten)
    {
        if (rebelBeaten) return true;
        if (originBeaten) return false;
        var d = game.Data.Defines;
        double score = cw.War.ScoreFor(cw.Origin);
        int months = (game.World.Date.TotalDays - cw.War.StartDate.TotalDays) / 30;
        if (months >= d.Get("civil_war_decisive_months", 3) && Math.Abs(score) >= d.Get("civil_war_decisive_score", 40)) return score > 0;
        if (months >= d.Get("civil_war_stalemate_months", 18) && Math.Abs(score) >= 15) return score > 0;
        if (months >= d.Get("civil_war_max_months", 36)) return score >= 0;
        return null;
    }

    /// <summary>Years after a coup or civil war before the next one can start.</summary>
    public virtual double CalmYears(Game game, Country c) => game.Data.Defines.Get("revolt_calm_years", 5);

    // =============================================================================================
    // Pops and parliament
    // =============================================================================================

    /// <summary>Where an ideology stands on the economy: -1 (workers, redistribution) to +1 (property, order). The ideology file can set economic_axis.</summary>
    public virtual double IdeologyStance(Game game, IdeologyDef ideology)
    {
        if (ideology.Definition.Has("economic_axis")) return Math.Clamp(ideology.Definition.GetDouble("economic_axis", 0), -1, 1);
        string k = ideology.Key.ToLowerInvariant();
        if (k.Contains("commun") || k.Contains("anarch")) return -1;
        if (k.Contains("social") || k.Contains("labour") || k.Contains("labor") || k.Contains("agrar")) return -0.6;
        if (k.Contains("fascis") || k.Contains("nazi") || k.Contains("monarch") || k.Contains("reaction")) return 0.7;
        if (k.Contains("conserv") || k.Contains("nationalis") || k.Contains("authorit")) return 0.55;
        if (k.Contains("liberal")) return 0.2;
        return 0;
    }

    /// <summary>What a pop group wants from the government. Varies by state because acceptance, cores and the majority faith do.</summary>
    public virtual string PopObjective(Game game, Country c, State s, Pop pop)
    {
        var popSys = game.Population;
        if (!popSys.IsAccepted(c, pop.Culture) || (popSys.CoresEnabled && !s.Cores.Contains(c) && s.Cores.Count > 0)) return "autonomy";
        if (popSys.ReligionEnabled && pop.Religion != null && popSys.Of(c).Religion is { } faith && pop.Religion != faith) return "religious_rights";
        string t = (pop.Type ?? "").ToLowerInvariant();
        if (t.Length == 0) return "stability";
        if (t.Contains("work") || t.Contains("labo") || t.Contains("prolet") || t.Contains("peasant") || t.Contains("farm") || t.Contains("serf") || t.Contains("slave")) return "labour_rights";
        if (t.Contains("noble") || t.Contains("aristo") || t.Contains("landown") || t.Contains("capital") || t.Contains("bourg") || t.Contains("elite") || t.Contains("clergy")) return "property";
        return "stability";
    }

    /// <summary>How well the current government serves a pop group's objective and its preferred party (-1 to +1).</summary>
    public virtual double PopSatisfaction(Game game, Country c, State s, Pop pop, PopMood mood)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        double stance = 0;
        if (pol.Ideology(p.Ruling) is { } ruling) stance = IdeologyStance(game, ruling);
        double v = (StabilityOf(game, c) - 50) / 100.0;
        switch (mood.Objective)
        {
            case "autonomy":
                v -= 0.35;
                if (p.PartySeats.GetValueOrDefault("culture:" + pop.Culture) > 0) v += 0.45;
                break;
            case "religious_rights":
                v -= 0.3;
                if (pop.Religion != null && p.PartySeats.GetValueOrDefault("religion:" + pop.Religion) > 0) v += 0.45;
                break;
            case "labour_rights": v -= stance * 0.5; break;
            case "property": v += stance * 0.5; break;
            default: v += 0.05; break;
        }
        if (mood.PreferredParty.Length > 0)
        {
            if (pol.IsPartyBanned(c, mood.PreferredParty)) v -= 0.6;
            else if (p.GovernmentOrganizations.Contains(mood.PreferredParty, StringComparer.OrdinalIgnoreCase)) v += 0.35;
            else if (p.PartySeats.GetValueOrDefault(mood.PreferredParty) > 0) v += 0.05;
            else v -= 0.15;
        }
        return Math.Clamp(v, -1, 1);
    }

    /// <summary>
    /// How strongly a pop group leans to a party (1 = neutral, 0 = never). Looks at the party's own culture, religion and class
    /// attributes, minority parties of the group's culture or faith, the group's objective against the party's ideology, and
    /// radical groups drifting to extremist parties.
    /// </summary>
    public virtual double PopPartyAffinity(Game game, Country c, State s, Pop pop, PopMood mood, string partyKey)
    {
        if (partyKey.StartsWith("culture:", StringComparison.OrdinalIgnoreCase)) return string.Equals(partyKey[8..], pop.Culture, StringComparison.OrdinalIgnoreCase) ? 3 : 0.05;
        if (partyKey.StartsWith("religion:", StringComparison.OrdinalIgnoreCase)) return string.Equals(partyKey[9..], pop.Religion, StringComparison.OrdinalIgnoreCase) ? 3 : 0.05;
        var pol = game.Politics;
        double a = 1;
        if (game.World.GetGenericScope(partyKey) is { } party && PoliticsSystem.IsParty(party))
        {
            bool Match(string attr, string? value, double hit, double miss)
            {
                if (!party.Attributes.TryGetValue(attr, out var raw) || string.IsNullOrWhiteSpace(raw) || value == null) return true;
                bool ok = raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.OrdinalIgnoreCase);
                a *= ok ? hit : miss;
                return ok;
            }
            Match("culture", pop.Culture, 1.7, 0.7);
            Match("religion", pop.Religion, 1.7, 0.7);
            Match("class", pop.Type, 1.5, 0.8);
            // a regional branch in this very state makes the party known there
            if (pol.PartyUnitsOf(party, PoliticsSystem.PartyBranchKind).Any(b => pol.StateOfBranch(b) == s)) a *= 1.4;
        }
        if (pol.Ideology(pol.IdeologyKeyOfParty(partyKey)) is { } ideology)
        {
            double stance = IdeologyStance(game, ideology);
            if (mood.Objective == "labour_rights") a *= 1 - 0.5 * stance;
            else if (mood.Objective == "property") a *= 1 + 0.5 * stance;
            if (ideology.Extremist) a *= 1 + mood.Radicalism / 50.0;
            else a *= 1 - Math.Min(0.5, mood.Radicalism / 200.0);
        }
        return Math.Max(0.02, a);
    }

    /// <summary>What lengthens (+) or shortens (-) a seat holder's term, as fractions of the base term: (source, value).</summary>
    public virtual List<(string Source, double Value)> ParliamentTermModifiers(Game game, Country c, Seat seat, Character holder)
    {
        var list = new List<(string, double)>();
        double law = game.Modifiers.Sum(c, "parliament_term");
        if (Math.Abs(law) > 1e-9) list.Add(("Laws and government", law));
        double trait = holder.TraitModifier("parliament_term");
        if (Math.Abs(trait) > 1e-9) list.Add(("Traits", trait));
        double stability = StabilityOf(game, c);
        if (stability < 40) list.Add(("Unstable country", -0.15 * (40 - stability) / 40));
        return list;
    }

    /// <summary>Whether a pool candidate may stand for a seat (hard filters; affiliation and age).</summary>
    public virtual bool CandidateEligible(Game game, Country c, Seat seat, PoolCandidate cand)
        => cand.Age >= (int)game.Data.Defines.Get("parliament_min_age", 25)
           && cand.Age <= 85
           && cand.Party.Equals(seat.Party, StringComparison.OrdinalIgnoreCase);
}
