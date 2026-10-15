using System.Net;
using Burger.Core;
using Burger.Core.Multiplayer;

namespace Burger.Tools;

/// <summary>
/// <c>burger mp-test &lt;project&gt; [--days N] [--seed S]</c>: a host and three clients (one over TCP, two in memory) play the same world in one process.
/// Checks: picking countries, voting, commands, a forced desync with full-save resync, a player leaving (AI takeover), and equal state at the end.
/// </summary>
public static partial class Program
{
    private static int MultiplayerTest(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger mp-test <project> [--days N] [--seed S]"));
        int days = int.Parse(Opt(args, "--days") ?? "120");
        int seed = int.Parse(Opt(args, "--seed") ?? "7");
        int failures = 0;
        if (args.Contains("--watch")) Burger.Core.Log.EchoToConsole = true;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {what}"); if (!ok) failures++; }

        Game LoadStarted(string _, int s) { var g = Game.Load(GameDir(project), true, s); g.StartNewGame(null); return g; }
        var hostGame = LoadStarted(project, seed);
        var host = LockstepSession.Host(hostGame, "mp-test", seed, "Host");
        var toasts = new List<string>();
        host.Toast += t => toasts.Add(t);

        using var listener = new TcpHostListener();
        var code = JoinCode.Make(IPAddress.Loopback, listener.Port, listener.Secret);
        Console.WriteLine($"Join code {code}");
        Check(JoinCode.TryParse(code, out var h2, out int p2, out var sec2) && sec2.SequenceEqual(listener.Secret) && h2 == "127.0.0.1" && p2 == listener.Port, "join code round trip");

        var clients = new List<LockstepSession>();
        var tcp = TcpTransport.Connect("127.0.0.1", listener.Port, listener.Secret);
        clients.Add(LockstepSession.Join(tcp, "Ann", LoadStarted));
        for (int i = 0; i < 2; i++)
        {
            var (a, b) = MemoryTransport.Pair();
            host.AddPeer(b);
            clients.Add(LockstepSession.Join(a, i == 0 ? "Bob" : "Cy", LoadStarted));
            { var cl = clients[^1]; cl.Toast += t => Console.WriteLine($"    [{cl.Me?.Name}] {t}"); }
        }
        var watched = new HashSet<LockstepSession>();
        void Frame(double dt = 0.05)
        {
            while (listener.TryAccept(out var t)) host.AddPeer(t);
            host.Update(dt);
            foreach (var c in clients) c.Update(dt);
            if (args.Contains("--watch")) foreach (var c in clients) if (c.Disconnected && !watched.Contains(c)) { watched.Add(c); Console.WriteLine($"    WATCH {c.Me?.Name} disconnected at host tick {host.Tick}, its tick {c.Tick}, host sees {string.Join(",", host.Players.Select(p => p.Name + (p.Active ? "+" : "-")))}"); }
        }
        void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }
        bool Until(Func<bool> cond, int max = 4000) { for (int i = 0; i < max && !cond(); i++) { Frame(); if (i % 50 == 0) Thread.Sleep(1); } return cond(); }

        Check(Until(() => clients.All(c => c.Game != null && c.Players.Count == 4 && c.Me!.Id > 0)), "3 clients joined and received the world");

        var tags = hostGame.World.Countries.Where(c => c.Exists && hostGame.World.Divisions.Any(d => d.Owner == c)).Select(c => c.Tag)
            .OrderBy(t => t, StringComparer.Ordinal).Take(4).ToList();
        Check(tags.Count == 4, "four countries with divisions to play");
        Check(host.Pick(tags[0]) == null, "host picks " + tags[0]);
        for (int i = 0; i < 3; i++) clients[i].Pick(tags[i + 1]);
        clients[2].Pick(tags[0]); // taken: refused, keeps tags[3]
        Until(() => clients.All(c => c.Players.Count(p => p.Tag.Length > 0) == 4) && host.Players.All(p => p.Tag.Length > 0));
        Check(host.Players.Select(p => p.Tag).Distinct().Count() == 4, "no country held twice");

        // votes: one client is not a majority of 4, three are
        host.SetSpeed(5); host.SetPaused(false);
        Frames(40);
        clients[0].SetPaused(true); Frames(10);
        Check(!host.Paused, "1 of 4 voting to pause does not pause");
        clients[1].SetPaused(true); clients[2].SetPaused(true); Until(() => clients.All(c => c.Paused), 600);
        Console.WriteLine($"    host.Paused={host.Paused} clients={string.Join(",", clients.Select(c => c.Paused))}");
        Check(host.Paused && clients.All(c => c.Paused), "3 of 4 voting to pause pauses everybody");
        Check(toasts.Any(t => t.Contains("Majority voted to pause")), "toast on majority pause");
        host.SetPaused(false);

        Check(Until(() => hostGame.World.Date.TotalDays >= hostGame.World.Date.TotalDays + 0 && host.Tick > 24 * 5), "world runs");
        // commands from everybody
        int Days() => hostGame.World.Date.TotalDays;
        int start = Days();
        var hostCountry = hostGame.World.GetCountry(tags[0])!;
        var enemyTag = hostGame.World.Countries.Where(c => c.Exists && !tags.Contains(c.Tag) && hostGame.World.Divisions.Any(d => d.Owner == c)).Select(c => c.Tag).OrderBy(t => t, StringComparer.Ordinal).FirstOrDefault();
        var myDiv = hostGame.World.Divisions.FirstOrDefault(d => d.Owner == hostCountry);
        if (myDiv != null && myDiv.Location.Neighbors.Count > 0) host.Submit(GameCommand.Move, myDiv.Id.ToString(), myDiv.Location.Neighbors[0].To.Key);
        if (myDiv != null && myDiv.Location.Neighbors.Count > 1) host.Submit(GameCommand.Move, myDiv.Id.ToString(), myDiv.Location.Neighbors[1].To.Key, "q");
        if (hostGame.Data.Equipment.Keys.FirstOrDefault() is { } eqKey) host.Submit(GameCommand.QueueProduction, eqKey, "0");
        clients[1].Submit(GameCommand.Respond, "CallToArms", tags[0], "1"); // nothing pending: refused everywhere, no drift
        if (enemyTag != null) clients[0].Submit(GameCommand.DeclareWar, enemyTag);
        var eq = hostGame.Data.Equipment.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        if (eq != null) clients[1].Submit(GameCommand.QueueProduction, eq, "2");
        clients[1].SendChat("hello");
        Until(() => Days() >= start + days / 3);

