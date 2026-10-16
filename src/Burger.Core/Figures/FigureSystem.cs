using Burger.Core.Model;

namespace Burger.Core.Figures;

/// <summary>
/// The Figures: every animated body the game can show, by key. The engine always has <see cref="HumanMale.Key"/>, the bare
/// human body with its idle, walk and run cycles, and <see cref="LightTank.Key"/>, a light tank with tracks and a gun that moves; anything that shows a person (soldiers on the map, portraits, scenes) takes
/// the model from here and plays its clips through an <see cref="Animator"/>, so one rig and one set of animations serve the
/// whole game. A game or mod replaces or adds to them with .glb files in gfx/models/ (see <see cref="Load"/>) or from C# with
/// <see cref="Register"/>. Core only holds the data; drawing is the renderer's job.
/// </summary>
public sealed class FigureSystem
{
    private readonly Dictionary<string, FigureModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private bool _builtIn;
    private readonly Dictionary<string, (string Body, string[] Pieces)> _outfits = new(StringComparer.OrdinalIgnoreCase);

    private readonly Game _game;
    private bool _glbBody; // a .glb replaced the built-in body: people variants are drawn from the procedural one only
    private readonly Dictionary<string, (int Day, List<(string Key, double Weight)> Mix)> _mixes = new(StringComparer.OrdinalIgnoreCase);

    public FigureSystem(Game game) { _game = game; }

    /// <summary>Changes whenever a model is added or replaced, so renderers know to rebuild what they uploaded.</summary>
    public int Version { get; private set; }

    /// <summary>The built-in human body (or whatever a game registered in its place).</summary>
    public FigureModel Human => Find(HumanMale.Key)!;

    /// <summary>The infantryman in a country's uniform (the generic soldier when it has none).</summary>
    public FigureModel SoldierOf(string tag) => Find(Uniforms.OutfitKey(tag)) ?? Soldier;

    /// <summary>The equipped infantryman: the body in uniform with helmet, pack and rifle (the figure the map shows).</summary>
    public FigureModel Soldier => Find(SoldierGear.Soldier) ?? Human;

    public IEnumerable<FigureModel> All { get { EnsureBuiltIn(); return _models.Values; } }

    /// <summary>The model with this key, or null.</summary>
    public FigureModel? Find(string key)
    {
        EnsureBuiltIn();
        if (_models.TryGetValue(key, out var m)) return m;
        return key.Contains('@') ? MakeVariant(key) : null;
    }

    /// <summary>The infantryman of a country as one people looks: key <c>soldier_tag@ancestry</c> (the plain soldier when that look is unknown).</summary>
    public FigureModel SoldierOf(string tag, string? ancestry)
    {
        var plain = SoldierOf(tag);
        return ancestry == null ? plain : Find(plain.Key + "@" + ancestry) ?? plain;
    }

    /// <summary>
    /// The infantryman for a division: its country's uniform on a body that looks like one of the country's peoples. The people come from
    /// the uniform's Look, else its Mix, else the culture groups of the country's states; the division id decides which, so the same division always looks the same.
    /// </summary>
    public FigureModel SoldierFor(Division d)
    {
        var tag = d.Owner.Tag;
        var u = Uniforms.Find(tag);
        string? key = u?.Look;
        if (key == null)
        {
            int day = _game.World.Date.TotalDays;
            if (!_mixes.TryGetValue(tag, out var cached) || day - cached.Day > 30)
                _mixes[tag] = cached = (day, MixOf(d.Owner, u));
            key = Ancestries.Pick(cached.Mix, d.Id);
        }
        if (key == null) return SoldierOf(tag);
        bool female = u?.FemaleShare > 0 && (uint)(d.Id * 40503 + 17) % 1000 < u.FemaleShare * 1000;
        return SoldierOf(tag, Ancestries.Person(key, female, d.Id));
    }

