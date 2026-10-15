using System.Globalization;

namespace Burger.Core.Geography;

/// <summary>
/// The historical province layer. Locations stay the permanent geographic atoms (their keys never change); this registry groups atoms into
/// playable provinces over time, records lineage when provinces split, merge or are redrawn, and keeps every temporal property (owner, name,
/// population, ...) as a <see cref="Timeline{T}"/> keyed by subject (a province id or an atom key) and property.
/// Everything is derived deterministically from the data files plus any runtime operations, in date order; nothing here is touched unless
/// a project has <c>history/geography</c>. See docs/game/GEOGRAPHY.md.
/// </summary>
public sealed class GeoRegistry
{
    private readonly Dictionary<string, GeoProvince> _provinces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyDef> _props = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Subject, string Prop), Timeline<string>> _tracks = new();
    private readonly Dictionary<string, List<GeoProvince>> _byAtom = new(StringComparer.Ordinal);
    private readonly List<GeoOperation> _ops = new();
    private readonly List<GeoOperation> _dynamicOps = new();
    private readonly List<GeoOperation> _applied = new();
    private readonly Dictionary<string, GeoFeature> _features = new(StringComparer.Ordinal);
    private readonly Dictionary<int, GeoSnapshot> _snapshots = new();
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
    private readonly List<GeoIssue> _issues = new();
    private readonly List<(string Subject, string Prop, int Day, string Value, string Where)> _explicit = new();

    public readonly List<DensityTarget> DensityTargets = new();
    public string DensityProperty = "continent";
    /// <summary>When true the validator demands that every land atom is covered by a province on <see cref="CoverageDay"/>.</summary>
    public bool RequireFullCoverage;
    public int CoverageDay = HistoricalCalendar.Beginning;
    /// <summary>Relative weight of an atom when a quantity is split by area (spherical km2 once a map is loaded; 1 otherwise).</summary>
    public Func<string, double> AtomWeight { get; set; } = _ => 1.0;
    /// <summary>Increases on every change; caches compare against it.</summary>
    public int Version { get; private set; }

    public bool Enabled => _provinces.Count > 0 || _features.Count > 0;
    public IEnumerable<GeoProvince> AllProvinces => _provinces.Values;
    public IReadOnlyList<GeoOperation> Operations => _ops;
    public IReadOnlyList<GeoOperation> DynamicOperations => _dynamicOps;
    public IReadOnlyCollection<GeoFeature> Features => _features.Values;
    public IReadOnlyList<GeoIssue> LoadIssues => _issues;
    public IEnumerable<PropertyDef> Properties => _props.Values;

    public void Issue(GeoSeverity s, string message, string where = "") => _issues.Add(new GeoIssue(s, message, where));

    // ------------------------------------------------------------------ properties
    public void DefineProperty(PropertyDef def) => _props[def.Key] = def;
    public PropertyDef PropertyOf(string key) => _props.TryGetValue(key, out var d) ? d : new PropertyDef(key, PropertyKind.Categorical);

    // ------------------------------------------------------------------ provinces
    /// <summary>Creates an initial province (no parents). Returns an error text, or null on success.</summary>
    public string? CreateProvince(string id, IEnumerable<string> atoms, int from, bool archipelago = false, string source = "")
    {
        if (string.IsNullOrWhiteSpace(id)) return "province needs an id";
        if (!_usedIds.Add(id)) return $"province id '{id}' is already used or was retired; ids are never reused";
        var sorted = atoms.Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        if (sorted.Length == 0) { _usedIds.Remove(id); return $"province '{id}' has no atoms"; }
        var p = new GeoProvince { Id = id, Atoms = sorted, From = from, Archipelago = archipelago, Source = source };
        _provinces[id] = p;
        IndexProvince(p);
        Touch();
        return null;
    }

    public GeoProvince? Get(string id) => _provinces.TryGetValue(id, out var p) ? p : null;

    private void IndexProvince(GeoProvince p)
    {
        foreach (var a in p.Atoms)
        {
            if (!_byAtom.TryGetValue(a, out var list)) _byAtom[a] = list = new List<GeoProvince>();
            int i = list.FindIndex(x => x.From > p.From);
            if (i < 0) list.Add(p); else list.Insert(i, p);
        }
    }

    /// <summary>The province holding an atom on a day, or null. Binary search over that atom's (short) history.</summary>
    public GeoProvince? ProvinceOfAtom(string atom, int day)
    {
        if (!_byAtom.TryGetValue(atom, out var list)) return null;
        int lo = 0, hi = list.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (list[mid].From <= day) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        // overlapping definitions are reported by the validator; return the latest-starting active one
        for (int i = found; i >= 0; i--) if (list[i].ActiveOn(day)) return list[i];
        return null;
    }

    public IEnumerable<GeoProvince> ProvincesOfAtom(string atom) => _byAtom.TryGetValue(atom, out var l) ? l : Enumerable.Empty<GeoProvince>();

    public GeoSnapshot Snapshot(int day)
    {
        if (_snapshots.TryGetValue(day, out var s)) return s;
        if (_snapshots.Count > 16) _snapshots.Clear();
        s = new GeoSnapshot(this, day, _provinces.Values.Where(p => p.ActiveOn(day)).OrderBy(p => p.Id, StringComparer.Ordinal).ToList());
        _snapshots[day] = s;
        return s;
    }

    // ------------------------------------------------------------------ lineage
    public IEnumerable<GeoProvince> Ancestors(string id)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
            if (Get(stack.Pop()) is { } p)
                foreach (var par in p.Parents) if (seen.Add(par)) { if (Get(par) is { } pp) yield return pp; stack.Push(par); }
    }

    public IEnumerable<GeoProvince> Descendants(string id)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
            if (Get(stack.Pop()) is { } p)
                foreach (var c in p.Children) if (seen.Add(c)) { if (Get(c) is { } cc) yield return cc; stack.Push(c); }
    }

    /// <summary>
    /// What an old reference means on a day: the province itself if it exists then, its descendants active that day if it was retired
    /// (a split yields several, a merge the merged province), or its ancestors if the day is before it was created. Never an unrelated territory.
    /// </summary>
    public IReadOnlyList<GeoProvince> ResolveAt(string id, int day)
    {
        var p = Get(id);
        if (p == null) return Array.Empty<GeoProvince>();
        if (p.ActiveOn(day)) return new[] { p };
        var pool = day >= p.To ? Descendants(id) : Ancestors(id);
        return pool.Where(x => x.ActiveOn(day)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ temporal properties
    /// <summary>Records an explicit value from a data file. Conflicting values on one day are reported, not silently resolved.</summary>
    public bool SetValue(string subject, string prop, int day, string value, string where = "")
    {
        var key = (subject, prop.ToLowerInvariant());
        NoteProp(key.Item2);
        if (!_tracks.TryGetValue(key, out var tl)) _tracks[key] = tl = new Timeline<string>();
        if (!tl.TrySet(day, value))
        {
            Issue(GeoSeverity.Error, $"conflicting values for {subject}.{prop} on {HistoricalCalendar.Format(day)}: '{(tl.TryAt(day, out var old) ? old : "?")}' and '{value}'", where);
            return false;
        }
        _explicit.Add((subject, key.Item2, day, value, where));
        Touch();
        return true;
    }

    public IReadOnlyList<(string Subject, string Prop, int Day, string Value, string Where)> ExplicitValues => _explicit;

    /// <summary>Settings of the runtime province system (see <see cref="GeoDynamics"/>).</summary>
    public GeoDynamicSettings DynamicSettings = new();

    /// <summary>
    /// Records a value computed from the running simulation (the owner or controller a province has today). Unlike <see cref="SetValue"/> it is not
    /// part of the data files: it is recomputed from the game state after a load, so it is never saved. Returns false if the day is already taken by an other value.
    /// </summary>
    public bool SetDerived(string subject, string prop, int day, string value)
    {
        var key = (subject, prop.ToLowerInvariant());
        NoteProp(key.Item2);
        if (!_tracks.TryGetValue(key, out var tl)) _tracks[key] = tl = new Timeline<string>();
        if (tl.TryAt(day, out var cur) && cur == value) return true;
        bool ok = tl.TrySet(day, value);
        if (ok) Touch();
        return ok;
    }

    public Timeline<string>? Track(string subject, string prop) => _tracks.TryGetValue((subject, prop.ToLowerInvariant()), out var t) ? t : null;

    /// <summary>
    /// The value of a property on a day. A province uses its own timeline; if that has no value yet (nothing recorded or derived),
    /// the values recorded on its atoms are combined by the property's kind. An atom key reads its own timeline.
    /// </summary>
    public string? Value(string subject, string prop, int day)
    {
        prop = prop.ToLowerInvariant();
        if (_tracks.TryGetValue((subject, prop), out var tl) && tl.TryAt(day, out var v))
        {
            var p0 = Get(subject);
            if (p0 == null || p0.ActiveOn(day)) return v;
            return null;
        }
        var p = Get(subject);
        if (p == null || !p.ActiveOn(day)) return null;
        var parts = new List<(string Value, double Weight)>();
        foreach (var a in p.Atoms)
            if (_tracks.TryGetValue((a, prop), out var at) && at.TryAt(day, out var av)) parts.Add((av, Weight(a)));
        return parts.Count == 0 ? null : Combine(PropertyOf(prop), parts);
    }

    public double Number(string subject, string prop, int day, double fallback = 0)
        => Value(subject, prop, day) is { } s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public string? Name(string provinceId, int day) => Value(provinceId, "name", day);
    public string? Region(string provinceId, int day) => Value(provinceId, "state", day);
    public string? Owner(string provinceId, int day) => Value(provinceId, "owner", day);
    public string? Controller(string provinceId, int day) => Value(provinceId, "controller", day) ?? Owner(provinceId, day);

    private double Weight(string atom)
    {
        double w = AtomWeight(atom);
        return w > 0 ? w : 1e-9;
    }

    // ------------------------------------------------------------------ features
    public string? AddFeature(GeoFeature f)
    {
        if (string.IsNullOrWhiteSpace(f.Id)) return "feature needs an id";
        if (_features.ContainsKey(f.Id)) return $"feature '{f.Id}' is defined twice";
        _features[f.Id] = f;
        Touch();
        return null;
    }
    public IEnumerable<GeoFeature> FeaturesAt(int day) => _features.Values.Where(f => f.ActiveOn(day));
    public IEnumerable<GeoFeature> FeaturesOfAtom(string atom, int day) => _features.Values.Where(f => f.ActiveOn(day) && Array.IndexOf(f.Atoms, atom) >= 0);

    // ------------------------------------------------------------------ reorganization
    /// <summary>Queues an operation from a data file; applied in date order by <see cref="Rebuild"/>.</summary>
    public void AddOperation(GeoOperation op) { _ops.Add(op); Touch(); }

    /// <summary>
    /// Applies a runtime operation (scenario event). Returns an error text, or null on success. An operation dated after everything applied so far
    /// is applied incrementally; an earlier one rebuilds the derived state from the data files (rare).
    /// </summary>
    public string? ApplyRuntime(GeoOperation op)
    {
        int last = _applied.Count == 0 ? HistoricalCalendar.Beginning : _applied[^1].Date;
        if (op.Date >= last)
        {
            var err = Apply(op);
            if (err != null) return err;
            _dynamicOps.Add(op);
            return null;
        }
        int baseline = Rebuild().Count;
        _dynamicOps.Add(op);
        var issues = Rebuild();
        if (issues.Count > baseline) { _dynamicOps.Remove(op); Rebuild(); return issues[^1]; }
        return null;
    }

    /// <summary>Forgets runtime operations (before a save is loaded) and replays the data files.</summary>
    public void ResetRuntime() { if (_dynamicOps.Count > 0) { _dynamicOps.Clear(); Rebuild(); } }

    /// <summary>Replays all operations from scratch, in (date, file order). Returns the error texts of operations that failed.</summary>
    public List<string> Rebuild()
    {
        var errors = new List<string>();
        // reset derived state: drop provinces made by operations, restore initial ones, drop derived track points
        foreach (var id in _provinces.Where(kv => kv.Value.Parents.Length > 0).Select(kv => kv.Key).ToList())
        {
            var p = _provinces[id];
            foreach (var a in p.Atoms) _byAtom[a].Remove(p);
            _provinces.Remove(id);
            _usedIds.Remove(id);
        }
        foreach (var p in _provinces.Values) { p.To = HistoricalCalendar.Forever; p.Children.Clear(); }
        _tracks.Clear();
        foreach (var e in _explicit)
        {
            if (!_tracks.TryGetValue((e.Subject, e.Prop), out var tl)) _tracks[(e.Subject, e.Prop)] = tl = new Timeline<string>();
            tl.TrySet(e.Day, e.Value);
        }
        _applied.Clear();
        _snapshots.Clear();
        var all = _ops.Concat(_dynamicOps).Select((o, i) => (o, i)).OrderBy(x => x.o.Date).ThenBy(x => x.i).Select(x => x.o).ToList();
        foreach (var op in all)
        {
            var err = Apply(op);
            if (err != null) errors.Add($"{err}" + (op.Source.Length > 0 ? $" ({op.Source})" : ""));
        }
        Touch();
        return errors;
    }

    private string? Apply(GeoOperation op)
    {
        if (op.Parents.Length == 0 || op.Children.Count == 0) return "an operation needs at least one parent and one child";
        var parents = new List<GeoProvince>();
        foreach (var pid in op.Parents)
        {
            var p = Get(pid);
            if (p == null) return $"{op.Kind} on {HistoricalCalendar.Format(op.Date)}: unknown parent '{pid}'";
            if (parents.Contains(p)) return $"{op.Kind}: parent '{pid}' listed twice";
            if (op.Date <= p.From) return $"{op.Kind}: '{pid}' is created {HistoricalCalendar.Format(p.From)} and cannot change on or before that day";
            if (!p.ActiveOn(op.Date)) return $"{op.Kind}: parent '{pid}' is not active on {HistoricalCalendar.Format(op.Date)} (retired {HistoricalCalendar.Format(p.To)})";
            parents.Add(p);
        }
        var parentAtoms = new HashSet<string>(parents.SelectMany(p => p.Atoms), StringComparer.Ordinal);
        // merge shorthand: a single child without atoms takes every atom of its parents
        if (op.Children.Count == 1 && op.Children[0].Atoms.Length == 0) op.Children[0].Atoms = parentAtoms.OrderBy(a => a, StringComparer.Ordinal).ToArray();
        var childAtoms = new HashSet<string>(StringComparer.Ordinal);
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in op.Children)
        {
            if (string.IsNullOrWhiteSpace(c.Id)) return $"{op.Kind}: child without id";
            if (_usedIds.Contains(c.Id) || !childIds.Add(c.Id)) return $"{op.Kind}: child id '{c.Id}' is already used or retired; ids are never reused";
            if (c.Atoms.Length == 0) return $"{op.Kind}: child '{c.Id}' has no atoms";
            foreach (var a in c.Atoms)
            {
                if (!parentAtoms.Contains(a)) return $"{op.Kind}: child '{c.Id}' contains atom '{a}' that is not in the parents (territory would appear from nowhere)";
                if (!childAtoms.Add(a)) return $"{op.Kind}: atom '{a}' appears in more than one child (overlap)";
            }
        }
        if (childAtoms.Count != parentAtoms.Count)
            return $"{op.Kind}: children leave {parentAtoms.Count - childAtoms.Count} parent atom(s) uncovered (gap), e.g. '{parentAtoms.First(a => !childAtoms.Contains(a))}'";
        bool anyShare = op.Children.Any(c => c.Share.HasValue);
        if (anyShare)
        {
            if (parents.Count != 1) return $"{op.Kind}: explicit shares are only valid for a single parent";
            if (op.Children.Any(c => !c.Share.HasValue)) return $"{op.Kind}: either every child has a share or none";
            double sum = op.Children.Sum(c => c.Share!.Value);
            if (Math.Abs(sum - 1) > 1e-9 || op.Children.Any(c => c.Share!.Value < 0)) return $"{op.Kind}: shares must be non-negative and sum to 1 (got {sum.ToString(CultureInfo.InvariantCulture)})";
        }

        // snapshot parent values the day before, then create children and retire parents
        int before = op.Date - 1;
        var parentValues = new Dictionary<string, Dictionary<string, string>>();
        foreach (var p in parents)
        {
            var d = new Dictionary<string, string>();
            foreach (var prop in PropertyKeys())
                if (Value(p.Id, prop, before) is { } v) d[prop] = v;
            parentValues[p.Id] = d;
        }
        var created = new List<GeoProvince>();
        foreach (var c in op.Children)
        {
            var np = new GeoProvince
            {
                Id = c.Id, Atoms = c.Atoms.Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToArray(), From = op.Date,
                Parents = parents.Select(p => p.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray(), Reason = op.Reason, Archipelago = c.Archipelago, Source = op.Source,
            };
            _usedIds.Add(np.Id);
            _provinces[np.Id] = np;
            IndexProvince(np);
            created.Add(np);
        }
        foreach (var p in parents)
        {
            p.To = op.Date;
            foreach (var c in created) p.Children.Add(c.Id);
        }
        Derive(op, parents, created, parentValues);
        foreach (var (c, spec) in created.Zip(op.Children))
            if (!string.IsNullOrEmpty(spec.Name)) SetDerived(c.Id, "name", op.Date, spec.Name);
        _applied.Add(op);
        Touch();
        return null;
    }

    private readonly HashSet<string> _propKeys = new(StringComparer.Ordinal);
    private List<string>? _propKeysSorted;
    private IEnumerable<string> PropertyKeys() => _propKeysSorted ??= _propKeys.OrderBy(x => x, StringComparer.Ordinal).ToList();
    private void NoteProp(string prop) { if (_propKeys.Add(prop)) _propKeysSorted = null; }

    private void Derive(GeoOperation op, List<GeoProvince> parents, List<GeoProvince> children, Dictionary<string, Dictionary<string, string>> parentValues)
    {
        var props = parentValues.Values.SelectMany(d => d.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var prop in props)
        {
            var def = PropertyOf(prop);
            var contributors = parents.Where(p => parentValues[p.Id].ContainsKey(prop)).ToList();
            // overlap weight of each (parent, child) pair
            double Overlap(GeoProvince p, GeoProvince c) => p.Atoms.Intersect(c.Atoms, StringComparer.Ordinal).Sum(Weight);
            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            if (def.Kind == PropertyKind.Extensive)
            {
                var exact = children.ToDictionary(c => c.Id, _ => 0.0, StringComparer.Ordinal);
                foreach (var p in contributors)
                {
                    double v = double.Parse(parentValues[p.Id][prop], CultureInfo.InvariantCulture);
                    double total = p.Atoms.Sum(Weight);
                    foreach (var (c, spec) in children.Zip(op.Children))
                    {
                        double share = spec.Share ?? (total > 0 ? Overlap(p, c) / total : 0);
                        exact[c.Id] += v * share;
                    }
                }
                if (def.Integer)
                {
                    long target = (long)Math.Round(exact.Values.Sum());
                    var floors = exact.ToDictionary(kv => kv.Key, kv => (long)Math.Floor(kv.Value + 1e-9), StringComparer.Ordinal);
                    long left = target - floors.Values.Sum();
                    foreach (var id in exact.OrderByDescending(kv => kv.Value - Math.Floor(kv.Value + 1e-9)).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList())
                    {
                        if (left <= 0) break;
                        floors[id]++; left--;
                    }
                    foreach (var kv in floors) results[kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
                }
                else foreach (var kv in exact) results[kv.Key] = kv.Value.ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                foreach (var c in children)
                {
                    var parts = new List<(string Value, double Weight)>();
                    foreach (var p in contributors)
                    {
                        double w = Overlap(p, c);
                        if (w > 0) parts.Add((parentValues[p.Id][prop], w));
                    }
                    if (parts.Count > 0) results[c.Id] = Combine(def, parts);
                }
            }
            foreach (var c in children)
            {
                if (!results.TryGetValue(c.Id, out var val)) continue;
                var key = (c.Id, prop);
                if (!_tracks.TryGetValue(key, out var tl)) _tracks[key] = tl = new Timeline<string>();
                tl.TrySet(op.Date, val); // an explicit value for that day wins
            }
        }
    }

    private static string Combine(PropertyDef def, List<(string Value, double Weight)> parts)
    {
        switch (def.Kind)
        {
            case PropertyKind.Extensive:
                {
                    double s = parts.Sum(p => double.Parse(p.Value, CultureInfo.InvariantCulture));
                    return def.Integer ? ((long)Math.Round(s)).ToString(CultureInfo.InvariantCulture) : s.ToString("R", CultureInfo.InvariantCulture);
                }
            case PropertyKind.Intensive:
                {
                    double w = parts.Sum(p => p.Weight);
                    double s = parts.Sum(p => double.Parse(p.Value, CultureInfo.InvariantCulture) * p.Weight) / w;
                    return s.ToString("R", CultureInfo.InvariantCulture);
                }
            case PropertyKind.Set:
                return string.Join("|", parts.SelectMany(p => p.Value.Split('|', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
            default:
                return parts.GroupBy(p => p.Value).Select(g => (Value: g.Key, W: g.Sum(x => x.Weight)))
                    .OrderByDescending(x => x.W).ThenBy(x => x.Value, StringComparer.Ordinal).First().Value;
        }
    }

    private void Touch() { Version++; _snapshots.Clear(); }
}

/// <summary>The playable provinces on one day.</summary>
public sealed class GeoSnapshot
{
    private readonly GeoRegistry _reg;
    public int Day { get; }
    public IReadOnlyList<GeoProvince> Provinces { get; }
    public GeoSnapshot(GeoRegistry reg, int day, List<GeoProvince> provinces) { _reg = reg; Day = day; Provinces = provinces; }
    public string? NameOf(GeoProvince p) => _reg.Name(p.Id, Day);
    public string? OwnerOf(GeoProvince p) => _reg.Owner(p.Id, Day);
}