        // --battle: wait for a battle in progress, so the resync's save is taken in the middle of one
        bool battleMode = args.Contains("--battle");
        int battlesAtDrift = 0;
        if (battleMode)
        {
            bool found = Until(() => hostGame.World.Battles.Count > 0, 400000);
            Check(found, "a battle is in progress on the host");
            battlesAtDrift = hostGame.World.Battles.Count;
            Console.WriteLine($"    {battlesAtDrift} battle(s) at the injected drift, {hostGame.World.Wars.Count} war(s), {hostGame.World.Date.ToDisplay()}");
        }
        // a desync: one client's gold drifts
        Check(host.Resyncs == 0 && clients.All(c => c.Resyncs == 0), "no resync before the injected drift (a natural desync would show here)");
        int resyncsBefore = host.Resyncs;
        clients[1].Game.World.GetCountry(tags[2])!.Gold += 1;
        Until(() => host.Resyncs > resyncsBefore && clients[1].Resyncs > 0, 20000);
        Check(host.Resyncs > resyncsBefore && clients[1].Resyncs > 0, "checksum caught the drift and the client was resynced");
        Check(host.Resyncs == 1 && clients[0].Resyncs == 0, "exactly one resync: the injected one, only for the drifted client");

        if (args.Contains("--after-resync"))
        {
            host.SetPaused(true);
            Until(() => clients.All(c => c.Tick == host.Tick), 200000);
            string hdg = StateDigest(hostGame);
            Console.WriteLine($"    after resync, tick {host.Tick} ({hostGame.World.Date.ToDisplay()}): host {hdg}, " + string.Join(", ", clients.Select(c => c.Me!.Name + " " + StateDigest(c.Game) + (c.Tick == host.Tick ? "" : " (tick " + c.Tick + ")"))));
            foreach (var c in clients) Console.WriteLine($"    diff {c.Me!.Name}: " + string.Join(" | ", StateChecksum.Diff(hostGame, c.Game).Take(8)));
            host.SetPaused(false);
        }
        if (args.Contains("--diag"))
        {
            host.SetPaused(true);
            Until(() => clients.All(c => c.Tick == host.Tick), 8000);
            foreach (var c in clients) Console.WriteLine($"    diag {c.Me!.Name}: " + string.Join(" | ", StateChecksum.Diff(hostGame, c.Game).Take(8)));
            host.SetPaused(false);
        }
        // a player leaves
        Until(() => Days() >= start + days * 2 / 3);
        clients[2].Leave();
        Until(() => host.Players.First(p => p.Name == "Cy").IsAi);
        Frames(200);
        Check(host.Players.First(p => p.Name == "Cy").IsAi, "leaving player shows as AI in the list");
        Check(!hostGame.World.GetCountry(tags[3])!.IsPlayer && !clients[0].Game.World.GetCountry(tags[3])!.IsPlayer, "left country is AI-run on host and clients");
        Check(toasts.Any(t => t.Contains("left")), "toast on leave");
        clients.RemoveAt(2);

        Until(() => Days() >= start + days);
        host.SetPaused(true);
        Until(() => clients.All(c => c.Tick == host.Tick), 200000);
        foreach (var c in clients) Console.WriteLine($"    tick {c.Me!.Name} {c.Tick} host {host.Tick} resyncs {c.Resyncs}");
        Check(clients.All(c => c.Tick == host.Tick), "clients caught up with the host at tick " + host.Tick);
        ulong hs = StateChecksum.Compute(hostGame);
        foreach (var c in clients) Check(StateChecksum.Compute(c.Game) == hs, $"{c.Me!.Name} checksum equals the host's ({hs:x16})");
        Check(host.Players.Any(p => p.Id > 0 && p.Active && p.PingMs >= 0), "ping tracked");
        Check(host.Resyncs == 1, "no resync other than the injected one by the end");
        string hd = StateDigest(hostGame);
        foreach (var c in clients) Check(StateDigest(c.Game) == hd, $"{c.Me!.Name} full state digest equals the host's ({hd}; covers states, characters, wars, politics)");
        if (battleMode) Console.WriteLine($"    battles at the end on host {hostGame.World.Battles.Count}, battle score total {hostGame.World.Wars.Sum(w => w.BattleScore):0.##}");
        Console.WriteLine($"Ran {host.Tick} ticks ({hostGame.World.Date.ToDisplay()}), resyncs {host.Resyncs} (1 injected on purpose)");
        Console.WriteLine($"    pending proposals host {hostGame.PendingProposals.Count}: " + string.Join(", ", hostGame.PendingProposals.Select(p => p.Kind + ":" + p.From.Tag + ">" + p.To.Tag)));
        foreach (var t in toasts.Where(t => t.Contains("Desync"))) Console.WriteLine("    " + t);
        foreach (var c in clients) c.Leave();
        host.Leave();
        return failures == 0 ? 0 : 1;
    }
}
