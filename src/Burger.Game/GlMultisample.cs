using System.Runtime.InteropServices;

namespace Burger.Runtime;

/// <summary>The few OpenGL calls raylib does not offer: what the graphics card is, how long it took to draw (timer queries), the swap interval, multisampling on and off. Everything is optional: a call that cannot be loaded does nothing.</summary>
internal static unsafe class Gl
{
    private static IntPtr _lib;
    private static bool _libTried;
    private static delegate* unmanaged[Cdecl]<byte*, IntPtr> _getProc;

    private static void LoadLib()
    {
        _libTried = true;
        string[] names = OperatingSystem.IsWindows() ? new[] { "opengl32.dll" }
            : OperatingSystem.IsMacOS() ? new[] { "/System/Library/Frameworks/OpenGL.framework/OpenGL" }
            : new[] { "libGL.so.1", "libGL.so" };
        foreach (var n in names)
            if (NativeLibrary.TryLoad(n, out _lib)) break;
        if (_lib == IntPtr.Zero) return;
        foreach (var name in new[] { "wglGetProcAddress", "glXGetProcAddressARB", "glXGetProcAddress" })
            if (NativeLibrary.TryGetExport(_lib, name, out var p)) { _getProc = (delegate* unmanaged[Cdecl]<byte*, IntPtr>)p; break; }
    }

