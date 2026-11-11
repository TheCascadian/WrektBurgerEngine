using System.Runtime.InteropServices;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Opt-in GPU stage timing with OpenGL timestamp queries (GL 3.3 core). Results are read a few frames later and only
/// once the driver says they are ready, so timing never stalls the pipeline. Each stamp first flushes raylib's batch,
/// so batched 2D draws land in the stage that issued them; that flush is the only cost when timing is on.
/// </summary>
/// <remarks>
/// A stage's time is the GPU clock between two stamps. When the GPU waits for the CPU to submit work, that wait counts
/// too, so a stage only means "GPU cost" while the frame is GPU-bound.
/// </remarks>
internal sealed unsafe class GpuTimer
{
    public static readonly string[] Names = { "terrain", "sites/figures", "labels", "overlays+ui" };
    private const uint GlTimestamp = 0x8E28, GlQueryResult = 0x8866, GlQueryResultAvailable = 0x8867;
    private const int InFlight = 6;
    private const int Stamps = 5; // frame start, after terrain, after world, after labels, frame end

    private static delegate* unmanaged[Cdecl]<int, uint*, void> _gen;
    private static delegate* unmanaged[Cdecl]<uint, uint, void> _counter;
    private static delegate* unmanaged[Cdecl]<uint, uint, ulong*, void> _get64;
    private static delegate* unmanaged[Cdecl]<uint, uint, int*, void> _getI;

    private readonly uint[] _queries = new uint[InFlight * Stamps];
    private readonly bool[] _pending = new bool[InFlight];
    private readonly int _capacity;
    private readonly double[][] _samples;
    private int _slot, _count, _next, _dropped;

    /// <summary>Frames collected since the timer was created (never reset): marks the start of a window for <see cref="Report(long)"/>.</summary>
    public long Collected { get; private set; }
    private bool _active;

    private GpuTimer(int capacity)
    {
        _capacity = capacity;
        _samples = Enumerable.Range(0, Names.Length + 1).Select(_ => new double[capacity]).ToArray();
        fixed (uint* q = _queries) _gen(_queries.Length, q);
    }

    /// <summary>A timer, or null when the driver does not expose timestamp queries (or BURGER_NO_GPU_TIMER=1).</summary>
    public static GpuTimer? TryCreate(int capacity)
    {
        if (Environment.GetEnvironmentVariable("BURGER_NO_GPU_TIMER") == "1" || !Load()) return null;
        return new GpuTimer(capacity);
    }

    private static bool Load()
    {
        if (_gen != null) return true;
        nint lib = 0;
        string[] names = OperatingSystem.IsWindows() ? new[] { "opengl32.dll" }
            : OperatingSystem.IsMacOS() ? new[] { "/System/Library/Frameworks/OpenGL.framework/OpenGL" }
            : new[] { "libGL.so.1", "libGL.so" };
        foreach (var n in names) if (NativeLibrary.TryLoad(n, out lib)) break;
        if (lib == 0) return false;
        // opengl32.dll exports only GL 1.1; later entry points come from the context. GLX exports them directly but
        // glXGetProcAddressARB is the documented way, so it is the fallback there.
        nint getProc = 0;
        if (OperatingSystem.IsWindows()) NativeLibrary.TryGetExport(lib, "wglGetProcAddress", out getProc);
        else if (!OperatingSystem.IsMacOS()) NativeLibrary.TryGetExport(lib, "glXGetProcAddressARB", out getProc);
        nint Find(string name)
        {
            if (NativeLibrary.TryGetExport(lib, name, out var p) && p != 0) return p;
            if (getProc == 0) return 0;
            var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* b = bytes) p = ((delegate* unmanaged<byte*, nint>)getProc)(b);
            return p is 0 or 1 or 2 or 3 or -1 ? 0 : p; // wglGetProcAddress can return small error values
        }
        nint gen = Find("glGenQueries"), counter = Find("glQueryCounter"), get64 = Find("glGetQueryObjectui64v"), getI = Find("glGetQueryObjectiv");
        if (gen == 0 || counter == 0 || get64 == 0 || getI == 0) return false;
        _gen = (delegate* unmanaged[Cdecl]<int, uint*, void>)gen;
        _counter = (delegate* unmanaged[Cdecl]<uint, uint, void>)counter;
        _get64 = (delegate* unmanaged[Cdecl]<uint, uint, ulong*, void>)get64;
        _getI = (delegate* unmanaged[Cdecl]<uint, uint, int*, void>)getI;
        return true;
    }

    /// <summary>Call right after BeginDrawing. Collects the oldest finished frame, then starts this one.</summary>
    public void Begin()
    {
        _slot = (_slot + 1) % InFlight;
        if (_pending[_slot]) Collect(_slot);
        _active = !_pending[_slot]; // still not ready after InFlight frames: skip this frame rather than wait
        if (!_active) { _dropped++; return; }
        Stamp(0);
    }

    /// <summary>Stamps the end of stage <paramref name="stage"/> (1 = terrain ... 4 = everything before EndDrawing).</summary>
    public void Mark(int stage)
    {
        if (!_active) return;
        Rlgl.DrawRenderBatchActive();
        Stamp(stage);
        if (stage == Stamps - 1) { _pending[_slot] = true; _active = false; }
    }

    private void Stamp(int i) => _counter(_queries[_slot * Stamps + i], GlTimestamp);

    private void Collect(int slot)
    {
        int ready = 0;
        _getI(_queries[slot * Stamps + Stamps - 1], GlQueryResultAvailable, &ready);
        if (ready == 0) return;
        var t = stackalloc ulong[Stamps];
        for (int i = 0; i < Stamps; i++) _get64(_queries[slot * Stamps + i], GlQueryResult, &t[i]);
        for (int s = 0; s < Names.Length; s++) _samples[s][_next] = (t[s + 1] - t[s]) / 1e6;
        _samples[Names.Length][_next] = (t[Stamps - 1] - t[0]) / 1e6;
        _next = (_next + 1) % _capacity;
        if (_count < _capacity) _count++;
        Collected++;
        _pending[slot] = false;
    }

    /// <summary>Percentiles over every frame collected since <see cref="Clear"/>, or since <paramref name="since"/> (a <see cref="Collected"/> value).</summary>
    public string Report(long since = -1)
    {
        int n = since < 0 ? _count : (int)Math.Min(_count, Collected - since);
        if (n <= 0) return "gpu n/a";
        string P(double[] values)
        {
            var ordered = new double[n];
            for (int i = 0; i < n; i++) ordered[i] = values[((_next - n + i) % _capacity + _capacity) % _capacity];
            Array.Sort(ordered);
            double At(double q) => ordered[Math.Min(n - 1, (int)Math.Ceiling(q * n) - 1)];
            return $"p50/p95/p99/max {At(.50):0.00}/{At(.95):0.00}/{At(.99):0.00}/{ordered[^1]:0.00}ms";
        }
        var parts = new List<string> { $"gpu n={n} dropped={_dropped} total {P(_samples[Names.Length])}" };
        for (int s = 0; s < Names.Length; s++) parts.Add($"gpu {Names[s]} {P(_samples[s])}");
        return string.Join("; ", parts);
    }

    /// <summary>Forgets collected samples; frames still in flight are collected into the new window.</summary>
    public void Clear() { _count = 0; _next = 0; _dropped = 0; }
}
