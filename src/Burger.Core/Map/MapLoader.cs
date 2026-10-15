using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>Thrown when the game cannot be loaded at all (missing province map, etc.).</summary>
public sealed class GameLoadException : Exception
{
    public GameLoadException(string message) : base(message) { }
}

/// <summary>
/// Builds the map from the input images:
/// <list type="bullet">
/// <item><b>provinces.png</b> - one unique color per location (required)</item>
/// <item><b>locations.txt</b> - <c>HEX = location_name</c> for every color</item>
/// <item><b>heightmap.png</b> - greyscale elevation</item>
/// <item><b>terrain.png</b> - terrain type colors from common/terrain.txt</item>
/// <item><b>trees.png</b> - greyscale tree density</item>
/// <item><b>rivers.png</b> - any non-background pixel is a river</item>
/// </list>
/// Images may have different resolutions; everything is resampled to the province map size.
/// </summary>
public static class MapLoader
{
    private sealed class LocationDef
    {
        public Rgb Color;
        public string Key = "";
        public string? Name;
        public string? Terrain;
        public string? Type;
        public int Fort;
        public string Where = "";
    }

    private sealed class BorderAccum
    {
        public int A, B;
        public int Count;
        public double SumX, SumY;
        public bool River;
        public List<int> Points = new();
    }

