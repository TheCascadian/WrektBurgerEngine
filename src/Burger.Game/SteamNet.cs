using System.Runtime.InteropServices;
using Burger.Core;
using Burger.Core.Multiplayer;
using Steamworks;

namespace Burger.Runtime;

/// <summary>
/// Steam Networking for multiplayer: Steam relays the traffic, finds the way through routers, encrypts and authenticates it, and hides both players'
/// network addresses. Players find each other by the host's Steam account id (inside the join code); nobody opens a router port. Works only with the
/// Steam client running; without it <see cref="Available"/> is false and the game falls back to same-network play.
/// </summary>
public static class SteamService
{
    /// <summary>Dev builds use Valve's public test app (480, "Spacewar"). A shipped build takes its id from Steam, or from steam_appid.txt next to the exe.</summary>
    public const uint TestAppId = 480;

    public static bool Available { get; private set; }
    /// <summary>One plain sentence for the player when Steam is not usable.</summary>
    public static string Problem { get; private set; } = "Steam has not been started";
    public static ulong MyId { get; private set; }
    public static string PersonaName { get; private set; } = "";
    private static bool _tried;

    /// <summary>Diagnostics for the multiplayer screen and logs/error.log (category "multiplayer"), newest last.</summary>
    public static LogEntry[] LogLines => Log.Entries.Where(e => e.Where == "multiplayer").TakeLast(200).ToArray();
    public static void Note(string text, LogLevel level = LogLevel.Info)
    {
        if (level == LogLevel.Error) Log.Error("Steam: " + text, "multiplayer");
        else if (level == LogLevel.Warning) Log.Warn("Steam: " + text, "multiplayer");
        else Log.Info("Steam: " + text, "multiplayer");
    }
    public static string AppIdUsed { get; private set; } = "";

    [DllImport("libc.so.6", EntryPoint = "setenv")] private static extern int setenv_linux(string name, string value, int overwrite);
    [DllImport("libSystem.dylib", EntryPoint = "setenv")] private static extern int setenv_mac(string name, string value, int overwrite);

