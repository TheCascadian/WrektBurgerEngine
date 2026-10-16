using System.Net;
using System.Net.Sockets;
using Burger.Core.Multiplayer;

namespace Burger.Tools;

/// <summary><c>burger relay-test</c>: a relay, a host behind it and joiners through it, in one process. No game files needed.</summary>
public static partial class Program
{
    private static int RelayTest()
    {
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {what}"); if (!ok) failures++; }
        bool Wait(Func<bool> f, int ms = 8000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (f()) return true; Thread.Sleep(20); } return false; }
        string? Read(ITransport t) { string? l = null; Wait(() => t.TryReceive(out l), 4000); return l; }

        using var relay = new RelayServer(0);
        using var listener = new TcpHostListener();
        using var link = new RelayHostLink("127.0.0.1", relay.Port, listener);
        Check(Wait(() => link.State == RelayHostLink.Phase.Ready), "host registers a room with the relay (no open port needed)");
        string code = JoinCode.MakeRelay(link.Room, listener.Secret);
        Check(JoinCode.TryParseAny(code, out string h, out int p, out long room, out var sec) && room == link.Room && h == "" && p == 0 && sec.SequenceEqual(listener.Secret), "relay code round trip, holding no address");
        Check(JoinCode.TryParseAny(JoinCode.Make(IPAddress.Parse("192.168.1.20"), 27960, listener.Secret), out string h2, out int p2, out long r2, out _) && r2 == 0 && h2 == "192.168.1.20" && p2 == 27960, "a direct code is still direct");

        var joiners = new List<(TcpTransport J, ITransport H)>();
        for (int i = 0; i < 3; i++)
        {
            var j = TcpTransport.ConnectRelay("127.0.0.1", relay.Port, room, sec);
            ITransport? hostSide = null;
            Wait(() => listener.TryAccept(out hostSide));
            Check(hostSide != null, $"joiner {i + 1} reaches the host through the relay, encrypted end to end");
            if (hostSide != null) joiners.Add((j, hostSide));
        }
        foreach (var (j, hs) in joiners)
        {
            j.Send("HELLO there"); hs.Send("WELCOME back");
            Check(Read(hs) == "HELLO there" && Read(j) == "WELCOME back", "lines pass both ways");
        }

        bool wrong = false;
        try { TcpTransport.ConnectRelay("127.0.0.1", relay.Port, room, JoinCode.NewSecret()); } catch { wrong = true; }
        Check(wrong, "wrong secret refused (the relay never held it)");
        bool nobody = false;
        try { TcpTransport.ConnectRelay("127.0.0.1", relay.Port, room ^ 1, sec, 3000); } catch { nobody = true; }
        Check(nobody, "unknown room refused");

        // flood: more connections from one address than allowed are dropped by the relay, the others still work
        var junk = new List<TcpClient>();
        for (int i = 0; i < RelayServer.MaxPerAddress + 10; i++) { try { var c = new TcpClient(); c.Connect("127.0.0.1", relay.Port); junk.Add(c); } catch { } }
        Thread.Sleep(300);
        int dropped = junk.Count(c => { try { c.Client.Poll(100000, SelectMode.SelectRead); return c.Client.Available == 0 && c.Client.Poll(0, SelectMode.SelectRead); } catch { return true; } });
        Check(dropped >= 5, $"flood from one address limited ({dropped} of {junk.Count} dropped)");
        foreach (var c in junk) c.Close();
        Thread.Sleep(6000);
        Check(relay.RoomCount == 1, "idle junk connections time out, room still there");

        link.Dispose();
        var gone = joiners[0].J;
        Console.WriteLine(failures == 0 ? "relay-test passed" : $"relay-test FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
