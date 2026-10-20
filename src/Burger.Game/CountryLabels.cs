using System.Diagnostics;
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Paradox-style country names: one label per contiguous block of a country's land, written along a gentle curve
/// that follows the block's shape and stays inside it. Every country gets a label on the block with its capital,
/// however small (it simply becomes readable when zoomed in); other blocks (colonies, exclaves) get one if big enough.
/// </summary>
public sealed class CountryLabels
{
    private sealed class Label
    {
        public string Text = "";
        public Vector2[] Points = Array.Empty<Vector2>(); // curve in map pixels, left to right
        public float FontPx;                              // letter height in map pixels
        public float[] Heights = Array.Empty<float>();    // smoothed terrain height under each point (the name lies on the ground)
        public float LengthPx;
        public string Kind = "";                        // how the line was found (debugging)
        public float Coverage = 1;                        // share of the land's length the name runs along
        public bool Quick;                                // laid out by the fast first pass (one direction, no coast-following lines); the full pass replaces it
        // identity of what the label was fitted to: the same owner, name and land gives the same label (no refit, no movement)
        public int Owner = -1;
        public long Key;
        public int[] Locs = Array.Empty<int>();
        // a refit that really had to move the name glides from its old curve to the new one instead of jumping
        public Vector2[]? FromPoints;
        public float FromFontPx, FromLengthPx;
        public double MorphAt = -1;
        public Label Rekeyed(long key, int[] locs) => new Label
        {
            Text = Text, Points = Points, FontPx = FontPx, Heights = Heights, LengthPx = LengthPx, Kind = Kind, Coverage = Coverage, Quick = Quick,
            Owner = Owner, Key = key, Locs = locs,
        };
    }

    private sealed record LocationInput(int Index, int OwnerIndex, bool IsWater, int PixelCount,
        float CenterX, float CenterY, int MinX, int MinY, int MaxX, int MaxY, int[] LandNeighbors);
    private sealed record CountryInput(int Index, string Tag, string Name, int CapitalIndex);
    private sealed record BuildInput(int Version, int Width, int Height, int[] PixelLocation,
        LocationInput[] Locations, Dictionary<int, CountryInput> CountryByIndex, Label[] Previous, bool Quick);
    private sealed record BuildResult(int Version, Label[] Labels);
    private sealed class Job
    {
        public readonly CountryInput Country; public readonly int Group; public readonly List<LocationInput> Locs; public readonly int[] Idx; public readonly long Key; public readonly Label? Same;
        public Label? Fitted;
        public Job(CountryInput country, int group, List<LocationInput> locs, int[] idx, long key, Label? same) { Country = country; Group = group; Locs = locs; Idx = idx; Key = key; Same = same; }
    }

    private const double BuildDebounceSeconds = 0.4;
    private const double MinBuildIntervalSeconds = 1.0;
    /// <summary>A rebuild starts at most this long after the first change, even if changes keep coming (a war raises them every tick).</summary>
    private const double MaxDebounceSeconds = 1.5;
    private readonly Game _game;
    private readonly MapRenderer _map;
    private readonly List<Label> _labels = new();
    private bool _dirty = true;
    private int _version;
    private int _tiSeen;
    private long _firstDirtyTicks = Stopwatch.GetTimestamp();
    private long _lastInvalidationTicks = Stopwatch.GetTimestamp();
    private double _lastBuildStartedAt = double.NegativeInfinity;
    private Task<BuildResult>? _buildTask;
    private bool _needFull;

    public CountryLabels(Game game, MapRenderer map)
    {
        _game = game;
        _map = map;
        game.MapChanged += MarkDirty;
        // the names of the date the world was made for (read from the cache of an earlier run when there is one)
        if (game.Timeline.Enabled) PrecomputeStep(game.Timeline);
        RelayoutNow(game.Timeline.Enabled ? game.Timeline.SnapshotFor(game.World.Date)?.Date : null);
    }

