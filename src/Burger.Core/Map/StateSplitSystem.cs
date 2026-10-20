using System.Globalization;
using Burger.Core.Geography;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>One runtime split of a state: which locations moved to the new state, its share of the parent's area, and what the parent was before.</summary>
public sealed class StateSplitRecord
{
    public string Parent = "", Child = "", Name = "";
    public List<string> Locations = new();
    /// <summary>Share (0-1) of the parent's land area that went to the child.</summary>
    public double Share;
    public double ParentTax, ParentManpower;
    public Dictionary<string, int> ParentBuildings = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> ParentDamaged = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Splits one state in two while the game runs (counter-settlement). The new state takes a connected piece of the parent's land and
/// the geography carry-over rules decide what each half keeps: extensive values (tax, manpower, people, building levels, deposits) split by area with whole numbers by
/// largest remainder, intensive values (development, literacy) stay as they were, categories and cores/claims copy. Subsystems follow through
/// <see cref="GameEvents.StateSplit"/>; splits are saved and replayed on load.
/// </summary>
public sealed class StateSplitSystem
{
    private readonly Game _game;
    public readonly List<StateSplitRecord> Splits = new();
    private double[]? _areas;

    public StateSplitSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "state_splits", Early = true, Write = Save, Read = Load, Reset = UndoAll });
    }

    private double AreaOf(Location l)
    {
        _areas ??= GeoMetrics.LocationAreasKm2(_game.World);
        // locations created at run time (none today) have no cached area: fall back to pixels
        return l.Index < _areas.Length && _areas[l.Index] > 0 ? _areas[l.Index] : Math.Max(1, l.PixelCount);
    }

    public double AreaOf(IEnumerable<Location> locs) => locs.Sum(AreaOf);

    /// <summary>Why the state cannot be split (null = it can).</summary>
    public string? CannotSplit(State s)
    {
        if (s.Locations.Count(l => l.IsLand) < 2) return "Too small to divide";
        if (GeoDynamics.Components(s.Locations.Where(l => l.IsLand)).Count != 1) return "Not one connected piece of land";
        return null;
    }

    /// <summary>
    /// Picks the connected piece of land the child takes: it grows from the location nearest <paramref name="from"/> until it holds <paramref name="share"/> of the area,
    /// never cutting the rest in two. The same input always gives the same piece.
    /// </summary>
    public List<Location>? Carve(State parent, double share, (double X, double Y) from)
    {
        var land = parent.Locations.Where(l => l.IsLand).ToList();
        if (land.Count < 2) return null;
        double total = land.Sum(AreaOf), target = total * Math.Clamp(share, 0.05, 0.95);
        // the seed must not be a bridge: the rest has to stay in one piece
        var seed = land.OrderBy(l => Dist2(l, from)).ThenBy(l => l.Key, StringComparer.Ordinal)
            .FirstOrDefault(l => GeoDynamics.Components(land.Where(x => x != l)).Count == 1);
        if (seed == null) return null;
        var take = new List<Location> { seed };
        var takeSet = new HashSet<Location> { seed };
        double got = AreaOf(seed);
        var landSet = new HashSet<Location>(land);
        while (got < target && take.Count < land.Count - 1)
        {
            Location? best = null;
            foreach (var t in take)
                foreach (var a in t.Neighbors)
                {
                    var n = a.To;
                    if (!landSet.Contains(n) || takeSet.Contains(n) || a.Kind != AdjacencyKind.Land || a.BorderPixels < GeoValidator.MinBorderPixels) continue;
                    if (best == null || Dist2(n, from) < Dist2(best, from) || (Dist2(n, from) == Dist2(best, from) && string.CompareOrdinal(n.Key, best.Key) < 0)) best = n;
                }
            if (best == null) break;
            // stop when this location overshoots the target by more than stopping now undershoots it
            double a2 = AreaOf(best);
            if (got + a2 - target > target - got) break;
            var rest = land.Where(l => !takeSet.Contains(l) && l != best).ToList();
            if (rest.Count == 0 || GeoDynamics.Components(rest).Count != 1)
            {
                // taking it would cut the rest in two: look for another edge location that does not
                var alt = EdgeLocations(land, takeSet).Where(l => l != best).OrderBy(l => Dist2(l, from)).ThenBy(l => l.Key, StringComparer.Ordinal)
                    .FirstOrDefault(l => { var r = land.Where(x => !takeSet.Contains(x) && x != l).ToList(); return r.Count > 0 && GeoDynamics.Components(r).Count == 1; });
                if (alt == null) break;
                best = alt; a2 = AreaOf(best);
            }
            take.Add(best); takeSet.Add(best); got += a2;
        }
        var left = land.Where(l => !takeSet.Contains(l)).ToList();
        if (left.Count == 0 || GeoDynamics.Components(left).Count != 1) return null;
        return take;
    }

    private static IEnumerable<Location> EdgeLocations(List<Location> land, HashSet<Location> takeSet)
    {
        var landSet = new HashSet<Location>(land);
        foreach (var t in takeSet)
            foreach (var a in t.Neighbors)
                if (landSet.Contains(a.To) && !takeSet.Contains(a.To) && a.Kind == AdjacencyKind.Land && a.BorderPixels >= GeoValidator.MinBorderPixels) yield return a.To;
    }

    private static double Dist2(Location l, (double X, double Y) p) { double dx = l.CenterX - p.X, dy = l.CenterY - p.Y; return dx * dx + dy * dy; }

    /// <summary>Splits <paramref name="parent"/> so the child takes <paramref name="share"/> of its land from the side facing <paramref name="from"/>. Returns the child, or null if it cannot be done.</summary>
    public State? Split(State parent, double share, (double X, double Y) from, string name)
    {
        if (CannotSplit(parent) != null) return null;
        var take = Carve(parent, share, from);
        if (take == null) return null;
        string key = parent.Key + "_c";
        for (int n = 2; _game.World.StateByKey.ContainsKey(key); n++) key = $"{parent.Key}_c{n}";
        var rec = new StateSplitRecord { Parent = parent.Key, Child = key, Name = name, Locations = take.Select(l => l.Key).OrderBy(k => k, StringComparer.Ordinal).ToList() };
        rec.Share = AreaOf(take) / Math.Max(1, AreaOf(parent.Locations.Where(l => l.IsLand)));
        rec.ParentTax = parent.Tax; rec.ParentManpower = parent.Manpower;
        foreach (var (k, v) in parent.Buildings) rec.ParentBuildings[k] = v;
        foreach (var (k, v) in parent.Damaged) rec.ParentDamaged[k] = v;
        var child = Apply(rec);
        Splits.Add(rec);
        _game.RaiseMapChanged();
        return child;
    }

    private State Apply(StateSplitRecord rec)
    {
        var w = _game.World;
        var parent = w.StateByKey[rec.Parent];
        var child = new State
        {
            Key = rec.Child, Index = w.States.Count, NameOverride = rec.Name, SourceFile = parent.SourceFile, Category = parent.Category, Continent = parent.Continent,
            Demilitarized = parent.Demilitarized, Owner = parent.Owner,
        };
        foreach (var c in parent.Cores) child.Cores.Add(c);
        foreach (var c in parent.Claims) child.Claims.Add(c);
        var moved = rec.Locations.Select(k => w.LocationByKey[k]).ToList();
        var movedSet = new HashSet<Location>(moved);
        // water next to the new land and to nothing of the parent's stays with the child
        foreach (var l in parent.Locations.Where(l => !l.IsLand))
        {
            var inState = l.Neighbors.Select(a => a.To).Where(n => n.State == parent && n.IsLand).ToList();
            if (inState.Count > 0 && inState.All(movedSet.Contains)) movedSet.Add(l);
        }
        foreach (var l in movedSet.OrderBy(l => l.Index)) { parent.Locations.Remove(l); l.State = child; child.Locations.Add(l); }
        w.States.Add(child);
        w.StateByKey[child.Key] = child;
        w.ScopeHierarchy.Register(child);
        // carry-over: extensive values split by area, whole numbers by largest remainder
        double share = rec.Share;
        child.Tax = parent.Tax * share; parent.Tax -= child.Tax;
        child.Manpower = parent.Manpower * share; parent.Manpower -= child.Manpower;
        foreach (var (k, whole, part) in SplitInts(parent.Buildings.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, x.Value)).ToList(), share))
        {
            if (part > 0) child.Buildings[k] = part;
            if (whole - part > 0) parent.Buildings[k] = whole - part; else parent.Buildings.Remove(k);
        }
        foreach (var (k, v) in parent.Damaged.ToList())
        {
            double d = v * share;
            if (d > 0) child.Damaged[k] = d;
            parent.Damaged[k] = v - d;
        }
        // contested land is not controlled by anyone yet: nothing to carry for controllers, they follow the location
        _game.Hub.RaiseStateSplit(parent, child, share);
        return child;
    }

    /// <summary>Splits whole numbers between two halves in proportion <paramref name="share"/> by largest remainder (deterministic: remainder, then key order).</summary>
    public static List<(string Key, int Whole, int Part)> SplitInts(List<(string Key, int Value)> items, double share)
    {
        var result = new List<(string, int, int)>();
        var parts = items.Select(i => (i.Key, i.Value, Floor: (int)Math.Floor(i.Value * share), Rem: i.Value * share - Math.Floor(i.Value * share))).ToList();
        int wanted = (int)Math.Round(items.Sum(i => i.Value) * share);
        int left = wanted - parts.Sum(p => p.Floor);
        var bump = parts.OrderByDescending(p => p.Rem).ThenBy(p => p.Key, StringComparer.Ordinal).Take(Math.Max(0, left)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var p in parts) result.Add((p.Key, p.Value, Math.Min(p.Value, p.Floor + (bump.Contains(p.Key) ? 1 : 0))));
        return result;
    }

    /// <summary>Largest-remainder split of one whole number: how much of <paramref name="value"/> goes to the child.</summary>
    public static int SplitInt(int value, double share) => (int)Math.Clamp(Math.Round(value * share), 0, value);

    /// <summary>Puts a split back together (the child's land returns to the parent; what they held at the split is restored).</summary>
    private void Undo(StateSplitRecord rec)
    {
        var w = _game.World;
        if (!w.StateByKey.TryGetValue(rec.Child, out var child) || !w.StateByKey.TryGetValue(rec.Parent, out var parent)) return;
        _game.Hub.RaiseStateUnsplit(parent, child);
        foreach (var l in child.Locations.ToList()) { l.State = parent; parent.Locations.Add(l); }
        child.Locations.Clear();
        parent.Tax = rec.ParentTax; parent.Manpower = rec.ParentManpower;
        parent.Buildings.Clear(); foreach (var (k, v) in rec.ParentBuildings) parent.Buildings[k] = v;
        parent.Damaged.Clear(); foreach (var (k, v) in rec.ParentDamaged) parent.Damaged[k] = v;
        w.States.Remove(child);
        w.StateByKey.Remove(child.Key);
        w.ScopeHierarchy.Unregister(child);
    }

    private void UndoAll()
    {
        for (int i = Splits.Count - 1; i >= 0; i--) Undo(Splits[i]);
        Splits.Clear();
        _game.World.RefreshOwnership();
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var r in Splits)
        {
            var n = root.AddBlock("split");
            n.Add("parent", r.Parent); n.Add("child", r.Child); n.Add("name", r.Name); n.Add("share", F(r.Share));
            n.Add("parent_tax", F(r.ParentTax)); n.Add("parent_manpower", F(r.ParentManpower));
            foreach (var l in r.Locations) n.Add("location", l);
            foreach (var (k, v) in r.ParentBuildings.OrderBy(x => x.Key, StringComparer.Ordinal)) n.Add("pb_" + k, v.ToString(CultureInfo.InvariantCulture));
            foreach (var (k, v) in r.ParentDamaged.OrderBy(x => x.Key, StringComparer.Ordinal)) n.Add("pd_" + k, F(v));
        }
    }

    private void Load(Node root)
    {
        UndoAll();
        foreach (var n in root.GetAll("split"))
        {
            var rec = new StateSplitRecord
            {
                Parent = n.GetString("parent") ?? "", Child = n.GetString("child") ?? "", Name = n.GetString("name") ?? "", Share = n.GetDouble("share"),
                ParentTax = n.GetDouble("parent_tax"), ParentManpower = n.GetDouble("parent_manpower"), Locations = n.GetList("location").ToList(),
            };
            foreach (var c in n.All)
            {
                if (c.Key.StartsWith("pb_", StringComparison.Ordinal) && c.Value != null) rec.ParentBuildings[c.Key[3..]] = (int)c.AsDouble();
                else if (c.Key.StartsWith("pd_", StringComparison.Ordinal) && c.Value != null) rec.ParentDamaged[c.Key[3..]] = c.AsDouble();
            }
            var w = _game.World;
            if (!w.StateByKey.ContainsKey(rec.Parent) || w.StateByKey.ContainsKey(rec.Child) || rec.Locations.Any(k => !w.LocationByKey.ContainsKey(k)))
            { Log.Error($"Save: cannot replay the split of '{rec.Parent}' into '{rec.Child}'"); continue; }
            Apply(rec);
            Splits.Add(rec);
        }
        _game.RaiseMapChanged();
    }
}
