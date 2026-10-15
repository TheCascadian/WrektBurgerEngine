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

    /// <summary>What is kept of a generated person who left parliament and was forgotten: their last age, when they left and how many times they sat.</summary>
    private readonly record struct FormerMember(int Age, GameDate Left, int Times);
    private readonly Dictionary<string, FormerMember> _former = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Character> _retired = new();
    /// <summary>How many forgotten former members are noted (their names, last ages and times seated).</summary>
    public int ForgottenMembers => _former.Count;
    /// <summary>Every seat built so far, in every country (does not build any).</summary>
    public IEnumerable<Seat> AllSeats => _parliaments.Values.SelectMany(p => p.Seats);
    private HashSet<Character>? _pinned;
    private int _pinnedDay = int.MinValue;

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
                    if (seat.PopGroup.Length == 0 || !seat.Occupied) seat.PopGroup = PickPopGroup(c, party, state, seat);
                    seats.Add(seat);
                }
            }
        }
        // people whose seat disappeared are no longer seated
        foreach (var q in old.Values) foreach (var gone in q) Leave(gone);
        par.Seats = seats;
        if (c.IsPlayer) foreach (var seat in seats) if (!seat.Occupied || seat.Character is { Alive: false }) FillSeat(c, par, seat);
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

    /// <summary>Seats the chosen candidate as a light record. The person who sat here before leaves first, so a same-named replacement is the same person again.</summary>
    private void Seat_Seat(Country c, Seat seat, PoolCandidate cand)
    {
        var now = _game.World.Date;
        Leave(seat);
        var stats = _game.Data.Defines.CharacterStats;
        var m = new SeatMember
        {
            FirstName = cand.FirstName,
            Family = cand.Family,
            Female = cand.Female,
            BirthDate = now.AddYears(-cand.Age),
            Traits = cand.Traits.ToArray(),
            Stats = stats.Select(k => (byte)Math.Clamp(cand.Stats.TryGetValue(k, out var v) ? v : 5, 0, 255)).ToArray(),
        };
        // somebody of this name sat before and was forgotten: it is the same person again, older by the years away
        if (_former.Remove(m.FirstName + "|" + m.Family, out var back))
        {
            m.Times = back.Times + 1;
            var birth = back.Left.AddYears(-back.Age);
            if (GameDate.YearsBetween(birth, now) <= (int)Def("parliament_max_age", 90)) m.BirthDate = birth;
        }
        seat.Member = m;
        seat.CandidateId = cand.Id;
        seat.Faction = cand.Faction;
        seat.TermStart = now;
        seat.TermEnd = now.AddMonths(TermMonths(c, seat).Months);
    }

    // ---- light records and full characters

    /// <summary>A character object for a light record: not registered anywhere, so the world does not know it.</summary>
    private Character Build(Country c, Seat seat, SeatMember m)
    {
        var (culture, religion, type) = SplitPopGroup(seat.PopGroup);
        var ch = new Character
        {
            FirstName = m.FirstName, Family = m.Family, Female = m.Female, Country = c,
            Culture = culture.Length > 0 ? culture : null, Religion = religion, PopType = type, HomeState = seat.State?.Key,
            BirthDate = m.BirthDate, Traits = new List<Trait>(m.Traits), ParliamentTimes = m.Times,
        };
        var names = _game.Data.Defines.CharacterStats;
        for (int i = 0; i < names.Count; i++) ch.Stats[names[i]] = i < m.Stats.Length ? m.Stats[i] : 5;
        return ch;
    }

    /// <summary>The seat holder as a full, registered character, made on the first request (null for an empty seat). The only place a light record becomes a character.</summary>
    public Character? CharacterOf(Country c, Seat seat)
    {
        if (seat.Character != null) return seat.Character;
        if (seat.Member is not { } m) return null;
        var ch = _game.Characters.Register(Build(c, seat, m));
        seat.Character = ch;
        seat.Member = null;
        return ch;
    }

    /// <summary>The holder as something rules can read: the full character if there is one, else a short-lived copy of the record.</summary>
    private Character? ViewOf(Country c, Seat seat) => seat.Character ?? (seat.Member is { } m ? Build(c, seat, m) : null);

    // ---- forgetting

    /// <summary>
    /// The holder leaves the seat. A light record is dropped and noted (name, last age, when they left, times seated). A full character is also
    /// removed from the world once nothing points at it: family, a post, an army, variables, remembered acts, scheduled events, modifiers, scope links.
    /// </summary>
    private void Leave(Seat seat)
    {
        var now = _game.World.Date;
        if (seat.Member is { } m)
        {
            Note(m.FirstName + "|" + m.Family, m.Age(now), m.Times, now);
        }
        else if (seat.Character is { } ch && ch.PopType != null && _game.World.CharacterByKey.TryGetValue(ch.Key, out var reg) && reg == ch && !_retired.Contains(ch)
                 && ch.Father == null && ch.Mother == null && ch.Spouse == null && ch.Children.Count == 0 && !ch.IsGeneral
                 && ch.Vars.Flags.Count == 0 && ch.Vars.Vars.Count == 0 && !Pinned().Contains(ch))
        {
            Note(ch.FirstName + "|" + ch.Family, ch.Age(now), Math.Max(1, ch.ParliamentTimes), now);
            _retired.Add(ch);
        }
        seat.Member = null;
        seat.Character = null;
    }

    private void Note(string name, int age, int times, GameDate now)
    {
        if (_former.TryGetValue(name, out var old)) times = Math.Max(times, old.Times);
        _former[name] = new FormerMember(age, now, times);
    }

    /// <summary>Removes the people retired so far from the character list (once a month, so the list is not rewritten per person).</summary>
    private void FlushRetired()
    {
        _game.World.RemoveCharacters(_retired);
        _retired.Clear();
    }

    /// <summary>Characters other systems point at; built once per day.</summary>
    private HashSet<Character> Pinned()
    {
        var w = _game.World;
        if (_pinned != null && _pinnedDay == w.Date.TotalDays) return _pinned;
        var set = new HashSet<Character>(ReferenceEqualityComparer.Instance);
        void Add(Character? c) { if (c != null) set.Add(c); }
        void AddScope(IScope? s) { if (s is Character c) set.Add(c); }
        foreach (var country in w.Countries)
        {
            Add(country.Ruler); Add(country.Heir);
            foreach (var m in Of(country).Ministers.Values) Add(m);
        }
        foreach (var army in w.Armies) Add(army.General);
        foreach (var scope in w.GenericScopes)
            foreach (var v in scope.Attributes.Values) Add(w.GetCharacter(v));
        foreach (var key in _game.Memory.CharacterKeys()) Add(w.GetCharacter(key));
        foreach (var e in _game.Events.Scheduled)
        {
            AddScope(e.Root);
            foreach (var s in e.Scopes.Values) AddScope(s);
        }
        foreach (var (scope, list) in _game.Modifiers.Active) if (list.Count > 0) AddScope(scope);
        foreach (var (child, parent) in w.ScopeHierarchy.ParentLinks) { AddScope(child); AddScope(parent); }
        _pinnedDay = w.Date.TotalDays;
        return _pinned = set;
    }

    // ---- terms

    /// <summary>Length of a term for whoever holds the seat (rules see a full character, or a short-lived copy of the light record).</summary>
    public (int Months, List<(string Source, double Value)> Sources) TermMonths(Country c, Seat seat)
        => ViewOf(c, seat) is { } holder ? TermMonths(c, seat, holder) : (1, new List<(string, double)>());

    /// <summary>Length of a term and where each modifier moved it: (months, [(source, value)]).</summary>
    public (int Months, List<(string Source, double Value)> Sources) TermMonths(Country c, Seat seat, Character holder)
    {
        double baseMonths = Def("parliament_term_months", ElectionMonths());
        var mods = Rules.ParliamentTermModifiers(_game, c, seat, holder);
        double factor = Math.Clamp(1 + mods.Sum(m => m.Value), 0.25, 3);
        int months = (int)Math.Round(Math.Clamp(baseMonths * factor, Def("parliament_term_min", 6), Def("parliament_term_max", 120)));
        return (Math.Max(1, months), mods);
    }

    // ---- text (built when asked for, never stored)

    /// <summary>The seat's description, built from the seat's current data.</summary>
    public string SeatText(Country c, Seat seat)
    {
        if (!seat.Occupied) return $"A seat for {seat.State?.DisplayName ?? c.DisplayName} held by {PartyDisplayName(c, seat.Party)}. Its member is not tracked.";
        var (culture, religion, _) = SplitPopGroup(seat.PopGroup);
        var trait = seat.Character?.Traits.FirstOrDefault() ?? seat.Member?.Traits.FirstOrDefault();
        var args = new Dictionary<string, string>
        {
            ["name"] = seat.HolderName ?? "",
            ["party"] = PartyDisplayName(c, seat.Party),
            ["faction"] = seat.Faction != null && _game.World.GetGenericScope(seat.Faction) is { } f ? f.DisplayName : "",
            ["culture"] = culture.Length > 0 ? (_game.Population.Culture(culture)?.Name ?? Localisation.GetOrPretty(culture)) : "",
            ["religion"] = religion != null ? (_game.Population.Religion(religion)?.Name ?? Localisation.GetOrPretty(religion)) : "",
            ["province"] = seat.State?.DisplayName ?? c.DisplayName,
            ["term_end"] = seat.TermEnd.ToDisplay(),
            ["trait"] = trait?.Name ?? "",
        };
        bool minority = IsMinorityParty(seat.Party);
        string key = minority ? "parliament_seat_minority" : "parliament_seat_default";
        string template = Localisation.Has(key) ? Localisation.Get(key)
            : minority
                ? "{name} sits for {province} for {party}, speaking for the {culture} {religion} people there. Term ends {term_end}."
                : "{name} sits for {province} for {party}. Elected by {culture} {religion} voters. Term ends {term_end}.";
        var sb = new System.Text.StringBuilder(template);
        foreach (var kv in args) sb.Replace("{" + kv.Key + "}", kv.Value);
        return sb.ToString();
    }

    // ---- monthly upkeep and elections

    /// <summary>Refills seats whose holder died or whose term ended. Called monthly for every country.</summary>
    public void ParliamentMonthly(Country c)
    {
        // only a player's parliament has named members; an AI country's seats are just counts (party, constituency, pop group)
        if (!Enabled || !c.IsPlayer) return;
        var par = ParliamentOf(c);
        if (par.Seats.Count == 0) return;
        var now = _game.World.Date;
        foreach (var seat in par.Seats.ToList())
        {
            bool gone = seat.Character != null ? !seat.Character.Alive : seat.Member == null || MemberDies(c, par, seat, now);
            if (!gone && seat.TermEnd.TotalDays > now.TotalDays) continue;
            if (!gone && SeatRng(c, seat, par.Epoch, "reelect").NextDouble() < Def("parliament_reelect_chance", 0.5))
            {
                seat.TermStart = now;
                seat.TermEnd = now.AddMonths(TermMonths(c, seat).Months);
                continue;
            }
            seat.Draws++;
            seat.CandidateId = "";
            FillSeat(c, par, seat);
        }
    }

    /// <summary>A light record ages like anyone: once a year, in a month fixed by the seat, it rolls the character death chance (the full character gets it from the yearly tick).</summary>
    private bool MemberDies(Country c, CountryParliament par, Seat seat, GameDate now)
    {
        var m = seat.Member!;
        if (Hash(seat.Id, c.Tag) % 12 + 1 != now.Month) return false;
        var rng = new Random(Hash(_game.Seed.ToString(CultureInfo.InvariantCulture), c.Tag, seat.Id, "death", now.Year.ToString(CultureInfo.InvariantCulture)));
        return rng.NextDouble() < _game.Rules.Characters.YearlyDeathChance(_game, Build(c, seat, m));
    }

    /// <summary>A general election: everyone's term ends and every seat is drawn again (incumbents may be re-elected).</summary>
    public void ParliamentElection(Country c)
    {
        if (!Enabled) return;
        var par = ParliamentOf(c);
        par.Epoch++;
        if (!c.IsPlayer) { foreach (var seat in par.Seats) { Leave(seat); seat.CandidateId = ""; seat.Draws = 0; } return; }
        foreach (var seat in par.Seats.ToList())
        {
            bool alive = seat.Character != null ? seat.Character.Alive : seat.Member != null;
            if (alive && SeatRng(c, seat, par.Epoch, "reelect").NextDouble() < Def("parliament_reelect_chance", 0.5))
            {
                seat.TermStart = _game.World.Date;
                seat.TermEnd = seat.TermStart.AddMonths(TermMonths(c, seat).Months);
                continue;
            }
            seat.CandidateId = "";
            seat.Draws = 0;
            FillSeat(c, par, seat);
        }
    }

    // ---- save

    private void SaveParliaments(Node root)
    {
        if (_former.Count > 0)
        {
            var former = root.AddBlock("former");
            foreach (var kv in _former.OrderBy(k => k.Key, StringComparer.Ordinal))
                former.Add("m", $"{kv.Key}|{kv.Value.Age}|{kv.Value.Left.ToScript()}|{kv.Value.Times}", true);
        }
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
                if (s.Character != null) b.Add("holder", s.Character.Key);
                else if (s.Member is { } m) b.Add("member", $"{m.FirstName.Replace('|', '/')}|{m.Family.Replace('|', '/')}|{(m.Female ? 1 : 0)}|{m.BirthDate.ToScript()}|{m.Times}|{string.Concat(m.Stats.Select(v => (char)('0' + Math.Min((int)v, 9))))}|{string.Join(",", m.Traits.Select(t => t.Key))}");
                b.Add("candidate", s.CandidateId, true);
                if (s.Faction != null) b.Add("faction", s.Faction);
                b.Add("start", s.TermStart.ToScript());
                b.Add("end", s.TermEnd.ToScript());
                if (s.Draws != 0) b.Add("draws", s.Draws.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private void LoadParliaments(Node root)
    {
        _former.Clear();
        _retired.Clear();
        _pinned = null;
        foreach (var m in root.Get("former")?.GetAll("m") ?? Enumerable.Empty<Node>())
        {
            var parts = (m.Value ?? "").Split('|');
            if (parts.Length < 5 || !int.TryParse(parts[^3], out var age) || !GameDate.TryParse(parts[^2], out var left) || !int.TryParse(parts[^1], out var times)) continue;
            _former[string.Join("|", parts[..^3])] = new FormerMember(age, left, times);
        }
        foreach (var n in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var par = new CountryParliament { Epoch = n.GetInt("epoch"), Signature = n.GetString("signature", "")! };
            foreach (var b in n.GetAll("seat"))
            {
                Character? holder = null;
                if (b.GetString("holder") is { } hk && !_game.World.CharacterByKey.TryGetValue(hk, out holder)) continue;
                SeatMember? member = b.GetString("member") is { } packed ? ReadMember(packed) : null;
                var seat = new Seat
                {
                    Id = b.GetString("id", "")!,
                    Party = b.GetString("party", "")!,
                    State = b.GetString("state") is { } sk ? _game.World.GetState(sk) : null,
                    PopGroup = b.GetString("group", "")!,
                    Character = holder,
                    Member = member,
                    CandidateId = b.GetString("candidate", "")!,
                    Faction = b.GetString("faction"),
                    Draws = b.GetInt("draws"),
                };
                if (GameDate.TryParse(b.GetString("start"), out var st)) seat.TermStart = st;
                if (GameDate.TryParse(b.GetString("end"), out var en)) seat.TermEnd = en;
                par.Seats.Add(seat);
            }
            _parliaments[c] = par;
        }
    }

    private SeatMember? ReadMember(string packed)
    {
        var p = packed.Split('|');
        if (p.Length != 7 || !GameDate.TryParse(p[3], out var birth) || !int.TryParse(p[4], out var times)) return null;
        return new SeatMember
        {
            FirstName = p[0], Family = p[1], Female = p[2] == "1", BirthDate = birth, Times = Math.Max(1, times),
            Stats = p[5].Select(ch => (byte)Math.Clamp(ch - '0', 0, 9)).ToArray(),
            Traits = p[6].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => _game.Data.Traits.GetValueOrDefault(k)).OfType<Trait>().ToArray(),
        };
    }
}
