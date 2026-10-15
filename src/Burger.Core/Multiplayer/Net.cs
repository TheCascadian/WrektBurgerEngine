using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Burger.Core.Multiplayer;

/// <summary>Helpers shared by the wire protocol: field escaping and the scripts hash.</summary>
public static class Net
{
    public static string Esc(string s) => Uri.EscapeDataString(s);
    public static string Unesc(string s) => Uri.UnescapeDataString(s);

    /// <summary>SHA-256 over the C# scripts of a game folder (relative path and bytes, in path order). Players must have the same one to play together.</summary>
    public static string ScriptsHash(string gameRoot)
    {
        using var sha = SHA256.Create();
        void Fold(string dir, string tag)
        {
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => (Rel: tag + Path.GetRelativePath(dir, f).Replace('\\', '/'), Full: f))
                .OrderBy(f => f.Rel, StringComparer.Ordinal);
            foreach (var (rel, full) in files)
            {
                var name = Encoding.UTF8.GetBytes(rel + "\n");
                sha.TransformBlock(name, 0, name.Length, null, 0);
                var data = File.ReadAllBytes(full);
                sha.TransformBlock(data, 0, data.Length, null, 0);
            }
        }
        Fold(Path.Combine(gameRoot, "scripts"), "scripts/");
        Fold(Path.Combine(gameRoot, "..", "backend"), "backend/"); // the locked project code decides the simulation too
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}

/// <summary>An ordered, reliable stream of text lines to one peer.</summary>
public interface ITransport
{
    bool Connected { get; }
    void Send(string line);
    bool TryReceive(out string line);
    void Close();
}

/// <summary>Two connected in-memory ends (tests).</summary>
public sealed class MemoryTransport : ITransport
{
    private readonly ConcurrentQueue<string> _in = new();
    private MemoryTransport _peer = null!;
    public bool Connected { get; private set; } = true;

    public static (MemoryTransport A, MemoryTransport B) Pair()
    {
        var a = new MemoryTransport(); var b = new MemoryTransport();
        a._peer = b; b._peer = a;
        return (a, b);
    }
    public void Send(string line) { if (Connected && _peer.Connected) _peer._in.Enqueue(line); }
    public bool TryReceive(out string line) => _in.TryDequeue(out line!);
    public void Close() { Connected = false; _peer.Connected = false; }
}

/// <summary>
/// The encrypted channel under every TCP link. Handshake: both sides send an ephemeral ECDH P-256 key and a nonce, then prove they know the
/// session secret (from the join code) with an HMAC over the whole transcript. Keys come from HKDF over the shared point, the secret and the
/// transcript, one per direction. Each line is then AES-GCM sealed with a strict counter as nonce, so a replayed, reordered, dropped or
/// altered line fails and ends the link. A peer without the secret never gets past the handshake.
/// </summary>
internal static class SecureChannel
{
    public const int HandshakeMs = 5000;

    public static byte[] SecretKey(byte[] joinSecret) => SHA256.HashData(Concat(Encoding.ASCII.GetBytes("burger-mp-psk-v2"), joinSecret));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static string B(byte[] b) => Convert.ToBase64String(b);

    private static byte[] Unb(string s, int min, int max)
    {
        if (s.Length > max * 2) throw new InvalidDataException("handshake field too long");
        var b = Convert.FromBase64String(s);
        if (b.Length < min || b.Length > max) throw new InvalidDataException("handshake field size");
        return b;
    }

