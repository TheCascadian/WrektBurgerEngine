using Burger.Core.Figures;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Sites;

/// <summary>A site on the map: where, which kit and tier it shows now, and its baked model.</summary>
public sealed class Site
{
    public SiteDef Def = null!;
    public Location Location = null!;
    public SiteKit Kit = null!;
    public SiteTier Tier = null!;
    public double Score;
    public string Signature = "";
    public FigureModel? Model;
    public List<SitePlacement> Placements = new();
    /// <summary>Radius in metres that the layout reaches.</summary>
    public float Extent;
}

/// <summary>
/// The 3D models of the largest places (<c>common/sites/*.txt</c>): each site's score (its state's urbanization times its weight) picks a tier of the kit
/// of its state's culture group; the tier lays pieces out from the site's seed and the layout is baked into one model. Layouts are rebuilt only when the tier or
/// the quantized score changes, or when the piece models change. See docs/game/SITES.md.
/// </summary>
public sealed class SiteSystem
{
    /// <summary>Score steps: layouts change when the score crosses one.</summary>
    public const double ScoreStep = 0.01;

    private readonly Game _game;
    public readonly Dictionary<string, SiteKit> Kits = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<Site> Sites = new();
    private readonly Dictionary<string, float> _radius = new();
    private int _figureVersion = -1;
    private int _day = int.MinValue;

    public SiteSystem(Game game) => _game = game;

    /// <summary>Reads <c>common/sites/*.txt</c> of a game folder: <c>kits = { ... }</c> and <c>sites = { ... }</c>.</summary>
    public static (Dictionary<string, SiteKit> Kits, List<SiteDef> Sites) Read(string gameRoot)
    {
        var kits = new Dictionary<string, SiteKit>(StringComparer.OrdinalIgnoreCase);
        var defs = new List<SiteDef>();
        string dir = Path.Combine(gameRoot, "common", "sites");
        if (!Directory.Exists(dir)) return (kits, defs);
        foreach (string file in Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var root = ScriptParser.ParseFile(file, Path.GetRelativePath(gameRoot, file));
            foreach (var block in root.GetAll("kits"))
                foreach (var n in block.All.Where(n => n.Children != null)) kits[n.Key] = SiteKit.Parse(n);
            foreach (var block in root.GetAll("sites"))
                foreach (var n in block.All.Where(n => n.Children != null))
                    defs.Add(new SiteDef { Key = n.Key, Name = n.GetString("name") ?? n.Key, LocationKey = n.GetString("location") ?? "", Weight = n.GetDouble("weight", 1), Kit = n.GetString("kit") });
        }
        return (kits, defs);
    }

    /// <summary>Loads the kits and sites of the game folder (after the world, so the locations exist).</summary>
    public void Load(string gameRoot)
    {
        Kits.Clear(); Sites.Clear();
        var (kits, defs) = Read(gameRoot);
        foreach (var kv in kits) Kits[kv.Key] = kv.Value;
        foreach (var d in defs)
        {
            var loc = _game.World.GetLocation(d.LocationKey);
            if (loc == null) { Log.Warn($"Site '{d.Key}': unknown location '{d.LocationKey}'"); continue; }
            Sites.Add(new Site { Def = d, Location = loc });
            Log.Info($"Site '{d.Key}': {loc.Key} at map pixel {loc.CenterX:F0},{loc.CenterY:F0} (state {loc.State?.Key})");
        }
    }

    /// <summary>The kit for a state: by its dominant pop culture's group (null when none matches).</summary>
    public SiteKit? KitFor(State? state)
    {
        if (state == null || !_game.Population.Enabled) return null;
        string? group = _game.Population.Of(state).Pops.GroupBy(p => p.Culture).OrderByDescending(g => g.Sum(p => p.Size)).Select(g => _game.Population.Culture(g.Key)?.Group.Key).FirstOrDefault();
        return group == null ? null : Kits.Values.FirstOrDefault(k => k.CultureGroups.Contains(group));
    }

    /// <summary>The score of a site now: urbanization of its state (1 for a game without the development feature) times the site's weight.</summary>
    public double ScoreOf(Site site)
        => (_game.Development.Enabled && site.Location.State is { } s ? _game.Development.Urbanization(s) : 0.1) * site.Def.Weight;

    /// <summary>Brings the sites up to date: call every frame, it only works when the day (monthly) or the piece models changed.
    /// <paramref name="ground"/> gives the terrain height in metres at a map position relative to a location (null: flat).</summary>
    public void Refresh(Func<Location, float, float, float>? ground = null)
    {
        int day = _game.World.Date.TotalDays;
        bool figures = _figureVersion != _game.Figures.Version;
        if (!figures && day / 30 == _day / 30 && _day != int.MinValue) return;
        _day = day; _figureVersion = _game.Figures.Version;
        if (figures) _radius.Clear();
        foreach (var site in Sites)
        {
            var kit = (site.Def.Kit != null && Kits.TryGetValue(site.Def.Kit, out var forced) ? forced : null) ?? KitFor(site.Location.State);
            if (kit == null) { site.Model = null; continue; }
            double score = Math.Round(ScoreOf(site) / ScoreStep) * ScoreStep;
            var tier = kit.TierFor(score);
            if (tier == null) { site.Model = null; continue; }
            string signature = $"{kit.Key}/{tier.Key}/{score:F2}/{_figureVersion}";
            if (signature == site.Signature && site.Model != null) continue;
            site.Kit = kit; site.Tier = tier; site.Score = score; site.Signature = signature;
            site.Placements = SiteLayout.Build(kit, tier, score, Seed(site.Def.Key), RadiusOf);
            var loc = site.Location;
            site.Model = site.Placements.Count == 0 ? null
                : SiteLayout.Bake("site:" + site.Def.Key, site.Placements, k => _game.Figures.Find(kit.ModelKey(k)), ground == null ? null : (x, z) => ground(loc, x, z));
            site.Extent = site.Placements.Count == 0 ? 0 : site.Placements.Max(p => MathF.Sqrt(p.X * p.X + p.Z * p.Z));
        }
    }

    private float RadiusOf(string modelKey)
    {
        if (_radius.TryGetValue(modelKey, out float r)) return r;
        var m = _game.Figures.Find(modelKey);
        return _radius[modelKey] = m == null ? 0 : SiteLayout.RadiusOf(m);
    }

    /// <summary>A stable seed from a key (string.GetHashCode differs between runs).</summary>
    public static int Seed(string key)
    {
        uint h = 2166136261;
        foreach (char c in key) h = (h ^ c) * 16777619;
        return (int)(h & 0x7fffffff);
    }
}
