using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
/// The channel under every TCP link: TLS 1.3 (only; no older version is offered, so there is no downgrade) with a throw-away ECDSA P-256 certificate
/// per host session, plus key confirmation bound to that certificate. The certificate itself proves nothing (anybody can make one), so after the
/// TLS handshake both sides prove they know the session secret of the join code with an HMAC that covers the role, both nonces and the
/// SHA-256 of the host certificate. A man in the middle ends up with a different certificate on each side, so the proofs do not match.
/// Specification: docs/channel-spec.md.
/// </summary>
internal static class SecureChannel
{
    public const int HandshakeMs = 5000;
    public const string TargetHost = "burger-host";

    public static byte[] SecretKey(byte[] joinSecret) => SHA256.HashData(Concat(Encoding.ASCII.GetBytes("burger-mp-psk-v3"), joinSecret));

    public static X509Certificate2 NewCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=" + TargetHost, key, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx)); // re-imported so the private key survives on every platform
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static string B(byte[] b) => Convert.ToBase64String(b);

    private static byte[] Unb(string s, int size)
    {
        if (s.Length > 128) throw new InvalidDataException("handshake field too long");
        var b = Convert.FromBase64String(s);
        if (b.Length != size) throw new InvalidDataException("handshake field size");
        return b;
    }

    public static byte[] Proof(byte[] psk, string role, byte[] certHash, byte[] first, byte[] second) =>
        HMACSHA256.HashData(psk, Concat(Encoding.ASCII.GetBytes("burger-mp-v3|" + role + "|"), certHash, first, second));

    /// <summary>Key confirmation over the established TLS stream. Throws when the peer fails it.</summary>
    public static void Confirm(SslStream tls, LineReader reader, bool server, byte[] psk, byte[] certHash)
    {
        if (tls.SslProtocol != SslProtocols.Tls13) throw new InvalidDataException("TLS 1.3 required");
        if (server)
        {
            byte[] ns = RandomNumberGenerator.GetBytes(16);
            WriteLine(tls, "A1|" + B(ns));
            var f = Read(reader, "A2", 3);
            byte[] nc = Unb(f[1], 16);
            if (!CryptographicOperations.FixedTimeEquals(Unb(f[2], 32), Proof(psk, "client", certHash, ns, nc))) throw new InvalidDataException("wrong session secret");
            WriteLine(tls, "A3|" + B(Proof(psk, "server", certHash, nc, ns)));
        }
        else
        {
            var f = Read(reader, "A1", 2);
            byte[] ns = Unb(f[1], 16);
            byte[] nc = RandomNumberGenerator.GetBytes(16);
            WriteLine(tls, $"A2|{B(nc)}|{B(Proof(psk, "client", certHash, ns, nc))}");
            var g = Read(reader, "A3", 2);
            if (!CryptographicOperations.FixedTimeEquals(Unb(g[1], 32), Proof(psk, "server", certHash, nc, ns))) throw new InvalidDataException("the host does not know the session secret");
        }
    }

    private static string[] Read(LineReader r, string tag, int fields)
    {
        string? l = r.ReadLine(1024);
        if (l == null) throw new IOException("closed during handshake");
        var f = l.Split('|');
        if (f.Length != fields || f[0] != tag) throw new InvalidDataException("bad handshake");
        return f;
    }

    public static void WriteLine(Stream s, string line)
    {
        var b = Encoding.UTF8.GetBytes(line + "\n");
        s.Write(b, 0, b.Length); s.Flush();
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
    public string? ReadLine(int max = Wire.MaxRawLine)
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
                if (_line.Length >= max) throw new InvalidDataException("line too long");
                _line.WriteByte(c);
            }
            _len = _s.Read(_buf, 0, _buf.Length); _pos = 0;
            if (_len <= 0) return null;
        }
    }
}

