using System.Net;
using Burger.Core;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Multiplayer plumbing: hosting, joining, pumping the session each frame, rebinding the view when the game is replaced.</summary>
public sealed partial class App
{
    private readonly MpController _mpc = new();
    public LockstepSession? Mp => _mpc.Session;
    public List<string> MpChat => _mpc.Log;
    private string _joinCode = "", _mpMessage = "";
    public MpController Mpc => _mpc;
    private bool _mpWired;
    private string Project => Path.GetFileName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\'));

    private void WireController()
    {
        if (_mpWired) return;
        _mpWired = true;
        _mpc.Toast += t => Ui.SetStatus(t);
        _mpc.GameReplaced += g => _pending = () => AdoptGame(g);
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
        WireController();
        MpStop();
        int seed = Environment.TickCount & 0x7fffffff;
        LoadGame(keepMap: true, seed: seed);
        if (Game == null) return;
        Game.StartNewGame(null);
        var relayCfg = RelayConfig.Load();
        var kind = TransportSelector.ForHost(SteamService.Init(), relayCfg != null);
        bool steam = kind == TransportKind.SteamSdr;
        // with Steam the internet path is Steam's relay network: no router change, no address in the code; otherwise fall back to router and relay
        _mpc.Host(Game, Project, seed, UserSettings.EffectiveName, openRouter: !steam && UserSettings.OpenRouter, relay: kind == TransportKind.Relay ? relayCfg : null);
        if (steam)
        {
            try { var sh = new SteamHost(); _mpc.UsePlatform(sh, sh.Code, "Online through Steam"); }
            catch (Exception e) { Burger.Core.Log.Warn($"Steam hosting failed ({e.GetType().Name})", "multiplayer"); }
        }
        EnterPlaying();
        if (tag != null) Mp!.Pick(tag);
        Ui.SetStatus("Hosting. Invite players from the Multiplayer panel (paused: press Space to start)");
    }

    public void JoinMultiplayer(string code, string name)
    {
        WireController();
        Func<string, int, Game> load = (project, seed) =>
        {
            if (project != Project) throw new InvalidDataException($"this machine has '{Project}', the host plays '{project}'");
            var g = Game.Load(_gameRoot, compileScripts: true, seed: seed);
            g.StartNewGame(null);
            return g;
        };
        if (TransportSelector.ForCode(code) == TransportKind.SteamSdr && Burger.Core.Multiplayer.SteamCode.TryParse(code, out ulong hostId, out var secret))
        {
            if (!SteamService.Init()) { _mpMessage = SteamService.Problem; return; }
            try { _mpc.JoinLink(SteamJoin.Connect(hostId, secret), name, load); _mpMessage = _mpc.Message; }
            catch (Exception e) { _mpMessage = e is InvalidDataException ? e.Message : "Cannot connect through Steam."; }
            return;
        }
        else _mpc.Join(code, name, load);
        _mpMessage = _mpc.Message;
    }

    public void MpStop() => _mpc.Stop();

    private bool _codePrinted;

    private void MpPump(float dt)
    {
        if (_mpc.Session == null) return;
        if (_opt.HostTag != null && !_codePrinted && _mpc.InternetCode.Length > 0) { _codePrinted = true; Console.WriteLine("host: internet " + _mpc.InternetCode); }
        if (!_mpc.Pump(dt))
        {
            _mpMessage = _mpc.Message;
            if (_screen == AppScreen.Playing) { _menuPage = MenuPage.Multiplayer; _pending = () => LoadGame(keepMap: true); }
        }
        else if (_mpc.Message != "" ) _mpMessage = _mpc.Message;
    }
}