    /// <summary>.NET keeps its own copy of the environment on Linux and macOS; native libraries only see the real one.</summary>
    private static void SetNativeEnv(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsWindows()) return;
        try { if (OperatingSystem.IsMacOS()) setenv_mac(name, value, 1); else setenv_linux(name, value, 1); } catch (Exception e) { Note($"Could not set {name} natively ({e.GetType().Name})"); }
    }

    public static bool Init()
    {
        if (_tried) return Available;
        _tried = true;
        try
        {
            string idFile = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            string env = Environment.GetEnvironmentVariable("SteamAppId") ?? "";
            if (File.Exists(idFile)) { AppIdUsed = File.ReadAllText(idFile).Trim(); Note($"App ID {AppIdUsed} (from steam_appid.txt)"); }
            else if (env.Length > 0) { AppIdUsed = env; Note($"App ID {env} (from SteamAppId)"); }
            else { AppIdUsed = TestAppId.ToString(); Note($"App ID {AppIdUsed} (built-in test app; no steam_appid.txt next to the game)"); }
            // Steam reads steam_appid.txt from the working directory, which is not always the game folder: hand it the id as a real (native) environment variable
            SetNativeEnv("SteamAppId", AppIdUsed); SetNativeEnv("SteamGameId", AppIdUsed);
            Note("Starting Steam API");
            var r = SteamAPI.InitEx(out string msg);
            if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Note($"Steam API refused: {r}" + (string.IsNullOrWhiteSpace(msg) ? "" : " - " + msg), LogLevel.Error);
                Problem = r == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                    ? "Steam is not running or you are not signed in. Start Steam, then press Retry."
                    : "Steam would not start (" + r.ToString().Replace("k_ESteamAPIInitResult_", "") + "). See the log.";
                return false;
            }
            Note("Steam API started");
            SteamNetworkingUtils.InitRelayNetworkAccess();
            RaiseSendBuffer();
            Note("Asked Steam to start its relay network connection");
            MyId = SteamUser.GetSteamID().m_SteamID;
            PersonaName = SteamFriends.GetPersonaName() ?? "";
            Note($"Signed in as {PersonaName} (account {MyId})");
            Available = true;
            Problem = "";
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Problem = "The Steam library is missing from this copy of the game.";
            Note($"Steam library did not load ({e.GetType().Name}): " + (e.Message.Split('\n')[0]), LogLevel.Error);
            Note("Expected steam_api64.dll (Windows), libsteam_api.so (Linux) or libsteam_api.dylib (macOS) next to the game, from a Steamworks SDK matching Steamworks.NET 2024.8.0", LogLevel.Error);
        }
        catch (Exception e)
        {
            Burger.Core.Log.Warn($"Steam could not start ({e.GetType().Name})", "multiplayer");
            Problem = "Steam could not be started.";
            Note($"Unexpected Steam error: {e.GetType().Name}: {e.Message}", LogLevel.Error);
        }
        return Available;
    }

    /// <summary>The default send buffer (512 KB) is smaller than the game state a joiner is sent: let a connection queue up to 16 MB.</summary>
    private static unsafe void RaiseSendBuffer()
    {
        try
        {
            int size = 16 * 1024 * 1024;
            bool ok = SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, (IntPtr)(&size));
            Note(ok ? "Send buffer raised to 16 MB" : "Could not raise the send buffer (large saves are queued by the game instead)", ok ? LogLevel.Info : LogLevel.Warning);
        }
        catch (Exception e) { Note($"Could not raise the send buffer ({e.GetType().Name})", LogLevel.Warning); }
    }

    private static readonly List<SteamLink> _links = new();
    internal static void Track(SteamLink l) { lock (_links) _links.Add(l); }
    internal static void Untrack(SteamLink l) { lock (_links) _links.Remove(l); }

    /// <summary>Try again after the player started Steam.</summary>
    public static void Retry() { if (Available) return; _tried = false; Note("Retrying"); Init(); }

    private static ESteamNetworkingAvailability _lastRelay = (ESteamNetworkingAvailability)999;
    /// <summary>State of Steam's relay network (what carries the game): ok is true when ready.</summary>
    public static string RelayState(out bool ok)
    {
        ok = false;
        if (!Available) return "not checked";
        var a = SteamNetworkingUtils.GetRelayNetworkStatus(out var st);
        string text;
        switch (a)
        {
            case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current: ok = true; text = "relay network ready"; break;
            case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Attempting:
            case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Waiting:
            case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_NeverTried:
            case ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Previously:
                text = "relay network starting"; break;
            default: text = "relay network unavailable (" + a.ToString().Replace("k_ESteamNetworkingAvailability_", "") + ")"; break;
        }
        if (a != _lastRelay) { _lastRelay = a; Note("Steam " + text + (string.IsNullOrWhiteSpace(st.m_debugMsg) ? "" : ": " + st.m_debugMsg)); }
        return text;
    }

    /// <summary>Once per frame.</summary>
    public static void Pump()
    {
        if (!Available) return;
        SteamAPI.RunCallbacks();
        SteamLink[] links; lock (_links) links = _links.ToArray();
        foreach (var l in links) l.Flush();
    }

    public static void Shutdown() { if (Available) { try { SteamAPI.Shutdown(); } catch { } Available = false; } }
}

/// <summary>One Steam connection as a message link. Outgoing messages are queued and handed to Steam in order as its send buffer allows, so a large save is never dropped.</summary>
internal sealed class SteamLink : IMessageLink
{
    private readonly HSteamNetConnection _conn;
    private readonly string _name;
    private volatile bool _open = true;
    private readonly IntPtr[] _buffer = new IntPtr[32];
    private readonly Queue<byte[]> _in = new();
    private readonly Queue<byte[]> _out = new();
    private int _sentTotal, _queuedPeak;
    private bool _warnedSlow;
    public SteamLink(HSteamNetConnection conn, string name) { _conn = conn; _name = name; SteamService.Track(this); }
    public HSteamNetConnection Handle => _conn;
    public void MarkClosed() { if (_open) { _open = false; SteamService.Untrack(this); } }

    public bool Connected
    {
        get
        {
            if (!_open) return false;
            if (SteamNetworkingSockets.GetConnectionInfo(_conn, out var info) &&
                info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None)
            {
                SteamService.Note($"{_name}: connection ended: {info.m_eState.ToString().Replace("k_ESteamNetworkingConnectionState_", "")}, reason {info.m_eEndReason}" + (string.IsNullOrEmpty(info.m_szEndDebug) ? "" : $" ({info.m_szEndDebug})") + $"; sent {_sentTotal} messages, {_out.Count} still queued", LogLevel.Warning);
                MarkClosed();
            }
            return _open;
        }
    }

    public void Send(byte[] message)
    {
        if (!_open) return;
        _out.Enqueue(message);
        if (_out.Count > _queuedPeak) _queuedPeak = _out.Count;
        Flush();
    }

