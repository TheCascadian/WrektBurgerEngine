using System.Numerics;
using Burger.Core;
using Burger.Core.Geography;
using Burger.Core.Map;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public enum MapMode { Political, Terrain, States, Diplomacy, Locations, Supply, Air, Ideology, Population, Culture, Religion, Provinces, Continents, Development, Institution, Spheres, Titles }

/// <summary>
/// Builds and draws the 3D map from the province map, heightmap, terrain, tree and river inputs.
/// Province borders, country borders, map mode colors and occupation stripes are computed in a shader
/// from two textures: a per-pixel "map data" texture (location index, terrain, river) and a small
/// per-location lookup texture that the CPU updates when ownership changes.
/// </summary>
public sealed unsafe class MapRenderer : IDisposable
{
    private const int LutWidth = 256;
    private const int MaxTileSize = 1024;   // atlas cell cap: larger textures are scaled down to this, never lower
    private const int DefaultTileSize = 512; // cell size when no terrain has a texture file
    private const int AtlasPad = 32;         // wrapped border around each cell, so filtering and mipmaps never read a neighbor

    private Game _game;
    private MapData _map;
    private Mesh[] _chunks = Array.Empty<Mesh>();
    private BoundingBox[] _chunkBoxes = Array.Empty<BoundingBox>();
    // the same terrain at a quarter of the resolution, drawn when zoomed far out (its triangles are smaller than a pixel otherwise)
    private Mesh[] _lodChunks = Array.Empty<Mesh>();
    private BoundingBox[] _lodBoxes = Array.Empty<BoundingBox>();
    private const int LodStride = 4;
    private const float LodHeight = 90f;
    private Mesh _seaMesh;
    private Mesh _treeMesh;
    private bool _hasTrees;
    private Material _treeMaterial;
    private Material _material;
    private Shader _shader;
    private Texture2D _mapDataTex;
    private Texture2D _lutTex;
    private Texture2D _atlasTex;
    private Texture2D _normalTex;
    private Texture2D _blendTex0, _blendTex1; // terrain blend weights at half the map resolution (ids 0-3, 4-7)
    private bool _hasBlendTex;
    private Texture2D _riverTex; // river coverage at half the map resolution, filtered and mipmapped
    private bool _hasNormalMap;
    private Color[] _lut = Array.Empty<Color>();
    private int _blockRows;
    private int _atlasGrid;
    private int _tilePx, _padPx;

    // CPU copy of the mesh heights for picking and placing things on the ground
    private int _hw, _hh;
    // the heights of the vertices of the terrain mesh as drawn: figures and counters must stand on this surface, not on the finer field
    private float[] _meshHeights = Array.Empty<float>();
    private int _mw, _mh;

    private int _locMapData, _locLut, _locLutSize, _locBlockRows, _locMapSize, _locWorldSize, _locAtlasGrid, _locTileScale, _locAtlasTile,
        _locWater, _locRiver, _locTime, _locBorderScale, _locStripe, _locStateBorders, _locShowBorders, _locGeoBase, _locTint, _locViewPos, _locFog, _locGlobe, _locGlobeRadius, _locSun, _locLodWater, _locLodRiver, _locLodTerrain;
    private int _visibilityVersion = -1;
    // terra incognita: locations being charted right now (index -> progress, negative = still waiting its turn), and the shader switch
    private int _tiVersion = -1, _locTi;
    private readonly Dictionary<int, float> _revealing = new();
    private const float RevealSeconds = 2.4f;

    /// <summary>Draw the map on a globe instead of the flat, east-west wrapping map. Set through <see cref="CameraController.SetGlobe"/>.</summary>
    public bool Globe { get; internal set; }
    /// <summary>Radius of the globe: the map width is its circumference, so one map unit is the same length at the equator in both modes.</summary>
    public float GlobeRadius => WorldWidth / MathF.Tau;
    /// <summary>X of the point the camera looks at on the flat map. Things are drawn in the copy of the wrapping map nearest to it.</summary>
    public float ViewCenterX { get; set; }
    public float PixelsPerUnit => _map.Settings.PixelsPerUnit;
    public float WorldWidth { get; }
    public float WorldHeight { get; }
    public MapMode Mode { get; set; } = MapMode.Political;
    /// <summary>Date shown by the Provinces map mode (null = the game date). Set with the console command geodate.</summary>
    public int? GeoDay;
    /// <summary>Debug view of the Provinces mode: provinces whose shape differs from the start (made by the dynamic system and not reunited) are bright (orange front line, cyan growth split, yellow merge), each in its own shade; everything else is dim gray.</summary>
    public bool GeoDebug;
    public bool GeoBase; // overlay: borders of the start-layout provinces drawn over the current ones
    private int _geoVersion = -1;
    /// <summary>A data/C# defined map mode (overrides <see cref="Mode"/> when set).</summary>
    public Burger.Core.Mechanics.MapModeDef? CustomMode { get; set; }
    /// <summary>The institution the Institution map mode shows.</summary>
    public string InstitutionKey { get; set; } = "";
    Burger.Core.Development.InstitutionDef? ShownInstitution() => _game.Development.Institutions.FirstOrDefault(i => i.Key == InstitutionKey) ?? _game.Development.Institutions.FirstOrDefault();
    public Location? Selected { get; set; }
    public Location? Hovered { get; set; }
    public State? HighlightState { get; set; }
    /// <summary>Country whose relations the diplomacy map mode shows (defaults to the player).</summary>
    public Country? DiplomacyFocus { get; set; }
    public bool Dirty { get; set; } = true;

    // CPU-only products (no GL calls) are made on worker threads while the main thread builds the mesh and compiles the shader
    private System.Threading.Tasks.Task<Image>? _riverTask, _atlasTask, _dataTask;
    private System.Threading.Tasks.Task<(Image A, Image B)?>? _blendTask;
    private static MapRenderer? _prewarmed;

    /// <summary>Starts the CPU-only work (atlas, rivers, terrain blend) on worker threads. Call while the game is still loading; <see cref="Create"/> picks it up.</summary>
    public static void Prewarm(Game game) => _prewarmed = new MapRenderer(game, true);

    /// <summary>The renderer for <paramref name="game"/>, using work started by <see cref="Prewarm"/> when it was for this game.</summary>
    public static MapRenderer Create(Game game)
    {
        var p = System.Threading.Interlocked.Exchange(ref _prewarmed, null);
        if (p == null || !ReferenceEquals(p._game, game)) return new MapRenderer(game);
        p.Finish();
        return p;
    }

    public MapRenderer(Game game) : this(game, true) { Finish(); }

    private MapRenderer(Game game, bool prepare)
    {
        _game = game;
        _map = game.World.Map;
        WorldWidth = _map.WorldWidth;
        WorldHeight = _map.WorldHeight;
        _atlasTask = System.Threading.Tasks.Task.Run(MakeAtlasImage);
        _dataTask = System.Threading.Tasks.Task.Run(MakeMapDataImage);
        _riverTask = System.Threading.Tasks.Task.Run(MakeRiverImage);
        _blendTask = System.Threading.Tasks.Task.Run(MakeBlendImages);
    }

    private void Finish()
    {
        BuildTerrainMesh();
        BuildMapDataTexture();
        BuildLut();
        BuildAtlas();
        BuildShader();
        BuildTrees();
        _seaMesh = Raylib.GenMeshPlane(WorldWidth * 7, WorldHeight * 7, 8, 8);
        _game.MapChanged += () => Dirty = true;
    }

    /// <summary>
    /// True when <paramref name="game"/> (a fresh load of the same files) has the same map as the one this renderer was built for,
    /// so the terrain mesh, textures, atlas, shader and trees can be kept (see <see cref="Rebind"/>).
    /// </summary>
    public bool CanRebind(Game game)
        => game.World.Map.Width == _map.Width && game.World.Map.Height == _map.Height
           && game.World.Locations.Count == _game.World.Locations.Count
           && string.Equals(game.Data.Root, _game.Data.Root, StringComparison.Ordinal);

    /// <summary>Points the renderer at a fresh game of the same map without rebuilding anything on the GPU. Only the per-location colors are rewritten (on the next draw).</summary>
    public void Rebind(Game game)
    {
        _game = game;
        _map = game.World.Map;
        game.MapChanged += () => Dirty = true;
        Mode = MapMode.Political;
        CustomMode = null;
        Selected = Hovered = null;
        HighlightState = null;
        DiplomacyFocus = null;
        GeoDay = null; GeoDebug = false; GeoBase = false;
        _visibilityVersion = -1; _geoVersion = -1;
        Dirty = true;
    }

    // ===============================================================================================
    // Construction
    // ===============================================================================================

    /// <summary>Cells per side of one terrain chunk: 128 x 128 cells is 129 x 129 vertices, within the 16-bit indices raylib meshes use.</summary>
    private const int ChunkCells = 128;

