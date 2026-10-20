using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Population;

public sealed partial class PopulationSystem
{
    // =============================================================================================
    // Cores and claims (cores_claims feature)
    // =============================================================================================

    /// <summary>Monthly: owners gain cores (their culture lives there, or long ownership), former owners lose them,
    /// and once a year countries claim neighbouring land where their own culture is the majority.</summary>
    private void MonthlyCores()
    {
        var w = _game.World;
        int today = w.Date.TotalDays;
        foreach (var s in w.States)
        {
            var owner = s.Owner;
            if (owner == null || !_states.TryGetValue(s, out var sp)) continue;
            string? major = MajorityCulture(s);
            double owned = (today - sp.OwnedSinceDay) / 365.0;
            if (!s.Cores.Contains(owner) && Rules.CanCore(_game, s, owner, AcceptedShare(s, owner), owned, major != null && major == Of(owner).Culture))
            {
                s.Cores.Add(owner);
                s.Claims.Remove(owner);
                CoresGained++;
                if (owner.IsPlayer) _game.Notify($"{s.DisplayName} is now a core state of ours.", NotificationKind.Good);
            }
            foreach (var former in s.Cores.Where(c => c != owner).ToList())
            {
                bool cultureThere = major != null && Of(former).Culture != null && IsAccepted(former, major);
                if (cultureThere || owned < Rules.CoreLossYears(_game, s, former)) continue;
                s.Cores.Remove(former);
                CoresLost++;
                if (!former.Exists) continue;
                s.Claims.Add(former); // the memory remains as a claim
                if (former.IsPlayer) _game.Notify($"We lost our core on {s.DisplayName}; a claim remains.", NotificationKind.Bad);
            }
        }
        if (w.Date.Month != 1) return;
        // irredentism: claims on neighbouring states (rule ClaimsNeighbour: where our culture is the majority)
        foreach (var c in w.Countries.Where(c => c.Exists))
        {
            var seen = new HashSet<State>();
            foreach (var s in c.OwnedStates)
                foreach (var n in NeighbourStates(s))
                {
                    if (n.Owner == null || n.Owner == c || !seen.Add(n) || n.Cores.Contains(c) || n.Claims.Contains(c)) continue;
                    if (Rules.ClaimsNeighbour(_game, c, n)) { n.Claims.Add(c); ClaimsMade++; }
                }
        }
    }

    /// <summary>Share of a state's people whose culture the country accepts (1 without pops).</summary>
    public double AcceptedShare(State s, Country c)
    {
        if (!_states.TryGetValue(s, out var sp) || sp.Total <= 0) return 1;
        return sp.Pops.Where(p => IsAccepted(c, p.Culture)).Sum(p => p.Size) / sp.Total;
    }

    /// <summary>
    /// A colony (rule IsColony): a state marked <c>category = colony</c>, or (with cores) a state the owner has no core on
    /// that is not connected by land to its capital. Colonies are not assimilated or converted.
    /// </summary>
    public bool IsColony(State s) => Rules.IsColony(_game, s);

    private readonly Dictionary<Country, (int Day, HashSet<State> States)> _home = new();