/// <summary>A TLS 1.3 TCP connection carrying one line per message.</summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly ConcurrentQueue<string> _in = new();
    private volatile bool _open = true;
    private readonly object _sendLock = new();

    public bool Connected => _open;

    private TcpTransport(TcpClient client, Stream stream, LineReader reader)
    {
        _client = client; _stream = stream;
        new Thread(() =>
        {
            try { string? l; while (_open && (l = reader.ReadLine()) != null) _in.Enqueue(l); }
            catch { }
            _open = false;
            try { _client.Close(); } catch { }
        }) { IsBackground = true, Name = "mp-read" }.Start();
    }

    /// <summary>TLS 1.3 plus key confirmation on an accepted or connected socket. Throws (and closes the socket) when the peer fails in time.</summary>
    internal static TcpTransport Secure(TcpClient client, bool server, byte[] joinSecret, X509Certificate2? cert = null)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = client.SendTimeout = SecureChannel.HandshakeMs;
            var tls = new SslStream(client.GetStream(), false);
            byte[] certHash;
            if (server)
            {
                tls.AuthenticateAsServer(new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert, ClientCertificateRequired = false, EnabledSslProtocols = SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                });
                certHash = SHA256.HashData(cert!.RawData);
            }
            else
            {
                tls.AuthenticateAsClient(new SslClientAuthenticationOptions
                {
                    TargetHost = SecureChannel.TargetHost, EnabledSslProtocols = SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, _, _, _) => true, // identity is proven by the session secret, bound to this certificate
                });
                certHash = SHA256.HashData(tls.RemoteCertificate!.GetRawCertData());
            }
            var reader = new LineReader(tls);
            SecureChannel.Confirm(tls, reader, server, SecureChannel.SecretKey(joinSecret), certHash);
            client.ReceiveTimeout = 0; client.SendTimeout = 10000;
            return new TcpTransport(client, tls, reader);
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
        try { lock (_sendLock) SecureChannel.WriteLine(_stream, line); }
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

/// <summary>
/// Accepts joining players. A connection is handed out only after the TLS handshake and the key confirmation; slow or wrong ones are dropped.
/// Limits against floods: 16 handshakes at once, 3 per address, and an address with 5 failed handshakes in a minute is refused for that minute.
/// </summary>
public sealed class TcpHostListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<ITransport> _accepted = new();
    private readonly X509Certificate2 _cert = SecureChannel.NewCertificate();
    private readonly Dictionary<IPAddress, (int Active, List<DateTime> Failures)> _ips = new();
    private volatile bool _run = true;
    private int _pending;
    public const int MaxPending = 16, MaxPerAddress = 3, MaxFailures = 5;
    public int Port { get; }
    /// <summary>The session secret that is part of the join code.</summary>
    public byte[] Secret { get; }
    /// <summary>Connections dropped before the handshake (flood limits).</summary>
    public int Refused { get; private set; }

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
                    var ip = ((IPEndPoint)c.Client.RemoteEndPoint!).Address;
                    if (!Admit(ip)) { Refused++; c.Close(); continue; }
                    new Thread(() =>
                    {
                        try { _accepted.Enqueue(TcpTransport.Secure(c, true, Secret, _cert)); Done(ip, false); }
                        catch { Done(ip, true); }
                    }) { IsBackground = true, Name = "mp-handshake" }.Start();
                }
                catch { if (!_run) break; }
            }
        }) { IsBackground = true, Name = "mp-accept" }.Start();
    }

    private bool Admit(IPAddress ip)
    {
        lock (_ips)
        {
            if (_pending >= MaxPending) return false;
            _ips.TryGetValue(ip, out var e);
            e.Failures ??= new();
            e.Failures.RemoveAll(t => (DateTime.UtcNow - t).TotalSeconds > 60);
            if (e.Active >= MaxPerAddress || e.Failures.Count >= MaxFailures) { _ips[ip] = e; return false; }
            e.Active++; _pending++;
            _ips[ip] = e;
            return true;
        }
    }

    private void Done(IPAddress ip, bool failed)
    {
        lock (_ips)
        {
            _pending--;
            var e = _ips[ip]; e.Active--;
            if (failed) e.Failures.Add(DateTime.UtcNow);
            _ips[ip] = e;
        }
    }

    public bool TryAccept(out ITransport t) => _accepted.TryDequeue(out t!);
    public void Dispose() { _run = false; try { _listener.Stop(); } catch { } _cert.Dispose(); }
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
