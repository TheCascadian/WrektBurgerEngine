using System.Globalization;

namespace Burger.Runtime;

/// <summary>
/// Player settings kept between sessions in <c>settings.cfg</c> next to the exe (one <c>key = value</c> per line).
/// </summary>
public static class UserSettings
{
    /// <summary>Most in-game days per real second at speed 5; 0 = as fast as the computer allows.</summary>
    public static double Speed5DaysPerSecond;
    /// <summary>Historical bias of the AI for new games (0 = free alternate history, 1 = strongly historical).</summary>
    public static double HistoricalBias = 0.75;
    /// <summary>
    /// Optional ahistorical AI: nations follow their own interests instead of their history (bias 0). Off by default, so the
    /// historical path stays the standard. Less accurate and plausible, but it frees the player to take any role.
    /// </summary>
    public static bool Ahistorical;
    /// <summary>Switches off every non-player country's AI: they stand still (no moves, diplomacy, recruitment or production).</summary>
    public static bool AiDisabled;
    /// <summary>The bias the AI actually uses: the player's setting, or 0 in ahistorical mode.</summary>
    public static double EffectiveBias => Ahistorical ? 0 : HistoricalBias;
    /// <summary>Play the sound of animated portraits (the speaker button on a character portrait toggles it).</summary>
    public static bool PortraitSound = true;
    /// <summary>Draw the map as a globe instead of the flat, east-west wrapping map.</summary>
    public static bool GlobeMode;
    /// <summary>Set from the game folder (game.txt <c>globe_mode</c>); false hides the toggle and keeps the map flat.</summary>
    public static bool GlobeAllowed = true;
    public static bool GlobeOn => GlobeMode && GlobeAllowed;
    /// <summary>The sun circles the map with the game's clock. Off: a fixed sun.</summary>
    public static bool SunCycle = true;
    /// <summary>Days per real second at speed 1; 0 = the game's default (0.5). Lets the clock run slower than the default.</summary>
    public static double Speed1DaysPerSecond;

    /// <summary>Level of detail: on, small map details fade out when zoomed out (faster); off, they stay at every zoom (slower on weak graphics cards).</summary>
    public static bool LodEnabled = true;
    /// <summary>Per feature, how far you can zoom out before its detail fades: 0 low, 1 default, 2 high, 3 max. Only used while LOD is on.</summary>
    public static int LodWater = 1, LodRiver = 1, LodTerrain = 1;
    public static readonly string[] LodNames = { "Low", "Default", "High", "Max" };
    private static readonly float[] LodScales = { 0.5f, 1f, 2.5f, 8f };
    /// <summary>The shader's distance scale for a level; with LOD off, every feature keeps its full detail.</summary>
    public static float LodScale(int level) => LodEnabled ? LodScales[Math.Clamp(level, 0, 3)] : 1000f;

    /// <summary>The name other players see in multiplayer.</summary>
    public static string PlayerName = "";
    /// <summary>Hosting opens the router's port by itself (UPnP). Off: only players on the same network can join.</summary>
    public static bool OpenRouter = true;

    public static string EffectiveName => PlayerName.Trim().Length > 0 ? PlayerName.Trim() : Environment.UserName;

    private static string PathOf => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.cfg");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathOf)) return;
            foreach (var raw in File.ReadAllLines(PathOf))
            {
                var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0] == "player_name") { PlayerName = parts[1].Length > 24 ? parts[1][..24] : parts[1]; continue; }
                if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (parts[0] == "speed5_days_per_second") Speed5DaysPerSecond = Math.Max(0, v);
                else if (parts[0] == "historical_bias") HistoricalBias = Math.Clamp(v, 0, 1);
                else if (parts[0] == "ahistorical") Ahistorical = v != 0;
                else if (parts[0] == "ai_disabled") AiDisabled = v != 0;
                else if (parts[0] == "portrait_sound") PortraitSound = v != 0;
                else if (parts[0] == "globe_mode") GlobeMode = v != 0;
                else if (parts[0] == "sun_cycle") SunCycle = v != 0;
                else if (parts[0] == "open_router") OpenRouter = v != 0;
                else if (parts[0] == "lod_enabled") LodEnabled = v != 0;
                else if (parts[0] == "lod_water") LodWater = Math.Clamp((int)v, 0, 3);
                else if (parts[0] == "lod_river") LodRiver = Math.Clamp((int)v, 0, 3);
                else if (parts[0] == "lod_terrain") LodTerrain = Math.Clamp((int)v, 0, 3);
                else if (parts[0] == "speed1_days_per_second") Speed1DaysPerSecond = Math.Max(0, v);
            }
        }
        catch { /* settings are a convenience: the defaults will do */ }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllLines(PathOf, new[]
            {
                $"speed5_days_per_second = {Speed5DaysPerSecond.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"historical_bias = {HistoricalBias.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"ahistorical = {(Ahistorical ? 1 : 0)}",
                $"ai_disabled = {(AiDisabled ? 1 : 0)}",
                $"portrait_sound = {(PortraitSound ? 1 : 0)}",
                $"globe_mode = {(GlobeMode ? 1 : 0)}",
                $"sun_cycle = {(SunCycle ? 1 : 0)}",
                $"player_name = {PlayerName.Replace('\n', ' ').Replace('\r', ' ')}",
                $"open_router = {(OpenRouter ? 1 : 0)}",
                $"lod_enabled = {(LodEnabled ? 1 : 0)}",
                $"lod_water = {LodWater}",
                $"lod_river = {LodRiver}",
                $"lod_terrain = {LodTerrain}",
                $"speed1_days_per_second = {Speed1DaysPerSecond.ToString("0.####", CultureInfo.InvariantCulture)}",
            });
        }
        catch { }
    }

    /// <summary>Gives the clock the speed 5 limit (the other speeds come from the game's defines).</summary>
    public static void Apply(Burger.Core.Game game)
    {
        game.Clock.DaysPerSecond[5] = Speed5DaysPerSecond;
        if (Speed1DaysPerSecond > 0) game.Clock.DaysPerSecond[1] = Speed1DaysPerSecond;
        game.HistoricalBias = EffectiveBias;
        game.Ai.Enabled = !AiDisabled;
    }
}
