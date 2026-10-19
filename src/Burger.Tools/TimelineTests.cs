using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Save;

namespace Burger.Tools;

public static partial class Program
{
    private static int TimelineTest(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger timeline-test <project>"));
        Log.EchoToConsole = false;
        try { TimelineFeatureTests(project); }
        finally { foreach (var e in Log.Entries.TakeLast(10)) Console.WriteLine("  log: " + e); }
        Console.WriteLine(_failedChecks == 0 ? "timeline checks passed" : $"{_failedChecks} timeline checks FAILED");
        return _failedChecks == 0 ? 0 : 1;
    }

    /// <summary>Start dates: the map, names and forces of a later start follow the snapshots of history/timeline, and the game runs and saves from there.</summary>
    private static void TimelineFeatureTests(string project)
    {
        var g0 = Game.Load(GameDir(project), compileScripts: true, seed: 5);
        var tl = g0.Timeline;
        Check(tl.Enabled && tl.Snapshots.Count >= 40 && tl.Bookmarks.Count >= 40 && tl.Latest > tl.Earliest, $"timeline: {tl.Snapshots.Count} snapshots and {tl.Bookmarks.Count} bookmarks from {tl.Earliest.ToDisplay()} to {tl.Latest.ToDisplay()}");
        int Owned(Game g, string tag) => g.World.GetCountry(tag)?.OwnedStates.Count ?? 0;
        Game At(string date) => Game.Load(GameDir(project), compileScripts: true, seed: 5, startDate: GameDate.Parse(date, default));
        int Named(Game g, string part) => g.World.Countries.Where(c => c.Exists && c.Name.Contains(part, StringComparison.OrdinalIgnoreCase)).Sum(c => c.OwnedStates.Count);

        var first = At("-499.1.1");
        Check(first.World.Date == tl.Earliest && Owned(first, "ACH") == Owned(g0, "ACH"), "timeline: the first start date is the unchanged world");

        // eras and bookmarks
        Check(tl.Eras.Count >= 6 && tl.Eras[0].From == tl.Earliest && tl.Eras[^1].To >= tl.Latest, $"timeline: {tl.Eras.Count} eras from {tl.Eras[0].From.YearDisplay} to {tl.Eras[^1].To.YearDisplay}");
        for (int i = 1; i < tl.Eras.Count; i++)
            Check(tl.Eras[i].From.TotalDays == tl.Eras[i - 1].To.TotalDays + 1, $"timeline: the era {tl.Eras[i].Name} follows {tl.Eras[i - 1].Name} without a gap");
        Check(tl.Eras.All(e => e.Suggested.Count >= 3 && e.Suggested.All(d => e.Contains(d) && tl.Bookmarks.Any(b => b.Date == d))), "timeline: every era suggests at least three dates, each a bookmark of the era");
        Check(tl.Bookmarks.Where(b => b.Date > tl.Earliest).All(b => tl.Snapshots.Any(s => s.Date == b.Date)), "timeline: every bookmark after the first is the date of a snapshot");

        // the map at some dates
        var alex = At("-322.1.1");
        Check(Named(alex, "Alexander") > 80, $"timeline: the Empire of Alexander holds {Named(alex, "Alexander")} states");
        var rome = At("100.1.1");
        Check(Named(rome, "Roman Empire") > 120 && Named(rome, "Han") > 100, $"timeline: in 100 CE Rome holds {Named(rome, "Roman Empire")} states and Han China {Named(rome, "Han")}");
        var hre = At("1500.1.1");
        var german = hre.World.Countries.Where(c => c.Exists && c.OwnedStates.Any(s => s.Locations.Any(l => l.IsLand) && Math.Abs(hre.World.Map.Settings.NorthLat - (hre.World.Map.Settings.NorthLat - hre.World.Map.Settings.SouthLat) * s.Locations.First().CenterY / hre.World.Map.Height - 50) < 3 && Math.Abs(-180.0 + 360.0 * s.Locations.First().CenterX / hre.World.Map.Width - 11) < 4)).ToList();
        Check(german.Count >= 5 && german.All(c => c.OwnedStates.Count <= 40), $"timeline: the Holy Roman Empire is {german.Count} countries ({string.Join(", ", german.Select(c => $"{c.Name} {c.OwnedStates.Count}"))})");
        var greek = At("-399.1.1").World.Countries.Where(c => c.Exists && c.Tag is "ATH" or "SPA" or "COR" or "THB" or "ARG").Count();
        Check(greek >= 2, $"timeline: {greek} Greek city-states stand in 399 BCE");
        var zhou = At("-399.1.1").World.Countries.Where(c => c.Exists && c.Name is "Qin" or "Qi" or "Chu" or "Zhao" or "Wei" or "Yan" or "Han").Count();
        Check(zhou >= 5, $"timeline: the Zhou states are {zhou} countries in 399 BCE");
        var today = At("2010.1.1");
        Check(Named(today, "Russia") > 250 && Named(today, "China") > 250 && Named(today, "United States") > 200 && today.World.Date.Year == 2010, $"timeline: in 2010 Russia holds {Named(today, "Russia")} states, China {Named(today, "China")}, the United States {Named(today, "United States")}");

        // every bookmark loads, without a new error, into a world with capitals of its own and forces that belong to someone
        int errors = Log.ErrorCount;
        foreach (var b in tl.Bookmarks)
        {
            var gb = At(b.Date.ToScript());
            int countries = gb.World.Countries.Count(c => c.Exists);
            Check(countries >= 20 && gb.World.Countries.Where(c => c.Exists).All(c => c.Capital != null && c.Capital.Owner == c) && gb.World.Divisions.All(d => d.Owner.Exists), $"timeline: {b.Name} ({b.Date.YearDisplay}) is a world of {countries} countries");
        }
        Check(Log.ErrorCount == errors, $"timeline: loading every bookmark logs no error ({Log.ErrorCount - errors} new)");

        // it plays and saves
        var play = At("1914.1.1");
        play.StartNewGame(null);
        play.Ai.Enabled = true;
        for (int i = 0; i < 24 * 60; i++) play.Tick();
        Check(true, "timeline: a game started in 1914 runs two months");
        string tmp = Path.Combine(Path.GetTempPath(), "burger_timeline.sav");
        SaveGame.Save(play, tmp);
        var again = Game.Load(GameDir(project), compileScripts: true, seed: 5);
        SaveGame.Load(again, tmp);
        Check(again.World.Date == play.World.Date && again.World.Countries.Count(c => c.Exists) == play.World.Countries.Count(c => c.Exists), "timeline: a save of a later start loads with its map");
    }
}
