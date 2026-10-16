using Burger.Core.Save;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawPauseMenu()
    {
        G.Clock.Paused = true;
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 120));
        if (Ui.SettingsOpen) { _settingsInGame = true; DrawSettingsPanel(); return; }
        float w = 300 * S, h = 380 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Menu");
        float x = r.X + 20 * S, y = r.Y + 44 * S, bw = w - 40 * S, bh = 38 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Resume")) Ui.PauseMenuOpen = false;
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Save / Load")) { Ui.SaveMenuOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Error log (F2)")) { Ui.ErrorsOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Settings")) Ui.SettingsOpen = true;
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Main menu")) { Ui.PauseMenuOpen = false; _app.ReturnToMainMenu(); return; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Quit")) _app.RequestQuit();
    }

    private string _speed5Text = "", _speed1Text = "";
    /// <summary>The settings panel is open from the pause menu of a running game (not from the main menu).</summary>
    private bool _settingsInGame;

    /// <summary>Settings: the most days per second speed 5 may run (0 = as fast as the computer allows).</summary>
    private void DrawSettingsPanel()
    {
        if (Ui.TerraConfirmOpen) { DrawTerraConfirm(); return; }
        float w = 840 * S, h = 620 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Settings");
        if (Gui.CloseButton(r)) { Ui.SettingsOpen = false; UserSettings.Save(); return; }
        float x = r.X + 20 * S, y = r.Y + 46 * S, bw = 380 * S;
        DrawMultiplayerSettings(r.X + 440 * S, r.Y + 46 * S, 380 * S);
        DrawGraphicsSettings(r.X + 440 * S, r.Y + 230 * S, 380 * S);
        if (_speed5Text.Length == 0) _speed5Text = UserSettings.Speed5DaysPerSecond.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Gui.Label("Speed 5: most days per second", x, y, 16);
        y += 26 * S;
        Gui.TextField("speed5", new Rectangle(x, y, 120 * S, 30 * S), ref _speed5Text, 8, "0");
        Gui.Label("0 = as fast as the computer allows", x + 134 * S, y + 6 * S, 14, Gui.TextDim);
        if (double.TryParse(_speed5Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 && Math.Abs(v - UserSettings.Speed5DaysPerSecond) > 1e-9)
        {
            UserSettings.Speed5DaysPerSecond = v;
            UserSettings.Apply(G);
        }
        y += 44 * S;
        if (_speed1Text.Length == 0) _speed1Text = UserSettings.Speed1DaysPerSecond.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        Gui.Label("Speed 1: days per second", x, y, 16);
        y += 26 * S;
        Gui.TextField("speed1", new Rectangle(x, y, 120 * S, 30 * S), ref _speed1Text, 8, "0");
        Gui.Label("0 = default (0.5); try 0.05 for slow", x + 134 * S, y + 6 * S, 14, Gui.TextDim);
        if (double.TryParse(_speed1Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v1) && v1 >= 0 && Math.Abs(v1 - UserSettings.Speed1DaysPerSecond) > 1e-9)
        {
            UserSettings.Speed1DaysPerSecond = v1;
            UserSettings.Apply(G);
        }
        y += 44 * S;
        Gui.Label($"Currently {G.Clock.ActualDaysPerSecond:0.0} days per second.", x, y, 14, Gui.TextDim);
        y += 34 * S;
        Gui.Label("AI behaviour", x, y, 16);
        y += 26 * S;
        if (Gui.Button(new Rectangle(x, y, bw, 32 * S), UserSettings.Ahistorical ? "Ahistorical AI: on" : "Historical AI (default)", true,
                "Historical: nations follow their history and ambitions (realistic, plausible).\nAhistorical: nations follow only their own interests, so the player can take any role. Less accurate.",
                UserSettings.Ahistorical, 15))
        {
            UserSettings.Ahistorical = !UserSettings.Ahistorical;
            G.HistoricalBias = UserSettings.EffectiveBias;
            UserSettings.Save();
        }
        y += 38 * S;
        Gui.Label(UserSettings.Ahistorical ? "Less realistic, but freer for role-play." : "Realistic and plausible; history shapes the AI.", x, y, 14, Gui.TextDim);
        y += 30 * S;
        if (Gui.Button(new Rectangle(x, y, bw, 32 * S), UserSettings.AiDisabled ? "Other countries' AI: DISABLED" : "Other countries' AI: enabled", true,
                "Disabled: every country except yours does nothing (no moves, diplomacy, recruitment or production). Useful for testing and sandbox play.",
                UserSettings.AiDisabled, 15))
        {
            UserSettings.AiDisabled = !UserSettings.AiDisabled;
            G.Ai.Enabled = !UserSettings.AiDisabled;
            UserSettings.Save();
        }
        y += 44 * S;
        if (UserSettings.GlobeAllowed)
        {
            if (Gui.Button(new Rectangle(x, y, bw, 32 * S), UserSettings.GlobeMode ? "Map view: globe" : "Map view: flat (wraps east-west)", true,
                    "Flat: the map repeats east-west, so you can pan around the world.\nGlobe: the same map on a sphere. Everything you see and select carries over.",
                    UserSettings.GlobeMode, 15))
            {
                UserSettings.GlobeMode = !UserSettings.GlobeMode;
                UserSettings.Save();
            }
            y += 44 * S;
        }
        if (Gui.Button(new Rectangle(x, y, bw, 32 * S), UserSettings.SunCycle ? "Sun: circles the map with time" : "Sun: fixed", true,
                "On: the sun travels around the map as the game clock runs, so day and night move across the world.\nOff: the sun stays where it is.",
                UserSettings.SunCycle, 15))
        {
            UserSettings.SunCycle = !UserSettings.SunCycle;
            UserSettings.Save();
        }
        y += 44 * S;
        if (G.Exploration.Allowed && (!_settingsInGame || G.World.Player != null))
        {
            bool on = _settingsInGame ? G.Exploration.Enabled : UserSettings.TerraIncognita;
            string tip = _settingsInGame
                ? "On: lands your nation has never seen stay an unknown chart; explore with armies and ships.\nTurning it off reveals the whole map and asks first. Turning it on again hides what was never charted."
                : "On: lands your nation has never seen stay an unknown chart. Applies to new games; each game keeps its own choice.";
            if (Gui.Button(new Rectangle(x, y, bw, 32 * S), on ? "Terra incognita: on" : "Terra incognita: off", true, tip, on, 15))
            {
                if (!_settingsInGame) { UserSettings.TerraIncognita = !UserSettings.TerraIncognita; UserSettings.Save(); }
                else if (on) Ui.TerraConfirmOpen = true;
                else G.Exploration.SetEnabled(true);
            }
            y += 44 * S;
        }
        if (Gui.Button(new Rectangle(x, y, bw, 34 * S), "Done")) { Ui.SettingsOpen = false; UserSettings.Save(); }
    }

    /// <summary>Settings page for the main menu (the same panel as in the pause menu).</summary>
    public void DrawSettings() { _settingsInGame = false; DrawSettingsPanel(); }

    /// <summary>Asks before the unknown lands are revealed: terra incognita is the intended way to play, so this is a soft rule, not a lock.</summary>
    private void DrawTerraConfirm()
    {
        float w = 480 * S, h = 292 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Reveal the whole map?");
        float x = r.X + 20 * S, y = r.Y + 44 * S, tw = w - 40 * S;
        y += Gui.Wrapped("Terra incognita is the intended way to play: the world beyond your borders stays unknown until your armies and ships chart it, and settling new lands is a discovery.", x, y, tw, 15, Gui.Text) + 10 * S;
        y += Gui.Wrapped("Switching it off shows every land, sea and nation at once. What you have already charted stays charted, and you can switch it back on later.", x, y, tw, 14, Gui.TextDim) + 10 * S;
        float by = r.Y + h - 52 * S, half = (tw - 8 * S) / 2;
        if (Gui.Button(new Rectangle(x, by, half, 36 * S), "Keep exploring", true, null, true, 16)) Ui.TerraConfirmOpen = false;
        if (Gui.Button(new Rectangle(x + half + 8 * S, by, half, 36 * S), "Reveal the map", true, "Switch terra incognita off for this game", false, 16))
        {
            G.Exploration.SetEnabled(false);
            Ui.TerraConfirmOpen = false;
        }
    }

    private void DrawGraphicsSettings(float x, float y, float w)
    {
        Gui.Label("Map detail", x, y, 16, Gui.Gold);
        y += 28 * S;
        if (Gui.Button(new Rectangle(x, y, w, 32 * S), UserSettings.LodEnabled ? "Level of detail: on (faster)" : "Level of detail: off (full detail)", true,
                "On: small details (water waves, river shine, terrain blending) fade out as you zoom out, which keeps weak graphics cards fast. The levels below choose how far each one lasts.\nOff: every detail stays at every zoom. Slower.",
                UserSettings.LodEnabled, 15))
        {
            UserSettings.LodEnabled = !UserSettings.LodEnabled;
            UserSettings.Save();
        }
        y += 38 * S;
        LodRow(x, ref y, w, "Water", ref UserSettings.LodWater, "Waves, glints and coast foam on the sea.");
        LodRow(x, ref y, w, "Rivers", ref UserSettings.LodRiver, "River shine, flow and white water.");
        LodRow(x, ref y, w, "Terrain", ref UserSettings.LodTerrain, "Blending between terrain types. Low is the cheapest.");
    }

    /// <summary>One feature's level: click to step Low, Default, High, Max. Greyed out (but still readable) while LOD is off.</summary>
    private void LodRow(float x, ref float y, float w, string name, ref int level, string tip)
    {
        string text = $"{name}: {UserSettings.LodNames[level]}";
        if (Gui.Button(new Rectangle(x, y, w, 30 * S), text, UserSettings.LodEnabled, tip + "\nLow, Default, High, Max: how far you can zoom out before it fades. Higher costs more.", false, 15))
        {
            level = (level + 1) % UserSettings.LodNames.Length;
            UserSettings.Save();
        }
        y += 34 * S;
    }

    private void DrawMultiplayerSettings(float x, float y, float w)
    {
        Gui.Label("Multiplayer", x, y, 16, Gui.Gold);
        y += 28 * S;
        Gui.Label("Your name (shown to other players)", x, y, 14, Gui.TextDim);
        y += 20 * S;
        string name = UserSettings.PlayerName;
        if (Gui.TextField("mp-setting-name", new Rectangle(x, y, w, 30 * S), ref name, 24, UserSettings.EffectiveName)) UserSettings.PlayerName = name;
        UserSettings.PlayerName = name;
        y += 44 * S;
        if (Gui.Button(new Rectangle(x, y, w, 32 * S), UserSettings.OpenRouter ? "Hosting: open the router automatically" : "Hosting: same network only", true,
                "On: when you host, the game asks your router to let friends in from the internet, and closes it again when you stop. No router settings needed.\nOff: only players on your own network can join.",
                UserSettings.OpenRouter, 15))
        {
            UserSettings.OpenRouter = !UserSettings.OpenRouter;
            UserSettings.Save();
        }
        y += 38 * S;
        Gui.Label("Your address is only ever inside a join code you hand out.", x, y, 13, Gui.TextDim);
    }

    private bool _saveCacheValid;
    private List<(string Path, (string, string, string) Info)> _saveCache = new();

    private void DrawSaveMenu()
    {
        float w = 520 * S, h = Math.Min(560 * S, Raylib.GetScreenHeight() - 120 * S);
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Save / Load");
        if (Gui.CloseButton(r)) { Ui.SaveMenuOpen = false; return; }
        float x = r.X + 14 * S, y = r.Y + 40 * S, bw = w - 28 * S;
        if (Gui.Button(new Rectangle(x, y, bw, 34 * S), "Save new game", true, "Saves to the saves folder next to the game folder (F5 = quick save)"))
        {
            Ui.SetStatus(_app.SaveAs(null));
            _saveCacheValid = false;
        }
        y += 44 * S;
        Gui.Label("Load a saved game:", x, y, 16, Gui.TextDim);
        y += 24 * S;
        // reading every save header each frame made this menu crawl: cache until it is reopened or a save is made
        if (!_saveCacheValid) { _saveCache = _app.ListSaves().Select(pa => (pa, SaveGame.Peek(pa))).ToList(); _saveCacheValid = true; }
        var saves = _saveCache;
        float top = Gui.BeginScroll("saves", new Rectangle(x, y, bw, r.Y + h - y - 10 * S), saves.Count * 40 * S);
        float sy = top;
        foreach (var (path, info) in saves)
        {
            var (_, date, player) = info;
            string label = Gui.Fit($"{Path.GetFileNameWithoutExtension(path)}   ({player}, {date})", bw - 36 * S, 15);
            if (Gui.Button(new Rectangle(x, sy, bw - 12 * S, 34 * S), label, true, "…/" + Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path), false, 15))
            {
                Ui.SaveMenuOpen = false;
                _app.LoadSave(path);
                Gui.EndScroll();
                return;
            }
            sy += 40 * S;
        }
        Gui.EndScroll();
        if (saves.Count == 0) Gui.Label("No saves yet.", x, y, 15, Gui.TextDim);
    }
}