    public static void Load(GameData data, World world)
    {
        string mapDir = data.PathOf("map");
        string mapFile = Path.Combine(mapDir, "default.map");
        var settings = File.Exists(mapFile)
            ? MapSettings.FromNode(ScriptParser.ParseFile(mapFile, "map/default.map"))
            : new MapSettings();
        if (!File.Exists(mapFile)) Log.Warn("map/default.map not found - using default file names");

        var map = new MapData { Settings = settings };
        world.Map = map;

        // ---------------------------------------------------------------- provinces
        string provPath = Path.Combine(mapDir, settings.Provinces);
        // The decoded per-pixel arrays are cached on disk (see MapCache); each entry's key covers every file and setting it was made from.
        string cacheDir = MapCache.Dir(data);
        string provKey = MapCache.Key("file:" + provPath, "file:" + Path.Combine(mapDir, settings.Locations));
        var cachedProv = MapCache.Load<ushort>(cacheDir, "provinces", provKey, out var provMeta); // meta: width, height, then the colors of locations made for unnamed provinces
        RawImage? prov = null;
        int w, h;
        if (cachedProv != null && provMeta.Length >= 2) { w = provMeta[0]; h = provMeta[1]; }
        else
        {
            cachedProv = null;
            prov = ImageIO.Load(provPath, false, "map/" + settings.Provinces)
                ?? throw new GameLoadException($"Province map not found or unreadable: map/{settings.Provinces}");
            w = prov.Width; h = prov.Height;
        }
        map.Width = w;
        map.Height = h;
        Log.Info($"Province map {w}x{h}");

        // ---------------------------------------------------------------- location definitions
        var defs = ParseLocationFile(Path.Combine(mapDir, settings.Locations), "map/" + settings.Locations);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
        {
            if (world.LocationByColor.ContainsKey(d.Color.Packed))
            {
                Log.Error($"Color {d.Color.ToHex()} is assigned to more than one location ('{d.Key}' ignored)", d.Where);
                continue;
            }
            if (!seenKeys.Add(d.Key))
            {
                Log.Error($"Duplicate location name '{d.Key}' - every location needs a unique name", d.Where);
                continue;
            }
            var loc = new Location { Index = world.Locations.Count, Key = d.Key, MapColor = d.Color, NameOverride = d.Name, Fort = d.Fort };
            world.Locations.Add(loc);
            world.LocationByKey[loc.Key] = loc;
            world.LocationByColor[d.Color.Packed] = loc;
            if (d.Type != null)
            {
                switch (d.Type.ToLowerInvariant())
                {
                    case "sea": case "ocean": loc.ForcedWater = true; break;
                    case "lake": loc.ForcedWater = true; loc.IsLake = true; break;
                    case "impassable": case "wasteland": loc.Impassable = true; break;
                    case "land": break;
                    default: Log.Warn($"Unknown location type '{d.Type}' (use land, sea, lake or impassable)", d.Where); break;
                }
            }
        }

        // ---------------------------------------------------------------- pixel → location
        map.PixelLocation = new int[w * h];
        var unknownColors = new Dictionary<int, int>();
        Location NewUnnamed(int col)
        {
            // auto-create so the map is still usable; tell the modder to name it
            var c = Rgb.FromPacked(col);
            string key = "loc_" + c.ToHex().ToLowerInvariant();
            var loc = new Location { Index = world.Locations.Count, Key = key, MapColor = c };
            world.Locations.Add(loc);
            world.LocationByKey[key] = loc;
            world.LocationByColor[col] = loc;
            unknownColors[col] = 0;
            return loc;
        }
        if (cachedProv != null)
        {
            for (int i = 2; i < provMeta.Length; i++) NewUnnamed(provMeta[i]);
            var pl = map.PixelLocation;
            for (int i = 0; i < pl.Length; i++) pl[i] = cachedProv[i] == ushort.MaxValue ? -1 : cachedProv[i];
        }
        else
        {
            int lastColor = -1, lastIdx = -1;
            for (int i = 0, p = 0; i < w * h; i++, p += 4)
            {
                int a = prov!.Data[p + 3];
                if (a == 0) { map.PixelLocation[i] = -1; continue; }
                int col = (prov.Data[p] << 16) | (prov.Data[p + 1] << 8) | prov.Data[p + 2];
                if (col != lastColor)
                {
                    lastColor = col;
                    lastIdx = world.LocationByColor.TryGetValue(col, out var l) ? l.Index : NewUnnamed(col).Index;
                }
                map.PixelLocation[i] = lastIdx;
            }
            if (world.Locations.Count <= 65000)
            {
                var packed = new ushort[w * h];
                for (int i = 0; i < packed.Length; i++) packed[i] = map.PixelLocation[i] < 0 ? ushort.MaxValue : (ushort)map.PixelLocation[i];
                MapCache.Save(cacheDir, "provinces", provKey, new[] { w, h }.Concat(unknownColors.Keys).ToArray(), packed);
            }
        }
        if (unknownColors.Count > 0)
            Log.Warn($"{unknownColors.Count} province colors have no entry in {settings.Locations}. They were named loc_<hex>. " +
                     "Run 'burger generate-locations' (or add lines like  FF0000 = berlin) to name them.", "map/" + settings.Locations);
        if (world.Locations.Count > 65000)
            throw new GameLoadException($"Too many locations ({world.Locations.Count}); the maximum is 65000.");

        // ---------------------------------------------------------------- terrain
        map.PixelTerrain = new byte[w * h];
        string terrainPath = Path.Combine(mapDir, settings.Terrain);
        RawImage? terrainImg = null;
        byte[]? cachedTerrain = null;
        int[] terrMeta = Array.Empty<int>();
        string terrKey = "";
        if (File.Exists(terrainPath))
        {
            terrKey = MapCache.Key("file:" + terrainPath, $"{w}x{h}", string.Join(",", data.Terrains.Select(t => t.MapColor.Packed + ":" + t.Index)));
            cachedTerrain = MapCache.Load<byte>(cacheDir, "terrain", terrKey, out terrMeta);
            if (cachedTerrain != null && cachedTerrain.Length != w * h) cachedTerrain = null;
            if (cachedTerrain == null) terrainImg = ImageIO.Load(terrainPath, false, "map/" + settings.Terrain);
        }
        if (data.Terrains.Count == 0) throw new GameLoadException("common/terrain.txt defines no terrain types.");
        if (cachedTerrain == null && terrainImg == null)
        {
            Log.Warn($"map/{settings.Terrain} not found - every location uses '{data.DefaultTerrain.Key}' terrain");
            Array.Fill(map.PixelTerrain, (byte)data.DefaultTerrain.Index);
        }
        else if (cachedTerrain != null)
        {
            map.PixelTerrain = cachedTerrain;
            if (terrMeta.Length >= 2 && terrMeta[0] > 0)
                Log.Warn($"terrain.png contains {terrMeta[0]} colors not defined in common/terrain.txt (nearest terrain used), e.g. {Rgb.FromPacked(terrMeta[1]).ToHex()}", "map/" + settings.Terrain);
        }
        else
        {
            var cache = new Dictionary<int, byte>();
            foreach (var t in data.Terrains) cache[t.MapColor.Packed] = (byte)t.Index;
            int tw = terrainImg!.Width, th = terrainImg.Height;
            var unknownTerrain = new HashSet<int>();
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(th - 1, (int)((y + 0.5) * th / h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(tw - 1, (int)((x + 0.5) * tw / w));
                    int col = terrainImg.RgbAt(sx, sy);
                    if (!cache.TryGetValue(col, out byte ti))
                    {
                        ti = (byte)NearestTerrain(data, Rgb.FromPacked(col)).Index;
                        cache[col] = ti;
                        unknownTerrain.Add(col);
                    }
                    map.PixelTerrain[y * w + x] = ti;
                }
            }
            if (unknownTerrain.Count > 0)
                Log.Warn($"terrain.png contains {unknownTerrain.Count} colors not defined in common/terrain.txt (nearest terrain used), e.g. {Rgb.FromPacked(unknownTerrain.First()).ToHex()}",
                    "map/" + settings.Terrain);
            MapCache.Save(cacheDir, "terrain", terrKey, new[] { unknownTerrain.Count, unknownTerrain.Count > 0 ? unknownTerrain.First() : 0 }, map.PixelTerrain);
        }

        map.ContentKey = MapCache.Enabled ? MapCache.Key(provKey, terrKey) : "";

        // ---------------------------------------------------------------- height, trees, rivers
        byte[]? CachedGrey(string name, string file)
        {
            string path = Path.Combine(mapDir, file);
            if (!File.Exists(path)) return null;
            string key = MapCache.Key("file:" + path, $"{w}x{h}");
            var hit = MapCache.Load<byte>(cacheDir, name, key, out _);
            if (hit != null && hit.Length == w * h) return hit;
            var img = ImageIO.Load(path, true, "map/" + file);
            if (img == null) return null;
            var grey = ImageIO.ResampleGrey(img, w, h);
            MapCache.Save(cacheDir, name, key, Array.Empty<int>(), grey);
            return grey;
        }
        var heightGrey = CachedGrey("heightmap", settings.Heightmap);
        if (heightGrey != null) map.HeightMap = heightGrey;
        else
        {
            Log.Warn($"map/{settings.Heightmap} not found - the map will be flat");
            map.HeightMap = new byte[w * h];
            Array.Fill(map.HeightMap, (byte)Math.Min(255, settings.SeaLevel + 20));
        }

        map.TreeMap = CachedGrey("trees", settings.Trees) ?? new byte[w * h];

        map.RiverMap = new byte[w * h];
        string riverPath = Path.Combine(mapDir, settings.Rivers);
        string riverKey = File.Exists(riverPath) ? MapCache.Key("file:" + riverPath, $"{w}x{h}", settings.RiverBackground.Packed.ToString()) : "";
        var cachedRivers = riverKey.Length > 0 ? MapCache.Load<byte>(cacheDir, "rivers", riverKey, out _) : null;
        RawImage? riverImg = null;
        if (cachedRivers != null && cachedRivers.Length == w * h) map.RiverMap = cachedRivers;
        else if (riverKey.Length > 0) riverImg = ImageIO.Load(riverPath, false, "map/" + settings.Rivers);
        if (riverImg != null)
        {
            int bg = settings.RiverBackground.Packed;
            const int paradoxPink = 0xFF0080;
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(riverImg.Height - 1, (int)((y + 0.5) * riverImg.Height / h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(riverImg.Width - 1, (int)((x + 0.5) * riverImg.Width / w));
                    int pi = (sy * riverImg.Width + sx) * 4;
                    if (riverImg.Data[pi + 3] < 128) continue;
                    int col = riverImg.RgbAt(sx, sy);
                    if (col != bg && col != paradoxPink) map.RiverMap[y * w + x] = 255;
                }
            }
            MapCache.Save(cacheDir, "rivers", riverKey, Array.Empty<int>(), map.RiverMap);
        }

        if (map.ContentKey.Length > 0) map.ContentKey = MapCache.Key(map.ContentKey, riverKey);
        ComputeLocationGeometry(data, world, defs);
        ComputeAdjacency(data, world);
        LoadAdjacencyFile(data, world, Path.Combine(mapDir, settings.Adjacencies), "map/" + settings.Adjacencies);

        foreach (var loc in world.Locations)
            loc.IsCoastal = loc.IsLand && loc.Neighbors.Any(n => n.To.IsWater && !n.To.IsLake);

        Log.Info($"Map loaded: {world.Locations.Count} locations, {world.Locations.Sum(l => l.Neighbors.Count) / 2} borders");
    }

