// Politics rules: how ideology popularity drifts each month, election votes, which parties form coalitions,
// how fast political movements grow and when shut-out ideologies start civil wars.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>Overridable rules for politics: how popularity drifts, who forms coalitions, how movements grow and when civil wars break out. Subclass and assign to <c>Game.Rules.Politics</c> (the politics feature puts a copy in backend/Politics.cs).</summary>
public class GamePoliticsRules : PoliticsRules
{
    /// <summary>
    /// Monthly change of each ideology's popularity, in percentage points (they are normalised to 100% afterwards).
    /// Sources: modifiers named <c>IDEOLOGY_drift</c> (laws, ministers, events), the ruling party's propaganda,
    /// lost wars, war exhaustion, lost core land, low stability, ideologies ruling the neighbours, political crises and a
    /// little chance. No ideology takes a country over for good: a dominant one loses support and small ones keep a following.
    /// </summary>
    public override Dictionary<string, double> MonthlyDrift(Game game, Country c)
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
            double rival = p.Popularity.Where(kv => !p.Government.Contains(kv.Key) && kv.Key != p.Ruling).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
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
        if (pol.Ideologies.Count > 1 && game.Rng.NextDouble() < d.Get("political_crisis_chance", 0.01))
        {
            var others = pol.Ideologies.Where(i => i.Key != p.Ruling).ToList();
            double Weight(IdeologyDef i) => 0.5 + p.Popularity.GetValueOrDefault(i.Key) + (i.Extremist && stability < 50 ? 1 : 0);
            double pick = game.Rng.NextDouble() * others.Sum(Weight);
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
        foreach (var i in pol.Ideologies) Add(i.Key, (game.Rng.NextDouble() - 0.5) * d.Get("random_drift", 0.4));
        // an old parliamentary tradition (profile trait democratic_roots) holds the extremists back, whatever feeds them: war, neighbours, lost land
        if (game.AiProfiles.Enabled && game.AiProfiles.PersonalityOf(c).Trait("democratic_roots", 0) is var roots && roots > 0)
            foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling))
                if (drift[i.Key] > 0) drift[i.Key] *= 1 - 0.85 * Math.Clamp(roots, 0, 1);
        return drift;
    }

    /// <summary>
    /// Does a government that has lost the country fall into a snap election? When the most popular party is out of government with a clear
    /// lead (snap_election_lead, 8 points) and the last vote was over snap_election_months (12) ago: coalitions collapse, the president dissolves
    /// parliament, the people are asked again.
    /// </summary>
    public override bool CallsSnapElection(Game game, Country c)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        var d = game.Data.Defines;
        if (p.LastElection is { } last && game.World.Date.TotalDays - last.TotalDays < d.Get("snap_election_months", 12) * 30) return false;
        var top = p.Popularity.OrderByDescending(kv => kv.Value).First();
        if (p.Government.Contains(top.Key)) return false;
        return top.Value - p.Popularity.GetValueOrDefault(p.Ruling) >= d.Get("snap_election_lead", 0.08);
    }

    /// <summary>Support for the parties in government (0-1): the sum of their ideologies' popularity.</summary>
    public override double GovernmentSupport(Game game, Country c)
    {
        var p = game.Politics.Of(c);
        return p.Government.Count > 0 ? p.Government.Sum(g => p.Popularity.GetValueOrDefault(g)) : p.Popularity.GetValueOrDefault(p.Ruling);
    }

    /// <summary>
    /// What moves a country's stability besides laws, ministers and buildings (points of stability_target, with the name
    /// shown in tooltips): poverty (poor countries are restless: poverty_stability, up to -25), prosperity, a government
    /// whose parties have under government_support_needed (40%) support, and core land held by others.
    /// </summary>
    public override List<(string Source, double Value)> StabilitySources(Game game, Country c)
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
        return list;
    }

 // (in a country split into two camps nobody takes the government from the other by the vote)

    /// <summary>
    /// Monthly chance an AI government gives in to a movement that demands power. Parties that would govern with it (WillGovernWith: the conservatives
    /// and the fascists in Germany 1933, the king and the Blackshirts in Italy 1922) give in far more readily than the rest, which fight it; the more
    /// popular the movement against the government, and the shakier the country, the likelier the surrender.
    /// </summary>
    public override double AiGivesIn(Game game, Country c, Movement m, double popularity)
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
        if (p.Ruling == "democratic") chance *= game.Data.Defines.Get("democracy_resilience", 1); // (the parliamentary democracies of the west did not hand over power; the profile trait appease overrides it)
        if (game.AiProfiles.Enabled) chance *= game.AiProfiles.PersonalityOf(c).Trait("appease"); // (profile trait appease: 1 = as usual, near 0 = never gives in)
        chance *= 0.6 + 0.8 * Math.Clamp(m.Organization / 100, 0, 1); // (an organised movement cannot be waved away; a loose one can)
        return Math.Clamp(chance, 0, 0.8);
    }

    /// <summary>
    /// The support a shut-out ideology can call on for a movement or a revolt: its own popularity plus that of the parties out of government that
    /// would govern with it (the monarchists, the army and the Falange against the Republic). Only where the define movement_blocs is 1.
    /// </summary>
    public override double MovementSupport(Game game, Country c, IdeologyDef ideology)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        double v = p.Popularity.GetValueOrDefault(ideology.Key);
        // (only in a country split into two camps - the profile trait civil_war_prone - do the right's parties rise together)
        if (game.Data.Defines.Get("movement_blocs", 0) <= 0 || !ideology.Extremist || !game.AiProfiles.Enabled || game.AiProfiles.PersonalityOf(c).Trait("civil_war_prone", 0) < 0.5) return v;
        foreach (var o in pol.Ideologies)
            if (o.Key != ideology.Key && !o.Extremist && !p.Government.Contains(o.Key) && o.Key != p.Ruling && WillGovernWith(game, c, o, ideology))
                v += p.Popularity.GetValueOrDefault(o.Key);
        return Math.Min(1, v);
    }

    /// <summary>
    /// Monthly chance that a shut-out ideology with this much support takes power in a coup (no war).
    /// Likelier the more popular it is, the less popular the government is and the lower stability is.
    /// </summary>
    public override double CoupChance(Game game, Country c, IdeologyDef ideology, double popularity)
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
        // (where the country is poor and unstable, or worn out by war, the struggle is fought out in a civil war instead)
        return (1 - hold) * d.Get("coup_chance", 0.06) * (1 + (popularity - threshold) * 5) * (1.5 - ruling) * (1.5 - stability / 100.0) * (1 - 0.7 * UnrestPressure(game, c)) * Math.Pow(MovementReadiness(game, c, game.Politics.MovementOf(c, ideology.Key)), 2) * 5;
    }

 // (the profile trait rebel_share: the army sided with the rebels)
}

/// <summary>Installs the politics rules.</summary>
public sealed class PoliticsSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Politics = new GamePoliticsRules();
    }
}