    private void BuildTerrainMesh()
    {
        var s = _map.Settings;
        int srcW = _map.Width, srcH = _map.Height;
        int mw = Math.Clamp(Math.Min(srcW, s.MeshWidth), 2, 8192);
        int mh = Math.Clamp((int)MathF.Round((mw - 1f) * srcH / srcW) + 1, 2, Math.Max(2, srcH));
        _mw = _hw = mw; _mh = _hh = mh;
        int sea = s.SeaLevel;
        var locs = _game.World.Locations;
        var water = new bool[locs.Count];
        for (int i = 0; i < water.Length; i++) water[i] = locs[i].IsWater;
        float stepX = (srcW - 1f) / (mw - 1), stepY = (srcH - 1f) / (mh - 1);
        int kx = Math.Clamp((int)MathF.Ceiling(stepX), 1, 4), ky = Math.Clamp((int)MathF.Ceiling(stepY), 1, 4);

        // Every vertex averages the map pixels under it (water counts as the sea surface), so a dense mesh follows the
        // terrain smoothly instead of picking single noisy pixels.
        var h = new float[mw * mh];
        var land = new bool[mw * mh];
        System.Threading.Tasks.Parallel.For(0, mh, y =>
        {
            for (int x = 0; x < mw; x++)
            {
                float sum = 0;
                for (int j = 0; j < ky; j++)
                    for (int i = 0; i < kx; i++)
                    {
                        int px = Math.Clamp((int)MathF.Round(x * stepX + ((i + 0.5f) / kx - 0.5f) * stepX), 0, srcW - 1);
                        int py = Math.Clamp((int)MathF.Round(y * stepY + ((j + 0.5f) / ky - 0.5f) * stepY), 0, srcH - 1);
                        int idx = py * srcW + px;
                        int li = _map.PixelLocation[idx];
                        sum += li < 0 || water[li] ? sea : Math.Max((int)_map.HeightMap[idx], Math.Min(255, sea + 2));
                    }
                h[y * mw + x] = sum / (kx * ky);
                int cx = Math.Min(srcW - 1, (int)MathF.Round(x * stepX)), cy = Math.Min(srcH - 1, (int)MathF.Round(y * stepY));
                int cl = _map.PixelLocation[cy * srcW + cx];
                land[y * mw + x] = cl >= 0 && !water[cl];
            }
        });
        // a light blur takes the 8-bit steps out of the slopes
        var sm = new float[mw * mh];
        System.Threading.Tasks.Parallel.For(0, mh, y =>
        {
            for (int x = 0; x < mw; x++)
            {
                float sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        sum += h[Math.Clamp(y + dy, 0, mh - 1) * mw + Math.Clamp(x + dx, 0, mw - 1)];
                float v = sum / 9f;
                // smoothing across a coast must not drop land below the separate sea plane
                if (land[y * mw + x]) v = Math.Max(v, Math.Min(255, sea + 2));
                sm[y * mw + x] = v;
            }
        });
        _meshHeights = new float[mw * mh];
        System.Threading.Tasks.Parallel.For(0, mh, y =>
        {
            // the last rows before a pole squeeze every column into a few units: heights that differ from column to column there
            // become radial spikes on the globe. Pull those rows towards their own mean, fully flat at the pole itself.
            double lat = s.SouthLat + (s.NorthLat - s.SouthLat) * (1.0 - y / (mh - 1.0));
            float cosLat = (float)Math.Cos(lat * Math.PI / 180.0);
            float t = Math.Clamp((cosLat - 0.03f) / 0.12f, 0f, 1f);
            float keep = t * t * (3f - 2f * t);
            float mean = 0;
            if (keep < 1f) { for (int x = 0; x < mw; x++) mean += sm[y * mw + x]; mean /= mw; }
            for (int x = 0; x < mw; x++)
            {
                float v = sm[y * mw + x];
                if (keep < 1f) v = mean + (v - mean) * keep;
                _meshHeights[y * mw + x] = v / 255f * s.HeightScale;
            }
        });

        // cut into chunks (so the camera can skip the ones it cannot see); normals come from the whole field, so chunk edges meet seamlessly
        float cellX = WorldWidth / (mw - 1f), cellZ = WorldHeight / (mh - 1f);
        (Mesh[], BoundingBox[]) BuildSet(int st)
        {
        var chunks = new List<Mesh>();
        var boxes = new List<BoundingBox>();
        int cw = (mw - 2) / st + 1, ch = (mh - 2) / st + 1; // cells per side at this stride
        for (int z0 = 0; z0 < ch; z0 += ChunkCells)
            for (int x0 = 0; x0 < cw; x0 += ChunkCells)
            {
                int nx = Math.Min(ChunkCells, cw - x0), nz = Math.Min(ChunkCells, ch - z0);
                int vw = nx + 1, vh = nz + 1;
                var mesh = new Mesh(vw * vh, nx * nz * 2);
                mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocTexCoords(); mesh.AllocIndices();
                var vs = mesh.VerticesAs<Vector3>();
                var ns = mesh.NormalsAs<Vector3>();
                var ts = mesh.TexCoordsAs<Vector2>();
                var ix = mesh.IndicesAs<ushort>();
                var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
                for (int z = 0; z < vh; z++)
                    for (int x = 0; x < vw; x++)
                    {
                        int gx = Math.Min((x0 + x) * st, mw - 1), gz = Math.Min((z0 + z) * st, mh - 1), v = z * vw + x;
                        float hh = _meshHeights[gz * mw + gx];
                        int xa = Math.Max(gx - st, 0), xb = Math.Min(gx + st, mw - 1), za = Math.Max(gz - st, 0), zb = Math.Min(gz + st, mh - 1);
                        float dhx = (_meshHeights[gz * mw + xb] - _meshHeights[gz * mw + xa]) / ((xb - xa) * cellX);
                        float dhz = (_meshHeights[zb * mw + gx] - _meshHeights[za * mw + gx]) / ((zb - za) * cellZ);
                        vs[v] = new Vector3(gx * cellX, hh, gz * cellZ);
                        ns[v] = Vector3.Normalize(new Vector3(-dhx, 1f, -dhz));
                        ts[v] = new Vector2(gx / (mw - 1f), gz / (mh - 1f));
                        lo = Vector3.Min(lo, vs[v]); hi = Vector3.Max(hi, vs[v]);
                    }
                int k = 0;
                for (int z = 0; z < nz; z++)
                    for (int x = 0; x < nx; x++)
                    {
                        int a = z * vw + x, b = a + 1, c = a + vw, d = c + 1;
                        // each cell is split along the top-right to bottom-left diagonal: HeightAt and Pick use the same two triangles
                        ix[k++] = (ushort)a; ix[k++] = (ushort)c; ix[k++] = (ushort)b;
                        ix[k++] = (ushort)b; ix[k++] = (ushort)c; ix[k++] = (ushort)d;
                    }
                Raylib.UploadMesh(ref mesh, new CBool(false));
                chunks.Add(mesh);
                boxes.Add(new BoundingBox(lo, hi));
            }
        return (chunks.ToArray(), boxes.ToArray());
        }
        (_chunks, _chunkBoxes) = BuildSet(1);
        (_lodChunks, _lodBoxes) = BuildSet(LodStride);
        Log.Info($"Terrain mesh: {mw} x {mh} vertices in {_chunks.Length} chunks ({(long)(mw - 1) * (mh - 1) * 2:N0} triangles)");
    }