    private List<(string Key, double Weight)> MixOf(Country c, Uniform? u)
    {
        var shares = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Add(string? key, double w) { if (key != null && w > 0) shares[key] = shares.GetValueOrDefault(key) + w; }
        var pop = _game.Population;
        if (u?.Mix is { } fixedMix)
            foreach (var (k, w) in fixedMix) Add(Ancestries.Find(k) != null ? k : Ancestries.KeyFor(k, pop.Culture(k)?.Group.Key, u), w);
        else if (pop.Enabled)
        {
            foreach (var s in c.OwnedStates)
                foreach (var p in pop.Of(s).Pops)
                    Add(Ancestries.KeyFor(p.Culture, pop.Culture(p.Culture)?.Group.Key, u), p.Size);
            double total = shares.Values.Sum();
            foreach (var k in shares.Where(kv => kv.Value < total * 0.02).Select(kv => kv.Key).ToList()) shares.Remove(k);
            if (shares.Count == 0 && pop.Of(c).Culture is { } home) Add(Ancestries.KeyFor(home, pop.Culture(home)?.Group.Key, u), 1);
        }
        return shares.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
    }

    // key "base@ancestry": the base figure (a soldier or the bare body) on a body with that people's look, made when first asked for
    private FigureModel? MakeVariant(string key)
    {
        if (_glbBody) return null;
        int at = key.IndexOf('@');
        string baseKey = key[..at];
        var look = Ancestries.Parse(key[(at + 1)..]);
        if (look == null || !_models.TryGetValue(HumanMale.Key, out var human)) return null;
        string bodyKey = HumanMale.Key + "@" + look.Key;
        if (!_models.TryGetValue(bodyKey, out var body)) _models[bodyKey] = body = HumanMale.BuildVariant(human, look);
        if (baseKey.Equals(HumanMale.Key, StringComparison.OrdinalIgnoreCase)) return body;
        if (!_outfits.TryGetValue(baseKey, out var outfit) || !outfit.Body.Equals(HumanMale.Key, StringComparison.OrdinalIgnoreCase)) return null;
        var model = SoldierGear.Compose(key, body, outfit.Pieces.Select(p => _models.GetValueOrDefault(p) ?? (SoldierGear.Fallback(p) is { } fb ? _models.GetValueOrDefault(fb) : null)).OfType<FigureModel>());
        _models[key] = model;
        return model;
    }

    private void DropVariants()
    {
        foreach (string k in _models.Keys.Where(k => k.Contains('@')).ToList()) _models.Remove(k);
    }

    /// <summary>Adds a model, replacing any with the same key.</summary>
    public void Register(FigureModel model)
    {
        EnsureBuiltIn();
        _models[model.Key] = model;
        if (model.Key.Equals(HumanMale.Key, StringComparison.OrdinalIgnoreCase)) { _glbBody = true; DropVariants(); }
        Version++;
    }

    /// <summary>
    /// Defines a model made of a body and pieces of clothing and gear (models on the same rig, by key) and builds it. It is
    /// rebuilt when files in gfx/models/ replace the body or a piece.
    /// </summary>
    public void DefineOutfit(string key, string body, params string[] pieces)
    {
        EnsureBuiltIn();
        _outfits[key] = (body, pieces);
        RebuildOutfits();
        Version++;
    }

    private void RebuildOutfits()
    {
        foreach (var (key, (body, pieces)) in _outfits)
        {
            if (!_models.TryGetValue(body, out var b)) continue;
            var parts = pieces.Select(p => _models.GetValueOrDefault(p) ?? (SoldierGear.Fallback(p) is { } fb ? _models.GetValueOrDefault(fb) : null)).OfType<FigureModel>().ToList();
            // a country's own weapon or shield (gear_spear_<tag>.glb, gear_shield_<tag>.glb) is carried on top of the kit when the file exists
            if (key.StartsWith(SoldierGear.Soldier + "_", StringComparison.OrdinalIgnoreCase))
                foreach (string extra in new[] { "gear_spear", "gear_shield" })
                    if (_models.GetValueOrDefault(extra + key[SoldierGear.Soldier.Length..]) is { } e) parts.Add(e);
            _models[key] = SoldierGear.Compose(key, b, parts);
        }
    }

