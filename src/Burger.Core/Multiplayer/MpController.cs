using System.Net;

namespace Burger.Core.Multiplayer;

/// <summary>
/// Everything the game window does for multiplayer that is not drawing: host a session behind a listener, join by code, move accepted
/// connections into the session, hand the session's pause and speed to the game clock, keep the local player's country bound, and clean up
/// when the link ends. The app calls these methods; tests (burger sec-test, mp-test) call the same ones without a window.
/// </summary>
public sealed class MpController
{
    public LockstepSession? Session { get; private set; }
    public TcpHostListener? Listener { get; private set; }
    /// <summary>The last message for the player (errors, "Connecting...").</summary>
    public string Message { get; private set; } = "";
    /// <summary>Lines for the chat and toast list.</summary>
    public List<string> Log { get; } = new();
    /// <summary>The code for players on the same network (always there while hosting).</summary>
    public string LanCode { get; private set; } = "";
    /// <summary>The code for players over the internet: empty until the router has opened the port, and empty when it cannot.</summary>
    public string InternetCode { get; private set; } = "";
    /// <summary>One plain sentence on how reachable the host is.</summary>
    /// <summary>Why the platform (Steam) path is off, shown with the network status.</summary>
    public string PlatformNote { get; set; } = "";
    public string NetworkStatus => (PlatformNote.Length > 0 ? PlatformNote + " " : "") + ((_extraStatus.Length > 0 ? _extraStatus : null) ?? RelayLink?.Status ?? Mapper?.Status ?? (LanCode.Length > 0 ? "Players on your own network can join" : ""));
    public PortMapper? Mapper { get; private set; }
    /// <summary>Peers that arrive over a platform link (Steam).</summary>
    public IAcceptSource? Extra { get; private set; }
    private string _extraStatus = "";

    /// <summary>The platform (Steam) is hosting the internet path: its code replaces the router and relay paths. Call after <see cref="Host"/>.</summary>
    public void UsePlatform(IAcceptSource source, string internetCode, string status)
    {
        Mapper?.Dispose(); Mapper = null; RelayLink?.Dispose(); RelayLink = null;
        Extra = source; InternetCode = internetCode; _extraStatus = status;
        Add("* Internet join code ready");
    }

    /// <summary>Joins over a platform link that is already authenticated (see <see cref="LinkAcceptor.Join"/>).</summary>
    public bool JoinLink(ITransport link, string playerName, Func<string, int, Game> load)
    {
        Stop();
        Wire(LockstepSession.Join(link, string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim(), load));
        Message = "Connecting...";
        return true;
    }
    public RelayHostLink? RelayLink { get; private set; }
    public event Action<Game>? GameReplaced;
    public event Action<string>? Toast;

    private void Add(string text) { Log.Add(text); if (Log.Count > 50) Log.RemoveAt(0); }

    private void Wire(LockstepSession mp)
    {
        Session = mp; Log.Clear();
        mp.Toast += t => { Add("* " + t); Message = t; Toast?.Invoke(t); };
        mp.Chat += (n, t) => Add($"{n}: {t}");
        mp.GameReplaced += g => GameReplaced?.Invoke(g);
    }

    /// <summary>Starts hosting a game that is already loaded and started. Returns the join code.</summary>
    public string Host(Game game, string project, int seed, string playerName, int preferredPort = 27960, bool openRouter = true, (string Host, int Port)? relay = null)
    {
        Stop();
        var mp = LockstepSession.Host(game, project, seed, string.IsNullOrWhiteSpace(playerName) ? "Host" : playerName.Trim());
        Wire(mp);
        try { Listener = new TcpHostListener(preferredPort); } catch { Listener = new TcpHostListener(); }
        LanCode = JoinCode.Make(JoinCode.LocalAddress(), Listener.Port, Listener.Secret);
        InternetCode = "";
        // a relay, when there is one, replaces opening the router: no address of the host's goes into any internet code
        if (relay is { } rl) RelayLink = new RelayHostLink(rl.Host, rl.Port, Listener);
        else if (openRouter) Mapper = new PortMapper(Listener.Port);
        Add("* Join code ready");
        return LanCode;
    }