    /// <summary>The owner's states connected by land to its capital (cached for a month).</summary>
    public HashSet<State> HomeOf(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_home.TryGetValue(c, out var h) && today - h.Day < 30) return h.States;
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start != null && start.Owner == c)
        {
            var q = new Queue<State>();
            result.Add(start);
            q.Enqueue(start);
            while (q.Count > 0)
                foreach (var n in NeighbourStates(q.Dequeue()))
                    if (n.Owner == c && result.Add(n)) q.Enqueue(n);
        }
        _home[c] = (today, result);
        return result;
    }

    /// <summary>States of <paramref name="target"/> next to <paramref name="c"/>'s land whose majority follows c's religion while target does not.</summary>
    public List<State> FellowBelieverStates(Country c, Country target)
    {
        var result = new List<State>();
        if (!ReligionEnabled) return result;
        string? rel = Of(c).Religion;
        if (rel == null || Of(target).Religion == rel) return result;
        var mine = c.OwnedStates.ToHashSet();
        foreach (var s in target.OwnedStates)
            if (!s.Claims.Contains(c) && !s.Cores.Contains(c) && NeighbourStates(s).Any(mine.Contains) && MajorityReligion(s) == rel) result.Add(s);
        return result;
    }

    // =============================================================================================
    // Accepted cultures
    // =============================================================================================

    /// <summary>Cost of accepting a culture: political power with politics, else gold with the economy, else free.</summary>
    public (double Amount, string Currency) AcceptCost(Country c) => Rules.AcceptCultureCost(_game, c);

    public string? CannotAccept(Country c, string culture)
    {
        if (!Enabled || !CultureByKey.ContainsKey(culture)) return "Unknown culture";
        if (IsAccepted(c, culture)) return "Already accepted";
        if (Rules.CannotAcceptCulture(_game, c, culture) is { } why) return why;
        var (cost, cur) = AcceptCost(c);
        if (cost > 0 && (cur == "gold" ? c.Gold : _game.Politics.PowerOf(c)) < cost) return $"Needs {cost:0} {cur}";
        return null;
    }

    public string? AcceptCulture(Country c, string culture)
    {
        var reason = CannotAccept(c, culture);
        if (reason != null) return reason;
        var (cost, cur) = AcceptCost(c);
        if (cost > 0)
        {
            if (cur == "gold") c.Gold -= cost;
            else if (_game.Politics.PowerResource is { } pr) _game.Resources.Set(c, pr, _game.Resources.Get(c, pr) - cost);
        }
        Of(c).Accepted.Add(culture);
        _countryModsDirty = true;
        foreach (var s in c.OwnedStates) if (_states.TryGetValue(s, out var sp)) ApplyOutput(s, sp);
        return null;
    }

    // =============================================================================================
    // Colonization (colonization feature)
    // =============================================================================================

    public int MaxColonies(Country c) => Rules.MaxColonies(_game, c);
    public IEnumerable<State> ColoniesOf(Country c)
    {
        foreach (var s in SettledStates())
            if (s.Owner == null && _states.TryGetValue(s, out var sp) && sp.PartyOf(c) != null) yield return s;
    }

    // States that have (or had) a settler party; rebuilt when any party or the population table changes.
    private State[] _settled = Array.Empty<State>();
    private int _settledVersion = -1, _settledTable = -1;
    private int _tableVersion;

    private State[] SettledStates()
    {
        lock (_states)
        {
            int v = SettlerParty.Version;
            if (v != _settledVersion || _tableVersion != _settledTable)
            {
                _settled = _states.Where(kv => kv.Value.Main.Country != null || kv.Value.Counter.Country != null).Select(kv => kv.Key).ToArray();
                _settledVersion = v; _settledTable = _tableVersion;
            }
            return _settled;
        }
    }
    public double ColonizeCost => Rules.ColonizeCost(_game);

    /// <summary>Why <paramref name="c"/> cannot send a colonist to <paramref name="s"/> (null = it can).</summary>
    public string? CannotColonize(Country c, State s, double knownDistanceKm = double.NaN)
    {
        if (!ColonizationEnabled) return "No colonization in this game";
        if (s.Owner != null) return "Already owned";
        if (!s.Locations.Any(l => l.IsLand && l.IsPassable)) return "No land to settle";
        var sp = Of(s);
        if (sp.PartyOf(c) != null) return "Our colonists are already there";
        if (sp.Main.Country is { Exists: true } first)
        {
            // counter-settlement: a second country may settle the same land; the leader keeps the larger part when it is divided
            if (sp.Counter.Country is { Exists: true } second) return $"{first.Name} and {second.Name} are already settling it";
            if (_game.StateSplits.CannotSplit(s) is { } why) return $"{first.Name} is settling it and it cannot be shared: {why}";
        }
        var region = RegionOf(s);
        if (ColoniesInRegion(c, region) >= MaxColoniesIn(c, region)) return $"All our colonists in {region?.Name ?? "this land"} are busy ({MaxColoniesIn(c, region)})";
        if (ColonizeCost > 0 && c.Gold < ColonizeCost) return $"Needs {ColonizeCost:0} gold";
        // colonies must reach the homeland by land (next to a state connected to the capital; a finished colony joins that chain),
        // unless the country has the ships and the seafaring tradition to settle a coast overseas (Rules.CanColonizeOverseas)
        if (_game.Data.Defines.Get("colony_land_link", 0) > 0)
        {
            var home = HomeOf(c);
            if (!NeighbourStates(s).Any(home.Contains))
            {
                if (!Rules.CanColonizeOverseas(_game, c)) return "Not connected by land to our homeland (next to our land or a connected colony)";
                if (!s.Locations.Any(l => l.IsLand && l.IsCoastal)) return "Overseas colonists need a coast to land on";
                if (!c.OwnedStates.Any(o => o.Locations.Any(l => l.IsLand && l.IsCoastal))) return "No harbor to sail from";
            }
        }
        double range = Rules.ColonyRangeKm(_game, c);
        if ((double.IsNaN(knownDistanceKm) ? DistanceKm(c, s) : knownDistanceKm) > range) return $"Too far from our land (more than {range:0} km)";
        return null;
    }

    /// <summary>Distance from the country's nearest land to the middle of a state.</summary>
    public double DistanceKm(Country c, State s)
    {
        double kmPerPx = _game.Data.Defines.KmPerPixel;
        var (sx, sy) = Center(s);
        double best = double.MaxValue;
        foreach (var o in c.OwnedStates)
            foreach (var l in o.Locations)
            {
                double dx = l.CenterX - sx, dy = l.CenterY - sy;
                double d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
        return best == double.MaxValue ? double.MaxValue : Math.Sqrt(best) * kmPerPx;
    }

    private static (double X, double Y) Center(State s)
    {
        double x = 0, y = 0; int n = 0;
        foreach (var l in s.Locations) { x += l.CenterX; y += l.CenterY; n++; }
        return n == 0 ? (0, 0) : (x / n, y / n);
    }

    public string? Colonize(Country c, State s)
    {
        var reason = CannotColonize(c, s);
        if (reason != null) return reason;
        c.Gold -= ColonizeCost;
        var sp = Of(s);
        bool counter = sp.Main.Country is { Exists: true };
        (counter ? sp.Counter : sp.Main).Start(c);
        string people = NativePeoplesText(s);
        if (counter) _game.NotifyPlayer($"{c.Name} sent colonists to counter-settle {s.DisplayName}, already being settled by {sp.Main.Country!.Name}.{people}", NotificationKind.Info, c);
        else _game.NotifyPlayer($"{c.Name} sent colonists to {s.DisplayName}.{people}", NotificationKind.Info, c);
        if (counter) _game.NotifyPlayer($"{c.Name} is counter-settling {s.DisplayName}, which we are settling.", NotificationKind.Bad, sp.Main.Country!);
        return null;
    }

    /// <summary>Calls the colonists of <paramref name="c"/> back; a rival settling the same land becomes its only settler.</summary>
    public void AbandonColony(State s, Country c)
    {
        var sp = Of(s);
        if (sp.Main.Country == c)
        {
            sp.Main.Country = sp.Counter.Country; sp.Main.Progress = sp.Counter.Progress; sp.Main.Reinforcements = sp.Counter.Reinforcements;
            sp.Counter.Clear();
            if (sp.Main.Country == null) { sp.Main.Progress = 0; sp.Main.Reinforcements = 0; }
        }
        else if (sp.Counter.Country == c) sp.Counter.Clear();
    }

    // ---- the native peoples of a settlement target

    /// <summary>The people living on land that is being settled (or could be), largest first: culture, number of people, and share of the state.</summary>
    public List<(string Culture, double People, double Share)> NativePeoples(State s)
    {
        if (!_states.TryGetValue(s, out var sp) || sp.Total <= 0) return new();
        double total = sp.Total;
        return sp.Pops.GroupBy(p => p.Culture).Select(g => (Culture: CultureByKey.TryGetValue(g.Key, out var cd) ? cd.Name : Localisation.GetOrPretty(g.Key), People: g.Sum(p => p.Size), Share: g.Sum(p => p.Size) / total))
            .OrderByDescending(x => x.People).ThenBy(x => x.Culture, StringComparer.Ordinal).ToList();
    }

    /// <summary>" Natives: Lakota (60%), Crow (30%)." for messages; empty when nobody lives there.</summary>
    public string NativePeoplesText(State s)
    {
        var peoples = NativePeoples(s);
        return peoples.Count == 0 ? "" : " Natives: " + string.Join(", ", peoples.Take(3).Select(x => $"{x.Culture} ({x.Share * 100:0}%)")) + ".";
    }

    /// <summary>The native share of a state's people from the settlers' point of view: everyone who is not of an accepted culture.</summary>
    public double NativeShare(State s, Country c)
    {
        if (!_states.TryGetValue(s, out var sp) || sp.Total <= 0) return 0;
        return sp.Pops.Where(p => !IsAccepted(c, p.Culture)).Sum(p => p.Size) / sp.Total;
    }

    // ---- reinforcements

    public double ReinforceGold(State s, Country c) => Rules.ReinforceCost(_game, s, c);

    /// <summary>Why <paramref name="c"/> cannot send more settlers to <paramref name="s"/> (null = it can).</summary>
    public string? CannotReinforce(Country c, State s)
    {
        if (!ColonizationEnabled) return "No colonization in this game";
        if (s.Owner != null || !_states.TryGetValue(s, out var sp) || sp.PartyOf(c) is not { } party) return "We are not settling it";
        int max = Rules.MaxReinforcements(_game, s, c);
        if (party.Reinforcements >= max) return $"Already guarded by {max} waves";
        double cost = ReinforceGold(s, c);
        if (cost > 0 && c.Gold < cost) return $"Needs {cost:0} gold";
        return null;
    }

    /// <summary>Pays gold to send another wave of settlers: it adds progress at once and stands guard, and the next native attack that would hurt the colonists may be stopped by it.</summary>
    public string? Reinforce(Country c, State s)
    {
        var reason = CannotReinforce(c, s);
        if (reason != null) return reason;
        c.Gold -= ReinforceGold(s, c);
        var party = Of(s).PartyOf(c)!;
        party.Reinforcements++;
        party.Progress = Math.Min(99, party.Progress + Rules.ReinforceProgress(_game, s, c));
        _game.NotifyPlayer($"{c.Name} sent more settlers to {s.DisplayName}.", NotificationKind.Info, c);
        return null;
    }

    private void MonthlyColonies()
    {
        foreach (var (s, sp) in _states.ToList())
        {
            if (sp.Main.Country == null && sp.Counter.Country == null) continue;
            if (s.Owner != null) { sp.Main.Clear(); sp.Counter.Clear(); continue; }
            foreach (var party in new[] { sp.Main, sp.Counter })
                if (party.Country is { } pc && !pc.Exists) AbandonColony(s, pc);
            foreach (var party in new[] { sp.Main, sp.Counter })
                if (party.Country is { } c) Grow(s, sp, party, c);
            // the first side to finish settles the land; a rival still settling it gets the smaller part
            var done = sp.Main.Country != null && sp.Main.Progress >= 100 ? sp.Main : sp.Counter.Country != null && sp.Counter.Progress >= 100 ? sp.Counter : null;
            if (done != null) Resolve(s, sp, done);
        }
    }

    /// <summary>One month of one settler party: upkeep, progress, and the natives' answer (a guarding wave may stop an attack and is spent).</summary>
    private void Grow(State s, StatePopulation sp, SettlerParty party, Country c)
    {
        c.Gold -= Rules.ColonyUpkeep(_game, s, c);
        double natives = NativeShare(s, c);
        party.Progress += Rules.ColonyProgress(_game, s, c, natives);
        double setback = Rules.NativeAttack(_game, s, c, natives);
        if (setback <= 0) return;
        string people = NativePeoples(s) is { Count: > 0 } l ? l[0].Culture : "Natives";
        if (party.Reinforcements > 0 && _game.Rngs.Population.NextDouble() < Math.Clamp(Rules.ReinforceGuard(_game, s, c) * party.Reinforcements, 0, 1))
        {
            party.Reinforcements--;
            _game.NotifyPlayer($"Reinforcements drove off the {people} at {s.DisplayName}.", NotificationKind.Good, c);
            return;
        }
        party.Progress = Math.Max(0, party.Progress - setback);
        _game.NotifyPlayer($"The {people} attacked our colonists in {s.DisplayName}.", NotificationKind.Bad, c);
    }

    /// <summary>Settles the state for the finished party; when a rival is counter-settling it, the land is divided first: the leader keeps the larger share.</summary>
    private void Resolve(State s, StatePopulation sp, SettlerParty done)
    {
        var winner = done.Country!;
        var other = done == sp.Main ? sp.Counter : sp.Main;
        var rival = other.Country;
        double winnerProgress = done.Progress, rivalProgress = other.Progress;
        sp.Main.Clear(); sp.Counter.Clear();
        State? rivalState = null;
        if (rival != null)
        {
            double mainShare = Rules.CounterSettleMainShare(_game, s, winnerProgress, rivalProgress);
            var (rx, ry) = NearestLand(rival, s);
            rivalState = _game.StateSplits.Split(s, 1 - mainShare, (rx, ry), $"{s.DisplayName} ({rival.Name})");
            if (rivalState == null) _game.NotifyPlayer($"{s.DisplayName} could not be divided: {winner.Name} took all of it.", NotificationKind.Bad, rival);
        }
        Settle(s, winner);
        if (rival != null && rivalState != null)
        {
            Settle(rivalState, rival);
            double share = _game.StateSplits.Splits[^1].Share;
            _game.NotifyPlayer($"{s.DisplayName} was divided: {winner.Name} {(1 - share) * 100:0}% and {rival.Name} {share * 100:0}%.", NotificationKind.Info, winner);
            _game.NotifyPlayer($"Our counter-settlement won {rivalState.DisplayName} ({share * 100:0}% of {s.DisplayName}).", NotificationKind.Good, rival);
        }
    }

    /// <summary>Where the country's land is nearest to the middle of the state (the side a counter-settlement is carved from).</summary>
    private (double X, double Y) NearestLand(Country c, State s)
    {
        var (sx, sy) = Center(s);
        double best = double.MaxValue; (double X, double Y) at = (sx, sy);
        foreach (var o in c.OwnedStates)
            foreach (var l in o.Locations)
            {
                double dx = l.CenterX - sx, dy = l.CenterY - sy, d = dx * dx + dy * dy;
                if (d < best) { best = d; at = (l.CenterX, l.CenterY); }
            }
        return at;
    }

    /// <summary>The state becomes a colony of <paramref name="c"/>.</summary>
    private void Settle(State s, Country c)
    {
        var sp = Of(s);
        sp.Main.Clear(); sp.Counter.Clear();
        _game.Diplomacy.TransferState(s, c);
        s.Category = "colony";
        _home.Remove(c); // the new colony extends the land connected to the capital
        var cp = Of(c);
        double settlers = Rules.ColonySettlers(_game, s, c);
        if (cp.Culture != null && settlers > 0)
            sp.Pops.Add(new Pop { Culture = cp.Culture, Religion = ReligionEnabled ? cp.Religion : null, Type = ClassesEnabled ? PopTypes.OrderByDescending(t => t.Share).First().Key : null, Size = settlers });
        if (sp.InitialTotal <= 0) sp.InitialTotal = sp.Total;
        var (colonyTax, colonyManpower) = Rules.ColonyBaseOutput(_game, s, c);
        if (sp.BaseTax <= 0) sp.BaseTax = colonyTax;
        if (sp.BaseManpower <= 0) sp.BaseManpower = colonyManpower;
        sp.Owner = c;
        sp.OwnedSinceDay = _game.World.Date.TotalDays;
        ColoniesFounded++;
        ApplyOutput(s, sp);
        if (_game.World.Player == null) _game.Notify($"{s.DisplayName} is now a colony of {c.Name}.", NotificationKind.Info, s.Locations.FirstOrDefault(l => l.IsLand)); // an observer sees the AI settle
        else _game.NotifyPlayer($"{s.DisplayName} is now a colony of {c.Name}.", NotificationKind.Good, c);
        _game.Events.FireOnAction("on_colony_founded", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["state"] = s });
        CheckColonialNation(c, s);
    }

    private List<(State State, double X, double Y)> _free = new();
    private long _freeStamp = long.MinValue;
    private int _freeVersion = -1;

    /// <summary>The states nobody owns and their middles, counted once per day and map change (every AI country looks at them).</summary>
    private List<(State State, double X, double Y)> FreeStates()
    {
        long stamp = _game.World.Date.TotalDays;
        lock (_free)
        {
            if (stamp != _freeStamp || _freeVersion != _game.MapVersion)
            {
                var list = new List<(State, double, double)>();
                foreach (var s in _game.World.States)
                    if (s.Owner == null) { var (x, y) = Center(s); list.Add((s, x, y)); }
                _free = list; _freeStamp = stamp; _freeVersion = _game.MapVersion;
            }
            return _free;
        }
    }

    private void AiColonize(Country c)
    {
        if (!ColonizationEnabled) return;
        AiReinforce(c);
        if (ColonizeCost > 0 && c.Gold < ColonizeCost * 3) return;
        State? best = null;
        double bestScore = double.NegativeInfinity;
        // nothing beyond the colony range of the land's bounding box can be settled: skip those states before the dearer checks (hundreds of countries each look at every free state)
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var px = new List<double>(); var py = new List<double>();
        foreach (var o in c.OwnedStates)
            foreach (var l in o.Locations)
            {
                px.Add(l.CenterX); py.Add(l.CenterY);
                if (l.CenterX < minX) minX = l.CenterX; if (l.CenterX > maxX) maxX = l.CenterX;
                if (l.CenterY < minY) minY = l.CenterY; if (l.CenterY > maxY) maxY = l.CenterY;
            }
        if (minX == double.MaxValue) return;
        double rangeKm = Rules.ColonyRangeKm(_game, c), rangePx = rangeKm / _game.Data.Defines.KmPerPixel;
        // the free states within range of the bounding box, nearest first; only the nearest few hundred are looked at in full (a big empire has thousands in its box)
        long tA = System.Diagnostics.Stopwatch.GetTimestamp();
        var near = new List<(double Gap, State State)>();
        foreach (var (s, cx, cy) in FreeStates())
        {
            if (s.Owner != null) continue;
            double dx = cx < minX ? minX - cx : cx > maxX ? cx - maxX : 0, dy = cy < minY ? minY - cy : cy > maxY ? cy - maxY : 0;
            double gap = dx * dx + dy * dy;
            if (gap > rangePx * rangePx) continue;
            near.Add((gap, s));
        }
        if (near.Count > 200)
        {
            near.Sort((a, b) => { int g = a.Gap.CompareTo(b.Gap); return g != 0 ? g : a.State.Index.CompareTo(b.State.Index); });
            near.RemoveRange(200, near.Count - 200);
        }
        long tB = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_game.Profiling) _game.ProfileElapsed("Col.AScan", tB - tA);
        foreach (var (_, s) in near)
        {
            var (sx, sy) = Center(s);
            double best2 = double.MaxValue;
            for (int i = 0; i < px.Count; i++)
            {
                double dx = px[i] - sx, dy = py[i] - sy;
                double d = dx * dx + dy * dy;
                if (d < best2) best2 = d;
            }
            double distKm = best2 == double.MaxValue ? double.MaxValue : Math.Sqrt(best2) * _game.Data.Defines.KmPerPixel;
            if (distKm > rangeKm) continue;   // (the same test CannotColonize ends with, before its dearer ones)
            bool contested = _states.TryGetValue(s, out var sp) && sp.Main.Country != null;
            if (CannotColonize(c, s, distKm) != null) continue;
            double score = Rules.AiColonyScore(_game, c, s) - (contested ? Rules.AiCounterSettlePenalty(_game, c, s) : 0);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        if (_game.Profiling) _game.ProfileElapsed("Col.Cand", System.Diagnostics.Stopwatch.GetTimestamp() - tB);
        if (best != null && bestScore >= Rules.AiColonyMinScore(_game, c)) Colonize(c, best);
    }

    /// <summary>The AI sends more settlers where natives are many and the colony is not nearly done, if it can pay several waves.</summary>
    private void AiReinforce(Country c)
    {
        foreach (var s in ColoniesOf(c).OrderBy(x => x.Index).ToList())
        {
            var party = Of(s).PartyOf(c)!;
            if (party.Progress >= 80 || NativeShare(s, c) < 0.25) continue;
            double cost = ReinforceGold(s, c);
            if (cost > 0 && c.Gold < cost * 4) continue;
            Reinforce(c, s);
        }
    }

    // =============================================================================================
    // Buildings bought with gold (games without production)
    // =============================================================================================

    public double GoldCost(BuildingType b) => b.Stats.GetValueOrDefault("gold_cost");
    public int BuildMonths(BuildingType b) => Math.Max(1, (int)Math.Round(b.Stats.GetValueOrDefault("build_months", 12)));

    /// <summary>Buildings that can be bought with gold (they have gold_cost) - only without the production feature.</summary>
    public IEnumerable<BuildingType> GoldBuildings()
        => !_game.Features.Has(FeatureSet.Economy)
            ? Enumerable.Empty<BuildingType>()
            : _game.Data.BuildingTypes.Values.Where(b => GoldCost(b) > 0 && b.Active(_game.World.Date)
                && (!_game.Features.Has(FeatureSet.Production) || b.Category != "factory"));

    public string? CannotGoldBuild(Country c, BuildingType b, State s)
    {
        if (!Enabled) return "Needs the population feature";
        if (_game.Features.Has(FeatureSet.Production) && b.Category == "factory" && _game.Production.CivilianFactories(c) > 0)
            return "Built with factories (Production menu)";
        if (!_game.Features.Has(FeatureSet.Economy)) return "Needs the economy feature";
        if (GoldCost(b) <= 0) return "Cannot be bought with gold";
        if (!b.Active(_game.World.Date)) return "This building belongs to another era";
        if (s.Owner != c) return "You must own the state";
        if (s.ControlledShare(c) < 0.5) return "Most of the state is occupied";
        int level = s.Buildings.GetValueOrDefault(b.Key) + GoldProjects.Count(p => p.State == s && p.Building == b);
        if (level >= b.MaxLevel) return $"Maximum level ({b.MaxLevel})";
        if (b.Potential != null && !_game.Script.EvalTrigger(b.Potential, _game.Context(s))) return "Not possible in this state";
        if (c.Gold < GoldCost(b)) return $"Needs {GoldCost(b):0} gold";
        return null;
    }

    public string? GoldBuild(Country c, BuildingType b, State s)
    {
        var reason = CannotGoldBuild(c, b, s);
        if (reason != null) return reason;
        c.Gold -= GoldCost(b);
        int months = BuildMonths(b);
        GoldProjects.Add(new GoldProject { Country = c, State = s, Building = b, MonthsLeft = months, TotalMonths = months });
        return null;
    }

    private void MonthlyGoldProjects()
    {
        foreach (var p in GoldProjects.ToList())
        {
            if (p.State.Owner != p.Country) { GoldProjects.Remove(p); continue; }
            if (--p.MonthsLeft > 0) continue;
            GoldProjects.Remove(p);
            p.State.Buildings[p.Building.Key] = Math.Min(p.Building.MaxLevel, p.State.Buildings.GetValueOrDefault(p.Building.Key) + 1);
            BuildingsBuilt++;
            _countryModsDirty = true;
            if (p.Country.IsPlayer) _game.Notify($"{p.Building.Name} finished in {p.State.DisplayName}.", NotificationKind.Good);
        }
    }

    private void AiBuild(Country c)
    {
        if (_game.Features.Has(FeatureSet.Commodities)) return; // TradeNetworkSystem owns shortage-driven construction.
        var options = GoldBuildings().ToList();
        if (options.Count == 0 || GoldProjects.Count(p => p.Country == c) >= 2) return;
        double reserve = 100;
        // the most populous states get buildings first
        foreach (var s in c.OwnedStates.OrderByDescending(TotalOf).Take(5))
            foreach (var b in options.OrderBy(GoldCost))
            {
                if (c.Gold < GoldCost(b) + reserve || CannotGoldBuild(c, b, s) != null) continue;
                if (_game.Rngs.Population.NextDouble() < 0.5) continue;
                GoldBuild(c, b, s);
                return;
            }
    }

    /// <summary>Production games: now and then the AI queues a building that helps its people (modern buildings).</summary>
    private void AiProductionBuild(Country c)
    {
        if (!_game.Features.Has(FeatureSet.Production) || c.Construction.Count >= 4 || _game.Rngs.Population.NextDouble() > 0.25) return;
        var options = _game.Data.BuildingTypes.Values.Where(b => _buildingMods.ContainsKey(b.Key) || _buildingCountryMods.ContainsKey(b.Key)).ToList();
        if (options.Count == 0) return;
        foreach (var s in c.OwnedStates.OrderByDescending(TotalOf).Take(3))
        {
            var b = options[_game.Rngs.Population.Next(options.Count)];
            if (_game.Production.QueueBuilding(c, b, s) == null) return;
        }
    }

    /// <summary>AI: colonies and buildings (monthly).</summary>
    public void AiMonthly(Country c)
    {
        _aiCalled = true;
        if (!Enabled || c.IsPlayer || !c.Exists) return;
        _game.P("Pop.AiColonize", c.Tag, () => AiColonize(c));
        _game.P("Pop.AiIndependence", c.Tag, () => AiIndependence(c));
        _game.P("Pop.AiBuild", c.Tag, () => AiBuild(c));
        _game.P("Pop.AiProduction", c.Tag, () => AiProductionBuild(c));
    }

    private bool _aiCalled;
    private int _monthsTicked;

    /// <summary>Runs the AI part itself when a project's backend AI.cs predates this feature (never calls AiMonthly).</summary>
    public void AiFallback()
    {
        if (!Enabled) return;
        if (!_aiCalled && _monthsTicked > 0 && _game.Features.Has(FeatureSet.Ai) && _game.Ai.Enabled)
            foreach (var c in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer).ToList()) AiMonthly(c);
        _aiCalled = false;
        _monthsTicked++;
    }
}
