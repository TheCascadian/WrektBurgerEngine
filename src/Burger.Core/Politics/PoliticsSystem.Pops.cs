using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Population;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>
/// What the people think. Every pop group (culture, religion, class) in every state has a <see cref="PopMood"/>: what it
/// wants (its objective), what it thinks of the government, and how it reacts. The same code runs for every country, the
/// player's and the AI's. The mood feeds elections (which party each group leans to, and so which seats a state sends),
/// stability, and the support of banned parties, which people keep backing in secret.
/// </summary>
public sealed partial class PoliticsSystem
{
    private readonly Dictionary<State, Dictionary<string, PopMood>> _moods = new();
    private readonly Dictionary<Country, PopSummary> _popSummary = new();

    /// <summary>People are tracked when politics and population are both on.</summary>
    public bool PopsEnabled => Enabled && _game.Population.Enabled;

    public static string PopKey(Pop pop) => pop.Key;

    /// <summary>The mood of a pop group (created on first use, with the group's objective already worked out).</summary>
    public PopMood MoodOf(Country c, State s, Pop pop)
    {
        if (!_moods.TryGetValue(s, out var moods)) _moods[s] = moods = new Dictionary<string, PopMood>(StringComparer.OrdinalIgnoreCase);
        string key = PopKey(pop);
        if (!moods.TryGetValue(key, out var mood))
        {
            moods[key] = mood = new PopMood();
            mood.Objective = Rules.PopObjective(_game, c, s, pop);
        }
        return mood;
    }

    /// <summary>The moods stored for a state, or null when it has none yet.</summary>
    public IReadOnlyDictionary<string, PopMood>? MoodsOf(State s) => _moods.TryGetValue(s, out var m) ? m : null;

    /// <summary>Parties the people can lean to: every named party and ideology, plus minority parties that hold seats.</summary>
    private List<string> ReactionPartyKeys(Country c, CountryPolitics p)
    {
        var keys = PartyKeysOf(c).ToList();
        foreach (var k in p.PartySeats.Keys) if (IsMinorityParty(k) && !keys.Contains(k, StringComparer.OrdinalIgnoreCase)) keys.Add(k);
        return keys;
    }

