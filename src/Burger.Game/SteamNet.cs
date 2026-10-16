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

    public static bool Init()
    {
        if (_tried) return Available;
        _tried = true;
        try
        {
            string idFile = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
            if (!File.Exists(idFile) && Environment.GetEnvironmentVariable("SteamAppId") == null) Environment.SetEnvironmentVariable("SteamAppId", TestAppId.ToString());
            if (!SteamAPI.Init()) { Problem = "Steam is not running or you are not signed in. Start Steam to play over the internet."; return false; }
            SteamNetworkingUtils.InitRelayNetworkAccess();
            MyId = SteamUser.GetSteamID().m_SteamID;
            PersonaName = SteamFriends.GetPersonaName() ?? "";
            Available = true;
            Problem = "";
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Problem = "The Steam library is missing from this copy of the game.";
        }
        catch (Exception e)
        {
            Burger.Core.Log.Warn($"Steam could not start ({e.GetType().Name})", "multiplayer");
            Problem = "Steam could not be started.";
        }
        return Available;
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
        _status = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
        Code = SteamCode.Make(SteamService.MyId, Secret);
    }

    private void OnStatus(SteamNetConnectionStatusChangedCallback_t c)
    {
        if (c.m_info.m_hListenSocket != _socket) return;
        uint id = c.m_hConn.m_HSteamNetConnection;
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
        var id = new SteamNetworkingIdentity();
        id.SetSteamID64(hostId);
        var conn = SteamNetworkingSockets.ConnectP2P(ref id, 0, 0, null);
        if (conn == HSteamNetConnection.Invalid) throw new InvalidDataException("Steam could not start the connection");
        var link = new SteamLink(conn);
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end)
        {
            SteamService.Pump();
            if (SteamNetworkingSockets.GetConnectionInfo(conn, out var info))
            {
                if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected) return LinkAcceptor.Join(link, secret, SteamService.Pump);
                if (info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                    break;
            }
            Thread.Sleep(10);
        }
        link.Close();
        throw new InvalidDataException("Could not reach the host through Steam. Check the code and that the host is playing.");
    }
}
