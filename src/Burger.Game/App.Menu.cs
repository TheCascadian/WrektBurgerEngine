using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public enum MenuPage { Home, Play, Multiplayer, Host, Join }

/// <summary>
/// The main menu, one question per page: Home (Play, Multiplayer, Load, Settings, Quit) opens Play (pick a country) or Multiplayer, which opens
/// Host (pick a country, get a code) or Join (paste a code). Every page but Home has Back, and so does Esc.
/// </summary>
public sealed partial class App
{
    private MenuPage _menuPage = MenuPage.Home;
    private bool _menuSettings;
    private bool _steamLogOpen;

    private void DrawMenuPages(Game game, MapRenderer map, CameraController cam)
    {
        float s = Gui.Scale;
        int sh = Raylib.GetScreenHeight();
        var panel = new Rectangle(16 * s, 16 * s, 420 * s, sh - 32 * s);
        if (_menuSettings && !Ui.SettingsOpen) _menuSettings = false;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) && !Gui.TextFocused)
        {
            if (_menuSettings) { Ui.SettingsOpen = false; UserSettings.Save(); _menuSettings = false; }
            else if (Ui.SaveMenuOpen) Ui.SaveMenuOpen = false;
            else if (Ui.ErrorsOpen) Ui.ErrorsOpen = false;
            else _menuPage = _menuPage switch { MenuPage.Host or MenuPage.Join => MenuPage.Multiplayer, _ => MenuPage.Home };
        }

        Gui.Panel(panel);
        float x = panel.X + 18 * s, y = panel.Y + 16 * s, w = panel.Width - 36 * s;
        Gui.LabelShadow(game.Data.Info.Name, x, y, 32, Gui.Gold, FontKind.Header);
        y += 40 * s;
        string sub = $"Version {game.Data.Info.Version}" + (game.Data.Info.Author.Length > 0 ? $" - by {game.Data.Info.Author}" : "");
        Gui.Label(sub, x, y, 15, Gui.TextDim);
        y += 28 * s;

        switch (_menuPage)
        {
            case MenuPage.Home: DrawPageHome(game, x, y, w, panel); break;
            case MenuPage.Play: DrawPagePlay(game, map, cam, x, y, w, panel); break;
            case MenuPage.Multiplayer: DrawPageMultiplayer(x, y, w); break;
            case MenuPage.Host: DrawPageHost(game, map, cam, x, y, w, panel); break;
            case MenuPage.Join: DrawPageJoin(x, y, w); break;
        }

        if (Ui.ErrorsOpen) DrawMenuErrors();
        if (Ui.SaveMenuOpen) DrawMenuSaves();
        if (_menuSettings) { _hud?.DrawSettings(); if (!Ui.SettingsOpen) _menuSettings = false; }
    }

    private bool Big(float x, ref float y, float w, string text, string? tip = null, bool enabled = true)
    {
        float s = Gui.Scale;
        bool hit = !_menuSettings && Gui.Button(new Rectangle(x, y, w, 46 * s), text, enabled, tip, false, 20);
        y += 54 * s;
        return hit;
    }

    private bool Back(float x, float y, float w, string to)
    {
        float s = Gui.Scale;
        return !_menuSettings && Gui.Button(new Rectangle(x, y, w, 32 * s), "Back to " + to, true, null, false, 15);
    }

    private void DrawPageHome(Game game, float x, float y, float w, Rectangle panel)
    {
        float s = Gui.Scale;
        if (game.Data.Info.Description.Length > 0) y += Gui.Wrapped(game.Data.Info.Description, x, y, w, 15, Gui.Text) + 14 * s;
        else y += 8 * s;
        if (Big(x, ref y, w, "Play", "Start a new game, or watch the AI")) _menuPage = MenuPage.Play;
        if (Big(x, ref y, w, "Multiplayer", "Host a game for friends, or join one with a code")) _menuPage = MenuPage.Multiplayer;
        if (Big(x, ref y, w, "Load game", ListSaves().Count > 0 ? "Continue a saved game" : "No saves yet", ListSaves().Count > 0)) Ui.SaveMenuOpen = !Ui.SaveMenuOpen;
        if (Big(x, ref y, w, "Settings", "Speed, AI, map view, your multiplayer name")) { Ui.SettingsOpen = true; _menuSettings = true; }
        if (Big(x, ref y, w, "Quit")) _quit = true;

        float fy = panel.Y + panel.Height - 40 * s;
        float half = (w - 6 * s) / 2;
        if (!_menuSettings)
        {
            if (Gui.Button(new Rectangle(x, fy, half, 28 * s), "Reload files", true, "Re-read all game files (after editing them)", false, 14)) ReloadFiles();
            if (Log.ErrorCount > 0 && Gui.Button(new Rectangle(x + half + 6 * s, fy, half, 28 * s), $"{Log.ErrorCount} error(s) - log", true, "Problems found in the game files", false, 14)) Ui.ErrorsOpen = !Ui.ErrorsOpen;
        }
    }

    private void DrawPagePlay(Game game, MapRenderer map, CameraController cam, float x, float y, float w, Rectangle panel)
    {
        float s = Gui.Scale;
        DrawStartDate(game, x, ref y, w);
        bool terra = game.Exploration.Allowed;
        DrawCountryPicker(game, map, cam, x, ref y, w, panel, (terra ? 198 : 160) * s);
        if (terra)
        {
            if (Gui.Button(new Rectangle(x, y, w, 32 * s), UserSettings.TerraIncognita ? "Terra incognita: on" : "Terra incognita: off", true,
                    "On: the lands your nation has never seen stay an unknown chart. Explore with armies and ships, colonize what you find.\nChosen now; you can switch it off later from the pause menu settings (it asks first).\nRecommended for the intended experience.",
                    UserSettings.TerraIncognita, 15))
            {
                UserSettings.TerraIncognita = !UserSettings.TerraIncognita;
                UserSettings.Save();
            }
            y += 38 * s;
        }
        float bh = 40 * s;
        if (Gui.Button(new Rectangle(x, y, w, bh), _menuSelection != null ? $"Play as {_menuSelection.Name}" : "Select a country", _menuSelection != null, null, false, 18))
        {
            RequestStart(_menuSelection!.Tag);
            Gui.EndFrame(); Raylib.EndDrawing();
            return;
        }
        y += bh + 6 * s;
        if (Gui.Button(new Rectangle(x, y, w, 32 * s), "Observe: watch the AI play", true, null, false, 15)) { RequestStart(null); Gui.EndFrame(); Raylib.EndDrawing(); return; }
        y += 38 * s;
        if (Back(x, y, w, "main menu")) _menuPage = MenuPage.Home;
    }

    /// <summary>Country list and the selected country's summary, down to <paramref name="reserve"/> pixels above the bottom of the panel.</summary>
    private void DrawCountryPicker(Game game, MapRenderer map, CameraController cam, float x, ref float y, float w, Rectangle panel, float reserve)
    {
        float s = Gui.Scale;
        Gui.Label("Choose a country (or click it on the map):", x, y, 16);
        y += 26 * s;
        var playable = game.World.Countries.Where(c => c.Playable && c.Exists).OrderBy(c => c.Name).ToList();
        float listH = panel.Y + panel.Height - y - reserve - 66 * s;
        var area = new Rectangle(x, y, w, Math.Max(40 * s, listH));
        float top = Gui.BeginScroll("menu-countries", area, playable.Count * 40 * s);
        float cy = top;
        foreach (var c in playable)
        {
            var row = new Rectangle(x, cy, w - 10 * s, 36 * s);
            bool sel = c == _menuSelection;
            Gui.ButtonFrame(row, true, Gui.Hover(row), sel);
            Gui.Flag(c, new Rectangle(row.X + 4, row.Y + 4, 42 * s, 28 * s));
            Gui.Label(c.Name, row.X + 54 * s, row.Y + 8 * s, 17, sel ? Color.White : Gui.Text, FontKind.Bold);
            // scissor clips drawing, not hit testing: ignore rows outside the visible list
            if (!_menuSettings && Raylib.CheckCollisionRecs(row, area) && Gui.Clicked(row))
            {
                _menuSelection = c;
                if (c.Capital != null) cam.LookAt(map.LocationPos(c.Capital));
            }
            cy += 40 * s;
        }
        Gui.EndScroll();
        y += area.Height + 10 * s;
        if (_menuSelection != null)
        {
            var c = _menuSelection;
            Gui.Flag(c, new Rectangle(x, y, 60 * s, 40 * s));
            Gui.LabelShadow(c.Name, x + 70 * s, y, 21, Gui.Gold, FontKind.Header);
            Gui.Label($"{c.OwnedStates.Count} states - ruler: {c.Ruler?.DisplayName ?? "none"}", x + 70 * s, y + 24 * s, 14, Gui.TextDim);
        }
        y += 50 * s;
    }

    private void DrawPageMultiplayer(float x, float y, float w)
    {
        float s = Gui.Scale;
        y += Gui.Wrapped("Play with friends. The host picks a country and shares a code. Everyone else pastes it.", x, y, w, 15, Gui.Text) + 14 * s;
        if (Big(x, ref y, w, "Host a game", "You get a code to share. Nothing else to set up.")) _menuPage = MenuPage.Host;
        if (Big(x, ref y, w, "Join a game", "Paste a code from the host")) _menuPage = MenuPage.Join;
        if (_mpMessage.Length > 0) y += Gui.Wrapped(_mpMessage, x, y, w, 15, Gui.Gold) + 10 * s;
        if (Back(x, y, w, "main menu")) { _menuPage = MenuPage.Home; _mpMessage = ""; }
        y += 44 * s;
        DrawSteamStatus(x, ref y, w);
    }

    /// <summary>One line on whether Steam is connected, with a collapsible log that explains failures.</summary>
    private void DrawSteamStatus(float x, ref float y, float w)
    {
        float s = Gui.Scale;
        bool ok = SteamService.Init() && SteamService.Available;
        string relay = SteamService.RelayState(out bool relayOk);
        var color = !ok ? Gui.Bad : relayOk ? Gui.Good : Gui.Gold;
        string head = !ok ? "Steam: not connected" : relayOk ? $"Steam: connected as {SteamService.PersonaName}" : $"Steam: connected, {relay}";
        Gui.Label(head, x, y + 4 * s, 15, color, FontKind.Bold);
        float bw = 84 * s;
        if (!_menuSettings && Gui.Button(new Rectangle(x + w - bw, y, bw, 26 * s), _steamLogOpen ? "Hide log" : "Show log", true, "Steam connection details, for troubleshooting", false, 14)) _steamLogOpen = !_steamLogOpen;
        y += 30 * s;
        if (!ok) y += Gui.Wrapped(SteamService.Problem, x, y, w, 13, Gui.TextDim) + 4 * s;
        if (!_steamLogOpen) return;
        float bh = 26 * s, half = (w - 6 * s) / 2;
        if (!_menuSettings && Gui.Button(new Rectangle(x, y, half, bh), "Retry", !ok, ok ? "Steam is already connected" : "Check again after starting Steam", false, 14)) SteamService.Retry();
        if (!_menuSettings && Gui.Button(new Rectangle(x + half + 6 * s, y, half, bh), "Copy log", true, "Copy the whole multiplayer log to send to the developer", false, 14))
            Raylib.SetClipboardText(string.Join("\n", SteamService.LogLines.Select(e => e.ToString())));
        y += bh + 6 * s;
        var lines = SteamService.LogLines;
        Gui.Label($"{lines.Length} entries - full log in logs/error.log", x, y, 12, Gui.TextDim);
        y += 18 * s;
        foreach (var e in lines.Skip(Math.Max(0, lines.Length - 18)))
        {
            var c = e.Level == LogLevel.Error ? Gui.Bad : e.Level == LogLevel.Warning ? Gui.Gold : Gui.TextDim;
            y += Gui.Wrapped($"{e.Time:HH:mm:ss} {e.Message}", x, y, w, 12, c) + 2 * s;
        }
    }

    private void DrawPageHost(Game game, MapRenderer map, CameraController cam, float x, float y, float w, Rectangle panel)
    {
        float s = Gui.Scale;
        Gui.Label("Host a game as " + UserSettings.EffectiveName + " (change the name in Settings)", x, y, 14, Gui.TextDim);
        y += 24 * s;
        DrawStartDate(game, x, ref y, w);
        DrawCountryPicker(game, map, cam, x, ref y, w, panel, 120 * s);
        float bh = 40 * s;
        if (Gui.Button(new Rectangle(x, y, w, bh), _menuSelection != null ? $"Host as {_menuSelection.Name}" : "Select a country", _menuSelection != null, "Starts the game paused and gives you the join code to share", false, 18))
        {
            HostMultiplayer(_menuSelection!.Tag);
            Gui.EndFrame(); Raylib.EndDrawing();
            return;
        }
        y += bh + 6 * s;
        if (Back(x, y, w, "multiplayer")) _menuPage = MenuPage.Multiplayer;
    }

    private void DrawPageJoin(float x, float y, float w)
    {
        float s = Gui.Scale;
        Gui.Label("Join code from the host", x, y, 16);
        y += 26 * s;
        float paste = 80 * s;
        Gui.TextField("mp-code", new Rectangle(x, y, w - paste - 6 * s, 34 * s), ref _joinCode, 48, "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX");
        if (Gui.Button(new Rectangle(x + w - paste, y, paste, 34 * s), "Paste", true, "Paste the code you copied", false, 15)) _joinCode = (Raylib.GetClipboardText_() ?? "").Trim();
        y += 44 * s;
        Gui.Label("You join as " + UserSettings.EffectiveName + " (change the name in Settings)", x, y, 14, Gui.TextDim);
        y += 28 * s;
        bool valid = Burger.Core.Multiplayer.JoinCode.TryParseAny(_joinCode, out _, out _, out _, out _) || Burger.Core.Multiplayer.SteamCode.TryParse(_joinCode, out _, out _);
        if (Gui.Button(new Rectangle(x, y, w, 44 * s), "Connect", valid, valid ? null : "Enter the whole code", false, 19))
        {
            _mpMessage = "Connecting...";
            JoinMultiplayer(_joinCode, UserSettings.EffectiveName);
        }
        y += 54 * s;
        if (_mpMessage.Length > 0) y += Gui.Wrapped(_mpMessage, x, y, w, 15, Gui.Gold) + 10 * s;
        if (Back(x, y, w, "multiplayer")) { _menuPage = MenuPage.Multiplayer; _mpMessage = ""; }
    }
}
