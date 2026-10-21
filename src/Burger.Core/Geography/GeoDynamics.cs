using Burger.Core.Model;

namespace Burger.Core.Geography;

/// <summary>Tuning of the runtime province system. Defaults suit any project; a geography file can override them in its <c>settings</c> block.</summary>
public sealed class GeoDynamicSettings
{
    /// <summary><c>dynamic = no</c> keeps the provinces exactly as the data files define them.</summary>
    public bool Enabled = true;
    /// <summary>A province holding more people than this (and at least two locations) splits in two, balanced by people.</summary>
    public double SplitPopulation = 400000;
    /// <summary>Two siblings merge again when together they hold at most this fraction of <see cref="SplitPopulation"/> (well under the split level, so no flip-flopping).</summary>
    public double MergeCeiling = 0.6;
    /// <summary>A province born from growth cannot change again for this many days.</summary>
    public int MinAgeDays = 3650;
    /// <summary>A front-line province cannot change again for this many days.</summary>
    public int FrontMinAgeDays = 90;
    public int MaxChangesPerMonth = 40;
}

/// <summary>
/// Provinces that react to the simulation. Once a month, for every project with a geography layer:
/// <list type="bullet">
/// <item>front line: a province whose locations are held by different countries (a war occupied part of it) splits along the front, one child per holder and connected piece;</item>
/// <item>growth: a province that grew past <see cref="GeoDynamicSettings.SplitPopulation"/> splits in two balanced halves;</item>
/// <item>consolidation: provinces born from those rules merge back when the front disappears (same holder again) or the people dwindled.</item>
/// </list>
/// Every change is a normal runtime operation (lineage, conservation, saved with the game). Ids are new each time; nothing is read from a data file.
/// The values here (people of a province) are estimates: the people of a state spread over its land by area.
/// </summary>
public sealed class GeoDynamics
{
    private const string FrontReason = "dynamic: front line";
    private const string GrowthReason = "dynamic: population growth";
    private const string FrontMergeReason = "dynamic: front line reunited";
    private const string ShiftReason = "dynamic: border shift";
    private const string GrowthMergeReason = "dynamic: consolidation";

    private readonly Game _g;
    private double[]? _area;
    private int _idDay = int.MinValue, _idN;
    private int _syncKey = int.MinValue;

    private HashSet<string>? _baseline;
    private static string Sig(GeoProvince p) => string.Join(",", p.Atoms.OrderBy(a => a, StringComparer.Ordinal));