    /// <summary>
    /// The names are there at once (a new game, or another start date shown on the loaded world): a quick layout is made now, on every core,
    /// and the full one follows in the background.
    /// </summary>
    public void RelayoutNow(Burger.Core.GameDate? snapshotDate = null)
    {
        _buildTask = null;   // a layout still running belongs to the map as it was: its result is dropped
        _version++;
        var swl = System.Diagnostics.Stopwatch.StartNew();
        bool fromCache = false;
        try
        {
            Label[] labels;
            if (snapshotDate is { } d && _cache.TryGetValue(d, out var cached))
            {
                // laid out earlier (in the background, or in an earlier run): at once
                fromCache = true;
                labels = cached.Select(l => l.Rekeyed(l.Key, l.Locs)).ToArray();
                if (labels.Any(l => l.Heights.Length != l.Points.Length)) ApplyHeights(labels);
            }
            else
            {
                { labels = BuildLabels(CaptureInput(_version, true));
                ApplyHeights(labels); }
            }
            _labels.Clear();
            _labels.AddRange(labels);
            _needFull = true;
            Log.Info($"Country names laid out in {swl.ElapsedMilliseconds} ms ({labels.Length} names, {(fromCache ? "from the cache" : "new")})");
            _lastBuildStartedAt = Raylib.GetTime();
            _dirty = true;
        }
        catch (Exception ex) { Log.Error($"Country-label first layout failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ------------------------------------------------------------------------------------- every start date, laid out ahead of time
    private readonly Dictionary<Burger.Core.GameDate, Label[]> _cache = new();
    private Queue<Burger.Core.History.Snapshot>? _ahead;
    private Task<Label[]>? _aheadTask;
    private Burger.Core.GameDate _aheadDate;
    private int[][]? _landNeighbors;
    private List<State>? _statesByKey;

    /// <summary>
    /// While the start screen is up, the names of every start date are laid out one date after another in the background (suggested dates first),
    /// so that picking a date shows them at once. Call every frame of the start screen.
    /// </summary>
    public void PrecomputeStep(Burger.Core.History.TimelineSystem tl)
    {
        if (!tl.Enabled) return;
        if (!_diskTried) { _diskTried = true; LoadDiskCache(tl); }
        if (_aheadTask is { IsCompleted: true } done)
        {
            try
            {
                var labels = done.GetAwaiter().GetResult();
                ApplyHeights(labels);
                _cache[_aheadDate] = labels;
            }
            catch (Exception ex) { Log.Error($"Country-label layout of a start date failed: {ex.GetType().Name}: {ex.Message}"); }
            _aheadTask = null;
        }
        if (_aheadTask != null || _diskComplete) return;
        if (tl.FusedGroups.Count > 0) return;   // a game on fused states: the layouts of the other dates belong to the fine states of the start screen
        // first every date quickly (so that all of them show at once soon), then every date in full; the full layouts are kept for the next run
        if (_ahead == null || _ahead.Count == 0)
        {
            if (_aheadFull) { SaveDiskCache(tl); _diskComplete = true; return; }
            _aheadFull = _ahead != null;
            var first = tl.Eras.SelectMany(e => e.Suggested).Distinct().Select(tl.SnapshotFor).Where(sn => sn != null).Select(sn => sn!);
            _ahead = new Queue<Burger.Core.History.Snapshot>(first.Concat(tl.Snapshots).DistinctBy(sn => sn.Date));
        }
        while (_ahead.Count > 0 && !_aheadFull && _cache.ContainsKey(_ahead.Peek().Date)) _ahead.Dequeue();
        if (_ahead.Count == 0) return;
        var snap = _ahead.Dequeue();
        BuildInput input;
        try { input = CaptureSnapshotInput(tl, snap, !_aheadFull); }
        catch (Exception ex) { Log.Error($"Could not snapshot the names of a start date: {ex.GetType().Name}: {ex.Message}"); return; }
        _aheadDate = snap.Date;
        _aheadTask = Task.Run(() => BuildLabels(input));
    }

    private bool _diskTried, _diskComplete, _aheadFull;
    private const int DiskVersion = 1;

    private string? DiskPath(Burger.Core.History.TimelineSystem tl)
    {
        if (!Burger.Core.Map.MapCache.Enabled) return null;
        string key = Burger.Core.Map.MapCache.Key("labels" + DiskVersion, "file:" + Path.Combine(_game.Data.Root, "history", "timeline", "snapshots.txt"), _game.World.Map.ContentKey,
            _game.World.Countries.Count.ToString(), tl.Snapshots.Count.ToString());
        return Path.Combine(Burger.Core.Map.MapCache.Dir(_game.Data), $"labels_{key}.bin");
    }

    /// <summary>The layouts of an earlier run (every start date, in full): nothing has to be laid out before a date shows.</summary>
    private void LoadDiskCache(Burger.Core.History.TimelineSystem tl)
    {
        try
        {
            string? path = DiskPath(tl);
            if (path == null || !File.Exists(path)) return;
            using var r = new BinaryReader(File.OpenRead(path));
            int dates = r.ReadInt32();
            var loaded = new Dictionary<Burger.Core.GameDate, Label[]>();
            for (int d = 0; d < dates; d++)
            {
                var date = new Burger.Core.GameDate(r.ReadInt32());
                int count = r.ReadInt32();
                var labels = new Label[count];
                for (int i = 0; i < count; i++)
                {
                    var l = new Label { Text = r.ReadString(), FontPx = r.ReadSingle(), LengthPx = r.ReadSingle(), Coverage = r.ReadSingle() };
                    string tag = r.ReadString();
                    l.Owner = _game.World.GetCountry(tag)?.Index ?? -1;
                    l.Points = new Vector2[r.ReadInt32()];
                    for (int k = 0; k < l.Points.Length; k++) l.Points[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    l.Locs = new int[r.ReadInt32()];
                    for (int k = 0; k < l.Locs.Length; k++) l.Locs[k] = r.ReadInt32();
                    l.Key = LabelKey(l.Owner, NameAt(l.Owner, tl, date), l.Locs);
                    labels[i] = l;
                }
                loaded[date] = labels;
            }
            foreach (var kv in loaded) _cache[kv.Key] = kv.Value;
            _diskComplete = true;
            Log.Info($"Country labels: {loaded.Count} start dates read from the cache");
        }
        catch (Exception ex) { Log.Warn($"Country-label cache not read: {ex.Message}"); }
    }

    // the name a country is laid out with at a snapshot (its Key hashes it): the snapshot's name, else its name in the project data
    private string NameAt(int owner, Burger.Core.History.TimelineSystem tl, Burger.Core.GameDate date)
    {
        if (owner < 0) return "";
        var c = _game.World.Countries[owner];
        var snap = tl.Snapshots.FirstOrDefault(sn => sn.Date == date);
        return snap != null && snap.Names.TryGetValue(c.Tag, out var n) ? n : tl.BaseNameOf(c);
    }

    private void SaveDiskCache(Burger.Core.History.TimelineSystem tl)
    {
        try
        {
            string? path = DiskPath(tl);
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(_cache.Count);
                foreach (var (date, labels) in _cache)
                {
                    w.Write(date.TotalDays);
                    w.Write(labels.Length);
                    foreach (var l in labels)
                    {
                        w.Write(l.Text); w.Write(l.FontPx); w.Write(l.LengthPx); w.Write(l.Coverage);
                        w.Write(l.Owner >= 0 ? _game.World.Countries[l.Owner].Tag : "");
                        w.Write(l.Points.Length);
                        foreach (var p in l.Points) { w.Write(p.X); w.Write(p.Y); }
                        w.Write(l.Locs.Length);
                        foreach (int i in l.Locs) w.Write(i);
                    }
                }
            }
            File.Move(tmp, path, true);
            Log.Info($"Country labels: {_cache.Count} start dates laid out and kept for the next run");
        }
        catch (Exception ex) { Log.Warn($"Country-label cache not written: {ex.Message}"); }
    }

    /// <summary>What the layout needs to know about the world of a snapshot, without changing the world.</summary>
    private BuildInput CaptureSnapshotInput(Burger.Core.History.TimelineSystem tl, Burger.Core.History.Snapshot snap, bool quick)
    {
        var world = _game.World;
        _statesByKey ??= world.States.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        if (snap.Owners.Length != _statesByKey.Count) throw new InvalidOperationException("the snapshot is for other states");
        _landNeighbors ??= world.Locations.Select(l => l.Neighbors.Where(a => a.Kind != AdjacencyKind.Sea).Select(a => a.To.Index).ToArray()).ToArray();
        var ownerOf = new int[world.Locations.Count];
        Array.Fill(ownerOf, -1);
        var tagIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var holds = new HashSet<int>();
        for (int i = 0; i < _statesByKey.Count; i++)
        {
            string tag = snap.Owners[i];
            if (tag == "_") continue;
            if (!tagIndex.TryGetValue(tag, out int ci)) { ci = world.GetCountry(tag)?.Index ?? -1; tagIndex[tag] = ci; }
            if (ci < 0) continue;
            holds.Add(ci);
            foreach (var l in _statesByKey[i].Locations) ownerOf[l.Index] = ci;
        }
        var locations = new LocationInput[world.Locations.Count];
        foreach (var l in world.Locations)
            locations[l.Index] = new LocationInput(l.Index, ownerOf[l.Index], l.IsWater, l.PixelCount, l.CenterX, l.CenterY, l.MinX, l.MinY, l.MaxX, l.MaxY, _landNeighbors[l.Index]);
        var countries = new Dictionary<int, CountryInput>();
        foreach (var c in world.Countries)
        {
            if (!holds.Contains(c.Index)) continue;
            string name = snap.Names.TryGetValue(c.Tag, out var n) ? n : tl.BaseNameOf(c);
            int cap = tl.BaseCapitalOf(c)?.Index ?? -1;
            countries[c.Index] = new CountryInput(c.Index, c.Tag, name, cap >= 0 && ownerOf[cap] == c.Index ? cap : -1);
        }
        return new BuildInput(0, world.Map.Width, world.Map.Height, world.Map.PixelLocation, locations, countries, Array.Empty<Label>(), quick);
    }

    public void MarkDirty()
    {
        _version++;
        if (!_dirty) _firstDirtyTicks = Stopwatch.GetTimestamp();
        _dirty = true;
        _lastInvalidationTicks = Stopwatch.GetTimestamp();
    }

    // ------------------------------------------------------------------------------------- immutable snapshot + background layout
    private BuildInput CaptureInput(int version, bool quick)
    {
        var world = _game.World;
        var map = world.Map;
        _landNeighbors ??= world.Locations.Select(l => l.Neighbors.Where(a => a.Kind != AdjacencyKind.Sea).Select(a => a.To.Index).ToArray()).ToArray();   // (the neighbours do not change once the map is loaded)
        var locations = new LocationInput[world.Locations.Count];
        foreach (var l in world.Locations)
        {
            // Adjacency and province geometry do not change after map load; only owner/name/capital
            // state is copied here. PixelLocation is likewise immutable after MapLoader finishes.
            int[] landNeighbors = _landNeighbors[l.Index];
            locations[l.Index] = new LocationInput(l.Index, (_game.Exploration.IsCharted(l) ? l.Owner?.Index : null) ?? -1, l.IsWater,
                l.PixelCount, l.CenterX, l.CenterY, l.MinX, l.MinY, l.MaxX, l.MaxY, landNeighbors);
        }
        var countries = world.Countries.Where(c => c.Exists)
            .Select(c => new CountryInput(c.Index, c.Tag, c.Name, c.Capital?.Index ?? -1)).ToArray();
        return new BuildInput(version, map.Width, map.Height, map.PixelLocation, locations,
            countries.ToDictionary(c => c.Index), _labels.ToArray(), quick);
    }

    private void RefreshLabels()
    {
        if (_buildTask is { IsCompleted: true } completed)
        {
            _buildTask = null;
            try
            {
                var result = completed.GetAwaiter().GetResult();
                ApplyHeights(result.Labels);
                double now0 = Raylib.GetTime();
                foreach (var l in result.Labels) if (l.FromPoints != null && l.MorphAt < 0) l.MorphAt = now0;
                _labels.Clear();
                _labels.AddRange(result.Labels);
                // Publish the most recent completed snapshot even if newer map changes arrived while
                // it was being built; retain it until a coalesced follow-up is ready.
                _dirty = result.Version != _version || _needFull;
                if (_dirty) _firstDirtyTicks = Stopwatch.GetTimestamp();
            }
            catch (Exception ex)
            {
                _dirty = true;
                _lastBuildStartedAt = Raylib.GetTime();
                Log.Error($"Country-label layout failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (_tiSeen != _game.Exploration.Version) { _tiSeen = _game.Exploration.Version; MarkDirty(); }
        if (!_dirty || _buildTask != null) return;
        double now = Raylib.GetTime();
        // the first layout of a game (a new map, a new start date) is built at once: there is nothing on screen to keep still
        bool first = double.IsNegativeInfinity(_lastBuildStartedAt);
        bool refine = _needFull;
        if (!first && !refine && (now - _lastBuildStartedAt < MinBuildIntervalSeconds ||
            Stopwatch.GetElapsedTime(_lastInvalidationTicks).TotalSeconds < BuildDebounceSeconds &&
            Stopwatch.GetElapsedTime(_firstDirtyTicks).TotalSeconds < MaxDebounceSeconds)) return;

        BuildInput input;
        try { input = CaptureInput(_version, first); }
        catch (Exception ex)
        {
            _lastBuildStartedAt = now;
            Log.Error($"Could not snapshot country labels: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        _lastBuildStartedAt = now;
        _needFull = first;       // the first pass is quick; the full layout follows at once
        _buildTask = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var labels = BuildLabels(input);
            if (first) Log.Info($"Country labels: {labels.Length} names laid out in {sw.ElapsedMilliseconds} ms (first pass)");
            return new BuildResult(input.Version, labels);
        });
    }

    private void ApplyHeights(Label[] labels)
    {
        foreach (var label in labels)
        {
            int n = label.Points.Length;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = _map.HeightAt(label.Points[i].X / _map.PixelsPerUnit, label.Points[i].Y / _map.PixelsPerUnit);
            var heights = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0, weightSum = 0;
                for (int j = Math.Max(0, i - 6); j <= Math.Min(n - 1, i + 6); j++)
                {
                    float weight = 7 - Math.Abs(i - j);
                    sum += raw[j] * weight;
                    weightSum += weight;
                }
                heights[i] = sum / weightSum + 0.02f;
            }
            label.Heights = heights;
        }
    }

    // The expensive connected-region and shape-fitting work uses only this immutable input.
    private static Label[] BuildLabels(BuildInput input)
    {
        int n = input.Locations.Length;
        var block = new int[n];
        Array.Fill(block, -1);
        var blocks = new List<(int OwnerIndex, List<LocationInput> Locs, long Px)>();
        foreach (var start in input.Locations)
        {
            int owner = start.OwnerIndex;
            if (owner < 0 || start.IsWater || block[start.Index] >= 0) continue;
            int id = blocks.Count;
            var list = new List<LocationInput>();
            long px = 0;
            var q = new Queue<LocationInput>();
            block[start.Index] = id;
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var l = q.Dequeue();
                list.Add(l);
                px += l.PixelCount;
                foreach (int neighborIndex in l.LandNeighbors)
                {
                    var neighbor = input.Locations[neighborIndex];
                    if (!neighbor.IsWater && neighbor.OwnerIndex == owner && block[neighborIndex] < 0)
                    {
                        block[neighborIndex] = id;
                        q.Enqueue(neighbor);
                    }
                }
            }
            blocks.Add((owner, list, px));
        }
        var labels = new List<Label>();
        if (blocks.Count == 0) return labels.ToArray();

        // blocks of one country separated only by a narrow stretch of sea share one name
        var groupOf = new int[blocks.Count];
        for (int i = 0; i < groupOf.Length; i++) groupOf[i] = i;
        int Find(int i) { while (groupOf[i] != i) i = groupOf[i] = groupOf[groupOf[i]]; return i; }
        float gap = Math.Max(6, input.Width * 0.0025f);
        static float BoxGap(int ax0, int ay0, int ax1, int ay1, int bx0, int by0, int bx1, int by1)
            => Math.Max(Math.Max(0, Math.Max(ax0 - bx1, bx0 - ax1)), Math.Max(0, Math.Max(ay0 - by1, by0 - ay1)));
        var bbox = blocks.Select(b => (b.Locs.Min(l => l.MinX), b.Locs.Min(l => l.MinY), b.Locs.Max(l => l.MaxX), b.Locs.Max(l => l.MaxY))).ToArray();
        foreach (var g in Enumerable.Range(0, blocks.Count).GroupBy(i => blocks[i].OwnerIndex))
        {
            var ids = g.ToList();
            for (int x = 0; x < ids.Count; x++)
                for (int y = x + 1; y < ids.Count; y++)
                {
                    int a = ids[x], b = ids[y];
                    var (ax0, ay0, ax1, ay1) = bbox[a];
                    var (bx0, by0, bx1, by1) = bbox[b];
                    if (BoxGap(ax0, ay0, ax1, ay1, bx0, by0, bx1, by1) > gap || Find(a) == Find(b)) continue;
                    bool near = false;
                    foreach (var la in blocks[a].Locs)
                    {
                        foreach (var lb in blocks[b].Locs)
                            if (BoxGap(la.MinX, la.MinY, la.MaxX, la.MaxY, lb.MinX, lb.MinY, lb.MaxX, lb.MaxY) <= gap) { near = true; break; }
                        if (near) break;
                    }
                    if (near) groupOf[Find(a)] = Find(b);
                }
        }
        for (int i = 0; i < groupOf.Length; i++) groupOf[i] = Find(i);
        var blockGroup = groupOf;
        var jobs = new List<Job>();
        foreach (var g in Enumerable.Range(0, blocks.Count).GroupBy(i => blocks[i].OwnerIndex))
        {
            if (!input.CountryByIndex.TryGetValue(g.Key, out var country)) continue;
            int homeBlock = country.CapitalIndex >= 0 && country.CapitalIndex < n &&
                input.Locations[country.CapitalIndex].OwnerIndex == country.Index && block[country.CapitalIndex] >= 0
                ? block[country.CapitalIndex] : g.OrderByDescending(i => blocks[i].Px).First();
            int home = blockGroup[homeBlock];
            foreach (var grp in g.GroupBy(i => blockGroup[i]))
            {
                var locs = grp.SelectMany(i => blocks[i].Locs).ToList();
                long px = grp.Sum(i => blocks[i].Px);
                bool wanted = grp.Key == home || (locs.Count >= 2 && px >= 400);
                if (!wanted) continue;
                int[] idx = locs.Select(l => l.Index).OrderBy(i => i).ToArray();
                long key = LabelKey(country.Index, country.Name, idx);
                jobs.Add(new Job(country, grp.Key, locs, idx, key, input.Previous.FirstOrDefault(pl => pl.Key == key && (input.Quick || !pl.Quick))));
            }
        }
        // the fits are independent of each other: lay them out on every core
        Parallel.ForEach(jobs.Where(j => j.Same == null), j => j.Fitted = Fit(j.Country, j.Locs, j.Group, block, blockGroup, input, input.Quick));
        foreach (var job in jobs)
        {
            var country = job.Country; var idx = job.Idx; long key = job.Key;
            if (job.Same != null) { labels.Add(job.Same); continue; }   // nothing this name depends on changed: it stays exactly where it is
            var label = job.Fitted;
            if (label == null) continue;
            label.Quick = input.Quick;
            label.Owner = country.Index; label.Key = key; label.Locs = idx;
            var old = BestPrevious(input.Previous, country.Index, label.Text, idx);
            if (old != null)
            {
                // a small change in the land (a province gained or lost elsewhere) must not shuffle the name: keep the old
                // curve while it still lies inside the land and its letters are not much smaller than a fresh fit would give
                int gid = job.Group, inside = 0, total = 0;
                bool In(Vector2 q)
                {
                    int x = (int)q.X, y = (int)q.Y;
                    if (x < 0 || y < 0 || x >= input.Width || y >= input.Height) return false;
                    int li = input.PixelLocation[y * input.Width + x];
                    return li >= 0 && block[li] >= 0 && blockGroup[block[li]] == gid;
                }
                for (int i = 0; i < old.Points.Length; i++)
                {
                    total++; if (In(old.Points[i])) inside++;
                    if (i > 0) { total++; if (In((old.Points[i] + old.Points[i - 1]) / 2)) inside++; }
                }
                if (inside >= total * 0.9f && old.FontPx >= label.FontPx * 0.75f) { labels.Add(old.Rekeyed(key, idx)); continue; }
                label.FromPoints = Resample(old.Points, label.Points.Length);
                label.FromFontPx = old.FontPx; label.FromLengthPx = old.LengthPx;
            }
            labels.Add(label);
        }
        return labels.ToArray();
    }

    private static long LabelKey(int owner, string name, int[] sortedLocs)
    {
        var h = new HashCode();
        h.Add(owner); h.Add(name);
        foreach (int i in sortedLocs) h.Add(i);
        return ((long)h.ToHashCode() << 32) ^ (uint)sortedLocs.Length * 2654435761u ^ sortedLocs.Sum(i => (long)i * 7919);
    }

    /// <summary>The earlier label of this country and name whose land overlaps the new land most (null if none).</summary>
    private static Label? BestPrevious(Label[] previous, int owner, string text, int[] sortedLocs)
    {
        Label? best = null;
        int bestShared = 0;
        foreach (var pl in previous)
        {
            if (pl.Owner != owner || pl.Text != text) continue;
            int a = 0, b = 0, shared = 0;
            while (a < pl.Locs.Length && b < sortedLocs.Length)
            {
                if (pl.Locs[a] == sortedLocs[b]) { shared++; a++; b++; }
                else if (pl.Locs[a] < sortedLocs[b]) a++; else b++;
            }
            if (shared > bestShared) { bestShared = shared; best = pl; }
        }
        // mostly the same land (a name must not hop to a different part of the country)
        return best != null && bestShared * 2 >= Math.Min(best.Locs.Length, sortedLocs.Length) ? best : null;
    }

    /// <summary>The same curve with <paramref name="m"/> points, evenly spaced along its length.</summary>
    private static Vector2[] Resample(Vector2[] pts, int m)
    {
        var res = new Vector2[m];
        int n = pts.Length;
        if (n == 0 || m == 0) return res;
        if (n == 1 || m == 1) { Array.Fill(res, pts[0]); return res; }
        var cum = new float[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        float total = Math.Max(cum[n - 1], 1e-3f);
        int k = 1;
        for (int i = 0; i < m; i++)
        {
            float at = total * i / (m - 1);
            while (k < n - 1 && cum[k] < at) k++;
            float t = (at - cum[k - 1]) / Math.Max(1e-4f, cum[k] - cum[k - 1]);
            res[i] = Vector2.Lerp(pts[k - 1], pts[k], Math.Clamp(t, 0, 1));
        }
        return res;
    }

    private static Label? Fit(CountryInput c, List<LocationInput> locs, int groupId, int[] block, int[] blockGroup, BuildInput md, bool quick)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        foreach (var l in locs) { minX = Math.Min(minX, l.MinX); minY = Math.Min(minY, l.MinY); maxX = Math.Max(maxX, l.MaxX); maxY = Math.Max(maxY, l.MaxY); }
        long area = (long)(maxX - minX + 1) * (maxY - minY + 1);
        int step = Math.Max(1, (int)Math.Sqrt(area / (quick ? 5000.0 : 25000.0)));
        var xs = new List<float>();
        var ys = new List<float>();
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= md.Width || y >= md.Height) return false;
            int li = md.PixelLocation[y * md.Width + x];
            return li >= 0 && block[li] >= 0 && blockGroup[block[li]] == groupId;
        }
        for (int y = minY; y <= maxY; y += step)
            for (int x = minX; x <= maxX; x += step)
                if (Inside(x, y)) { xs.Add(x); ys.Add(y); }
        if (xs.Count < 3)
        {
            xs.Clear(); ys.Clear();
            foreach (var l in locs) { xs.Add(l.CenterX); ys.Add(l.CenterY); xs.Add(l.MinX); ys.Add(l.CenterY); xs.Add(l.MaxX); ys.Add(l.CenterY); }
        }
        int m = xs.Count;
        double mx = xs.Average(), my = ys.Average();
        double cxx = 0, cyy = 0, cxy = 0;
        for (int i = 0; i < m; i++) { double dx = xs[i] - mx, dy = ys[i] - my; cxx += dx * dx; cyy += dy * dy; cxy += dx * dy; }
        double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        // how elongated the block is (ratio of the two axes); roundish blocks get (nearly) horizontal names
        double tr = cxx + cyy, dt = Math.Sqrt(Math.Max(0, (cxx - cyy) * (cxx - cyy) / 4 + cxy * cxy));
        double l1 = tr / 2 + dt, l2 = Math.Max(1e-6, tr / 2 - dt);
        double elong = Math.Sqrt(l1 / l2);
        if (ang > Math.PI / 2) ang -= Math.PI;
        if (ang < -Math.PI / 2) ang += Math.PI;
        if (elong < 2.2) ang *= Math.Clamp((elong - 1.2) / 1.0, 0, 1);
        // keep text readable: never steeper than 60 degrees, always left to right
        double lim = 60 * Math.PI / 180;
        ang = Math.Clamp(ang, -lim, lim);
        // try the main axis and a fan of other directions; the one that fits the biggest letters wins
        // (flatter names are preferred a little, the main axis a little more)
        Label? best = null;
        double bestScore = 0;
        var angles = new List<double> { ang };
        if (!quick) for (int deg = -60; deg <= 60; deg += 15) angles.Add(deg * Math.PI / 180);
        Label? fallback = null;
        // BURGER_LABEL_DEBUG=TAG writes every candidate line of that country to label_debug.txt
        bool debug = Environment.GetEnvironmentVariable("BURGER_LABEL_DEBUG") is { } dbgTag && dbgTag == c.Tag;
        var dbg = debug ? new System.Text.StringBuilder($"{c.Tag} group {groupId} locs {locs.Count} ang {ang * 180 / Math.PI:F0} elong {elong:F2}\n") : null;
        foreach (var a in angles)
        {
            var cands = FitAngle(a);
            if (dbg != null) dbg.Append($" angle {a * 180 / Math.PI:F0}: {cands.Count} lines\n");
            foreach (var cand in cands)
            {
                fallback ??= cand;
                // names stay on their land: lines that run over the sea or other countries lose
                double on = cand.Points.Count(p => Inside((int)p.X, (int)p.Y)) / (double)cand.Points.Length;
                if (on < 0.6) { dbg?.Append($"  {cand.Kind} font {cand.FontPx:F1} cov {cand.Coverage:F2} on {on:F2} REJECTED\n"); continue; }
                // steep names read worse: they must fit clearly bigger letters to win (less so along the land's own long axis)
                var chord = cand.Points[^1] - cand.Points[0];
                double tilt = Math.Min(Math.Abs(Math.Atan2(chord.Y, chord.X)), Math.Abs(Math.Atan2(-chord.Y, -chord.X)));
                double steep = Math.Pow(Math.Min(1.3, tilt / lim), 1.3) * (a == ang && elong > 2.2 ? 0.35 : 1);
                // and a name that runs along more of the land is worth more than slightly bigger letters on a part of it
                // bent names are a little harder to read than straight ones
                double bend = 0;
                for (int k = 2; k < cand.Points.Length; k++)
                {
                    var d0 = cand.Points[k - 1] - cand.Points[k - 2];
                    var d1 = cand.Points[k] - cand.Points[k - 1];
                    bend += Math.Abs(Math.Atan2(d0.X * d1.Y - d0.Y * d1.X, d0.X * d1.X + d0.Y * d1.Y));
                }
                // (bigger letters count, but less than running along more of the land: long thin countries get long names)
                double axis = a == ang ? (elong > 2.2 ? 1.25 : 1.06) : 1;
                double score = Math.Pow(cand.FontPx, 0.7) * (1 - 0.5 * Math.Min(1, steep)) * axis * Math.Pow(on, 1.5) * Math.Pow(cand.Coverage, 1.3)
                             * (1 - 0.05 * Math.Min(1.5, bend));
                dbg?.Append($"  {cand.Kind} font {cand.FontPx:F1} cov {cand.Coverage:F2} on {on:F2} bend {bend:F2} steep {steep:F2} score {score:F2} ({cand.Points[0].X:F0},{cand.Points[0].Y:F0})-({cand.Points[^1].X:F0},{cand.Points[^1].Y:F0})\n");
                if (score > bestScore) { bestScore = score; best = cand; }
            }
        }
        if (dbg != null)
        {
            dbg.Append($" chosen {best?.Kind} score {bestScore:F2}\n");
            try { System.IO.File.AppendAllText("label_debug.txt", dbg.ToString()); } catch { }
        }
        return best ?? fallback;

        List<Label> FitAngle(double ang)
        {
        var result = new List<Label>();
        double ca = Math.Cos(ang), sa = Math.Sin(ang);
        var us = new double[m];
        var vs = new double[m];
        for (int i = 0; i < m; i++)
        {
            double dx = xs[i] - mx, dy = ys[i] - my;
            us[i] = dx * ca + dy * sa;
            vs[i] = -dx * sa + dy * ca;
        }
        var sortedU = us.OrderBy(v => v).ToArray();
        double u0 = sortedU[(int)(m * 0.03)], u1 = sortedU[Math.Min(m - 1, (int)(m * 0.97))];
        if (u1 - u0 < 1) { u0 -= 2; u1 += 2; }
        const int bins = 24;
        var vmin = Enumerable.Repeat(double.MaxValue, bins).ToArray();
        var vmax = Enumerable.Repeat(double.MinValue, bins).ToArray();
        var cnt = new int[bins];
        for (int i = 0; i < m; i++)
        {
            int b = (int)((us[i] - u0) / (u1 - u0) * bins);
            if (b < 0 || b >= bins) continue;
            cnt[b]++;
            vmin[b] = Math.Min(vmin[b], vs[i]);
            vmax[b] = Math.Max(vmax[b], vs[i]);
        }
        // the land in each bin as separate stretches across the line (a bay or gulf splits a bin in two)
        var segs = new List<(double Lo, double Hi)>[bins];
        {
            var binVs = new List<double>[bins];
            for (int b = 0; b < bins; b++) binVs[b] = new List<double>();
            for (int i = 0; i < m; i++)
            {
                int b = (int)((us[i] - u0) / (u1 - u0) * bins);
                if (b >= 0 && b < bins) binVs[b].Add(vs[i]);
            }
            for (int b = 0; b < bins; b++)
            {
                segs[b] = new List<(double, double)>();
                var vl = binVs[b];
                if (vl.Count == 0) continue;
                vl.Sort();
                double lo = vl[0], prev = vl[0];
                foreach (var v in vl)
                {
                    if (v - prev > step * 3.5) { segs[b].Add((lo, prev + step)); lo = v; }
                    prev = v;
                }
                segs[b].Add((lo, prev + step));
            }
        }
        var thick = new double[bins];
        double maxThick = 0;
        for (int b = 0; b < bins; b++) { thick[b] = cnt[b] > 0 ? vmax[b] - vmin[b] + step : 0; maxThick = Math.Max(maxThick, thick[b]); }
        double binW = (u1 - u0) / bins;
        string text = c.Name.ToUpperInvariant();
        var across = new Vector2((float)-sa, (float)ca);
        float limit = (float)Math.Max(8, maxThick * 1.2);
        float stepPx = Math.Max(1f, step * 0.75f);
        float gapMax = (float)Math.Max(2 * step, maxThick * 0.06);
        double InsideShare(Vector2[] pts) => pts.Count(p => Inside((int)p.X, (int)p.Y)) / (double)pts.Length;
        Vector2 ToMap(double u, double v) => new((float)(mx + u * ca - v * sa), (float)(my + u * sa + v * ca));
        // how far the country goes from p in a direction; small lakes and inlets (short gaps) do not stop it
        float Room(Vector2 p, Vector2 dir)
        {
            float t = 0, lastInside = 0, gap = 0;
            while (t < limit)
            {
                t += stepPx;
                if (Inside((int)(p.X + dir.X * t), (int)(p.Y + dir.Y * t))) { lastInside = t; gap = 0; }
                else if ((gap += stepPx) > gapMax) break;
            }
            return lastInside;
        }

        // two ways to lay the name, each on a thick part (35% of the widest) or also thinner parts (18%):
        // a gentle arc, and a line through the middle of the land that bends with it (coasts, peninsulas)
        foreach (double thr in quick ? new[] { 0.35 } : new[] { 0.35, 0.18 })
        {
            int bestStart = 0, bestLen = 0;
            for (int b = 0; b < bins;)
            {
                if (thick[b] < maxThick * thr) { b++; continue; }
                int s0_ = b;
                while (b < bins && thick[b] >= maxThick * thr) b++;
                if (b - s0_ > bestLen) { bestLen = b - s0_; bestStart = s0_; }
            }
            if (bestLen == 0) { bestStart = 0; bestLen = bins; }
            double r0 = u0 + bestStart * binW, r1 = u0 + (bestStart + bestLen) * binW;
            double extent = u1 - u0;
            var arc = Arc(bestStart, bestLen, r0, r1);
            if (arc != null) { var l = Finish(arc, rigid: true, extent); if (l != null) { l.Kind = $"arc{thr}"; result.Add(l); } }
            var spine = Spine(bestStart, bestLen);
            if (spine != null) { var l = Finish(spine, rigid: false, extent); if (l != null) { l.Kind = $"spine{thr}"; result.Add(l); } }
            var ridge = quick ? null : Ridge(thr);
            if (ridge != null) { var l = Finish(ridge, rigid: false, extent); if (l != null) { l.Kind = $"ridge{thr}"; result.Add(l); } }
        }
        // and down the whole length of long, thin land (Norway's coast, Chile), however narrow it gets
        {
            var ridge = quick ? null : Ridge(0.06);
            if (ridge != null) { var l = Finish(ridge, rigid: false, u1 - u0); if (l != null) { l.Kind = "ridgeAll"; result.Add(l); } }
        }
        return result;

        // the line down the middle of the land itself: one stretch of land per bin, chosen so the line runs on
        // without jumps (a peninsula with a gulf in it - Italy's boot - is followed down one side, not through the sea)
        Vector2[]? Ridge(double thr)
        {
            // best chain of stretches (Viterbi): long stretches are good, jumping sideways between bins is bad
            var score = new double[bins][];
            var from = new int[bins][];
            for (int b = 0; b < bins; b++)
            {
                score[b] = new double[segs[b].Count];
                from[b] = new int[segs[b].Count];
                for (int k = 0; k < segs[b].Count; k++)
                {
                    var (lo, hi) = segs[b][k];
                    double mid = (lo + hi) / 2;
                    double bestPrev = 0; int arg = -1;
                    if (b > 0)
                        for (int j = 0; j < segs[b - 1].Count; j++)
                        {
                            var (plo, phi) = segs[b - 1][j];
                            // the stretches must touch across the bin border to be one piece of land
                            if (plo > hi + step * 2 || phi < lo - step * 2) continue;
                            double v = score[b - 1][j] - Math.Abs((plo + phi) / 2 - mid) * 0.8;
                            if (v > bestPrev) { bestPrev = v; arg = j; }
                        }
                    score[b][k] = (hi - lo) + bestPrev;
                    from[b][k] = arg;
                }
            }
            int eb = -1, ek = -1; double es = 0;
            for (int b = 0; b < bins; b++)
                for (int k = 0; k < segs[b].Count; k++)
                    if (score[b][k] > es) { es = score[b][k]; eb = b; ek = k; }
            if (eb < 0) return null;
            var chain = new List<(int B, double Lo, double Hi)>();
            for (int b = eb, k = ek; b >= 0 && k >= 0; k = from[b][k], b--) chain.Add((b, segs[b][k].Lo, segs[b][k].Hi));
            chain.Reverse();
            // keep the longest run of stretches that are not too thin compared to the widest one in the chain
            double widest = chain.Max(x => x.Hi - x.Lo);
            int rs = 0, rl = 0;
            for (int i = 0; i < chain.Count;)
            {
                if (chain[i].Hi - chain[i].Lo < widest * thr) { i++; continue; }
                int st = i;
                while (i < chain.Count && chain[i].Hi - chain[i].Lo >= widest * thr) i++;
                if (i - st > rl) { rl = i - st; rs = st; }
            }
            if (rl < 4) return null;
            var mids = new List<Vector2>();
            for (int i = rs; i < rs + rl; i++) mids.Add(ToMap(u0 + (chain[i].B + 0.5) * binW, (chain[i].Lo + chain[i].Hi) / 2));
            return Smooth(mids);
        }

        // least-squares quadratic through the middles of the bins in the run
        Vector2[]? Arc(int bestStart, int bestLen, double r0, double r1)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            double uc = (r0 + r1) / 2;
            for (int b = bestStart; b < bestStart + bestLen; b++)
            {
                if (cnt[b] == 0) continue;
                double u = u0 + (b + 0.5) * binW - uc, v = (vmin[b] + vmax[b]) / 2, wgt = Math.Sqrt(cnt[b]);
                s0 += wgt; s1 += wgt * u; s2 += wgt * u * u; s3 += wgt * u * u * u; s4 += wgt * u * u * u * u;
                t0 += wgt * v; t1 += wgt * u * v; t2 += wgt * u * u * v;
            }
            double qa = 0, qb = 0, qc = s0 > 0 ? t0 / s0 : 0;
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (bestLen >= 3 && Math.Abs(det) > 1e-9)
            {
                qc = (t0 * (s2 * s4 - s3 * s3) - s1 * (t1 * s4 - s3 * t2) + s2 * (t1 * s3 - s2 * t2)) / det;
                qb = (s0 * (t1 * s4 - t2 * s3) - t0 * (s1 * s4 - s3 * s2) + s2 * (s1 * t2 - t1 * s2)) / det;
                qa = (s0 * (s2 * t2 - s3 * t1) - s1 * (s1 * t2 - s2 * t1) + t0 * (s1 * s3 - s2 * s2)) / det;
            }
            double half = (r1 - r0) / 2;
            // limit the bend so the name stays readable
            double maxA = half > 0 ? 0.22 * half / (half * half) : 0;
            qa = Math.Clamp(qa, -maxA, maxA);
            qb = Math.Clamp(qb, -0.6, 0.6);
            // the tilt of the curve adds to the label's angle: together they stay within the readable limit
            qb = Math.Clamp(qb, Math.Tan(Math.Max(-lim - ang, -1.2)), Math.Tan(Math.Min(lim - ang, 1.2)));
            double endSlope = Math.Max(Math.Abs(ang + Math.Atan(qb + 2 * qa * half)), Math.Abs(ang + Math.Atan(qb - 2 * qa * half)));
            if (endSlope > lim + 0.17) qa = 0;
            double margin = (r1 - r0) * 0.04;
            Vector2[] Curve(double a, double bb, double cc)
            {
                const int N = 24;
                var pts = new Vector2[N];
                for (int i = 0; i < N; i++)
                {
                    double u = r0 + margin + (r1 - r0 - 2 * margin) * i / (N - 1) - uc;
                    pts[i] = ToMap(u + uc, a * u * u + bb * u + cc);
                }
                return pts;
            }
            var points = Curve(qa, qb, qc);
            if (InsideShare(points) < 0.75)
            {
                var straight = Curve(0, qb, qc);
                if (InsideShare(straight) > InsideShare(points)) points = straight;
            }
            return points;
        }

        // the middles of the bins, smoothed, as a bending line (resampled evenly)
        Vector2[]? Spine(int bestStart, int bestLen)
        {
            var mids = new List<Vector2>();
            for (int b = bestStart; b < bestStart + bestLen; b++)
                if (cnt[b] > 0) mids.Add(ToMap(u0 + (b + 0.5) * binW, (vmin[b] + vmax[b]) / 2));
            if (mids.Count < 4) return null;
            return Smooth(mids);
        }

        // smooth until it reads well: no part steeper than 80 degrees and no sharp turns
        static Vector2[]? Smooth(List<Vector2> mids)
        {
            for (int pass = 0; pass < 14; pass++)
            {
                var pts = Resample(mids);
                if (pts == null) return null;
                if (Readable(pts)) return pts;
                var sm = new List<Vector2>(mids);
                for (int i = 1; i < mids.Count - 1; i++) sm[i] = (mids[i - 1] + mids[i] * 2 + mids[i + 1]) / 4;
                mids = sm;
            }
            return null;
        }

        // readable: no part steeper than 80 degrees, gentle turns, and not more than about 70 degrees of bending in all
        static bool Readable(Vector2[] pts)
        {
            double sum = 0;
            for (int k = 1; k < pts.Length; k++)
            {
                var dd = pts[k] - pts[k - 1];
                if (Math.Abs(dd.Y) > Math.Abs(dd.X) * 5.7f) return false;
                if (k >= 2)
                {
                    var d0 = pts[k - 1] - pts[k - 2];
                    double turn = Math.Atan2(d0.X * dd.Y - d0.Y * dd.X, d0.X * dd.X + d0.Y * dd.Y);
                    if (Math.Abs(turn) > 0.14) return false;
                    sum += turn;
                    if (Math.Abs(sum) > 1.25) return false;
                }
            }
            return true;
        }

        // evenly spaced points along a line, a little in from both ends
        static Vector2[]? Resample(List<Vector2> mids)
        {
            var cum = new float[mids.Count];
            for (int i = 1; i < mids.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(mids[i - 1], mids[i]);
            float total = cum[^1];
            if (total < 2) return null;
            const int N = 24;
            var pts = new Vector2[N];
            for (int k = 0; k < N; k++)
            {
                float d = total * (0.03f + 0.94f * k / (N - 1));
                int j = 1;
                while (j < mids.Count - 1 && cum[j] < d) j++;
                float t = (d - cum[j - 1]) / Math.Max(1e-3f, cum[j] - cum[j - 1]);
                pts[k] = Vector2.Lerp(mids[j - 1], mids[j], Math.Clamp(t, 0, 1));
            }
            return pts;
        }

        // room for the letters, centring (arcs only), letter height, terrain heights
        Label? Finish(Vector2[] points, bool rigid, double extent)
        {
            int np = points.Length;
            var up = new float[np];
            var down = new float[np];
            var normals = new Vector2[np];
            for (int i = 0; i < np; i++)
            {
                if (rigid) normals[i] = across;
                else
                {
                    var d = points[Math.Min(np - 1, i + 1)] - points[Math.Max(0, i - 1)];
                    normals[i] = d.LengthSquared() < 1e-6f ? across : Vector2.Normalize(new Vector2(-d.Y, d.X));
                }
                up[i] = Room(points[i], -normals[i]);
                down[i] = Room(points[i], normals[i]);
            }
            var inner = Enumerable.Range(np / 8, np - 2 * (np / 8)).ToList();
            float shift = 0;
            if (rigid)
            {
                // move the whole arc (without bending it) to the middle of the room
                shift = inner.Select(i => (down[i] - up[i]) / 2).OrderBy(v => v).ElementAt(inner.Count / 2);
                for (int i = 0; i < np; i++) points[i] += across * shift;
            }
            var room = inner.Select(i => 2 * Math.Min(up[i] + shift, down[i] - shift)).Select(v => Math.Max(0f, v)).OrderBy(v => v).ToList();
            float len = 0;
            for (int i = 1; i < np; i++) len += Vector2.Distance(points[i - 1], points[i]);
            if (len < 1) len = 1;
            // letter height: as tall as the country allows along the line (a few narrow spots may be crossed) and the name's length
            float byThick = room.Count > 0 ? room[room.Count / 2] * 0.9f : (float)(maxThick * 0.5);
            float byLength = len / Math.Max(3, text.Length * 0.62f);
            float fontPx = Math.Max(1.5f, Math.Min(byThick, byLength));
            return new Label { Text = text, Points = points, FontPx = fontPx, LengthPx = len, Coverage = (float)Math.Min(1, Math.Min(len, fontPx * (text.Length * 0.62f + MaxSpacing * (text.Length - 1))) / Math.Max(1, extent)) };
        }
        }
    }

    // ------------------------------------------------------------------------------------- drawing
    // widest gap between letters, in letter heights: long names spread out over long countries
    const float MaxSpacing = 2.0f;
    private const double MorphSeconds = 0.7;
    // letters taller than this share of the window start to fade, and are gone at the larger one
    private const float FadeBigStart = 0.12f, FadeBigEnd = 0.24f;
    // share of the name's curve that is on screen: below the first it is gone, above the second fully visible
    private const float FadeSeenStart = 0.3f, FadeSeenEnd = 0.65f;
    private static float SmoothStep(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }
    private static bool OnScreen(Vector2 q) => q.X >= 0 && q.X <= Viewport.W && q.Y >= 0 && q.Y <= Viewport.H;

    private int _pollFrame, _signature;

    /// <summary>
    /// Not every change to what a label shows (ruler, party, ideology, subject status, annexation, a name set directly) raises
    /// MapChanged, so a few times a second the names are re-derived and a signature of what the labels are made from is compared.
    /// </summary>
    private void PollChanges()
    {
        if (++_pollFrame % 15 != 0) return;
        _game.RefreshNames();
        var h = new HashCode();
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists) continue;
            h.Add(c.Index); h.Add(c.Name); h.Add(c.Capital?.Index ?? -1);
        }
        foreach (var l in _game.World.Locations) h.Add(l.Owner?.Index ?? -1);
        int sig = h.ToHashCode();
        if (sig != _signature) { _signature = sig; MarkDirty(); }
    }