    /// <summary>Runs the handshake on <paramref name="stream"/>; returns the two keys, or throws when the peer fails it.</summary>
    public static (byte[] Send, byte[] Recv) Handshake(Stream stream, LineReader reader, bool server, byte[] psk)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] myPub = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        byte[] myNonce = RandomNumberGenerator.GetBytes(16);
        byte[] theirPub, theirNonce;
        byte[] cPub, sPub, cNonce, sNonce;
        if (!server)
        {
            WriteLine(stream, $"K1|{B(myPub)}|{B(myNonce)}");
            var f = Read(reader, "K2", 4);
            theirPub = Unb(f[1], 60, 200); theirNonce = Unb(f[2], 16, 16);
            cPub = myPub; cNonce = myNonce; sPub = theirPub; sNonce = theirNonce;
            var keys = Derive(ecdh, theirPub, psk, cPub, sPub, cNonce, sNonce, out var transcript);
            if (!CryptographicOperations.FixedTimeEquals(Unb(f[3], 32, 32), Proof(psk, "server", transcript))) throw new InvalidDataException("the host does not know the session secret");
            WriteLine(stream, "K3|" + B(Proof(psk, "client", transcript)));
            return (keys.C2S, keys.S2C);
        }
        else
        {
            var f = Read(reader, "K1", 3);
            theirPub = Unb(f[1], 60, 200); theirNonce = Unb(f[2], 16, 16);
            cPub = theirPub; cNonce = theirNonce; sPub = myPub; sNonce = myNonce;
            var keys = Derive(ecdh, theirPub, psk, cPub, sPub, cNonce, sNonce, out var transcript);
            WriteLine(stream, $"K2|{B(myPub)}|{B(myNonce)}|{B(Proof(psk, "server", transcript))}");
            var g = Read(reader, "K3", 2);
            if (!CryptographicOperations.FixedTimeEquals(Unb(g[1], 32, 32), Proof(psk, "client", transcript))) throw new InvalidDataException("wrong session secret");
            return (keys.S2C, keys.C2S);
        }
    }

    private static string[] Read(LineReader r, string tag, int fields)
    {
        string? l = r.ReadLine();
        if (l == null) throw new IOException("closed during handshake");
        var f = l.Split('|');
        if (f.Length != fields || f[0] != tag || l.Length > 1024) throw new InvalidDataException("bad handshake");
        return f;
    }

    private static (byte[] C2S, byte[] S2C) Derive(ECDiffieHellman mine, byte[] theirSpki, byte[] psk, byte[] cPub, byte[] sPub, byte[] cNonce, byte[] sNonce, out byte[] transcript)
    {
        using var theirs = ECDiffieHellman.Create();
        theirs.ImportSubjectPublicKeyInfo(theirSpki, out _);
        byte[] shared = mine.DeriveRawSecretAgreement(theirs.PublicKey);
        transcript = SHA256.HashData(Concat(Encoding.ASCII.GetBytes("burger-mp-v2"), cPub, sPub, cNonce, sNonce));
        byte[] okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 64, psk, transcript);
        return (okm[..32], okm[32..]);
    }

    private static byte[] Proof(byte[] psk, string who, byte[] transcript) => HMACSHA256.HashData(psk, Concat(Encoding.ASCII.GetBytes(who), transcript));

    public static void WriteLine(Stream s, string line)
    {
        var b = Encoding.UTF8.GetBytes(line + "\n");
        s.Write(b, 0, b.Length); s.Flush();
    }

    public static byte[] Nonce(ulong counter)
    {
        var n = new byte[12];
        for (int i = 0; i < 8; i++) n[11 - i] = (byte)(counter >> (8 * i));
        return n;
    }
}

/// <summary>Reads newline-terminated lines with a hard size limit: a peer that never ends a line cannot make us buffer without bound.</summary>
internal sealed class LineReader
{
    private readonly Stream _s;
    private readonly byte[] _buf = new byte[8192];
    private int _pos, _len;
    private readonly MemoryStream _line = new();
    public LineReader(Stream s) { _s = s; }

    /// <summary>The next line, or null at end of stream. Throws when a line exceeds the raw limit.</summary>
    public string? ReadLine()
    {
        while (true)
        {
            while (_pos < _len)
            {
                byte c = _buf[_pos++];
                if (c == (byte)'\n')
                {
                    var t = Encoding.UTF8.GetString(_line.GetBuffer(), 0, (int)_line.Length);
                    _line.SetLength(0);
                    return t;
                }
                if (_line.Length >= Wire.MaxRawLine) throw new InvalidDataException("line too long");
                _line.WriteByte(c);
            }
            _len = _s.Read(_buf, 0, _buf.Length); _pos = 0;
            if (_len <= 0) return null;
        }
    }
}

/// <summary>An encrypted TCP connection carrying one line per message.</summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly AesGcm _sendAes, _recvAes;
    private ulong _sendCtr, _recvCtr;
    private readonly ConcurrentQueue<string> _in = new();
    private volatile bool _open = true;
    private readonly object _sendLock = new();

    public bool Connected => _open;

    private TcpTransport(TcpClient client, Stream stream, LineReader reader, byte[] sendKey, byte[] recvKey)
    {
        _client = client; _stream = stream;
        _sendAes = new AesGcm(sendKey, 16); _recvAes = new AesGcm(recvKey, 16);
        new Thread(() =>
        {
            try
            {
                string? l;
                while (_open && (l = reader.ReadLine()) != null)
                {
                    var raw = Convert.FromBase64String(l);
                    if (raw.Length < 16) throw new InvalidDataException("short frame");
                    var plain = new byte[raw.Length - 16];
                    _recvAes.Decrypt(SecureChannel.Nonce(_recvCtr++), raw.AsSpan(0, plain.Length), raw.AsSpan(plain.Length), plain);
                    _in.Enqueue(Encoding.UTF8.GetString(plain));
                }
            }
            catch { }
            _open = false;
            try { _client.Close(); } catch { }
        }) { IsBackground = true, Name = "mp-read" }.Start();
    }

    /// <summary>Handshake on an accepted or connected socket. Throws (and closes the socket) when the peer fails it in time.</summary>
    internal static TcpTransport Secure(TcpClient client, bool server, byte[] joinSecret)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = client.SendTimeout = SecureChannel.HandshakeMs;
            var stream = client.GetStream();
            var reader = new LineReader(stream);
            var (send, recv) = SecureChannel.Handshake(stream, reader, server, SecureChannel.SecretKey(joinSecret));
            client.ReceiveTimeout = 0;
            return new TcpTransport(client, stream, reader, send, recv);
        }
        catch { try { client.Close(); } catch { } throw; }
    }

    public static TcpTransport Connect(string host, int port, byte[] joinSecret, int timeoutMs = 5000)
    {
        var c = new TcpClient();
        if (!c.ConnectAsync(host, port).Wait(timeoutMs)) { c.Dispose(); throw new IOException($"Could not reach {host}:{port}"); }
        return Secure(c, false, joinSecret);
    }

    public void Send(string line)
    {
        if (!_open) return;
        try
        {
            lock (_sendLock)
            {
                var plain = Encoding.UTF8.GetBytes(line);
                var buf = new byte[plain.Length + 16];
                _sendAes.Encrypt(SecureChannel.Nonce(_sendCtr++), plain, buf.AsSpan(0, plain.Length), buf.AsSpan(plain.Length));
                SecureChannel.WriteLine(_stream, Convert.ToBase64String(buf));
            }
        }
        catch { _open = false; }
    }

    // a closed peer still delivers what it sent before closing
    public bool TryReceive(out string line) => _in.TryDequeue(out line!);

    public void Close()
    {
        _open = false;
        try { _client.Close(); } catch { }
    }
}

