using System.Net;
using Burger.Core;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Multiplayer plumbing: hosting, joining, pumping the session each frame, rebinding the view when the game is replaced.</summary>
public sealed partial class App
{
    public LockstepSession? Mp { get; private set; }
    public List<string> MpChat { get; } = new();
    private TcpHostListener? _listener;
    private string _joinCode = "", _joinName = Environment.UserName, _mpMessage = "";
    private bool _joinOpen;
    private string Project => Path.GetFileName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\'));

    private void MpLog(string text) { MpChat.Add(text); if (MpChat.Count > 50) MpChat.RemoveAt(0); }

    private void WireSession(LockstepSession mp)
    {
        Mp = mp;
        MpChat.Clear();
        mp.Toast += t => { MpLog("* " + t); Ui.SetStatus(t); _mpMessage = t; };
        mp.Chat += (n, t) => MpLog($"{n}: {t}");
        mp.GameReplaced += g => _pending = () => AdoptGame(g);
    }

    /// <summary>The session made a new game (first sync of a joiner, or a resync after a mismatch): show it.</summary>
    private void AdoptGame(Game g)
    {
        if (Mp == null) return;
        bool wasPlaying = _screen == AppScreen.Playing;
        LoadGame(keepMap: true, adopt: g);
        if (Game == null) return;
        _screen = AppScreen.Playing;
        Renderer!.Dirty = true;
        if (!wasPlaying)
        {
            var p = Game.World.Player;
            if (p?.Capital != null) Camera!.LookAt(Renderer.LocationPos(p.Capital), 45);
        }
    }

    public void HostMultiplayer(string? tag)
    {
        MpStop();
        int seed = Environment.TickCount & 0x7fffffff;
        LoadGame(keepMap: true, seed: seed);
        if (Game == null) return;
        Game.StartNewGame(null);
        var mp = LockstepSession.Host(Game, Project, seed, string.IsNullOrWhiteSpace(_joinName) ? "Host" : _joinName.Trim());
        WireSession(mp);
        try { _listener = new TcpHostListener(27960); } catch { _listener = new TcpHostListener(); }
        string code = JoinCode.Make(JoinCode.LocalAddress(), _listener.Port);
        MpLog("* Join code: " + code);
        EnterPlaying();
        if (tag != null) mp.Pick(tag);
        Ui.SetStatus("Hosting. Join code " + code + " (paused: press Space to start)");
    }

    public void JoinMultiplayer(string code, string name)
    {
        if (!JoinCode.TryParse(code, out var host, out int port)) { _mpMessage = "That join code is not valid"; return; }
        MpStop();
        try
        {
            var link = TcpTransport.Connect(host, port);
            var mp = LockstepSession.Join(link, string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim(), (project, seed) =>
            {
                if (project != Project) throw new InvalidDataException($"this machine has '{Project}', the host plays '{project}'");
                var g = Game.Load(_gameRoot, compileScripts: true, seed: seed);
                g.StartNewGame(null);
                return g;
            });
            WireSession(mp);
            _mpMessage = "Connecting...";
        }
        catch (Exception e) { _mpMessage = "Cannot connect: " + e.Message; }
    }

    public void MpStop()
    {
        try { Mp?.Leave(); } catch { }
        Mp = null;
        _listener?.Dispose();
        _listener = null;
    }

    private void MpPump(float dt)
    {
        var mp = Mp;
        if (mp == null) return;
        try
        {
            while (_listener != null && _listener.TryAccept(out var t)) mp.AddPeer(t);
            var game = mp.Game;
            if (game != null)
            {
                game.Clock.Paused = mp.Paused;
                game.Clock.Speed = mp.Speed;
            }
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
            Log.Error($"Multiplayer error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Ui.SetStatus("Multiplayer error: " + ex.Message);
        }
        if (mp.Disconnected && Mp == mp) { MpLog("* Disconnected"); _mpMessage = "Disconnected"; Mp = null; _listener?.Dispose(); _listener = null; if (_screen == AppScreen.Playing) { _screen = AppScreen.Menu; } }
    }

    private void DrawMenuMultiplayer(float x, float y, float w, float half, float bh)
    {
        float s = Gui.Scale;
        if (Gui.Button(new Rectangle(x, y, half, bh), "Host game", _menuSelection != null, "Others join with a code; you play the selected country")) { HostMultiplayer(_menuSelection!.Tag); Gui.EndFrame(); Raylib.EndDrawing(); return; }
        if (Gui.Button(new Rectangle(x + half + 6 * s, y, half, bh), "Join game", true, "Enter a join code")) _joinOpen = !_joinOpen;
    }

    private void DrawMenuJoin()
    {
        float s = Gui.Scale;
        if (_joinOpen)
        {
            var r = new Rectangle(460 * s, 30 * s, 420 * s, 200 * s);
            Gui.Panel(r, "Join a game");
            float x = r.X + 16 * s, y = r.Y + 40 * s, w = r.Width - 32 * s;
            Gui.Label("Your name", x, y, 14, Gui.TextDim);
            Gui.TextField("mp-name", new Rectangle(x, y + 20 * s, w, 28 * s), ref _joinName, 24, "Name");
            y += 58 * s;
            Gui.Label("Join code (XXXXX-XXXXX)", x, y, 14, Gui.TextDim);
            Gui.TextField("mp-code", new Rectangle(x, y + 20 * s, w, 28 * s), ref _joinCode, 16, "Code");
            y += 58 * s;
            if (Gui.Button(new Rectangle(x, y, w, 32 * s), "Connect", _joinCode.Length > 0)) JoinMultiplayer(_joinCode, _joinName);
            Gui.Block(r);
        }
        if (_mpMessage.Length > 0 && _screen == AppScreen.Menu)
        {
            float w = Gui.Measure(_mpMessage, 16).X + 24 * s;
            var r = new Rectangle(460 * s, Raylib.GetScreenHeight() - 50 * s, w, 32 * s);
            Gui.Panel(r);
            Gui.Label(_mpMessage, r.X + 12 * s, r.Y + 7 * s, 16, Gui.Gold);
        }
    }
}
