using System.Diagnostics;
using System.Numerics;
using Burger.Core;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Scripted, repeatable render benchmark (--bench). Flies the camera along one continuous path of legs (no jumps or resets between them) with vsync and the frame cap off,
/// so frame time is the real cost, and logs per-leg and overall frame percentiles plus one per-stage CPU breakdown for the whole pass.
/// Variants (environment): BENCH_SKIP=map|sea|terrain|trees|ui (comma list), BURGER_NO_MSAA=1.
/// </summary>
internal sealed class Bench
{
    public static readonly HashSet<string> Skip =
        new((Environment.GetEnvironmentVariable("BENCH_SKIP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    // One continuous camera path: every leg starts where the previous one ended, so the camera never jumps and the run is a single pass.
    private sealed record Leg(string Name, double Lon, double Lat, double Dist, double Seconds);

    private const double StartLon = 0, StartLat = 20, StartDist = 800;

    private static readonly Leg[] Legs =
    {
        new("far-static",   0, 20, 800, 6),
        new("far-pan",     90, 20, 800, 6),
        new("to-mid",      10, 45, 300, 4),
        new("mid-static",  10, 45, 300, 6),
        new("mid-pan",     40, 45, 300, 6),
        new("to-near",     12, 42,  40, 4),
        new("near-static", 12, 42,  40, 6),
        new("near-pan",    20, 46,  40, 6),
        new("zoom-sweep",  15, 35,1500, 8),
    };

    private const double Warmup = 1.0;
    private int _leg;
    private double _t, _total;
    private double _lon0 = StartLon, _lat0 = StartLat, _d0 = StartDist;
    private bool _started;
    private readonly List<double> _frames = new();
    private readonly List<double> _all = new();
    private long _last = Stopwatch.GetTimestamp();
    private readonly string _variant = $"skip=[{string.Join(',', Skip)}] msaa={(Environment.GetEnvironmentVariable("BURGER_NO_MSAA") == "1" ? "off" : "4x")}";

    /// <summary>Call once per playing frame before camera update. Returns true when the run is finished.</summary>
    public bool Step(CameraController cam, MapRenderer map, RenderPerf perf)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = (now - _last) / (double)Stopwatch.Frequency;
        _last = now;

        if (!_started) { _started = true; perf.Clear(); dt = 0; }
        _t += dt; _total += dt;
        if (_total >= Warmup) { _frames.Add(dt * 1000.0); _all.Add(dt * 1000.0); }

        var g = Legs[_leg];
        if (_t >= g.Seconds)
        {
            Report(g.Name, _frames);
            _frames.Clear();
            _lon0 = g.Lon; _lat0 = g.Lat; _d0 = g.Dist; _t -= g.Seconds;
            if (++_leg >= Legs.Length)
            {
                Report("ALL", _all);
                Log.Info($"BENCH {_variant} ALL stages: {perf.Report(_total - Warmup, 0, 1)}");
                return true;
            }
            g = Legs[_leg];
        }

        double f = Math.Clamp(_t / g.Seconds, 0, 1);
        double lon = _lon0 + (g.Lon - _lon0) * f, lat = _lat0 + (g.Lat - _lat0) * f;
        double d = _d0 * Math.Pow(g.Dist / _d0, f); // geometric so zoom feels even
        float x = (float)((lon + 180) / 360 * map.WorldWidth), z = (float)((90 - lat) / 180 * map.WorldHeight);
        cam.LookAtFlat(x, z, (float)d);
        return false;
    }

    private void Report(string name, List<double> frames)
    {
        if (frames.Count == 0) return;
        var s = frames.OrderBy(v => v).ToArray();
        double At(double q) => s[Math.Min(s.Length - 1, (int)Math.Ceiling(q * s.Length) - 1)];
        double avg = s.Average();
        Log.Info($"BENCH {_variant} {name}: n={s.Length} fps={1000.0 / avg:0.0} frame_ms avg={avg:0.00} p50={At(.5):0.00} p95={At(.95):0.00} p99={At(.99):0.00} max={s[^1]:0.00}");
    }
}
