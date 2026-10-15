using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Script;

namespace Burger.Core.Geography;

/// <summary>
/// Reads <c>history/geography/*.txt</c>. Blocks: <c>settings</c>, <c>property</c>, <c>province</c>, <c>track</c>, <c>split</c>, <c>merge</c>,
/// <c>reorganize</c>, <c>feature</c>, <c>density</c>. A project without that folder gets an empty registry and behaves exactly as before.
/// </summary>
public static class GeoLoader
{
    public const string Folder = "history/geography";

    public static void Load(Game game)
    {
        var reg = game.Geo;
        var files = DataLoader.Files(game.Data, Folder).ToList();
        if (files.Count == 0) return;
        foreach (var (path, display) in files)
            LoadNode(reg, ScriptParser.ParseFile(path, display));
        Finish(reg);
        // weights for area-proportional splits: spherical area, computed once and only when the map is loaded
        var world = game.World;
        double[]? areas = null;
        reg.AtomWeight = atom =>
        {
            areas ??= GeoMetrics.LocationAreasKm2(world);
            var loc = world.GetLocation(atom);
            return loc != null && loc.Index < areas.Length ? areas[loc.Index] : 1.0;
        };
        foreach (var e in reg.Rebuild()) reg.Issue(GeoSeverity.Error, e);
        Install(game);
        foreach (var issue in GeoValidator.Run(reg, game.World))
        {
            if (issue.Severity == GeoSeverity.Error) Log.Error("Geography: " + issue.Message, issue.Where);
            else if (issue.Severity == GeoSeverity.Warning) Log.Warn("Geography: " + issue.Message, issue.Where);
        }
        Log.Info($"Geography: {reg.AllProvinces.Count(p => p.Parents.Length == 0)} initial provinces, {reg.Operations.Count} reorganizations, {reg.Features.Count} features");
    }

    /// <summary>Parses one file's blocks into the registry (public so tools and tests can load without a game).</summary>
    public static void LoadNode(GeoRegistry reg, Node root)
    {
        foreach (var n in root.All)
        {
            if (n.Children == null) { reg.Issue(GeoSeverity.Error, $"expected a block, found '{n.Key}'", n.Location); continue; }
            switch (n.Key.ToLowerInvariant())
            {
                case "settings":
                    reg.RequireFullCoverage = string.Equals(n.GetString("coverage"), "full", StringComparison.OrdinalIgnoreCase);
                    if (n.GetString("coverage_date") is { } cd) { if (TryDate(reg, cd, n, out int d)) reg.CoverageDay = d; }
                    reg.DensityProperty = n.GetString("density_property", reg.DensityProperty)!;
                    var ds = reg.DynamicSettings;
                    if (n.Has("dynamic")) ds.Enabled = n.GetBool("dynamic");
                    if (n.Has("split_population")) ds.SplitPopulation = n.GetDouble("split_population");
                    if (n.Has("merge_ceiling")) ds.MergeCeiling = n.GetDouble("merge_ceiling");
                    if (n.Has("min_age_days")) ds.MinAgeDays = (int)n.GetDouble("min_age_days");
                    if (n.Has("front_min_age_days")) ds.FrontMinAgeDays = (int)n.GetDouble("front_min_age_days");
                    if (n.Has("max_changes_per_month")) ds.MaxChangesPerMonth = (int)n.GetDouble("max_changes_per_month");
                    break;
                case "property":
                    {
                        string key = n.GetString("key") ?? "";
                        if (key.Length == 0) { reg.Issue(GeoSeverity.Error, "property needs a key", n.Location); break; }
                        var kind = (n.GetString("kind", "categorical") ?? "").ToLowerInvariant() switch
                        {
                            "extensive" => PropertyKind.Extensive, "intensive" => PropertyKind.Intensive, "set" => PropertyKind.Set, _ => PropertyKind.Categorical,
                        };
                        reg.DefineProperty(new PropertyDef(key.ToLowerInvariant(), kind, n.GetBool("integer")));
                        break;
                    }
                case "province":
                    {
                        string id = n.GetString("id") ?? "";
                        int from = HistoricalCalendar.Beginning;
                        if (n.GetString("from") is { } f && !TryDate(reg, f, n, out from)) break;
                        var err = reg.CreateProvince(id, n.GetList("atoms"), from, n.GetBool("archipelago"), n.Location);
                        if (err != null) reg.Issue(GeoSeverity.Error, err, n.Location);
                        break;
                    }
                case "track":
                    LoadTrack(reg, n);
                    break;
                case "split":
                case "merge":
                case "reorganize":
                    {
                        var op = ParseOperation(reg, n);
                        if (op != null) reg.AddOperation(op);
                        break;
                    }
                case "feature":
                    LoadFeature(reg, n);
                    break;
                case "density":
                    reg.DensityTargets.Add(new DensityTarget
                    {
                        Group = n.GetString("group") ?? "", Share = n.GetDouble("share"),
                        MinAreaKm2 = n.GetDouble("min_area_km2"), MaxAreaKm2 = n.GetDouble("max_area_km2"),
                    });
                    break;
                default:
                    reg.Issue(GeoSeverity.Error, $"unknown geography block '{n.Key}'", n.Location);
                    break;
            }
        }
    }

    private static bool TryDate(GeoRegistry reg, string text, Node n, out int days)
    {
        if (HistoricalCalendar.TryParse(text, out days, out var err)) return true;
        reg.Issue(GeoSeverity.Error, err ?? "bad date", n.Location);
        return false;
    }

