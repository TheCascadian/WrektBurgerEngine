using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Many letters of one font in one draw call: the map's country names. raylib draws text letter by letter, each with a string to convert, a glyph to search, a matrix to push: with thousands of
/// letters a frame that alone took several milliseconds. Here the corners are worked out in plain arithmetic, the whole list goes to the graphics card at once, and a frame in which nothing moved draws the last list again.
/// </summary>
internal sealed unsafe class GlyphBatch : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float X, Y, U, V; public uint Color; }

    private readonly record struct Glyph(float U0, float V0, float U1, float V1, float OffX, float OffY, float W, float H, float Advance);

    private const string VertexCode = @"#version 330
in vec2 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform vec2 screenSize;
out vec2 uv;
out vec4 tint;
void main()
{
    uv = vertexTexCoord;
    tint = vertexColor;
    gl_Position = vec4(vertexPosition.x / screenSize.x * 2.0 - 1.0, 1.0 - vertexPosition.y / screenSize.y * 2.0, 0.0, 1.0);
}";
    private const string FragmentCode = @"#version 330
in vec2 uv;
in vec4 tint;
uniform sampler2D tex;
out vec4 finalColor;
void main() { finalColor = texture(tex, uv) * tint; }";

    private const int Ring = 3;   // a buffer the card may still be reading is not written to: the next one is
    private readonly uint[] _vao = new uint[Ring], _vbo = new uint[Ring];
    private readonly int[] _capacity = new int[Ring];
    private int _at;
    private Vertex[] _shadow = new Vertex[6 * 4096], _main = new Vertex[6 * 4096];
    private Vertex[] _upload = new Vertex[6 * 8192];
    private int _shadowCount, _mainCount, _uploaded;
    private Shader _shader;
    private int _locScreen, _locTex;
    private bool _ready;
    private Font _font;
    private bool _hasFont;
    private readonly Dictionary<char, Glyph> _glyphs = new();
    private float _baseSize;

    private void Init()
    {
        _ready = true;
        _shader = Raylib.LoadShaderFromMemory(VertexCode, FragmentCode);
        _locScreen = Raylib.GetShaderLocation(_shader, "screenSize");
        _locTex = Raylib.GetShaderLocation(_shader, "tex");
    }

    /// <summary>Starts a new list of letters in this font.</summary>
    public void Begin(Font font)
    {
        if (!_hasFont || font.Texture.Id != _font.Texture.Id) { _glyphs.Clear(); _font = font; _hasFont = true; _baseSize = Math.Max(1, font.BaseSize); }
        _shadowCount = _mainCount = 0;
    }

    private Glyph GlyphOf(char ch)
    {
        if (_glyphs.TryGetValue(ch, out var g)) return g;
        int idx = Raylib.GetGlyphIndex(_font, ch);
        var rec = _font.Recs[idx];
        var info = _font.Glyphs[idx];
        float pad = _font.GlyphPadding, tw = Math.Max(1, _font.Texture.Width), th = Math.Max(1, _font.Texture.Height);
        float x = rec.X - pad, y = rec.Y - pad, w = rec.Width + 2 * pad, h = rec.Height + 2 * pad;
        g = new Glyph(x / tw, y / th, (x + w) / tw, (y + h) / th, info.OffsetX - pad, info.OffsetY - pad, w, h, info.AdvanceX != 0 ? info.AdvanceX : rec.Width + info.OffsetX);
        return _glyphs[ch] = g;
    }

    /// <summary>The width of a letter at this size, as raylib measures it.</summary>
    public float Advance(char ch, float size) => GlyphOf(ch).Advance * size / _baseSize;

    private static void Put(ref Vertex[] list, ref int n, in Glyph g, Vector2 at, Vector2 axis, float scale, float originX, float originY, uint color)
    {
        if (n + 6 > list.Length) Array.Resize(ref list, list.Length * 2);
        // the rectangle of the letter, relative to the point it turns around, then turned
        float x0 = g.OffX * scale - originX, y0 = g.OffY * scale - originY, x1 = x0 + g.W * scale, y1 = y0 + g.H * scale;
        float c = axis.X, s = axis.Y;
        float ax = at.X + x0 * c - y0 * s, ay = at.Y + x0 * s + y0 * c;
        float bx = at.X + x1 * c - y0 * s, by = at.Y + x1 * s + y0 * c;
        float cx = at.X + x1 * c - y1 * s, cy = at.Y + x1 * s + y1 * c;
        float dx = at.X + x0 * c - y1 * s, dy = at.Y + x0 * s + y1 * c;
        ref Vertex v = ref list[n];
        v.X = ax; v.Y = ay; v.U = g.U0; v.V = g.V0; v.Color = color;
        Unsafe_Next(ref list, n + 1, dx, dy, g.U0, g.V1, color);
        Unsafe_Next(ref list, n + 2, cx, cy, g.U1, g.V1, color);
        Unsafe_Next(ref list, n + 3, ax, ay, g.U0, g.V0, color);
        Unsafe_Next(ref list, n + 4, cx, cy, g.U1, g.V1, color);
        Unsafe_Next(ref list, n + 5, bx, by, g.U1, g.V0, color);
        n += 6;
    }

    private static void Unsafe_Next(ref Vertex[] list, int i, float x, float y, float u, float v, uint color)
    {
        ref Vertex p = ref list[i];
        p.X = x; p.Y = y; p.U = u; p.V = v; p.Color = color;
    }

    /// <summary>A letter of the size <paramref name="size"/>, turned around <paramref name="at"/> by the unit vector <paramref name="axis"/>, with its dark shadow copies under all the letters of the list.</summary>
    public void Letter(char ch, Vector2 at, Vector2 axis, float size, float originX, float originY, uint color, uint shadow, float shadowOffset, bool fullShadow)
    {
        var g = GlyphOf(ch);
        float scale = size / _baseSize;
        Put(ref _shadow, ref _shadowCount, g, at + new Vector2(shadowOffset, shadowOffset), axis, scale, originX, originY, shadow);
        if (fullShadow)
        {
            Put(ref _shadow, ref _shadowCount, g, at + new Vector2(-shadowOffset * 0.6f, shadowOffset * 0.6f), axis, scale, originX, originY, shadow);
            Put(ref _shadow, ref _shadowCount, g, at + new Vector2(shadowOffset * 0.6f, -shadowOffset * 0.6f), axis, scale, originX, originY, shadow);
        }
        Put(ref _main, ref _mainCount, g, at, axis, scale, originX, originY, color);
    }

    /// <summary>Sends the list to the graphics card.</summary>
    public void Upload()
    {
        if (!_ready) Init();
        int total = _shadowCount + _mainCount;
        _uploaded = total;
        if (total == 0) return;
        if (_upload.Length < total) _upload = new Vertex[total + total / 2];
        Array.Copy(_shadow, 0, _upload, 0, _shadowCount);
        Array.Copy(_main, 0, _upload, _shadowCount, _mainCount);
        _at = (_at + 1) % Ring;
        int size = total * sizeof(Vertex);
        fixed (Vertex* data = _upload)
        {
            if (_vao[_at] == 0 || _capacity[_at] < size)
            {
                if (_vao[_at] != 0) { Rlgl.UnloadVertexBuffer(_vbo[_at]); Rlgl.UnloadVertexArray(_vao[_at]); }
                int capacity = size + size / 2;
                _vao[_at] = Rlgl.LoadVertexArray();
                Rlgl.EnableVertexArray(_vao[_at]);
                _vbo[_at] = Rlgl.LoadVertexBuffer(null, capacity, new CBool(true));
                Rlgl.EnableVertexBuffer(_vbo[_at]);
                uint stride = (uint)sizeof(Vertex);
                int pos = _shader.Locs[(int)ShaderLocationIndex.VertexPosition], tex = _shader.Locs[(int)ShaderLocationIndex.VertexTexcoord01], col = _shader.Locs[(int)ShaderLocationIndex.VertexColor];
                Rlgl.SetVertexAttribute((uint)pos, 2, 0x1406, new CBool(false), (int)stride, 0);
                Rlgl.EnableVertexAttribute((uint)pos);
                Rlgl.SetVertexAttribute((uint)tex, 2, 0x1406, new CBool(false), (int)stride, 8);
                Rlgl.EnableVertexAttribute((uint)tex);
                Rlgl.SetVertexAttribute((uint)col, 4, 0x1401, new CBool(true), (int)stride, 16);
                Rlgl.EnableVertexAttribute((uint)col);
                Rlgl.DisableVertexArray();
                _capacity[_at] = capacity;
            }
            Rlgl.UpdateVertexBuffer(_vbo[_at], data, size, 0);
        }
    }

    /// <summary>Draws the list last sent. Call in plain 2D (not inside the 3D mode).</summary>
    public void Draw(int width, int height)
    {
        if (_uploaded == 0 || !_hasFont) return;
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableShader(_shader.Id);
        var screen = stackalloc float[2];
        screen[0] = width; screen[1] = height;
        Rlgl.SetUniform(_locScreen, screen, (int)ShaderUniformDataType.Vec2, 1);
        int slot = 0;
        Rlgl.SetUniform(_locTex, &slot, (int)ShaderUniformDataType.Int, 1);
        Rlgl.ActiveTextureSlot(0);
        Rlgl.EnableTexture(_font.Texture.Id);
        Rlgl.EnableVertexArray(_vao[_at]);
        Rlgl.DrawVertexArray(0, _uploaded);
        Rlgl.DisableVertexArray();
        Rlgl.DisableTexture();
        Rlgl.DisableShader();
    }

    public void Dispose()
    {
        if (!_ready) return;
        for (int i = 0; i < Ring; i++)
            if (_vao[i] != 0) { Rlgl.UnloadVertexBuffer(_vbo[i]); Rlgl.UnloadVertexArray(_vao[i]); }
        Raylib.UnloadShader(_shader);
    }
}
