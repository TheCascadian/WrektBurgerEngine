using Burger.Core.Script;

namespace Burger.Core.Sites;

/// <summary>One step of how a tier is laid out (<c>common/sites/*.txt</c>, <c>kits = { kit = { tiers = { tier = { layers... } } } }</c>).</summary>
public sealed class SiteLayer
{
    /// <summary><c>place</c> (one piece at an offset), <c>ring</c> (a wall of pieces round the site, with gates), <c>street</c> (paths from the center to the gates) or <c>scatter</c> (pieces dropped without overlap).</summary>
    public string Kind = "";
    /// <summary>Pieces with weights (scatter), or one piece (the others).</summary>
    public List<(string Piece, double Weight)> Pieces = new();
    /// <summary>Count of scattered pieces: <c>count + count_per_score * score</c>.</summary>
    public double Count, CountPerScore;
    /// <summary>Metres from the center: scatter band, ring radius, street end.</summary>
    public float Inner, Outer, Radius;
    /// <summary>Metres: <c>place</c> offset and heading (degrees).</summary>
    public float X, Z, Yaw;
    /// <summary>Ring: the gate piece, how many gates, the first gate's angle in degrees.</summary>
    public string Gate = "";
    public int Gates = 1;
    public float GateAngle;
    /// <summary>Metres between ring pieces (the piece length).</summary>
    public float Spacing = 8;
    /// <summary>Scatter heading: <c>center</c> (doors to the middle), <c>random</c> or <c>radial</c> (facing out).</summary>
    public string Face = "center";
    /// <summary>The layer only applies from this score.</summary>
    public double MinScore;
}

/// <summary>A size step of a site: used from <see cref="MinScore"/> upwards.</summary>
public sealed class SiteTier
{
    public string Key = "";
    public double MinScore;
    public List<SiteLayer> Layers = new();
}

/// <summary>The buildings of one people at one time: which culture groups use it and how each tier is laid out. Pieces are the models <c>site_&lt;piece&gt;_&lt;kit&gt;</c>.</summary>
public sealed class SiteKit
{
    public string Key = "";
    public string Name = "";
    /// <summary>Where the design comes from, and what is open.</summary>
    public string Source = "";
    public HashSet<string> CultureGroups = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Ordered by <see cref="SiteTier.MinScore"/>.</summary>
    public List<SiteTier> Tiers = new();

    public string ModelKey(string piece) => $"site_{piece}_{Key}";

    public SiteTier? TierFor(double score)
    {
        SiteTier? best = null;
        foreach (var t in Tiers) if (score >= t.MinScore) best = t;
        return best;
    }

    public static SiteKit Parse(Node n)
    {
        var kit = new SiteKit { Key = n.Key, Name = n.GetString("name") ?? n.Key, Source = n.GetString("source") ?? "" };
        foreach (var g in n.Get("culture_groups")?.All ?? Enumerable.Empty<Node>()) kit.CultureGroups.Add(g.Value ?? g.Key);
        foreach (var t in n.Get("tiers")?.All ?? Enumerable.Empty<Node>())
        {
            if (t.Children == null) continue;
            var tier = new SiteTier { Key = t.Key, MinScore = t.GetDouble("min_score") };
            foreach (var l in t.GetAll("layer"))
            {
                var layer = new SiteLayer
                {
                    Kind = l.GetString("kind", "place")!, Count = l.GetDouble("count"), CountPerScore = l.GetDouble("count_per_score"),
                    Inner = (float)l.GetDouble("inner"), Outer = (float)l.GetDouble("outer", 30), Radius = (float)l.GetDouble("radius", 30),
                    X = (float)l.GetDouble("x"), Z = (float)l.GetDouble("z"), Yaw = (float)l.GetDouble("yaw"),
                    Gate = l.GetString("gate", "")!, Gates = Math.Max(0, l.GetInt("gates", 1)), GateAngle = (float)l.GetDouble("gate_angle"),
                    Spacing = (float)l.GetDouble("spacing", 8), Face = l.GetString("face", "center")!, MinScore = l.GetDouble("min_score"),
                };
                if (l.GetString("piece") is { } one) layer.Pieces.Add((one, 1));
                foreach (var p in l.Get("pieces")?.All ?? Enumerable.Empty<Node>())
                    layer.Pieces.Add((p.Key, p.Value != null ? p.AsDouble(1) : 1));
                tier.Layers.Add(layer);
            }
            kit.Tiers.Add(tier);
        }
        kit.Tiers.Sort((a, b) => a.MinScore.CompareTo(b.MinScore));
        return kit;
    }
}

/// <summary>A place that gets a 3D model: a location, how big it is next to the rest of its state, and which kit it uses (default: the one of the state's culture group).</summary>
public sealed class SiteDef
{
    public string Key = "";
    public string Name = "";
    public string LocationKey = "";
    /// <summary>Score = the state's urbanization times this: a capital counts more than a village.</summary>
    public double Weight = 1;
    public string? Kit;
}