    private readonly Dictionary<int, Dictionary<char, float>> _advBySize = new();   // glyph advances by pixel size, kept between frames
    private float[] _adv = new float[64];
    private static readonly Dictionary<char, IntPtr> GlyphText = new();
    private static unsafe sbyte* GlyphZ(char ch)
    {
        if (!GlyphText.TryGetValue(ch, out var ptr))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(ch.ToString());
            ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length + 1);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
            System.Runtime.InteropServices.Marshal.WriteByte(ptr, bytes.Length, 0);
            GlyphText[ch] = ptr;
        }
        return (sbyte*)ptr;
    }

    public unsafe void Draw(Camera3D cam, float minScreenFont = 3f)
    {
        PollChanges();
        RefreshLabels();
        float ppu = _game.World.Map.Settings.PixelsPerUnit;
        var font = Assets.HeaderFont;
        var scr = new Vector2[24];
        var cum = new float[24];
        var tan = new Vector2[24];
        double clock = Raylib.GetTime();
        // Glyph advances only depend on (font, char, size); measuring every letter of every label
        // each frame (Raylib MeasureTextEx -> glyph-atlas lookup) dominated the frame time while
        // panning. Cache them; sizes are rounded to whole pixels so the cache stays small and bounded.
        Dictionary<char, float>? advCache = null;
        int advCacheSize = 0;
        var advBySize = _advBySize;
        var forward = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var lb in _labels)
        {
            int n = lb.Points.Length;
            // a name that was refitted glides from its old curve to the new one
            var pts = lb.Points;
            float fontPx = lb.FontPx, lengthPx = lb.LengthPx;
            if (lb.FromPoints != null && lb.FromPoints.Length == n)
            {
                float mt = (float)((clock - lb.MorphAt) / MorphSeconds);
                if (mt >= 1) lb.FromPoints = null;
                else
                {
                    float e = mt <= 0 ? 0 : mt * mt * (3 - 2 * mt);
                    pts = new Vector2[n];
                    for (int i = 0; i < n; i++) pts[i] = Vector2.Lerp(lb.FromPoints[i], lb.Points[i], e);
                    fontPx = float.Lerp(lb.FromFontPx, lb.FontPx, e);
                    lengthPx = float.Lerp(lb.FromLengthPx, lb.LengthPx, e);
                }
            }
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool behindCamera = false;
            float anchorX = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                float wxp = p.X / ppu;
                // flat map: keep the whole name in one copy of the wrapping world (a name near the seam must not split across both edges)
                if (!_map.Globe && i > 0) wxp += _map.WorldWidth * MathF.Round((anchorX - wxp) / _map.WorldWidth);
                var world = !_map.Globe && i > 0 ? new Vector3(wxp, lb.Heights[i], p.Y / ppu) : _map.PlaceAt(wxp, p.Y / ppu, lb.Heights[i]);
                if (i == 0) anchorX = _map.Globe ? wxp : world.X;
                // A point behind the eye projects mirrored and magnified (see Viewport.TryProjectSnapped), which made
                // names flash across the screen while panning. A name that touches that region is not drawn at all.
                if (Viewport.DepthAlong(world, cam.Position, forward) < Viewport.MinProjectionDepth || Viewport.IsHidden(world, cam)) { behindCamera = true; break; }
                scr[i] = Viewport.ToScreen(world, cam); // not snapped: whole-pixel vertices made the letters step while the camera moved
                minX = Math.Min(minX, scr[i].X); maxX = Math.Max(maxX, scr[i].X); minY = Math.Min(minY, scr[i].Y); maxY = Math.Max(maxY, scr[i].Y);
            }
            if (behindCamera) continue;
            if (maxX < -100 || minX > Viewport.W + 100 || maxY < -100 || minY > Viewport.H + 100) continue;
            // left to right on screen
            if (scr[0].X > scr[n - 1].X) Array.Reverse(scr, 0, n);
            cum[0] = 0;
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(scr[i - 1], scr[i]);
            float L = cum[n - 1];
            if (L < 2) continue;
            float fs = fontPx * (L / lengthPx);
            if (fs < minScreenFont) continue;
            // Too big to read as a name, or too little of it on screen: it fades out. It never shrinks or stops growing
            // with the land, and it is not pinned to the screen edge.
            float fade = 1f - SmoothStep(Viewport.H * FadeBigStart, Viewport.H * FadeBigEnd, fs);
            if (fade > 0f)
            {
                int inView = 0, samples = 0;
                for (int i = 0; i < n; i++)
                {
                    samples++; if (OnScreen(scr[i])) inView++;
                    if (i > 0) { samples++; if (OnScreen((scr[i] + scr[i - 1]) * 0.5f)) inView++; }
                }
                fade *= SmoothStep(FadeSeenStart, FadeSeenEnd, (float)inView / samples);
            }
            if (fade < 0.02f) continue;
            // glyph advances
            int keySize = (int)Math.Round(fs);
            float kscale = keySize / fs;
            if (advCacheSize != keySize) { if (!advBySize.TryGetValue(keySize, out advCache)) advBySize[keySize] = advCache = new Dictionary<char, float>(); advCacheSize = keySize; }
            float total = 0;
            if (_adv.Length < lb.Text.Length) _adv = new float[lb.Text.Length * 2];
            var adv = _adv;
            for (int i = 0; i < lb.Text.Length; i++)
            {
                char ch = lb.Text[i];
                if (!advCache.TryGetValue(ch, out float a))
                {
                    a = Raylib.MeasureTextEx(font, ch.ToString(), keySize, 0).X;
                    advCache[ch] = a;
                }
                adv[i] = a *= kscale;
                total += adv[i];
            }
            float avail = L * 0.9f;   // a little room at both ends, so neighbouring names do not touch
            if (total > avail) { float k = avail / total; fs *= k; for (int i = 0; i < lb.Text.Length; i++) adv[i] *= k; total = avail; if (fs < minScreenFont) continue; }
            float spacing = lb.Text.Length > 1 ? Math.Clamp((avail - total) / (lb.Text.Length - 1), 0, fs * MaxSpacing) : 0;
            float used = total + spacing * (lb.Text.Length - 1);
            float pos = (L - used) / 2;
            // solid letters with a dark outline, so names read on any map color (fading in only while tiny)
            byte alpha = (byte)(Math.Clamp(150 + (fs - minScreenFont) * 30, 150, 245) * fade);
            var col = new Color((byte)250, (byte)246, (byte)230, alpha);
            var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.8f));
            float so = Math.Max(1f, fs / 22f);
            // direction of the curve at each point, blended along the segments: letters turn smoothly instead of snapping at every corner
            for (int i = 0; i < n; i++)
            {
                var dv = scr[Math.Min(n - 1, i + 1)] - scr[Math.Max(0, i - 1)];
                tan[i] = dv.LengthSquared() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(dv);
            }
            for (int i = 0; i < lb.Text.Length; i++)
            {
                float center = pos + adv[i] / 2;
                pos += adv[i] + spacing;
                char ch = lb.Text[i];
                if (ch == ' ') continue;
                int k = 1;
                while (k < n - 1 && cum[k] < center) k++;
                float t = (center - cum[k - 1]) / Math.Max(0.001f, cum[k] - cum[k - 1]);
                var p = Vector2.Lerp(scr[k - 1], scr[k], Math.Clamp(t, 0, 1));
                var d = Vector2.Lerp(tan[k - 1], tan[k], Math.Clamp(t, 0, 1));
                float rot = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
                var origin = new Vector2(adv[i] / 2, fs / 2);
                sbyte* z = GlyphZ(ch);   // (the letter as text the library reads as it is: a new string and a copy of it for each of the four draws were most of the garbage of a frame)
                Raylib.DrawTextPro(font, z, p + new Vector2(so, so), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, z, p + new Vector2(-so * 0.6f, so * 0.6f), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, z, p + new Vector2(so * 0.6f, -so * 0.6f), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, z, p, origin, rot, fs, 0, col);
            }
        }
    }
}
