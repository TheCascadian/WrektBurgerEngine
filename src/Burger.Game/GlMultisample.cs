using System.Runtime.InteropServices;

namespace Burger.Runtime;

/// <summary>Turns multisampling on and off at run time (the window keeps its multisampled framebuffer; only the rasterizer changes).</summary>
internal static unsafe class GlMultisample
{
    private const uint GlMultisampleCap = 0x809D;
    private const uint GlRenderer = 0x1F01;
    private static delegate* unmanaged[Cdecl]<uint, void> _enable, _disable;
    private static delegate* unmanaged[Cdecl]<uint, byte*> _getString;
    private static bool _tried, _on = true;

    private static void Load()
    {
        _tried = true;
        string[] names = OperatingSystem.IsWindows() ? new[] { "opengl32.dll" }
            : OperatingSystem.IsMacOS() ? new[] { "/System/Library/Frameworks/OpenGL.framework/OpenGL" }
            : new[] { "libGL.so.1", "libGL.so" };
        foreach (var n in names)
        {
            if (!NativeLibrary.TryLoad(n, out var h)) continue;
            if (NativeLibrary.TryGetExport(h, "glGetString", out var g)) _getString = (delegate* unmanaged[Cdecl]<uint, byte*>)g;
            if (NativeLibrary.TryGetExport(h, "glEnable", out var e) && NativeLibrary.TryGetExport(h, "glDisable", out var d))
            { _enable = (delegate* unmanaged[Cdecl]<uint, void>)e; _disable = (delegate* unmanaged[Cdecl]<uint, void>)d; return; }
        }
    }

    /// <summary>The current context renders on the CPU (Mesa llvmpipe or softpipe, SwiftShader). Call after the window is created.</summary>
    public static bool IsSoftwareRenderer()
    {
        if (!_tried) Load();
        if (_getString == null) return false;
        var p = _getString(GlRenderer);
        string renderer = p == null ? "" : Marshal.PtrToStringAnsi((nint)p) ?? "";
        return renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) || renderer.Contains("softpipe", StringComparison.OrdinalIgnoreCase)
            || renderer.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on)
    {
        if (on == _on) return;
        if (!_tried) Load();
        if (_enable == null) return;
        if (on) _enable(GlMultisampleCap); else _disable(GlMultisampleCap);
        _on = on;
    }
}
