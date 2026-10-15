using System.Diagnostics;
using Burger.Core;
using Burger.Core.Multiplayer;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>
    /// Mods whose <c>validate</c> or <c>apply</c> never return, ignore the stop, recurse without end or allocate without end.
    /// The host and a client must stay responsive, no thread may keep running afterwards, and both machines must stay in sync.
    /// </summary>
    private static void RunawayMods(string project, string tmp, Func<string, int, Game> Load, Action<bool, string> Check)
    {
        Console.WriteLine("Runaway mod code (validate and apply)");
        string dir = CopyProject(project, Path.Combine(tmp, "runaway"));
        Directory.CreateDirectory(Path.Combine(dir, "scripts"));
        File.WriteAllText(Path.Combine(dir, "scripts", "Runaway.cs"), @"using System.Collections.Generic; using System.Linq; using Burger.Core; using Burger.Core.Modding;
namespace Runaway { public class RunawayMod : GameScript {
  static int Deep(int x) => Deep(x + 1) + 1;
  static IEnumerable<int> Forever() { for (int i = 0; ; i++) yield return i; }
  public override void OnLoad() {
    var s = ModMessageSchema.Create().Int(""n"", 1, 10);
    Network.Register(""spin"", s, m => { while (true) { } }, m => { });
    Network.Register(""swallow"", s, m => { while (true) { try { while (true) { } } catch { } finally { } } }, m => { });
    Network.Register(""filter"", s, m => { while (true) { try { for (;;) { } } catch (System.Exception) when (true) { } } }, m => { });
    Network.Register(""jump"", s, m => { again: goto again; }, m => { });
    Network.Register(""recurse"", s, m => Deep(0) > 0 ? null : ""x"", m => { });
    Network.Register(""lambdas"", s, m => Enumerable.Repeat(0, int.MaxValue).SelectMany(a => Enumerable.Repeat(a, int.MaxValue).Select(b => b + 1)).Contains(-1) ? null : ""x"", m => { });
    Network.Register(""iterator"", s, m => Forever().Contains(-1) ? null : ""x"", m => { });
    Network.Register(""hog"", s, m => { var l = new List<int[]>(); while (true) l.Add(new int[1 << 16]); }, m => { });
    Network.Register(""applyloop"", s, m => null, m => { m.Sender.Gold += 1; while (true) { try { while (true) { } } catch { } } });
    Network.Register(""fine"", s, m => null, m => { m.Sender.Gold = 777777; });
  } } }");
        File.WriteAllText(Path.Combine(dir, "scripts", "mod.json"), "{\"permissions\":[\"network\"]}");

        var hg = Load(dir, 6);
        foreach (var e in hg.Mods.CompileErrors) Console.WriteLine("    " + e);
        Check(hg.ModMessageKeys.Count() == 10, "a mod with runaway rules still compiles and loads (" + hg.ModMessageKeys.Count() + " messages)");
        string Key(string n) => hg.ModMessageKeys.First(k => k.EndsWith("/" + n));
        var host = LockstepSession.Host(hg, "sec-test", 6, "Host");
        var (ca, cb) = MemoryTransport.Pair(); host.AddPeer(cb);
        var client = LockstepSession.Join(ca, "Ann", (_, s) => Load(dir, s));
        void Frame() { host.Update(0.05); client.Update(0.05); }
        bool Until(Func<bool> c, int max = 3000) { for (int i = 0; i < max && !c(); i++) { Frame(); if (i % 50 == 0) Thread.Sleep(1); } return c(); }
        Check(Until(() => client.Game != null && client.Players.Count >= 2), "client joined with the runaway mod");
        var lc = hg.World.Countries.First(c => c.Exists && hg.World.Divisions.Any(d => d.Owner == c));
        host.Pick(lc.Tag);
        Until(() => client.Players.Any(p => p.Tag == lc.Tag), 600);

        var proc = Process.GetCurrentProcess();
        double IdleCpu()
        {
            // the quietest of three seconds: JIT and GC work after loading a game is not a runaway thread
            GC.Collect(); GC.WaitForPendingFinalizers();
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                proc.Refresh(); var cpu0 = proc.TotalProcessorTime; Thread.Sleep(1000); proc.Refresh();
                best = Math.Min(best, (proc.TotalProcessorTime - cpu0).TotalMilliseconds);
            }
            return best;
        }
        double idle0 = IdleCpu();
        proc.Refresh(); int threads0 = proc.Threads.Count;
        foreach (var n in new[] { "spin", "swallow", "filter", "jump", "recurse", "lambdas", "iterator", "hog" })
        {
            var sw = Stopwatch.StartNew();
            host.Submit(GameCommand.ModMessage, Key(n), "1");
            long first = sw.ElapsedMilliseconds; sw.Restart();
            host.Submit(GameCommand.ModMessage, Key(n), "1");
            long second = sw.ElapsedMilliseconds;
            string why = Log.Entries.LastOrDefault(l => l.Message.Contains("'" + Key(n) + "'"))?.Message ?? "no log";
            why = why.Contains('(') ? why[(why.IndexOf('(') + 1)..why.IndexOf(')')] : why;
            Check(first < 1000 && why is "ran out of steps" or "recursion too deep" or "allocated too much memory", $"validate '{n}' is stopped on the host thread ({first} ms, {why})");
            Check(second < 20, $"'{n}' is then switched off ({second} ms)");
        }

        // apply runs on every machine: it must stop at the same step everywhere and leave both in sync
        double g0 = lc.Gold;
        host.SetSpeed(5); host.SetPaused(false);
        var apSw = Stopwatch.StartNew();
        host.Submit(GameCommand.ModMessage, Key("applyloop"), "1");
        Check(Until(() => lc.Gold != g0, 600), "apply that never returns is reached and stopped on the host");
        Check(Until(() => client.Game!.World.GetCountry(lc.Tag)!.Gold != g0, 600), "and on the client");
        Check(apSw.ElapsedMilliseconds < 5000, $"both machines kept running ({apSw.ElapsedMilliseconds} ms)");
        long t0 = host.Tick;
        Check(Until(() => host.Tick > t0 + 20 && client.Tick > t0 + 20, 600), "the game keeps ticking after the stopped apply");
        host.Submit(GameCommand.ModMessage, Key("applyloop"), "1");
        Check(lc.Gold == g0 + 1, "the host then refuses the message whose apply was stopped");

        // nothing left running: no extra thread, no busy core
        proc.Refresh();
        Check(proc.Threads.Count <= threads0 + 2, $"no thread left behind ({threads0} -> {proc.Threads.Count})");
        double idle1 = IdleCpu();
        Check(idle1 < idle0 + 200, $"no core kept busy after the stops ({idle0:0} ms CPU in the quietest idle second before, {idle1:0} ms after)");

        host.Submit(GameCommand.ModMessage, Key("fine"), "1");
        Check(Until(() => lc.Gold > 700000 && client.Game!.World.GetCountry(lc.Tag)!.Gold > 700000, 800), "another message of the same mod still works on both machines");
        host.SetPaused(true); Until(() => client.Tick == host.Tick && client.Paused, 600);
        Check(client.Tick == host.Tick && StateChecksum.Compute(hg) == StateChecksum.Compute(client.Game!), "checksums equal after the stopped apply");
        host.Leave(); client.Leave();
    }
}
