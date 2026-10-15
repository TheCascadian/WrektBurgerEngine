using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Burger.Core;
using Burger.Core.Multiplayer;
using Burger.Core.Script;

namespace Burger.Tools;

public static partial class Program
{
    /// <summary>One-connection TCP relay between a client and <paramref name="target"/>. <paramref name="filter"/> sees every chunk (0 = client to host, 1 = host to client) and returns what to forward (null drops it).</summary>
    private static (int Port, TcpListener Listener) StartRelay(int target, Func<int, byte[], byte[]?> filter)
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        new Thread(() =>
        {
            try
            {
                using var a = l.AcceptTcpClient(); using var b = new TcpClient("127.0.0.1", target);
                void Pump(TcpClient from, TcpClient to, int dir)
                {
                    try
                    {
                        var buf = new byte[16384]; var s = from.GetStream(); int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) { var o = filter(dir, buf[..n]); if (o != null) to.GetStream().Write(o); }
                    }
                    catch { }
                    try { to.Close(); } catch { }
                }
                var t = new Thread(() => Pump(b, a, 1)) { IsBackground = true }; t.Start();
                Pump(a, b, 0);
            }
            catch { }
        }) { IsBackground = true }.Start();
        return (((IPEndPoint)l.LocalEndpoint).Port, l);
    }

    private static bool Accepted(TcpHostListener l, int waitMs = 600)
    {
        for (int i = 0; i < waitMs / 20; i++) { if (l.TryAccept(out var t)) { t.Close(); return true; } Thread.Sleep(20); }
        return false;
    }

    private static SslStream RawTls(int port, SslProtocols protocols)
    {
        var c = new TcpClient("127.0.0.1", port);
        var tls = new SslStream(c.GetStream(), false, (_, _, _, _) => true);
        tls.AuthenticateAsClient(new SslClientAuthenticationOptions { TargetHost = SecureChannelHost, EnabledSslProtocols = protocols });
        return tls;
    }
    private const string SecureChannelHost = "burger-host";

    private static string RawLine(Stream s)
    {
        var sb = new StringBuilder(); int b;
        while ((b = s.ReadByte()) >= 0 && b != '\n') sb.Append((char)b);
        return sb.ToString();
    }

    /// <summary>Adversarial tests of the channel: replay, reflection, downgrade, unknown key share, tampering, truncation, flooding. See docs/channel-spec.md.</summary>
    private static void ChannelAttacks(Action<bool, string> Check)
    {
        Console.WriteLine("Channel attacks (docs/channel-spec.md)");

        // tampering: one bit flipped in an encrypted record after the handshake
        using (var l = new TcpHostListener())
        {
            bool armed = false;
            var (port, rl) = StartRelay(l.Port, (dir, b) => { if (dir == 0 && armed) { b[^1] ^= 1; } return b; });
            var viaRelay = TcpTransport.Connect("127.0.0.1", port, l.Secret);
            ITransport? srv = null; for (int i = 0; i < 100 && !l.TryAccept(out srv); i++) Thread.Sleep(20);
            Check(srv != null, "relay without tampering passes the handshake");
            if (srv != null)
            {
                viaRelay.Send("clean"); string? got = null; for (int i = 0; i < 50 && !srv.TryReceive(out got!); i++) Thread.Sleep(20);
                Check(got == "clean", "untampered line through the relay arrives");
                armed = true; viaRelay.Send("tampered"); Thread.Sleep(400);
                Check(!srv.TryReceive(out _) && !srv.Connected, "a record altered on the way is rejected and the link ends");
                srv.Close();
            }
            viaRelay.Close(); rl.Stop();
        }

        // replay: everything a legitimate client sent is played again to a new connection
        using (var l = new TcpHostListener())
        {
            var recorded = new List<byte[]>();
            var (port, rl) = StartRelay(l.Port, (dir, b) => { if (dir == 0) lock (recorded) recorded.Add(b.ToArray()); return b; });
            var c = TcpTransport.Connect("127.0.0.1", port, l.Secret);
            Check(Accepted(l), "recording: legit session accepted");
            c.Send("hello"); Thread.Sleep(100); c.Close(); rl.Stop();
            using var raw = new TcpClient("127.0.0.1", l.Port);
            try { var s = raw.GetStream(); lock (recorded) foreach (var r in recorded) s.Write(r); Thread.Sleep(500); } catch { }
            Check(!Accepted(l), "replayed handshake and records are not accepted (fresh randoms and keys per connection)");
        }

        // reflection: the client sends the host's own challenge back as its answer
        using (var l = new TcpHostListener())
        {
            var tls = RawTls(l.Port, SslProtocols.Tls13);
            string a1 = RawLine(tls); // A1|ns
            string ns = a1.Split('|')[1];
            var reply = Encoding.ASCII.GetBytes($"A2|{ns}|{ns}{ns}\n"); tls.Write(reply); tls.Flush();
            Check(!Accepted(l), "reflected challenge is not accepted as a proof");
            var tls2 = RawTls(l.Port, SslProtocols.Tls13);
            RawLine(tls2); // wrong order: answer with a host-style message
            tls2.Write(Encoding.ASCII.GetBytes("A3|" + Convert.ToBase64String(new byte[32]) + "\n")); tls2.Flush();
            Check(!Accepted(l), "a host-role message sent by a client is not accepted");
        }

        // reflection of the host: a fake host that echoes what the client sends
        {
            var fake = new TcpListener(IPAddress.Loopback, 0); fake.Start();
            var cert = SelfSigned();
            new Thread(() =>
            {
                try
                {
                    using var c = fake.AcceptTcpClient(); var tls = new SslStream(c.GetStream());
                    tls.AuthenticateAsServer(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = SslProtocols.Tls13 });
                    tls.Write(Encoding.ASCII.GetBytes("A1|" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) + "\n")); tls.Flush();
                    string a2 = RawLine(tls);                     // client's A2|nc|proof
                    var f = a2.Split('|');
                    tls.Write(Encoding.ASCII.GetBytes("A3|" + f[2] + "\n")); tls.Flush(); // echo the client's own proof as the host's
                    Thread.Sleep(500);
                }
                catch { }
            }) { IsBackground = true }.Start();
            bool failed = false;
            try { TcpTransport.Connect("127.0.0.1", ((IPEndPoint)fake.LocalEndpoint).Port, JoinCode.NewSecret()); } catch { failed = true; }
            Check(failed, "client refuses a host that reflects the client's own proof");
            fake.Stop();
        }

        // downgrade: older TLS and no-TLS clients
        using (var l = new TcpHostListener())
        {
            bool refused12 = false;
            try { RawTls(l.Port, SslProtocols.Tls12); } catch { refused12 = true; }
            Check(refused12 && !Accepted(l), "TLS 1.2 is refused (TLS 1.3 only)");
        }
        {
            // a host that only speaks TLS 1.2 is refused by the client
            var fake = new TcpListener(IPAddress.Loopback, 0); fake.Start();
            var cert = SelfSigned();
            new Thread(() =>
            {
                try { using var c = fake.AcceptTcpClient(); var tls = new SslStream(c.GetStream()); tls.AuthenticateAsServer(new SslServerAuthenticationOptions { ServerCertificate = cert, EnabledSslProtocols = SslProtocols.Tls12 }); Thread.Sleep(300); }
                catch { }
            }) { IsBackground = true }.Start();
            bool failed = false;
            try { TcpTransport.Connect("127.0.0.1", ((IPEndPoint)fake.LocalEndpoint).Port, JoinCode.NewSecret()); } catch { failed = true; }
            Check(failed, "client refuses a host that only offers TLS 1.2");
            fake.Stop();
        }

        // unknown key share / man in the middle with its own certificate: TLS ends at the attacker on both sides, proofs are bound to the host certificate
        using (var l = new TcpHostListener())
        {
            var mitm = new TcpListener(IPAddress.Loopback, 0); mitm.Start();
            var cert = SelfSigned();
            bool forwarded = false;
            new Thread(() =>
            {
                try
                {
                    using var cc = mitm.AcceptTcpClient(); var down = new SslStream(cc.GetStream());
                    down.AuthenticateAsServer(new SslServerAuthenticationOptions { ServerCertificate = cc == null ? null : cert, EnabledSslProtocols = SslProtocols.Tls13 });
                    var up = RawTls(l.Port, SslProtocols.Tls13);
                    string a1 = RawLine(up); down.Write(Encoding.ASCII.GetBytes(a1 + "\n")); down.Flush();
                    string a2 = RawLine(down); up.Write(Encoding.ASCII.GetBytes(a2 + "\n")); up.Flush();
                    forwarded = true;
                    Thread.Sleep(500);
                }
                catch { }
            }) { IsBackground = true }.Start();
            bool failed = false;
            try { TcpTransport.Connect("127.0.0.1", ((IPEndPoint)mitm.LocalEndpoint).Port, l.Secret); } catch { failed = true; }
            Thread.Sleep(300);
            Check(failed && forwarded && !Accepted(l), "man in the middle with its own certificate fails even when it knows nothing but forwards every message");
            mitm.Stop();
        }

        // an attacker who does not know the secret cannot be a host either
        using (var l = new TcpHostListener())
        {
            bool failed = false;
            try { TcpTransport.Connect("127.0.0.1", l.Port, JoinCode.NewSecret()); } catch { failed = true; }
            Check(failed && !Accepted(l), "wrong session secret is refused by both sides");
        }

        // truncation: connection ends in the middle of the handshake
        using (var l = new TcpHostListener())
        {
            using (var raw = new TcpClient("127.0.0.1", l.Port)) { raw.GetStream().Write(new byte[] { 0x16, 0x03, 0x01, 0x02, 0x00, 0x01, 0x00 }); }
            var tls = RawTls(l.Port, SslProtocols.Tls13); RawLine(tls); tls.Dispose(); // stops before sending its answer
            var tls2 = RawTls(l.Port, SslProtocols.Tls13); RawLine(tls2); tls2.Write(Encoding.ASCII.GetBytes("A2|abc")); tls2.Flush(); tls2.Dispose(); // half a line
            Check(!Accepted(l), "handshakes cut short are not accepted");
            var good = TcpTransport.Connect("127.0.0.1", l.Port, l.Secret);
            Check(Accepted(l), "the listener still accepts a legit client afterwards");
            good.Close();
        }

        // flooding: many silent connections from one address
        using (var l = new TcpHostListener())
        {
            var socks = new List<TcpClient>();
            for (int i = 0; i < 60; i++) { try { socks.Add(new TcpClient("127.0.0.1", l.Port)); } catch { } }
            Thread.Sleep(500);
            Check(l.Refused >= 40, $"flood of 60 silent connections: {l.Refused} refused before any handshake work (limit {TcpHostListener.MaxPerAddress} per address)");
            foreach (var s in socks) s.Close();
        }
        using (var l = new TcpHostListener())
        {
            // repeated failures from one address earn a temporary ban
            for (int i = 0; i < TcpHostListener.MaxFailures; i++) { try { TcpTransport.Connect("127.0.0.1", l.Port, JoinCode.NewSecret()); } catch { } }
            Thread.Sleep(300);
            bool failed = false;
            try { TcpTransport.Connect("127.0.0.1", l.Port, l.Secret); } catch { failed = true; }
            Check(failed && !Accepted(l, 200), $"after {TcpHostListener.MaxFailures} failed handshakes the address is refused for a minute");
        }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=attacker", key, HashAlgorithmName.SHA256);
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(c.Export(X509ContentType.Pfx));
    }

    /// <summary>Hostile save files: the parser and the checks must refuse each one and the client must leave without touching its game.</summary>
    private static void SaveAttacks(string project, string tmp, Action<bool, string> Check)
    {
        Console.WriteLine("Hostile saves (parser limits, schema, host-sent save)");
        Game Fresh(int seed) { var g = Game.Load(GameDir(project), true, seed); g.StartNewGame(null); return g; }
        var game = Fresh(11);
        string path = Path.Combine(tmp, "good.sav"); Burger.Core.Save.SaveGame.Save(game, path);
        string good = File.ReadAllText(path);
        Check(Try(() => SaveGuard.ReadText(game, good)) == null, "a genuine save passes the strict reader");

        string? Try(Action a) { try { a(); return null; } catch (InvalidDataException e) { return e.Message; } catch (Exception e) { return "CRASH " + e.GetType().Name; } }
        void Refused(string name, string text)
        {
            string? r = Try(() => SaveGuard.ReadText(game, text));
            Check(r != null && !r.StartsWith("CRASH"), $"{name} refused ({r})");
        }
        Refused("unknown top-level block", good + "\nbackdoor = { x = 1 }\n");
        Refused("duplicate block", good + "\nmeta = { version = 1 }\n");
        Refused("value at the top level", good + "\nloose = 1\n");
        Refused("no meta block", "countries = { }\n");
        Refused("wrong version", good.Replace("version = \"1\"", "version = \"9\"").Replace("version = 1", "version = 9"));
        Refused("deep nesting (100000 levels)", "meta = { version = 1 }\n" + new string('{', 100000));
        Refused("deep nesting (500 levels)", "x_deep = " + string.Concat(Enumerable.Repeat("{ a = ", 500)) + "1" + new string('}', 500));
        Refused("unbalanced braces", good + "\n}");
        Refused("unterminated block", "meta = { version = 1\n");
        Refused("control characters", good.Replace("meta", "me\u0001ta"));
        Refused("overlong string", "meta = { version = 1 name = \"" + new string('a', 100000) + "\" }");
        Refused("too many entries", "meta = { version = 1 }\ncountries = {\n" + string.Concat(Enumerable.Range(0, 1_500_000).Select(i => $"a{i}={{}}\n")) + "}\n");
        Refused("non-tag country key", good.Replace("countries = {", "countries = {\n  \"../../etc\" = { }\n"));
        Refused("NaN gold", good.Replace("gold = ", "gold = NaN # "));
        Refused("invalid UTF-8", "");
        string? bad = Try(() => SaveGuard.Read(game, new byte[] { 0xFF, 0xFE, 0xC0, 0x80, 0x6d }));
        Check(bad != null && !bad.StartsWith("CRASH"), $"invalid UTF-8 refused ({bad})");
        string? big = Try(() => SaveGuard.Read(game, new byte[1]) );
        Check(big != null, "one byte of garbage refused");

        // a hostile host sends bad saves to a real client
        var hostGame = Fresh(11);
        var host = LockstepSession.Host(hostGame, "sec-test", 11, "Host");
        var (pa, pb) = MemoryTransport.Pair(); host.AddPeer(pb);
        pa.Send("HELLO|2|Probe|-");
        string? welcome = null;
        for (int i = 0; i < 200 && welcome == null; i++) { host.Update(0.05); while (pa.TryReceive(out var l)) if (l.StartsWith("WELCOME")) welcome = l; }
        Check(welcome != null, "captured the host's WELCOME for the fake-host test");
        if (welcome == null) return;
        string firstTag = hostGame.World.Countries.First(c => c.Exists).Tag;

        byte[] Gz(Action<Stream> write) { using var ms = new MemoryStream(); using (var z = new GZipStream(ms, CompressionLevel.Fastest, true)) write(z); return ms.ToArray(); }
        byte[] GzText(string t) => Gz(s => s.Write(Encoding.UTF8.GetBytes(t)));
        void HostileHost(string name, byte[] gz)
        {
            var (ca, fb) = MemoryTransport.Pair();
            var client = LockstepSession.Join(ca, "Victim", (_, s) => Fresh(s));
            var toasts = new List<string>(); client.Toast += t => toasts.Add(t);
            client.Update(0.05);
            while (fb.TryReceive(out _)) { }
            fb.Send(welcome);
            for (int i = 0; i < 20; i++) { client.Update(0.05); while (fb.TryReceive(out _)) { } }
            fb.Send($"SYNC|0|{firstTag}|{gz.Length}");
            for (int off = 0, seq = 0; off < gz.Length; off += Wire.SyncChunk, seq++)
                fb.Send($"SYNCDATA|{seq}|{Convert.ToBase64String(gz, off, Math.Min(Wire.SyncChunk, gz.Length - off))}");
            fb.Send($"SYNCEND|{(gz.Length + Wire.SyncChunk - 1) / Wire.SyncChunk}");
            for (int i = 0; i < 400 && !client.Disconnected; i++) client.Update(0.05);
            Check(client.Disconnected && client.Tick == 0 && toasts.Any(t => t.Contains("invalid save")), $"client leaves on a hostile save from the host: {name}");
        }
        HostileHost("not a save", GzText("hello world"));
        HostileHost("unknown block", GzText(good + "\nevil = { }\n"));
        HostileHost("deep nesting", GzText("meta = { version = 1 }\n" + new string('{', 200000)));
        HostileHost("not gzip", Encoding.ASCII.GetBytes("this is not gzip data at all"));
        HostileHost("gzip bomb (300 MB expanded)", Gz(s => { var z = new byte[1 << 20]; for (int i = 0; i < 300; i++) s.Write(z); }));
    }

    /// <summary>A client that keeps reporting a wrong checksum must not make the host send full saves again and again.</summary>
    private static void SumFlood(string project, Action<bool, string> Check)
    {
        Console.WriteLine("False checksum flood");
        var g = Game.Load(GameDir(project), true, 12); g.StartNewGame(null);
        var host = LockstepSession.Host(g, "sec-test", 12, "Host");
        var (a, b) = MemoryTransport.Pair(); host.AddPeer(b);
        a.Send("HELLO|2|Liar|-");
        var inbox = new List<string>(); void Drain() { while (a.TryReceive(out var l)) inbox.Add(l); }
        for (int i = 0; i < 200 && !inbox.Any(l => l.StartsWith("WELCOME")); i++) { host.Update(0.05); Drain(); }
        a.Send("READY");
        for (int i = 0; i < 200; i++) { host.Update(0.05); Drain(); }
        host.SetSpeed(5); host.SetPaused(false);
        for (int i = 0; i < 4000 && host.Tick < 200; i++) { host.Update(0.05); Drain(); if (i % 50 == 0) a.Send("ACK|" + host.Tick); }
        int syncs0 = inbox.Count(l => l.StartsWith("SYNC|"));
        var liar = host.Players.First(p => p.Name == "Liar");
        for (int rep = 0; rep < 10; rep++) for (long t = 0; t <= host.Tick; t++) a.Send($"SUM|{t}|{new string('0', 16)}");
        for (int i = 0; i < 400; i++) { host.Update(0.05); Drain(); }
        int syncs = inbox.Count(l => l.StartsWith("SYNC|")) - syncs0;
        Check(syncs <= 1, $"{10 * (host.Tick + 1)} false checksums caused {syncs} full save(s) (at most one)");
        Check(host.Resyncs <= 1, "host counted at most one resync for a liar");
        Check(!liar.Active, "the peer reporting false checksums was removed");
        host.Leave();
    }
}
