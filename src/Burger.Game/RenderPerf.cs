using System.Diagnostics;
using System.Text;

namespace Burger.Runtime;

/// <summary>Opt-in, allocation-free per-frame CPU timing for presentation stages.</summary>
/// <remarks>
/// Percentiles come from a ring of the most recent <c>capacity</c> frames; the report prints that sample count. The
/// frame count, FPS, allocation and GC figures cover every frame since the last <see cref="Clear"/>, ring or not.
/// </remarks>
internal sealed class RenderPerf
{
    private static readonly string[] Names = { "input/pick", "map", "labels", "orders", "units", "fleets", "air", "hover", "ui", "EndDrawing/flush+swap" };
    private readonly int _capacity;
    private readonly double[][] _samples;
    private readonly double[] _frameMs;
    private readonly double[] _intervalMs; // wall time from the previous frame's start: what the player sees, ticks and vsync included
    private long _lastBegin;
    private int _hitches;
    private int _count, _next;
    private long _frames;
    private long _stageStart, _frameStart, _allocated;
    private TimeSpan _pauseStart;
    private int _stage;
    private long _stageAlloc;
    private readonly double[] _allocByStage = new double[Names.Length];
    private readonly double[] _lastStageMs = new double[Names.Length];
    private double _allocTotalKb, _pauseTotalMs, _lastFrameMs, _lastAllocKb, _lastPauseMs;
    private int _gc0, _gc1, _gc2, _lastGc0, _lastGc1, _lastGc2, _gcStart0, _gcStart1, _gcStart2;

    /// <summary>GPU stage timing, when the driver supports it.</summary>
    public readonly GpuTimer? Gpu;

    public RenderPerf(int capacity = 600)
    {
        Gpu = GpuTimer.TryCreate(capacity);
        _capacity = capacity;
        _samples = Enumerable.Range(0, Names.Length).Select(_ => new double[capacity]).ToArray();
        _frameMs = new double[capacity];
        _intervalMs = new double[capacity];
        Clear();
    }

    public void Begin()
    {
        _frameStart = _stageStart = Stopwatch.GetTimestamp();
        double interval = _lastBegin == 0 ? 0 : (_frameStart - _lastBegin) * 1000.0 / Stopwatch.Frequency;
        _intervalMs[_next] = interval;
        if (interval > 50) _hitches++;
        _lastBegin = _frameStart;
        _allocated = _stageAlloc = GC.GetAllocatedBytesForCurrentThread();
        _pauseStart = GC.GetTotalPauseDuration();
        _stage = 0;
        _gcStart0 = GC.CollectionCount(0); _gcStart1 = GC.CollectionCount(1); _gcStart2 = GC.CollectionCount(2);
    }

    public void Mark(int nextStage)
    {
        long now = Stopwatch.GetTimestamp();
        _samples[_stage][_next] = _lastStageMs[_stage] = (now - _stageStart) * 1000.0 / Stopwatch.Frequency;
        long al = GC.GetAllocatedBytesForCurrentThread(); _allocByStage[_stage] += (al - _stageAlloc) / 1024.0; _stageAlloc = al;
        _stage = nextStage;
        _stageStart = now;
    }

    /// <summary>Closes the frame. The frame time is measured from <see cref="Begin"/> to here (CPU side, including the swap).</summary>
    public void End()
    {
        long now = Stopwatch.GetTimestamp();
        _samples[_stage][_next] = _lastStageMs[_stage] = (now - _stageStart) * 1000.0 / Stopwatch.Frequency;
        long al = GC.GetAllocatedBytesForCurrentThread();
        _allocByStage[_stage] += (al - _stageAlloc) / 1024.0;
        _frameMs[_next] = _lastFrameMs = (now - _frameStart) * 1000.0 / Stopwatch.Frequency;
        _lastAllocKb = (al - _allocated) / 1024.0;
        _lastPauseMs = (GC.GetTotalPauseDuration() - _pauseStart).TotalMilliseconds;
        _allocTotalKb += _lastAllocKb;
        _pauseTotalMs += _lastPauseMs;
        _lastGc0 = GC.CollectionCount(0) - _gcStart0; _lastGc1 = GC.CollectionCount(1) - _gcStart1; _lastGc2 = GC.CollectionCount(2) - _gcStart2;
        _gc0 += _lastGc0; _gc1 += _lastGc1; _gc2 += _lastGc2;
        _next = (_next + 1) % _capacity;
        if (_count < _capacity) _count++;
        _frames++;
    }

    /// <summary>Stage breakdown of the frame <see cref="End"/> closed last, for logging a hitch.</summary>
    public string LastFrame()
    {
        var sb = new StringBuilder();
        sb.Append($"frame {_lastFrameMs:0.0}ms alloc {_lastAllocKb:0}KB gc {_lastGc0}/{_lastGc1}/{_lastGc2} pause {_lastPauseMs:0.0}ms;");
        for (int s = 0; s < Names.Length; s++)
            if (_lastStageMs[s] >= 0.5) sb.Append($" {Names[s]} {_lastStageMs[s]:0.0}");
        return sb.ToString();
    }

    public string Report(double windowSeconds, double simMs, double simFrames)
    {
        if (_count == 0) return string.Empty;
        string P(double[] values, string suffix = "ms")
        {
            var ordered = new double[_count];
            Array.Copy(values, ordered, _count);
            Array.Sort(ordered);
            double At(double q) => ordered[Math.Min(_count - 1, (int)Math.Ceiling(q * _count) - 1)];
            return $"p50/p95/p99/max {At(.50):0.0}/{At(.95):0.0}/{At(.99):0.0}/{ordered[^1]:0.0}{suffix}";
        }
        long frames = Math.Max(1, _frames);
        var parts = new List<string>(Names.Length + 2)
        {
            $"frames {_frames} (percentiles over last {_count})", $"render frame {P(_frameMs)}", $"frame interval {P(_intervalMs)}, {_hitches} over 50 ms", $"avg FPS {_frames / windowSeconds:0.0}",
            $"alloc {_allocTotalKb / frames:0.0} KB/frame", $"GC Δ {_gc0}/{_gc1}/{_gc2}, pause {_pauseTotalMs:0.0} ms"
        };
        for (int s = 0; s < Names.Length; s++) parts.Add($"{Names[s]} {P(_samples[s])}");
        parts.Add("KB/frame by stage " + string.Join(" ", Names.Select((n, i) => $"{n} {_allocByStage[i] / frames:0.0}")));
        parts.Add($"simulation {simMs / Math.Max(1, simFrames):0.0} ms/frame");
        parts.Add(Gpu?.Report() ?? "gpu timer unavailable");
        return string.Join("; ", parts);
    }

    public void Clear()
    {
        _count = 0; _next = 0; _frames = 0; _hitches = 0;
        _allocTotalKb = _pauseTotalMs = 0;
        _gc0 = _gc1 = _gc2 = 0;
        Array.Clear(_allocByStage);
        Gpu?.Clear();
    }
}