    private static IntPtr Proc(string name)
    {
        if (!_libTried) LoadLib();
        if (_lib == IntPtr.Zero) return IntPtr.Zero;
        if (NativeLibrary.TryGetExport(_lib, name, out var direct)) return direct;
        if (_getProc == null) return IntPtr.Zero;
        var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* b = bytes)
        {
            var p = _getProc(b);
            // (wglGetProcAddress may answer 1, 2, 3 or -1 for "no")
            return (long)p is >= -1 and <= 3 ? IntPtr.Zero : p;
        }
    }

    // ------------------------------------------------------------------------------------- what the card is
    /// <summary>The name of the graphics card as the driver gives it ("" if it cannot be asked).</summary>
    public static string Renderer()
    {
        var p = Proc("glGetString");
        if (p == IntPtr.Zero) return "";
        var s = ((delegate* unmanaged[Cdecl]<uint, byte*>)p)(0x1F01);
        return s == null ? "" : Marshal.PtrToStringAnsi((IntPtr)s) ?? "";
    }

    /// <summary>The largest texture the card takes (0 if it cannot be asked).</summary>
    public static int MaxTextureSize()
    {
        var p = Proc("glGetIntegerv");
        if (p == IntPtr.Zero) return 0;
        int v = 0;
        ((delegate* unmanaged[Cdecl]<uint, int*, void>)p)(0x0D33, &v);
        return v;
    }

    /// <summary>The memory of the graphics card in megabytes, where the driver says (0 if it does not: Intel and the like, which share the main memory).</summary>
    public static int VideoMemoryMb()
    {
        var p = Proc("glGetIntegerv");
        var e = Proc("glGetError");
        if (p == IntPtr.Zero || e == IntPtr.Zero) return 0;
        var getInt = (delegate* unmanaged[Cdecl]<uint, int*, void>)p;
        var getError = (delegate* unmanaged[Cdecl]<uint>)e;
        while (getError() != 0) { }
        int nvidia = 0;
        getInt(0x9048, &nvidia);       // GL_GPU_MEMORY_INFO_TOTAL_AVAILABLE_MEMORY_NVX, in kB
        bool ok = getError() == 0 && nvidia > 0;
        if (ok) return nvidia / 1024;
        int* amd = stackalloc int[4];
        amd[0] = 0;
        getInt(0x87FC, amd);           // GL_TEXTURE_FREE_MEMORY_ATI, in kB, free memory
        ok = getError() == 0 && amd[0] > 0;
        return ok ? amd[0] / 1024 : 0;
    }

    // ------------------------------------------------------------------------------------- multisampling
    private const uint GlMultisampleCap = 0x809D;
    private static delegate* unmanaged[Cdecl]<uint, void> _enable, _disable;
    private static bool _msTried, _msOn = true;

    /// <summary>Turns multisampling on and off at run time (the window keeps its multisampled framebuffer; only the rasterizer changes).</summary>
    public static void Multisample(bool on)
    {
        if (on == _msOn) return;
        if (!_msTried)
        {
            _msTried = true;
            _enable = (delegate* unmanaged[Cdecl]<uint, void>)Proc("glEnable");
            _disable = (delegate* unmanaged[Cdecl]<uint, void>)Proc("glDisable");
        }
        if (_enable == null || _disable == null) return;
        if (on) _enable(GlMultisampleCap); else _disable(GlMultisampleCap);
        _msOn = on;
    }

    // ------------------------------------------------------------------------------------- swap interval
    private static delegate* unmanaged[Cdecl]<int, int> _swapInterval;
    private static bool _swapTried;
    private static int _swapNow = int.MinValue;

    /// <summary>Waits for this many screen refreshes at each swap: 1 is vsync, 0 none, -1 vsync that lets a late frame through at once where the driver offers that. Returns false if it cannot be set here.</summary>
    public static bool SwapInterval(int n)
    {
        if (!_swapTried)
        {
            _swapTried = true;
            _swapInterval = (delegate* unmanaged[Cdecl]<int, int>)Proc("wglSwapIntervalEXT");
        }
        if (_swapInterval == null) return false;
        if (_swapNow == n) return true;
        _swapNow = n;
        _swapInterval(n);
        return true;
    }

    // ------------------------------------------------------------------------------------- how long the card took
    private static delegate* unmanaged[Cdecl]<int, uint*, void> _genQueries;
    private static delegate* unmanaged[Cdecl]<uint, uint, void> _beginQuery;
    private static delegate* unmanaged[Cdecl]<uint, void> _endQuery;
    private static delegate* unmanaged[Cdecl]<uint, uint, int*, void> _getQueryiv;
    private static delegate* unmanaged[Cdecl]<uint, uint, ulong*, void> _getQueryui64v;
    private static bool _queryTried;
    private const uint TimeElapsed = 0x88BF, QueryResult = 0x8866, QueryResultAvailable = 0x8867;

    private static bool LoadQueries()
    {
        if (_queryTried) return _genQueries != null;
        _queryTried = true;
        var a = Proc("glGenQueries"); var b = Proc("glBeginQuery"); var c = Proc("glEndQuery"); var d = Proc("glGetQueryObjectiv"); var e = Proc("glGetQueryObjectui64v");
        if (a == IntPtr.Zero || b == IntPtr.Zero || c == IntPtr.Zero || d == IntPtr.Zero || e == IntPtr.Zero) return false;
        _genQueries = (delegate* unmanaged[Cdecl]<int, uint*, void>)a;
        _beginQuery = (delegate* unmanaged[Cdecl]<uint, uint, void>)b;
        _endQuery = (delegate* unmanaged[Cdecl]<uint, void>)c;
        _getQueryiv = (delegate* unmanaged[Cdecl]<uint, uint, int*, void>)d;
        _getQueryui64v = (delegate* unmanaged[Cdecl]<uint, uint, ulong*, void>)e;
        return true;
    }

    /// <summary>Several timer queries in a ring, so that asking for a result never makes the program wait for the card.</summary>
    public sealed class GpuTimer
    {
        private readonly uint[] _ids = new uint[4];
        private int _write, _read, _pending;
        private readonly bool _ok;
        private bool _running;
        /// <summary>The last time measured, in milliseconds (negative until there is one, and always where the driver has no timer queries).</summary>
        public double LastMs { get; private set; } = -1;

        public GpuTimer()
        {
            _ok = LoadQueries();
            if (_ok) fixed (uint* p = _ids) _genQueries(_ids.Length, p);
        }

        public void Begin()
        {
            if (!_ok || _running) return;
            Collect();
            if (_pending >= _ids.Length) return;   // the card is that far behind: skip a measurement rather than wait
            _beginQuery(TimeElapsed, _ids[_write]);
            _running = true;
        }

        public void End()
        {
            if (!_running) return;
            _endQuery(TimeElapsed);
            _running = false;
            _write = (_write + 1) % _ids.Length;
            _pending++;
        }

        private void Collect()
        {
            while (_pending > 0)
            {
                int ready = 0;
                _getQueryiv(_ids[_read], QueryResultAvailable, &ready);
                if (ready == 0) return;
                ulong ns = 0;
                _getQueryui64v(_ids[_read], QueryResult, &ns);
                LastMs = ns / 1e6;
                _read = (_read + 1) % _ids.Length;
                _pending--;
            }
        }
    }
}
