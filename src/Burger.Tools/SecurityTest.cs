using System.Net.Sockets;
using System.Text;
using Burger.Core;
using Burger.Core.Modding;
using Burger.Core.Multiplayer;

namespace Burger.Tools;

/// <summary>
/// <c>burger sec-test &lt;project&gt;</c>: attacks the multiplayer and modding layers and checks that each attack fails while a legitimate mod keeps working.
/// Covers: the mod sandbox (escape attempts), path traversal and injection, malformed packets (fuzz), the encrypted channel, host authority,
/// and a mod's network message end to end.
/// </summary>
public static partial class Program
{
    private static int SecurityTest(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger sec-test <project>"));
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {what}"); if (!ok) failures++; }
        string tmp = Path.Combine(Path.GetTempPath(), "burger_sec_" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        try
        {
            // ------------------------------------------------------------------ sandbox
            Console.WriteLine("Sandbox escapes (each must be refused)");
            var escapes = new (string Name, string Code)[]
            {
                ("file read", "System.IO.File.ReadAllText(\"/etc/passwd\");"),
                ("directory listing", "System.IO.Directory.GetFiles(\"/\");"),
                ("process start", "System.Diagnostics.Process.Start(\"/bin/sh\");"),
                ("environment", "System.Environment.GetEnvironmentVariable(\"HOME\");"),
                ("http client", "new System.Net.Http.HttpClient();"),
                ("socket", "new System.Net.Sockets.TcpClient();"),
                ("reflection invoke", "typeof(string).GetMethod(\"Intern\")!.Invoke(null, new object[]{\"x\"});"),
                ("assembly load", "System.Reflection.Assembly.Load(\"System.Diagnostics.Process\");"),
                ("activator", "System.Activator.CreateInstance(typeof(object));"),
                ("marshal", "System.Runtime.InteropServices.Marshal.AllocHGlobal(8);"),
                ("dllimport", "[System.Runtime.InteropServices.DllImport(\"libc\")] static extern int getpid(); getpid();"),
                ("clock", "var t = System.DateTime.Now;"),
                ("random guid", "var g = System.Guid.NewGuid();"),
                ("engine multiplayer", "Burger.Core.Multiplayer.LockstepSession s = null!; s.Leave();"),
                ("engine save load", "Game.Load(\"/tmp\", false);"),
                ("mod manager", "var m = Game.Mods;"),
                ("threads", "new System.Threading.Thread(() => { }).Start();"),
                ("unsafe pointer", "unsafe { int x = 1; int* p = &x; }"),
                // routes through APIs that look harmless (audit of the allowed surface; see docs/multiplayer-security.md)
                ("type to assembly", "var a = typeof(string).Assembly;"),
                ("type to module", "var m = \"x\".GetType().Module;"),
                ("type by name", "var t = System.Type.GetType(\"System.IO.File\");"),
                ("type members list", "var ms = typeof(int).GetMethods();"),
                ("type invoke member", "typeof(object).InvokeMember(\"ToString\", System.Reflection.BindingFlags.InvokeMethod, null, new object(), null);"),
                ("delegate method info", "Func<int> f = () => 1; var mi = f.Method;"),
                ("delegate dynamic invoke", "Func<int> f = () => 1; f.DynamicInvoke();"),
                ("delegate create", "var d = System.Delegate.CreateDelegate(typeof(Action), null, \"x\");"),
                ("exception target site", "var s = new Exception().TargetSite;"),
                ("runtime helpers", "System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(object).TypeHandle);"),
                ("unsafe class", "var x = System.Runtime.CompilerServices.Unsafe.SizeOf<int>();"),
                ("intptr", "var p = System.IntPtr.Zero;"),
                ("native library", "System.Runtime.InteropServices.NativeLibrary.Load(\"libc\");"),
                ("memory pool", "var p = System.Buffers.MemoryPool<byte>.Shared;"),
                ("array pool", "var p = System.Buffers.ArrayPool<byte>.Shared;"),
                ("explicit layout", "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] struct S { [System.Runtime.InteropServices.FieldOffset(0)] int a; }"),
                ("time provider", "var t = System.TimeProvider.System.GetUtcNow();"),
                ("utc now", "var t = System.DateTime.UtcNow;"),
                ("date today", "var t = System.DateTime.Today;"),
                ("offset now", "var t = System.DateTimeOffset.UtcNow;"),
                ("guid v7", "var g = System.Guid.CreateVersion7();"),
                ("tick count", "var t = System.Environment.TickCount64;"),
                ("stopwatch", "var t = System.Diagnostics.Stopwatch.GetTimestamp();"),
                ("unseeded random", "var r = System.Random.Shared.Next();"),
                ("new random", "var r = new System.Random().Next();"),
                ("crypto random", "var r = System.Security.Cryptography.RandomNumberGenerator.GetInt32(5);"),
                ("sleep", "System.Threading.Thread.Sleep(1);"),
                ("task run", "System.Threading.Tasks.Task.Run(() => { });"),
                ("interlocked", "int x = 0; System.Threading.Interlocked.Increment(ref x);"),
                ("lock statement", "var o = new object(); lock (o) { }"),
                ("thread pool", "System.Threading.ThreadPool.QueueUserWorkItem(_ => { });"),
                ("timer", "new System.Threading.Timer(_ => { });"),
                ("gc", "System.GC.Collect();"),
                ("weak reference", "var w = new System.WeakReference(new object());"),
                ("uri", "var u = new System.Uri(\"file:///etc/passwd\");"),
                ("set culture", "System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;"),
                ("read culture", "var c = System.Globalization.CultureInfo.CurrentCulture;"),
                ("regex", "System.Text.RegularExpressions.Regex.IsMatch(\"a\", \"a\");"),
                ("vector simd", "var v = System.Numerics.Vector<float>.One;"),
                ("process id", "var p = System.Environment.ProcessId;"),
                ("app context", "var s = System.AppContext.BaseDirectory;"),
                ("console", "System.Console.WriteLine(\"x\");"),
                ("string intern", "var s = string.Intern(\"x\");"),
                ("linq expression compile", "System.Linq.Expressions.Expression.Lambda<Func<int>>(System.Linq.Expressions.Expression.Constant(1)).Compile();"),
                ("member info", "var m = typeof(object).GetMember(\"ToString\");"),
                ("member info module", "var m = typeof(object).Module;"),
                ("type interfaces", "var m = typeof(object).GetInterfaces();"),
                ("dynamic", "dynamic d = 1; d.Foo();"),
            };
            string header = "using System; using Burger.Core; using Burger.Core.Modding;\nnamespace Esc { public class M : GameScript { public override void OnLoad() { ";
            foreach (var (name, code) in escapes)
            {
                string dir = Path.Combine(tmp, "esc_" + name.Replace(' ', '_'));
                Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                string file = Path.Combine(dir, "scripts", "M.cs");
                bool local = code.StartsWith("[");
                File.WriteAllText(file, header + (local ? "" : code) + " } " + (local ? code.Replace("static extern int getpid(); getpid();", "static extern int getpid();") : "") + " } }");
                var errs = new List<string>();
                var asm = ModManager.Compile(new List<string> { file }, dir, errs);
                Check(asm == null && errs.Count > 0 && !errs[0].Contains("Attribute"), $"refused: {name}" + (errs.Count > 0 ? " (" + errs[0].Split('\n')[0][..Math.Min(70, errs[0].Split('\n')[0].Length)] + ")" : ""));
            }

            // the API that mods are meant to use must keep compiling (so the audit above did not cripple modding)
            {
                string dir = Path.Combine(tmp, "allowed_api"); Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                string file = Path.Combine(dir, "scripts", "M.cs");
                File.WriteAllText(file, header + @"
  var l = new System.Collections.Generic.List<int> { 3, 1, 2 }; l.Sort();
  var d = new System.Collections.Generic.Dictionary<string, double>(); d[""a""] = System.Math.Sqrt(2) + System.Math.Abs(-1.5);
  var sb = new System.Text.StringBuilder(); sb.Append(l.Count).Append(string.Join("","", l.Select(x => x * 2)));
  string s = 12.5.ToString(System.Globalization.CultureInfo.InvariantCulture) + sb + System.Convert.ToString(7) + string.Format(""{0:N2}"", 3.14159);
  var big = System.Numerics.BigInteger.Pow(2, 100);
  var imm = System.Collections.Immutable.ImmutableList.Create(1, 2, 3);
  Span<int> span = stackalloc int[4]; span[0] = 1;
  var tuple = (a: 1, b: ""x""); var (a1, b1) = tuple;
  Func<int, int> f = x => x + 1; Action<string> act = _ => { }; act(f(1).ToString());
  var q = from x in l where x > 1 orderby x descending select x;
  var e = Enum.Parse<System.DayOfWeek>(""Monday""); var t = typeof(Game).Name; var ts = new TimeSpan(1, 2, 3); var dt = new DateTime(2020, 1, 1).AddDays(5);
  try { throw new InvalidOperationException(""x""); } catch (Exception ex) when (ex.Message == ""x"") { } finally { }
  Log.Info(""ok"" + s + big + imm.Count + q.Count() + e + t + ts + dt);
  } } }");
                var errs = new List<string>();
                var asm = ModManager.Compile(new List<string> { file }, dir, errs);
                Check(asm != null, "ordinary mod code (collections, LINQ, Math, StringBuilder, BigInteger, Span, tuples, formatting) still compiles" + (errs.Count > 0 ? ": " + errs[0].Split('\n')[0] : ""));
            }

            // a malicious image must be refused even when it bypasses the compiler: verify raw metadata of the engine itself (uses forbidden APIs)
            var engineImage = File.ReadAllBytes(typeof(Game).Assembly.Location);
            Check(ModSandbox.Verify(engineImage).Count > 0, "verifier rejects an image full of IO and reflection (the engine itself)");

            // ------------------------------------------------------------------ paths, names, chat
            Console.WriteLine("Paths, names, injection");
            string root = Path.Combine(tmp, "root"); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ok.txt"), "x");
            foreach (var bad in new[] { "../x", "..", "a/../../b", "/etc/passwd", "a\\b", "C:\\x", "a/./b", "a//b", "a\0b", "~/x", ".hidden/x", new string('a', 300), "" })
                Check(Sanitize.RelativePath(root, bad) == null, "path refused: " + (bad.Length > 20 ? bad[..20] + "..." : bad.Replace("\0", "\\0")));
            Check(Sanitize.RelativePath(root, "ok.txt") != null, "plain relative path accepted");
            try { File.CreateSymbolicLink(Path.Combine(root, "link"), "/etc"); Check(Sanitize.RelativePath(root, "link/passwd") == null, "symlink out of the root refused"); }
            catch { Console.WriteLine("  [skip] symlinks not available"); }
            Check(Sanitize.PlayerName("Ev\u202Eil\u200B\n\tName") == "Evil Name" || !Sanitize.PlayerName("Ev\u202Eil\u200B\n\tName").Any(c => char.IsControl(c) || c is '\u202E' or '\u200B'), "name loses bidi, zero-width and control characters");
            Check(Sanitize.Chat(new string('x', 5000)).Length <= Sanitize.MaxChat, "chat length capped");
            Check(Sanitize.Tag("A|B") == "" && Sanitize.Tag("A,B") == "" && Sanitize.Tag("../x") == "" && Sanitize.Tag("GER") == "GER", "tags cannot carry separators");
            Check(Net.Unesc(Net.Esc("a|b;c,d\n")) == "a|b;c,d\n" && !Net.Esc("a|b;c,d\n").Any(c => c is '|' or ',' or ';' or '\n'), "field escaping blocks protocol injection");
            Check(!JoinCode.TryParse(new string('Z', 5000), out _, out _, out _) && !JoinCode.TryParse("ABC", out _, out _, out _), "bad join codes refused");

            // ------------------------------------------------------------------ fuzz
            Console.WriteLine("Fuzz: malformed packets");
            var rng = new Random(1234);
            string[] valid = { "HELLO|2|Ann|-", "READY", "PICK|GER", "CMD|0||move|1,x", "VOTE|pause|1", "CHAT|hi", "ACK|5", "SUM|5|0123456789abcdef", "PONG|1",
                "WELCOME|1|p|1|aa|bb|2", "SYNC|5|A,B|100", "SYNCDATA|0|AAAA", "SYNCEND|1", "PLAYERS|1,a,b,1,0,1,1", "HORIZON|9", "PACE|1|2", "TOAST|x", "PING|1", "CMD|3|GER|move|1,x", "CHAT|a|b" };
            int thrown = 0;
            for (int i = 0; i < 40000; i++)
            {
                string line = Mutate(rng, valid[rng.Next(valid.Length)]);
                try { foreach (var d in new[] { Wire.Dir.ToHost, Wire.Dir.ToClient }) { Wire.Check(d, line, out var f, rng.Next(2) == 0); _ = GameCommand.Decode(f, 1); } }
                catch { thrown++; }
            }
            Check(thrown == 0, $"Wire.Check and GameCommand.Decode never throw on 40000 mutated lines (threw {thrown})");
            Check(Wire.Check(Wire.Dir.ToHost, new string('A', Wire.MaxLine + 1), out _) != null, "oversized line refused");
            Check(Wire.Check(Wire.Dir.ToHost, "WELCOME|1|p|1|a|b|2", out _) != null, "host-only message from a client refused");
            Check(Wire.Check(Wire.Dir.ToClient, "CMD|1|GER|move|", out _) == null && Wire.Check(Wire.Dir.ToHost, "FOO|1", out _) != null, "unknown message refused");
            Check(Wire.Check(Wire.Dir.ToHost, "CMD|0||move|1,x", out _, false) != null, "command before the handshake finished refused");

            // ------------------------------------------------------------------ encrypted channel
            Console.WriteLine("Encrypted channel");
            using (var listener = new TcpHostListener())
            {
                bool wrong = false;
                try { TcpTransport.Connect("127.0.0.1", listener.Port, JoinCode.NewSecret()); } catch { wrong = true; }
                Thread.Sleep(200);
                Check(wrong && !listener.TryAccept(out _), "client with the wrong secret fails the handshake and is not accepted");

                using (var raw = new TcpClient("127.0.0.1", listener.Port)) { var w = raw.GetStream(); w.Write(Encoding.UTF8.GetBytes("HELLO|2|Mallory|-\n")); Thread.Sleep(300); }
                Check(!listener.TryAccept(out _), "plaintext protocol line is not accepted");

                using (var raw = new TcpClient("127.0.0.1", listener.Port))
                {
                    var w = raw.GetStream(); bool closed = false;
                    try { w.Write(new byte[Wire.MaxRawLine * 3]); raw.ReceiveTimeout = 3000; var b = new byte[16]; for (int i = 0; i < 20 && !closed; i++) closed = w.Read(b, 0, 16) <= 0; }
                    catch { closed = true; }
                    Check(closed, "line without end is cut off at the size limit and the link closed");
                }

                var good = TcpTransport.Connect("127.0.0.1", listener.Port, listener.Secret);
                ITransport? srv = null; for (int i = 0; i < 100 && !listener.TryAccept(out srv); i++) Thread.Sleep(20);
                Check(srv != null, "client with the right secret is accepted");
                if (srv != null)
                {
                    good.Send("hello"); string? got = null; for (int i = 0; i < 100 && !srv.TryReceive(out got!); i++) Thread.Sleep(20);
                    Check(got == "hello", "encrypted line arrives intact");
                    srv.Send(new string('é', 20000)); string? back = null; for (int i = 0; i < 100 && !good.TryReceive(out back!); i++) Thread.Sleep(20);
                    Check(back != null && back.Length == 20000, "large line arrives intact");
                    good.Close(); srv.Close();
                }
            }
            ChannelAttacks(Check);
            SaveAttacks(project, tmp, Check);
            SumFlood(project, Check);
            AppPathSmoke(project, Check);
            PeerIdentity(Check);

            // ------------------------------------------------------------------ host authority + mod messages
            Console.WriteLine("Host authority and a legitimate mod over the network");
            string good1 = CopyProject(project, Path.Combine(tmp, "withmod"));
            Directory.CreateDirectory(Path.Combine(good1, "scripts"));
            const string modSource = @"using Burger.Core; using Burger.Core.Modding;
namespace Gift { public class GiftMod : GameScript {
  public override void OnLoad() {
    Network.Register(""gift"", ModMessageSchema.Create().Int(""amount"", 1, 100),
      m => m.Sender.Gold >= m.GetInt(""amount"") ? null : ""not enough gold"",
      m => { m.Sender.Gold -= m.GetInt(""amount""); });
  } } }";
            File.WriteAllText(Path.Combine(good1, "scripts", "GiftMod.cs"), modSource);
            File.WriteAllText(Path.Combine(good1, "scripts", "mod.json"), "{\"permissions\":[\"network\"]}");
            string noPerm = CopyProject(project, Path.Combine(tmp, "noperm"));
            Directory.CreateDirectory(Path.Combine(noPerm, "scripts"));
            File.WriteAllText(Path.Combine(noPerm, "scripts", "GiftMod.cs"), modSource);

            Game Load(string gameRoot, int s) { var g = Game.Load(gameRoot, true, s); g.StartNewGame(null); return g; }
            var denied = Load(noPerm, 5);
            Check(denied.ModMessageKeys.Count() == 0, "a mod without the network permission cannot register a message");
            var hostGame = Load(good1, 5);
            Check(hostGame.ModMessageKeys.Count() == 1, "a mod with the network permission registers its message (" + string.Join(",", hostGame.ModMessageKeys) + ")");
            string key = hostGame.ModMessageKeys.FirstOrDefault() ?? "gift";

            var host = LockstepSession.Host(hostGame, "sec-test", 5, "Host");
            var hostToasts = new List<string>(); host.Toast += t => hostToasts.Add(t);
            var (ca, cb) = MemoryTransport.Pair(); host.AddPeer(cb);
            var client = LockstepSession.Join(ca, "Ann", (_, s) => Load(good1, s));
            var (ra2, rb2) = MemoryTransport.Pair(); host.AddPeer(rb2); // the raw attacker
            ra2.Send("HELLO|2|<b>Eve</b>\u202E|-");
            void Frame() { host.Update(0.05); client.Update(0.05); }
            bool Until(Func<bool> c, int max = 3000) { for (int i = 0; i < max && !c(); i++) { Frame(); if (i % 50 == 0) Thread.Sleep(1); } return c(); }
            Check(Until(() => client.Game != null && client.Players.Count >= 3), "legit client joined with the same mod set");
            var tags = hostGame.World.Countries.Where(c => c.Exists && hostGame.World.Divisions.Any(d => d.Owner == c)).Select(c => c.Tag).OrderBy(t => t, StringComparer.Ordinal).Take(3).ToList();
            host.Pick(tags[0]); client.Pick(tags[1]);
            // the attacker finishes the handshake like a client and takes the third country
            var inbox = new List<string>(); void Drain() { while (ra2.TryReceive(out var l)) inbox.Add(l); }
            Until(() => { Drain(); return inbox.Any(l => l.StartsWith("WELCOME")); });
            ra2.Send("READY"); ra2.Send("PICK|" + tags[2]);
            Until(() => host.Players.Any(p => p.Tag == tags[2]));
            var eve = host.Players.FirstOrDefault(p => p.Tag == tags[2]);
            Check(eve != null && eve.Name == "bEve/b" || eve != null && !eve.Name.Any(c => c is '\u202E') , "attacker's name was sanitized: '" + eve?.Name + "'");
            host.SetSpeed(3); host.SetPaused(false);
            var victim = hostGame.World.Divisions.First(d => d.Owner.Tag == tags[0]);
            string victimLoc = victim.Location.Key;
            var other = victim.Location.Neighbors.FirstOrDefault()?.To.Key ?? victimLoc;
            double goldA = hostGame.World.GetCountry(tags[1])!.Gold, goldE = hostGame.World.GetCountry(tags[2])!.Gold;
            ra2.Send($"CMD|0||move|{victim.Id},{other}");                                  // someone else's division
            ra2.Send($"CMD|0||declare_war|{tags[2]}");                                     // war on itself
            ra2.Send($"CMD|0||{GameCommand.Claim}|99");                                    // system command from a client
            ra2.Send($"CMD|0||mod_message|{Net.Esc(key)},100000");                         // out of range
            ra2.Send($"CMD|0||mod_message|{Net.Esc("nope/x")},1");                         // unregistered
            ra2.Send($"CMD|5|{tags[0]}|move|1,2");                                         // forging tick and country
            for (int i = 0; i < 60; i++) { Frame(); Drain(); }
            Check(victim.Location.Key == victimLoc || victim.Location.Key != other, "attacker cannot move another player's division");
            Check(hostGame.World.GetCountry(tags[2])!.Gold == goldE, "out of range and unknown mod messages changed nothing");
            Check(eve != null && !eve.Active, "attacker was removed after repeated violations");
            Check(hostGame.World.GetCountry(tags[2])!.IsPlayer == false, "attacker's country went back to the AI");
            // the legit client's mod message goes through the host and arrives everywhere
            client.Submit(GameCommand.ModMessage, key, "25");
            Until(() => hostGame.World.GetCountry(tags[1])!.Gold != goldA, 600);
            Frame(); Until(() => client.Tick >= host.Tick - 2, 600);
            Check(hostGame.World.GetCountry(tags[1])!.Gold == goldA - 25, "legit mod message applied by the host (gold " + goldA + " -> " + hostGame.World.GetCountry(tags[1])!.Gold + ")");
            Check(client.Game!.World.GetCountry(tags[1])!.Gold == hostGame.World.GetCountry(tags[1])!.Gold, "same result on the client");
            host.SetPaused(true); Until(() => client.Tick == host.Tick && client.Paused, 600);
            Check(client.Tick == host.Tick && StateChecksum.Compute(hostGame) == StateChecksum.Compute(client.Game!), "checksums equal after the mod message");
            host.Leave(); client.Leave();

            // a mod rule that never returns must not freeze the host
            Console.WriteLine("Mod validate deadline");
            string loopDir = CopyProject(project, Path.Combine(tmp, "loopmod"));
            Directory.CreateDirectory(Path.Combine(loopDir, "scripts"));
            File.WriteAllText(Path.Combine(loopDir, "scripts", "Loop.cs"), @"using Burger.Core; using Burger.Core.Modding;
namespace Loop { public class LoopMod : GameScript {
  public override void OnLoad() {
    Network.Register(""spin"", ModMessageSchema.Create().Int(""n"", 1, 10), m => { while (true) { } }, m => { });
    Network.Register(""fine"", ModMessageSchema.Create().Int(""n"", 1, 10), m => null, m => { m.Sender.Gold = 777777; });
  } } }");
            File.WriteAllText(Path.Combine(loopDir, "scripts", "mod.json"), "{\"permissions\":[\"network\"]}");
            var lg = Load(loopDir, 6);
            var lhost = LockstepSession.Host(lg, "sec-test", 6, "Host");
            var lkey = lg.ModMessageKeys.First(k => k.EndsWith("/spin")); var fkey = lg.ModMessageKeys.First(k => k.EndsWith("/fine"));
            var lc = lg.World.Countries.First(c => c.Exists && lg.World.Divisions.Any(d => d.Owner == c));
            lhost.Pick(lc.Tag);
            ModLimits.ValidateDeadlineMs = 100;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            lhost.Submit(GameCommand.ModMessage, lkey, "1");
            long first = sw.ElapsedMilliseconds; sw.Restart();
            lhost.Submit(GameCommand.ModMessage, lkey, "1");
            long second = sw.ElapsedMilliseconds;
            Check(first < 1500, $"a validate that loops forever is cut off after the deadline ({first} ms)");
            Check(second < 50, $"the rule is then switched off and later messages are refused at once ({second} ms)");
            double g0 = lc.Gold;
            lhost.Submit(GameCommand.ModMessage, fkey, "1");
            lhost.SetSpeed(5); lhost.SetPaused(false);
            for (int i = 0; i < 800 && lc.Gold < 700000; i++) lhost.Update(0.05);
            Check(lc.Gold > 700000, "another mod message of the same mod still works");
            lhost.Leave();
                    }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        Console.WriteLine(failures == 0 ? "All security checks passed" : $"{failures} security check(s) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static string Mutate(Random r, string s)
    {
        var sb = new StringBuilder(s);
        int n = r.Next(1, 5);
        for (int i = 0; i < n; i++)
        {
            switch (r.Next(7))
            {
                case 0 when sb.Length > 0: sb.Remove(r.Next(sb.Length), 1); break;
                case 1: sb.Insert(r.Next(sb.Length + 1), (char)r.Next(0, 0x2100)); break;
                case 2: sb.Insert(r.Next(sb.Length + 1), "|"); break;
                case 3: sb.Insert(r.Next(sb.Length + 1), new string((char)r.Next(32, 127), r.Next(1, 3000))); break;
                case 4: sb.Insert(r.Next(sb.Length + 1), "-99999999999999999999"); break;
                case 5 when sb.Length > 0: sb.Length = r.Next(sb.Length); break;
                default: sb.Append(',', r.Next(1, 40)); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Copies the game and backend folders of a project (not caches or logs) and returns the new game folder.</summary>
    private static string CopyProject(string project, string dest)
    {
        void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from))
            {
                string n = Path.GetFileName(d);
                if (n is "cache" or "logs" or "scripts") continue;
                Copy(d, Path.Combine(to, n));
            }
        }
        Copy(GameDir(project), Path.Combine(dest, "game"));
        if (Directory.Exists(Path.Combine(project, "backend"))) Copy(Path.Combine(project, "backend"), Path.Combine(dest, "backend"));
        return Path.Combine(dest, "game");
    }
}
