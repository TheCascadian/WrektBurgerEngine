using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Burger.Core.Multiplayer;

/// <summary>
/// A meeting point for players who cannot open their router. The host dials OUT to the relay and keeps that connection open ("H room"); a joiner dials the
/// relay with the room from the join code ("J room"); the relay tells the host, the host dials out once more ("A room id"), and the relay copies bytes
/// between the two. The relay never sees game data: the TLS channel and the session secret are between host and joiner, and the secret is not
/// in the relay's hands. Neither player's address reaches the other: the host sees only an opaque per-joiner key, the joiner sees only the relay.
/// Limits: connections per address, rooms per address, pending joiners per room, a deadline for the first line, idle and pending timeouts.
/// </summary>
public sealed class RelayServer : IDisposable
{
    public const int FirstLineMs = 5000, PendingMs = 10000, IdleMs = 30 * 60 * 1000, PingMs = 25000;
    public const int MaxConnections = 4000, MaxPerAddress = 24, MaxRoomsPerAddress = 2, MaxPendingPerRoom = 8;

    private sealed class Room
    {
        public TcpClient Control = null!;
        public IPAddress Owner = IPAddress.None;
        public readonly object Lock = new();
        public readonly Dictionary<int, (TcpClient Joiner, DateTime At)> Pending = new();
        public int NextId;
    }

    private readonly TcpListener _listener;
    private readonly Dictionary<long, Room> _rooms = new();
    private readonly Dictionary<IPAddress, int> _perAddress = new();
    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(16);
    private readonly CancellationTokenSource _stop = new();
    private int _connections;
    public int Port { get; }
    /// <summary>Rooms right now (hosts waiting for joiners).</summary>
    public int RoomCount { get { lock (_rooms) return _rooms.Count; } }
    public long Spliced;
    public Action<string>? Log;

