using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Burger.Core;
using Burger.Core.Data;
using Burger.Core.Geography;
using Burger.Core.Model;
using Burger.Core.Save;
using Burger.Core.Script;

namespace Burger.Tools;

public static partial class Program
{
    private static GeoGenOptions GenOptions(List<string> args)
    {
        var o = new GeoGenOptions();
        if (Opt(args, "--provinces") is { } n) o.Provinces = int.Parse(n);
        if (Opt(args, "--min-area") is { } m) o.MinAreaKm2 = double.Parse(m, System.Globalization.CultureInfo.InvariantCulture);
        if (Flag(args, "--synthetic")) o.Synthetic = true;
        return o;
    }

    /// <summary>burger geo generate &lt;project&gt; [--provinces N] [--synthetic] [--force]: writes history/geography from the project's map and states.</summary>
    private static int GeoGenerate(List<string> args)
    {
        bool force = Flag(args, "--force");
        var opt = GenOptions(args);
        string project = ResolveProject(RequireProjectArg(args, "burger geo generate <project> [--provinces N] [--synthetic] [--force]"));
        string dir = Path.Combine(GameDir(project), GeoLoader.Folder);
        if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.txt").Length > 0 && !force) return Fail($"{dir} already has geography files; use --force to replace them");
        Log.EchoToConsole = false;
        var game = Game.Load(GameDir(project), compileScripts: true, seed: 1);
        var sw = Stopwatch.StartNew();
        var r = GeoGenerator.Generate(game, dir, opt);
        Console.WriteLine($"Wrote {string.Join(", ", r.Files)} to {dir} in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  {r.Provinces} provinces from {r.Components} state pieces, {r.Features} features, {r.SyntheticOps} synthetic changes");
        foreach (var (g, v) in r.Groups.OrderBy(x => x.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {g,-14} {v.N,5} provinces ({v.N / (double)r.Provinces:P1}), mean {v.Area / v.N:0} km2");
        return 0;
    }

    private static string WorldFingerprint(Game game)
    {
        var sb = new StringBuilder();
        var w = game.World;
        foreach (var l in w.Locations)
        {
            sb.Append(l.Key).Append('|').Append(l.State?.Key).Append('|').Append(l.Terrain.Key).Append('|').Append(l.PixelCount);
            foreach (var a in l.Neighbors) sb.Append('>').Append(a.To.Key).Append(a.Kind).Append(a.BorderPixels);
            sb.Append('\n');
        }
        sb.Append("#states\n");
        foreach (var s in w.States)
        {
            sb.Append(s.Key).Append('|').Append(s.Owner?.Tag).Append('|').Append(s.Manpower).Append('|').Append(s.Tax);
            foreach (var c in s.Cores.OrderBy(c => c.Tag, StringComparer.Ordinal)) sb.Append(" core:").Append(c.Tag);
            foreach (var c in s.Claims.OrderBy(c => c.Tag, StringComparer.Ordinal)) sb.Append(" claim:").Append(c.Tag);
            sb.Append('\n');
        }
        if (Environment.GetEnvironmentVariable("GEO_FP_DUMP") is { } dump) File.WriteAllText(dump + "." + Environment.TickCount64, sb.ToString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static string FolderHash(string dir) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))))))[..16];

    private static string GeoDump(GeoRegistry r, int day) => string.Join(";", r.Snapshot(day).Provinces.OrderBy(p => p.Id, StringComparer.Ordinal)
        .Select(p => $"{p.Id}:{string.Join(",", p.Atoms)}:{r.Owner(p.Id, day)}:{r.Controller(p.Id, day)}:{r.Name(p.Id, day)}:{r.Number(p.Id, "population", day)}"));

    /// <summary>
    /// burger geo pipeline &lt;project&gt;: generates the geography for the (copied) project and proves it end to end: validity, coverage, lineage, dates,
    /// conservation, determinism, save/load, compatibility with the same project without the layer, and cost. Writes into the project it is given.
    /// </summary>
    private static int GeoPipeline(List<string> args)
    {
        bool force = Flag(args, "--force");
        var opt = GenOptions(args);
        opt.Synthetic = true;
        string project = ResolveProject(RequireProjectArg(args, "burger geo pipeline <project> [--provinces N] [--force]  (use a COPY of the project)"));
        string gameDir = GameDir(project), dir = Path.Combine(gameDir, GeoLoader.Folder);
        if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.txt").Length > 0 && !force) return Fail($"{dir} already has geography files; use --force");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Log.EchoToConsole = false;
        _failedChecks = 0;
        int D(string s) => HistoricalCalendar.TryParse(s, out var d, out _) ? d : int.MinValue + 1;
        Console.WriteLine($"Geography pipeline on {project}");

        // ---- 1. baseline without the layer
        GC.Collect(); long mem0 = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var baseGame = Game.Load(gameDir, compileScripts: true, seed: 1);
        long baseLoadMs = sw.ElapsedMilliseconds;
        string baseFp = WorldFingerprint(baseGame);
        GC.Collect(); long baseMem = GC.GetTotalMemory(true) - mem0;
        Check(!baseGame.Geo.Enabled, "the project starts without a historical province layer (inert)");
        int landCount = baseGame.World.Locations.Count(l => l.IsLand);
        long totalPop = baseGame.World.States.Sum(s => { try { return (long)Math.Round(baseGame.Population.Of(s).Total); } catch { return 0L; } });
        Console.WriteLine($"  baseline: load {baseLoadMs} ms, {landCount} land locations, {baseGame.World.States.Count} states, {totalPop:N0} people, world fingerprint {baseFp}");

        // ---- 2. generate, twice, to prove determinism
        sw.Restart();
        var r1 = GeoGenerator.Generate(baseGame, dir, opt);
        long genMs = sw.ElapsedMilliseconds;
        string h1 = FolderHash(dir);
        string tmpDir = Path.Combine(Path.GetTempPath(), "burger_geo_det_" + Environment.ProcessId);
        GeoGenerator.Generate(baseGame, tmpDir, opt);
        Check(h1 == FolderHash(tmpDir), $"generation is deterministic (two runs give identical files, hash {h1})");
        Directory.Delete(tmpDir, true);
        Console.WriteLine($"  generated {r1.Provinces} provinces, {r1.Features} features, {r1.SyntheticOps} synthetic changes in {genMs} ms");
        foreach (var (g, v) in r1.Groups.OrderBy(x => x.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {g,-14} {v.N,5} provinces ({v.N / (double)r1.Provinces:P1}), {v.Area:N0} km2, mean {v.Area / v.N:0} km2, {v.N / v.Area * 1e6:0.0} per million km2");
        var dens = r1.Groups.ToDictionary(x => x.Key, x => x.Value.N / x.Value.Area);
        Check(dens.ContainsKey("europe") && dens["europe"] == dens.Values.Max(), "Europe has the highest province density per land area");
        Check(r1.Groups.Where(x => x.Key != "asia").All(x => r1.Groups["asia"].N > x.Value.N), "Asia has the largest total province allocation");
        Check(r1.Groups.Where(x => x.Key is "africa" or "north_america" or "south_america" or "oceania").All(x => x.Value.N >= 100), "every other continent gets substantial detail (100+ provinces)");

        // ---- 3. load with the layer
        sw.Restart();
        var game = Game.Load(gameDir, compileScripts: true, seed: 1);
        long geoLoadMs = sw.ElapsedMilliseconds;
        GC.Collect(); long geoMem = GC.GetTotalMemory(true) - mem0;
        var reg = game.Geo;
        Check(reg.Enabled, "the project with history/geography enables the layer");
        Check(WorldFingerprint(game) == baseFp, "the world (locations, adjacency, terrain, states, owners, cores, claims) is identical with and without the layer");
        sw.Restart();
        var issues = GeoValidator.Run(reg, game.World, D(opt.Start));
        long valMs = sw.ElapsedMilliseconds;
        foreach (var i in issues.Where(i => i.Severity == GeoSeverity.Error).Take(5)) Console.WriteLine("    " + i);
        Check(!issues.Any(i => i.Severity == GeoSeverity.Error), $"the validator finds no errors ({issues.Count(i => i.Severity == GeoSeverity.Warning)} warnings, {valMs} ms)");
        foreach (var w in issues.Where(i => i.Severity == GeoSeverity.Warning).Take(3)) Console.WriteLine("    " + w);

        // ---- 4. coverage and conservation at many dates
        string[] dates = { "500BCE.1.1", "300BCE.1.1", "150BCE.1.1", "1BCE.12.31", "1CE.1.1", "300CE.1.1", "700CE.1.1", "1000.1.1", "1300.1.1", "1600.1.1", "2020.1.1", "3500.1.1", "99999.1.1" };
        var landKeys = game.World.Locations.Where(l => l.IsLand).Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
        bool coverOk = true, popOk = true, ownerOk = true; string detail = "";
        var counts = new List<string>();
        foreach (var ds in dates)
        {
            int day = D(ds);
            var snap = reg.Snapshot(day);
            var atoms = snap.Provinces.SelectMany(p => p.Atoms).ToList();
            if (atoms.Count != landKeys.Count || !landKeys.SetEquals(atoms)) { coverOk = false; detail += $" coverage@{ds}={atoms.Count}/{landKeys.Count}"; }
            long pop = snap.Provinces.Sum(p => (long)reg.Number(p.Id, "population", day));
            if (pop != totalPop) { popOk = false; detail += $" pop@{ds}={pop}"; }
            if (snap.Provinces.Any(p => string.IsNullOrEmpty(reg.Name(p.Id, day)))) { ownerOk = false; detail += $" unnamed@{ds}"; }
            counts.Add($"{ds}={snap.Provinces.Count}");
        }
        Console.WriteLine("  active provinces: " + string.Join(", ", counts));
        Check(coverOk, "every land location is in exactly one province on every tested date (no gaps, no overlaps)" + detail);
        Check(popOk, $"total population is the same on every date ({totalPop:N0}): splits and merges create and lose no people");
        Check(ownerOk, "every active province has a name on every date");
        Check(dates.Select(D).Zip(dates.Select(D).Skip(1), (a, b) => a < b).All(x => x), "the test dates (500 BCE through the far future) are strictly ordered as plain integers");

        // ---- 5. operations: lineage, retirement, id reuse
        var ops = reg.Operations;
        Check(ops.Count(o => o.Kind == "split") > 0 && ops.Count(o => o.Kind == "merge") > 0 && ops.Count(o => o.Kind == "redraw") > 0 && ops.Any(o => o.Date < D("1CE.1.1")) && ops.Any(o => o.Date > D("1CE.1.1")),
            $"the history holds splits ({ops.Count(o => o.Kind == "split")}), merges ({ops.Count(o => o.Kind == "merge")}) and redraws ({ops.Count(o => o.Kind == "redraw")}) on both sides of the BCE/CE boundary");
        bool lineage = true; string lbad = "";
        foreach (var op in ops)
            foreach (var par in op.Parents)
            {
                var p = reg.Get(par)!;
                var after = reg.ResolveAt(par, op.Date);
                var union = after.SelectMany(a => a.Atoms).ToHashSet(StringComparer.Ordinal);
                bool nowActive = reg.Snapshot(op.Date).Provinces.Any(x => x.Id == par);
                var before = reg.ResolveAt(par, op.Date - 1);
                if (p.To != op.Date || nowActive || !p.Atoms.All(union.Contains) || before.Count != 1 || before[0].Id != par || reg.Descendants(par).Count() == 0) { lineage = false; lbad = par; }
            }
        Check(lineage, "every retired province resolves to successors that cover exactly its territory, before and after its end date" + (lbad.Length > 0 ? " (" + lbad + ")" : ""));
        var ids = reg.AllProvinces.Select(p => p.Id).ToList();
        Check(ids.Count == ids.Distinct(StringComparer.Ordinal).Count() && reg.AllProvinces.All(p => p.To == HistoricalCalendar.Forever || p.Children.Count > 0), "no province id is reused and no retired province lost its territory");
        var sample = ops.First(o => o.Kind == "split");
        int dAfter = sample.Date + 1;
        long pBefore = (long)reg.Number(sample.Parents[0], "population", sample.Date - 1);
        long pAfter = sample.Children.Sum(c => (long)reg.Number(c.Id, "population", dAfter));
        Check(pBefore == pAfter && reg.Region(sample.Children[0].Id, dAfter) == reg.Region(sample.Parents[0], sample.Date - 1) && reg.Owner(sample.Children[0].Id, dAfter) == reg.Owner(sample.Parents[0], sample.Date - 1),
            $"a split keeps population ({pBefore}), state and owner: {sample.Parents[0]} -> {string.Join(", ", sample.Children.Select(c => c.Id))}");
        var mrg = ops.First(o => o.Kind == "merge");
        Check((long)reg.Number(mrg.Children[0].Id, "population", mrg.Date) == mrg.Parents.Sum(p => (long)reg.Number(p, "population", mrg.Date - 1)), "a merge adds the populations of its parents");

        // ---- 6. temporal properties: ownership/control change without changing identity
        var transfers = reg.ExplicitValues.Where(v => v.Prop == "owner" && v.Day > D(opt.Start)).ToList();
        bool timeOk = transfers.Count > 0;
        foreach (var (subject, _, day, value, _) in transfers.Take(200))
        {
            string? was = reg.Owner(subject, day - 1), now = reg.Owner(subject, day), occupier = reg.Controller(subject, D("10BCE.1.1")), start = reg.Owner(subject, D(opt.Start));
            if (was != start || now != value || was == now || occupier != value || reg.Get(subject)!.Atoms.Length == 0) timeOk = false;
        }
        Check(timeOk, $"{transfers.Count} owner changes across 1 BCE/1 CE: the owner differs before and after, control changes first (occupation), the province id and territory stay the same");
        Check(D("1CE.1.1") - D("1BCE.12.31") == 1 && D("500BCE.1.1") < D("1BCE.1.1") && D("1CE.1.1") < D("2020.1.1") && D("2020.1.1") < D("99999.1.1"), "BCE/CE comparisons have no gap, no year zero and no terminal year");
        var unit = reg.Snapshot(D("2020.1.1")).Provinces.Count > 0;
        Check(unit && reg.Snapshot(D("99999.1.1")).Provinces.Count == reg.Snapshot(D("2020.1.1")).Provinces.Count, "dates far past 2020 still resolve (open-ended future)");
        var cs = reg.Features.Where(f => f.Kind == GeoFeatureKind.Crossing).ToList();
        Check(cs.All(f => f.Atoms.Length == 2) && (cs.Count == 0 || game.World.GetLocation(cs[0].Atoms[0]) != null), $"{reg.Features.Count} physical features read from the map load against real locations");

        // ---- 7. determinism of load and replay
        string dump1 = GeoDump(reg, D("2020.1.1")) + GeoDump(reg, D("300BCE.1.1"));
        var game2 = Game.Load(gameDir, compileScripts: false, seed: 7);
        Check(dump1 == GeoDump(game2.Geo, D("2020.1.1")) + GeoDump(game2.Geo, D("300BCE.1.1")), "loading the project again gives identical province state on both dates");
        var rebuild = reg.Rebuild();
        Check(rebuild.Count == 0 && dump1 == GeoDump(reg, D("2020.1.1")) + GeoDump(reg, D("300BCE.1.1")), "replaying the whole history reproduces the same state");

        // ---- 8. invalid data is caught on real data
        string good = File.ReadAllText(Path.Combine(dir, "00_provinces.txt"));
        var fx = new GeoRegistry();
        GeoLoader.LoadNode(fx, ScriptParser.Parse(good, "gen.txt"));
        var pick = fx.AllProvinces.First(p => p.Atoms.Length >= 2);
        string line = $"province = {{ id = {pick.Id} atoms = {{ {string.Join(" ", pick.Atoms)} }} from = {opt.Start} }}";
        Check(good.Contains(line), "(setup) the generated file has the expected province line");
        Check(GeoValidator.Run(GeoFixture(good.Replace(line, line + "\n" + line), null), game.World).Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains("already used")), "a duplicate province id in the real data is detected");
        string dropped = pick.Atoms.Length > 2 ? string.Join(" ", pick.Atoms.Skip(1)) : pick.Atoms[1];
        Check(GeoValidator.Run(GeoFixture(good.Replace(line, $"province = {{ id = {pick.Id} atoms = {{ {dropped} }} from = {opt.Start} }}"), null), game.World).Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains("not in any province")),
            "a land location left out of every province is reported (full coverage)");
        string orphan = line.Replace(string.Join(" ", pick.Atoms), "no_such_location");
        Check(GeoValidator.Run(GeoFixture(good.Replace(line, orphan), null), game.World).Any(i => i.Message.Contains("unknown location")), "an unknown location in a province is detected");
        var far = game.World.Locations.Where(l => l.IsLand).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
        Location? farA = null, farB = null;
        foreach (var a in far) { farA = a; farB = far.FirstOrDefault(b => b != a && a.Neighbors.All(n => n.To != b) && Math.Abs(b.CenterX - a.CenterX) > 400); if (farB != null) break; }
        Check(GeoValidator.Run(GeoFixture($"province = {{ id = bad atoms = {{ {farA!.Key} {farB!.Key} }} }}", null), game.World).Any(i => i.Message.Contains("not contiguous")), "a province of two distant, unconnected locations is detected as not contiguous");
        var sea = game.World.Locations.First(l => l.IsWater);
        Check(GeoValidator.Run(GeoFixture($"province = {{ id = bad atoms = {{ {farA.Key} {sea.Key} }} }}", null), game.World).Any(i => i.Message.Contains("mixes land and water")), "a province mixing land and sea is detected");
        Check(GeoValidator.Run(GeoFixture($"province = {{ id = bad atoms = {{ {farA.Key} }} }} split = {{ date = 10.1.1 parent = bad child = {{ id = c1 atoms = {{ {farA.Key} }} }} child = {{ id = c2 atoms = {{ {farB.Key} }} }} }}", null), game.World).Any(i => i.Severity == GeoSeverity.Error),
            "a split that invents territory is detected on real data");

        // ---- 9. save and load
        var target = reg.Snapshot(D("1800.1.1")).Provinces.Where(p => p.Atoms.Length >= 2).OrderBy(p => p.Id, StringComparer.Ordinal).First();
        var locs = target.Atoms.Select(a => game.World.GetLocation(a)!).ToList();
        var cut = GeoGenerator.Partition(locs, 2, GeoMetrics.LocationAreasKm2(game.World));
        var run = new GeoOperation { Date = D("1850.1.1"), Parents = new[] { target.Id }, Reason = "pipeline test" };
        for (int i = 0; i < cut.Count; i++) run.Children.Add(new GeoChildSpec { Id = $"run_{target.Id}_{i}", Atoms = cut[i].Select(l => l.Key).ToArray() });
        string? err = reg.ApplyRuntime(run);
        Check(err == null && reg.Get(target.Id)!.To == run.Date, "a runtime split of a real province is accepted" + (err != null ? ": " + err : ""));
        string save = Path.Combine(Path.GetTempPath(), "burger_geo_pipeline.sav");
        SaveGame.Save(game, save);
        string saveText = File.ReadAllText(save);
        Check(saveText.Contains("x_geography"), "the saved game holds the x_geography section");
        var g3 = Game.Load(gameDir, compileScripts: true, seed: 1);
        SaveGame.Load(g3, save);
        Check(GeoDump(g3.Geo, D("1900.1.1")) == GeoDump(reg, D("1900.1.1")) && GeoDump(g3.Geo, D("2020.1.1")) == GeoDump(reg, D("2020.1.1")), "save then load reproduces the province state, including the runtime split");
        var legacy = ScriptParser.ParseFile(save, "save");
        legacy.Children!.RemoveAll(n => n.Key.Equals("x_geography", StringComparison.OrdinalIgnoreCase));
        string legacyPath = save + ".legacy";
        ScriptWriter.WriteFile(legacyPath, legacy, "legacy-format save");
        var g4 = Game.Load(gameDir, compileScripts: true, seed: 1);
        SaveGame.Load(g4, legacyPath);
        Check(g4.Geo.Get(run.Children[0].Id) == null && g4.Geo.Enabled, "a save without the geography section loads with only the data-file history");
        // a save made WITHOUT the layer loads in the project WITH it (and the reverse: layer saves load in a project without geography)
        var plain = Game.Load(gameDir, compileScripts: true, seed: 1);
        File.Delete(save); File.Delete(legacyPath);
        string noGeoSave = Path.Combine(Path.GetTempPath(), "burger_geo_plain.sav");
        SaveGame.Save(baseGame, noGeoSave);
        var g5 = Game.Load(gameDir, compileScripts: true, seed: 1);
        SaveGame.Load(g5, noGeoSave);
        string fp5 = WorldFingerprint(g5);
        Check(g5.Geo.Enabled && fp5 == baseFp, $"a save made before the layer existed loads with it, world unchanged (layer {g5.Geo.Enabled}, fingerprint {fp5} vs {baseFp})");
        File.Delete(noGeoSave);

        // ---- 10. cost
        sw.Restart();
        long hits = 0;
        var keys = landKeys.ToArray();
        for (int i = 0; i < 1_000_000; i++) if (game2.Geo.ProvinceOfAtom(keys[i % keys.Length], D("2020.1.1")) != null) hits++;
        long lookupMs = sw.ElapsedMilliseconds;
        sw.Restart();
        game2.Geo.Rebuild();
        long replayMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  cost: load {baseLoadMs} ms without, {geoLoadMs} ms with ({geoLoadMs - baseLoadMs:+0;-0} ms); managed memory, rough: +{(geoMem - 2 * baseMem) / 1048576.0:0.0} MB; validate {valMs} ms; full replay {replayMs} ms; 1M lookups {lookupMs} ms");
        Check(hits == 1_000_000 && lookupMs < 3000 && replayMs < 5000 && geoLoadMs - baseLoadMs < 5000, "lookups, replay and load stay fast on the real map");
        Console.WriteLine(_failedChecks == 0 ? "Geography pipeline: all checks passed" : $"Geography pipeline: {_failedChecks} check(s) FAILED");
        return _failedChecks == 0 ? 0 : 1;
    }
}