    /// <summary>One month of reactions in a country: every pop group reconsiders the government.</summary>
    public void PopReactionMonthly(Country c)
    {
        if (!PopsEnabled) return;
        var p = Of(c);
        var keys = ReactionPartyKeys(c, p);
        if (keys.Count == 0) return;
        double rate = Math.Clamp(Def("pop_reaction_rate", 0.15), 0.01, 1);
        var radicalToward = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double total = 0;
        int announced = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in c.OwnedStates)
        {
            if (!_game.Population.HasPops(s)) continue;
            var sp = _game.Population.Of(s);
            double stateTotal = sp.Total;
            seen.Clear();
            foreach (var pop in sp.Pops)
            {
                if (pop.Size <= 0) continue;
                var m = MoodOf(c, s, pop);
                seen.Add(PopKey(pop));
                m.Objective = Rules.PopObjective(_game, c, s, pop);
                string best = "";
                double bestScore = -1;
                foreach (var k in keys)
                {
                    double score = Rules.PopPartyAffinity(_game, c, s, pop, m, k) * (0.2 + p.PartyPopularity.GetValueOrDefault(k));
                    if (score > bestScore) { bestScore = score; best = k; }
                }
                m.PreferredParty = best;
                double target = Rules.PopSatisfaction(_game, c, s, pop, m) * 100;
                m.Opinion = Math.Clamp(m.Opinion + (target - m.Opinion) * rate, -100, 100);
                m.Grievance = Math.Clamp(m.Grievance + (Math.Max(0, -m.Opinion) - m.Grievance) * rate * 0.7, 0, 100);
                bool banned = best.Length > 0 && IsPartyBanned(c, p, best);
                if (banned) m.Radicalism += 3;
                else if (m.Grievance > 50) m.Radicalism += 1.5;
                else m.Radicalism -= 1.2;
                m.Radicalism = Math.Clamp(m.Radicalism, 0, 100);
                string before = m.Reaction;
                m.Reaction = m.Opinion > 25 ? "Approve" : m.Opinion > -10 ? "Comply" : m.Grievance < 45 ? "Disapprove" : m.Radicalism < 55 ? "Protest" : "Radicalise";
                if (m.Reaction == "Radicalise" && IdeologyKeyOfParty(best) is { } ideology)
                    radicalToward[ideology] = radicalToward.GetValueOrDefault(ideology) + pop.Size;
                total += pop.Size;
                if (c.IsPlayer && announced < 3 && before != m.Reaction && (m.Reaction is "Protest" or "Radicalise") && pop.Size >= stateTotal * 0.2)
                {
                    announced++;
                    _game.Notify($"{PopGroupName(pop)} in {s.DisplayName} are {(m.Reaction == "Protest" ? "protesting against" : "turning against")} the government.", NotificationKind.Bad);
                }
            }
            if (_moods.TryGetValue(s, out var moods))
                foreach (var stale in moods.Keys.Where(k => !seen.Contains(k)).ToList()) moods.Remove(stale);
        }
        _popSummary[c] = Summarize(c, p);
        // radicalised people push their party's ideology up; a banned party gains strength the harder it is repressed
        if (total > 0)
        {
            double drift = Def("pop_radical_drift", 1) / 100.0;
            foreach (var kv in radicalToward) AddPopularity(c, kv.Key, kv.Value / total * drift);
        }
        _modsDirty.Add(c);
    }

    public static string PopGroupNameOf(Game game, string culture, string? religion)
    {
        string name = game.Population.Culture(culture)?.Name ?? Localisation.GetOrPretty(culture);
        if (religion != null && game.Population.ReligionEnabled)
            name += " " + (game.Population.Religion(religion)?.Name ?? Localisation.GetOrPretty(religion));
        return name;
    }

    public string PopGroupName(Pop pop) => PopGroupNameOf(_game, pop.Culture, pop.Religion);

    private PopSummary Summarize(Country c, CountryPolitics p)
    {
        var summary = new PopSummary();
        double total = 0, opinion = 0, unrest = 0;
        var grievanceSum = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var byState = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in c.OwnedStates)
        {
            if (!_moods.TryGetValue(s, out var moods) || !_game.Population.HasPops(s)) continue;
            foreach (var pop in _game.Population.Of(s).Pops)
            {
                if (pop.Size <= 0 || !moods.TryGetValue(PopKey(pop), out var m)) continue;
                total += pop.Size;
                opinion += pop.Size * m.Opinion;
                if (m.Reaction is "Protest" or "Radicalise") unrest += pop.Size;
                if (m.PreferredParty.Length > 0 && IdeologyKeyOfParty(m.PreferredParty) is { } bk)
                {
                    summary.Backing[bk] = summary.Backing.GetValueOrDefault(bk) + pop.Size;
                    grievanceSum[bk] = grievanceSum.GetValueOrDefault(bk) + pop.Size * m.Grievance;
                    byState.TryAdd(bk, new Dictionary<string, double>());
                    byState[bk][s.Key] = byState[bk].GetValueOrDefault(s.Key) + pop.Size;
                }
                if (m.PreferredParty.Length > 0 && IsPartyBanned(c, p, m.PreferredParty) && IdeologyKeyOfParty(m.PreferredParty) is { } ik)
                    summary.Underground[ik] = summary.Underground.GetValueOrDefault(ik) + pop.Size * m.Radicalism / 100.0;
            }
        }
        foreach (var kv in summary.Backing.ToList())
        {
            summary.BackerGrievance[kv.Key] = kv.Value > 0 ? grievanceSum[kv.Key] / kv.Value : 0;
            var top = byState[kv.Key].MaxBy(x => x.Value);
            summary.Stronghold[kv.Key] = (top.Key, kv.Value > 0 ? top.Value / kv.Value : 0);
        }
        if (total > 0) foreach (var k in summary.Backing.Keys.ToList()) summary.Backing[k] /= total;
        if (total > 0)
        {
            summary.AverageOpinion = opinion / total;
            summary.UnrestShare = unrest / total;
            foreach (var k in summary.Underground.Keys.ToList()) summary.Underground[k] /= total;
        }
        return summary;
    }

    /// <summary>The country-wide pop summary (recounted monthly; computed from the stored moods when none was made yet).</summary>
    public PopSummary PopSummaryOf(Country c)
    {
        if (_popSummary.TryGetValue(c, out var s)) return s;
        return _popSummary[c] = Summarize(c, Of(c));
    }

    /// <summary>Stability points from what people think of the government (rule StabilitySources).</summary>
    public double PopOpinionStability(Country c)
    {
        if (!PopsEnabled) return 0;
        return Math.Clamp(PopSummaryOf(c).AverageOpinion / 100.0, -1, 1) * Def("pop_opinion_stability", 6);
    }

    /// <summary>Share of the people (0-1) secretly backing a banned ideology's party, scaled by underground_support_weight.</summary>
    public double UndergroundSupport(Country c, string ideology)
    {
        if (!PopsEnabled) return 0;
        return PopSummaryOf(c).Underground.GetValueOrDefault(ideology) * Def("underground_support_weight", 0.6);
    }

    /// <summary>Mean affinity of a state's people for a party, and the group that leans to it most (weighted by size).</summary>
    public (double Mean, Pop? Dominant) StateProfile(Country c, State s, string partyKey)
    {
        var sp = _game.Population.Of(s);
        double total = 0, sum = 0, bestScore = -1;
        Pop? best = null;
        foreach (var pop in sp.Pops)
        {
            if (pop.Size <= 0) continue;
            var m = MoodOf(c, s, pop);
            double a = Rules.PopPartyAffinity(_game, c, s, pop, m, partyKey);
            total += pop.Size;
            sum += pop.Size * a;
            if (pop.Size * a > bestScore) { bestScore = pop.Size * a; best = pop; }
        }
        return (total > 0 ? sum / total : 1, best);
    }

    /// <summary>
    /// Multiplier on each party's vote from what the country's people lean to (their culture, religion, class, objectives
    /// and mood), scaled by pop_vote_weight. 1 = no change. Minority parties are not touched (they have their own votes).
    /// </summary>
    public Dictionary<string, double> PopVoteMultipliers(Country c, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double w = Def("pop_vote_weight", 0.5);
        var list = keys.Where(k => !IsMinorityParty(k)).ToList();
        foreach (var k in list) result[k] = 1;
        if (!PopsEnabled || w <= 0 || list.Count == 0) return result;
        var voters = _game.Population.HomeStates(c).Where(_game.Population.HasPops).ToList();
        double total = voters.Sum(_game.Population.TotalOf);
        if (total <= 0) return result;
        var mean = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in list)
            mean[k] = voters.Sum(s => _game.Population.TotalOf(s) * StateProfile(c, s, k).Mean) / total;
        double avg = mean.Values.Average();
        if (avg <= 1e-9) return result;
        foreach (var k in list) result[k] = Math.Max(0.05, 1 + w * (mean[k] / avg - 1));
        return result;
    }

    /// <summary>One pop group summed over a set of states, for the People views.</summary>
    public sealed class PopGroupRow
    {
        public string Key = "", Name = "", Objective = "stability", Reaction = "Comply", PreferredParty = "";
        public double Size, Opinion, Grievance, Radicalism;
        /// <summary>States where the group's opinion is lowest: (state, opinion).</summary>
        public List<(State State, double Opinion)> Worst = new();
        public int States;
    }

    /// <summary>Pop groups of the given states with size-weighted moods (all of a country's states when none are given).</summary>
    public List<PopGroupRow> PopGroupRows(Country c, IEnumerable<State>? states = null)
    {
        var rows = new Dictionary<string, PopGroupRow>(StringComparer.OrdinalIgnoreCase);
        var tally = new Dictionary<string, Dictionary<string, double>[]>(StringComparer.OrdinalIgnoreCase);
        if (!PopsEnabled) return new List<PopGroupRow>();
        foreach (var s in states ?? c.OwnedStates)
        {
            if (!_moods.TryGetValue(s, out var moods) || !_game.Population.HasPops(s)) continue;
            foreach (var pop in _game.Population.Of(s).Pops)
            {
                if (pop.Size <= 0 || !moods.TryGetValue(PopKey(pop), out var m)) continue;
                string key = PopKey(pop);
                if (!rows.TryGetValue(key, out var row))
                {
                    rows[key] = row = new PopGroupRow { Key = key, Name = PopGroupName(pop) };
                    tally[key] = new[] { new Dictionary<string, double>(), new Dictionary<string, double>(), new Dictionary<string, double>() };
                }
                row.Size += pop.Size;
                row.Opinion += pop.Size * m.Opinion;
                row.Grievance += pop.Size * m.Grievance;
                row.Radicalism += pop.Size * m.Radicalism;
                row.States++;
                row.Worst.Add((s, m.Opinion));
                var t = tally[key];
                t[0][m.Objective] = t[0].GetValueOrDefault(m.Objective) + pop.Size;
                t[1][m.Reaction] = t[1].GetValueOrDefault(m.Reaction) + pop.Size;
                if (m.PreferredParty.Length > 0) t[2][m.PreferredParty] = t[2].GetValueOrDefault(m.PreferredParty) + pop.Size;
            }
        }
        foreach (var (key, row) in rows)
        {
            row.Opinion /= row.Size; row.Grievance /= row.Size; row.Radicalism /= row.Size;
            var t = tally[key];
            row.Objective = t[0].Count > 0 ? t[0].MaxBy(kv => kv.Value).Key : "stability";
            row.Reaction = t[1].Count > 0 ? t[1].MaxBy(kv => kv.Value).Key : "Comply";
            row.PreferredParty = t[2].Count > 0 ? t[2].MaxBy(kv => kv.Value).Key : "";
            row.Worst = row.Worst.OrderBy(w => w.Opinion).Take(3).ToList();
        }
        return rows.Values.OrderByDescending(r => r.Size).ToList();
    }

    // ---- save

    private void SavePopMoods(Node root)
    {
        foreach (var (s, moods) in _moods)
        {
            if (moods.Count == 0) continue;
            var n = root.AddBlock("state");
            n.Add("key", s.Key);
            var pops = n.AddBlock("pops");
            foreach (var (key, m) in moods)
            {
                var b = pops.AddBlock("pop");
                b.Add("key", key, true);
                b.Add("opinion", F(m.Opinion));
                b.Add("grievance", F(m.Grievance));
                b.Add("radicalism", F(m.Radicalism));
                b.Add("objective", m.Objective);
                b.Add("reaction", m.Reaction);
                if (m.PreferredParty.Length > 0) b.Add("party", m.PreferredParty, true);
            }
        }
    }

    private void LoadPopMoods(Node root)
    {
        foreach (var n in root.GetAll("state"))
        {
            var s = _game.World.GetState(n.GetString("key"));
            if (s == null) continue;
            var moods = new Dictionary<string, PopMood>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in n.Get("pops")?.GetAll("pop") ?? Enumerable.Empty<Node>())
            {
                string? key = b.GetString("key");
                if (key == null) continue;
                moods[key] = new PopMood
                {
                    Opinion = b.GetDouble("opinion"),
                    Grievance = b.GetDouble("grievance"),
                    Radicalism = b.GetDouble("radicalism"),
                    Objective = b.GetString("objective", "stability")!,
                    Reaction = b.GetString("reaction", "Comply")!,
                    PreferredParty = b.GetString("party", "")!,
                };
            }
            _moods[s] = moods;
        }
    }
}