    public RelayServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true, Name = "relay-accept" }.Start();
        new Thread(Sweep) { IsBackground = true, Name = "relay-sweep" }.Start();
    }

    private void AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient c;
            try { c = _listener.AcceptTcpClient(); } catch { if (_stop.IsCancellationRequested) break; continue; }
            var ip = ((IPEndPoint)c.Client.RemoteEndPoint!).Address;
            if (!Enter(ip)) { c.Close(); continue; }
            new Thread(() => { try { Handle(c, ip); } catch { } finally { Leave(ip); } }) { IsBackground = true, Name = "relay-conn" }.Start();
        }
    }

    private bool Enter(IPAddress ip)
    {
        lock (_perAddress)
        {
            if (_connections >= MaxConnections) return false;
            _perAddress.TryGetValue(ip, out int n);
            if (n >= MaxPerAddress) return false;
            _perAddress[ip] = n + 1; _connections++;
            return true;
        }
    }

    private void Leave(IPAddress ip)
    {
        lock (_perAddress)
        {
            _connections--;
            if (_perAddress.TryGetValue(ip, out int n)) { if (n <= 1) _perAddress.Remove(ip); else _perAddress[ip] = n - 1; }
        }
    }

    private string Key(IPAddress ip) => Convert.ToHexString(SHA256.HashData(_salt.Concat(ip.GetAddressBytes()).ToArray()))[..12];

    private static string? ReadLine(NetworkStream s, int timeoutMs)
    {
        s.ReadTimeout = timeoutMs;
        var sb = new StringBuilder();
        while (sb.Length < 80)
        {
            int b = s.ReadByte();
            if (b < 0) return null;
            if (b == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)b);
        }
        return null;
    }

    private void Handle(TcpClient c, IPAddress ip)
    {
        c.NoDelay = true;
        var s = c.GetStream();
        string? line = ReadLine(s, FirstLineMs);
        var f = line?.Split(' ');
        if (f == null || f.Length < 2 || !long.TryParse(f[1], out long room) || room <= 0 || room >= (1L << 40)) { c.Close(); return; }
        switch (f[0])
        {
            case "H": Host(c, s, ip, room); break;
            case "J": Join(c, ip, room); break;
            case "A": if (f.Length == 3 && int.TryParse(f[2], out int id)) Accept(c, room, id); else c.Close(); break;
            default: c.Close(); break;
        }
    }

    private void Host(TcpClient c, NetworkStream s, IPAddress ip, long roomId)
    {
        var room = new Room { Control = c, Owner = ip };
        lock (_rooms)
        {
            if (_rooms.ContainsKey(roomId) || _rooms.Values.Count(r => r.Owner.Equals(ip)) >= MaxRoomsPerAddress) { Write(s, "NO\n"); c.Close(); return; }
            _rooms[roomId] = room;
        }
        try
        {
            Write(s, "OK\n");
            Log?.Invoke("room opened");
            s.ReadTimeout = Timeout.Infinite;
            var ping = new Timer(_ => { try { lock (room.Lock) Write(s, "P\n"); } catch { try { c.Close(); } catch { } } }, null, PingMs, PingMs);
            try { while (s.ReadByte() >= 0) { } } catch { }
            ping.Dispose();
        }
        finally
        {
            lock (_rooms) { if (_rooms.TryGetValue(roomId, out var r) && r == room) _rooms.Remove(roomId); }
            lock (room.Lock) foreach (var p in room.Pending.Values) try { p.Joiner.Close(); } catch { }
            c.Close();
        }
    }

    private void Join(TcpClient joiner, IPAddress ip, long roomId)
    {
        Room? room;
        lock (_rooms) _rooms.TryGetValue(roomId, out room);
        if (room == null) { joiner.Close(); return; }
        int id;
        lock (room.Lock)
        {
            if (room.Pending.Count >= MaxPendingPerRoom) { joiner.Close(); return; }
            id = ++room.NextId;
            room.Pending[id] = (joiner, DateTime.UtcNow);
            try { Write(room.Control.GetStream(), $"I {id} {Key(ip)}\n"); }
            catch { room.Pending.Remove(id); joiner.Close(); }
        }
        // the joiner socket stays open and unread until the host's dial-out claims it (or the sweep closes it)
        Thread.Sleep(Timeout.Infinite);
    }

    private void Accept(TcpClient host, long roomId, int id)
    {
        Room? room;
        lock (_rooms) _rooms.TryGetValue(roomId, out room);
        TcpClient? joiner = null;
        if (room != null) lock (room.Lock) { if (room.Pending.Remove(id, out var p)) joiner = p.Joiner; }
        if (joiner == null) { host.Close(); return; }
        Interlocked.Increment(ref Spliced);
        var a = host.GetStream(); var b = joiner.GetStream();
        a.ReadTimeout = b.ReadTimeout = IdleMs;
        var t = new Thread(() => Copy(b, a, joiner, host)) { IsBackground = true, Name = "relay-copy" };
        t.Start();
        Copy(a, b, host, joiner);
    }

    private static void Copy(NetworkStream from, NetworkStream to, TcpClient x, TcpClient y)
    {
        var buf = new byte[16384];
        try { int n; while ((n = from.Read(buf, 0, buf.Length)) > 0) to.Write(buf, 0, n); } catch { }
        try { x.Close(); } catch { }
        try { y.Close(); } catch { }
    }

    private static void Write(NetworkStream s, string text) { var b = Encoding.ASCII.GetBytes(text); s.Write(b, 0, b.Length); }

    private void Sweep()
    {
        while (!_stop.Token.WaitHandle.WaitOne(2000))
        {
            Room[] rooms; lock (_rooms) rooms = _rooms.Values.ToArray();
            foreach (var r in rooms)
                lock (r.Lock)
                    foreach (var kv in r.Pending.Where(kv => (DateTime.UtcNow - kv.Value.At).TotalMilliseconds > PendingMs).ToList())
                    { r.Pending.Remove(kv.Key); try { kv.Value.Joiner.Close(); } catch { } }
        }
    }

    public void Dispose() { _stop.Cancel(); try { _listener.Stop(); } catch { } }
}

/// <summary>Where the relay is: BURGER_RELAY, else the first line of relay.txt next to the game (host:port).</summary>
public static class RelayConfig
{
    public const int DefaultPort = 27961;

    public static (string Host, int Port)? Load()
    {
        string? text = Environment.GetEnvironmentVariable("BURGER_RELAY");
        if (string.IsNullOrWhiteSpace(text))
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "relay.txt");
                if (File.Exists(path)) text = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            }
            catch { }
        }
        return Parse(text);
    }

    public static (string Host, int Port)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        int colon = text.LastIndexOf(':');
        string host = colon > 0 ? text[..colon] : text;
        int port = colon > 0 && int.TryParse(text[(colon + 1)..], out int p) && p is > 0 and < 65536 ? p : DefaultPort;
        return host.Length > 0 && host.Length < 200 && host.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-') ? (host, port) : null;
    }
}