    private static TerrainType NearestTerrain(GameData data, Rgb c)
    {
        TerrainType best = data.Terrains[0];
        int bestD = int.MaxValue;
        foreach (var t in data.Terrains)
        {
            int dr = t.MapColor.R - c.R, dg = t.MapColor.G - c.G, db = t.MapColor.B - c.B;
            int d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static List<LocationDef> ParseLocationFile(string path, string display)
    {
        var list = new List<LocationDef>();
        if (!File.Exists(path))
        {
            Log.Warn($"{display} not found - all locations will get automatic names");
            return list;
        }
        var root = ScriptParser.ParseFile(path, display);
        foreach (var n in root.All)
        {
            if (n.Op == Op.None) { Log.Error($"Expected  HEX = name  but found '{n.Key}'", n.Location); continue; }
            string? hex = null;
            string? key = null;
            Node? block = null;
            if (n.Children != null)
            {
                // FF0000 = { name = berlin terrain = hills }    or    berlin = { color = FF0000 }
                if (Rgb.TryParseHex(n.Key, out _)) { hex = n.Key; key = n.GetString("id") ?? n.GetString("name"); }
                else { key = n.Key; hex = n.GetString("color"); if (hex == null && Rgb.TryParse(n.Get("color"), out var cc)) hex = cc.ToHex(); }
                block = n;
            }
            else if (n.Value != null)
            {
                bool leftHex = Rgb.TryParseHex(n.Key, out _);
                bool rightHex = Rgb.TryParseHex(n.Value, out _);
                if (leftHex) { hex = n.Key; key = n.Value; }
                else if (rightHex) { hex = n.Value; key = n.Key; }
            }
            if (hex == null || !Rgb.TryParseHex(hex, out var color))
            {
                Log.Error($"Could not read a hex color in '{n.Key}' (expected something like  4A7F1E = berlin)", n.Location);
                continue;
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "loc_" + color.ToHex().ToLowerInvariant();
            }
            var d = new LocationDef { Color = color, Where = n.Location };
            // keys must be single words; turn "New York" into new_york and keep the nice name
            if (key.Any(char.IsWhiteSpace))
            {
                d.Name = key;
                key = string.Join("_", key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            }
            d.Key = key;
            if (block != null)
            {
                d.Name = block.GetString("display_name", d.Name);
                d.Terrain = block.GetString("terrain");
                d.Type = block.GetString("type");
                d.Fort = block.GetInt("fort", 0);
            }
            list.Add(d);
        }
        return list;
    }

    private static void ComputeLocationGeometry(GameData data, World world, List<LocationDef> defs)
    {
        var map = world.Map;
        int w = map.Width, h = map.Height, n = world.Locations.Count;
        var sumX = new double[n];
        var sumY = new double[n];
        var bestTerr = new int[n];
        string cacheDir = MapCache.Dir(data);
        string geomKey = map.ContentKey.Length > 0 ? MapCache.Key(map.ContentKey, "geom", data.Terrains.Count.ToString()) : "";
        const int Fields = 10;
        var g = geomKey.Length > 0 ? MapCache.Load<int>(cacheDir, "geom", geomKey, out _) : null;
        if (g != null && g.Length == n * Fields)
        {
            for (int li = 0; li < n; li++)
            {
                var loc = world.Locations[li];
                int o = li * Fields;
                loc.PixelCount = g[o]; loc.MinX = g[o + 1]; loc.MaxX = g[o + 2]; loc.MinY = g[o + 3]; loc.MaxY = g[o + 4];
                sumX[li] = BitConverter.Int64BitsToDouble(((long)(uint)g[o + 6] << 32) | (uint)g[o + 5]);
                sumY[li] = BitConverter.Int64BitsToDouble(((long)(uint)g[o + 8] << 32) | (uint)g[o + 7]);
                bestTerr[li] = g[o + 9];
            }
        }
        else
        {
            var terrainHist = new int[n * 256];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int li = map.PixelLocation[i];
                    if (li < 0) continue;
                    var loc = world.Locations[li];
                    loc.PixelCount++;
                    sumX[li] += x + 0.5;
                    sumY[li] += y + 0.5;
                    if (x < loc.MinX) loc.MinX = x;
                    if (x > loc.MaxX) loc.MaxX = x;
                    if (y < loc.MinY) loc.MinY = y;
                    if (y > loc.MaxY) loc.MaxY = y;
                    terrainHist[li * 256 + map.PixelTerrain[i]]++;
                }
            }
            var packed = new int[n * Fields];
            for (int li = 0; li < n; li++)
            {
                var loc = world.Locations[li];
                int bestT = 0, bestC = -1;
                for (int t = 0; t < data.Terrains.Count && t < 256; t++)
                {
                    int c = terrainHist[li * 256 + t];
                    if (c > bestC) { bestC = c; bestT = t; }
                }
                bestTerr[li] = bestT;
                long bx = BitConverter.DoubleToInt64Bits(sumX[li]), by = BitConverter.DoubleToInt64Bits(sumY[li]);
                int o = li * Fields;
                packed[o] = loc.PixelCount; packed[o + 1] = loc.MinX; packed[o + 2] = loc.MaxX; packed[o + 3] = loc.MinY; packed[o + 4] = loc.MaxY;
                packed[o + 5] = (int)bx; packed[o + 6] = (int)(bx >> 32); packed[o + 7] = (int)by; packed[o + 8] = (int)(by >> 32); packed[o + 9] = bestT;
            }
            if (geomKey.Length > 0) MapCache.Save(cacheDir, "geom", geomKey, Array.Empty<int>(), packed);
        }

        var defByKey = defs.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var loc in world.Locations)
        {
            int li = loc.Index;
            if (loc.PixelCount == 0)
            {
                Log.Warn($"Location '{loc.Key}' ({loc.MapColor.ToHex()}) does not appear on the province map");
                loc.MinX = loc.MinY = loc.MaxX = loc.MaxY = 0;
                loc.Terrain = data.DefaultTerrain;
                continue;
            }
            loc.CenterX = (float)(sumX[li] / loc.PixelCount);
            loc.CenterY = (float)(sumY[li] / loc.PixelCount);
            // make sure the label/unit position lies inside the location (centroid can be outside for C-shapes)
            int cx = Math.Clamp((int)loc.CenterX, 0, w - 1), cy = Math.Clamp((int)loc.CenterY, 0, h - 1);
            if (map.PixelLocation[cy * w + cx] != li)
            {
                double best = double.MaxValue;
                int bx = cx, by = cy;
                int step = Math.Max(1, (int)Math.Sqrt((loc.MaxX - loc.MinX + 1) * (loc.MaxY - loc.MinY + 1) / 40000.0));
                for (int y = loc.MinY; y <= loc.MaxY; y += step)
                    for (int x = loc.MinX; x <= loc.MaxX; x += step)
                    {
                        if (map.PixelLocation[y * w + x] != li) continue;
                        double d = (x + 0.5 - loc.CenterX) * (x + 0.5 - loc.CenterX) + (y + 0.5 - loc.CenterY) * (y + 0.5 - loc.CenterY);
                        if (d < best) { best = d; bx = x; by = y; }
                    }
                loc.CenterX = bx + 0.5f;
                loc.CenterY = by + 0.5f;
            }

            // terrain: explicit override or most common terrain pixel
            TerrainType? terrain = null;
            if (defByKey.TryGetValue(loc.Key, out var def) && def.Terrain != null)
            {
                if (!data.TerrainByKey.TryGetValue(def.Terrain, out terrain))
                    Log.Error($"Unknown terrain '{def.Terrain}' for location '{loc.Key}'", def.Where);
            }
            if (terrain == null)
            {
                terrain = data.Terrains[bestTerr[li]];
                // a location that is marked as land but whose pixels are water-terrain should still be land
                if (terrain.IsWater && !loc.ForcedWater && def != null && string.Equals(def.Type, "land", StringComparison.OrdinalIgnoreCase))
                    terrain = data.DefaultTerrain;
            }
            if (loc.ForcedWater && !terrain.IsWater && data.WaterTerrain != null) terrain = data.WaterTerrain;
            loc.Terrain = terrain;
        }
    }