    /// <summary>A player for one figure of the model, started on a clip. Falls back to the human body if the key is unknown.</summary>
    public Animator NewAnimator(string model = HumanMale.Key, string? clip = HumanMale.Idle) => new(Find(model) ?? Human, clip);

    // the body is generated on first use: games that never show a figure do not pay for it
    private void EnsureBuiltIn()
    {
        if (_builtIn) return;
        _builtIn = true;
        _models[HumanMale.Key] = HumanMale.Build();
        _models[LightTank.Key] = LightTank.Build();
        foreach (var piece in SoldierGear.BuildPieces(_models[HumanMale.Key])) _models[piece.Key] = piece;
        _outfits[SoldierGear.Soldier] = (HumanMale.Key, SoldierGear.Kit);
        foreach (string tag in Uniforms.Tags) // one outfit per country with a uniform of its own
        {
            string suffix = "_" + tag.ToLowerInvariant();
            foreach (var piece in SoldierGear.BuildPieces(_models[HumanMale.Key], Uniforms.Find(tag)!, suffix)) _models[piece.Key] = piece;
            _outfits[Uniforms.OutfitKey(tag)] = (HumanMale.Key, SoldierGear.KitFor(Uniforms.Find(tag)!, suffix));
        }
        RebuildOutfits();
        Version++;
    }

    /// <summary>
    /// Loads the .glb files of <c>gfx/models/</c> in a game folder. <c>name.glb</c> is a model with key <c>name</c> (a file
    /// called <c>human_male.glb</c> replaces the built-in body); <c>name.anything.glb</c> only adds its animations to model
    /// <c>name</c>, matched by joint name, replacing clips of the same name.
    /// </summary>
    public void Load(string gameRoot)
    {
        string dir = Path.Combine(gameRoot, "gfx", "models");
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "*.glb").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        // whole models first, so animation files find the model they belong to
        foreach (string file in files.Where(f => !Path.GetFileNameWithoutExtension(f).Contains('.')))
        {
            string key = Path.GetFileNameWithoutExtension(file);
            Read(file, warnings =>
            {
                var model = Glb.Read(file, key, warnings);
                var old = Find(key);
                foreach (var clip in model.Clips.Values) KeepSpeed(clip, old);
                // a replacement body on the same skeleton keeps the animations it does not bring itself
                if (old != null && SameJoints(old.Rig, model.Rig))
                    foreach (var clip in old.Clips.Values) model.Clips.TryAdd(clip.Name, clip);
                Register(model);
                Log.Info($"Figure '{key}': {model.Mesh.TriangleCount} triangles, {model.Rig.Count} joints, clips: {string.Join(", ", model.Clips.Keys)}");
            });
        }
        RebuildOutfits();
        foreach (string file in files.Where(f => Path.GetFileNameWithoutExtension(f).Contains('.')))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string key = name[..name.IndexOf('.')];
            var model = Find(key);
            if (model == null) { Log.Warn($"No figure '{key}' to add these animations to.", file); continue; }
            Read(file, warnings =>
            {
                foreach (var clip in Glb.ReadClips(file, model.Rig, warnings))
                {
                    KeepSpeed(clip, model);
                    model.Clips[clip.Name] = clip;
                }
                Version++;
            });
        }
        RebuildOutfits();
        Version++;
    }

    private static void Read(string file, Action<List<string>> read)
    {
        var warnings = new List<string>();
        try { read(warnings); }
        catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            Log.Error($"Could not load figure file: {e.Message}", file);
        }
        foreach (string w in warnings) Log.Warn(w, file);
    }

    // exported files rarely say how fast a cycle travels: a clip replacing one of the same name moves at the same speed
    private static void KeepSpeed(Clip clip, FigureModel? old)
    {
        if (clip.Speed == 0 && old != null && old.Clips.TryGetValue(clip.Name, out var before)) clip.Speed = before.Speed;
    }

    private static bool SameJoints(Rig a, Rig b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a.Joints[i].Name, b.Joints[i].Name, StringComparison.OrdinalIgnoreCase) || a.Joints[i].Parent != b.Joints[i].Parent) return false;
        return true;
    }
}