    /// <summary>Connects to a host by join code. <paramref name="load"/> makes a fresh game for the host's project and seed (and refuses a project this machine does not have).</summary>
    public bool Join(string code, string playerName, Func<string, int, Game> load)
    {
        if (!JoinCode.TryParseAny(code, out var host, out int port, out long room, out var secret)) { Message = "That join code is not valid"; return false; }
        Stop();
        try
        {
            ITransport link;
            if (room != 0)
            {
                var relay = RelayConfig.Load();
                if (relay == null) { Message = "This code needs the relay, and this copy of the game has none set (relay.txt)"; return false; }
                link = TcpTransport.ConnectRelay(relay.Value.Host, relay.Value.Port, room, secret);
            }
            else
            {
                if (IPAddress.TryParse(host, out var ha) && !IPAddress.IsLoopback(ha) && !TransportSelector.SameSubnet(ha) && !PortMapper.IsPublic(ha))
                { Message = "That code only works on the host's own network. Ask the host for the Internet code (the host needs Steam running)."; return false; }
                link = TcpTransport.Connect(host, port, secret);
            }
            Wire(LockstepSession.Join(link, string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim(), load));
            Message = "Connecting...";
            return true;
        }
        catch (Exception e)
        {
            // the text of a socket error names the address: keep it off the screen and out of logs
            Message = e is InvalidDataException or ArgumentException ? "Cannot connect: " + e.Message : "Cannot connect. Check the code and that the host is running.";
            return false;
        }
    }

    public void Stop()
    {
        try { Session?.Leave(); } catch { }
        Session = null;
        Listener?.Dispose();
        Listener = null;
        Mapper?.Dispose();
        Mapper = null;
        RelayLink?.Dispose();
        RelayLink = null;
        Extra?.Dispose();
        Extra = null; _extraStatus = "";
        LanCode = InternetCode = "";
    }

    /// <summary>Once per frame. Returns false when the session ended (the caller shows the menu).</summary>
    public bool Pump(float dt)
    {
        var mp = Session;
        if (mp == null) return true;
        try
        {
            if (RelayLink is { State: RelayHostLink.Phase.Ready } rlk && InternetCode.Length == 0 && Listener != null)
            {
                InternetCode = JoinCode.MakeRelay(rlk.Room, Listener.Secret);
                Add("* Internet join code ready");
            }
            if (Mapper is { State: PortMapper.Phase.Ready } m && InternetCode.Length == 0 && Listener != null && IPAddress.TryParse(m.PublicAddress, out var pub))
            {
                InternetCode = JoinCode.Make(pub, m.ExternalPort, Listener.Secret);
                Add("* Internet join code ready");
            }
            while (Listener != null && Listener.TryAccept(out var t)) mp.AddPeer(t);
            while (Extra != null && Extra.TryAccept(out var t2)) mp.AddPeer(t2);
            var game = mp.Game;
            if (game != null) { game.Clock.Paused = mp.Paused; game.Clock.Speed = mp.Speed; }
            mp.Update(dt);
            game = mp.Game;
            if (game != null && mp.Me is { Tag.Length: > 0 } me && game.World.Player?.Tag != me.Tag)
            {
                var c = game.World.GetCountry(me.Tag);
                if (c != null) game.World.Player = c;
            }
        }
        catch (Exception ex)
        {
            Burger.Core.Log.Error($"Multiplayer error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Message = "Multiplayer error: " + ex.Message;
        }
        if (mp.Disconnected && Session == mp)
        {
            Add("* Disconnected"); Message = "Disconnected";
            Stop();
            return false;
        }
        return true;
    }
}
