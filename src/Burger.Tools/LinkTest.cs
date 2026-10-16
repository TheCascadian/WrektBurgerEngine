using System.Collections.Concurrent;
using Burger.Core.Multiplayer;

namespace Burger.Tools;

/// <summary><c>burger link-test</c>: the Steam join check and Steam code, over an in-memory link pair. No Steam needed.</summary>
public static partial class Program
{
    private sealed class MemLink : IMessageLink
    {
        public MemLink? Peer; public readonly ConcurrentQueue<byte[]> In = new(); private volatile bool _open = true;
        public bool Connected => _open && (Peer?._open ?? false);
        public void Send(byte[] m) { if (Connected) Peer!.In.Enqueue(m); }
        public bool TryReceive(out byte[] m) { if (In.TryDequeue(out var x)) { m = x; return true; } m = Array.Empty<byte>(); return false; }
        public void Close() => _open = false;
    }

    private static (MemLink A, MemLink B) MemPair() { var a = new MemLink(); var b = new MemLink { Peer = a }; a.Peer = b; return (a, b); }

    private static int LinkTest()
    {
        int failures = 0;
        void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {what}"); if (!ok) failures++; }
        var secret = JoinCode.NewSecret();
        string code = SteamCode.Make(76561198000000001UL, secret);
        Check(SteamCode.TryParse(code, out ulong id, out var s2) && id == 76561198000000001UL && s2.SequenceEqual(secret), "steam code round trip");
        Check(!SteamCode.TryParse(code[..^1] + (code[^1] == 'A' ? 'B' : 'A'), out _, out var s3) || !s3.SequenceEqual(secret), "altered code differs");

        ITransport? Admit(byte[] joinerSecret, out Exception? err)
        {
            var (host, joiner) = MemPair();
            using var acc = new LinkAcceptor(secret);
            acc.Offer(host, "acct1");
            ITransport? got = null; Exception? e = null;
            var t = new Thread(() => { try { Thread.Sleep(50); LinkAcceptor.Join(joiner, joinerSecret, () => Thread.Sleep(5), 3000); } catch (Exception x) { e = x; } });
            t.Start();
            var end = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < end && got == null && acc.TryAccept(out var tr)) got = tr;
            while (DateTime.UtcNow < end && got == null) { if (acc.TryAccept(out var tr)) got = tr; Thread.Sleep(5); }
            t.Join(); err = e; return got;
        }
        Check(Admit(secret, out var e1) != null && e1 == null, "right secret is admitted");
        Check(Admit(JoinCode.NewSecret(), out var e2) == null && e2 != null, "wrong secret is refused");
        Console.WriteLine(failures == 0 ? "link-test passed" : $"link-test FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
