using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Burger.Core.Multiplayer;

/// <summary>A reliable, ordered, already encrypted message pipe to one peer (a platform connection such as Steam Networking). One message is one line.</summary>
public interface IMessageLink
{
    bool Connected { get; }
    void Send(byte[] message);
    bool TryReceive(out byte[] message);
    void Close();
}

/// <summary>Where accepted peers come from besides the TCP listener.</summary>
public interface IAcceptSource : IDisposable
{
    bool TryAccept(out ITransport t);
}

/// <summary>A line transport over a message link.</summary>
public sealed class MessageTransport : ITransport
{
    private readonly IMessageLink _link;
    public MessageTransport(IMessageLink link) { _link = link; }
    public bool Connected => _link.Connected;
    public void Send(string line) { if (_link.Connected) _link.Send(Encoding.UTF8.GetBytes(line)); }
    public bool TryReceive(out string line)
    {
        if (_link.TryReceive(out var m) && m.Length <= Wire.MaxRawLine) { line = Encoding.UTF8.GetString(m); return true; }
        line = ""; return false;
    }
    public void Close() => _link.Close();
}

/// <summary>
/// Admits peers that arrive over a platform link. The platform has already encrypted and authenticated the connection (and hides both players' network
/// addresses); this adds the session secret from the join code, so that only people who were given the code get in: the host sends a random challenge,
/// the joiner answers with an HMAC keyed by the secret. Limits: 16 pending, 5 s to answer, 5 failures per minute per account.
/// </summary>
public sealed class LinkAcceptor : IAcceptSource
{
    public const int MaxPending = 16, AuthMs = 5000, MaxFailures = 5;
    private sealed record Pending(IMessageLink Link, string Key, string Nonce, DateTime Since);

    private readonly List<Pending> _pending = new();
    private readonly Dictionary<string, List<DateTime>> _failures = new();
    private readonly ConcurrentQueue<(IMessageLink Link, string Key)> _offers = new();
    private readonly ConcurrentQueue<ITransport> _ready = new();
    private byte[] _secret;
    public int Refused { get; private set; }

    public LinkAcceptor(byte[] secret) { _secret = secret; }

    /// <summary>A new connection (any thread). <paramref name="key"/> identifies the account, for failure counting.</summary>
    public void Offer(IMessageLink link, string key) => _offers.Enqueue((link, key));

    public static string Proof(byte[] secret, string nonce) =>
        Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes("burger-link-v1|" + nonce)));

    public bool TryAccept(out ITransport t)
    {
        while (_offers.TryDequeue(out var o))
        {
            _failures.TryGetValue(o.Key, out var f);
            f?.RemoveAll(x => (DateTime.UtcNow - x).TotalSeconds > 60);
            if (_pending.Count >= MaxPending || (f?.Count ?? 0) >= MaxFailures) { Refused++; o.Link.Close(); continue; }
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            _pending.Add(new Pending(o.Link, o.Key, nonce, DateTime.UtcNow));
            o.Link.Send(Encoding.ASCII.GetBytes("CH " + nonce));
        }
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            bool done = false, ok = false;
            while (!done && p.Link.TryReceive(out var m))
            {
                string s = m.Length < 100 ? Encoding.ASCII.GetString(m) : "";
                ok = s == "PR " + Proof(_secret, p.Nonce);
                done = true;
            }
            if (!done && p.Link.Connected && (DateTime.UtcNow - p.Since).TotalMilliseconds < AuthMs) continue;
            _pending.RemoveAt(i);
            if (ok) { p.Link.Send(Encoding.ASCII.GetBytes("OK")); _ready.Enqueue(new MessageTransport(p.Link)); }
            else
            {
                Refused++;
                if (!_failures.TryGetValue(p.Key, out var f)) _failures[p.Key] = f = new();
                f.Add(DateTime.UtcNow);
                p.Link.Close();
            }
        }
        return _ready.TryDequeue(out t!);
    }

    public void Dispose() { foreach (var p in _pending) p.Link.Close(); _pending.Clear(); while (_ready.TryDequeue(out var t)) t.Close(); }

    /// <summary>The joiner's side: answers the host's challenge. <paramref name="pump"/> is called while waiting (platform callbacks). Throws when refused or too slow.</summary>
    public static ITransport Join(IMessageLink link, byte[] secret, Action pump, int timeoutMs = 10000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        bool answered = false;
        while (DateTime.UtcNow < end)
        {
            pump();
            if (!link.Connected) break;
            while (link.TryReceive(out var m))
            {
                string s = m.Length < 100 ? Encoding.ASCII.GetString(m) : "";
                if (!answered && s.StartsWith("CH ")) { link.Send(Encoding.ASCII.GetBytes("PR " + Proof(secret, s[3..]))); answered = true; }
                else if (answered && s == "OK") return new MessageTransport(link);
                else { link.Close(); throw new InvalidDataException("the host refused the code"); }
            }
            Thread.Sleep(5);
        }
        link.Close();
        throw new InvalidDataException("the host did not answer");
    }
}

/// <summary>Steam join codes: the host's 64-bit account id and the 77-bit session secret, 29 base-32 characters (the other kind has 25). No network address in it.</summary>
public static class SteamCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Chars = 29;

    public static string Make(ulong steamId, byte[] secret)
    {
        BigInteger s = 0;
        foreach (byte b in secret) s = (s << 8) | b;
        s &= (BigInteger.One << 77) - 1;
        BigInteger v = ((BigInteger)steamId << 77) | s;
        var sb = new StringBuilder();
        for (int i = Chars - 1; i >= 0; i--) sb.Append(Alphabet[(int)((v >> (i * 5)) & 31)]);
        string raw = sb.ToString();
        return string.Join("-", Enumerable.Range(0, (Chars + 4) / 5).Select(k => raw.Substring(k * 5, Math.Min(5, Chars - k * 5))));
    }

    public static bool TryParse(string code, out ulong steamId, out byte[] secret)
    {
        steamId = 0; secret = Array.Empty<byte>();
        if (code == null || code.Length > 64) return false;
        BigInteger v = 0; int n = 0;
        foreach (char ch in code.ToUpperInvariant())
        {
            if (ch == '-' || ch == ' ') continue;
            char c = ch switch { 'O' => '0', 'I' or 'L' => '1', 'U' => 'V', _ => ch };
            int i = Alphabet.IndexOf(c);
            if (i < 0) return false;
            v = (v << 5) | (uint)i; n++;
        }
        if (n != Chars) return false;
        BigInteger mask = (BigInteger.One << 77) - 1;
        var id = v >> 77;
        if (id > ulong.MaxValue || id.IsZero) return false;
        steamId = (ulong)id;
        var s = v & mask;
        var o = new byte[16];
        for (int i = 15; i >= 0; i--) { o[i] = (byte)(s & 255); s >>= 8; }
        secret = o;
        return true;
    }

    /// <summary>True when the text has the length of a Steam code (29 characters), so the menu can tell the two kinds apart.</summary>
    public static bool Looks(string code) => code != null && code.Count(ch => ch != '-' && ch != ' ') == Chars;
}
