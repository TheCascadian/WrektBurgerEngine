using System.Globalization;
using System.Text;
using Burger.Core.Model;
using Burger.Core.Population;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>
/// Seat-level parliament. Every seat belongs to a party, a constituency (state) and the pop group it was won from.
/// Its holder is drawn from a seeded pool of generated people of that pop group, filtered by party affiliation and
/// eligibility, and serves a term (in months) that modifiers can lengthen or shorten. The same code runs for the AI.
/// </summary>
public sealed partial class PoliticsSystem
{
    private readonly Dictionary<Country, CountryParliament> _parliaments = new();

    /// <summary>The seats of a country, built (or rebuilt when party totals or constituencies changed) on first use.</summary>
    public CountryParliament ParliamentOf(Country c)
    {
        if (!_parliaments.TryGetValue(c, out var par)) _parliaments[c] = par = new CountryParliament();
        var p = Of(c);
        if (p.PartySeats.Count == 0) return par;
        var voters = VoterStates(c);
        string sig = p.TotalSeats + ";" + string.Join(",", p.PartySeats.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value))
                     + ";" + string.Join(",", voters.Select(s => s.Key));
        if (sig != par.Signature) { RebuildSeats(c, p, par, voters); par.Signature = sig; }
        return par;
    }

    private List<State> VoterStates(Country c)
        => PopsEnabled ? _game.Population.HomeStates(c).Where(_game.Population.HasPops).ToList() : new List<State>();

    private static int Hash(params string[] parts)
    {
        uint h = 2166136261;
        foreach (var part in parts)
        {
            foreach (char ch in part) { h ^= ch; h *= 16777619; }
            h ^= 0xFF; h *= 16777619;
        }
        return (int)(h & 0x7FFFFFFF);
    }

    private Random SeatRng(Country c, Seat seat, int epoch, string salt = "")
        => new(Hash(_game.Seed.ToString(CultureInfo.InvariantCulture), c.Tag, seat.Id, epoch.ToString(CultureInfo.InvariantCulture), seat.Draws.ToString(CultureInfo.InvariantCulture), salt));

    // ---- allocation

    private void RebuildSeats(Country c, CountryPolitics p, CountryParliament par, List<State> voters)
    {
        var old = par.Seats.GroupBy(s => (s.State, s.Party.ToLowerInvariant())).ToDictionary(g => g.Key, g => new Queue<Seat>(g));
        var seats = new List<Seat>();
        foreach (var (party, count) in p.PartySeats.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var perState = AllocateToStates(c, party, count, voters);
            foreach (var (state, n) in perState)
            {
                for (int i = 0; i < n; i++)
                {
                    var key = (state, party.ToLowerInvariant());
                    Seat seat;
                    if (old.TryGetValue(key, out var q) && q.Count > 0) seat = q.Dequeue();
                    else
                    {
                        seat = new Seat { Party = party, State = state };
                    }
                    seat.Id = $"{party}@{state?.Key ?? "nation"}#{i + 1}";
                    if (seat.PopGroup.Length == 0 || seat.Holder == null) seat.PopGroup = PickPopGroup(c, party, state, seat);
                    seats.Add(seat);
                }
            }
        }
        // people whose seat disappeared are simply no longer seated
        par.Seats = seats;
        foreach (var seat in seats) if (seat.Holder == null || !seat.Holder.Alive) FillSeat(c, par, seat);
    }

    /// <summary>Largest-remainder split of a party's seats over constituencies by population times how much its people lean to the party.</summary>
    private List<(State? State, int Seats)> AllocateToStates(Country c, string party, int count, List<State> voters)
    {
        if (voters.Count == 0) return new List<(State?, int)> { (null, count) };
        var weights = voters.Select(s => (State: s, W: Math.Max(1e-6, _game.Population.TotalOf(s) * StateProfile(c, s, party).Mean))).ToList();
        double sum = weights.Sum(w => w.W);
        var quota = weights.Select(w => (w.State, Exact: w.W / sum * count)).ToList();
        var result = quota.Select(q => (State: (State?)q.State, N: (int)Math.Floor(q.Exact), Frac: q.Exact - Math.Floor(q.Exact))).ToList();
        int left = count - result.Sum(r => r.N);
        foreach (var idx in result.Select((r, i) => (r, i)).OrderByDescending(x => x.r.Frac).ThenBy(x => x.r.State!.Key, StringComparer.Ordinal).Take(left).Select(x => x.i).ToList())
            result[idx] = (result[idx].State, result[idx].N + 1, 0);
        return result.Where(r => r.N > 0).Select(r => (r.State, r.N)).ToList();
    }

    private string PickPopGroup(Country c, string party, State? state, Seat seat)
    {
        if (state == null || !_game.Population.HasPops(state)) return "";
        var rng = new Random(Hash(_game.Seed.ToString(CultureInfo.InvariantCulture), c.Tag, seat.Id, "group"));
        var pops = _game.Population.Of(state).Pops.Where(x => x.Size > 0).ToList();
        if (pops.Count == 0) return "";
        var w = pops.Select(x => x.Size * Rules.PopPartyAffinity(_game, c, state, x, MoodOf(c, state, x), party)).ToList();
        return PopKey(pops[WeightedIndex(w, rng)]);
    }

    private static int WeightedIndex(IReadOnlyList<double> weights, Random rng)
    {
        double total = weights.Sum(w => Math.Max(0, w));
        if (total <= 0) return rng.Next(weights.Count);
        double r = rng.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++) { r -= Math.Max(0, weights[i]); if (r <= 0) return i; }
        return weights.Count - 1;
    }

    // ---- the pool

    private (string Culture, string? Religion, string? Type) SplitPopGroup(string group)
    {
        var parts = group.Split('|');
        string culture = parts.Length > 0 ? parts[0] : "";
        string? religion = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        string? type = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
        return (culture, religion, type);
    }

    private NameList NamesFor(Country c, string culture, string? religion)
    {
        var d = _game.Data;
        if (culture.Length > 0 && d.CultureNames.TryGetValue(culture, out var cn) && (cn.Male.Count > 0 || cn.Female.Count > 0)) return cn;
        if (religion != null && d.ReligionNames.TryGetValue(religion, out var rn) && (rn.Male.Count > 0 || rn.Female.Count > 0)) return rn;
        if (c.Names.Male.Count > 0 || c.Names.Female.Count > 0) return c.Names;
        return d.DefaultNames;
    }

    private static string Pick(List<string> list, List<string> fallback, Random rng)
        => list.Count > 0 ? list[rng.Next(list.Count)] : fallback.Count > 0 ? fallback[rng.Next(fallback.Count)] : "";

    private List<Trait>? _traitPool;
    private object? _traitPoolSrc;
    private int _traitPoolCount = -1;

    private List<Trait> TraitPool()
    {
        var src = _game.Data.Traits;
        if (_traitPool == null || !ReferenceEquals(_traitPoolSrc, src) || _traitPoolCount != src.Count)
        {
            _traitPool = src.Values.Where(t => t.RandomWeight > 0).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
            _traitPoolSrc = src; _traitPoolCount = src.Count;
        }
        return _traitPool;
    }

    /// <summary>Generates one candidate of the seat's pop group, standing for <paramref name="party"/>; fully determined by <paramref name="rng"/>.</summary>
    private PoolCandidate MakeCandidate(Country c, Seat seat, string party, Random rng, int index)
    {
        var (culture, religion, type) = SplitPopGroup(seat.PopGroup);
        var names = NamesFor(c, culture, religion);
        var def = _game.Data.DefaultNames;
        var cand = new PoolCandidate
        {
            Id = $"{seat.Id}:{index}",
            Female = rng.NextDouble() < Def("parliament_female_share", 0.25),
            Age = 25 + rng.Next(46),
            Culture = culture, Religion = religion, PopType = type,
            StateKey = seat.State?.Key,
            Party = party,
        };
        cand.FirstName = Pick(cand.Female ? names.Female : names.Male, cand.Female ? def.Female : def.Male, rng);
        cand.Family = Pick(names.Family, def.Family, rng);
        foreach (var stat in _game.Data.Defines.CharacterStats) cand.Stats[stat] = 2 + rng.Next(7);
        var traitPool = TraitPool();
        for (int i = 0, n = 1 + rng.Next(2); i < n && traitPool.Count > 0; i++)
        {
            var avail = traitPool.Where(t => !cand.Traits.Contains(t) && !cand.Traits.Any(o => o.Opposites.Contains(t.Key, StringComparer.OrdinalIgnoreCase) || t.Opposites.Contains(o.Key, StringComparer.OrdinalIgnoreCase))).ToList();
            if (avail.Count == 0) break;
            cand.Traits.Add(avail[WeightedIndex(avail.Select(t => t.RandomWeight).ToList(), rng)]);
        }
        if (_game.World.GetGenericScope(party) is { } scope && IsParty(scope))
        {
            var factions = FactionsOf(scope).ToList();
            if (factions.Count > 0)
            {
                var w = factions.Select(f => f.Attributes.TryGetValue("weight", out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0).ToList();
                cand.Faction = factions[WeightedIndex(w, rng)].Key;
            }
        }
        return cand;
    }

    /// <summary>Builds the pop group's pool (affiliations sampled by how much the group leans to each party) and draws a holder for the seat.</summary>
    private void FillSeat(Country c, CountryParliament par, Seat seat, bool incumbent = false)
    {
        var p = Of(c);
        var taken = new HashSet<string>(par.Seats.Where(s => s != seat && s.CandidateId.Length > 0).Select(s => s.CandidateId));
        var rng = SeatRng(c, seat, par.Epoch);
        int size = Math.Max(4, (int)Def("parliament_pool_size", 24));
        var parties = ReactionPartyKeys(c, p).Where(k => !IsPartyBanned(c, p, k)).ToList();
        var (culture, religion, type) = SplitPopGroup(seat.PopGroup);
        List<double> affinity;
        if (seat.State != null && _game.Population.HasPops(seat.State) && _game.Population.Of(seat.State).Pops.FirstOrDefault(x => PopKey(x) == seat.PopGroup) is { } pop)
        {
            var mood = MoodOf(c, seat.State, pop);
            affinity = parties.Select(k => Rules.PopPartyAffinity(_game, c, seat.State, pop, mood, k)).ToList();
        }
        else affinity = parties.Select(_ => 1.0).ToList();

        var pool = new List<PoolCandidate>();
        for (int i = 0; i < size; i++)
        {
            string party = parties.Count > 0 ? parties[WeightedIndex(affinity, rng)] : seat.Party;
            pool.Add(MakeCandidate(c, seat, party, rng, i));
        }
        var eligible = pool.Where(x => !taken.Contains(x.Id) && Rules.CandidateEligible(_game, c, seat, x)).ToList();
        if (eligible.Count == 0)
        {
            // nobody in the pool stands for this party: one more person of the group is drawn, and joins that party
            seat.Draws++;
            var extra = SeatRng(c, seat, par.Epoch, "extra");
            var cand = MakeCandidate(c, seat, seat.Party, extra, size + seat.Draws);
            cand.Age = Math.Max(cand.Age, (int)Def("parliament_min_age", 25));
            eligible.Add(cand);
        }
        var weights = eligible.Select(x =>
        {
            double stat = x.Stats.Count > 0 ? x.Stats.Values.Average() : 5;
            double w = 0.5 + stat / 10.0 + x.Traits.Sum(t => t.Mod("parliament_weight"));
            return Math.Max(0.05, w);
        }).ToList();
        var chosen = eligible[WeightedIndex(weights, rng)];
        Seat_Seat(c, seat, chosen);
    }

    private void Seat_Seat(Country c, Seat seat, PoolCandidate cand)
    {
        var now = _game.World.Date;
        var ch = new Character
        {
            FirstName = cand.FirstName,
            Family = cand.Family,
            Female = cand.Female,
            Country = c,
            Culture = cand.Culture.Length > 0 ? cand.Culture : null,
            Religion = cand.Religion,
            PopType = cand.PopType,
            HomeState = cand.StateKey,
            BirthDate = now.AddYears(-cand.Age),
            Traits = new List<Trait>(cand.Traits),
        };
        foreach (var kv in cand.Stats) ch.Stats[kv.Key] = kv.Value;
        _game.Characters.Register(ch);
        seat.Holder = ch;
        seat.CandidateId = cand.Id;
        seat.Faction = cand.Faction;
        seat.TermStart = now;
        seat.TermEnd = now.AddMonths(TermMonths(c, seat, ch).Months);
        seat.Text = null;
        BuildSeatText(c, seat);
    }

    // ---- terms

    /// <summary>Length of a term and where each modifier moved it: (months, [(source, value)]).</summary>
    public (int Months, List<(string Source, double Value)> Sources) TermMonths(Country c, Seat seat, Character holder)
    {
        double baseMonths = Def("parliament_term_months", ElectionMonths());
        var mods = Rules.ParliamentTermModifiers(_game, c, seat, holder);
        double factor = Math.Clamp(1 + mods.Sum(m => m.Value), 0.25, 3);
        int months = (int)Math.Round(Math.Clamp(baseMonths * factor, Def("parliament_term_min", 6), Def("parliament_term_max", 120)));
        return (Math.Max(1, months), mods);
    }

    // ---- text (collected once, then kept until the holder changes)

    private void BuildSeatText(Country c, Seat seat)
    {
        if (seat.Holder is not { } h) return;
        var (culture, religion, _) = SplitPopGroup(seat.PopGroup);
        bool minority = IsMinorityParty(seat.Party);
        seat.TextKey = minority ? "parliament_seat_minority" : "parliament_seat_default";
        var args = seat.TextArgs;
        args.Clear();
        args["name"] = h.DisplayName;
        args["party"] = PartyDisplayName(c, seat.Party);
        args["faction"] = seat.Faction != null && _game.World.GetGenericScope(seat.Faction) is { } f ? f.DisplayName : "";
        args["culture"] = culture.Length > 0 ? (_game.Population.Culture(culture)?.Name ?? Localisation.GetOrPretty(culture)) : "";
        args["religion"] = religion != null ? (_game.Population.Religion(religion)?.Name ?? Localisation.GetOrPretty(religion)) : "";
        args["province"] = seat.State?.DisplayName ?? c.DisplayName;
        args["term_end"] = seat.TermEnd.ToDisplay();
        args["trait"] = h.Traits.Count > 0 ? h.Traits[0].Name : "";
        seat.Text = RenderSeatText(seat);
    }

    private static string RenderSeatText(Seat seat)
    {
        string template = Localisation.Has(seat.TextKey) ? Localisation.Get(seat.TextKey)
            : seat.TextKey == "parliament_seat_minority"
                ? "{name} sits for {province} for {party}, speaking for the {culture} {religion} people there. Term ends {term_end}."
                : "{name} sits for {province} for {party}. Elected by {culture} {religion} voters. Term ends {term_end}.";
        var sb = new StringBuilder(template);
        foreach (var kv in seat.TextArgs) sb.Replace("{" + kv.Key + "}", kv.Value);
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Replace(" .", ".").Trim();
    }

    /// <summary>The seat's description, built once when its holder was seated and cached since.</summary>
    public string SeatText(Seat seat) => seat.Text ??= RenderSeatText(seat);

    // ---- monthly upkeep and elections

    /// <summary>Refills seats whose holder died or whose term ended. Called monthly for every country.</summary>
    public void ParliamentMonthly(Country c)
    {
        if (!Enabled) return;
        var par = ParliamentOf(c);
        if (par.Seats.Count == 0) return;
        var now = _game.World.Date;
        foreach (var seat in par.Seats.ToList())
        {
            if (seat.Holder is { Alive: true } && seat.TermEnd.TotalDays > now.TotalDays) continue;
            var previous = seat.Holder;
            bool reelect = previous is { Alive: true } && SeatRng(c, seat, par.Epoch, "reelect").NextDouble() < Def("parliament_reelect_chance", 0.5);
            if (reelect && previous != null)
            {
                seat.TermStart = now;
                seat.TermEnd = now.AddMonths(TermMonths(c, seat, previous).Months);
                seat.Text = null;
                BuildSeatText(c, seat);
                continue;
            }
            seat.Draws++;
            seat.CandidateId = "";
            FillSeat(c, par, seat);
        }
    }

    /// <summary>A general election: everyone's term ends and every seat is drawn again (incumbents may be re-elected).</summary>
    public void ParliamentElection(Country c)
    {
        if (!Enabled) return;
        var par = ParliamentOf(c);
        par.Epoch++;
        foreach (var seat in par.Seats.ToList())
        {
            var previous = seat.Holder;
            bool reelect = previous is { Alive: true } && SeatRng(c, seat, par.Epoch, "reelect").NextDouble() < Def("parliament_reelect_chance", 0.5);
            if (reelect && previous != null)
            {
                seat.TermStart = _game.World.Date;
                seat.TermEnd = seat.TermStart.AddMonths(TermMonths(c, seat, previous).Months);
                seat.Text = null;
                BuildSeatText(c, seat);
                continue;
            }
            seat.Holder = null;
            seat.CandidateId = "";
            seat.Draws = 0;
            FillSeat(c, par, seat);
        }
    }

    // ---- save

    private void SaveParliaments(Node root)
    {
        foreach (var (c, par) in _parliaments)
        {
            if (par.Seats.Count == 0) continue;
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            n.Add("epoch", par.Epoch.ToString(CultureInfo.InvariantCulture));
            n.Add("signature", par.Signature, true);
            foreach (var s in par.Seats)
            {
                var b = n.AddBlock("seat");
                b.Add("id", s.Id, true);
                b.Add("party", s.Party, true);
                if (s.State != null) b.Add("state", s.State.Key);
                b.Add("group", s.PopGroup, true);
                if (s.Holder != null) b.Add("holder", s.Holder.Key);
                b.Add("candidate", s.CandidateId, true);
                if (s.Faction != null) b.Add("faction", s.Faction);
                b.Add("start", s.TermStart.ToScript());
                b.Add("end", s.TermEnd.ToScript());
                b.Add("draws", s.Draws.ToString(CultureInfo.InvariantCulture));
                b.Add("text_key", s.TextKey);
                if (s.Text != null) b.Add("text", s.Text, true);
                var args = b.AddBlock("args");
                foreach (var kv in s.TextArgs) args.Add(kv.Key, kv.Value, true);
            }
        }
    }

    private void LoadParliaments(Node root)
    {
        foreach (var n in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var par = new CountryParliament { Epoch = n.GetInt("epoch"), Signature = n.GetString("signature", "")! };
            foreach (var b in n.GetAll("seat"))
            {
                Character? holder = null;
                if (b.GetString("holder") is { } hk && !_game.World.CharacterByKey.TryGetValue(hk, out holder)) continue;
                var seat = new Seat
                {
                    Id = b.GetString("id", "")!,
                    Party = b.GetString("party", "")!,
                    State = b.GetString("state") is { } sk ? _game.World.GetState(sk) : null,
                    PopGroup = b.GetString("group", "")!,
                    Holder = holder,
                    CandidateId = b.GetString("candidate", "")!,
                    Faction = b.GetString("faction"),
                    Draws = b.GetInt("draws"),
                    TextKey = b.GetString("text_key", "")!,
                    Text = b.GetString("text"),
                };
                if (GameDate.TryParse(b.GetString("start"), out var st)) seat.TermStart = st;
                if (GameDate.TryParse(b.GetString("end"), out var en)) seat.TermEnd = en;
                foreach (var kv in b.Get("args")?.All ?? Enumerable.Empty<Node>()) seat.TextArgs[kv.Key] = kv.Value ?? "";
                par.Seats.Add(seat);
            }
            _parliaments[c] = par;
        }
    }
}