    /// <summary>Hand queued messages to Steam until its buffer is full (then try again next frame).</summary>
    public unsafe void Flush()
    {
        while (_open && _out.Count > 0)
        {
            var m = _out.Peek();
            EResult r;
            fixed (byte* p = m) r = SteamNetworkingSockets.SendMessageToConnection(_conn, (IntPtr)p, (uint)m.Length, Constants.k_nSteamNetworkingSend_Reliable, out _);
            if (r == EResult.k_EResultOK) { _out.Dequeue(); _sentTotal++; continue; }
            if (r == EResult.k_EResultLimitExceeded)
            {
                if (!_warnedSlow && _out.Count > 200) { _warnedSlow = true; SteamService.Note($"{_name}: Steam's send buffer is full, {_out.Count} messages waiting (peak {_queuedPeak}); the connection is slower than the data being sent", LogLevel.Warning); }
                return;
            }
            SteamService.Note($"{_name}: Steam refused a {m.Length}-byte message: {r}; closing", LogLevel.Error);
            _open = false; SteamService.Untrack(this);
            return;
        }
    }

    public bool TryReceive(out byte[] message)
    {
        if (_in.Count == 0 && _open)
        {
            int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(_conn, _buffer, _buffer.Length);
            for (int i = 0; i < n; i++)
            {
                var m = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_buffer[i]);
                if (m.m_cbSize > 0 && m.m_cbSize <= Wire.MaxRawLine * 2) { var b = new byte[m.m_cbSize]; Marshal.Copy(m.m_pData, b, 0, m.m_cbSize); _in.Enqueue(b); }
                else SteamService.Note($"{_name}: dropped a {m.m_cbSize}-byte message (limit {Wire.MaxRawLine * 2})", LogLevel.Warning);
                SteamNetworkingMessage_t.Release(_buffer[i]);
            }
        }
        if (_in.Count > 0) { message = _in.Dequeue(); return true; }
        message = Array.Empty<byte>(); return false;
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        SteamService.Untrack(this);
        SteamService.Note($"{_name}: closing the connection ourselves ({_out.Count} messages were still queued, {_sentTotal} sent)");
        try { SteamNetworkingSockets.CloseConnection(_conn, 0, "", true); } catch { }
    }

    /// <summary>One line on how the connection runs: ping and whether it goes through Steam's relays.</summary>
    public string Describe()
    {
        try
        {
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(_conn, ref _status, 0, ref _lane) == EResult.k_EResultOK)
            {
                string detail = SteamNetworkingSockets.GetDetailedConnectionStatus(_conn, out string text, 2048) == 0 ? text.Replace('\n', ' ') : "";
                return $"ping {_status.m_nPing} ms, quality {_status.m_flConnectionQualityLocal:P0} local / {_status.m_flConnectionQualityRemote:P0} remote. {detail}".Trim();
            }
        }
        catch { }
        return "no status";
    }
    private SteamNetConnectionRealTimeStatus_t _status;
    private SteamNetConnectionRealTimeLaneStatus_t _lane;
}

/// <summary>Hosting over Steam: a listen socket that hands every connection to the session-secret check.</summary>
public sealed class SteamHost : IAcceptSource
{
    private readonly HSteamListenSocket _socket;
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t> _status;
    private readonly LinkAcceptor _acceptor;
    private readonly Dictionary<uint, SteamLink> _links = new();
    public byte[] Secret { get; }
    public string Code { get; }

    public SteamHost()
    {
        Secret = JoinCode.NewSecret();
        _acceptor = new LinkAcceptor(Secret);
        _socket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        SteamService.Note("Host: listening for players (Steam relay)");
        _status = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
        Code = SteamCode.Make(SteamService.MyId, Secret);
    }