/// <summary>Accepts joining players. A connection is handed out only after it passed the secure handshake; slow or wrong ones are dropped.</summary>
public sealed class TcpHostListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<ITransport> _accepted = new();
    private volatile bool _run = true;
    private int _pending;
    private const int MaxPending = 16;
    public int Port { get; }
    /// <summary>The session secret that is part of the join code.</summary>
    public byte[] Secret { get; }

    public TcpHostListener(int port = 0, byte[]? secret = null)
    {
        Secret = secret ?? JoinCode.NewSecret();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(() =>
        {
            while (_run)
            {
                try
                {
                    var c = _listener.AcceptTcpClient();
                    if (Interlocked.Increment(ref _pending) > MaxPending) { Interlocked.Decrement(ref _pending); c.Close(); continue; }
                    new Thread(() =>
                    {
                        try { _accepted.Enqueue(TcpTransport.Secure(c, true, Secret)); }
                        catch { }
                        finally { Interlocked.Decrement(ref _pending); }
                    }) { IsBackground = true, Name = "mp-handshake" }.Start();
                }
                catch { if (!_run) break; }
            }
        }) { IsBackground = true, Name = "mp-accept" }.Start();
    }

    public bool TryAccept(out ITransport t) => _accepted.TryDequeue(out t!);
    public void Dispose() { _run = false; try { _listener.Stop(); } catch { } }
}

/// <summary>
/// Join codes: IPv4 address (32 bits), port (16 bits) and a random session secret (77 bits) as 25 base-32 characters (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX).
/// The secret keys the encrypted channel, so the code must be shared only with the people who should join. Works on a LAN or through a forwarded port.
/// </summary>
public static class JoinCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly UInt128 SecretMask = (UInt128.One << 77) - 1;

    /// <summary>A fresh random 77-bit secret (16 bytes, upper bits zero).</summary>
    public static byte[] NewSecret()
    {
        return Normalize(RandomNumberGenerator.GetBytes(16));
    }

    private static byte[] Normalize(byte[] raw)
    {
        UInt128 v = 0;
        foreach (byte x in raw) v = (v << 8) | x;
        v &= SecretMask;
        var o = new byte[16];
        for (int i = 15; i >= 0; i--) { o[i] = (byte)v; v >>= 8; }
        return o;
    }

    public static string Make(IPAddress ip, int port, byte[] secret)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        UInt128 s = 0;
        foreach (byte x in Normalize(secret)) s = (s << 8) | x;
        UInt128 v = ((UInt128)(uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]) << 93) | ((UInt128)(ushort)port << 77) | s;
        var sb = new StringBuilder();
        for (int i = 24; i >= 0; i--)
        {
            sb.Append(Alphabet[(int)((v >> (i * 5)) & 31)]);
            if (i > 0 && i % 5 == 0) sb.Append('-');
        }
        return sb.ToString();
    }

    public static bool TryParse(string code, out string host, out int port, out byte[] secret)
    {
        host = ""; port = 0; secret = Array.Empty<byte>();
        if (code == null || code.Length > 64) return false;
        UInt128 v = 0; int n = 0;
        foreach (char ch in code.ToUpperInvariant())
        {
            if (ch == '-' || ch == ' ') continue;
            char c = ch switch { 'O' => '0', 'I' or 'L' => '1', 'U' => 'V', _ => ch };
            int i = Alphabet.IndexOf(c);
            if (i < 0) return false;
            v = (v << 5) | (uint)i; n++;
        }
        if (n != 25) return false;
        uint ip = (uint)(v >> 93);
        host = $"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}";
        port = (int)((v >> 77) & 0xFFFF);
        var o = new byte[16]; var s = v & SecretMask;
        for (int i = 15; i >= 0; i--) { o[i] = (byte)s; s >>= 8; }
        secret = o;
        return true;
    }

    /// <summary>The first non-loopback IPv4 address of this machine (loopback when there is none).</summary>
    public static IPAddress LocalAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) return u.Address;
        }
        return IPAddress.Loopback;
    }
}
