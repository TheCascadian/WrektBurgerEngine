using System.Text;
using Burger.Core;
using Burger.Core.Data;
using Burger.Core.Geography;
using Burger.Core.Model;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>burger borders: compile the plain-text border claims of a project into the geography layer.</summary>
    private static int Borders(List<string> args)
    {
        if (args.Count > 0 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            _failedChecks = 0;
            BordersSelfTest();
            return _failedChecks == 0 ? 0 : 1;
        }
        string usage = "burger borders <build|areas> <project> [--year Y] [--ledger file] [--claims] [--force]";
        if (args.Count < 2) return Fail(usage);
        string verb = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        string project = ResolveProject(RequireProjectArg(rest, usage));
        string game = GameDir(project);
        return verb switch
        {
            "build" => BordersBuild(rest, project, game),
            "areas" => BordersAreas(rest, game),
            _ => Fail(usage),
        };
    }

    /// <summary>The source ledger: <c>--ledger</c>, else <c>provenance/sources.csv</c> in the project, else the repo's map500bce ledger found by walking up.</summary>
    private static string? FindLedger(List<string> args, string project)
    {
        if (Opt(args, "--ledger") is { } given) return given;
        var local = Path.Combine(project, "provenance", "sources.csv");
        if (File.Exists(local)) return local;
        for (var dir = new DirectoryInfo(project); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "tools", "map500bce", "provenance", "sources.csv");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static int BordersBuild(List<string> args, string project, string gameDir)
    {
        int? snapshot = null;
        if (Opt(args, "--year") is { } y)
        {
            if (!HistoricalCalendar.TryParse(y, out int d, out var err)) return Fail(err ?? "bad --year");
            snapshot = d;
        }
        string outFile = Path.Combine(gameDir, "history", "geography", "_generated_borders.txt");
        if (File.Exists(outFile)) File.Delete(outFile); // the map must load without last run's output
        Log.EchoToConsole = false;
        var g = Game.Load(gameDir, compileScripts: false, seed: 1);
        PrintLog();
        var set = BorderLoader.LoadFolder(gameDir, FindLedger(args, project));
        var tl = BorderCompiler.Replay(set);
        var issues = BordersValidator.Run(set, tl, g.World);
        foreach (var i in issues) Console.WriteLine("  " + i);
        int errors = issues.Count(i => i.Severity == GeoSeverity.Error);
        if (errors > 0) return Fail($"{errors} errors; nothing written");

        WriteIfChanged(outFile, BorderCompiler.Geography(set, tl));
        Console.WriteLine($"{set.AreaOrder.Count} areas, {set.Claims.Count} claims -> {Path.GetRelativePath(project, outFile)}");
        if (snapshot is { } sd)
        {
            string states = Path.Combine(gameDir, "map", "states_snapshots", $"{HistoricalCalendar.ToScript(sd)}.txt");
            WriteIfChanged(states, BorderCompiler.States(set, tl, sd));
            Console.WriteLine($"snapshot of {HistoricalCalendar.Format(sd)} -> {Path.GetRelativePath(project, states)}");
        }
        double[]? areas = null;
        double Weight(string a) { areas ??= GeoMetrics.LocationAreasKm2(g.World); return g.World.GetLocation(a) is { } l ? areas[l.Index] : 0; }
        var land = g.World.Locations.Where(l => !l.IsWater).Select(l => l.Key).ToList();
        var days = (snapshot is { } s ? new List<int> { s } : set.Claims.Select(c => c.Day).Distinct().OrderBy(x => x).ToList());
        foreach (var day in days)
        {
            var c = BordersValidator.Coverage(set, tl, day, Weight, land);
            Console.WriteLine($"  {HistoricalCalendar.Format(day),-18} attested {c.AttestedPct,5:0.0}%  generated {c.GeneratedPct,5:0.0}%  unknown {c.UnknownPct,5:0.0}%");
        }
        return 0;
    }

    private static void WriteIfChanged(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>Makes map/areas.txt from map/states: one area per state, cut into pieces that are connected by real land borders.
    /// With --claims also writes one placeholder claim file per owner (source = generated).</summary>
    private static int BordersAreas(List<string> args, string gameDir)
    {
        string path = Path.Combine(gameDir, "map", "areas.txt");
        if (File.Exists(path) && !Flag(args, "--force")) return Fail($"{path} exists; pass --force to overwrite it");
        string date = Opt(args, "--date") ?? "500BCE.1.1";
        if (!HistoricalCalendar.TryParse(date, out _, out var derr)) return Fail(derr ?? "bad --date");
        Log.EchoToConsole = false;
        var g = Game.Load(gameDir, compileScripts: false, seed: 1);
        PrintLog();
        var sb = new StringBuilder("# Made by `burger borders areas` from map/states. A starting point: edit it freely.\n# Each area is a named group of locations; claims in history/borders/ add and remove whole areas.\n");
        var owned = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        int n = 0;
        foreach (var st in g.World.States.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var pieces = Pieces(st.Locations);
            for (int i = 0; i < pieces.Count; i++)
            {
                string name = pieces.Count == 1 ? st.Key : $"{st.Key}_{i + 1}";
                sb.Append(name).Append(" = { ").Append(string.Join(' ', pieces[i])).Append(" }\n");
                n++;
                if (st.Owner != null) { if (!owned.TryGetValue(st.Owner.Tag, out var l)) owned[st.Owner.Tag] = l = new(); l.Add(name); }
            }
        }
        WriteIfChanged(path, sb.ToString());
        Console.WriteLine($"{n} areas from {g.World.States.Count} states -> {path}");
        if (Flag(args, "--claims"))
        {
            foreach (var (tag, names) in owned)
            {
                var cs = new StringBuilder($"# Placeholder: ownership copied from map/states. Nothing here is attested history.\n{tag} = {{\n\t{date} = {{\n\t\tadd = {{ {string.Join(' ', names)} }}\n\t\tcontrol = unknown\n\t\tsource = generated\n\t\tnote = \"Converted from the project's states; not a cited claim.\"\n\t}}\n}}\n");
                WriteIfChanged(Path.Combine(gameDir, "history", "borders", tag + ".txt"), cs.ToString());
            }
            Console.WriteLine($"{owned.Count} claim files -> history/borders");
        }
        return 0;
    }

    /// <summary>Splits locations into groups joined by real land borders (land and water never mix).</summary>
    private static List<List<string>> Pieces(List<Location> locs)
    {
        var left = locs.ToHashSet();
        var result = new List<List<string>>();
        foreach (var start in locs.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (!left.Remove(start)) continue;
            var comp = new List<string> { start.Key };
            var q = new Queue<Location>();
            q.Enqueue(start);
            while (q.Count > 0)
                foreach (var adj in q.Dequeue().Neighbors)
                    if (adj.Kind != AdjacencyKind.Strait && adj.BorderPixels >= GeoValidator.MinBorderPixels && adj.To.IsWater == start.IsWater && left.Remove(adj.To))
                    { comp.Add(adj.To.Key); q.Enqueue(adj.To); }
            result.Add(comp);
        }
        return result;
    }
}

public static partial class Program
{
    private static void BordersSelfTest()
    {
        Console.WriteLine("Border claims:");
        int D(string s) => HistoricalCalendar.TryParse(s, out var d, out _) ? d : int.MinValue + 1;
        const string areas = "attica = { a1 a2 }\nboeotia = { b1 }\nlaconia = { l1 l2 l3 }\nloose = { x1 }\n";
        BorderSet Load(string areaText, params (string Tag, string Text)[] files)
        {
            var set = new BorderSet();
            set.ApprovedSources.Add("pleiades");
            set.Denied.Add("cliopatria");
            BorderLoader.LoadAreas(set, Burger.Core.Script.ScriptParser.Parse(areaText, "areas.txt"));
            foreach (var (tag, text) in files) BorderLoader.LoadClaims(set, Burger.Core.Script.ScriptParser.Parse(text, tag + ".txt"), tag);
            return set;
        }
        bool Fails(string claim, string fragment)
        {
            var s = Load(areas, ("ATH", claim));
            return s.Issues.Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains(fragment) && i.Where.StartsWith("ATH.txt:"));
        }

        // ---- parser errors carry file and line
        Check(Fails("ATH = { -700 = { add = { nowhere } control = core source = generated } }", "unknown area 'nowhere'"), "an unknown area is an error with file and line");
        Check(Fails("ATH = { -700 = { add = { attica } control = core } }", "needs a source"), "a claim without a source is an error");
        Check(Fails("ATH = { -700 = { add = { attica } control = core source = made_up } }", "not APPROVED"), "a source missing from the ledger is an error");
        Check(Fails("ATH = { -700 = { add = { attica } control = core source = cliopatria } }", "denylist"), "a denylisted source is an error");
        Check(Fails("ATH = { -700 = { add = { attica } control = ruled source = generated } }", "unknown control"), "an unknown control level is an error");
        Check(Fails("ATH = { -700 = { add = { attica } colour = red control = core source = generated } }", "unknown key 'colour'"), "an unknown key is an error");
        Check(Fails("ATH = { 0BCE = { add = { attica } control = core source = generated } }", "BCE"), "a bad date is an error");
        Check(Fails("SPA = { -700 = { add = { attica } control = core source = generated } }", "does not match file name"), "a block named after another polity is an error");
        Check(Fails("ATH = { -700 = { add = { attica } source = generated } }", "needs a control"), "add without control is an error");
        Check(Load(areas, ("ATH", "ATH = { -700 = { add = { attica } control = core source = pleiades note = \"x\" } }")).Issues.Count == 0, "a claim with an approved source and a note parses clean");

        // ---- replay: add/remove across dates, same-day handover independent of file order
        var set = Load(areas,
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = pleiades } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"),
            ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }"));
        var tl = BorderCompiler.Replay(set);
        Check(tl.Issues.Count == 0, "a same-day handover is not an overlap even when the adding file sorts after the removing one" + (tl.Issues.Count > 0 ? ": " + tl.Issues[0] : ""));
        Check(tl.At("boeotia", D("450BCE.1.1"))?.Owner == "ATH" && tl.At("boeotia", D("400BCE.1.1"))?.Owner == "BOE" && tl.At("attica", D("100BCE.1.1"))?.Owner == "ATH" && tl.At("attica", D("600BCE.1.1")) == null,
            "add and remove take effect on their date and not before");
        var rev = Load(areas, ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }"),
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = pleiades } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"));
        Check(BorderCompiler.Replay(rev).Issues.Count == 0 && BorderCompiler.Replay(rev).At("boeotia", D("400BCE.1.1"))?.Owner == "BOE", "reversing file order gives the same result");
        var clash = Load(areas, ("ATH", "ATH = { 500BCE.1.1 = { add = { attica } control = core source = generated } }"), ("SPA", "SPA = { 400BCE.1.1 = { add = { attica } control = core source = generated } }"));
        Check(BorderCompiler.Replay(clash).Issues.Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains("overlap")), "taking an area another polity still holds is reported as an overlap");

        // ---- override order
        var over = new BorderSet();
        BorderLoader.LoadAreas(over, Burger.Core.Script.ScriptParser.Parse("attica = { a1 }", "areas.txt"));
        BorderLoader.LoadAreas(over, Burger.Core.Script.ScriptParser.Parse("attica = { a1 a2 a3 }", "areas_mod.txt"));
        Check(over.Areas["attica"].Length == 3 && over.AreaOrder.Count == 1, "a later areas file overrides an earlier one by name");

        // ---- determinism and load round trip
        string g1 = BorderCompiler.Geography(set, tl), g2 = BorderCompiler.Geography(Load(areas,
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = pleiades } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"),
            ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }")), BorderCompiler.Replay(set));
        Check(g1 == g2 && g1.StartsWith("# GENERATED") && g1.Contains("DO NOT EDIT"), "building twice gives byte-identical output with a GENERATED header");
        var reg = GeoFixture(g1);
        Check(!reg.LoadIssues.Any(i => i.Severity == GeoSeverity.Error) && reg.Owner("boeotia", D("450BCE.1.1")) == "ATH" && reg.Owner("boeotia", D("300BCE.1.1")) == "BOE"
              && reg.Control("boeotia", D("450BCE.1.1")) == "core" && reg.Control("boeotia", D("300BCE.1.1")) == "direct" && reg.Value("boeotia", "claim_source", D("450BCE.1.1")) == "pleiades"
              && reg.Value("boeotia", "claim_source", D("300BCE.1.1")) == "generated", "the generated file loads into the geography layer and reads back owner, control and source");
        Check(reg.DynamicSettings.Enabled == false, "the generated layer turns off runtime splitting so gameplay is unchanged");
        // ---- the Time view reads a location on two dates
        var v1 = BorderView.At(reg, "b1", D("450BCE.1.1")); var v2 = BorderView.At(reg, "b1", D("300BCE.1.1"));
        Check(v1 is { Area: "boeotia", Owner: "ATH", Control: "core", Source: "pleiades" } && v2 is { Owner: "BOE", Control: "direct", Source: "generated" }, "Time view: a location shows its polity, control and source on two different dates");
        Check(BorderView.At(reg, "l1", D("450BCE.1.1")) is { Area: "laconia", Owner: null } && BorderView.At(reg, "nowhere", D("450BCE.1.1")) == null, "Time view: an unclaimed area has no polity and an atom outside every area has no entry");
        Check(!BorderControl.Hatched("core") && !BorderControl.Hatched("direct") && new[] { "tributary", "claimed", "contested", "unknown" }.All(c => BorderControl.Hatched(c)), "Time view hatches every control level except core and direct");
        string dir = Path.Combine(Path.GetTempPath(), "burger_borders_selftest");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "map")); Directory.CreateDirectory(Path.Combine(dir, "history", "borders"));
        File.WriteAllText(Path.Combine(dir, "map", "areas.txt"), areas);
        File.WriteAllText(Path.Combine(dir, "history", "borders", "ATH.txt"), "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = generated } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }");
        File.WriteAllText(Path.Combine(dir, "history", "borders", "BOE.txt"), "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }");
        var disk = BorderLoader.LoadFolder(dir);
        Check(disk.Issues.Count == 0 && BorderCompiler.Geography(disk, BorderCompiler.Replay(disk)) == BorderCompiler.Geography(Load(areas,
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = generated } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"),
            ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }")), BorderCompiler.Replay(Load(areas,
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = generated } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"),
            ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = direct source = generated } }")))), "claim files read from disk compile the same as the same text in memory");
        Directory.Delete(dir, true);

        // ---- one edit changes one province on one date
        var edited = Load(areas,
            ("ATH", "ATH = { 500BCE.1.1 = { add = { attica boeotia } control = core source = pleiades } 400BCE.1.1 = { remove = { boeotia } control = unknown source = generated } }"),
            ("BOE", "BOE = { 400BCE.1.1 = { add = { boeotia } control = tributary source = generated } }"));
        var etl = BorderCompiler.Replay(edited);
        var changed = new List<string>();
        foreach (var name in set.AreaOrder)
            foreach (var day in new[] { D("450BCE.1.1"), D("350BCE.1.1") })
                if (!Equals(tl.At(name, day), etl.At(name, day)) && (tl.At(name, day)?.Control != etl.At(name, day)?.Control || tl.At(name, day)?.Owner != etl.At(name, day)?.Owner)) changed.Add($"{name}@{day}");
        Check(changed.Count == 1 && changed[0] == $"boeotia@{D("350BCE.1.1")}", "changing one claim line changes only that province on that date");

        // ---- coverage on a small fixture: weights a1=1 a2=1 b1=2 l*=1 x1=4 (x1 is in an area nobody claims)
        double W(string a) => a switch { "b1" => 2, "x1" => 4, _ => 1 };
        var cov = BordersValidator.Coverage(set, tl, D("450BCE.1.1"), W);
        Check(Math.Abs(cov.AttestedPct - 100 * 4.0 / 11) < 1e-9 && cov.GeneratedPct == 0 && Math.Abs(cov.UnknownPct - 100 * 7.0 / 11) < 1e-9, "coverage: attested, generated and unknown percentages use the atom weights");
        var cov2 = BordersValidator.Coverage(set, tl, D("350BCE.1.1"), W, new[] { "a1", "a2", "b1", "l1", "l2", "l3", "x1", "z9" });
        Check(Math.Abs(cov2.AttestedPct - 100 * 2.0 / 12) < 1e-9 && Math.Abs(cov2.GeneratedPct - 100 * 2.0 / 12) < 1e-9 && Math.Abs(cov2.UnknownPct - 100 * 8.0 / 12) < 1e-9,
            "coverage counts land that no area covers as unknown");
        var vi = BordersValidator.Run(Load("a = { q1 }\nb = { q1 q2 }"), new BorderTimeline());
        Check(vi.Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains("q1") && i.Message.Contains("must not overlap")), "the validator rejects overlapping areas");
    }
}
