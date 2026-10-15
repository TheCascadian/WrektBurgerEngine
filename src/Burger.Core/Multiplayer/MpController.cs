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
    public string Host(Game game, string project, int seed, string playerName, int preferredPort = 27960)
    {
        Stop();
        var mp = LockstepSession.Host(game, project, seed, string.IsNullOrWhiteSpace(playerName) ? "Host" : playerName.Trim());
        Wire(mp);
        try { Listener = new TcpHostListener(preferredPort); } catch { Listener = new TcpHostListener(); }
        string code = JoinCode.Make(JoinCode.LocalAddress(), Listener.Port, Listener.Secret);
        Add("* Join code: " + code);
        return code;
    }

    /// <summary>Connects to a host by join code. <paramref name="load"/> makes a fresh game for the host's project and seed (and refuses a project this machine does not have).</summary>
    public bool Join(string code, string playerName, Func<string, int, Game> load)
    {
        if (!JoinCode.TryParse(code, out var host, out int port, out var secret)) { Message = "That join code is not valid"; return false; }
        Stop();
        try
        {
            var link = TcpTransport.Connect(host, port, secret);
            Wire(LockstepSession.Join(link, string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim(), load));
            Message = "Connecting...";
            return true;
        }
        catch (Exception e) { Message = "Cannot connect: " + e.Message; return false; }
    }

    public void Stop()
    {
        try { Session?.Leave(); } catch { }
        Session = null;
        Listener?.Dispose();
        Listener = null;
    }

    /// <summary>Once per frame. Returns false when the session ended (the caller shows the menu).</summary>
    public bool Pump(float dt)
    {
        var mp = Session;
        if (mp == null) return true;
        try
        {
            while (Listener != null && Listener.TryAccept(out var t)) mp.AddPeer(t);
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
            Session = null; Listener?.Dispose(); Listener = null;
            return false;
        }
        return true;
    }
}