    /// <summary>The border scan of the whole map flattened to ints: per border A, B, pixel count, river flag, the two sums (double bits), and the packed points.</summary>
    private static int[] WriteBorders(Dictionary<long, BorderAccum> acc)
    {
        var o = new List<int>(acc.Count * 16) { acc.Count };
        foreach (var ba in acc.Values)
        {
            long sx = BitConverter.DoubleToInt64Bits(ba.SumX), sy = BitConverter.DoubleToInt64Bits(ba.SumY);
            o.Add(ba.A); o.Add(ba.B); o.Add(ba.Count); o.Add(ba.River ? 1 : 0);
            o.Add((int)sx); o.Add((int)(sx >> 32)); o.Add((int)sy); o.Add((int)(sy >> 32));
            o.Add(ba.Points.Count); o.AddRange(ba.Points);
        }
        return o.ToArray();
    }

    private static bool TryReadBorders(int[] d, Dictionary<long, BorderAccum> acc)
    {
        try
        {
            int n = d[0], p = 1;
            for (int k = 0; k < n; k++)
            {
                var ba = new BorderAccum { A = d[p], B = d[p + 1], Count = d[p + 2], River = d[p + 3] != 0 };
                ba.SumX = BitConverter.Int64BitsToDouble(((long)(uint)d[p + 5] << 32) | (uint)d[p + 4]);
                ba.SumY = BitConverter.Int64BitsToDouble(((long)(uint)d[p + 7] << 32) | (uint)d[p + 6]);
                int np = d[p + 8];
                p += 9;
                ba.Points = new List<int>(new ArraySegment<int>(d, p, np));
                p += np;
                acc[((long)ba.A << 32) | (uint)ba.B] = ba;
            }
            return p == d.Length;
        }
        catch (IndexOutOfRangeException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static void ComputeAdjacency(GameData data, World world)
    {
        var map = world.Map;
        int w = map.Width, h = map.Height;
        var acc = new Dictionary<long, BorderAccum>();

        void Add(int a, int b, int x2, int y2, bool river)
        {
            if (a < 0 || b < 0 || a == b) return;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (!acc.TryGetValue(key, out var ba))
            {
                ba = new BorderAccum { A = lo, B = hi };
                acc[key] = ba;
            }
            ba.Count++;
            ba.SumX += x2 / 2.0;
            ba.SumY += y2 / 2.0;
            if (river) ba.River = true;
            ba.Points.Add((int)(((uint)x2 << 16) | ((uint)y2 & 0xFFFF)));
        }

        string cacheDir = MapCache.Dir(data);
        string adjKey = map.ContentKey.Length > 0 ? MapCache.Key(map.ContentKey, "borders") : "";
        var cached = adjKey.Length > 0 ? MapCache.Load<int>(cacheDir, "borders", adjKey, out _) : null;
        if (cached != null && TryReadBorders(cached, acc)) { }
        else
        {
            acc.Clear();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int a = map.PixelLocation[i];
                    if (x + 1 < w)
                    {
                        int b = map.PixelLocation[i + 1];
                        if (a != b) Add(a, b, (x + 1) * 2, y * 2 + 1, map.RiverMap[i] != 0 || map.RiverMap[i + 1] != 0);
                    }
                    if (y + 1 < h)
                    {
                        int b = map.PixelLocation[i + w];
                        if (a != b) Add(a, b, x * 2 + 1, (y + 1) * 2, map.RiverMap[i] != 0 || map.RiverMap[i + w] != 0);
                    }
                }
            }
            if (adjKey.Length > 0) MapCache.Save(cacheDir, "borders", adjKey, Array.Empty<int>(), WriteBorders(acc));
        }

        double kmPerPixel = data.Defines.KmPerPixel;
        foreach (var ba in acc.Values)
        {
            var la = world.Locations[ba.A];
            var lb = world.Locations[ba.B];
            var kind = la.IsWater || lb.IsWater ? AdjacencyKind.Sea : AdjacencyKind.Land;
            float midX = (float)(ba.SumX / ba.Count), midY = (float)(ba.SumY / ba.Count);
            var points = OrderBorderPoints(ba.Points, la, lb);
            double dist = Math.Sqrt((la.CenterX - lb.CenterX) * (la.CenterX - lb.CenterX) + (la.CenterY - lb.CenterY) * (la.CenterY - lb.CenterY)) * kmPerPixel;
            dist = Math.Max(dist, 1);
            la.Neighbors.Add(new Adjacency { From = la, To = lb, Kind = kind, River = ba.River, BorderPixels = ba.Count, MidX = midX, MidY = midY, BorderPoints = points, DistanceKm = dist });
            lb.Neighbors.Add(new Adjacency { From = lb, To = la, Kind = kind, River = ba.River, BorderPixels = ba.Count, MidX = midX, MidY = midY, BorderPoints = points, DistanceKm = dist });
        }
    }

