using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Multiplayer;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The in-game interface. Split over several files (Hud.*.cs) by panel.</summary>
public sealed partial class Hud
{
    private readonly App _app;
    private Game G => _app.Game!;
    private MapRenderer Map => _app.Renderer!;
    private CameraController Cam => _app.Camera!;
    private UiState Ui => _app.Ui;

    private float S => Gui.Scale;
    private float TopH => Gui.TopBarHeight * S;

    public Hud(App app) { _app = app; }

    private static Color C(Rgb c, int a = 255) => new(c.R, c.G, c.B, (byte)a);

    /// <summary>Is a feature (game/features.txt) installed? Menus of missing features are hidden.</summary>
    private bool Has(string feature) => G.Features.Has(feature);

    /// <summary>True while a text field (console) has keyboard focus.</summary>
    public bool KeyboardCaptured => Ui.ConsoleOpen || Gui.TextFocused;

    // ===============================================================================================
    // Keyboard shortcuts
    // ===============================================================================================
    public void HandleKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Grave) || Raylib.IsKeyPressed(KeyboardKey.F12))
        {
            Ui.ConsoleOpen = !Ui.ConsoleOpen;
            _consoleSkipChar = true;
            return;
        }
        if (Ui.ConsoleOpen || Gui.TextFocused) return;
        var clock = G.Clock;
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) SetPace(!clock.Paused, null);
        for (int i = 1; i <= 5; i++)
            if (Raylib.IsKeyPressed(KeyboardKey.Zero + i)) SetPace(false, i);
        if (Raylib.IsKeyPressed(KeyboardKey.KpAdd) || Raylib.IsKeyPressed(KeyboardKey.Equal)) SetPace(null, Math.Min(5, clock.Speed + 1));
        if (Raylib.IsKeyPressed(KeyboardKey.KpSubtract) || Raylib.IsKeyPressed(KeyboardKey.Minus)) SetPace(null, Math.Max(1, clock.Speed - 1));
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleMapMode();
        if (Raylib.IsKeyPressed(KeyboardKey.F) && Has(FeatureSet.FogOfWar)) MapRenderer.FogShading = !MapRenderer.FogShading;
        if (Raylib.IsKeyPressed(KeyboardKey.H) && (Has(FeatureSet.Military) || Has(FeatureSet.Navy))) MapOverlay.ShowUnits = !MapOverlay.ShowUnits;
        if (Raylib.IsKeyPressed(KeyboardKey.G) && G.Focus.Enabled && G.World.Player != null) ToggleLeft(LeftPanel.Focus);
        if (Raylib.IsKeyPressed(KeyboardKey.T) && G.Tech.Enabled && G.World.Player != null) ToggleLeft(LeftPanel.Technology);
        if (G.World.Player == null && Raylib.IsKeyPressed(KeyboardKey.F7)) ToggleObserveUi();
        if (G.World.Player == null && Ui.ObserveUi && Raylib.IsKeyPressed(KeyboardKey.O) && G.Politics.Enabled) ToggleLeft(LeftPanel.Politics);
        if (G.World.Player == null && Ui.ObserveUi && Raylib.IsKeyPressed(KeyboardKey.U) && G.Population.Enabled) ToggleLeft(LeftPanel.Population);
        if (G.World.Player != null)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F1) && Has(FeatureSet.Military)) ToggleLeft(LeftPanel.Military);
            if (Raylib.IsKeyPressed(KeyboardKey.R) && Has(FeatureSet.Military)) ToggleLeft(LeftPanel.Recruit);
            if (Raylib.IsKeyPressed(KeyboardKey.F3) && Has(FeatureSet.Characters)) ToggleLeft(LeftPanel.Characters);
            if (Raylib.IsKeyPressed(KeyboardKey.P) && Has(FeatureSet.Production)) ToggleLeft(LeftPanel.Production);
            if (Raylib.IsKeyPressed(KeyboardKey.N) && Has(FeatureSet.Navy)) ToggleLeft(LeftPanel.Navy);
            if (Raylib.IsKeyPressed(KeyboardKey.O) && G.Politics.Enabled) ToggleLeft(LeftPanel.Politics);
            if (Raylib.IsKeyPressed(KeyboardKey.U) && G.Population.Enabled) ToggleLeft(LeftPanel.Population);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F2)) Ui.ErrorsOpen = !Ui.ErrorsOpen;
        if (Raylib.IsKeyPressed(KeyboardKey.F4) && Has(FeatureSet.Diplomacy)) ToggleRight(RightPanel.Wars);
        if (Raylib.IsKeyPressed(KeyboardKey.F5)) _app.QuickSave();
        if (Raylib.IsKeyPressed(KeyboardKey.F8)) ToggleRight(RightPanel.World);
        if (Raylib.IsKeyPressed(KeyboardKey.F9)) ToggleRight(RightPanel.Ledger);
        if (Raylib.IsKeyPressed(KeyboardKey.F10)) ToggleEditor();
        HandleMechanicKeys();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (Ui.SettingsOpen) { Ui.SettingsOpen = false; Ui.PauseMenuOpen = false; UserSettings.Save(); } // ESC leaves the pause menu, settings included
            else if (Ui.SaveMenuOpen) Ui.SaveMenuOpen = false;
            else if (Ui.ChoosingGeneralFor != null) Ui.ChoosingGeneralFor = null;
            else if (Ui.RecruitTemplate != null && Ui.Left == LeftPanel.Recruit) Ui.RecruitTemplate = null;
            else if (Ui.BuildType != null && Ui.Left == LeftPanel.Production) Ui.BuildType = null;
            else if (Ui.SelectedDivisions.Count > 0) Ui.ClearSelection();
            else if (Ui.SelectedFleet != null) Ui.SelectedFleet = null;
            else if (Ui.OrderingWing != null) Ui.OrderingWing = null;
            else if (Ui.Tool != PlanTool.None) { Ui.Tool = PlanTool.None; Ui.PlanArmy = null; }
            else if (Ui.Right != RightPanel.None) Ui.Right = RightPanel.None;
            else if (Ui.Left != LeftPanel.None) CloseLeft();
            else if (Ui.ErrorsOpen) Ui.ErrorsOpen = false;
            else Ui.PauseMenuOpen = !Ui.PauseMenuOpen;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace))
            foreach (var d in Ui.SelectedDivisions) G.Warfare.StopDivision(d);
    }

    private void ToggleRight(RightPanel p) => Ui.Right = Ui.Right == p ? RightPanel.None : p;

    private void ToggleLeft(LeftPanel p)
    {
        if (Ui.Left == p) CloseLeft();
        else
        {
            Ui.Left = p;
            if (p != LeftPanel.Location && Map.Selected != null) { Map.Selected = null; Map.Dirty = true; }
        }
    }

    private void CloseLeft()
    {
        Ui.Left = LeftPanel.None;
        Ui.RecruitTemplate = null;
        Ui.ChoosingGeneralFor = null;
        if (Map.Selected != null) { Map.Selected = null; Map.Dirty = true; }
    }

    private void CycleMapMode()
    {
        Map.CustomMode = null;
        Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Diplomacy && !Has(FeatureSet.Diplomacy)) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Supply && !G.Features.HasSupply) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Air && !Has(FeatureSet.Air)) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Ideology && !G.Politics.Enabled) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode is MapMode.Population or MapMode.Culture && !G.Population.Enabled) Map.Mode = MapMode.Political;
        if (Map.Mode == MapMode.Religion && !G.Population.ReligionEnabled) Map.Mode = MapMode.Political;
        if (Map.Mode == MapMode.Provinces && !G.Geo.Enabled) Map.Mode = MapMode.Political;
        Map.Dirty = true;
    }

    private void ToggleEditor()
    {
        Ui.EditorActive = !Ui.EditorActive;
        if (Ui.EditorActive)
        {
            G.Clock.Paused = true;
            Map.Mode = MapMode.States;
        }
        else
        {
            _editorBrush = null;
            Map.HighlightState = null;
        }
        Map.Dirty = true;
    }

    // ===============================================================================================
    // Map clicks (called by App when the mouse is not over the UI)
    // ===============================================================================================
    public void OnMapLeftClick(Location? loc)
    {
        if (Ui.EditorActive) { EditorClick(loc); return; }
        // construction mode: clicking one of your states queues the chosen building there
        if (Ui.Left == LeftPanel.Production && Ui.BuildType != null && G.World.Player != null)
        {
            if (loc?.State != null)
            {
                var err = G.Production.QueueBuilding(G.World.Player, Ui.BuildType, loc.State);
                Ui.SetStatus(err != null ? $"Cannot build in {loc.State.DisplayName}: {err}" : $"{Ui.BuildType.Name} queued in {loc.State.DisplayName}");
            }
            return;
        }
        // battle plan drawing (front segment, offensive arrow, fallback line)
        if (Ui.Tool != PlanTool.None && Ui.PlanArmy != null)
        {
            if (loc != null) PlanMapClick(Ui.PlanArmy, loc);
            return;
        }
        // air orders: clicking the map gives the wing a region (with its mission) or a new air base
        if (Ui.OrderingWing != null && Has(FeatureSet.Air))
        {
            if (loc != null) WingMapOrder(Ui.OrderingWing, loc);
            return;
        }
        // recruit mode: clicking one of your locations trains the chosen template there
        if (Ui.Left == LeftPanel.Recruit && Ui.RecruitTemplate != null && G.World.Player != null)
        {
            if (loc != null) RecruitAt(G.World.Player, Ui.RecruitTemplate, loc);
            return;
        }
        Ui.SelectedDivisions.Clear();
        Ui.SelectedArmy = null;
        Ui.SelectedFleet = null;
        Map.Selected = loc;
        Map.Dirty = true;
        Ui.SelectedLocation = loc;
        if (loc == null) { if (Ui.Left is LeftPanel.Location or LeftPanel.Country) Ui.Left = LeftPanel.None; return; }
        if (Ui.Right == RightPanel.Peace && Ui.Peace != null && loc.State != null)
        {
            TogglePeaceState(loc.State);
            return;
        }
        Ui.Left = LeftPanel.Location;
    }

    public void OnMapRightClick(Location? loc)
    {
        if (Ui.EditorActive) return;
        if (Ui.RecruitTemplate != null) { Ui.RecruitTemplate = null; Ui.SetStatus("Recruiting cancelled"); return; }
        if (Ui.BuildType != null) { Ui.BuildType = null; Ui.SetStatus("Construction mode ended"); return; }
        if (Ui.OrderingWing != null) { Ui.OrderingWing = null; Ui.SetStatus("Air order cancelled"); return; }
        if (Ui.Tool != PlanTool.None) { Ui.Tool = PlanTool.None; Ui.PlanArmy = null; Ui.SetStatus("Done drawing"); return; }
        if (loc == null) return;
        if (Ui.SelectedFleet != null && Has(FeatureSet.Navy))
        {
            var f = Ui.SelectedFleet;
            if (!G.Navy.Fleets.Contains(f)) { Ui.SelectedFleet = null; return; }
            if (f.Battle != null) Ui.SetStatus($"{f.Name} is in battle");
            else if (G.Navy.OrderMove(f, loc)) Ui.SetStatus($"{f.Name} sailing to {loc.DisplayName}");
            else Ui.SetStatus(loc.IsLand ? $"{f.Name} cannot dock at {loc.DisplayName} (not a friendly port or no sea route)" : $"No sea route to {loc.DisplayName}");
            return;
        }
        if (Ui.SelectedDivisions.Count > 0)
        {
            int ok = 0;
            bool queue = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            if (Mp != null)
            {
                var mine = Ui.SelectedDivisions.Where(d => IsMine(d.Owner)).ToList();
                MpMove(mine, loc, queue);
                Ui.SetStatus(mine.Count > 0 ? $"{mine.Count} division(s) ordered to {loc.DisplayName}" : "Not your divisions");
                return;
            }
            foreach (var d in Ui.SelectedDivisions)
            {
                if (d.Army != null && d.Army.FrontEnemy != null) d.FrontPost = null;
                if (queue ? G.Warfare.QueueMove(d, loc) : G.Warfare.OrderMove(d, loc)) ok++;
            }
            Ui.SetStatus(ok > 0 ? $"{ok} division(s) {(queue ? "will then go on to" : "moving to")} {loc.DisplayName}" : $"No path to {loc.DisplayName}");
        }
        else if (Ui.SelectedArmy != null)
        {
            if (Mp != null) MpMove(Ui.SelectedArmy.Divisions.Where(d => IsMine(d.Owner)).ToList(), loc, false);
            else G.Warfare.MoveArmy(Ui.SelectedArmy, loc);
            Ui.SetStatus($"{Ui.SelectedArmy.Name} moving to {loc.DisplayName}");
        }
        else if (loc.Owner != null)
        {
            Ui.SelectedCountry = loc.Owner;
            Ui.Left = LeftPanel.Country;
        }
    }

    // ===============================================================================================
    // Frame
    // ===============================================================================================
    private int _lastDay = -1;

    private MapMode? _modeBeforeCountryView;

    public void Draw()
    {
        // the state of the selected location is highlighted on the map
        if (!Ui.EditorActive)
        {
            var hs = Ui.Left == LeftPanel.Location ? Ui.SelectedLocation?.State : null;
            if (Map.HighlightState != hs) { Map.HighlightState = hs; Map.Dirty = true; }
        }
        // viewing a country: the map shows its territory, allies (blue), enemies (red) and subjects
        bool viewing = Ui.Left == LeftPanel.Country && Ui.SelectedCountry != null && Has(FeatureSet.Diplomacy) && !Ui.EditorActive;
        if (viewing && Map.DiplomacyFocus != Ui.SelectedCountry)
        {
            if (_modeBeforeCountryView == null) _modeBeforeCountryView = Map.Mode;
            Map.DiplomacyFocus = Ui.SelectedCountry;
            Map.Mode = MapMode.Diplomacy;
            Map.CustomMode = null;
            Map.Dirty = true;
        }
        else if (!viewing && _modeBeforeCountryView != null)
        {
            Map.Mode = _modeBeforeCountryView.Value;
            _modeBeforeCountryView = null;
            Map.DiplomacyFocus = null;
            Map.Dirty = true;
        }
        // map modes that change every day (supply) refresh daily
        if (G.World.Date.TotalDays != _lastDay) { _lastDay = G.World.Date.TotalDays; if (Map.Mode is MapMode.Supply or MapMode.Air || (G.World.Date.Day == 1 && Map.Mode is MapMode.Population or MapMode.Culture or MapMode.Religion)) Map.Dirty = true; }
        DrawTopBar();
        if (Ui.EditorActive) DrawEditor();
        else
        {
            DrawLeftMenu();
            switch (Ui.Left)
            {
                case LeftPanel.Location: DrawLocationPanel(); break;
                case LeftPanel.Country: DrawCountryPanel(); break;
                case LeftPanel.Military: DrawMilitaryPanel(); break;
                case LeftPanel.Recruit: DrawRecruitPanel(); break;
                case LeftPanel.Characters: DrawCharactersPanel(); break;
                case LeftPanel.Decisions: DrawDecisionsPanel(); break;
                case LeftPanel.Custom: DrawCustomPanel(); break;
                case LeftPanel.Production: DrawProductionPanel(); break;
                case LeftPanel.Designer: DrawDesignerPanel(); break;
                case LeftPanel.Navy: DrawNavyPanel(); break;
                case LeftPanel.Air: DrawAirPanel(); break;
                case LeftPanel.Politics: DrawPoliticsPanel(); break;
                case LeftPanel.Population: DrawPopulationPanel(); break;
                case LeftPanel.Technology: DrawTechnologyPanel(); break;
                case LeftPanel.Focus: DrawFocusPanel(); break;
            }
            DrawSupportDialog();
        }
        switch (Ui.Right)
        {
            case RightPanel.Wars: DrawWarsPanel(); break;
            case RightPanel.Peace: DrawPeacePanel(); break;
            case RightPanel.World: DrawWorldPanel(); break;
            case RightPanel.Ledger: DrawLedgerPanel(); break;
        }
        DrawMapModes();
        DrawSelectedDivisions();
        DrawMultiplayer();
        DrawNotifications();
        DrawStatus();
        if (Ui.ErrorsOpen) DrawErrorLog();
        DrawProposals();
        DrawEvents();
        if (Ui.ConsoleOpen) DrawConsole();
        if (Ui.SaveMenuOpen) DrawSaveMenu(); else _saveCacheValid = false;
        if (Ui.PauseMenuOpen) DrawPauseMenu();
    }

    // ===============================================================================================
    // Top bar: your country and resources on the left, world-wide things (wars, world, time) on the right
    // ===============================================================================================
    private void DrawTopBar()
    {
        int sw = Raylib.GetScreenWidth();
        var bar = new Rectangle(0, 0, sw, TopH);
        Gui.Block(bar);
        Gui.TopBarBackground(bar);
        float x = 8 * S;
        var player = G.World.Player;
        if (player != null)
        {
            var flagR = new Rectangle(x, 6 * S, 51 * S, 34 * S);
            Gui.Flag(player, flagR);
            if (Gui.Hover(flagR)) Gui.Tooltip("Open your country (diplomacy, ruler)");
            if (Gui.Clicked(flagR)) { Ui.SelectedCountry = player; Ui.Left = LeftPanel.Country; }
            x += 58 * S;
            Gui.LabelShadow(player.Name, x, 3 * S, 18, Gui.Gold, FontKind.Header);
            Gui.Label(player.Ruler != null ? player.Ruler.DisplayName : "", x, 25 * S, 14, Gui.TextDim);
            x += Math.Max(150 * S, Gui.Measure(player.Name, 18, FontKind.Header).X + 16);
            Separator(ref x);
            if (Has(FeatureSet.Economy))
            {
                double net = player.LastIncome - player.LastExpenses;
                Stat(ref x, "Gold", Num(player.Gold), $"{(net >= 0 ? "+" : "")}{Num(net, 1)}",
                    $"Treasury: {player.Gold:0.0}\nIncome: +{player.LastIncome:0.0}/month\nArmy upkeep: -{player.LastExpenses:0.0}/month", net >= 0 ? Gui.Good : Gui.Bad);
                Stat(ref x, "Manpower", Num(player.Manpower), $"+{Num(player.LastManpowerGain)}", "Manpower: used to train and reinforce divisions\nMonthly gain shown in green", Gui.Good);
            }
            if (Has(FeatureSet.Military) && !Has(FeatureSet.Economy))
                Stat(ref x, "Manpower", Num(player.Manpower), $"+{Num(player.LastManpowerGain)}", "Manpower: used to train and reinforce divisions\nMonthly gain shown in green", Gui.Good);
            if (Has(FeatureSet.Production))
            {
                int civ = G.Production.CivilianFactories(player), mil = G.Production.MilitaryFactories(player), dock = G.Production.Dockyards(player);
                Stat(ref x, "Factories", $"{civ} / {mil}" + (dock > 0 ? $" / {dock}" : ""), "",
                    $"Civilian factories: {civ} (construction and consumer goods)\nMilitary factories: {mil} (equipment)" + (dock > 0 ? $"\nDockyards: {dock} (ships)" : "") + "\n\nProduction menu: P", Gui.TextDim);
            }
            if (Has(FeatureSet.Military))
            {
                int divs = G.World.Divisions.Count(d => d.Owner == player);
                Stat(ref x, "Divisions", divs.ToString(), player.Recruitment.Count > 0 ? $"+{player.Recruitment.Count}" : "",
                    $"{divs} divisions, {player.Recruitment.Count} in training\nMilitary menu: F1, recruit: R", Gui.TextDim);
            }
            DrawResources(ref x, player);
        }
        else
        {
            x = DrawObserverTopBar(x, sw - 560 * S);
        }

        // right side: date, speed and the world-wide buttons
        float rx = sw - 8 * S;
        var clock = G.Clock;
        string date = G.World.Date.ToDisplay(G.Clock.Speed <= 1);
        float dateTextW = Gui.Measure(date, 15, FontKind.Bold).X;
        float dw = Math.Max(210 * S, dateTextW + 66 * S);
        rx -= dw;
        var dateR = new Rectangle(rx, 6 * S, dw, 34 * S);
        Gui.ButtonFrame(dateR, true, Gui.Hover(dateR), false);
        if (clock.Paused) Gui.Gradient(new Rectangle(dateR.X + 1, dateR.Y + 1, dateR.Width - 2, dateR.Height - 2), new Color(150, 52, 40, 255), new Color(90, 26, 20, 255));
        Gui.Label(Gui.Fit(date, dateR.Width - 58 * S, 15, FontKind.Bold), dateR.X + 8 * S, dateR.Y + 9 * S, 15, Color.White, FontKind.Bold);
        var stateR = new Rectangle(dateR.X + dateR.Width - 48 * S, dateR.Y + 8 * S, 42 * S, 18 * S);
        var stateColor = clock.Paused ? Gui.Bad : Gui.Good;
        Gui.Gradient(stateR, Gui.Lighter(stateColor, 0.12f), Gui.Darker(stateColor, 0.28f));
        Raylib.DrawRectangleLinesEx(stateR, 1, new Color(0, 0, 0, 180));
        Gui.LabelCentered(clock.Paused ? "PAUSE" : "LIVE", stateR, 10, Color.White, FontKind.Bold);
        if (Gui.Hover(dateR)) Gui.Tooltip($"{date}\n{(clock.Paused ? "Paused" : $"Running at {clock.ActualDaysPerSecond:0.#} days per second")}\nSpace: pause/unpause; 1-5: speed; + / -: adjust speed\nWheel: zoom; middle-drag / WASD: pan");
        Gui.Block(dateR);
        if (Gui.Clicked(dateR)) SetPace(!clock.Paused, null);
        rx -= 5 * 18 * S + 10 * S;
        for (int i = 1; i <= 5; i++)
        {
            // speed pips: filled up to the current speed
            var r = new Rectangle(rx + (i - 1) * 18 * S, 13 * S, 15 * S, 20 * S);
            bool on = i <= clock.Speed;
            bool hover = Gui.Hover(r);
            Gui.Gradient(r, on ? Gui.Lighter(Gui.Gold, 0.2f) : new Color(60, 58, 54, 255), on ? Gui.Darker(Gui.Gold, 0.35f) : new Color(30, 29, 27, 255));
            Raylib.DrawRectangleLinesEx(r, 1, hover ? Color.White : new Color(0, 0, 0, 200));
            Gui.Block(r);
            if (hover) Gui.Tooltip($"Speed {i}");
            if (Gui.Clicked(r)) SetPace(false, i);
        }
        rx -= 8 * S;
        if (Has(FeatureSet.Diplomacy))
        {
            // observers see the button too (all wars of the world)
            int wars = player != null ? G.World.WarsOf(player).Count() : 0;
            int allWars = G.World.Wars.Count;
            string wl = allWars > 0 ? $"Wars ({allWars})" : "Wars";
            float ww = Math.Max(78 * S, Gui.Measure(wl, 15, FontKind.Bold).X + 18 * S);
            rx -= ww;
            if (Gui.Button(new Rectangle(rx, 8 * S, ww, 30 * S), wl, true,
                    (player == null ? "" : wars > 0 ? $"You are in {wars} war(s).\n" : "You are at peace.\n") + $"{allWars} war(s) in the world.\nWars and peace deals (F4)", Ui.Right is RightPanel.Wars or RightPanel.Peace, 15))
                ToggleRight(RightPanel.Wars);
            if (wars > 0) Badge(new Vector2(rx + ww - 2 * S, 9 * S), wars.ToString(), Gui.Bad);
            rx -= 4 * S;
        }
        float wdw = 78 * S;
        rx -= wdw;
        if (Gui.Button(new Rectangle(rx, 8 * S, wdw, 30 * S), "World", true, "All countries of the world (F8)", Ui.Right == RightPanel.World, 15))
            ToggleRight(RightPanel.World);
        rx -= wdw + 4 * S;
        if (Gui.Button(new Rectangle(rx, 8 * S, wdw, 30 * S), "Ledger", true, "Charts of population, income, armies and more over time (F9)", Ui.Right == RightPanel.Ledger, 15))
            ToggleRight(RightPanel.Ledger);
    }

    private void Separator(ref float x)
    {
        Raylib.DrawLineEx(new Vector2(x - 6 * S, 7 * S), new Vector2(x - 6 * S, TopH - 7 * S), 1, new Color(Gui.PanelInner.R, Gui.PanelInner.G, Gui.PanelInner.B, (byte)200));
        x += 6 * S;
    }

    /// <summary>Compact number: 12.3K, 4.56M, 1.2B (small values keep their digits).</summary>
    public static string Num(double v, int decimals = 0)
    {
        double a = Math.Abs(v);
        if (a >= 1e9) return $"{v / 1e9:0.##}B";
        if (a >= 1e6) return $"{v / 1e6:0.##}M";
        if (a >= 1e4) return $"{v / 1e3:0.#}K";
        return v.ToString(decimals > 0 ? "F" + decimals : "0");
    }

    private static string? StatIcon(string label) => label.ToLowerInvariant() switch
    {
        "gold" => "gold", "manpower" => "manpower", "factories" => "factory", "divisions" => "army",
        "political power" => "politics", "stability" => "legitimacy", _ => null,
    };

    private void Stat(ref float x, string label, string value, string sub, string tooltip, Color subColor)
    {
        string? icon = StatIcon(label);
        bool hasIcon = icon != null && Gui.HasIcon(icon);
        float iconW = hasIcon ? 22 * S : 0;
        float vw = Gui.Measure(value, 18, FontKind.Bold).X, sw = sub.Length > 0 ? Gui.Measure(sub, 13).X + 5 * S : 0;
        float w = hasIcon ? iconW + 4 * S + vw + sw + 14 * S : Math.Max(92 * S, Math.Max(Gui.Measure(label, 13).X, vw + sw) + 14 * S);
        var r = new Rectangle(x, 4 * S, w, 38 * S);
        float tx = x;
        if (hasIcon)
        {
            Gui.Icon(icon!, new Rectangle(x, 12 * S, iconW, iconW));
            tx += iconW + 4 * S;
            Gui.Label(value, tx, 12 * S, 18, Gui.Text, FontKind.Bold);
            if (sub.Length > 0) Gui.Label(sub, tx + vw + 5 * S, 15 * S, 13, subColor);
        }
        else
        {
            Gui.Label(label, x, 4 * S, 13, Gui.TextDim);
            Gui.Label(value, x, 19 * S, 18, Gui.Text, FontKind.Bold);
            if (sub.Length > 0) Gui.Label(sub, x + vw + 5 * S, 22 * S, 13, subColor);
        }
        if (Gui.Hover(r)) Gui.Tooltip(hasIcon ? label + "\n" + tooltip : tooltip);
        x += w;
    }

    /// <summary>A small number in a circle (counts on buttons).</summary>
    private void Badge(Vector2 center, string text, Color col)
    {
        float r = Math.Max(8 * S, Gui.Measure(text, 12, FontKind.Bold).X / 2 + 4 * S);
        Raylib.DrawCircleV(center, r + 1, new Color(0, 0, 0, 200));
        Raylib.DrawCircleV(center, r, col);
        var m = Gui.Measure(text, 12, FontKind.Bold);
        Gui.Label(text, center.X - m.X / 2, center.Y - m.Y / 2, 12, Color.White, FontKind.Bold);
    }

    // ===============================================================================================
    // Map modes (bottom right)
    // ===============================================================================================
    private float MapModeBarH => 34 * S;

    private void DrawMapModes()
    {
        var modes = new List<(string Name, MapMode Mode)> { ("Political", MapMode.Political), ("Terrain", MapMode.Terrain), ("States", MapMode.States) };
        if (Has(FeatureSet.Diplomacy)) modes.Add(("Diplomacy", MapMode.Diplomacy));
        if (G.Features.HasSupply && Has(FeatureSet.Military)) modes.Add(("Supply", MapMode.Supply));
        if (Has(FeatureSet.Air)) modes.Add(("Air", MapMode.Air));
        if (G.Politics.Enabled) modes.Add(("Ideology", MapMode.Ideology));
        if (G.Population.Enabled) { modes.Add(("People", MapMode.Population)); modes.Add(("Culture", MapMode.Culture)); }
        if (G.Population.ReligionEnabled) modes.Add(("Religion", MapMode.Religion));
        modes.Add(("Locations", MapMode.Locations));
        if (G.Geo.Enabled) modes.Add(("Provinces", MapMode.Provinces));
        var custom = G.Interface.MapModes;
        bool fogToggle = Has(FeatureSet.FogOfWar);
        bool unitsToggle = Has(FeatureSet.Military) || Has(FeatureSet.Navy);
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        string current = Map.CustomMode != null ? Map.CustomMode.Name : modes.FirstOrDefault(m => m.Mode == Map.Mode).Name ?? Map.Mode.ToString();
        float bw = 74 * S, gap = 3 * S, pad = 6 * S, modeW = 150 * S;
        int toggles = (fogToggle ? 1 : 0) + (unitsToggle ? 1 : 0);
        float w = toggles * (bw + gap) + modeW + 2 * pad;
        var r = new Rectangle(sw - w - 8 * S, sh - MapModeBarH - 8 * S, w, MapModeBarH);
        Gui.Block(r);
        Gui.Frame(r, Gui.MenuBar, false, "menu_bar");
        float x = r.X + pad, y = r.Y + 5 * S, h = r.Height - 10 * S;
        if (fogToggle)
        {
            // map shading only: units you cannot see stay hidden either way
            if (Gui.Button(new Rectangle(x, y, bw, h), "Fog", true, "Fog of war shading on the map (F)\nTurning it off only clears the map: enemy units you cannot see stay hidden.", MapRenderer.FogShading, 14))
                MapRenderer.FogShading = !MapRenderer.FogShading;
            x += bw + gap;
        }
        if (unitsToggle)
        {
            if (Gui.Button(new Rectangle(x, y, bw, h), "Units", true, "Units on the map (H)\nTurning it off hides divisions, fleets, battles and order arrows, for a clear look at the map.", MapOverlay.ShowUnits, 14))
                MapOverlay.ShowUnits = !MapOverlay.ShowUnits;
            x += bw + gap;
        }
        var toggle = new Rectangle(x, y, modeW, h);
        if (Gui.Button(toggle, "", true, "Choose what the map shows (Tab cycles)", _mapModesOpen, 14)) _mapModesOpen = !_mapModesOpen;
        Gui.Label(Gui.Fit("Map: " + current, modeW - 30 * S, 14), toggle.X + 8 * S, toggle.Y + (h - Gui.FontSize(14)) / 2, 14, Gui.Text, FontKind.Bold);
        // open/close chevron
        float cx = toggle.X + modeW - 14 * S, cy = toggle.Y + h / 2, cs = 4 * S;
        if (_mapModesOpen) Raylib.DrawTriangle(new Vector2(cx - cs, cy + cs / 2), new Vector2(cx + cs, cy + cs / 2), new Vector2(cx, cy - cs / 2 - 1), Gui.Gold);
        else Raylib.DrawTriangle(new Vector2(cx - cs, cy - cs / 2), new Vector2(cx, cy + cs / 2 + 1), new Vector2(cx + cs, cy - cs / 2), Gui.Gold);
        if (!_mapModesOpen) return;

        // the open menu: map modes in a grid above the bar
        var all = modes.Select(m => (m.Name, (Action)(() => { Map.Mode = m.Mode; Map.CustomMode = null; Map.Dirty = true; }), Map.CustomMode == null && Map.Mode == m.Mode, MapModeHelp(m.Mode))).ToList();
        foreach (var m in custom)
        {
            var cm = m;
            all.Add((cm.Name, () => { Map.CustomMode = cm; Map.Dirty = true; }, Map.CustomMode == cm,
                $"{cm.Name}: values {cm.Min:0.##} to {cm.Max:0.##}\nLow color: RGB {cm.LowColor.R}, {cm.LowColor.G}, {cm.LowColor.B}\nHigh color: RGB {cm.HighColor.R}, {cm.HighColor.G}, {cm.HighColor.B}"));
        }
        int cols = 3, rows = (all.Count + cols - 1) / cols;
        float cw = 92 * S, ch = 26 * S;
        var pr = new Rectangle(r.X + r.Width - (cols * (cw + gap) - gap + 2 * pad), r.Y - 4 * S - (rows * (ch + gap) - gap + 2 * pad), cols * (cw + gap) - gap + 2 * pad, rows * (ch + gap) - gap + 2 * pad);
        Gui.Block(pr);
        Gui.Frame(pr, Gui.MenuBar, false, "menu_bar");
        for (int i = 0; i < all.Count; i++)
        {
            var br = new Rectangle(pr.X + pad + (i % cols) * (cw + gap), pr.Y + pad + (i / cols) * (ch + gap), cw, ch);
            if (Gui.Button(br, all[i].Name, true, all[i].Item4, all[i].Item3, 14)) { all[i].Item2(); _mapModesOpen = false; }
        }
    }

    private bool _mapModesOpen;

    private string MapModeHelp(MapMode mode) => mode switch
    {
        MapMode.Political => "Political: territory color shows its country; stripes mark occupied provinces. Tab cycles modes.",
        MapMode.Terrain => "Terrain: terrain textures and relief; province colors are hidden. Tab cycles modes.",
        MapMode.States => "States: each state gets a distinct color; dark lines show province boundaries. Tab cycles modes.",
        MapMode.Diplomacy => "Diplomacy: shows relations to the focused country; hover a province for details. Tab cycles modes.",
        MapMode.Locations => "Locations: each province uses its configured map color. Tab cycles modes.",
        MapMode.Supply => "Supply: low/unsupplied areas trend red or dark; stronger supply trends green. Tab cycles modes.",
        MapMode.Provinces => "Provinces: historical provinces on the shown date (console: geodate 1CE.1.1), colored by owner, dark lines at province borders. Tab cycles modes.",
        MapMode.Air => "Air: color distinguishes air regions; red-to-green indicates contested air superiority. Tab cycles modes.",
        _ => $"{mode} map mode. Tab cycles modes."
    };

    // ===============================================================================================
    // Notifications & status
    // ===============================================================================================
    private void DrawNotifications()
    {
        var list = G.Notifications;
        int n = Math.Min(6, list.Count);
        float w = 420 * S, lineH = 22 * S;
        float x = Raylib.GetScreenWidth() - w - 8 * S;
        float y = Raylib.GetScreenHeight() - MapModeBarH - 16 * S - n * lineH;
        for (int i = list.Count - n; i < list.Count; i++)
        {
            var note = list[i];
            int age = G.World.Date.TotalDays - note.Date.TotalDays;
            if (age > 60) { y += lineH; continue; }
            var col = note.Kind switch
            {
                NotificationKind.Good => Gui.Good,
                NotificationKind.Bad => Gui.Bad,
                NotificationKind.War => new Color(240, 140, 90, 255),
                NotificationKind.Diplomacy => new Color(140, 190, 250, 255),
                NotificationKind.Character => new Color(220, 190, 250, 255),
                _ => Gui.Text,
            };
            var r = new Rectangle(x, y, w, lineH - 2);
            Gui.Gradient(r, new Color(8, 8, 10, 120), new Color(8, 8, 10, 200));
            Raylib.DrawRectangle((int)x, (int)y, (int)(3 * S), (int)(lineH - 2), col);
            Gui.Label(Gui.Fit(note.Text, w - 16 * S, 14), x + 9 * S, y + 3 * S, 14, col);
            if (Gui.Hover(r))
            {
                Gui.Tooltip($"{note.Date.ToDisplay()}\n{note.Text}" + (note.Location != null ? "\n(click to view)" : ""));
                if (note.Location != null && Gui.Clicked(r)) Cam.LookAt(Map.LocationPos(note.Location), 30);
                Gui.Block(r);
            }
            y += lineH;
        }
    }

    private void DrawStatus()
    {
        if (Ui.StatusTime <= 0 || Ui.Status.Length == 0) return;
        Ui.StatusTime -= Raylib.GetFrameTime();
        var m = Gui.Measure(Ui.Status, 18, FontKind.Bold);
        float x = (Raylib.GetScreenWidth() - m.X) / 2, y = TopH + 12 * S;
        var r = new Rectangle(x - 14 * S, y - 5 * S, m.X + 28 * S, m.Y + 10 * S);
        Gui.Frame(r, new Color(14, 14, 18, 220), false);
        Gui.Label(Ui.Status, x, y, 18, Gui.Gold, FontKind.Bold);
    }

    // ===============================================================================================
    // Selected divisions bar
    // ===============================================================================================
    private void DrawSelectedDivisions()
    {
        Ui.SelectedDivisions.RemoveAll(d => !G.World.Divisions.Contains(d));
        if (Ui.SelectedDivisions.Count == 0) return;
        float w = Math.Min(760 * S, Raylib.GetScreenWidth() - 900 * S);
        w = Math.Max(w, 520 * S);
        float h = 118 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, Raylib.GetScreenHeight() - h - 8 * S, w, h);
        Gui.Panel(r, $"{Ui.SelectedDivisions.Count} division(s) selected - right-click the map to move/attack");
        float y = r.Y + 32 * S;
        float x = r.X + 8 * S;
        foreach (var d in Ui.SelectedDivisions.Take(6))
        {
            var row = new Rectangle(x, y, 118 * S, 44 * S);
            Raylib.DrawRectangleRec(row, new Color(36, 40, 48, 255));
            Gui.Label(d.Name.Length > 16 ? d.Name[..15] + "." : d.Name, x + 4, y + 2, 13);
            Gui.Bar(new Rectangle(x + 4, y + 21 * S, 108 * S, 7 * S), (float)d.StrengthFraction, new Color(90, 200, 90, 255));
            Gui.Bar(new Rectangle(x + 4, y + 31 * S, 108 * S, 7 * S), (float)d.OrgFraction, new Color(80, 160, 240, 255));
            if (Gui.Hover(row))
                Gui.Tooltip($"{d.Name} ({d.Template.Name})\nLocation: {d.Location.DisplayName}\nStrength {d.Strength:0}/{d.MaxStrength:0}\nOrganisation {d.Org:0.0}/{d.MaxOrg:0}\n" +
                            $"Army: {d.Army?.Name ?? "none"}\n" + string.Join("\n", d.Template.Stats.Select(kv => $"{Localisation.GetOrPretty(kv.Key)}: {kv.Value:0.##}")));
            x += 122 * S;
        }
        if (Ui.SelectedDivisions.Count > 6) Gui.Label($"+{Ui.SelectedDivisions.Count - 6}", x + 4, y + 12 * S, 16);
        float by = r.Y + h - 34 * S;
        float bx = r.X + 8 * S;
        if (Gui.Button(new Rectangle(bx, by, 90 * S, 28 * S), "Stop", true, "Cancel movement (Delete)")) foreach (var d in Ui.SelectedDivisions) G.Warfare.StopDivision(d);
        bx += 96 * S;
        if (Gui.Button(new Rectangle(bx, by, 150 * S, 28 * S), "New army", true, "Put the selected divisions into a new army"))
        {
            var player = G.World.Player!;
            var army = G.Warfare.CreateArmy(player);
            foreach (var d in Ui.SelectedDivisions) G.Warfare.AssignToArmy(d, army);
            Ui.SelectedArmy = army;
            Ui.Left = LeftPanel.Military;
            Ui.SetStatus($"Created {army.Name}");
        }
        bx += 156 * S;
        var armies = G.World.ArmiesOf(G.World.Player!).ToList();
        foreach (var a in armies.Take(3))
        {
            if (Gui.Button(new Rectangle(bx, by, 130 * S, 28 * S), "To " + (a.Name.Length > 11 ? a.Name[..10] + "." : a.Name), true, $"Add to {a.Name}"))
                foreach (var d in Ui.SelectedDivisions) G.Warfare.AssignToArmy(d, a);
            bx += 134 * S;
        }
    }
}
