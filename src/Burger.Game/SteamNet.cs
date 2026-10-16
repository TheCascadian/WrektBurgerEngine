using System.Runtime.InteropServices;
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

    private static readonly List<string> _log = new();
    /// <summary>Timestamped diagnostics for the multiplayer screen, newest last.</summary>
    public static string[] LogLines { get { lock (_log) return _log.ToArray(); } }
    public static void Note(string text)
    {
        lock (_log) { _log.Add(DateTime.Now.ToString("HH:mm:ss ") + text); if (_log.Count > 80) _log.RemoveAt(0); }
    }
    public static string AppIdUsed { get; private set; } = "";

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
            else { AppIdUsed = TestAppId.ToString(); Environment.SetEnvironmentVariable("SteamAppId", AppIdUsed); Note($"App ID {AppIdUsed} (built-in test app; no steam_appid.txt next to the game)"); }
            Note("Starting Steam API");
            var r = SteamAPI.InitEx(out string msg);
            if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Note($"Steam API refused: {r}" + (string.IsNullOrWhiteSpace(msg) ? "" : " - " + msg));
                Problem = r == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                    ? "Steam is not running or you are not signed in. Start Steam, then press Retry."
                    : "Steam would not start (" + r.ToString().Replace("k_ESteamAPIInitResult_", "") + "). See the log.";
                return false;
            }
            Note("Steam API started");
            SteamNetworkingUtils.InitRelayNetworkAccess();
            MyId = SteamUser.GetSteamID().m_SteamID;
            PersonaName = SteamFriends.GetPersonaName() ?? "";
            Note($"Signed in as {PersonaName} (account {MyId})");
            Available = true;
            Problem = "";
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Problem = "The Steam library is missing from this copy of the game.";
            Note($"Steam library did not load ({e.GetType().Name}): " + (e.Message.Split('\n')[0]));
            Note("Expected steam_api64.dll (Windows), libsteam_api.so (Linux) or libsteam_api.dylib (macOS) next to the game");
        }
        catch (Exception e)
        {
            Burger.Core.Log.Warn($"Steam could not start ({e.GetType().Name})", "multiplayer");
            Problem = "Steam could not be started.";
            Note($"Unexpected Steam error: {e.GetType().Name}");
        }
        return Available;
    }

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
    public static void Pump() { if (Available) SteamAPI.RunCallbacks(); }

    public static void Shutdown() { if (Available) { try { SteamAPI.Shutdown(); } catch { } Available = false; } }
}

/// <summary>One Steam connection as a message link.</summary>
internal sealed class SteamLink : IMessageLink
{
    private readonly HSteamNetConnection _conn;
    private volatile bool _open = true;
    private readonly IntPtr[] _buffer = new IntPtr[32];
    private readonly Queue<byte[]> _in = new();
    public SteamLink(HSteamNetConnection conn) { _conn = conn; }
    public bool Connected => _open;
    public void MarkClosed() => _open = false;
    public HSteamNetConnection Handle => _conn;

    public unsafe void Send(byte[] message)
    {
        if (!_open) return;
        fixed (byte* p = message)
        {
            var r = SteamNetworkingSockets.SendMessageToConnection(_conn, (IntPtr)p, (uint)message.Length, Constants.k_nSteamNetworkingSend_Reliable, out _);
            if (r != EResult.k_EResultOK && r != EResult.k_EResultLimitExceeded) _open = false;
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
        try { SteamNetworkingSockets.CloseConnection(_conn, 0, "", false); } catch { }
    }
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
        SteamService.Note($"Host: connection {c.m_info.m_identityRemote.GetSteamID64()} is now {c.m_info.m_eState.ToString().Replace("k_ESteamNetworkingConnectionState_", "")}" + (c.m_info.m_eEndReason != 0 ? $" (reason {c.m_info.m_eEndReason}: {c.m_info.m_szEndDebug})" : ""));
        switch (c.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                if (_links.Count >= 32 || SteamNetworkingSockets.AcceptConnection(c.m_hConn) != EResult.k_EResultOK) SteamNetworkingSockets.CloseConnection(c.m_hConn, 0, "", false);
                else _links[id] = new SteamLink(c.m_hConn);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (_links.TryGetValue(id, out var link)) _acceptor.Offer(link, c.m_info.m_identityRemote.GetSteamID64().ToString());
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
        SteamService.Note($"Joining host account {hostId}");
        var id = new SteamNetworkingIdentity();
        id.SetSteamID64(hostId);
        var conn = SteamNetworkingSockets.ConnectP2P(ref id, 0, 0, null);
        if (conn == HSteamNetConnection.Invalid) throw new InvalidDataException("Steam could not start the connection");
        var link = new SteamLink(conn);
        var end = DateTime.UtcNow.AddSeconds(15);
        var last = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None;
        while (DateTime.UtcNow < end)
        {
            SteamService.Pump();
            if (SteamNetworkingSockets.GetConnectionInfo(conn, out var info))
            {
                if (info.m_eState != last) { last = info.m_eState; SteamService.Note("Join: " + last.ToString().Replace("k_ESteamNetworkingConnectionState_", "") + (info.m_eEndReason != 0 ? $" (reason {info.m_eEndReason}: {info.m_szEndDebug})" : "")); }
                if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected) return LinkAcceptor.Join(link, secret, SteamService.Pump);
                if (info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                    break;
            }
            Thread.Sleep(10);
        }
        SteamService.Note("Join failed: no connection within 15 s");
        link.Close();
        throw new InvalidDataException("Could not reach the host through Steam. Check the code and that the host is playing.");
    }
}
