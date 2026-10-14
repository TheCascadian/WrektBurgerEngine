using System.Diagnostics;
using System.Numerics;
using Burger.Core;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Scripted, repeatable render benchmark (--bench). Flies the camera through fixed phases with vsync and the frame cap off,
/// so frame time is the real cost, and logs per-phase frame percentiles plus the per-stage CPU breakdown.
/// Variants (environment): BENCH_SKIP=map|sea|terrain|trees|ui (comma list), BURGER_NO_MSAA=1.
/// </summary>
internal sealed class Bench
{
    public static readonly HashSet<string> Skip =
        new((Environment.GetEnvironmentVariable("BENCH_SKIP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    private sealed record Phase(string Name, double Lon0, double Lat0, double D0, double Lon1, double Lat1, double D1, double Seconds);

    private static readonly Phase[] Phases =
    {
        new("far-static",   0, 20, 800, 0, 20, 800, 6),
        new("far-pan",    -90, 20, 800, 90, 20, 800, 6),
        new("mid-static",  10, 45, 300, 10, 45, 300, 6),
        new("mid-pan",    -10, 45, 300, 40, 45, 300, 6),
        new("near-static", 12, 42, 40, 12, 42, 40, 6),
        new("near-pan",     8, 40, 40, 20, 46, 40, 6),
        new("zoom-sweep",  15, 35, 1500, 15, 35, 10, 8),
    };

    private const double Warmup = 1.0;
    private int _phase = -1;
    private double _t;
    private readonly List<double> _frames = new();
    private long _last = Stopwatch.GetTimestamp();
    private readonly string _variant = $"skip=[{string.Join(',', Skip)}] msaa={(Environment.GetEnvironmentVariable("BURGER_NO_MSAA") == "1" ? "off" : "4x")}";

    /// <summary>Call once per playing frame before camera update. Returns true when the run is finished.</summary>
    public bool Step(CameraController cam, MapRenderer map, RenderPerf perf)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = (now - _last) / (double)Stopwatch.Frequency;
        _last = now;

        if (_phase < 0) { StartPhase(0, perf); }
        _t += dt;
        var p = Phases[_phase];
        if (_t >= Warmup) _frames.Add(dt * 1000.0);

        if (_t >= p.Seconds)
        {
            Report(p, perf);
            if (_phase + 1 >= Phases.Length) return true;
            StartPhase(_phase + 1, perf);
            p = Phases[_phase];
        }

        double f = Math.Clamp(_t / p.Seconds, 0, 1);
        double lon = p.Lon0 + (p.Lon1 - p.Lon0) * f, lat = p.Lat0 + (p.Lat1 - p.Lat0) * f;
        double d = p.D0 * Math.Pow(p.D1 / p.D0, f); // geometric so zoom feels even
        float x = (float)((lon + 180) / 360 * map.WorldWidth), z = (float)((90 - lat) / 180 * map.WorldHeight);
        cam.LookAtFlat(x, z, (float)d);
        return false;
    }

    private void StartPhase(int i, RenderPerf perf)
    {
        _phase = i; _t = 0; _frames.Clear(); perf.Clear();
        _last = Stopwatch.GetTimestamp();
    }

    private void Report(Phase p, RenderPerf perf)
    {
        if (_frames.Count == 0) return;
        var s = _frames.OrderBy(v => v).ToArray();
        double At(double q) => s[Math.Min(s.Length - 1, (int)Math.Ceiling(q * s.Length) - 1)];
        double avg = s.Average();
        Log.Info($"BENCH {_variant} {p.Name}: n={s.Length} fps={1000.0 / avg:0.0} frame_ms avg={avg:0.00} p50={At(.5):0.00} p95={At(.95):0.00} p99={At(.99):0.00} max={s[^1]:0.00}");
        Log.Info($"BENCH {_variant} {p.Name} stages: {perf.Report(p.Seconds - Warmup, 0, 1)}");
    }
}
