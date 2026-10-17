using System.Numerics;
using Burger.Core;
using Burger.Core.Figures;
using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Save;
using Raylib_cs;

namespace Burger.Runtime;

public enum AppScreen { Menu, Playing, Error }

/// <summary>Window, main loop, main menu and game loading.</summary>
public sealed partial class App
{
    public Game? Game { get; private set; }
    private readonly List<Burger.Core.Model.Location> _geoWar = new();
    public MapRenderer? Renderer { get; private set; }
    public CameraController? Camera { get; private set; }
    public MapOverlay? Overlay { get; private set; }
    public MapFigures? Figures { get; private set; }
    public MapSites? Sites { get; private set; }
    public MapParticles? Particles { get; private set; }
    public UiState Ui { get; private set; } = new();
    private Hud? _hud;

    private readonly AppOptions _opt;
    private string _gameRoot = "";
    private AppScreen _screen = AppScreen.Menu;
    private string _errorMessage = "";
    private Country? _menuSelection;
    /// <summary>The start date picked on the start screen (null = the first start date); the game is reloaded with the history up to it.</summary>
    private GameDate? _menuDate;
    private bool _menuDateDirty;
    private bool _quit;
    private int _frame;
    private Action? _pending;
    private RenderPerf? _renderPerf;
    private Bench? _bench;

    public App(AppOptions options) { _opt = options; }

    public string GameRoot => _gameRoot;
    public string SavesDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\')) ?? ".", "saves");
    public string LogsDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\')) ?? ".", "logs");