    /// <summary>Remembers the province layout now (the first monthly tick or debug view does it), to tell later what really changed.</summary>
    public void CaptureBaseline()
    {
        if (_baseline != null || !_g.Geo.Enabled) return;
        // the layout just before the first dynamic change (so a late capture, or one after a load, still shows the true start)
        var ops = _g.Geo.DynamicOperations;
        int d0 = ops.Count > 0 ? ops.Min(o => o.Date) - 1 : _g.World.Date.TotalDays;
        var provs = _g.Geo.Snapshot(d0).Provinces;
        _baseline = new HashSet<string>(provs.Select(Sig), StringComparer.Ordinal);
        _baseAtom = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < provs.Count; i++) foreach (var a in provs[i].Atoms) _baseAtom[a] = i + 1;
    }

    private Dictionary<string, int>? _baseAtom;

    /// <summary>Number (1-based) of the start-layout province this atom belonged to, 0 if none; for the initial-shapes overlay.</summary>
    public int BaselineIdOf(string atom) { CaptureBaseline(); return _baseAtom != null && _baseAtom.TryGetValue(atom, out int i) ? i : 0; }

    /// <summary>True when this province has a shape the layout did not have at the start (a province made by a split that was later reunited is not changed).</summary>
    public bool IsChanged(GeoProvince p) { CaptureBaseline(); return !_baseline!.Contains(Sig(p)); }

    /// <summary>Provinces now whose shape differs from the start.</summary>
    public int ChangedCount() { CaptureBaseline(); return _g.Geo.Snapshot(_g.World.Date.TotalDays).Provinces.Count(p => !_baseline!.Contains(Sig(p))); }

    public int Splits { get; private set; }
    public int Merges { get; private set; }
    public int Shifts { get; private set; }

    public GeoDynamics(Game g) { _g = g; }

    /// <summary>One line on why growth splits do or do not happen right now (for debugging).</summary>
    public string Diagnose()
    {
        var cfg = _g.Geo.DynamicSettings;
        int day = _g.World.Date.TotalDays;
        var pop = PopulationByLocation();
        if (pop == null) return "growth splits off: the project has no population feature";
        var ps = _g.Geo.Snapshot(day).Provinces;
        int multi = 0, over = 0, young = 0;
        double max = 0;
        var byKey = _g.World.LocationByKey;
        foreach (var p in ps)
        {
            if (p.Atoms.Length < 2 || p.Archipelago) continue;
            multi++;
            double t = 0; foreach (var a in p.Atoms) if (byKey.TryGetValue(a, out var l)) t += pop[l.Index];
            max = Math.Max(max, t);
            if (t > cfg.SplitPopulation) { over++; if (Young(p, day, cfg.MinAgeDays)) young++; }
        }
        double total = pop.Sum();
        return $"{ps.Count} provinces, {multi} with 2+ locations; world population {total:N0}; biggest multi-location province {max:N0}; split level {cfg.SplitPopulation:N0}; over it: {over} ({young} too young)";
    }

    /// <summary>Forget what was synced (after a save is loaded) so owners and controllers are recomputed.</summary>
    public void Invalidate() => _syncKey = int.MinValue;

    /// <summary>Only provinces the system made wait before changing again; those from the data files may split from the first month.</summary>
    private static bool Young(GeoProvince p, int day, int minAge) => p.Reason.StartsWith("dynamic:", StringComparison.Ordinal) && day - p.From < minAge;

    private double[] Areas => _area ??= GeoMetrics.LocationAreasKm2(_g.World);

    private static string Holder(Location l) => (l.Controller ?? l.Owner)?.Tag ?? "";

    public void MonthlyTick()
    {
        var reg = _g.Geo;
        var cfg = reg.DynamicSettings;
        if (!cfg.Enabled || !reg.Enabled) return;
        CaptureBaseline();
        int day = _g.World.Date.TotalDays;
        int budget = cfg.MaxChangesPerMonth;
        var pop = PopulationByLocation();
        var provinces = reg.Snapshot(day).Provinces.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        // history written in the data files wins: a province with authored events still to come is left alone
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in reg.ExplicitValues) if (e.Day > day) touched.Add(e.Subject);
        foreach (var o in reg.Operations) if (o.Date > day) foreach (var pid in o.Parents) touched.Add(pid);

        foreach (var p in provinces)
        {
            if (budget <= 0) break;
            if (touched.Contains(p.Id) || day - p.From < cfg.FrontMinAgeDays) continue;
            if (TryShift(p, day, touched)) { budget--; continue; }
        }
        foreach (var p in provinces)
        {
            if (budget <= 0) break;
            if (touched.Contains(p.Id) || day - p.From < cfg.FrontMinAgeDays) continue;
            if (TryFrontSplit(p, day)) { budget--; touched.Add(p.Id); }
        }
        if (pop != null)
            foreach (var p in provinces)
            {
                if (budget <= 0) break;
                if (touched.Contains(p.Id) || Young(p, day, cfg.MinAgeDays)) continue;
                if (TryGrowthSplit(p, day, pop, cfg)) { budget--; touched.Add(p.Id); }
            }
        foreach (var p in provinces)
        {
            if (budget <= 0) break;
            if (touched.Contains(p.Id) || !p.Reason.StartsWith("dynamic:", StringComparison.Ordinal) || p.Reason == ShiftReason) continue;
            int minAge = p.Reason == GrowthReason || p.Reason == GrowthMergeReason ? cfg.MinAgeDays : cfg.FrontMinAgeDays;
            if (day - p.From < minAge) continue;
            if (TryMerge(p, day, pop, cfg, touched)) budget--;
        }
        Sync(day);
    }

    /// <summary>Makes the owner and controller of every province follow the game (a province is held by whoever holds most of its land).</summary>
    public void EnsureSynced()
    {
        var d = _g.World.Date;
        int key = d.Year * 12 + d.Month;
        if (key != _syncKey && _g.Geo.Enabled) Sync(d.TotalDays);
    }

    private void Sync(int day)
    {
        var d = _g.World.Date;
        _syncKey = d.Year * 12 + d.Month;
        var reg = _g.Geo;
        var areas = Areas;
        var byKey = _g.World.LocationByKey;
        foreach (var p in reg.Snapshot(day).Provinces)
        {
            string owner = Majority(p, byKey, areas, l => l.Owner?.Tag ?? "");
            string holder = Majority(p, byKey, areas, Holder);
            if (owner.Length > 0 && reg.Owner(p.Id, day) != owner) reg.SetDerived(p.Id, "owner", day, owner);
            if (holder.Length > 0 && reg.Value(p.Id, "controller", day) != holder) reg.SetDerived(p.Id, "controller", day, holder);
        }
    }

    private static string Majority(GeoProvince p, Dictionary<string, Location> byKey, double[] areas, Func<Location, string> key)
    {
        var w = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in p.Atoms)
            if (byKey.TryGetValue(a, out var l)) { var k = key(l); w[k] = w.GetValueOrDefault(k) + Math.Max(areas[l.Index], 1e-6); }
        if (w.Count == 0) return "";
        return w.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>People per location (estimate); null when the project has no population system.</summary>
    private double[]? PopulationByLocation()
    {
        if (!_g.Population.Enabled) return null;
        var pop = new double[_g.World.Locations.Count];
        var areas = Areas;
        foreach (var s in _g.World.States)
        {
            double total = _g.Population.TotalOf(s);
            if (total <= 0) continue;
            double land = s.Locations.Where(l => l.IsLand).Sum(l => areas[l.Index]);
            if (land <= 0) continue;
            foreach (var l in s.Locations) if (l.IsLand) pop[l.Index] = total * areas[l.Index] / land;
        }
        return pop;
    }

    private List<Location> Locations(GeoProvince p)
    {
        var byKey = _g.World.LocationByKey;
        var list = new List<Location>(p.Atoms.Length);
        foreach (var a in p.Atoms) if (byKey.TryGetValue(a, out var l)) list.Add(l);
        return list;
    }

    private string NewId(int day)
    {
        if (day != _idDay) { _idDay = day; _idN = 0; }
        string id;
        do { id = $"dyn{(day < 0 ? "n" : "")}{Math.Abs(day)}_{++_idN}"; } while (_g.Geo.Get(id) != null);
        return id;
    }

    private static string BaseName(string? name) { name ??= ""; int i = name.LastIndexOf(" (", StringComparison.Ordinal); return i > 0 && name.EndsWith(')') ? name[..i] : name; }
    private string NameOf(GeoProvince p, int day) => _g.Geo.Name(p.Id, day) ?? p.Id;

    private bool Apply(GeoOperation op)
    {
        var err = _g.Geo.ApplyRuntime(op);
        if (err != null) { Log.Warn("Geography: dynamic change refused: " + err); return false; }
        if (op.Kind == "split") Splits++; else if (op.Kind == "merge") Merges++; else Shifts++;
        return true;
    }

    // ------------------------------------------------------------------ front line
    private bool TryFrontSplit(GeoProvince p, int day)
    {
        if (p.Atoms.Length < 2) return false;
        var locs = Locations(p);
        var groups = locs.GroupBy(Holder).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (groups.Count < 2) return false;
        var pieces = new List<(string Holder, List<Location> Locs)>();
        foreach (var g in groups)
        {
            if (p.Archipelago) pieces.Add((g.Key, g.ToList()));
            else foreach (var c in Components(g)) pieces.Add((g.Key, c));
        }
        if (pieces.Count < 2 || pieces.Count > 6) return false;
        string baseName = BaseName(NameOf(p, day));
        var op = new GeoOperation { Date = day, Parents = new[] { p.Id }, Reason = FrontReason, Source = "dynamic" };
        foreach (var grp in pieces.GroupBy(x => x.Holder))
        {
            int i = 0, n = grp.Count();
            foreach (var piece in grp)
            {
                i++;
                string tag = piece.Holder.Length > 0 ? piece.Holder : "unheld";
                op.Children.Add(new GeoChildSpec
                {
                    Id = NewId(day), Atoms = piece.Locs.Select(l => l.Key).ToArray(), Archipelago = p.Archipelago,
                    Name = $"{baseName} ({tag}{(n > 1 ? " " + i : "")})",
                });
            }
        }
        return Apply(op);
    }

    // ------------------------------------------------------------------ border shift
    /// <summary>
    /// A province holds land that belongs to the holder of a neighboring province: that land changes province.
    /// The donor loses those locations and the neighbor gains them (pixels change hands, nothing new is cut).
    /// Only a small, border-hugging piece moves; bigger or detached pieces are left for a front split.
    /// </summary>
    private bool TryShift(GeoProvince p, int day, HashSet<string> touched)
    {
        if (p.Atoms.Length < 3 || p.Archipelago) return false;
        var reg = _g.Geo;
        var byKey = _g.World.LocationByKey;
        var areas = Areas;
        string home = Majority(p, byKey, areas, Holder);
        var locs = Locations(p);
        var foreign = locs.Where(l => Holder(l) != home && Holder(l).Length > 0).ToList();
        if (foreign.Count == 0) return false;
        double total = locs.Sum(l => areas[l.Index]);
        var inP = new HashSet<Location>(locs);
        GeoProvince? bestQ = null; List<Location>? bestMove = null;
        var tried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in foreign.OrderBy(x => x.Key, StringComparer.Ordinal))
            foreach (var a in l.Neighbors)
            {
                if (!RealBorder(a) || inP.Contains(a.To)) continue;
                var q = reg.ProvinceOfAtom(a.To.Key, day);
                if (q == null || q == p || q.Archipelago || touched.Contains(q.Id) || !tried.Add(q.Id)) continue;
                if (day - q.From < _g.Geo.DynamicSettings.FrontMinAgeDays) continue;
                string qh = Majority(q, byKey, areas, Holder);
                if (qh.Length == 0 || qh == home) continue;
                var qIn = new HashSet<Location>(Locations(q));
                var move = foreign.Where(x => Holder(x) == qh && x.Neighbors.Any(n => RealBorder(n) && qIn.Contains(n.To))).ToList();
                if (move.Count == 0 || move.Count >= locs.Count) continue;
                if (move.Sum(x => areas[x.Index]) > 0.5 * total) continue;
                var rest = locs.Except(move).ToList();
                if (Components(rest).Count != 1) continue;
                if (bestMove == null || move.Count > bestMove.Count) { bestQ = q; bestMove = move; }
            }
        if (bestQ == null || bestMove == null) return false;
        var moved = new HashSet<string>(bestMove.Select(m => m.Key), StringComparer.Ordinal);
        var op = new GeoOperation { Date = day, Parents = new[] { p.Id, bestQ.Id }, Reason = ShiftReason, Source = "dynamic" };
        op.Children.Add(new GeoChildSpec { Id = NewId(day), Atoms = p.Atoms.Where(x => !moved.Contains(x)).ToArray(), Name = NameOf(p, day) });
        op.Children.Add(new GeoChildSpec { Id = NewId(day), Atoms = bestQ.Atoms.Concat(moved).ToArray(), Archipelago = bestQ.Archipelago, Name = NameOf(bestQ, day) });
        if (!Apply(op)) return false;
        touched.Add(p.Id); touched.Add(bestQ.Id);
        return true;
    }

    // ------------------------------------------------------------------ growth
    private bool TryGrowthSplit(GeoProvince p, int day, double[] pop, GeoDynamicSettings cfg)
    {
        if (p.Atoms.Length < 2 || p.Archipelago) return false;
        var locs = Locations(p).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        double total = locs.Sum(l => pop[l.Index]);
        if (total <= cfg.SplitPopulation) return false;
        if (locs.Any(l => Holder(l) != Holder(locs[0]))) return false; // a front split comes first
        // two seeds: the location farthest from the centre, and the one farthest from that
        double cx = locs.Average(l => (double)l.CenterX), cy = locs.Average(l => (double)l.CenterY);
        double Dist(Location a, double x, double y) => (a.CenterX - x) * (a.CenterX - x) + (a.CenterY - y) * (a.CenterY - y);
        var s0 = locs.OrderByDescending(l => Dist(l, cx, cy)).ThenBy(l => l.Key, StringComparer.Ordinal).First();
        var s1 = locs.OrderByDescending(l => Dist(l, s0.CenterX, s0.CenterY)).ThenBy(l => l.Key, StringComparer.Ordinal).First();
        if (s0 == s1) return false;
        var regions = new[] { new List<Location> { s0 }, new List<Location> { s1 } };
        var mass = new[] { pop[s0.Index] + 1, pop[s1.Index] + 1 };
        var inP = new HashSet<Location>(locs);
        var assigned = new HashSet<Location> { s0, s1 };
        while (assigned.Count < locs.Count)
        {
            int r = mass[0] <= mass[1] ? 0 : 1;
            Location? next = Frontier(regions[r], inP, assigned);
            if (next == null) { r = 1 - r; next = Frontier(regions[r], inP, assigned); }
            if (next == null) return false; // not connected by real borders; leave it alone
            regions[r].Add(next); assigned.Add(next); mass[r] += pop[next.Index];
        }
        double share = Math.Min(mass[0], mass[1]) / (mass[0] + mass[1]);
        if (share < 0.2) return false;
        string baseName = BaseName(NameOf(p, day));
        double dLon = Math.Abs(regions[0].Average(l => (double)l.CenterX) - regions[1].Average(l => (double)l.CenterX));
        double dLat = Math.Abs(regions[0].Average(l => (double)l.CenterY) - regions[1].Average(l => (double)l.CenterY));
        // image y grows southwards, x eastwards
        int a = dLat > dLon ? (regions[0].Average(l => (double)l.CenterY) < regions[1].Average(l => (double)l.CenterY) ? 0 : 1)
                            : (regions[0].Average(l => (double)l.CenterX) > regions[1].Average(l => (double)l.CenterX) ? 0 : 1);
        string[] names = dLat > dLon ? new[] { "North", "South" } : new[] { "West", "East" };
        // region a is the northern / western one when a == 0 here
        var order = dLat > dLon ? (a == 0 ? new[] { 0, 1 } : new[] { 1, 0 }) : (a == 0 ? new[] { 1, 0 } : new[] { 0, 1 });
        var op = new GeoOperation { Date = day, Parents = new[] { p.Id }, Reason = GrowthReason, Source = "dynamic" };
        for (int i = 0; i < 2; i++)
            op.Children.Add(new GeoChildSpec { Id = NewId(day), Atoms = regions[order[i]].Select(l => l.Key).ToArray(), Name = $"{baseName} ({names[i]})" });
        return Apply(op);
    }

    private static Location? Frontier(List<Location> region, HashSet<Location> inProvince, HashSet<Location> assigned)
    {
        Location? best = null;
        foreach (var l in region)
            foreach (var a in l.Neighbors)
            {
                if (!RealBorder(a) || !inProvince.Contains(a.To) || assigned.Contains(a.To)) continue;
                if (best == null || string.CompareOrdinal(a.To.Key, best.Key) < 0) best = a.To;
            }
        return best;
    }

    // ------------------------------------------------------------------ consolidation
    private bool TryMerge(GeoProvince p, int day, double[]? pop, GeoDynamicSettings cfg, HashSet<string> touched)
    {
        var reg = _g.Geo;
        bool front = p.Reason == FrontReason || p.Reason == FrontMergeReason;
        var mine = Locations(p);
        if (mine.Count == 0) return false;
        string holder = Holder(mine[0]);
        if (mine.Any(l => Holder(l) != holder || l.State != mine[0].State)) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cands = new List<GeoProvince>();
        foreach (var l in mine)
            foreach (var a in l.Neighbors)
            {
                if (!RealBorder(a)) continue;
                var q = reg.ProvinceOfAtom(a.To.Key, day);
                if (q == null || q == p || touched.Contains(q.Id) || !seen.Add(q.Id)) continue;
                if (!q.Reason.StartsWith("dynamic:", StringComparison.Ordinal) || q.Reason == ShiftReason) continue;
                bool qFront = q.Reason == FrontReason || q.Reason == FrontMergeReason;
                if (qFront != front || q.Archipelago != p.Archipelago) continue;
                if (day - q.From < (front ? cfg.FrontMinAgeDays : cfg.MinAgeDays)) continue;
                var theirs = Locations(q);
                if (theirs.Count == 0 || theirs.Any(x => Holder(x) != holder || x.State != mine[0].State)) continue;
                if (!front)
                {
                    if (pop == null) continue;
                    if (mine.Sum(x => pop[x.Index]) + theirs.Sum(x => pop[x.Index]) > cfg.MergeCeiling * cfg.SplitPopulation) continue;
                }
                cands.Add(q);
            }
        var best = cands.OrderBy(q => q.Atoms.Length).ThenBy(q => q.Id, StringComparer.Ordinal).FirstOrDefault();
        if (best == null) return false;
        var op = new GeoOperation { Date = day, Parents = new[] { p.Id, best.Id }, Reason = front ? FrontMergeReason : GrowthMergeReason, Source = "dynamic" };
        op.Children.Add(new GeoChildSpec { Id = NewId(day), Archipelago = p.Archipelago, Name = BaseName(NameOf(p.Atoms.Length >= best.Atoms.Length ? p : best, day)) });
        if (!Apply(op)) return false;
        touched.Add(p.Id); touched.Add(best.Id);
        return true;
    }

    // ------------------------------------------------------------------ geometry
    private static bool RealBorder(Adjacency a) => a.Kind == AdjacencyKind.Land && a.To.IsLand && a.BorderPixels >= GeoValidator.MinBorderPixels;

    /// <summary>Connected pieces of a set of locations (joined by real land borders), in a stable order.</summary>
    public static List<List<Location>> Components(IEnumerable<Location> locs)
    {
        var set = locs.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        var inSet = new HashSet<Location>(set);
        var seen = new HashSet<Location>();
        var result = new List<List<Location>>();
        foreach (var start in set)
        {
            if (!seen.Add(start)) continue;
            var comp = new List<Location> { start };
            for (int i = 0; i < comp.Count; i++)
                foreach (var a in comp[i].Neighbors)
                    if (RealBorder(a) && inSet.Contains(a.To) && seen.Add(a.To)) comp.Add(a.To);
            comp.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            result.Add(comp);
        }
        return result;
    }
}