    private static void LoadTrack(GeoRegistry reg, Node n)
    {
        string subject = n.GetString("subject") ?? "", prop = n.GetString("property") ?? "";
        if (subject.Length == 0 || prop.Length == 0) { reg.Issue(GeoSeverity.Error, "track needs subject and property", n.Location); return; }
        foreach (var item in n.All)
        {
            if (item.Key is "subject" or "property") continue;
            if (!HistoricalCalendar.TryParse(item.Key, out int day, out var err)) { reg.Issue(GeoSeverity.Error, err ?? "bad date", item.Location); continue; }
            if (item.Value == null && item.Children == null) { reg.Issue(GeoSeverity.Error, $"track date '{item.Key}' needs a value", item.Location); continue; }
            string value = item.Value ?? string.Join("|", item.Items().OrderBy(x => x, StringComparer.Ordinal));
            if (reg.PropertyOf(prop).Kind is PropertyKind.Extensive or PropertyKind.Intensive
                && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            { reg.Issue(GeoSeverity.Error, $"{subject}.{prop}: '{value}' is not a number", item.Location); continue; }
            reg.SetValue(subject, prop, day, value, item.Location);
        }
    }

    /// <summary>Parses a split, merge or reorganize block. Reports problems into the registry and returns null if unusable.</summary>
    public static GeoOperation? ParseOperation(GeoRegistry reg, Node n)
    {
        if (!TryDate(reg, n.GetString("date") ?? "", n, out int date)) return null;
        var op = new GeoOperation { Date = date, Reason = n.GetString("reason") ?? "", Source = n.Location };
        var parents = n.GetList("parents");
        if (n.GetString("parent") is { } single) parents.Add(single);
        op.Parents = parents.ToArray();
        foreach (var c in n.GetAll("child").Concat(n.GetAll("into")))
        {
            op.Children.Add(new GeoChildSpec
            {
                Id = c.GetString("id") ?? "", Name = c.GetString("name"), Atoms = c.GetList("atoms").ToArray(), Archipelago = c.GetBool("archipelago"),
                Share = c.Has("share") ? c.GetDouble("share") : null,
            });
        }
        if (op.Parents.Length == 0 || op.Children.Count == 0) { reg.Issue(GeoSeverity.Error, $"{n.Key} needs parents and children", n.Location); return null; }
        return op;
    }

    private static void LoadFeature(GeoRegistry reg, Node n)
    {
        var f = new GeoFeature
        {
            Id = n.GetString("id") ?? "", Name = n.GetString("name") ?? "", Atoms = n.GetList("atoms").ToArray(), Source = n.Location,
            Kind = Enum.TryParse<GeoFeatureKind>(n.GetString("kind", "other"), true, out var k) ? k : GeoFeatureKind.Other,
        };
        if (n.GetString("from") is { } a && TryDate(reg, a, n, out int from)) f.From = from;
        if (n.GetString("to") is { } b && TryDate(reg, b, n, out int to)) f.To = to;
        var err = reg.AddFeature(f);
        if (err != null) reg.Issue(GeoSeverity.Error, err, n.Location);
    }

    private static void Finish(GeoRegistry reg) { }

    /// <summary>Runtime hooks: a save section for operations applied while playing, and a console command to inspect the layer.</summary>
    public static void Install(Game game)
    {
        var reg = game.Geo;
        game.SaveSections.Add(new Burger.Core.Modding.SaveSection
        {
            Name = "geography",
            Reset = () => { reg.ResetRuntime(); game.GeoDynamics.Invalidate(); },
            Write = root =>
            {
                root.Add("version", "1");
                foreach (var op in reg.DynamicOperations) root.Children!.Add(OperationNode(op));
            },
            Read = root =>
            {
                foreach (var n in root.All)
                {
                    if (!n.Key.Equals("operation", StringComparison.OrdinalIgnoreCase)) continue;
                    var op = ParseOperation(reg, n);
                    if (op != null && reg.ApplyRuntime(op) is { } err) Log.Error("Geography: saved operation failed: " + err, n.Location);
                }
            },
        });
        game.Commands.Register("geo", "geo <location> - the historical province, lineage and owner of a location today", a =>
        {
            if (a.Length == 0) return "usage: geo <location key>";
            int day = game.World.Date.TotalDays;
            var p = reg.ProvinceOfAtom(a[0], day);
            if (p == null) return $"{a[0]} is not part of any historical province on {HistoricalCalendar.Format(day)}";
            string anc = string.Join(", ", reg.Ancestors(p.Id).Select(x => x.Id));
            return $"{a[0]} -> {p.Id} \"{reg.Name(p.Id, day)}\" owner {reg.Owner(p.Id, day) ?? "-"} state {reg.Region(p.Id, day) ?? "-"}, " +
                   $"{p.Atoms.Length} atoms, since {HistoricalCalendar.Format(p.From)}" + (anc.Length > 0 ? $", descended from {anc}" : "");
        });
    }

    public static Node OperationNode(GeoOperation op)
    {
        var n = new Node("operation", new List<Node>());
        n.Add("date", HistoricalCalendar.ToScript(op.Date));
        if (op.Reason.Length > 0) n.Add("reason", op.Reason, true);
        var parents = n.AddBlock("parents");
        foreach (var p in op.Parents) parents.AddItem(p);
        foreach (var c in op.Children)
        {
            var cb = n.AddBlock("child");
            cb.Add("id", c.Id);
            if (!string.IsNullOrEmpty(c.Name)) cb.Add("name", c.Name, true);
            if (c.Share.HasValue) cb.Add("share", c.Share.Value.ToString("R", CultureInfo.InvariantCulture));
            if (c.Archipelago) cb.Add("archipelago", "yes");
            var atoms = cb.AddBlock("atoms");
            foreach (var a in c.Atoms) atoms.AddItem(a);
        }
        return n;
    }
}