    public void Run()
    {
        _gameRoot = _opt.GameRoot;
        var flags = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint;
        if (Environment.GetEnvironmentVariable("BURGER_NO_MSAA") == "1") flags &= ~ConfigFlags.Msaa4xHint; // fullscreen test: 4x MSAA framebuffer resize
        if (_opt.Bench) flags &= ~ConfigFlags.VSyncHint;
        if (_opt.Hidden) flags |= ConfigFlags.HiddenWindow;
        Raylib.SetConfigFlags(flags);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(_opt.Width, _opt.Height, "BurgerEngine");
        Raylib.SetWindowMinSize(1024, 640);
        int monitor = _opt.Monitor ?? (int.TryParse(Environment.GetEnvironmentVariable("BURGER_MONITOR"), out var em) ? em : -1);
        if (monitor >= 0 && monitor < Raylib.GetMonitorCount())
        {
            // Wayland windows cannot place themselves, so the window goes full screen on that monitor
            Raylib.SetWindowMonitor(monitor);
        }
        Raylib.SetExitKey(KeyboardKey.Null);
        UserSettings.Load();
        Media.AudioEnabled = UserSettings.PortraitSound;
        Raylib.SetTargetFPS(_opt.Bench ? 0 : 120);
        // raylib defaults to near 0.01 / far 1000, which clips the far rows of the 1638-unit map; widen the range (set before any BeginMode3D)
        Rlgl.SetClipPlanes(0.5, 6000.0);
        Assets.Font = Raylib.GetFontDefault();
        if (_opt.Perf || _opt.Bench) _renderPerf = new RenderPerf();
        if (_opt.Bench) { _bench = new Bench(); _opt.Observe = true; }

        if (_opt.StartDate is { } sd && sd.TotalDays != 0) _menuDate = sd;
        LoadGame();
        if (Game != null && _opt.LoadSave != null) DoLoadSave(_opt.LoadSave);
        else if (Game != null && _opt.HostTag != null) { HostMultiplayer(_opt.HostTag); Console.WriteLine("host: lan " + _mpc.LanCode); if (!SteamService.Available) { Console.WriteLine("host: steam off: " + SteamService.Problem); foreach (var l in SteamService.LogLines) Console.WriteLine("steam log: " + l); } }
        else if (Game != null && _opt.JoinCode != null) { _menuPage = MenuPage.Join; _joinCode = _opt.JoinCode; JoinMultiplayer(_joinCode, UserSettings.EffectiveName); }
        else if (Game != null && (_opt.Tag != null || _opt.Observe)) StartGame(_opt.Observe ? null : _opt.Tag);
        _perfDay = Game?.World.Date.TotalDays ?? 0;

        while (!_quit && !Raylib.WindowShouldClose())
        {
            if (_pending != null)
            {
                var action = _pending;
                _pending = null;
                action();
            }
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            if (Game != null) Gui.CheckTheme(Game.Interface.Theme);
            Gui.Scale = Gui.ScaleFor(Raylib.GetScreenHeight());
            Gui.BeginFrame();
            SteamService.Pump();
            MpPump(dt);
            switch (_screen)
            {
                case AppScreen.Error: FrameError(); break;
                case AppScreen.Menu: FrameMenu(dt); break;
                case AppScreen.Playing: FramePlaying(dt); break;
            }
            _frame++;
            Media.EndFrame();
            if (_opt.Perf) { PerfSample(); if (Game != null && Game.Started && _opt.Speed > 0) Game.Clock.Paused = false; }
            if (_opt.Screenshot != null && _frame == _opt.ScreenshotFrame)
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, _opt.Screenshot);
                Raylib.UnloadImage(img);
                _quit = true;
            }
        }
        Renderer?.Dispose();
        Sites?.Dispose();
        Figures?.Dispose();
        Particles?.Dispose();
        Media.Shutdown();
        MpStop();
        SteamService.Shutdown();
        Raylib.CloseWindow();
    }

    // frame timing (--perf): average frame time, simulation time per frame and in-game days per second
    private double _perfTime, _perfFrames, _perfSim;
    private int _perfDay;
    private void PerfSample()
    {
        _perfTime += Raylib.GetFrameTime();
        _perfFrames++;
        _perfSim += Game?.Clock.LastUpdateMs ?? 0;
        if (_perfTime < 10) return;
        int day = Game?.World.Date.TotalDays ?? 0;
            Log.Info($"PERF fps {_perfFrames / _perfTime:0.0}, frame {_perfTime * 1000 / _perfFrames:0.0} ms, simulation {_perfSim / _perfFrames:0.0} ms/frame, {(day - _perfDay) / _perfTime:0.0} days/s");
            if (_renderPerf != null)
                Log.Info("PERF render " + _renderPerf.Report(_perfTime, _perfSim, _perfFrames));
            if (Game != null)
        {
            int days = Math.Max(1, day - _perfDay);
            var profile = Game.ProfileSnapshot();
            Log.Info("PERF systems (ms/day): " + string.Join(", ", profile.Where(kv => !kv.Key.StartsWith("Path.")).OrderByDescending(kv => kv.Value.TotalMilliseconds).Select(kv => $"{kv.Key} {kv.Value.TotalMilliseconds / days:0.00}ms/{kv.Value.Calls / (double)days:0.0} calls max={kv.Value.MaxMilliseconds:0.00} p99={kv.Value.P99Milliseconds:0.00} worst={string.Join('/', kv.Value.WorstDays)}")));
            Log.Info("PERF paths (per day): " + string.Join(", ", profile.Where(kv => kv.Key.StartsWith("Path.")).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.TotalMilliseconds / days:0.00}ms/{kv.Value.Calls / (double)days:0.0} calls")));
            Log.Info($"PERF world: {Game.World.Divisions.Count} divisions, {Game.World.Armies.Count} armies, {Game.World.Wars.Count} wars, {Game.World.Battles.Count} battles");
            Game.ClearProfile();
            Game.Profiling = true;
        }
        _perfDay = day;
        _perfTime = _perfFrames = _perfSim = 0;
        _renderPerf?.Clear();
    }

    public void RequestQuit() => _quit = true;

    // ===============================================================================================
    // Loading
    // ===============================================================================================
    private void DrawLoading(string text)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(16, 16, 20, 255));
        string t = text;
        int fs = 28;
        int w = Raylib.MeasureText(t, fs);
        Raylib.DrawText(t, (Raylib.GetScreenWidth() - w) / 2, Raylib.GetScreenHeight() / 2 - fs, fs, new Color(232, 196, 90, 255));
        Raylib.EndDrawing();
    }

    private void LoadGame(bool keepMap = false, Burger.Core.Game? adopt = null, int? seed = null)
    {
        DrawLoading("Loading...");
        string? keepTag = _menuSelection?.Tag;
        var keptRenderer = keepMap ? Renderer : null;
        if (keptRenderer == null) Renderer?.Dispose();
        Renderer = null;
        Sites?.Dispose();
        Sites = null;
        Figures?.Dispose();
        Figures = null;
        Particles?.Dispose();
        Particles = null;
        Game = null;
        Ui = new UiState();
        Log.Clear();
        // Launchers run from the repository root, making this a predictable shared
        // location for engine diagnostics and --perf output. --log-dir overrides it.
        Log.SetLogFile(Path.Combine(_opt.LogDir ?? Path.Combine(Directory.GetCurrentDirectory(), "logs"), "error.log"));
        try
        {
            Action<Burger.Core.Game> prewarm = MapRenderer.Prewarm;
            if (keptRenderer == null) Burger.Core.Game.MapLoaded += prewarm; // the renderer's CPU work runs while the rest of the game loads
            Burger.Core.Game game;
            try { game = adopt ?? Burger.Core.Game.Load(_gameRoot, compileScripts: true, seed: seed, startDate: seed == null ? _menuDate : null); }
            finally { Burger.Core.Game.MapLoaded -= prewarm; }
            Game = game;
            if (adopt == null && seed == null) // multiplayer games use the same settings on every machine
            {
                game.HistoricalBias = _opt.Bias ?? UserSettings.EffectiveBias;
                game.Ai.Enabled = !UserSettings.AiDisabled;
            }
            Raylib.SetWindowTitle(game.Data.Info.Name);
            if (!string.IsNullOrEmpty(game.Data.Info.Icon))
            {
                string icon = game.Data.PathOf(game.Data.Info.Icon);
                if (File.Exists(icon))
                {
                    var img = Raylib.LoadImage(icon);
                    Raylib.SetWindowIcon(img);
                    Raylib.UnloadImage(img);
                }
            }
            Assets.Init(game.Data.Root, game.Interface.Theme);
            Gui.ApplyTheme(game.Interface.Theme);
            DrawLoading(keptRenderer != null ? "Loading..." : "Building map...");
            UserSettings.GlobeAllowed = game.Data.Info.GlobeAllowed && game.World.Map.Settings.WholeGlobe; // the globe needs the whole planet
            if (keptRenderer != null && keptRenderer.CanRebind(game)) { Renderer = keptRenderer; Renderer.Rebind(game); keptRenderer = null; }
            else { keptRenderer?.Dispose(); keptRenderer = null; var rsw = System.Diagnostics.Stopwatch.StartNew(); Renderer = MapRenderer.Create(game); Log.Info($"Map renderer built in {rsw.ElapsedMilliseconds} ms"); }
            Camera = new CameraController(Renderer);
            Overlay = new MapOverlay(game, Renderer);
            Particles = new MapParticles(game, Renderer);
            Figures = new MapFigures(game, Renderer) { Particles = Particles };
            Sites = new MapSites(game, Renderer);
            game.Commands.Register("geodate", "geodate <date|clear> - date shown by the Provinces map mode, e.g. geodate 1CE.1.1 or 300BCE.6.1", args =>
            {
                if (!game.Geo.Enabled) return "This project has no history/geography";
                if (args.Length == 0 || args[0] == "clear") { Renderer!.GeoDay = null; Renderer.Dirty = true; return "Provinces map mode follows the game date"; }
                if (!Burger.Core.Geography.HistoricalCalendar.TryParse(args[0], out int day, out var err)) return err ?? "bad date";
                Renderer!.GeoDay = day; Renderer.Dirty = true;
                return $"Provinces map mode shows {Burger.Core.Geography.HistoricalCalendar.Format(day)}: {game.Geo.Snapshot(day).Provinces.Count} provinces";
            });
            game.Commands.Register("geodebug", "geodebug [on|off] - Provinces map mode highlights dynamic provinces: orange = front line, cyan = growth split, yellow = merge; gray = same shape as at the start", args =>
            {
                Renderer!.GeoDebug = args.Length > 0 ? args[0] != "off" : !Renderer.GeoDebug;
                if (Renderer.GeoDebug) Renderer.Mode = MapMode.Provinces;
                Renderer.Dirty = true;
                foreach (var op in game.Geo.DynamicOperations.Take(8)) Console.WriteLine($"geodebug: {Burger.Core.Geography.HistoricalCalendar.Format(op.Date)} {op.Kind} {string.Join("+", op.Parents)} -> {string.Join(",", op.Children.Select(c => c.Id + "/" + c.Atoms.Length))} [{op.Reason}] provinceReason={(game.Geo.Get(op.Children[0].Id)?.Reason ?? "null")}");
                return Renderer.GeoDebug ? "Geo debug on: orange front line, cyan growth split, yellow merge, gray from data files" : "Geo debug off";
            });
            game.Commands.Register("geobase", "geobase [on|off] - overlay the start-layout province borders (magenta) above the current province shapes in the Provinces map mode", args =>
            {
                Renderer!.GeoBase = args.Length > 0 ? args[0] != "off" : !Renderer.GeoBase;
                if (Renderer.GeoBase) Renderer.Mode = MapMode.Provinces;
                Renderer.Dirty = true;
                return Renderer.GeoBase ? "Initial shapes on: magenta lines are the province borders from the start" : "Initial shapes off";
            });
            game.Commands.Register("geolist", "geolist [n] - the last n (default 15) dynamic province changes with date, parents, children and reason", args =>
            {
                int n = args.Length > 0 && int.TryParse(args[0], out var k) ? k : 15;
                var ops = game.Geo.DynamicOperations;
                if (ops.Count == 0) return "No dynamic changes yet (geoskip advances time)";
                return $"{ops.Count} dynamic changes ({game.GeoDynamics.Splits} splits, {game.GeoDynamics.Merges} merges), last {Math.Min(n, ops.Count)}:\n" +
                    string.Join("\n", ops.Skip(Math.Max(0, ops.Count - n)).Select(o => $"  {Burger.Core.Geography.HistoricalCalendar.Format(o.Date)} {o.Kind}: {string.Join(" + ", o.Parents)} -> {string.Join(", ", o.Children.Select(c => c.Name ?? c.Id))} [{o.Reason}]"));
            });
            game.Commands.Register("geosplit", "geosplit <population> - test: set the population above which a province splits (default 400000); lower it to see growth splits", args =>
            {
                if (args.Length == 0 || !double.TryParse(args[0], out var v) || v <= 0) return $"Split population is {game.Geo.DynamicSettings.SplitPopulation:N0}. Usage: geosplit 100000";
                game.Geo.DynamicSettings.SplitPopulation = v;
                return "Split population is now " + game.GeoDynamics.Diagnose();
            });
            game.Commands.Register("geowar", "geowar [n] - test: occupy n border locations (default 60) so fronts form; geowar off ends it", args =>
            {
                foreach (var l in _geoWar) l.Controller = null;
                _geoWar.Clear();
                if (args.Length > 0 && args[0] == "off") { Renderer!.Dirty = true; return "Test occupation ended"; }
                int n = args.Length > 0 && int.TryParse(args[0], out var k) ? k : 60;
                var border = game.World.Locations.Where(l => l.IsLand && l.Owner != null && l.Neighbors.Any(x => x.To.IsLand && x.To.Owner != null && x.To.Owner != l.Owner)).OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
                int step = Math.Max(1, border.Count / Math.Max(1, n));
                for (int i = 0; i < border.Count && _geoWar.Count < n; i += step)
                {
                    var foe = border[i].Neighbors.Select(a => a.To.Owner).First(o => o != null && o != border[i].Owner);
                    border[i].Controller = foe; _geoWar.Add(border[i]);
                }
                Renderer!.Dirty = true;
                return $"{_geoWar.Count} locations occupied; fronts become provinces at the next month start (use geoskip)";
            });
            game.Commands.Register("geoskip", "geoskip [n|ny] - run the simulation forward n months (default 1) or n years (e.g. 5y) and report province changes", args =>
            {
                int months = 1;
                if (args.Length > 0)
                {
                    string a0 = args[0].ToLowerInvariant();
                    bool years = a0.EndsWith('y');
                    if (int.TryParse(years ? a0[..^1] : a0, out var m) && m > 0) months = years ? m * 12 : m;
                }
                int s0 = game.GeoDynamics.Splits, m0 = game.GeoDynamics.Merges;
                for (int i = 0; i < months; i++) do game.Tick(); while (!(game.World.Date.Day == 1 && game.World.Date.Hour == 0));
                Renderer!.Dirty = true;
                Console.WriteLine("geoskip: " + game.GeoDynamics.Diagnose());
                Console.WriteLine($"geoskip: {game.GeoDynamics.Splits} splits, {game.GeoDynamics.Merges} merges, {game.GeoDynamics.Shifts} border shifts so far; {_geoWar.Count} locations occupied");
                return $"Now {Burger.Core.Geography.HistoricalCalendar.Format(game.World.Date.TotalDays)}: +{game.GeoDynamics.Splits - s0} splits, +{game.GeoDynamics.Merges - m0} merges, borders moved {game.GeoDynamics.Shifts}, {game.Geo.Snapshot(game.World.Date.TotalDays).Provinces.Count} provinces, {game.GeoDynamics.ChangedCount()} with a shape different from the start";
            });
            game.Commands.Register("soldier_gag", "soldier_gag [scratch|pick_nose|scratch_pick_nose|fire|fire_side|fire_aggressive|off] - standing soldiers do a silly idle", args =>
            {
                string name = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                if (name is "" or "off" or "none") { MapFigures.StandingGag = null; return "Soldiers stand normally"; }
                if (!HumanMale.Gags.Contains(name) && !name.StartsWith(HumanMale.Fire)) return "Unknown gag. Try: " + string.Join(", ", HumanMale.Gags) + ", " + HumanMale.Fire + ", off";
                MapFigures.StandingGag = name;
                return "Standing soldiers: " + name;
            });
            game.Commands.Register("particle", "particle [name] [count] - shows an effect of common/particles.txt at the camera target (no name: lists them)", args =>
            {
                if (args.Length == 0) return string.Join(", ", game.Particles.All.Select(p => p.Key).OrderBy(k => k));
                if (Particles == null) return "No map";
                if (game.Particles.Find(args[0]) == null) return "Unknown effect. Try: " + string.Join(", ", game.Particles.All.Select(p => p.Key).OrderBy(k => k));
                int n = args.Length > 1 && int.TryParse(args[1], out var c) ? Math.Clamp(c, 1, 50) : 1;
                for (int i = 0; i < n; i++) game.Particles.Request(args[0]);
                return $"{args[0]} x{n}";
            });
            _hud = new Hud(this);
            _menuSelection = game.World.GetCountry(keepTag) is { Exists: true } kept ? kept : game.World.GetCountry(game.Data.Info.DefaultTag) is { Exists: true } def ? def : game.World.Countries.Where(c => c.Playable && c.Exists).OrderByDescending(c => c.OwnedStates.Count).FirstOrDefault();
            if (_menuSelection?.Capital != null) Camera.LookAt(Renderer.LocationPos(_menuSelection.Capital), Camera.MaxDistance * 0.5f);
            _screen = AppScreen.Menu;
            if (_opt.Open == "play") _menuPage = MenuPage.Play; // --open play: the start screen of a new game (screenshots)
        }
        catch (Exception ex)
        {
            _errorMessage = ex is GameLoadException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            Log.Error("Game failed to load: " + ex.Message);
            Game = null;
            _screen = AppScreen.Error;
        }
    }

    private void StartGame(string? tag)
    {
        if (Game == null) return;
        Game.Exploration.SetEnabled(tag != null && UserSettings.TerraIncognita && Game.Exploration.Allowed);
        Game.StartNewGame(tag);
        EnterPlaying();
    }

    private void EnterPlaying()
    {
        if (Game == null || Renderer == null || Camera == null) return;
        _screen = AppScreen.Playing;
        Game.Clock.Paused = true;
        Renderer.DiplomacyFocus = null;
        Renderer.Dirty = true;
        var p = Game.World.Player;
        if (p?.Capital != null) Camera.LookAt(Renderer.LocationPos(p.Capital), 45);
        foreach (var cmd in _opt.Exec) Log.Info($"exec '{cmd}': {Game.Commands.Execute(cmd)}");
        _opt.Exec.Clear();
        for (int i = 0; i < _opt.TicksBeforeScreenshot; i++) Game.Tick();
        if (_opt.Speed > 0) { Game.Clock.Speed = Math.Clamp(_opt.Speed, 1, 5); Game.Clock.Paused = false; }
        ApplyTestOptions();
    }

    /// <summary>Command line automation (used for testing and screenshots).</summary>
    private void ApplyTestOptions()
    {
        var game = Game!;
        foreach (var cmd in _opt.Exec)
        {
            string result = game.Commands.Execute(cmd);
            Log.Info($"exec '{cmd}': {result}");
        }
        _opt.Exec.Clear();
        if (_opt.MapMode != null && Enum.TryParse<MapMode>(_opt.MapMode, true, out var mode)) { Renderer!.Mode = mode; Renderer.Dirty = true; }
        if (_opt.LookAt != null && Renderer != null && !_opt.LookAt.Contains(',') && game.World.GetLocation(_opt.LookAt) is { } lookLoc)
            Camera!.LookAt(Renderer.LocationPos(lookLoc), _opt.Zoom ?? 40f);
        else if (_opt.LookAt != null && Renderer != null)
        {
            var ll = _opt.LookAt.Split(',');
            double lon = double.Parse(ll[0], System.Globalization.CultureInfo.InvariantCulture), lat = double.Parse(ll[1], System.Globalization.CultureInfo.InvariantCulture);
            var mp = Renderer;
            Camera!.LookAtFlat((float)((lon + 180) / 360 * mp.WorldWidth), (float)((90 - lat) / 180 * mp.WorldHeight), _opt.Zoom ?? 40f);
        }
        else if (_opt.Zoom != null) Camera!.LookAt(Camera.Target, _opt.Zoom.Value);
        if (_opt.NoFogShading) MapRenderer.FogShading = false;
        if (_opt.Open != null)
        {
            var parts = _opt.Open.Split(':');
            switch (parts[0].ToLowerInvariant())
            {
                case "military": Ui.Left = LeftPanel.Military; break;
                case "recruit": Ui.Left = LeftPanel.Recruit; Ui.RecruitTemplate = game.Data.Templates.Values.FirstOrDefault(); break;
                case "characters": Ui.Left = LeftPanel.Characters; break;
                case "decisions": Ui.Left = LeftPanel.Decisions; break;
                case "production": Ui.Left = LeftPanel.Production; Ui.ProductionTab = int.TryParse(parts.ElementAtOrDefault(1), out var pt) ? pt : 0; break;
                case "designer": Ui.Left = LeftPanel.Designer; break;
                case "air": Ui.Left = LeftPanel.Air; _hud!.AirTab = int.TryParse(parts.ElementAtOrDefault(1), out var at) ? at : 0; break;
                case "population": Ui.Left = LeftPanel.Population; break;
                case "ledger": // ledger[:world][:metric]
                    Ui.Right = RightPanel.Ledger;
                    _hud!.LedgerWorld = parts.Contains("world");
                    if (parts.Length > 1 && parts[^1] != "world") _hud.LedgerMetric = parts[^1];
                    break;
                case "politics": Ui.Left = LeftPanel.Politics; _hud!.PoliticsTab = int.TryParse(parts.ElementAtOrDefault(1), out var pt2) ? pt2 : 0; break;
                case "navy": Ui.Left = LeftPanel.Navy; _hud!.NavyTab = int.TryParse(parts.ElementAtOrDefault(1), out var nt) ? nt : 0; break;
                case "wars": Ui.Right = RightPanel.Wars; break;
                case "world": Ui.Right = RightPanel.World; break;
                case "errors": Ui.ErrorsOpen = true; break;
                case "editor": Ui.EditorActive = true; Renderer!.Mode = MapMode.States; break;
                case "console": Ui.ConsoleOpen = true; break;
                case "country":
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    break;
                case "declare_war": // declare_war:TAG - the casus belli window
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    if (Ui.SelectedCountry != null) _hud!.OpenSupportFromOutside("declare_war", Ui.SelectedCountry);
                    break;
                case "support": // support:TAG:volunteers|lend_lease
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    if (Ui.SelectedCountry != null) _hud!.OpenSupportFromOutside(parts.ElementAtOrDefault(2) ?? "lend_lease", Ui.SelectedCountry);
                    break;
                case "location":
                    var l = game.World.GetLocation(parts.ElementAtOrDefault(1)) ?? game.World.Player?.Capital;
                    Ui.SelectedLocation = l;
                    Renderer!.Selected = l;
                    Ui.Left = LeftPanel.Location;
                    foreach (var f in (parts.ElementAtOrDefault(2) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) _hud!.UnfoldLocation(f);   // location:<key>:fold,fold
                    break;
                case "peace":
                    var p = game.World.Player;
                    var war = p == null ? null : game.World.WarsOf(p).FirstOrDefault();
                    if (war != null) _hud!.OpenPeaceFromOutside(war, war.EnemyLeaderOf(p!));
                    break;
            }
        }
        if (_opt.Screenshot != null) game.Events.PlayerQueue.Clear(); // keep test screenshots clean
    }

    /// <summary>Reloads all game files and shows the main menu (runs between frames).</summary>
    /// <summary>Back to the main menu with a fresh world. The map's terrain, textures and shader are kept, only the game is read again.</summary>
    public void ReturnToMainMenu() => _pending = () => { MpStop(); _menuPage = MenuPage.Home; LoadGame(keepMap: true); };

    /// <summary>Reads every game file again and rebuilds the map too (the Reload files button).</summary>
    public void ReloadFiles() => _pending = () => LoadGame();

    /// <summary>Loads a save (runs between frames).</summary>
    public void LoadSave(string path) => _pending = () => DoLoadSave(path);

    // ===============================================================================================
    // Saves
    // ===============================================================================================
    // The menu asks every frame; scanning the folder and opening each save to read its header that often made it crawl.
    private List<string>? _saveListCache;
    private double _saveListTime;
    private readonly Dictionary<string, (DateTime Stamp, string Label)> _saveLabels = new();

    public List<string> ListSaves()
    {
        double now = Raylib.GetTime();
        if (_saveListCache != null && now - _saveListTime < 2) return _saveListCache;
        _saveListTime = now;
        if (!Directory.Exists(SavesDir)) return _saveListCache = new List<string>();
        return _saveListCache = Directory.GetFiles(SavesDir, "*.sav").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }

    private string SaveLabel(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_saveLabels.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Label;
        var (_, date, player) = SaveGame.Peek(path);
        string label = $"{Path.GetFileNameWithoutExtension(path)}  ({player}, {date})";
        _saveLabels[path] = (stamp, label);
        return label;
    }

    private static string ShortPath(string path, int max = 56)
    {
        if (path.Length <= max) return path;
        string name = Path.GetFileName(path);
        string dir = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        string tail = dir.Length > 0 ? $"{dir}/{name}" : name;
        return tail.Length + 2 <= max ? ".../" + tail : "..." + name[Math.Max(0, name.Length - (max - 3))..];
    }

    public string SaveAs(string? name)
    {
        if (Game == null || !Game.Started) return "Nothing to save";
        try
        {
            Directory.CreateDirectory(SavesDir);
            _saveListCache = null;
            name ??= $"{Game.World.Player?.Tag ?? "observer"}_{Game.World.Date.Year}-{Game.World.Date.Month:00}-{Game.World.Date.Day:00}_{DateTime.Now:yyyyMMdd-HHmmss}";
            string path = Path.Combine(SavesDir, name + ".sav");
            SaveGame.Save(Game, path);
            return $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Log.Error("Save failed: " + ex.Message);
            return "Save failed: " + ex.Message;
        }
    }

    public string QuickSave()
    {
        string r = SaveAs("quicksave");
        Ui.SetStatus(r);
        return r;
    }

    private void DoLoadSave(string path)
    {
        _menuDate = null;
        LoadGame(keepMap: true);
        if (Game == null) return;
        try
        {
            SaveGame.Load(Game, path);
            Renderer!.Dirty = true;
            EnterPlaying();
            Ui.SetStatus($"Loaded {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Could not load save '{path}':\n{ex.Message}";
            Log.Error(_errorMessage);
            _screen = AppScreen.Error;
        }
    }

    // ===============================================================================================
    // Frames
    // ===============================================================================================
    private void DrawWorld(Camera3D cam)
    {
        Raylib.ClearBackground(Renderer != null && Renderer.Globe ? new Color(2, 3, 9, 255) : new Color(12, 22, 38, 255)); // space behind the globe
        Raylib.BeginMode3D(cam);
        if (!Bench.Skip.Contains("map")) Renderer!.Draw(cam, (float)Raylib.GetTime());
        if (_screen == AppScreen.Playing && Sites != null) Sites.Draw(cam);
        if (_screen == AppScreen.Playing && Figures != null)
        {
            Overlay!.CounterBelowFigure = Figures.Visible(cam);
            float fdt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            Figures.Draw(cam, fdt);
            if (Particles != null) { Particles.Update(fdt, cam); Particles.Draw(cam); }
        }
        Raylib.EndMode3D();
    }

    private void FramePlaying(float dt)
    {
        var game = Game!;
        var map = Renderer!;
        var cam = Camera!;
        _renderPerf?.Begin();
        if (_bench != null && _bench.Step(cam, map, _renderPerf!)) { _quit = true; return; }
        _hud!.HandleKeys();
        // camera input + damped movement are resolved here, before BeginDrawing - never inside the draw loop
        cam.Update(dt, Gui.MouseOverUi, false, !_hud.KeyboardCaptured);
        // Always raycast against the heightfield. The sea-level shortcut used while moving
        // can land hundreds of provinces away on large, high-relief maps such as Earth1930.
        var hovered = Gui.MouseOverUi ? null : PickCached(map, cam);
        if (hovered != map.Hovered) { map.Hovered = hovered; map.Dirty = true; }
        _renderPerf?.Mark(1);

        Raylib.BeginDrawing();
        DrawWorld(cam.Camera);
        _renderPerf?.Mark(2);
        Overlay!.DrawLabels(cam.Camera);
        _renderPerf?.Mark(3);
        Overlay.DrawOrders(cam.Camera, Ui);
        _renderPerf?.Mark(4);
        Overlay.DrawUnits(cam.Camera, Ui, Gui.MouseOverUi);
        _renderPerf?.Mark(5);
        Overlay.DrawFleets(cam.Camera, Ui, Gui.MouseOverUi);
        _renderPerf?.Mark(6);
        if (Overlay.DrawAirBases(cam.Camera, Gui.MouseOverUi)) Ui.Left = LeftPanel.Air;
        _renderPerf?.Mark(7);
        HandleMapLeftMouse(hovered);
        if (!Gui.MouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Right)) _hud.OnMapRightClick(hovered);
        if (hovered != null && !Gui.MouseOverUi) DrawHoverInfo(hovered);
        _renderPerf?.Mark(8);
        if (!Bench.Skip.Contains("ui")) _hud.Draw();
        Gui.EndFrame();
        _renderPerf?.Mark(9);
        Raylib.EndDrawing();
        _renderPerf?.End(Raylib.GetFrameTime() * 1000.0);

        if (Mp != null) return; // the session runs the ticks
        try
        {
            game.Clock.Update(dt);
        }
        catch (Exception ex)
        {
            game.Clock.Paused = true;
            Log.Error($"Simulation error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Ui.SetStatus("Simulation error - game paused (see F2)");
        }
    }

    // Exact heightfield pick, but only re-run when the mouse, camera or window actually changed (a still frame reuses the last answer).
    private Vector2 _pickMouse, _pickScreen, _pickOrigin;
    private Camera3D _pickCam;
    private Location? _pickResult;
    private bool _pickValid;

    private Location? PickCached(MapRenderer map, CameraController cam)
    {
        // Pick() and Viewport.Ray() operate in map-local coordinates. Gui.Mouse is window-global;
        // using it here offsets every pick when the map is hosted below/inside a UI panel.
        var mouse = Viewport.Mouse;
        var screen = new Vector2(Viewport.W, Viewport.H);
        var origin = Viewport.Origin;
        var c = cam.Camera;
        if (_pickValid && mouse == _pickMouse && screen == _pickScreen
            && origin == _pickOrigin
            && c.Position == _pickCam.Position && c.Target == _pickCam.Target && c.Up == _pickCam.Up && c.FovY == _pickCam.FovY)
            return _pickResult;
        // Always use the exact heightfield pick. Caching avoids repeat work on unchanged frames;
        // a coarser movement step would reintroduce province offsets.
        _pickResult = map.Pick(mouse, c);
        _pickMouse = mouse; _pickScreen = screen; _pickOrigin = origin; _pickCam = c; _pickValid = true;
        return _pickResult;
    }

    // ---------------------------------------------------------------------------------------- click or drag-select
    private Vector2? _dragStart;
    private bool _dragging;

    /// <summary>Left mouse on the map: a click selects a location; a drag draws a box that selects your divisions.</summary>
    private void HandleMapLeftMouse(Location? hovered)
    {
        var mouse = Raylib.GetMousePosition();
        if (!Gui.MouseOverUi && !Gui.LeftClickConsumed && Raylib.IsMouseButtonPressed(MouseButton.Left)) { _dragStart = mouse; _dragging = false; }
        if (_dragStart == null) return;
        if (Raylib.IsMouseButtonDown(MouseButton.Left) && Vector2.Distance(mouse, _dragStart.Value) > 6) _dragging = true;
        if (_dragging)
        {
            var r = BoxOf(_dragStart.Value, mouse);
            Raylib.DrawRectangleRec(r, new Color(255, 230, 140, 40));
            Raylib.DrawRectangleLinesEx(r, 1.5f, new Color(255, 230, 140, 220));
        }
        if (!Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            if (_dragging) SelectInBox(BoxOf(_dragStart.Value, mouse));
            else _hud!.OnMapLeftClick(hovered);
            _dragStart = null;
            _dragging = false;
        }
    }

    private static Rectangle BoxOf(Vector2 a, Vector2 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void SelectInBox(Rectangle box)
    {
        var player = Game?.World.Player;
        if (player == null || Overlay == null) return;
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (!shift) Ui.SelectedDivisions.Clear();
        Ui.SelectedArmy = null;
        Ui.SelectedFleet = null;
        foreach (var (rect, owner, divisions) in Overlay.Counters)
        {
            if (owner != player) continue;
            var c = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            if (!Raylib.CheckCollisionPointRec(c, box)) continue;
            foreach (var d in divisions) if (!Ui.SelectedDivisions.Contains(d)) Ui.SelectedDivisions.Add(d);
        }
        Ui.SetStatus(Ui.SelectedDivisions.Count == 0 ? "No divisions of yours in the box" : $"{Ui.SelectedDivisions.Count} division(s) selected - right-click to move");
    }

    private void DrawHoverInfo(Location l)
    {
        bool unknown = Game != null && !Game.Exploration.IsCharted(l);
        string text = unknown ? "Terra incognita" : l.DisplayName;
        if (l.IsLand && !unknown)
        {
            text += l.State != null ? $" ({l.State.DisplayName})" : "";
            if (l.Owner != null) text += $" - {l.Owner.Name}";
            if (l.IsOccupied) text += $" [occupied by {l.Controller!.Name}]";
        }
        Color extraColor = Gui.Good;
        string extra = "";
        if (Ui.Left == LeftPanel.Production && Ui.BuildType != null && Game?.World.Player is { } builder && l.State != null)
        {
            var reason = Game.Production.CannotBuildReason(builder, Ui.BuildType, l.State);
            extra = reason == null ? $"Click to build a {Ui.BuildType.Name} in {l.State.DisplayName}" : $"Cannot build here: {reason}";
            if (reason != null) extraColor = Gui.Bad;
        }
        if (Game != null && Game.Features.HasSupply && Renderer?.Mode == MapMode.Supply && Game.World.Player is { } sp && l.IsLand)
            extra = $"Supply: {Game.Supply.SupplyAt(sp, l):0.0} (a division uses about 0.3 per day)";
        if (Game != null && Game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && Game.World.Player is { } ap && l.IsLand)
        {
            var region = Game.Air.RegionOf(l);
            if (Ui.OrderingWing is { } wing)
            {
                string? reason;
                if (Ui.WingMission is { } mission)
                {
                    reason = region == null ? "No air region here" : Game.Air.CannotAssignReason(wing, region, mission);
                    extra = reason == null ? $"Click: {Burger.Core.Air.AirSystem.MissionName(mission)} over {region!.DisplayName}" : $"Cannot fly here: {reason}";
                }
                else
                {
                    reason = l.State == null ? "No state here" : Game.Air.CannotRebaseReason(wing, l.State);
                    extra = reason == null ? $"Click to move {wing.Name} to {l.State!.DisplayName}" : $"Cannot base here: {reason}";
                }
                if (reason != null) extraColor = Gui.Bad;
            }
            else if (Renderer?.Mode == MapMode.Air && region != null)
            {
                var (share, contested) = Game.Air.Superiority(region, ap);
                extra = $"{region.DisplayName}: " + (contested ? $"your side holds {share:P0} of the sky" : "no air superiority missions") +
                        (l.State != null && l.State.Buildings.GetValueOrDefault("air_base") > 0 ? $"  |  air base {Game.Air.PlanesAt(l.State):0}/{Game.Air.Capacity(l.State):0} planes" : "");
            }
        }
        if (Ui.Tool != PlanTool.None && Ui.PlanArmy is { } planArmy && l.IsLand)
            extra = Ui.Tool switch
            {
                PlanTool.Front => planArmy.FrontLimit.Contains(l) ? "Click to remove from the front" : "Click to add to the front",
                PlanTool.Arrow => $"Click: attack towards {l.DisplayName}",
                PlanTool.PickFront => _hud?.FrontTargetOf(planArmy, l) is { } ft ? (ft == planArmy.Owner ? "Click: remove the front" : $"Click: front against {ft.Name}") : "Pick a neighbouring or enemy country",
                _ => planArmy.Fallback.Contains(l) ? "Click to remove from the fallback line" : "Click to add to the fallback line",
            };
        if (Ui.Left == LeftPanel.Recruit && Ui.RecruitTemplate != null && Game?.World.Player is { } player)
        {
            var reason = Game.Rules.Economy.CannotRecruitReason(Game, player, Ui.RecruitTemplate, l);
            extra = reason == null ? $"Click to train {Ui.RecruitTemplate.Name} here" : $"Cannot train here: {reason}";
            if (reason != null) extraColor = Gui.Bad;
        }
        if (unknown) { extra = l.IsWater ? "Uncharted waters" : "Unexplored by your people"; extraColor = Gui.TextDim; }
        var m = Gui.Measure(text, 15);
        var m2 = extra.Length > 0 ? Gui.Measure(extra, 14) : Vector2.Zero;
        float x = Gui.Mouse.X + 16, y = Gui.Mouse.Y + 18;
        float w = Math.Max(m.X, m2.X), h = m.Y + (extra.Length > 0 ? m2.Y + 2 : 0);
        Raylib.DrawRectangleRec(new Rectangle(x - 6, y - 3, w + 12, h + 6), new Color(10, 10, 14, 200));
        Raylib.DrawRectangleLinesEx(new Rectangle(x - 6, y - 3, w + 12, h + 6), 1, new Color(Gui.PanelInner.R, Gui.PanelInner.G, Gui.PanelInner.B, (byte)220));
        Gui.Label(text, x, y, 15);
        if (extra.Length > 0) Gui.Label(extra, x, y + m.Y + 2, 14, extraColor);
    }

    private void FrameMenu(float dt)
    {
        var game = Game!;
        var map = Renderer!;
        var cam = Camera!;
        cam.Update(dt, Gui.MouseOverUi, false, true);
        var hovered = Gui.MouseOverUi ? null : map.Pick(Gui.Mouse, cam.Camera);
        if (map.Mode != MapMode.Political) { map.Mode = MapMode.Political; map.Dirty = true; }

        Raylib.BeginDrawing();
        DrawWorld(cam.Camera);
        Overlay!.DrawLabels(cam.Camera);
        if (!Gui.MouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Left) && hovered?.Owner != null && hovered.Owner.Playable)
        {
            _menuSelection = hovered.Owner;
            map.Selected = null;
            map.Dirty = true;
        }
        if (hovered != null && !Gui.MouseOverUi) DrawHoverInfo(hovered);

        DrawMenuPages(game, map, cam);
        Gui.EndFrame();
        Raylib.EndDrawing();
    }

    private void DrawMenuErrors()
    {
        float s = Gui.Scale;
        float w = Math.Min(1000 * s, Raylib.GetScreenWidth() - 480 * s), h = Raylib.GetScreenHeight() * 0.7f;
        var r = new Rectangle(460 * s, 30 * s, w, h);
        Gui.Panel(r, "Error log");
        if (Gui.CloseButton(r)) { Ui.ErrorsOpen = false; return; }
        var entries = Log.Entries.Where(e => e.Level != LogLevel.Info).ToList();
        float top = Gui.BeginScroll("menu-errors", new Rectangle(r.X + 8, r.Y + 34 * s, w - 16, h - 42 * s), entries.Count * 19 * s);
        float y = top;
        foreach (var e in entries)
        {
            string t = e.ToString().Replace('\n', ' ');
            if (t.Length > 170) t = t[..167] + "...";
            Gui.Label(t, r.X + 12, y, 14, e.Level == LogLevel.Error ? Gui.Bad : Gui.Gold);
            y += 19 * s;
        }
        Gui.EndScroll();
    }

    private void DrawMenuSaves()
    {
        float s = Gui.Scale;
        float w = 520 * s, h = 460 * s;
        var r = new Rectangle(460 * s, 30 * s, w, h);
        Gui.Panel(r, "Load game");
        if (Gui.CloseButton(r)) { Ui.SaveMenuOpen = false; return; }
        var saves = ListSaves();
        float top = Gui.BeginScroll("menu-saves", new Rectangle(r.X + 10, r.Y + 36 * s, w - 20, h - 46 * s), saves.Count * 40 * s);
        float y = top;
        foreach (var path in saves)
        {
            if (Gui.Button(new Rectangle(r.X + 10, y, w - 34, 34 * s), SaveLabel(path), true, ShortPath(path), false, 15))
            {
                Gui.EndScroll();
                Ui.SaveMenuOpen = false;
                LoadSave(path);
                return;
            }
            y += 40 * s;
        }
        Gui.EndScroll();
    }

    private void FrameError()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(22, 16, 16, 255));
        float s = Gui.Scale;
        float w = Raylib.GetScreenWidth() - 80 * s, h = Raylib.GetScreenHeight() - 80 * s;
        var r = new Rectangle(40 * s, 40 * s, w, h);
        Gui.Panel(r, "The game could not be loaded");
        float pad = 18 * s, y = r.Y + Gui.HeaderHeight + 14 * s;
        y += Gui.Wrapped(_errorMessage, r.X + pad, y, w - 2 * pad, 17, Gui.Bad) + 12 * s;
        Gui.Label($"Game folder: {Path.GetFullPath(_gameRoot)}", r.X + pad, y, 15, Gui.TextDim);
        y += 30 * s;
        var entries = Log.Entries.Where(e => e.Level == LogLevel.Error).TakeLast(30).ToList();
        foreach (var e in entries)
        {
            Gui.Label(e.ToString().Length > 180 ? e.ToString()[..177] + "..." : e.ToString(), r.X + pad, y, 14, Gui.Gold);
            y += 21 * s;
        }
        if (Gui.Button(new Rectangle(r.X + pad, r.Y + h - 62 * s, 300 * s, 44 * s), "Retry (reload files)") || Raylib.IsKeyPressed(KeyboardKey.F5))
            ReloadFiles();
        if (Gui.Button(new Rectangle(r.X + pad + 314 * s, r.Y + h - 62 * s, 180 * s, 44 * s), "Quit")) _quit = true;
        Gui.EndFrame();
        Raylib.EndDrawing();
    }
}
