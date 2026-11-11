using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Keeps the game running on any graphics card. Four levels of detail for the map, chosen by hand or (the default) by measuring: how long the card really takes for the map is timed each frame,
/// a level that does not fit into the frame steps down within a second or two, and when there is plenty of room it steps up again, slowly. The level reached is remembered for the next start.
/// </summary>
internal sealed class GraphicsQuality
{
    public const int Levels = 4;
    public static readonly string[] Names = { "High", "Medium", "Low", "Lowest" };

    /// <summary>The level in use (0 High .. 3 Lowest).</summary>
    public static int Level { get; private set; }

    // what the level means
    public static float ResolutionScale => Level switch { 0 or 1 => 1f, 2 => 0.8f, _ => 0.62f };
    /// <summary>Waves of the sea lift the mesh (a few noise functions for every vertex at sea level).</summary>
    public static bool SeaSwell => Level <= 1;
    /// <summary>The figures of the soldiers on the map (a counter still shows them).</summary>
    public static bool Figures => Level <= 2;
    /// <summary>The smallest name on the map, in pixels: a lot of tiny names cost much and can hardly be read.</summary>
    public static float SmallestLabel => Level switch { 0 => 4f, 1 => 5f, 2 => 6f, _ => 7f };
    /// <summary>How much of the small details the map shader keeps, on top of the player's own level of detail settings.</summary>
    public static float WaterDetail => Level switch { 0 => 1f, 1 => 0.5f, 2 => 0.001f, _ => 0.001f };
    public static float RiverDetail => Level switch { 0 => 1f, 1 => 0.6f, 2 => 0.25f, _ => 0.05f };
    public static float TerrainDetail => Level switch { 0 => 1f, 1 => 0.6f, 2 => 0.25f, _ => 0.05f };

    /// <summary>Applies a level to everything that reads it directly.</summary>
    public static void Set(int level)
    {
        Level = Math.Clamp(level, 0, Levels - 1);
        MapRenderer.TerrainPixelError = Level switch { 0 => 18f, 1 => 26f, 2 => 38f, _ => 60f };
        MapRenderer.TerrainFinestLevel = Level switch { 0 or 1 => 0, 2 => 1, _ => 2 };
        MapRenderer.TreesAllowed = Level <= 1;
        MapRenderer.MultisampleAllowed = Level == 0;
    }

    /// <summary>
    /// Where to begin when nothing is known yet about this computer: a card that shares the main memory starts low and may step up,
    /// so that the first minutes are not the worst. Software renderers start at the bottom.
    /// </summary>
    public static int FirstGuess(string renderer)
    {
        string r = renderer.ToLowerInvariant();
        if (r.Contains("llvmpipe") || r.Contains("swiftshader") || r.Contains("softpipe") || r.Contains("microsoft basic") || r.Contains("gdi generic")) return 3;
        if (r.Contains("intel") || r.Contains("uhd") || r.Contains("iris") || r.Contains("vega") || r.Contains("radeon(tm) graphics") || r.Contains("radeon graphics") || r.Contains("mali") || r.Contains("adreno")) return 2;
        return 0;
    }

    // -------------------------------------------------------------------------------- the measuring
    private double _windowStart = -1, _gpuSum, _frameSum;
    private int _samples, _gpuSamples;
    private double _holdUntil;
    private int _roomWindows;
    private double _lastDrop = double.NegativeInfinity;
    private double _upWait = 6;   // (the level remembered from last time may be from a busy moment: it climbs back quickly until it meets a real limit)

    /// <summary>The level changed by itself (remember it).</summary>
    public event Action? Changed;

