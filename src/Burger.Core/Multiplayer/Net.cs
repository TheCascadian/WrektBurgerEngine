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
        string dir = Path.Combine(gameRoot, "scripts");
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => (Rel: Path.GetRelativePath(dir, f).Replace('\\', '/'), Full: f))
                .OrderBy(f => f.Rel, StringComparer.Ordinal);
            foreach (var (rel, full) in files)
            {
                var name = Encoding.UTF8.GetBytes(rel + "\n");
                sha.TransformBlock(name, 0, name.Length, null, 0);
                var data = File.ReadAllBytes(full);
                sha.TransformBlock(data, 0, data.Length, null, 0);
            }
        }
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

/// <summary>A TCP connection carrying one line per message.</summary>
public sealed class TcpTransport : ITransport
{
    private readonly TcpClient _client;
    private readonly StreamWriter _writer;
    private readonly ConcurrentQueue<string> _in = new();
    private volatile bool _open = true;
    private readonly object _sendLock = new();

    public bool Connected => _open;

    public TcpTransport(TcpClient client)
    {
        _client = client;
        client.NoDelay = true;
        var stream = client.GetStream();
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        new Thread(() =>
        {
            try { string? l; while (_open && (l = reader.ReadLine()) != null) _in.Enqueue(l); }
            catch { }
            _open = false;
        }) { IsBackground = true, Name = "mp-read" }.Start();
    }

    public static TcpTransport Connect(string host, int port, int timeoutMs = 5000)
    {
        var c = new TcpClient();
        if (!c.ConnectAsync(host, port).Wait(timeoutMs)) { c.Dispose(); throw new IOException($"Could not reach {host}:{port}"); }
        return new TcpTransport(c);
    }

    public void Send(string line)
    {
        if (!_open) return;
        try { lock (_sendLock) _writer.WriteLine(line); }
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

/// <summary>Accepts joining players.</summary>
public sealed class TcpHostListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<ITransport> _accepted = new();
    private volatile bool _run = true;
    public int Port { get; }

    public TcpHostListener(int port = 0)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(() =>
        {
            while (_run)
            {
                try { _accepted.Enqueue(new TcpTransport(_listener.AcceptTcpClient())); }
                catch { if (!_run) break; }
            }
        }) { IsBackground = true, Name = "mp-accept" }.Start();
    }

    public bool TryAccept(out ITransport t) => _accepted.TryDequeue(out t!);
    public void Dispose() { _run = false; try { _listener.Stop(); } catch { } }
}

/// <summary>Join codes: the host's IPv4 address and port as ten base-32 characters (XXXXX-XXXXX). Works on a LAN or through a forwarded port.</summary>
public static class JoinCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Make(IPAddress ip, int port)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        ulong v = ((ulong)b[0] << 40) | ((ulong)b[1] << 32) | ((ulong)b[2] << 24) | ((ulong)b[3] << 16) | (ushort)port;
        var sb = new StringBuilder();
        for (int i = 9; i >= 0; i--) sb.Append(Alphabet[(int)((v >> (i * 5)) & 31)]);
        return sb.ToString(0, 5) + "-" + sb.ToString(5, 5);
    }

    public static bool TryParse(string code, out string host, out int port)
    {
        host = ""; port = 0;
        ulong v = 0; int n = 0;
        foreach (char ch in code.ToUpperInvariant())
        {
            if (ch == '-' || ch == ' ') continue;
            char c = ch switch { 'O' => '0', 'I' or 'L' => '1', 'U' => 'V', _ => ch };
            int i = Alphabet.IndexOf(c);
            if (i < 0) return false;
            v = (v << 5) | (uint)i; n++;
        }
        if (n != 10) return false;
        host = $"{(v >> 40) & 255}.{(v >> 32) & 255}.{(v >> 24) & 255}.{(v >> 16) & 255}";
        port = (int)(v & 0xFFFF);
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