    /// <summary>Orders border points along the border and keeps at most 12 evenly spaced ones.</summary>
    private static List<(float X, float Y)> OrderBorderPoints(List<int> packed, Location a, Location b)
    {
        var pts = new List<(float X, float Y)>(packed.Count);
        foreach (int p in packed) pts.Add(((int)((uint)p >> 16) / 2f, (p & 0xFFFF) / 2f));
        // project on the direction perpendicular to the line between the two centers
        float dx = b.CenterX - a.CenterX, dy = b.CenterY - a.CenterY;
        float px = -dy, py = dx;
        float len = MathF.Sqrt(px * px + py * py);
        if (len < 1e-3f) { px = 1; py = 0; } else { px /= len; py /= len; }
        pts.Sort((p1, p2) => (p1.X * px + p1.Y * py).CompareTo(p2.X * px + p2.Y * py));
        const int max = 12;
        if (pts.Count <= max) return pts;
        var result = new List<(float X, float Y)>(max);
        for (int i = 0; i < max; i++) result.Add(pts[(int)((i + 0.5) * pts.Count / max)]);
        return result;
    }

    private static void LoadAdjacencyFile(GameData data, World world, string path, string display)
    {
        if (!File.Exists(path)) return;
        var root = ScriptParser.ParseFile(path, display);
        foreach (var n in root.GetAll("adjacency"))
        {
            var from = world.GetLocation(n.GetString("from"));
            var to = world.GetLocation(n.GetString("to"));
            if (from == null || to == null)
            {
                Log.Error($"adjacency: unknown location '{(from == null ? n.GetString("from") : n.GetString("to"))}'", n.Location);
                continue;
            }
            string type = (n.GetString("type") ?? "strait").ToLowerInvariant();
            if (type == "impassable")
            {
                from.Neighbors.RemoveAll(a => a.To == to);
                to.Neighbors.RemoveAll(a => a.To == from);
                continue;
            }
            if (from.AdjacencyTo(to) != null) continue;
            double dist = Math.Sqrt((from.CenterX - to.CenterX) * (from.CenterX - to.CenterX) + (from.CenterY - to.CenterY) * (from.CenterY - to.CenterY)) * data.Defines.KmPerPixel;
            var pts = new List<(float X, float Y)> { (from.CenterX, from.CenterY), (to.CenterX, to.CenterY) };
            float mx = (from.CenterX + to.CenterX) / 2, my = (from.CenterY + to.CenterY) / 2;
            bool river = n.GetBool("river", false);
            from.Neighbors.Add(new Adjacency { From = from, To = to, Kind = AdjacencyKind.Strait, MidX = mx, MidY = my, DistanceKm = dist, BorderPoints = pts, River = river });
            to.Neighbors.Add(new Adjacency { From = to, To = from, Kind = AdjacencyKind.Strait, MidX = mx, MidY = my, DistanceKm = dist, BorderPoints = pts, River = river });
        }
    }
}
