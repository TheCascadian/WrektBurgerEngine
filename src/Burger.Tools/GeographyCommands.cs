using Burger.Core;
using Burger.Core.Geography;
using Burger.Core.Map;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Save;
using Burger.Core.Script;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>burger geo: load a project, validate its historical provinces and report on a date.</summary>
    private static int Geo(List<string> args)
    {
        if (args.Count > 0 && args[0].Equals("generate", StringComparison.OrdinalIgnoreCase)) return GeoGenerate(args.Skip(1).ToList());
        if (args.Count > 0 && args[0].Equals("pipeline", StringComparison.OrdinalIgnoreCase)) return GeoPipeline(args.Skip(1).ToList());
        if (args.Count > 0 && args[0].Equals("simulate", StringComparison.OrdinalIgnoreCase)) return GeoSimulate(args.Skip(1).ToList());
        if (args.Count > 0 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            _failedChecks = 0;
            GeographySelfTest(null, null);
            return _failedChecks == 0 ? 0 : 1;
        }
        string dateText = Opt(args, "--date") ?? "";
        string? provinceId = Opt(args, "--province");
        bool density = Flag(args, "--density");
        string project = ResolveProject(RequireProjectArg(args, "burger geo <project> [--date Y.M.D] [--density] [--province id]"));
        Log.EchoToConsole = false;
        var game = Game.Load(GameDir(project), compileScripts: false, seed: 1);
        PrintLog();
        var reg = game.Geo;
        if (!reg.Enabled) { Console.WriteLine("No history/geography in this project: the historical province layer is not used."); return 0; }
        int day = game.World.Date.TotalDays;
        if (dateText.Length > 0 && !HistoricalCalendar.TryParse(dateText, out day, out var err)) return Fail(err ?? "bad date");
        var issues = GeoValidator.Run(reg, game.World, day);
        foreach (var i in issues) Console.WriteLine("  " + i);
        var snap = reg.Snapshot(day);
        Console.WriteLine($"{HistoricalCalendar.Format(day)}: {snap.Provinces.Count} active provinces of {reg.AllProvinces.Count()} ever defined, " +
                          $"{reg.Operations.Count} reorganizations, {reg.FeaturesAt(day).Count()} features");
        if (provinceId != null)
        {
            var p = reg.Get(provinceId);
            if (p == null) return Fail($"unknown province '{provinceId}'");
            Console.WriteLine($"  {p.Id}: {p.Atoms.Length} atoms, {HistoricalCalendar.Format(p.From)} to {HistoricalCalendar.Format(p.To)}");
            Console.WriteLine($"  ancestors: {string.Join(", ", reg.Ancestors(p.Id).Select(a => a.Id))}");
            Console.WriteLine($"  descendants: {string.Join(", ", reg.Descendants(p.Id).Select(a => a.Id))}");
            Console.WriteLine($"  on this date it means: {string.Join(", ", reg.ResolveAt(p.Id, day).Select(a => a.Id))}");
            foreach (var prop in reg.Properties.Select(x => x.Key).Concat(new[] { "name", "owner", "controller", "state" }).Distinct())
                if (reg.Value(p.Id, prop, day) is { } v) Console.WriteLine($"  {prop} = {v}");
        }
        if (density)
            foreach (var r in GeoValidator.DensityReport(reg, game.World, day))
                Console.WriteLine($"  {r.Group,-16} {r.Provinces,5} provinces ({r.Share:P1}, target {r.TargetShare:P1}), mean {r.MeanAreaKm2:0} km2");
        return issues.Any(i => i.Severity == GeoSeverity.Error) ? 1 : 0;
    }

    // ===============================================================================================
    private static GeoRegistry GeoFixture(string text, Func<string, double>? weight = null)
    {
        var reg = new GeoRegistry();
        if (weight != null) reg.AtomWeight = weight;
        GeoLoader.LoadNode(reg, ScriptParser.Parse(text, "selftest_geo.txt"));
        foreach (var e in reg.Rebuild()) reg.Issue(GeoSeverity.Error, e);
        return reg;
    }

    private static void GeographySelfTest(Game? game, string? gameDir)
    {
        Console.WriteLine("Historical provinces:");
        int D(string s) => HistoricalCalendar.TryParse(s, out var d, out _) ? d : int.MinValue + 1;

        // ---- dates
        Check(D("500BCE.1.1") == HistoricalCalendar.FromAstronomical(-499) && D("1BCE.1.1") == HistoricalCalendar.FromAstronomical(0) && D("1CE.1.1") == HistoricalCalendar.FromAstronomical(1),
            "BCE/CE text maps to astronomical years (500 BCE = -499, 1 BCE = 0, 1 CE = 1)");
        Check(D("1CE.1.1") - D("1BCE.12.31") == 1, "1 BCE 31 December is the day before 1 CE 1 January (no gap, no year zero)");
        Check(D("500BCE.1.1") < D("1BCE.1.1") && D("1BCE.1.1") < D("1CE.1.1") && D("2020.1.1") < D("9999.1.1") && D("9999.1.1") < HistoricalCalendar.Forever,
            "BCE < CE < modern < future < forever as plain integers");
        Check(!HistoricalCalendar.TryParse("0BCE", out _, out _) && !HistoricalCalendar.TryParse("0CE.1.1", out _, out _) && !HistoricalCalendar.TryParse("500BCE.13.1", out _, out _),
            "year 0 with BCE/CE and bad months are rejected");
        Check(HistoricalCalendar.TryParse(HistoricalCalendar.ToScript(D("500BCE.3.1")), out int rt, out _) && rt == D("500BCE.3.1") && HistoricalCalendar.Format(D("500BCE.3.1")) == "1 March 500 BCE",
            "dates round-trip through script text and display");

        // ---- timeline
        var tl = new Timeline<string>();
        Check(tl.TrySet(10, "a") && tl.TrySet(5, "b") && tl.TrySet(10, "a") && !tl.TrySet(10, "c"), "a timeline accepts the same value twice and refuses a conflicting one");
        Check(!tl.TryAt(4, out _) && tl.TryAt(5, out var v5) && v5 == "b" && tl.TryAt(9, out var v9) && v9 == "b" && tl.TryAt(10, out var v10) && v10 == "a" && tl.TryAt(int.MaxValue, out var vm) && vm == "a",
            "timeline lookups hold a value until the next change");

        // ---- split / merge / redraw
        const string data = @"
property = { key = population kind = extensive integer = yes }
property = { key = fertility kind = intensive }
property = { key = claims kind = set }
province = { id = delta atoms = { d1 d2 d3 d4 } from = 500BCE.1.1 }
province = { id = hills atoms = { h1 h2 } from = 500BCE.1.1 }
track = { subject = delta property = owner 500BCE.1.1 = persia  330BCE.1.1 = macedon  30BCE.1.1 = rome }
track = { subject = delta property = name 500BCE.1.1 = ""Delta"" }
track = { subject = alexandria property = name 100CE.1.1 = ""Nile Delta"" }
track = { subject = delta property = state 500BCE.1.1 = lower_egypt }
track = { subject = delta property = population 500BCE.1.1 = 1000 100BCE.1.1 = 1001 }
track = { subject = delta property = fertility 500BCE.1.1 = 0.8 }
track = { subject = delta property = claims 500BCE.1.1 = { persia assyria } }
track = { subject = hills property = owner 500BCE.1.1 = persia }
split = { date = 100CE.1.1 parent = delta reason = ""Alexandria grows into a separate center""
  child = { id = alexandria atoms = { d1 } }
  child = { id = delta_east atoms = { d2 d3 } }
  child = { id = delta_west atoms = { d4 } } }
merge = { date = 300CE.1.1 parents = { delta_east delta_west } into = { id = delta_rump } }
reorganize = { date = 600CE.1.1 parents = { delta_rump hills } child = { id = north atoms = { d2 d3 h1 } } child = { id = south atoms = { d4 h2 } } }
";
        Func<string, double> weights = a => a == "d1" ? 3 : 1; // d1 is the big one
        var reg = GeoFixture(data, weights);
        Check(reg.LoadIssues.Count == 0, "a valid history file loads without issues" + (reg.LoadIssues.Count > 0 ? ": " + reg.LoadIssues[0] : ""));
        int T(string s) => D(s);
        Check(reg.Snapshot(T("200BCE.1.1")).Provinces.Select(p => p.Id).SequenceEqual(new[] { "delta", "hills" }), "snapshot before the split has the original provinces");
        Check(reg.Snapshot(T("150CE.1.1")).Provinces.Select(p => p.Id).SequenceEqual(new[] { "alexandria", "delta_east", "delta_west", "hills" }), "snapshot after the split has the children, the parent is gone");
        Check(reg.Snapshot(T("350CE.1.1")).Provinces.Select(p => p.Id).SequenceEqual(new[] { "alexandria", "delta_rump", "hills" }), "snapshot after the merge has the merged province");
        Check(reg.Snapshot(T("700CE.1.1")).Provinces.Select(p => p.Id).SequenceEqual(new[] { "alexandria", "north", "south" }), "snapshot after the redraw has the redrawn provinces");
        Check(reg.Owner("delta", T("400BCE.1.1")) == "persia" && reg.Owner("delta", T("300BCE.1.1")) == "macedon" && reg.Owner("delta", T("50BCE.1.1")) == "macedon" && reg.Owner("delta", T("30BCE.1.1")) == "rome",
            "ownership changes by date while the province id stays the same");
        Check(reg.Name("delta", T("50CE.1.1")) == "Delta" && reg.Name("delta", T("100CE.1.1")) == null && reg.Name("alexandria", T("101CE.1.1")) == "Nile Delta" && reg.Name("delta_east", T("101CE.1.1")) == "Delta",
            "a name can be set on a new province, while siblings inherit the name in force at the split");
        Check(reg.Owner("alexandria", T("100CE.1.1")) == "rome" && reg.Owner("delta_east", T("200CE.1.1")) == "rome" && reg.Region("delta_west", T("200CE.1.1")) == "lower_egypt",
            "children inherit ownership and administrative region");
        long pop(string id, string d) => (long)reg.Number(id, "population", T(d));
        Check(pop("delta", "99CE.1.1") == 1001 && pop("alexandria", "100CE.1.1") + pop("delta_east", "100CE.1.1") + pop("delta_west", "100CE.1.1") == 1001,
            "an integer population is split exactly: nothing created or lost (" + pop("alexandria", "100CE.1.1") + "+" + pop("delta_east", "100CE.1.1") + "+" + pop("delta_west", "100CE.1.1") + " = 1001)");
        Check(pop("alexandria", "100CE.1.1") > pop("delta_east", "100CE.1.1") && pop("delta_east", "100CE.1.1") > pop("delta_west", "100CE.1.1"),
            "population follows the area of the atoms (3 : 2 : 1)");
        Check(pop("delta_rump", "300CE.1.1") == pop("delta_east", "299CE.1.1") + pop("delta_west", "299CE.1.1"), "a merge adds the populations");
        Check(Math.Abs(reg.Number("alexandria", "fertility", T("100CE.1.1")) - 0.8) < 1e-9 && reg.Value("alexandria", "claims", T("100CE.1.1")) == "assyria|persia",
            "intensive values are averaged and claim sets carried to every child");
        Check(reg.Ancestors("north").Select(p => p.Id).OrderBy(x => x).SequenceEqual(new[] { "delta", "delta_east", "delta_rump", "delta_west", "hills" }), "lineage reaches back through split, merge and redraw");
        Check(reg.ResolveAt("delta", T("150CE.1.1")).Select(p => p.Id).SequenceEqual(new[] { "alexandria", "delta_east", "delta_west" }) && reg.ResolveAt("delta", T("200BCE.1.1")).Single().Id == "delta"
              && reg.ResolveAt("delta_rump", T("150CE.1.1")).Select(p => p.Id).SequenceEqual(new[] { "delta_east", "delta_west" }),
            "an old id resolves to its successors (or ancestors) on any date, never to unrelated land");
        Check(reg.ProvinceOfAtom("d1", T("500BCE.1.1"))!.Id == "delta" && reg.ProvinceOfAtom("d1", T("101CE.1.1"))!.Id == "alexandria" && reg.ProvinceOfAtom("d1", T("2000.1.1"))!.Id == "alexandria" && reg.ProvinceOfAtom("zz", 0) == null,
            "an atom's province is found for any date; the atom's id never changes");
        var all = reg.AllProvinces.Where(p => p.To == HistoricalCalendar.Forever).SelectMany(p => p.Atoms).OrderBy(x => x).ToList();
        Check(all.SequenceEqual(new[] { "d1", "d2", "d3", "d4", "h1", "h2" }), "territory is covered exactly once after all reorganizations");
        Check(GeoValidator.Run(reg).All(i => i.Severity != GeoSeverity.Error), "the validator accepts the valid history");

        // ---- determinism and runtime operations
        string Dump(GeoRegistry r) => string.Join(";", r.AllProvinces.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => $"{p.Id}:{p.From}:{p.To}:{string.Join(",", p.Atoms)}:{r.Value(p.Id, "population", Math.Min(p.To - 1, 1000000))}"));
        Check(Dump(reg) == Dump(GeoFixture(data, weights)), "loading the same data twice gives identical results");
        string before = Dump(reg);
        var rt1 = reg.Rebuild();
        Check(rt1.Count == 0 && Dump(reg) == before, "replaying the whole history reproduces the same state");
        var late = new GeoOperation { Date = T("900CE.1.1"), Parents = new[] { "south" }, Children = { new GeoChildSpec { Id = "south_a", Atoms = new[] { "d4" } }, new GeoChildSpec { Id = "south_b", Atoms = new[] { "h2" } } }, Reason = "event" };
        long southPop = (long)reg.Number("south", "population", T("899CE.1.1"));
        Check(reg.ApplyRuntime(late) == null && reg.Get("south")!.To == T("900CE.1.1") && (long)(reg.Number("south_a", "population", T("900CE.1.1")) + reg.Number("south_b", "population", T("900CE.1.1"))) == southPop,
            "a runtime split applies incrementally and conserves the population");
        var node = GeoLoader.OperationNode(late);
        var saveRoot = Node.Root("x"); saveRoot.Add(node);
        var back = GeoLoader.ParseOperation(new GeoRegistry(), ScriptParser.Parse(ScriptWriter.Write(saveRoot), "x").Children![0]);
        Check(back != null && back.Date == late.Date && back.Parents.SequenceEqual(late.Parents) && back.Children.Select(c => c.Id + string.Join("", c.Atoms)).SequenceEqual(late.Children.Select(c => c.Id + string.Join("", c.Atoms))),
            "a runtime operation survives the save format round trip");
        var early = new GeoOperation { Date = T("200CE.1.1"), Parents = new[] { "hills" }, Children = { new GeoChildSpec { Id = "h_one", Atoms = new[] { "h1" } }, new GeoChildSpec { Id = "h_two", Atoms = new[] { "h2" } } } };
        Check(reg.ApplyRuntime(early) is { } ep && ep.Length > 0, "an earlier-dated runtime split that breaks later records is refused (hills is merged away at 600 CE)");
        Check(reg.Get("h_one") == null && Dump(reg).Contains("south_a"), "a refused operation leaves the registry as it was");
        reg.ResetRuntime();
        Check(reg.Get("south_a") == null && reg.Get("south")!.To == HistoricalCalendar.Forever, "loading a save resets runtime operations before reapplying");

        // ---- invalid data is caught
        GeoRegistry Bad(string s) => GeoFixture(s);
        bool Rejects(string s, string needle) { var r = Bad(s); return GeoValidator.Run(r).Any(i => i.Severity == GeoSeverity.Error && i.Message.Contains(needle)); }
        string baseP = "province = { id = a atoms = { x y } } ";
        Check(Rejects(baseP + "province = { id = a atoms = { z } }", "already used"), "a duplicate province id is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x } } }", "uncovered"), "a split that leaves a gap is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x y } } child = { id = c atoms = { y } } }", "more than one child"), "a split with overlapping children is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x } } child = { id = c atoms = { y q } } }", "from nowhere"), "a split that invents territory is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = a atoms = { x } } child = { id = c atoms = { y } } }", "never reused") || Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = a atoms = { x } } child = { id = c atoms = { y } } }", "already used"), "recycling a retired id is refused");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = gone child = { id = b atoms = { x y } } }", "unknown parent"), "an operation on a missing province is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x } share = 0.7 } child = { id = c atoms = { y } share = 0.7 } }", "sum to 1"), "shares that do not add to one are detected");
        Check(Rejects(baseP + "track = { subject = a property = owner 5.1.1 = r1 5.1.1 = r2 }", "conflicting"), "two values on one day conflict");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x } } child = { id = c atoms = { y } } } track = { subject = a property = owner 20.1.1 = late }", "after the province was retired"), "records dated after a retirement are detected");
        Check(Rejects(baseP + "province = { id = b atoms = { y } }", "overlap"), "overlapping provinces are detected");
        Check(Rejects("province = { id = a from = bogus atoms = { x } }", "not a number") || Rejects("province = { id = a from = 0BCE atoms = { x } }", "year 0"), "an invalid date is detected");
        Check(Rejects(baseP + "split = { date = 10.1.1 parent = a child = { id = b atoms = { x } } child = { id = c atoms = { y } } } split = { date = 20.1.1 parent = a child = { id = d atoms = { x y } } }", "not active"), "changing a retired province is detected");

        // ---- explicit shares and area weights
        var sh = GeoFixture("property = { key = population kind = extensive integer = yes } province = { id = p atoms = { u v } } track = { subject = p property = population 1.1.1 = 101 } " +
                            "split = { date = 5.1.1 parent = p child = { id = q atoms = { u } share = 0.75 } child = { id = r atoms = { v } share = 0.25 } }");
        Check((long)sh.Number("q", "population", D("5.1.1")) == 76 && (long)sh.Number("r", "population", D("5.1.1")) == 25, "explicit shares override area with exact integer totals (101 -> 76 + 25)");

        // ---- geometry on a small world
        var world = new Burger.Core.Data.World();
        var land = new TerrainType { Key = "plains", Index = 0 };
        Location L(string k) { var l = new Location { Index = world.Locations.Count, Key = k, Terrain = land }; world.Locations.Add(l); world.LocationByKey[k] = l; return l; }
        void Link(Location a, Location b, int px, AdjacencyKind kind = AdjacencyKind.Land) { a.Neighbors.Add(new Adjacency { From = a, To = b, Kind = kind, BorderPixels = px }); b.Neighbors.Add(new Adjacency { From = b, To = a, Kind = kind, BorderPixels = px }); }
        var (g1, g2, g3, g4, g5) = (L("g1"), L("g2"), L("g3"), L("g4"), L("g5"));
        Link(g1, g2, 10); Link(g2, g3, 1); Link(g4, g5, 8, AdjacencyKind.Strait);
        var geoOk = GeoFixture("province = { id = ok atoms = { g1 g2 } }");
        Check(!GeoValidator.Run(geoOk, world).Any(i => i.Severity == GeoSeverity.Error), "a province joined by a real border is contiguous");
        Check(GeoValidator.Run(GeoFixture("province = { id = corner atoms = { g2 g3 } }"), world).Any(i => i.Message.Contains("not contiguous")), "two polygons touching at a corner do not make one contiguous province");
        Check(GeoValidator.Run(GeoFixture("province = { id = isl atoms = { g4 g5 } }"), world).Any(i => i.Message.Contains("not contiguous"))
              && !GeoValidator.Run(GeoFixture("province = { id = isl atoms = { g4 g5 } archipelago = yes }"), world).Any(i => i.Severity == GeoSeverity.Error), "an island group needs archipelago = yes to be joined by a strait");
        Check(GeoValidator.Run(GeoFixture("province = { id = ghost atoms = { nowhere } }"), world).Any(i => i.Message.Contains("unknown location")), "a province naming a missing location is detected");
        g5.ForcedWater = true;
        Check(GeoValidator.Run(GeoFixture("province = { id = mix atoms = { g4 g5 } archipelago = yes }"), world).Any(i => i.Message.Contains("mixes land and water")), "a province mixing land and sea is detected");
        g2.Neighbors.RemoveAll(a => a.To == g3);
        Check(GeoValidator.Run(GeoFixture("province = { id = asym atoms = { g2 g3 } }"), world).Any(i => i.Message.Contains("no reverse link")), "broken adjacency (missing reverse link) is detected");

        // ---- real area
        double sum = 0;
        for (int y = 0; y < 1024; y++) sum += GeoMetrics.PixelAreaKm2(y, 2048, 1024) * 2048;
        Check(Math.Abs(sum - 4 * Math.PI * GeoMetrics.EarthRadiusKm * GeoMetrics.EarthRadiusKm) / sum < 1e-9, "pixel areas sum to the sphere's surface");
        Check(GeoMetrics.PixelAreaKm2(512, 2048, 1024) > 8 * GeoMetrics.PixelAreaKm2(5, 2048, 1024), "an equatorial pixel is far larger than a polar one (no flat pixel counting)");

        // ---- features and compatibility
        var ft = GeoFixture("province = { id = a atoms = { x } } feature = { id = gate kind = pass atoms = { x } from = 100BCE.1.1 to = 500CE.1.1 name = \"Gate\" }");
        Check(ft.FeaturesAt(D("50BCE.1.1")).Count() == 1 && !ft.FeaturesAt(D("600CE.1.1")).Any() && !ft.FeaturesAt(D("200BCE.1.1")).Any(), "a pass is usable only between its dates");
        Check(!new GeoRegistry().Enabled && GeoValidator.Run(new GeoRegistry()).Count == 0, "an empty registry is inert and valid");
        if (game != null)
        {
            Check(!game.Geo.Enabled, "a project without history/geography has an inert historical province layer");
            Check(!game.SaveSections.All.Any(s => s.Name == "geography"), "no geography save section is registered unless the project uses it");
            GeographyProjectTest(gameDir!);
        }

        // ---- scale: a world-sized history builds and answers quickly
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var big = new GeoRegistry();
        big.DefineProperty(new PropertyDef("population", PropertyKind.Extensive, true));
        for (int i = 0; i < 20000; i++)
        {
            big.CreateProvince("p" + i, new[] { "a" + i * 2, "a" + (i * 2 + 1) }, HistoricalCalendar.FromAstronomical(-499));
            big.SetValue("p" + i, "owner", HistoricalCalendar.FromAstronomical(-499), "o" + i % 50);
            big.SetValue("p" + i, "population", HistoricalCalendar.FromAstronomical(-499), (1000 + i).ToString());
        }
        for (int i = 0; i < 4000; i++)
            big.AddOperation(new GeoOperation { Date = HistoricalCalendar.FromAstronomical(100 + i % 900), Parents = new[] { "p" + i }, Children = { new GeoChildSpec { Id = "q" + i + "a", Atoms = new[] { "a" + i * 2 } }, new GeoChildSpec { Id = "q" + i + "b", Atoms = new[] { "a" + (i * 2 + 1) } } } });
        long buildMs = sw.ElapsedMilliseconds; sw.Restart();
        var errs = big.Rebuild();
        long rebuildMs = sw.ElapsedMilliseconds; sw.Restart();
        long hits = 0;
        for (int i = 0; i < 1_000_000; i++) if (big.ProvinceOfAtom("a" + (i % 40000), HistoricalCalendar.FromAstronomical(2000)) != null) hits++;
        long lookupMs = sw.ElapsedMilliseconds; sw.Restart();
        var snap500 = big.Snapshot(HistoricalCalendar.FromAstronomical(2000));
        var again = big.Snapshot(HistoricalCalendar.FromAstronomical(2000));
        long snapMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"    scale: 20000 provinces + 4000 splits: build {buildMs} ms, full replay {rebuildMs} ms, 1M atom lookups {lookupMs} ms, snapshot {snapMs} ms");
        if (errs.Count > 0 || hits != 1_000_000) Console.WriteLine($"    scale debug: {errs.Count} errors, first: {errs.FirstOrDefault()}, hits {hits}, snapshot {snap500.Provinces.Count}");
        Check(errs.Count == 0 && hits == 1_000_000 && ReferenceEquals(snap500, again) && snap500.Provinces.Count == 20000 + 4000,
            "a world-sized history (20000 provinces, 4000 splits) replays without errors and the snapshot is cached");
        Check(rebuildMs < 5000 && lookupMs < 3000, "world-sized replay and lookups stay fast");
    }

    /// <summary>Loads real locations from the project, builds a history on them, validates against the real map and round-trips a save.</summary>
    private static void GeographyProjectTest(string gameDir)
    {
        var game = Game.Load(gameDir, compileScripts: false, seed: 1234); // own instance: the shared self-test game stays untouched
        var world = game.World;
        // find a chain of four connected land locations: a-b-c-d
        List<Location>? chain = null;
        bool Extend(List<Location> path)
        {
            if (path.Count == 4) { chain = path; return true; }
            foreach (var adj in path[^1].Neighbors.Where(a => a.Kind == AdjacencyKind.Land && a.BorderPixels >= GeoValidator.MinBorderPixels && a.To.IsLand && !path.Contains(a.To)))
                if (Extend(new List<Location>(path) { adj.To })) return true;
            return false;
        }
        foreach (var l in world.Locations.Where(l => l.IsLand)) if (Extend(new List<Location> { l })) break;
        if (chain == null) { Check(false, "geography: the project has no chain of four connected land locations to test with"); return; }
        string[] k = chain.Select(l => l.Key).ToArray();
        string text = $@"property = {{ key = population kind = extensive integer = yes }}
province = {{ id = geo_test_whole atoms = {{ {k[0]} {k[1]} {k[2]} {k[3]} }} from = 500BCE.1.1 }}
track = {{ subject = geo_test_whole property = population 500BCE.1.1 = 12345 }}
track = {{ subject = geo_test_whole property = owner 500BCE.1.1 = alpha 1CE.1.1 = beta }}
split = {{ date = 1000.1.1 parent = geo_test_whole child = {{ id = geo_test_west atoms = {{ {k[0]} {k[1]} }} }} child = {{ id = geo_test_east atoms = {{ {k[2]} {k[3]} }} }} }}";
        var reg = game.Geo;
        GeoLoader.LoadNode(reg, ScriptParser.Parse(text, "selftest_project_geo.txt"));
        void Weigh(Game g)
        {
            double[]? areas = null;
            g.Geo.AtomWeight = a => { areas ??= GeoMetrics.LocationAreasKm2(g.World); return g.World.GetLocation(a) is { } l ? Math.Max(areas[l.Index], 1e-6) : 1; };
        }
        Weigh(game);
        foreach (var e in reg.Rebuild()) reg.Issue(GeoSeverity.Error, e);
        GeoLoader.Install(game);
        var issues = GeoValidator.Run(reg, world);
        Check(!issues.Any(i => i.Severity == GeoSeverity.Error), "real locations form valid, contiguous provinces on the project's own map" + (issues.Count > 0 ? ": " + issues[0] : ""));
        int day = HistoricalCalendar.FromAstronomical(1100);
        long total = (long)reg.Number("geo_test_west", "population", day) + (long)reg.Number("geo_test_east", "population", day);
        Check(total == 12345 && reg.Owner("geo_test_east", day) == "beta", "a split on real locations conserves population and keeps the owner");
        // runtime split saved and reloaded
        var op = new GeoOperation { Date = HistoricalCalendar.FromAstronomical(1500), Parents = new[] { "geo_test_east" }, Children = { new GeoChildSpec { Id = "geo_test_e1", Atoms = new[] { k[2] } }, new GeoChildSpec { Id = "geo_test_e2", Atoms = new[] { k[3] } } } };
        Check(reg.ApplyRuntime(op) == null, "a runtime split on real locations is accepted");
        string tmp = Path.Combine(Path.GetTempPath(), "burger_geo_selftest.sav");
        SaveGame.Save(game, tmp);
        var g2 = Game.Load(gameDir, compileScripts: true, seed: 1234);
        Weigh(g2);
        GeoLoader.LoadNode(g2.Geo, ScriptParser.Parse(text, "selftest_project_geo.txt"));
        foreach (var e in g2.Geo.Rebuild()) g2.Geo.Issue(GeoSeverity.Error, e);
        GeoLoader.Install(g2);
        SaveGame.Load(g2, tmp);
        Check(g2.Geo.Get("geo_test_e1") != null && g2.Geo.Get("geo_test_east")!.To == HistoricalCalendar.FromAstronomical(1500)
              && (long)g2.Geo.Number("geo_test_e1", "population", HistoricalCalendar.FromAstronomical(1500)) + (long)g2.Geo.Number("geo_test_e2", "population", HistoricalCalendar.FromAstronomical(1500)) == (long)reg.Number("geo_test_east", "population", HistoricalCalendar.FromAstronomical(1499)),
            "save/load keeps runtime province splits and their data");
        var lr = ScriptParser.ParseFile(tmp, Path.GetFileName(tmp));
        lr.Children!.RemoveAll(n => n.Key.Equals("x_geography", StringComparison.OrdinalIgnoreCase));
        string legacy = Path.Combine(Path.GetTempPath(), "burger_geo_legacy_selftest.sav");
        ScriptWriter.WriteFile(legacy, lr, "Legacy-format geography self-test");
        var g3 = Game.Load(gameDir, compileScripts: true, seed: 1234);
        Weigh(g3);
        GeoLoader.LoadNode(g3.Geo, ScriptParser.Parse(text, "selftest_project_geo.txt"));
        foreach (var e in g3.Geo.Rebuild()) g3.Geo.Issue(GeoSeverity.Error, e);
        GeoLoader.Install(g3);
        SaveGame.Load(g3, legacy);
        Check(g3.Geo.Get("geo_test_e1") == null && g3.Geo.Get("geo_test_east") != null, "a save without a geography section loads with only the data-file history");
        // the shared test game must not keep the fixture
        File.Delete(tmp); File.Delete(legacy);
    }

    /// <summary>burger geo simulate: runs the real simulation headless and reports what the dynamic province system did.</summary>
    private static int GeoSimulate(List<string> args)
    {
        int years = int.TryParse(Opt(args, "--years"), out var y) ? y : 10;
        double? splitPop = double.TryParse(Opt(args, "--split-population"), out var sp) ? sp : null;
        bool war = Flag(args, "--war");
        string project = ResolveProject(RequireProjectArg(args, "burger geo simulate <project> [--years N] [--split-population P] [--war]"));
        Log.EchoToConsole = false;
        string Run(out Game game, out int splits, out int merges, bool report)
        {
            game = Game.Load(GameDir(project), compileScripts: false, seed: 7);
            if (!game.Geo.Enabled) { splits = merges = 0; return "none"; }
            if (splitPop != null) game.Geo.DynamicSettings.SplitPopulation = splitPop.Value;
            game.StartNewGame(null);
            var occupied = new List<Burger.Core.Model.Location>();
            int months = years * 12;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int m = 0; m < months; m++)
            {
                if (war) // a rolling war: each month the front moves; locations next to a foreign country are taken for a while
                {
                    foreach (var l in occupied) l.Controller = null;
                    occupied.Clear();
                    var cands = game.World.Locations.Where(l => l.IsLand && l.Owner != null).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
                    for (int i = (m * 37) % 11; i < cands.Count && occupied.Count < 60; i += 11)
                    {
                        var foe = cands[i].Neighbors.Select(a => a.To.Owner).FirstOrDefault(o => o != null && o != cands[i].Owner);
                        if (foe == null) continue;
                        cands[i].Controller = foe; occupied.Add(cands[i]);
                    }
                }
                do game.Tick(); while (!(game.World.Date.Day == 1 && game.World.Date.Hour == 0));
            }
            splits = game.GeoDynamics.Splits; merges = game.GeoDynamics.Merges;
            int day = game.World.Date.TotalDays;
            if (report) Console.WriteLine($"  simulated {years} years in {sw.Elapsed.TotalSeconds:F1}s; now {HistoricalCalendar.Format(day)}");
            return string.Join(";", game.Geo.Snapshot(day).Provinces.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id + ":" + string.Join(",", p.Atoms)));
        }
        string a = Run(out var g1, out var s1, out var m1, true);
        PrintLog();
        if (a == "none") { Console.WriteLine("No geography layer in this project."); return 1; }
        var reg = g1.Geo;
        int endDay = g1.World.Date.TotalDays;
        Console.WriteLine($"  dynamic splits: {s1}, merges: {m1}; active provinces now {reg.Snapshot(endDay).Provinces.Count}");
        foreach (var op in reg.DynamicOperations.Take(12))
            Console.WriteLine($"    {HistoricalCalendar.Format(op.Date)} {op.Kind} {string.Join("+", op.Parents)} -> {string.Join(", ", op.Children.Select(c => c.Name ?? c.Id))}  [{op.Reason}]");
        var issues = GeoValidator.Run(reg, g1.World, endDay);
        int errs = issues.Count(i => i.Severity == GeoSeverity.Error);
        Console.WriteLine($"  validator errors at the end: {errs}");
        foreach (var i in issues.Where(i => i.Severity == GeoSeverity.Error).Take(5)) Console.WriteLine("    " + i);
        string b = Run(out _, out var s2, out var m2, false);
        bool det = a == b && s1 == s2 && m1 == m2;
        Console.WriteLine($"  deterministic (two identical runs agree): {(det ? "yes" : "NO")}");
        string tmp = Path.Combine(Path.GetTempPath(), "burger_geo_sim.sav");
        SaveGame.Save(g1, tmp);
        var g2 = Game.Load(GameDir(project), compileScripts: false, seed: 7);
        SaveGame.Load(g2, tmp);
        string c = string.Join(";", g2.Geo.Snapshot(endDay).Provinces.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id + ":" + string.Join(",", p.Atoms)));
        bool rt = c == a;
        Console.WriteLine($"  save/load keeps the dynamic provinces: {(rt ? "yes" : "NO")}");
        try { File.Delete(tmp); } catch { }
        return errs == 0 && det && rt ? 0 : 1;
    }
}