    /// <summary>True when a chunk (shifted east by <paramref name="shiftX"/>) can be in view. Always true for the globe and for non-perspective cameras.</summary>
    private bool ChunkVisible(in BoundingBox box, float shiftX, Camera3D cam, Vector3 fwd, Vector3 right, Vector3 up, float tanX, float tanY)
    {
        bool r = true, l = true, t = true, b = true, front = true;
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
            p.X += shiftX;
            var d = p - cam.Position;
            float z = Vector3.Dot(d, fwd), x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
            if (z > 0) front = false;
            if (x - tanX * z <= 0) r = false;
            if (-x - tanX * z <= 0) l = false;
            if (y - tanY * z <= 0) t = false;
            if (-y - tanY * z <= 0) b = false;
        }
        // z is positive in front of the eye, so "all corners beyond a plane" means the box is out of view
        return !(front || r || l || t || b);
    }

    private Image MakeMapDataImage()
    {
        int w = _map.Width, h = _map.Height;
        var locs = _game.World.Locations;
        // distance (in pixels, up to CoastRange) from every water pixel to the nearest land: shallow water near coasts
        const int CoastRange = 24;
        bool IsWaterPx(int i) { int li = _map.PixelLocation[i]; return li < 0 || locs[li].IsWater; }
        string cacheDir = MapCache.Dir(_game.Data);
        string distKey = _map.ContentKey.Length == 0 ? "" : MapCache.Key(_map.ContentKey, "coastdist", string.Concat(locs.Select(l => l.IsWater ? '1' : '0')));
        var dist = distKey.Length > 0 ? MapCache.Load<byte>(cacheDir, "coastdist", distKey, out _) : null;
        if (dist == null || dist.Length != w * h)
        {
            dist = new byte[w * h];
            var queue = new Queue<int>();
            for (int i = 0; i < w * h; i++)
                if (!IsWaterPx(i)) { dist[i] = 0; queue.Enqueue(i); }
                else dist[i] = 255;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int d = dist[i];
                if (d >= CoastRange) continue;
                int x = i % w, y = i / w;
                void Visit(int j) { if (dist[j] == 255) { dist[j] = (byte)(d + 1); queue.Enqueue(j); } }
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            if (distKey.Length > 0) MapCache.Save(cacheDir, "coastdist", distKey, Array.Empty<int>(), dist);
        }
        Image img = Raylib.GenImageColor(w, h, Color.Blank);
        byte* px = (byte*)img.Data;
        System.Threading.Tasks.Parallel.For(0, h, row =>
        {
            for (int i = row * w, end = i + w; i < end; i++)
            {
                int li = _map.PixelLocation[i] + 1; // 0 = none
                int o = i * 4;
                px[o] = (byte)(li & 0xFF);
                px[o + 1] = (byte)((li >> 8) & 0xFF);
                px[o + 2] = _map.PixelTerrain[i];
                // land: river flag. water: closeness to the coast (240 next to land, 0 far out); lake water: 245 (the shader keeps it still)
                int lw = _map.PixelLocation[i];
                px[o + 3] = lw >= 0 && locs[lw].IsLake ? (byte)245 : IsWaterPx(i) ? (byte)(dist[i] == 255 ? 0 : Math.Max(0, CoastRange - dist[i]) * 10) : _map.RiverMap[i];
            }
        });
        return img;
    }

    private void BuildMapDataTexture()
    {
        var img = _dataTask!.Result;
        int w = _map.Width, h = _map.Height;
        _mapDataTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_mapDataTex, TextureFilter.Point);
        Raylib.SetTextureWrap(_mapDataTex, TextureWrap.Clamp);

        // rivers are read from a downsampled copy: each texel is the share of river pixels in a 2x2 block, so the shader gets
        // coverage, bank and zoomed-out threads from one or two filtered lookups instead of a loop over single map pixels
        var rimg = _riverTask!.Result;
        _riverTex = Raylib.LoadTextureFromImage(rimg);
        Raylib.UnloadImage(rimg);
        Raylib.GenTextureMipmaps(ref _riverTex);
        Raylib.SetTextureFilter(_riverTex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(_riverTex, TextureWrap.Clamp);
        var blend = _blendTask!.Result;
        if (blend != null) UploadBlendTextures(blend.Value.A, blend.Value.B);
    }

    /// <summary>
    /// The shader blends the terrain types over a wide smooth kernel; summed per terrain it only depends on the map, so it is baked once:
    /// per half-resolution texel the normalized kernel weight of each terrain id (up to 8 ids in two RGBA textures).
    /// </summary>
    private Image MakeRiverImage()
    {
        int w = _map.Width, h = _map.Height;
        var locs = _game.World.Locations;
        int rw = Math.Max(1, w / 2), rh = Math.Max(1, h / 2);
        Image rimg = Raylib.GenImageColor(rw, rh, Color.Black);
        byte* rp = (byte*)rimg.Data;
        System.Threading.Tasks.Parallel.For(0, rh, y =>
        {
            for (int x = 0; x < rw; x++)
            {
                int n = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int sx = Math.Min(w - 1, x * 2 + i), sy = Math.Min(h - 1, y * 2 + j);
                        int si = sy * w + sx;
                        int sl = _map.PixelLocation[si];
                        if (_map.RiverMap[si] == 255 && !(sl < 0 || locs[sl].IsWater)) n++;
                    }
                byte v = (byte)(n * 255 / 4);
                int o = (y * rw + x) * 4;
                rp[o] = rp[o + 1] = rp[o + 2] = v; rp[o + 3] = 255;
            }
        });
        Raylib.ImageFormat(ref rimg, PixelFormat.UncompressedGrayscale);
        return rimg;
    }

    private (Image A, Image B)? MakeBlendImages()
    {
        int w = _map.Width, h = _map.Height;
        if (_game.Data.Terrains.Count > 8) return null;
        int bw = Math.Max(1, w / 2), bh = Math.Max(1, h / 2);
        Image a = Raylib.GenImageColor(bw, bh, Color.Black), b = Raylib.GenImageColor(bw, bh, Color.Black);
        byte* pa = (byte*)a.Data, pb = (byte*)b.Data;
        var ter = _map.PixelTerrain;
        System.Threading.Tasks.Parallel.For(0, bh, y =>
        {
            Span<float> acc = stackalloc float[8];
            for (int x = 0; x < bw; x++)
            {
                acc.Clear();
                // texel center in map pixel units (pixel centers are integers): between pixels 2x and 2x+1
                float cx = 2 * x + 0.5f, cy = 2 * y + 0.5f, tot = 0;
                for (int j = (int)MathF.Ceiling(cy - 2.7f); j <= (int)MathF.Floor(cy + 2.7f); j++)
                    for (int i = (int)MathF.Ceiling(cx - 2.7f); i <= (int)MathF.Floor(cx + 2.7f); i++)
                    {
                        float dx = (i - cx) / 2.7f, dy = (j - cy) / 2.7f;
                        float kw = Math.Max(1f - (dx * dx + dy * dy), 0f);
                        kw *= kw;
                        if (kw <= 0f) continue;
                        int id = ter[Math.Clamp(j, 0, h - 1) * w + Math.Clamp(i, 0, w - 1)];
                        if (id < 8) acc[id] += kw;
                        tot += kw;
                    }
                int o = (y * bw + x) * 4;
                float inv = tot > 0 ? 255f / tot : 0f;
                for (int k = 0; k < 4; k++) { pa[o + k] = (byte)Math.Min(255f, acc[k] * inv + 0.5f); pb[o + k] = (byte)Math.Min(255f, acc[k + 4] * inv + 0.5f); }
            }
        });
        return (a, b);
    }

    private void UploadBlendTextures(Image a, Image b)
    {
        _blendTex0 = Raylib.LoadTextureFromImage(a); _blendTex1 = Raylib.LoadTextureFromImage(b);
        Raylib.UnloadImage(a); Raylib.UnloadImage(b);
        foreach (var t in new[] { _blendTex0, _blendTex1 })
        {
            Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(t, TextureWrap.Clamp);
        }
        _hasBlendTex = true;
    }


    private void BuildLut()
    {
        int n = _game.World.Locations.Count;
        _blockRows = Math.Max(1, (n + LutWidth - 1) / LutWidth);
        _lut = new Color[LutWidth * _blockRows * 4];
        Image img = Raylib.GenImageColor(LutWidth, _blockRows * 4, Color.Blank);
        _lutTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_lutTex, TextureFilter.Point);
    }

    private Image MakeAtlasImage()
    {
        var terrains = _game.Data.Terrains;
        _atlasGrid = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(terrains.Count)));
        string cacheDir = MapCache.Dir(_game.Data);
        string atlasKey = MapCache.Enabled
            ? MapCache.Key(new[] { "atlas2", AtlasPad.ToString(), MaxTileSize.ToString(), DefaultTileSize.ToString() }
                .Concat(terrains.Select(t => { string? tp = t.Texture != null ? _game.Data.PathOf(t.Texture) : null; return tp != null && File.Exists(tp) ? "file:" + tp : "none:" + t.DisplayColor.Packed; })).ToArray())
            : "";
        int[] atlasMeta = Array.Empty<int>();
        var cachedAtlas = atlasKey.Length > 0 ? MapCache.Load<byte>(cacheDir, "atlas", atlasKey, out atlasMeta) : null;
        Image atlas;
        if (cachedAtlas != null && atlasMeta.Length >= 2 && cachedAtlas.Length == atlasMeta[0] * atlasMeta[0] * 4)
        {
            _tilePx = atlasMeta[1]; _padPx = AtlasPad;
            atlas = Raylib.GenImageColor(atlasMeta[0], atlasMeta[0], Color.Gray);
            System.Runtime.InteropServices.Marshal.Copy(cachedAtlas, 0, (IntPtr)atlas.Data, cachedAtlas.Length);
        }
        else atlas = BuildAtlasImage(terrains, atlasKey, cacheDir);
        return atlas;
    }

    private void BuildAtlas()
    {
        var atlas = _atlasTask!.Result;
        _atlasTex = Raylib.LoadTextureFromImage(atlas);
        Raylib.UnloadImage(atlas);
        Raylib.GenTextureMipmaps(ref _atlasTex);
        Raylib.SetTextureFilter(_atlasTex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(_atlasTex, TextureWrap.Clamp);
    }


    private Image BuildAtlasImage(List<TerrainType> terrains, string atlasKey, string cacheDir)
    {
        // load every texture first: the cell is as large as the biggest one, capped at MaxTileSize
        var tiles = new Image?[terrains.Count];
        int tileSize = 0;
        for (int t = 0; t < terrains.Count; t++)
        {
            var terr = terrains[t];
            string? path = terr.Texture != null ? _game.Data.PathOf(terr.Texture) : null;
            if (path != null && File.Exists(path))
            {
                var img = Raylib.LoadImage(path);
                Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
                tiles[t] = img;
                tileSize = Math.Max(tileSize, Math.Max(img.Width, img.Height));
            }
            else if (terr.Texture != null) Log.Warn($"Terrain texture not found: {terr.Texture}", "common/terrain.txt");
        }
        tileSize = tileSize == 0 ? DefaultTileSize : Math.Min(tileSize, MaxTileSize);
        int pad = AtlasPad, cell = tileSize + 2 * pad;
        _tilePx = tileSize; _padPx = pad;
        int size = _atlasGrid * cell;
        Image atlas = Raylib.GenImageColor(size, size, Color.Gray);
        var rnd = new Random(1234);
        for (int t = 0; t < terrains.Count; t++)
        {
            var terr = terrains[t];
            int gx = t % _atlasGrid, gy = t / _atlasGrid;
            Image tile = tiles[t] ?? Assets.NoiseTile(tileSize, terr.DisplayColor, rnd.Next());
            if (tile.Width != tileSize || tile.Height != tileSize) Raylib.ImageResize(ref tile, tileSize, tileSize);
            // the cell is the tile repeated 3x3, clipped: the border holds the texture's own wrapped edge
            Image padded = Raylib.GenImageColor(cell, cell, Color.Gray);
            var src = new Rectangle(0, 0, tileSize, tileSize);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    Raylib.ImageDraw(ref padded, tile, src, new Rectangle(pad + dx * tileSize, pad + dy * tileSize, tileSize, tileSize), Color.White);
            Raylib.ImageDraw(ref atlas, padded, new Rectangle(0, 0, cell, cell), new Rectangle(gx * cell, gy * cell, cell, cell), Color.White);
            Raylib.UnloadImage(padded);
            Raylib.UnloadImage(tile);
        }
        Log.Info($"Terrain atlas: {size}x{size}, {tileSize}px per terrain (+{pad}px wrapped border)");
        if (atlasKey.Length > 0)
        {
            var raw = new byte[size * size * 4];
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)atlas.Data, raw, 0, raw.Length);
            MapCache.Save(cacheDir, "atlas", atlasKey, new[] { size, tileSize }, raw);
        }
        return atlas;
    }

    private void BuildShader()
    {
        _shader = Raylib.LoadShaderFromMemory(Shaders.TerrainVertex, Shaders.TerrainFragment);
        if (_shader.Id == 0) Log.Error("Map shader failed to compile - see console output");
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Albedo, _mapDataTex);
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Metalness, _lutTex);
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Normal, _atlasTex);
        LoadNormalMap();

        _locMapSize = Raylib.GetShaderLocation(_shader, "mapSize");
        _locWorldSize = Raylib.GetShaderLocation(_shader, "worldSize");
        _locLutSize = Raylib.GetShaderLocation(_shader, "lutSize");
        _locBlockRows = Raylib.GetShaderLocation(_shader, "blockRows");
        _locAtlasGrid = Raylib.GetShaderLocation(_shader, "atlasGrid");
        _locTileScale = Raylib.GetShaderLocation(_shader, "tileScale");
        _locAtlasTile = Raylib.GetShaderLocation(_shader, "atlasTile");
        _locWater = Raylib.GetShaderLocation(_shader, "waterColor");
        _locRiver = Raylib.GetShaderLocation(_shader, "riverColor");
        _locSun = Raylib.GetShaderLocation(_shader, "sunDirU");
        _locTime = Raylib.GetShaderLocation(_shader, "time");
        _locBorderScale = Raylib.GetShaderLocation(_shader, "borderScale");
        _locStripe = Raylib.GetShaderLocation(_shader, "stripePeriod");
        _locStateBorders = Raylib.GetShaderLocation(_shader, "stateBorders");
        _locShowBorders = Raylib.GetShaderLocation(_shader, "showProvinceBorders");
        _locGeoBase = Raylib.GetShaderLocation(_shader, "geoBase");
        _locTint = Raylib.GetShaderLocation(_shader, "tintStrength");
        _locViewPos = Raylib.GetShaderLocation(_shader, "viewPos");
        _locFog = Raylib.GetShaderLocation(_shader, "fogStrength");
        _locTi = Raylib.GetShaderLocation(_shader, "terraIncognita");
        _locGlobe = Raylib.GetShaderLocation(_shader, "globe");
        _locLodWater = Raylib.GetShaderLocation(_shader, "lodWater");
        _locLodRiver = Raylib.GetShaderLocation(_shader, "lodRiver");
        _locLodTerrain = Raylib.GetShaderLocation(_shader, "lodTerrain");
        _locGlobeRadius = Raylib.GetShaderLocation(_shader, "globeRadius");
        _locMapData = _locLut = 0;

        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "seaH"), _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "waveAmp"), 0.3f * 2f / 255f * _map.Settings.HeightScale, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "heightScale"), _map.Settings.HeightScale, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "latRange"), new Vector2((float)_map.Settings.NorthLat, (float)_map.Settings.SouthLat), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locMapSize, new Vector2(_map.Width, _map.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locWorldSize, new Vector2(WorldWidth, WorldHeight), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locLutSize, new Vector2(LutWidth, _blockRows * 4), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locBlockRows, (float)_blockRows, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locAtlasGrid, (float)_atlasGrid, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locTileScale, 0.35f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locAtlasTile, new Vector2(_tilePx, _padPx), ShaderUniformDataType.Vec2);
        var wc = _map.Settings.WaterColor;
        var rc = _map.Settings.RiverColor;
        Raylib.SetShaderValue(_shader, _locWater, new Vector3(wc.R / 255f, wc.G / 255f, wc.B / 255f), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locRiver, new Vector3(rc.R / 255f, rc.G / 255f, rc.B / 255f), ShaderUniformDataType.Vec3);
    }

    /// <summary>The optional map/normal.png (name from <c>normalmap</c> in default.map). Without the file the map is shaded from the heightmap alone, as before.</summary>
    private void LoadNormalMap()
    {
        string path = _game.Data.PathOf("map/" + _map.Settings.NormalMap);
        if (File.Exists(path))
        {
            var img = Raylib.LoadImage(path);
            if (img.Width > 0)
            {
                Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
                const int maxW = 8192; // keeps the texture within memory limits on a 16384 wide map
                if (img.Width > maxW) Raylib.ImageResize(ref img, maxW, Math.Max(1, (int)((long)img.Height * maxW / img.Width)));
                _normalTex = Raylib.LoadTextureFromImage(img);
                Raylib.GenTextureMipmaps(ref _normalTex);
                Raylib.SetTextureFilter(_normalTex, TextureFilter.Trilinear);
                Raylib.SetTextureWrap(_normalTex, TextureWrap.Clamp);
                _hasNormalMap = true;
            }
            else Log.Warn($"Normal map could not be read: map/{_map.Settings.NormalMap}", "map/default.map");
            Raylib.UnloadImage(img);
        }
        if (_hasNormalMap)
        {
            // raylib binds material map N to texture unit N: the roughness slot is unit 3, which the shader reads as texture3
            Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Roughness, _normalTex);
            Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "texture3"), 3, ShaderUniformDataType.Int);
        }
        // the river texture rides the occlusion slot: texture unit 4
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Occlusion, _riverTex);
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "texture4"), 4, ShaderUniformDataType.Int);
        if (_hasBlendTex)
        {
            Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Emission, _blendTex0);
            Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Height, _blendTex1);
            Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "texture5"), 5, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "texture6"), 6, ShaderUniformDataType.Int);
        }
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "hasBlendTex"), _hasBlendTex ? 1f : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, Raylib.GetShaderLocation(_shader, "hasNormalMap"), _hasNormalMap ? 1f : 0f, ShaderUniformDataType.Float);
    }

    private void BuildTrees()
    {
        var s = _map.Settings;
        int step = s.TreeSpacing;
        var rnd = new Random(42);
        var positions = new List<(Vector3 Pos, float Size, int Shade)>();
        const int maxTrees = 60000;
        for (int y = step / 2; y < _map.Height; y += step)
        {
            for (int x = step / 2; x < _map.Width; x += step)
            {
                int jx = Math.Clamp(x + rnd.Next(-step / 2, step / 2 + 1), 0, _map.Width - 1);
                int jy = Math.Clamp(y + rnd.Next(-step / 2, step / 2 + 1), 0, _map.Height - 1);
                int i = jy * _map.Width + jx;
                float density = _map.TreeMap[i] / 255f * s.TreeDensity;
                if (density <= 0 || rnd.NextDouble() > density) continue;
                int li = _map.PixelLocation[i];
                if (li < 0 || _game.World.Locations[li].IsWater || _map.RiverMap[i] != 0) continue;
                float wx = (jx + 0.5f) / s.PixelsPerUnit, wz = (jy + 0.5f) / s.PixelsPerUnit;
                positions.Add((new Vector3(wx, HeightAt(wx, wz), wz), 0.7f + (float)rnd.NextDouble() * 0.6f, rnd.Next(40)));
            }
        }
        if (positions.Count == 0) return;
        if (positions.Count > maxTrees)
        {
            // too many for one map: keep an even random sample (not just the northern rows)
            for (int i = positions.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (positions[i], positions[j]) = (positions[j], positions[i]); }
            positions.RemoveRange(maxTrees, positions.Count - maxTrees);
        }
        // low-poly conifers: two stacked cones per tree, lit from the same sun as the terrain
        const int sides = 5, tiers = 2;
        int tris = positions.Count * sides * tiers;
        _treeMesh = new Mesh(tris * 3, tris);
        _treeMesh.AllocVertices();
        _treeMesh.AllocColors();
        var verts = _treeMesh.VerticesAs<Vector3>();
        var cols = _treeMesh.ColorsAs<Color>();
        float baseR = 0.6f / s.PixelsPerUnit * step * 0.3f, height = baseR * 3.6f;
        int v = 0;
        foreach (var (p, size, shade) in positions)
        {
            float r = baseR * size, hgt = height * size;
            // a few trees are lighter or more olive so forests don't look like one flat color
            int kind = shade % 7;
            (int R, int G, int B) baseCol = kind switch { 0 => (52, 86, 34), 1 => (70, 92, 40), _ => (26 + shade / 3, 62 + shade / 2, 30 + shade / 4) };
            for (int tier = 0; tier < tiers; tier++)
            {
                float y0 = hgt * (tier == 0 ? 0.08f : 0.42f);
                float y1 = hgt * (tier == 0 ? 0.68f : 1.0f);
                float rr = r * (tier == 0 ? 1.0f : 0.7f);
                var top = p + new Vector3(0, y1, 0);
                for (int k = 0; k < sides; k++)
                {
                    float a0 = MathF.PI * 2 * k / sides + p.X * 7, a1 = MathF.PI * 2 * (k + 1) / sides + p.X * 7;
                    var b0 = p + new Vector3(MathF.Cos(a0) * rr, y0, MathF.Sin(a0) * rr);
                    var b1 = p + new Vector3(MathF.Cos(a1) * rr, y0, MathF.Sin(a1) * rr);
                    // counter-clockwise when seen from outside
                    verts[v] = b0; verts[v + 1] = top; verts[v + 2] = b1;
                    float light = 0.5f + 0.5f * MathF.Max(0, MathF.Cos((a0 + a1) / 2 - 2.3f));
                    var c = new Color((int)(baseCol.R * light), (int)(baseCol.G * light), (int)(baseCol.B * light), 255);
                    cols[v] = c; cols[v + 1] = new Color((int)Math.Min(255, c.R * 1.35f), (int)Math.Min(255, c.G * 1.35f), (int)Math.Min(255, c.B * 1.35f), 255); cols[v + 2] = c;
                    v += 3;
                }
            }
        }
        Raylib.UploadMesh(ref _treeMesh, new CBool(false));
        _treeMaterial = Raylib.LoadMaterialDefault();
        _hasTrees = true;
    }

    // ===============================================================================================
    // Queries
    // ===============================================================================================

    /// <summary>Ground height at a world position (bilinear on the mesh grid).</summary>
    public float HeightAt(float wx, float wz)
    {
        if (_mw < 2 || _mh < 2) return 0;
        wx -= WorldWidth * MathF.Floor(wx / WorldWidth); // the map wraps east-west
        float gx = wx / WorldWidth * (_mw - 1), gz = wz / WorldHeight * (_mh - 1);
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, _mw - 2), z0 = Math.Clamp((int)MathF.Floor(gz), 0, _mh - 2);
        int x1 = x0 + 1, z1 = z0 + 1;
        float fx = Math.Clamp(gx - x0, 0, 1), fz = Math.Clamp(gz - z0, 0, 1);
        float a = _meshHeights[z0 * _mw + x0], b = _meshHeights[z0 * _mw + x1], c = _meshHeights[z1 * _mw + x0], d = _meshHeights[z1 * _mw + x1];
        // GenMeshHeightmap splits each cell along the top-right/bottom-left diagonal.
        // Use the same two planar triangles here: the picker, counters and figures then meet exactly the surface the player sees.
        return fx + fz <= 1
            ? a * (1 - fx - fz) + b * fx + c * fz
            : c * (1 - fx) + b * (1 - fz) + d * (fx + fz - 1);
    }

    /// <summary>
    /// Where a body standing at a point rests: the highest ground within <paramref name="radius"/> of it. On a steep face the height
    /// changes faster than a figure is wide, so the exact point alone would bury half of it in the slope.
    /// </summary>
    public float StandHeight(float wx, float wz, float radius)
    {
        float h = HeightAt(wx, wz);
        h = Math.Max(h, HeightAt(wx - radius, wz));
        h = Math.Max(h, HeightAt(wx + radius, wz));
        h = Math.Max(h, HeightAt(wx, wz - radius));
        h = Math.Max(h, HeightAt(wx, wz + radius));
        float d = radius * 0.7f;
        h = Math.Max(h, Math.Max(HeightAt(wx - d, wz - d), HeightAt(wx + d, wz + d)));
        return Math.Max(h, Math.Max(HeightAt(wx - d, wz + d), HeightAt(wx + d, wz - d)));
    }

    public Vector3 WorldPos(float pixelX, float pixelY, float lift = 0)
    {
        float wx = pixelX / _map.Settings.PixelsPerUnit, wz = pixelY / _map.Settings.PixelsPerUnit;
        return PlaceAt(wx, wz, HeightAt(wx, wz) + lift);
    }

    /// <summary>
    /// Where a point of the flat map (in map units, at a height above sea level) is in the 3D scene: on the globe in globe mode,
    /// otherwise in the copy of the east-west wrapping map nearest to the view. Everything drawn on the map goes through here.
    /// </summary>
    public Vector3 PlaceAt(float wx, float wz, float height)
    {
        if (Globe) return SpherePos(wx, wz, height);
        wx += WorldWidth * MathF.Round((ViewCenterX - wx) / WorldWidth);
        return new Vector3(wx, height, wz);
    }

    /// <summary>A map point on the globe: x runs around the equator (west to east), z from the north pole down to the south pole.</summary>
    public Vector3 SpherePos(float wx, float wz, float height)
    {
        float lon = (wx / WorldWidth - 0.5f) * MathF.Tau, lat = (0.5f - wz / WorldHeight) * MathF.PI;
        float cl = MathF.Cos(lat);
        return new Vector3(cl * MathF.Sin(lon), MathF.Sin(lat), cl * MathF.Cos(lon)) * (GlobeRadius + height);
    }

    /// <summary>The unit vectors up (away from the globe's center) and south, along the surface, at a map point.</summary>
    public (Vector3 Up, Vector3 South) GlobeFrame(float wx, float wz)
    {
        float lon = (wx / WorldWidth - 0.5f) * MathF.Tau, lat = (0.5f - wz / WorldHeight) * MathF.PI;
        float cl = MathF.Cos(lat), sl = MathF.Sin(lat), co = MathF.Cos(lon), so = MathF.Sin(lon);
        return (new Vector3(cl * so, sl, cl * co), new Vector3(sl * so, -cl, sl * co));
    }

    /// <summary>The map point (map units) below a point of the globe.</summary>
    public (float X, float Z) FlatOf(Vector3 p)
    {
        var d = Vector3.Normalize(p);
        float lat = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)), lon = MathF.Atan2(d.X, d.Z);
        return ((lon / MathF.Tau + 0.5f) * WorldWidth, (0.5f - lat / MathF.PI) * WorldHeight);
    }

    private static Vector3? RaySphere(Ray ray, float radius)
    {
        float b = Vector3.Dot(ray.Position, ray.Direction), c = ray.Position.LengthSquared() - radius * radius;
        float disc = b * b - c;
        if (disc < 0) return null;
        float t = -b - MathF.Sqrt(disc);
        return t < 0 ? null : ray.Position + ray.Direction * t;
    }

    /// <summary>The map point under a screen point on the globe, following the relief a little so a peak near the edge picks right.</summary>
    private bool GlobeHit(Ray ray, out float wx, out float wz)
    {
        wx = wz = 0;
        float seaH = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale;
        var hit = RaySphere(ray, GlobeRadius + seaH);
        if (hit == null) return false;
        (wx, wz) = FlatOf(hit.Value);
        for (int i = 0; i < 2; i++)
        {
            var again = RaySphere(ray, GlobeRadius + HeightAt(wx, wz));
            if (again == null) break;
            (wx, wz) = FlatOf(again.Value);
        }
        return true;
    }

    /// <summary>The unit normal of the terrain around a point, from heights <paramref name="reach"/> away on each side.</summary>
    public Vector3 GroundNormal(float wx, float wz, float reach)
    {
        float dx = HeightAt(wx - reach, wz) - HeightAt(wx + reach, wz);
        float dz = HeightAt(wx, wz - reach) - HeightAt(wx, wz + reach);
        return Vector3.Normalize(new Vector3(dx, 2 * reach, dz));
    }

    public Vector3 LocationPos(Location l, float lift = 0) => WorldPos(l.CenterX, l.CenterY, lift);

    /// <summary>Finds the first terrain location intersected by the mouse ray.</summary>
    /// <param name="screen">Mouse position relative to the map viewport.</param>
    /// <param name="cam">The camera used to render the map.</param>
    /// <param name="stepScale">Retained for source compatibility; exact triangle traversal has no step size.</param>
    public Location? Pick(Vector2 screen, Camera3D cam, float stepScale = 1f)
    {
        _ = stepScale;
        var ray0 = Viewport.Ray(screen, cam);
        if (Globe)
        {
            if (!GlobeHit(ray0, out float gx, out float gz)) return null;
            return _game.World.LocationAtPixel(Math.Clamp((int)(gx * _map.Settings.PixelsPerUnit), 0, _map.Width - 1), Math.Clamp((int)(gz * _map.Settings.PixelsPerUnit), 0, _map.Height - 1));
        }
        // the flat map repeats east-west: try the map and the copies on both sides, the nearest hit is what the cursor is on
        Location? best = null;
        float bestT = float.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            var r = ray0;
            r.Position.X -= (k == 0 ? 0 : k == 1 ? -1 : 1) * WorldWidth;
            var l = PickFlat(r, out float t);
            if (l != null && t < bestT) { best = l; bestT = t; }
        }
        return best;
    }

    private Location? PickFlat(Ray ray, out float hitT)
    {
        hitT = 0;
        // Traverse the heightfield cells in ray order and intersect the actual two triangles
        // rendered for each cell. Fixed-step ray marching can step over narrow high-relief peaks,
        // causing the cursor to select terrain far behind the visible surface.
        if (ray.Direction.Y >= 0 || _hw < 2 || _hh < 2) return null;

        float enter = 0f, exit = float.PositiveInfinity;
        if (!ClipRayAxis(ray.Position.X, ray.Direction.X, 0, WorldWidth, ref enter, ref exit) ||
            !ClipRayAxis(ray.Position.Z, ray.Direction.Z, 0, WorldHeight, ref enter, ref exit) ||
            exit < enter)
            return null;

        float cellX = WorldWidth / (_mw - 1f);
        float cellZ = WorldHeight / (_mh - 1f);
        // Probe a tiny distance inside the clipped area so a ray entering exactly on a grid
        // edge starts in the cell it is moving into, not the adjacent cell behind it.
        var probe = ray.Position + ray.Direction * (enter + 1e-4f);
        int ix = Math.Clamp((int)MathF.Floor(probe.X / cellX), 0, _mw - 2);
        int iz = Math.Clamp((int)MathF.Floor(probe.Z / cellZ), 0, _mh - 2);
        int stepX = ray.Direction.X > 1e-7f ? 1 : ray.Direction.X < -1e-7f ? -1 : 0;
        int stepZ = ray.Direction.Z > 1e-7f ? 1 : ray.Direction.Z < -1e-7f ? -1 : 0;
        float tDeltaX = stepX == 0 ? float.PositiveInfinity : cellX / MathF.Abs(ray.Direction.X);
        float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : cellZ / MathF.Abs(ray.Direction.Z);
        float tMaxX = float.PositiveInfinity, tMaxZ = float.PositiveInfinity;
        if (stepX != 0)
        {
            float boundaryX = (stepX > 0 ? ix + 1 : ix) * cellX;
            tMaxX = (boundaryX - ray.Position.X) / ray.Direction.X;
            while (tMaxX < enter) tMaxX += tDeltaX;
        }
        if (stepZ != 0)
        {
            float boundaryZ = (stepZ > 0 ? iz + 1 : iz) * cellZ;
            tMaxZ = (boundaryZ - ray.Position.Z) / ray.Direction.Z;
            while (tMaxZ < enter) tMaxZ += tDeltaZ;
        }

        Vector3 Vertex(int x, int z) => new(x * cellX, _meshHeights[z * _mw + x], z * cellZ);
        const float hitEpsilon = 1e-4f;
        // A ray crosses at most _mw + _mh cells in the map rectangle. The guard also
        // protects against floating-point stalls at exact grid corners.
        int maxCells = _mw + _mh + 8;
        for (int visited = 0; visited < maxCells && (uint)ix < (uint)(_mw - 1) && (uint)iz < (uint)(_mh - 1); visited++)
        {
            float nextT = Math.Min(exit, Math.Min(tMaxX, tMaxZ));
            var a = Vertex(ix, iz);
            var b = Vertex(ix + 1, iz);
            var c = Vertex(ix, iz + 1);
            var d = Vertex(ix + 1, iz + 1);
            // GenMeshHeightmap uses the diagonal from b to c.
            bool hit = RayTriangle(ray.Position, ray.Direction, a, c, b, out float ta);
            if (RayTriangle(ray.Position, ray.Direction, b, c, d, out float tb) && (!hit || tb < ta))
            {
                hit = true;
                ta = tb;
            }
            if (hit && ta >= enter - hitEpsilon && ta <= nextT + hitEpsilon)
            {
                var point = ray.Position + ray.Direction * ta;
                int px = Math.Clamp((int)(point.X * _map.Settings.PixelsPerUnit), 0, _map.Width - 1);
                int py = Math.Clamp((int)(point.Z * _map.Settings.PixelsPerUnit), 0, _map.Height - 1);
                hitT = ta;
                return _game.World.LocationAtPixel(px, py);
            }

            if (nextT >= exit - hitEpsilon) break;
            bool crossX = tMaxX <= tMaxZ + hitEpsilon;
            bool crossZ = tMaxZ <= tMaxX + hitEpsilon;
            if (crossX) { ix += stepX; tMaxX += tDeltaX; }
            if (crossZ) { iz += stepZ; tMaxZ += tDeltaZ; }
            if (stepX == 0 && stepZ == 0) break;
        }
        return null;
    }

    private static bool ClipRayAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) < 1e-7f) return origin >= min && origin <= max;
        float a = (min - origin) / direction;
        float b = (max - origin) / direction;
        if (a > b) (a, b) = (b, a);
        enter = Math.Max(enter, a);
        exit = Math.Min(exit, b);
        return exit >= enter;
    }

    private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        // Two-sided Moller-Trumbore ray/triangle intersection.
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        float det = Vector3.Dot(edge1, p);
        if (MathF.Abs(det) < 1e-7f) { distance = 0; return false; }
        float invDet = 1f / det;
        var tvec = origin - a;
        float u = Vector3.Dot(tvec, p) * invDet;
        const float eps = 1e-5f;
        if (u < -eps || u > 1f + eps) { distance = 0; return false; }
        var q = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(direction, q) * invDet;
        if (v < -eps || u + v > 1f + eps) { distance = 0; return false; }
        distance = Vector3.Dot(edge2, q) * invDet;
        return distance >= 0f;
    }

    /// <summary>
    /// Compatibility alias for callers that previously requested an approximate sea-level pick.
    /// Map-cell traversal is now bounded and inexpensive, so hover remains accurate while panning
    /// over mountains instead of selecting a location behind the visible terrain.
    /// </summary>
    public Location? PickFast(Vector2 screen, Camera3D cam) => Pick(screen, cam);

    /// <summary>The direction from the globe's center to the sea-level surface under a screen point (unit vector), or null off the globe.</summary>
    public Vector3? GlobeDir(Vector2 screen, Camera3D cam)
    {
        float seaH = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale;
        var hit = RaySphere(Viewport.Ray(screen, cam), GlobeRadius + seaH);
        return hit == null ? null : Vector3.Normalize(hit.Value);
    }

    /// <summary>World position under the mouse on the ground plane (for camera dragging).</summary>
    public Vector3? GroundPoint(Vector2 screen, Camera3D cam)
    {
        var ray = Viewport.Ray(screen, cam);
        if (Globe)
        {
            if (!GlobeHit(ray, out float gx, out float gz)) return null;
            gx += WorldWidth * MathF.Round((ViewCenterX - gx) / WorldWidth); // nearest copy to the view, so differences never jump across the seam
            return new Vector3(gx, 0, gz);
        }
        if (Math.Abs(ray.Direction.Y) < 1e-4) return null;
        float planeY = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale;
        float t = (planeY - ray.Position.Y) / ray.Direction.Y;
        if (t < 0) return null;
        return ray.Position + ray.Direction * t;
    }

    // ===============================================================================================
    // Map modes
    // ===============================================================================================

    private static Color C(Rgb c, int a = 255) => new(c.R, c.G, c.B, (byte)a);

    public static Color HashColor(int i)
    {
        var c = Rgb.FromHsv((i * 0.61803398875) % 1.0, 0.45 + (i % 3) * 0.15, 0.75 + (i % 2) * 0.15);
        return new Color(c.R, c.G, c.B, (byte)200);
    }

    /// <summary>Rebuilds the per-location lookup texture (colors, owners, states, flags).</summary>
    public void UpdateLut()
    {
        var w = _game.World;
        var player = DiplomacyFocus ?? w.Player;
        int block = LutWidth * _blockRows;
        _visibilityVersion = _game.VisibilityVersion;
        var explore = _game.Exploration;
        bool ti = explore.Enabled && w.Player != null;
        Array.Clear(_lut);
        // historical provinces (opt-in layer): the province covering each location on the shown date, numbered for border drawing
        Dictionary<string, int>? geoIndex = null;
        int geoDay = GeoDay ?? w.Date.TotalDays;
        if (Mode == MapMode.Provinces) _game.GeoDynamics.EnsureSynced();
        if (Mode == MapMode.Provinces && CustomMode == null && _game.Geo.Enabled)
        {
            geoIndex = new(StringComparer.Ordinal);
            foreach (var gp in _game.Geo.Snapshot(geoDay).Provinces) geoIndex[gp.Id] = geoIndex.Count + 1;
        }
        foreach (var l in w.Locations)
        {
            int i = l.Index;
            Color col = new(0, 0, 0, 0);
            var owner = l.Owner;
            if (CustomMode != null)
            {
                var cc = _game.Interface.MapModeColor(_game, CustomMode, l);
                if (cc != null) col = C(cc.Value, 200);
            }
            else switch (Mode)
            {
                case MapMode.Political:
                    if (owner != null && l.IsLand) col = C(owner.Color, 230);
                    break;
                case MapMode.States:
                    if (l.State != null) col = HashColor(l.State.Index + 7);
                    break;
                case MapMode.Locations:
                    col = C(l.MapColor, 230);
                    break;
                case MapMode.Diplomacy:
                    if (owner != null && l.IsLand && player != null) col = DiplomacyColor(player, owner);
                    break;
                case MapMode.Ideology:
                    if (owner != null && l.IsLand && _game.Politics.RulingIdeology(owner) is { } ide) col = C(ide.Color, 225);
                    break;
                case MapMode.Spheres:
                    if (owner != null && l.IsLand && _game.GreatPowers.Enabled)
                    {
                        // great powers in their own colour, the countries in a sphere in the colour of their power (paler), the rest grey
                        var power = _game.GreatPowers.IsGreat(owner) ? owner : _game.GreatPowers.SphereOf(owner);
                        col = power == null ? new Color(96, 98, 104, 200) : _game.GreatPowers.IsGreat(owner) ? C(power.Color, 240) : C(power.Color, 150);
                    }
                    break;
                case MapMode.Titles:
                    if (owner != null && l.IsLand && l.State != null && _game.Titles.Enabled)
                    {
                        // the land the ruler holds directly in the colour of the country (pale), the land of each vassal in a colour of their own
                        var vassal = _game.Titles.VassalOf(l.State);
                        col = vassal != null ? HashColor(vassal.Id * 13 + 5) : C(owner.Color, 120);
                    }
                    break;
                case MapMode.Population:
                    if (l.IsLand && l.State != null && _game.Population.HasPops(l.State))
                    {
                        // people per location of the state, on a log scale from sparse (dark green) to crowded (red)
                        double per = _game.Population.TotalOf(l.State) / Math.Max(1, l.State.Locations.Count(x => x.IsLand));
                        float t = (float)Math.Clamp((Math.Log10(Math.Max(1, per)) - 3) / 3.5, 0, 1);
                        col = t < 0.5f ? new Color((int)(40 + 400 * t), (int)(90 + 200 * t), 50, 220) : new Color(240, (int)(190 - 300 * (t - 0.5f)), 50, 220);
                    }
                    break;
                case MapMode.Culture:
                    if (l.IsLand && l.State != null && _game.Population.Culture(_game.Population.MajorityCulture(l.State)) is { } cul) col = C(cul.Color, 225);
                    break;
                case MapMode.Development:
                    if (l.IsLand && l.State != null && _game.Development.Enabled)
                    {
                        // development level, from slate (1) to gold (20 and up)
                        float t = Math.Clamp((_game.Development.Level(l.State) - 1) / 19f, 0, 1);
                        col = new Color((int)(60 + 190 * t), (int)(72 + 130 * t), (int)(100 - 40 * t), 225);
                    }
                    break;
                case MapMode.Institution:
                    if (l.IsLand && l.State != null && _game.Development.Enabled && ShownInstitution() is { } inst)
                    {
                        // presence of the institution: dark where it has not come, its own color where it has been adopted
                        float t = (float)_game.Development.Presence(inst, l.State);
                        col = new Color((int)(48 + (inst.Color.R - 48) * t), (int)(54 + (inst.Color.G - 54) * t), (int)(66 + (inst.Color.B - 66) * t), 225);
                    }
                    break;
                case MapMode.Continents:
                    if (l.Continent != null) col = C(l.Continent.Color, l.Continent.IsOcean ? 200 : 235);
                    break;
                case MapMode.Religion:
                    if (l.IsLand && l.State != null && _game.Population.Religion(_game.Population.MajorityReligion(l.State)) is { } rel) col = C(rel.Color, 225);
                    break;
                case MapMode.Provinces:
                    if (geoIndex != null && GeoDebug && l.IsLand && _game.Geo.ProvinceOfAtom(l.Key, geoDay) is { } gd)
                    {
                        float v = 0.62f + 0.38f * ((geoIndex[gd.Id] * 0.6180339f) % 1f);
                        string r = gd.Reason ?? "";
                        if (!r.StartsWith("dynamic:", StringComparison.Ordinal) || !_game.GeoDynamics.IsChanged(gd)) col = new Color(105, 105, 115, 215);
                        else if (r.Contains("reunited") || r.Contains("consolidation")) col = new Color((int)(255 * v), (int)(225 * v), 40, 245);
                        else if (r.Contains("growth")) col = new Color(30, (int)(220 * v), (int)(255 * v), 245);
                        else col = new Color((int)(255 * v), (int)(110 * v), 20, 245);
                    }
                    else if (geoIndex != null && l.IsLand && _game.Geo.ProvinceOfAtom(l.Key, geoDay) is { } gp2)
                    {
                        // the province's owner on that date, shaded per province so neighbors of one owner stay distinct
                        var gOwner = _game.Geo.Owner(gp2.Id, geoDay) is { } tag ? w.GetCountry(tag) : null;
                        var baseCol = gOwner != null ? C(gOwner.Color, 230) : new Color(120, 120, 120, 200);
                        float k = 0.78f + 0.22f * ((geoIndex[gp2.Id] * 0.6180339f) % 1f);
                        var ctl = _game.Geo.Controller(gp2.Id, geoDay);
                        col = new Color((byte)(baseCol.R * k), (byte)(baseCol.G * k), (byte)(baseCol.B * k), baseCol.A);
                        if (ctl != null && gOwner != null && !string.Equals(ctl, gOwner.Tag, StringComparison.OrdinalIgnoreCase)) col = new Color((byte)Math.Min(255, col.R + 60), (byte)Math.Min(255, col.G + 60), (byte)Math.Min(255, col.B + 60), col.A);
                    }
                    break;
                case MapMode.Terrain:
                    break;
                case MapMode.Supply:
                    if (player != null && l.IsLand && _game.Features.HasSupply)
                    {
                        double v = _game.Supply.SupplyAt(player, l);
                        if (v > 0.05)
                        {
                            float t = (float)Math.Clamp(v / 6.0, 0, 1);
                            col = t < 0.5f ? new Color(210, (int)(60 + 300 * t), 50, 215) : new Color((int)(210 - 300 * (t - 0.5f)), 210, 60, 215);
                        }
                        else if (l.Owner != null) col = new Color(60, 60, 60, 180);
                    }
                    break;
                case MapMode.Air:
                    if (l.IsLand && _game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && _game.Air.RegionOf(l) is { } region)
                    {
                        var (share, contested) = player != null ? _game.Air.Superiority(region, player) : (0.5, false);
                        if (contested)
                        {
                            float t = (float)share;
                            col = t < 0.5f ? new Color(215, (int)(60 + 300 * t), 50, 215) : new Color((int)(215 - 300 * (t - 0.5f)), 205, 70, 215);
                        }
                        else
                        {
                            var hc = HashColor(region.Index * 3 + 5);
                            col = new Color((byte)(hc.R * 0.55 + 40), (byte)(hc.G * 0.55 + 40), (byte)(hc.B * 0.55 + 50), (byte)190);
                        }
                    }
                    break;
            }
            // terra incognita: nothing of an unknown land leaks into the map (no map color, owner, state or borders)
            bool charted = !ti || explore.IsCharted(l);
            if (!charted) col = new Color(0, 0, 0, 0);
            _lut[i] = col;

            int ownerId = charted && owner != null ? owner.Index + 1 : 0;
            int stateId = charted && l.State != null ? l.State.Index + 1 : 0;
            // the air map mode draws region borders instead of state borders
            if (Mode == MapMode.Air && CustomMode == null && _game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && _game.Air.RegionOf(l) is { } ar) stateId = ar.Index + 1;
            if (geoIndex != null && l.IsLand) stateId = _game.Geo.ProvinceOfAtom(l.Key, geoDay) is { } gp3 && geoIndex.TryGetValue(gp3.Id, out int gi) ? gi : 0; // province borders instead of state borders
            if (!charted) stateId = 0;
            _lut[block + i] = new Color(ownerId & 0xFF, (ownerId >> 8) & 0xFF, stateId & 0xFF, (stateId >> 8) & 0xFF);

            int flags = 0;
            if (l.IsWater) flags |= 1;
            bool occupied = l.IsOccupied && Mode is MapMode.Political or MapMode.Diplomacy;
            if (occupied) flags |= 2;
            if (l == Selected) flags |= 4;
            if (l == Hovered) flags |= 8;
            if (l.IsLake) flags |= 16;
            if (!_game.IsVisible(l)) flags |= 32;
            if (HighlightState != null && l.State == HighlightState) flags |= 64;
            if (l.State?.Demilitarized == true && !l.IsWater) flags |= 128; // demilitarized zone: hatched
            if (!charted) flags &= 1 | 4 | 8 | 16 | 32;
            var stripe = occupied && l.Controller != null ? l.Controller.Color : Rgb.Black;
            _lut[block * 2 + i] = new Color(stripe.R, stripe.G, stripe.B, (byte)flags);
            // lut block 3: start-layout province id (red, green) and how unknown the location is (blue: 255 never seen .. 0 known)
            int bid = GeoBase && geoIndex != null && l.IsLand ? _game.GeoDynamics.BaselineIdOf(l.Key) : 0;
            byte unknown = TiByte(i, charted);
            if (bid != 0 || unknown != 0 || (GeoBase && geoIndex != null && l.IsLand))
                _lut[block * 3 + i] = new Color(bid & 0xFF, (bid >> 8) & 0xFF, (int)unknown, 255);
        }
        Raylib.UpdateTexture(_lutTex, _lut);
        Dirty = false;
    }

    private Color DiplomacyColor(Country me, Country other)
    {
        var w = _game.World;
        var R = w.Relations;
        if (other == me) return new Color(70, 200, 70, 225);
        if (w.AtWar(me, other)) return new Color(200, 40, 40, 210);
        if (R.AreAllied(me, other)) return new Color(50, 110, 220, 200);
        if (R.AreOverlordAndSubject(other, me)) return new Color(110, 190, 230, 200);
        if (R.HasTruce(me, other, w.Date)) return new Color(150, 150, 150, 200);
        double op = _game.Diplomacy.Opinion(other, me);
        // neutrals stay muted so the viewed country (green), allies (blue) and enemies (red) stand out;
        // a faint tint still shows opinion (brownish = dislikes, grey-green = likes)
        float t = (float)Math.Clamp((op + 100) / 200, 0, 1);
        return new Color((int)(150 - 50 * t), (int)(112 + 30 * t), (int)(100 + 12 * t), 120);
    }

    // ===============================================================================================
    // Drawing
    // ===============================================================================================

    /// <summary>Fog of war shading of the map (greyed, darker land you cannot see). Off: the map looks clear, but units you cannot see stay hidden.</summary>
    public static bool FogShading = true; // kept for the whole session (new games, loads)

    /// <summary>Camera height above which the map is a strategic overview: no fog of war, no unit counters.</summary>
    public float StrategicZoom => Math.Max(140f, Math.Max(WorldWidth, WorldHeight) * 0.33f);

    /// <summary>
    /// Direction to the sun. It depends only on the game's date and hour, never on the camera or the view mode: on the globe it is the
    /// true sun (noon on the prime meridian at 12:00, tilted by the season); on the flat map it swings east to west across the day.
    /// </summary>
    private Vector3 SunDirection()
    {
        if (!UserSettings.SunCycle) return Globe ? Vector3.Normalize(new Vector3(0.45f, 0.35f, 0.82f)) : Vector3.Normalize(new Vector3(-0.35f, 1f, -0.45f));
        var d = _game.World.Date;
        float hour = d.Hour + _game.Clock.HourFraction; // the clock's progress inside the hour keeps the sun moving smoothly at slow speeds
        if (Globe)
        {
            float decl = 23.44f * MathF.PI / 180f * MathF.Sin(MathF.Tau * (d.DayOfYear - 80) / 365f);
            float lon = (12f - hour) / 24f * MathF.Tau;
            return new Vector3(MathF.Cos(decl) * MathF.Sin(lon), MathF.Sin(decl), MathF.Cos(decl) * MathF.Cos(lon));
        }
        // a full turn around the map each day: due south of the zenith at noon, climbing and sinking over a mid-latitude arc
        float a = (hour - 12f) / 24f * MathF.Tau;
        float elev = 0.55f + 0.40f * MathF.Cos(a);
        return Vector3.Normalize(new Vector3(MathF.Sin(a) * MathF.Cos(0.4f), elev, MathF.Cos(a) * -0.6f));
    }

    private static Vector3[]? _stars;

    /// <summary>The backdrop behind the globe: stars fixed in space, drawn first so the globe covers them. Flat map mode draws none.</summary>
    private static void DrawSpace(Camera3D cam)
    {
        if (_stars == null)
        {
            var rng = new Random(500);
            _stars = new Vector3[2200];
            for (int i = 0; i < _stars.Length; i++)
            {
                float z = rng.NextSingle() * 2f - 1f, t = rng.NextSingle() * MathF.Tau, r = MathF.Sqrt(1f - z * z);
                _stars[i] = new Vector3(r * MathF.Cos(t), z, r * MathF.Sin(t));
            }
        }
        for (int i = 0; i < _stars.Length; i++)
        {
            int v = 90 + (i * 37 % 140);
            var c = i % 9 == 0 ? new Color(v, v - 10, 255, 255) : i % 7 == 0 ? new Color(255, 245, v, 255) : new Color(v, v, v + 10, 255);
            Raylib.DrawPoint3D(cam.Position + _stars[i] * 4500f, c);
        }
    }

    public void Draw(Camera3D cam, float time)
    {
        if (_game.VisibilityVersion != _visibilityVersion) Dirty = true;
        var explore = _game.Exploration;
        if (explore.Version != _tiVersion)
        {
            _tiVersion = explore.Version;
            var fresh = explore.DrainRevealed();
            _revealing.Clear();
            // newly charted lands burn off the chart one after another (a staggered start up to 0.8 s); a huge batch (a loaded game) just appears
            if (explore.Enabled && fresh.Count <= 700)
                foreach (int idx in fresh)
                    _revealing[idx] = -(unchecked((uint)idx * 2654435761u) % 1000u / 1000f) * 0.8f / RevealSeconds;
            Dirty = true;
        }
        if (Mode == MapMode.Provinces && _game.Geo.Enabled && _game.Geo.Version != _geoVersion) { _geoVersion = _game.Geo.Version; Dirty = true; }
        if (Dirty) UpdateLut();
        AdvanceReveal();
        Raylib.SetShaderValue(_shader, _locTi, explore.Enabled && _game.World.Player != null ? 1f : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locTime, time, ShaderUniformDataType.Float);
        if (Globe) DrawSpace(cam);
        Raylib.SetShaderValue(_shader, _locSun, SunDirection(), ShaderUniformDataType.Vec3);
        float camH = Viewport.HeightOf(cam);
        Raylib.SetShaderValue(_shader, _locGlobe, Globe ? 1f : 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locGlobeRadius, GlobeRadius, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locLodWater, UserSettings.LodScale(UserSettings.LodWater), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locLodRiver, UserSettings.LodScale(UserSettings.LodRiver), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locLodTerrain, UserSettings.LodScale(UserSettings.LodTerrain), ShaderUniformDataType.Float);
        // terrain shows through the map colors up close; far out the map reads like a political map
        float tint = Math.Clamp(0.55f + (camH - 25f) / 160f, 0.55f, 0.95f);
        Raylib.SetShaderValue(_shader, _locTint, tint, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locViewPos, cam.Position, ShaderUniformDataType.Vec3);
        // far out (strategic view) the fog of war fades away
        float fog = FogShading ? Math.Clamp((StrategicZoom - camH) / (StrategicZoom * 0.2f), 0f, 1f) : 0f;
        Raylib.SetShaderValue(_shader, _locFog, fog, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locBorderScale, Math.Clamp(camH / 40f, 0.6f, 4f), ShaderUniformDataType.Float);
        // occupation stripes keep their size on screen roughly, but only change in steps: a size that follows the camera
        // continuously makes the stripes crawl and flicker
        float stripe = MathF.Pow(2f, MathF.Round(MathF.Log2(Math.Max(0.05f, camH * 0.045f))));
        Raylib.SetShaderValue(_shader, _locStripe, stripe, ShaderUniformDataType.Float);
        // the Provinces map mode draws only real province borders: the thin lines between the locations inside one province would hide merges
        Raylib.SetShaderValue(_shader, _locGeoBase, GeoBase && Mode == MapMode.Provinces ? 1.0f : 0.0f, ShaderUniformDataType.Float);
        bool provincesOnly = Mode == MapMode.Provinces && CustomMode == null && _game.Geo.Enabled;
        Raylib.SetShaderValue(_shader, _locShowBorders, camH < 120 && !provincesOnly ? 1.0f : 0.0f, ShaderUniformDataType.Float);
        // state borders fade out when zoomed out (country borders stay)
        float stateFade = Math.Clamp((StrategicZoom * 0.6f - camH) / (StrategicZoom * 0.25f), 0f, 1f);
        if (HighlightState != null) stateFade = Math.Max(stateFade, 0.6f);
        Raylib.SetShaderValue(_shader, _locStateBorders, stateFade, ShaderUniformDataType.Float);

        // Hover/selection highlights change while the camera moves (every frame during a pan).
        // Patch just the affected locations' flag texels instead of rebuilding + re-uploading the
        // whole lookup texture per frame - that full rebuild was the main CPU cost of fast panning.
        int hoverIdx = Hovered?.Index ?? -1, selIdx = Selected?.Index ?? -1;
        if (!Dirty && (hoverIdx != _lastHovered || selIdx != _lastSelected))
        {
            if (_lastHovered >= 0 && _lastHovered != hoverIdx && _lastHovered != selIdx)
                UpdateLutFlags(_lastHovered);
            if (_lastSelected >= 0 && _lastSelected != hoverIdx && _lastSelected != selIdx)
                UpdateLutFlags(_lastSelected);
            if (hoverIdx >= 0 && hoverIdx != _lastHovered && hoverIdx != _lastSelected)
                UpdateLutFlags(hoverIdx);
            if (selIdx >= 0 && selIdx != _lastHovered && selIdx != _lastSelected)
                UpdateLutFlags(selIdx);
        }
        _lastHovered = hoverIdx;
        _lastSelected = selIdx;

        // endless sea around the map, drawn with the map shader so it matches the water at the edges
        float seaY = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale - 0.03f;
        // The flat map repeats east-west: the map and one copy on each side cover any view (the view is never wider than the map).
        // Draw the middle one first: the depth test then rejects the fragments hidden under it instead of shading them twice.
        int copies = Globe ? 1 : 3;
        // skip the terrain chunks outside the view (flat map only)
        bool cull = !Globe && cam.Projection == CameraProjection.Perspective;
        var fwd = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, cam.Up));
        var upV = Vector3.Cross(right, fwd);
        float tanY = MathF.Tan(cam.FovY * MathF.PI / 360f) * 1.08f, tanX = tanY * Viewport.W / Math.Max(1f, Viewport.H);
        bool trees = !Globe && _hasTrees && !Bench.Skip.Contains("trees") && (camH < 40 || (camH < 70 && Mode == MapMode.Terrain));
        // far out a map pixel is smaller than a screen pixel: the coarse mesh looks the same and costs a sixteenth of the triangles
        bool lod = UserSettings.LodEnabled && camH > LodHeight;
        GlMultisample.Set(!lod); // adaptive MSAA: smooth edges up close, none where triangles are sub-pixel anyway
        for (int c = 0; c < copies; c++)
        {
            float shift = (c == 0 ? 0 : c == 1 ? -1 : 1) * WorldWidth;
            var at = Raymath.MatrixTranslate(shift, 0, 0);
            if (!Bench.Skip.Contains("terrain"))
            {
                var set = lod ? _lodChunks : _chunks; var sb = lod ? _lodBoxes : _chunkBoxes;
                for (int k = 0; k < set.Length; k++)
                    if (!cull || ChunkVisible(sb[k], shift, cam, fwd, right, upV, tanX, tanY)) Raylib.DrawMesh(set[k], _material, at);
            }
            if (trees) Raylib.DrawMesh(_treeMesh, _treeMaterial, at);
        }
        // endless sea beyond the map's north and south edges (the globe has none)
        if (!Globe && !Bench.Skip.Contains("sea")) Raylib.DrawMesh(_seaMesh, _material, Raymath.MatrixTranslate(WorldWidth / 2, seaY, WorldHeight / 2));
    }

    private int _lastHovered = -1, _lastSelected = -1;

    /// <summary>
    /// Recomputes and re-uploads only one location's flag texel of the LUT (hover/selection highlight),
    /// avoiding a full UpdateLut rebuild when only the highlight moved. The other two LUT rows for this
    /// location are unchanged by hover state, so they keep their last-built values in _lut.
    /// </summary>
    private void UpdateLutFlags(int index)
    {
        var w = _game.World;
        if ((uint)index >= (uint)w.Locations.Count) return;
        var l = w.Locations[index];
        int flags = 0;
        if (l.IsWater) flags |= 1;
        bool occupied = l.IsOccupied && Mode is MapMode.Political or MapMode.Diplomacy;
        if (occupied) flags |= 2;
        if (l == Selected) flags |= 4;
        if (l == Hovered) flags |= 8;
        if (l.IsLake) flags |= 16;
        if (!_game.IsVisible(l)) flags |= 32;
        if (HighlightState != null && l.State == HighlightState) flags |= 64;
        if (_game.Exploration.Enabled && !_game.Exploration.IsCharted(l)) flags &= 1 | 4 | 8 | 16 | 32;
        var stripe = occupied && l.Controller != null ? l.Controller.Color : Rgb.Black;
        var cell = new Color(stripe.R, stripe.G, stripe.B, (byte)flags);
        _lut[LutWidth * _blockRows * 2 + index] = cell;
        // The flag rows start at 2*blockRows, then each row stores LutWidth locations.
        // Upload to the same row/column as the updated texel in the CPU-side LUT; using only
        // 2*blockRows writes every hover update into the first flag row and corrupts unrelated
        // provinces (including their water/land bit) when location indices exceed LutWidth.
        var src = stackalloc Color[1];
        src[0] = cell;
        int lutX = index % LutWidth;
        int lutY = _blockRows * 2 + index / LutWidth;
        Raylib.UpdateTextureRec(_lutTex, new Rectangle(lutX, lutY, 1, 1), src);
    }

    /// <summary>The lut value for how unknown a location is: 255 never seen, 0 known, in between while the chart burns away.</summary>
    private byte TiByte(int index, bool charted)
    {
        if (!charted) return 255;
        if (!_revealing.TryGetValue(index, out float p)) return 0;
        return p <= 0f ? (byte)255 : (byte)((1f - p) * 255f);
    }

    /// <summary>Moves the reveal of newly charted lands forward and patches just those lut texels.</summary>
    private unsafe void AdvanceReveal()
    {
        if (_revealing.Count == 0) return;
        float step = Math.Min(Raylib.GetFrameTime(), 0.1f) / RevealSeconds;
        int block3 = LutWidth * _blockRows * 3;
        var indices = _revealing.Keys.ToArray();
        var changed = new List<int>(indices.Length);
        foreach (int i in indices)
        {
            float p = _revealing[i] + step;
            if (p >= 1f) _revealing.Remove(i); else _revealing[i] = p;
            byte b = p <= 0f ? (byte)255 : p >= 1f ? (byte)0 : (byte)((1f - p) * 255f);
            var c = _lut[block3 + i];
            if (c.B == b && c.A == 255) continue;
            _lut[block3 + i] = new Color((int)c.R, (int)c.G, (int)b, 255);
            changed.Add(i);
        }
        if (changed.Count > 250) { Raylib.UpdateTexture(_lutTex, _lut); return; }
        var src = stackalloc Color[1];
        foreach (int i in changed)
        {
            src[0] = _lut[block3 + i];
            Raylib.UpdateTextureRec(_lutTex, new Rectangle(i % LutWidth, _blockRows * 3 + i / LutWidth, 1, 1), src);
        }
    }

    public void Dispose()
    {
        Raylib.UnloadTexture(_mapDataTex);
        Raylib.UnloadTexture(_lutTex);
        Raylib.UnloadTexture(_atlasTex);
        Raylib.UnloadTexture(_riverTex);
        if (_hasBlendTex) { Raylib.UnloadTexture(_blendTex0); Raylib.UnloadTexture(_blendTex1); }
        if (_hasNormalMap) Raylib.UnloadTexture(_normalTex);
        Raylib.UnloadShader(_shader);
        foreach (var c in _chunks) Raylib.UnloadMesh(c);
        foreach (var c in _lodChunks) Raylib.UnloadMesh(c);
        Raylib.UnloadMesh(_seaMesh);
        if (_hasTrees) Raylib.UnloadMesh(_treeMesh);
    }
}