    /// <summary>
    /// Call once a frame with how long the card took for the map (negative if that cannot be measured), how long the whole frame took and how long it may take.
    /// </summary>
    public void Frame(double gpuMs, double frameMs, double budgetMs, double now)
    {
        if (_windowStart < 0) _windowStart = now;
        _frameSum += frameMs;
        _samples++;
        if (gpuMs >= 0) { _gpuSum += gpuMs; _gpuSamples++; }
        if (now - _windowStart < 0.75) return;
        double gpu = _gpuSamples > 0 ? _gpuSum / _gpuSamples : -1;
        double frame = _frameSum / Math.Max(1, _samples);
        _windowStart = now; _gpuSum = _frameSum = 0; _samples = _gpuSamples = 0;
        if (now < _holdUntil) return;

        // too slow: the card alone takes most of the frame (or, without a timer, the frame is long although the program is not busy)
        bool slow = gpu >= 0 ? gpu > budgetMs * 0.8 : frame > budgetMs * 1.5;
        if (slow && Level < Levels - 1)
        {
            Set(Level + 1);
            _holdUntil = now + 1.5;
            _roomWindows = 0;
            _lastDrop = now;
            Log(gpu, frame);
            return;
        }
        // room to spare for a long time: try the next level up. If that goes wrong soon, wait much longer before the next try.
        if (gpu >= 0 && gpu < budgetMs * 0.3 && Level > 0)
        {
            if (++_roomWindows * 0.75 < _upWait) return;
            _roomWindows = 0;
            if (now - _lastDrop < 45) _upWait = Math.Min(Math.Max(_upWait, 15) * 3, 600);
            Set(Level - 1);
            _holdUntil = now + 2;
            Log(gpu, frame);
        }
        else _roomWindows = 0;
    }

    private void Log(double gpu, double frame)
    {
        Burger.Core.Log.Info($"Graphics quality: {Names[Level]} (map {gpu:0.0} ms on the card, frame {frame:0.0} ms)");
        Changed?.Invoke();
    }
}

/// <summary>
/// Waiting for the screen. Vsync keeps the picture clean, but a frame that just misses a refresh waits for the next one: 13 ms of work becomes 26 ms on screen. So vsync is on while the game keeps
/// up with the screen, and off while it does not; and it paces the frames by itself instead of being fought by a second limit (60 frames a second on a 75 Hz screen would mean an uneven 13 and 26 ms).
/// </summary>
internal sealed class FramePacing
{
    private bool _vsyncOn;
    private int _late, _early;
    private int _cap = -1, _swap = -1;
    private readonly bool _allowed;

    public FramePacing(bool allowed)
    {
        _allowed = allowed;
        _vsyncOn = allowed;
    }

    /// <summary>The time a frame may take on this screen, in milliseconds.</summary>
    public double BudgetMs(int cap)
    {
        int hz = Raylib.GetMonitorRefreshRate(Raylib.GetCurrentMonitor());
        if (hz <= 0) hz = 60;
        return 1000.0 / Math.Min(hz, cap);
    }

    /// <summary>Call once a frame with how long the program and the card were really busy this frame (without any waiting).</summary>
    public void Frame(double busyMs, int cap)
    {
        int hz = Raylib.GetMonitorRefreshRate(Raylib.GetCurrentMonitor());
        if (hz <= 0) hz = 60;
        double refresh = 1000.0 / hz;
        // every n-th refresh: the screen's rate, or the nearest to the frame cap below it
        int every = Math.Max(1, (int)Math.Round((double)hz / Math.Max(1, cap)));
        double interval = refresh * every;
        if (_allowed)
        {
            if (_vsyncOn)
            {
                _late = busyMs > interval * 0.96 ? _late + 1 : Math.Max(0, _late - 1);
                if (_late > 20) { _vsyncOn = false; _late = _early = 0; }
            }
            else
            {
                _early = busyMs < interval * 0.75 ? _early + 1 : 0;
                if (_early > 180) { _vsyncOn = true; _late = _early = 0; }
            }
        }
        // with vsync the screen sets the pace; without, the frame cap does
        int wantCap = _vsyncOn ? 0 : cap;
        int wantSwap = _vsyncOn ? every : 0;
        if (wantCap != _cap) { Raylib.SetTargetFPS(wantCap); _cap = wantCap; }
        if (wantSwap != _swap)
        {
            _swap = wantSwap;
            if (!Gl.SwapInterval(wantSwap))
            {
                // no wgl here: raylib's own switch (every refresh or none)
                if (wantSwap > 0) Raylib.SetWindowState(ConfigFlags.VSyncHint); else Raylib.ClearWindowState(ConfigFlags.VSyncHint);
            }
        }
    }

    public bool VsyncOn => _vsyncOn;

    /// <summary>The frame cap was changed elsewhere: set it again at the next frame.</summary>
    public void Invalidate() { _cap = -1; _swap = -1; }
}