/// <summary>The host's side of the relay: holds the control connection open, and dials out again for every joiner the relay announces.</summary>
public sealed class RelayHostLink : IDisposable
{
    public enum Phase { Connecting, Ready, Failed }

    private readonly string _host; private readonly int _port; private readonly TcpHostListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private volatile Phase _phase = Phase.Connecting;
    private volatile string _status = "Connecting to the relay...";
    public Phase State => _phase;
    public string Status => _status;
    public long Room { get; private set; }

    public RelayHostLink(string host, int port, TcpHostListener listener)
    {
        _host = host; _port = port; _listener = listener;
        new Thread(Run) { IsBackground = true, Name = "mp-relay" }.Start();
    }

    private void Run()
    {
        int failures = 0;
        bool everReady = false;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var c = new TcpClient { NoDelay = true };
                if (!c.ConnectAsync(_host, _port).Wait(5000)) throw new IOException("connect");
                var s = c.GetStream();
                long room = Room != 0 ? Room : NewRoom();
                Send(s, $"H {room}\n");
                s.ReadTimeout = 5000;
                string? ok = ReadLine(s);
                if (ok != "OK")
                {
                    if (ok == "NO" && !everReady) { Log.Info("Relay: room number already taken, picking another", "multiplayer"); Room = 0; continue; }
                    throw new IOException("the relay answered '" + (ok ?? "nothing") + "' instead of OK (room limit reached, or this is not a relay)");
                }
                Room = room; everReady = true; failures = 0;
                Log.Info("Relay: registered a room with the relay; join codes through it are valid", "multiplayer");
                _status = "Reachable over the internet through the relay"; _phase = Phase.Ready;
                s.ReadTimeout = Timeout.Infinite;
                string? line;
                while (!_stop.IsCancellationRequested && (line = ReadLine(s)) != null)
                {
                    var f = line.Split(' ');
                    if (f.Length == 3 && f[0] == "I" && int.TryParse(f[1], out int id)) { string key = f[2]; ThreadPool.QueueUserWorkItem(_ => Dial(room, id, key)); }
                }
                throw new IOException("closed");
            }
            catch when (_stop.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                failures++;
                // the text of socket errors names the relay address: log the kind of failure only
                string kind = e is SocketException se ? "network error " + se.SocketErrorCode : e is AggregateException ae && ae.InnerException is SocketException se2 ? "network error " + se2.SocketErrorCode : e is IOException io && io.InnerException is SocketException se3 ? "network error " + se3.SocketErrorCode : e is IOException ? e.Message : e.GetType().Name;
                Log.Warn($"Relay: connection problem #{failures}: {kind}" + (everReady ? " (was connected; retrying every 3 s)" : failures >= 3 ? " (giving up for this session)" : " (retrying)"), "multiplayer");
                if (everReady) { _phase = Phase.Connecting; _status = "Lost the relay, reconnecting..."; }
                else if (failures >= 3) { _phase = Phase.Failed; _status = "The relay is not reachable. Only players on your own network can join."; }
                _stop.Token.WaitHandle.WaitOne(3000);
            }
        }
    }

    private void Dial(long room, int id, string key)
    {
        try
        {
            var c = new TcpClient { NoDelay = true };
            if (!c.ConnectAsync(_host, _port).Wait(5000)) { c.Close(); return; }
            Send(c.GetStream(), $"A {room} {id}\n");
            _listener.AdmitRelayed(c, key);
        }
        catch { }
    }

    private static long NewRoom()
    {
        long r;
        do { r = BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & ((1L << 40) - 1); } while (r == 0);
        return r;
    }

    private static string? ReadLine(NetworkStream s)
    {
        var sb = new StringBuilder();
        while (sb.Length < 80)
        {
            int b = s.ReadByte();
            if (b < 0) return null;
            if (b == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)b);
        }
        return null;
    }

    private static void Send(NetworkStream s, string text) { var b = Encoding.ASCII.GetBytes(text); s.Write(b, 0, b.Length); }

    public void Dispose() => _stop.Cancel();
}
