using System.Diagnostics;
using System.Globalization;
using Burger.Core;
using Burger.Core.AI;

namespace Burger.Tools;

/// <summary>
/// Determinism scorecard for AI parallelism. For each seed three worlds are played at the same time: serial (Parallelism 1),
/// parallel, and parallel again. Each world prints a state digest at fixed checkpoints. A line of scorecard.txt asks whether two
/// digests match (kind = par_equal, a = serial|parallel, b = serial|parallel2, at = a day number or final); expect = 1.0-1.0.
/// The fourth world, "reload", saves at --reload-at (default: middle checkpoint), loads the save into a fresh game and plays on: a line asking
/// serial = reload tests that save and load lose nothing (including the RNG stream positions).
/// Usage: burger validate-par PROJECT --seeds 1,2,3 [--days N] [--every N] [--parallelism N] [--reload-at DAY]
/// </summary>
public static partial class Program
{
    private static readonly string[] ParRuns = { "serial", "parallel", "parallel2", "reload" };

    /// <summary>FNV-1a 64 over the simulation state that must not depend on thread timing.</summary>
    internal static string StateDigest(Game game)
    {
        ulong h = 14695981039346656037UL;
        void B(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 124; h *= 1099511628211UL; }
        void D(double d) => B(d.ToString("R", CultureInfo.InvariantCulture));
        var w = game.World;
        B(w.Date.ToDisplay());
        foreach (var c in w.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal))
        {
            B(c.Tag); B(c.Exists ? "1" : "0"); D(c.Gold); D(c.Manpower);
        }
        foreach (var s in w.States.OrderBy(s => s.Key, StringComparer.Ordinal)) { B(s.Key); B(s.Owner?.Tag ?? "-"); }
        foreach (var d in w.Divisions.OrderBy(d => d.Id))
        {
            B(d.Id.ToString(CultureInfo.InvariantCulture)); B(d.Owner.Tag); B(d.Location.Key); D(d.Strength); D(d.Org);
        }
        foreach (var c in w.Characters.OrderBy(c => c.Id)) { B(c.Id.ToString(CultureInfo.InvariantCulture)); B(c.Alive ? "1" : "0"); }
        foreach (var war in w.Wars.OrderBy(x => x.Name, StringComparer.Ordinal)) { B(war.Name); D(war.WarScore); }
        if (game.Politics.Enabled)
            foreach (var c in w.Countries.OrderBy(c => c.Tag, StringComparer.Ordinal).Where(c => c.Exists))
            {
                var pc = game.Politics.Of(c);
                B(pc.Ruling);
                foreach (var kv in pc.Popularity.OrderBy(k => k.Key, StringComparer.Ordinal)) { B(kv.Key); D(kv.Value); }
            }
        B(w.Relations.Alliances.Count.ToString(CultureInfo.InvariantCulture));
        B(w.Relations.Subjects.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (name, st) in game.Rngs.Snapshot()) { B(name); B(st.ToString(CultureInfo.InvariantCulture)); }
        return h.ToString("x16");
    }

    private static int ValidatePar(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger validate-par <project> --seeds 1,2,3 [--days N] [--every N]"));
        var seeds = (Opt(args, "--seeds") ?? "1,2,3").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string days = Opt(args, "--days") ?? "365", every = Opt(args, "--every") ?? "30";
        string par = Opt(args, "--parallelism") ?? "0";
        string reloadAt = Opt(args, "--reload-at") ?? (int.Parse(days) / 2 / int.Parse(every) * int.Parse(every)).ToString(CultureInfo.InvariantCulture);
        string exe = Environment.ProcessPath ?? "dotnet";
        bool viaDotnet = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();
        var procs = new List<(string Seed, string Run, Process P, Task<string> Out)>();
        foreach (var seed in seeds)
            foreach (var run in ParRuns)
            {
                var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false };
                if (viaDotnet) psi.ArgumentList.Add(typeof(Program).Assembly.Location);
                var childArgs = new List<string> { "validate", project, "--days", days, "--seed", seed, "--digest", every, "--parallelism", run == "parallel" || run == "parallel2" ? par : "1" };
                if (run == "reload") { childArgs.Add("--reload-at"); childArgs.Add(reloadAt); }
                foreach (var a in childArgs) psi.ArgumentList.Add(a);
                var p = Process.Start(psi)!;
                procs.Add((seed, run, p, p.StandardOutput.ReadToEndAsync()));
            }
        // seed -> run -> checkpoint -> digest
        var digests = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        int worst = 0;
        foreach (var (seed, run, p, outTask) in procs)
        {
            string text = outTask.Result;
            p.WaitForExit();
            worst = Math.Max(worst, p.ExitCode);
            var d = digests.GetValueOrDefault(seed) ?? (digests[seed] = new());
            var cp = d[run] = new Dictionary<string, string>();
            string tag = run == "reload" ? "RELOAD|" : "DIGEST|";
            foreach (var l in text.Split('\n').Where(l => l.StartsWith(tag)))
            {
                var parts = l.Trim().Split('|');
                if (parts.Length == 3) cp[parts[1]] = parts[2];
            }
        }
        Console.WriteLine($"{seeds.Length} seeds x {ParRuns.Length} runs in {sw.Elapsed.TotalSeconds:0} s");

        string card = Path.Combine(project, "scorecard.txt");
        if (!File.Exists(card)) { Console.WriteLine("no scorecard.txt in " + project); return 1; }
        int ok = 0, bad = 0, prioBad = 0;
        foreach (var l in ScorecardRecorder.Parse(card))
        {
            var n = l.Node;
            if (l.Kind != "par_equal") continue;
            string a = n.GetString("a", "serial")!, b = n.GetString("b", "parallel")!, at = n.GetString("at", "final")!;
            int hits = 0, asked = 0;
            var diverged = new List<string>();
            foreach (var seed in seeds)
            {
                var d = digests[seed];
                string? Pick(string run) => !d.TryGetValue(run, out var cp) || cp.Count == 0 ? null
                    : at == "final" ? cp.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)).Last().Value : cp.GetValueOrDefault(at);
                var (da, db) = (Pick(a), Pick(b));
                if (da == null || db == null) continue;
                asked++;
                if (da == db) hits++; else diverged.Add(seed);
            }
            if (asked == 0) { Console.WriteLine($"  [ n/a] {l.Name,-34} no digests"); continue; }
            double rate = hits / (double)asked;
            bool within = rate >= l.Min - 1e-9 && rate <= l.Max + 1e-9;
            if (within) ok++; else { bad++; if (l.Priority >= 2) prioBad++; }
            Console.WriteLine($"  [{(within ? "ok  " : "FAIL")}] P{l.Priority} {l.Name,-34} {hits}/{asked}" + (diverged.Count > 0 ? "  diverged seeds " + string.Join(",", diverged) : "") + $"   {l.Note}");
        }
        // first diverging checkpoint per seed, serial vs parallel
        foreach (var seed in seeds)
        {
            var d = digests[seed];
            if (!d.TryGetValue("serial", out var s) || !d.TryGetValue("parallel", out var q)) continue;
            var first = s.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)).FirstOrDefault(kv => q.GetValueOrDefault(kv.Key) != kv.Value);
            Console.WriteLine($"  seed {seed}: serial vs parallel " + (first.Key == null ? "identical at every checkpoint" : $"first differs at day {first.Key}"));
        }
        Console.WriteLine($"  PARALLELISM SCORECARD: {ok} within band, {bad} outside" + (prioBad > 0 ? $" ({prioBad} priority 2-5)" : ""));
        return worst != 0 ? worst : bad > 0 ? 2 : 0;
    }
}