    private void OnStatus(SteamNetConnectionStatusChangedCallback_t c)
    {
        if (c.m_info.m_hListenSocket != _socket) return;
        uint id = c.m_hConn.m_HSteamNetConnection;
        ulong who = c.m_info.m_identityRemote.GetSteamID64();
        string state = c.m_info.m_eState.ToString().Replace("k_ESteamNetworkingConnectionState_", "");
        string was = c.m_eOldState.ToString().Replace("k_ESteamNetworkingConnectionState_", "");
        string reason = c.m_info.m_eEndReason != 0 ? $" - reason {c.m_info.m_eEndReason}" + (string.IsNullOrEmpty(c.m_info.m_szEndDebug) ? "" : $": {c.m_info.m_szEndDebug}") : "";
        bool bad = c.m_info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;
        SteamService.Note($"Host: account {who}: {was} -> {state}{reason}", bad ? LogLevel.Warning : LogLevel.Info);
        switch (c.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                if (_links.Count >= 32) { SteamService.Note($"Host: refused account {who}: already {_links.Count} connections", LogLevel.Warning); SteamNetworkingSockets.CloseConnection(c.m_hConn, 0, "", false); }
                else
                {
                    var r = SteamNetworkingSockets.AcceptConnection(c.m_hConn);
                    if (r != EResult.k_EResultOK) { SteamService.Note($"Host: could not accept account {who}: {r}", LogLevel.Error); SteamNetworkingSockets.CloseConnection(c.m_hConn, 0, "", false); }
                    else _links[id] = new SteamLink(c.m_hConn, "Host/" + who);
                }
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (_links.TryGetValue(id, out var link))
                {
                    SteamService.Note($"Host: account {who} connected: {link.Describe()}");
                    _acceptor.Offer(link, who.ToString());
                }
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                if (_links.Remove(id, out var gone)) gone.MarkClosed();
                SteamNetworkingSockets.CloseConnection(c.m_hConn, 0, "", false);
                break;
        }
    }

    public bool TryAccept(out ITransport t) => _acceptor.TryAccept(out t);

    public void Dispose()
    {
        _status.Dispose();
        foreach (var l in _links.Values) l.Close();
        _links.Clear();
        _acceptor.Dispose();
        try { SteamNetworkingSockets.CloseListenSocket(_socket); } catch { }
    }
}

public static class SteamJoin
{
    /// <summary>Connects to the host named in the code and passes the session-secret check. Blocks up to about 15 s while pumping Steam; throws a message safe to show.</summary>
    public static ITransport Connect(ulong hostId, byte[] secret)
    {
        SteamService.Note($"Join: connecting to host account {hostId}");
        string relay = SteamService.RelayState(out bool relayOk);
        if (!relayOk) SteamService.Note($"Join: Steam's {relay}; the connection may take longer or fail", LogLevel.Warning);
        var id = new SteamNetworkingIdentity();
        id.SetSteamID64(hostId);
        var conn = SteamNetworkingSockets.ConnectP2P(ref id, 0, 0, null);
        if (conn == HSteamNetConnection.Invalid) { SteamService.Note("Join: Steam returned no connection handle (invalid identity or Steam not ready)", LogLevel.Error); throw new InvalidDataException("Steam could not start the connection"); }
        var link = new SteamLink(conn, "Join/" + hostId);
        var end = DateTime.UtcNow.AddSeconds(15);
        var last = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None;
        string detail = "no state change";
        while (DateTime.UtcNow < end)
        {
            SteamService.Pump();
            if (SteamNetworkingSockets.GetConnectionInfo(conn, out var info))
            {
                if (info.m_eState != last)
                {
                    last = info.m_eState;
                    detail = last.ToString().Replace("k_ESteamNetworkingConnectionState_", "") + (info.m_eEndReason != 0 ? $", reason {info.m_eEndReason}" + (string.IsNullOrEmpty(info.m_szEndDebug) ? "" : $" ({info.m_szEndDebug})") : "");
                    SteamService.Note("Join: " + detail, info.m_eEndReason != 0 ? LogLevel.Warning : LogLevel.Info);
                }
                if (last == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    SteamService.Note("Join: connected: " + link.Describe());
                    return LinkAcceptor.Join(link, secret, SteamService.Pump);
                }
                if (last is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                {
                    string why = info.m_eEndReason switch
                    {
                        >= 3000 and < 4000 => "the host is not online or is not hosting (is the game running, and Steam signed in?)",
                        >= 4000 and < 5000 => "the Steam network could not reach the host (a firewall, or Steam's relay network is down)",
                        _ => "the connection was refused or lost",
                    };
                    SteamService.Note($"Join failed: {detail}: {why}", LogLevel.Error);
                    link.Close();
                    throw new InvalidDataException("Steam could not reach the host: " + why);
                }
            }
            Thread.Sleep(10);
        }
        SteamService.Note($"Join failed: no connection within 15 s (last state: {detail}). Steam {SteamService.RelayState(out _)}", LogLevel.Error);
        link.Close();
        throw new InvalidDataException("Could not reach the host through Steam within 15 seconds. Check the code, that the host is in the game, and the Steam log.");
    }
}
