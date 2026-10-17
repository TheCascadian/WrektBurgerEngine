using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Supply;

/// <summary>
/// Army and fleet logistics (logistics feature): armies and fleets lose strength from the land and the sea, not only from missing supply - too many divisions in one
/// place (0.3% a day for each above the supply limit of 6, +4 for a port or the capital, + the infrastructure), hostile terrain and winter outside friendly land
/// (0.5% a day each), ships more than 30 days from a friendly port (0.2% a day), and attacks over a river (-20%, -35% over a large river, half of it with engineers).
/// The numbers are in the rules (<see cref="Warfare.WarfareRules"/>) and the defines.
/// </summary>
public sealed class LogisticsSystem
{
    private readonly Game _game;
    private readonly Dictionary<Adjacency, bool> _large = new();
    private readonly Dictionary<Fleet, int> _atSea = new();

    /// <summary>Counts for reports.</summary>
    public double StrengthLost, ShipsHpLost;
    public int Crossings;

    public LogisticsSystem(Game game) { _game = game; }

    public bool Enabled => _game.Features.Has(FeatureSet.Logistics) && _game.Features.Has(FeatureSet.Military);
    private Warfare.WarfareRules Rules => _game.Rules.Warfare;

    // ------------------------------------------------------------------------------------- land
    private bool IsFriendly(Country c, Country? other)
    {
        if (other == null || other == c) return true;
        var r = _game.World.Relations;
        return r.AreAllied(c, other) || r.AreOverlordAndSubject(c, other);
    }

    /// <summary>Is this location in land the country can count on: its own, its allies' and its overlord's or subjects'.</summary>
    public bool OnFriendlyLand(Country c, Location l) => l.IsLand && IsFriendly(c, l.Controller ?? l.Owner);

    /// <summary>Divisions a location can feed: 6 and 4 for a port or a capital, and a level for each level of infrastructure.</summary>
    public double SupplyLimit(Country owner, Location l) => Rules.SupplyLimit(_game, owner, l);

    /// <summary>Divisions of the owner standing in the location.</summary>
    public int StackAt(Country owner, Location l) => _game.World.Divisions.Count(d => d.Owner == owner && d.Location == l);

    /// <summary>Is it winter at this place (December to February north of the equator, June to August south of it).</summary>
    public bool IsWinter(Location l)
    {
        var map = _game.World.Map;
        bool north = l.CenterY < map.Height * _game.Data.Defines.Get("equator_fraction", 0.62);
        int m = _game.World.Date.Month;
        return north ? m is 12 or 1 or 2 : m is 6 or 7 or 8;
    }

    /// <summary>Strength a division loses a day, as a share of its maximum, and why.</summary>
    public (double Rate, string Why) AttritionOf(Division d, int stack)
    {
        double rate = 0;
        var why = new List<string>();
        double limit = SupplyLimit(d.Owner, d.Location);
        if (stack > limit)
        {
            double over = Rules.OverLimitAttrition(_game, d, stack - limit);
            rate += over;
            why.Add($"{over:P1} over supply limit ({stack} of {limit:0})");
        }
        if (!OnFriendlyLand(d.Owner, d.Location))
        {
            double t = Rules.TerrainAttrition(_game, d);
            if (t > 0) { rate += t; why.Add($"{t:P1} {d.Location.Terrain.Name.ToLowerInvariant()}"); }
            if (Rules.WinterAttrition(_game, d) is var w && w > 0 && IsWinter(d.Location)) { rate += w; why.Add($"{w:P1} winter"); }
        }
        return (rate, string.Join(", ", why));
    }

    /// <summary>Is a river crossing large: the river is wide where the two locations meet.</summary>
    public bool IsLargeRiver(Adjacency adj)
    {
        if (_large.TryGetValue(adj, out var v)) return v;
        var map = _game.World.Map;
        int cx = (int)adj.MidX, cy = (int)adj.MidY, count = 0;
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < map.Width && y < map.Height && map.RiverMap[y * map.Width + x] != 0) count++;
            }
        return _large[adj] = count >= (int)_game.Data.Defines.Get("large_river_pixels", 30);
    }

    /// <summary>The attack penalty of a battle for the attackers who come over a river: 20%, 35% over a large river, halved when an attacker has engineers.</summary>
    public double RiverPenalty(Battle b)
    {
        double worst = 0;
        foreach (var a in b.Attackers)
            if (a.Location.AdjacencyTo(b.Location) is { River: true } adj)
                worst = Math.Max(worst, IsLargeRiver(adj) ? _game.Data.Defines.Get("large_river_penalty", 0.35) : _game.Data.Defines.Get("river_attack_penalty", 0.2));
        if (worst > 0 && b.Attackers.Any(a => a.Stat("engineers") > 0)) worst *= 0.5;
        return worst;
    }

    /// <summary>A river crossing a division would face from one location into the next (for the map and tooltips); 0 when none.</summary>
    public double CrossingPenalty(Location from, Location to, bool engineers)
    {
        if (from.AdjacencyTo(to) is not { River: true } adj) return 0;
        double p = IsLargeRiver(adj) ? _game.Data.Defines.Get("large_river_penalty", 0.35) : _game.Data.Defines.Get("river_attack_penalty", 0.2);
        return engineers ? p * 0.5 : p;
    }

    // ------------------------------------------------------------------------------------- sea
    /// <summary>Is the fleet in a port or next to friendly coast: inside the range of a friendly port.</summary>
    public bool InFriendlyRange(Fleet f)
    {
        if (f.Location.IsLand) return OnFriendlyLand(f.Owner, f.Location);
        foreach (var adj in f.Location.Neighbors)
            if (adj.To.IsLand && OnFriendlyLand(f.Owner, adj.To) && (!_game.Navy.Enabled || _game.Navy.IsPort(f.Owner, adj.To) || adj.To.IsCoastal)) return true;
        return false;
    }

    public int DaysAtSea(Fleet f) => _atSea.GetValueOrDefault(f);

    // ------------------------------------------------------------------------------------- daily
    public void DailyTick()
    {
        if (!Enabled) return;
        var stacks = new Dictionary<(Country, Location), int>();
        foreach (var d in _game.World.Divisions) stacks[(d.Owner, d.Location)] = stacks.GetValueOrDefault((d.Owner, d.Location)) + 1;
        foreach (var d in _game.World.Divisions)
        {
            if (d.Location.IsWater) { d.Attrition = 0; d.AttritionWhy = ""; continue; } // at sea the supplies are carried
            var (rate, why) = AttritionOf(d, stacks[(d.Owner, d.Location)]);
            d.Attrition = rate;
            d.AttritionWhy = why;
            if (rate <= 0) continue;
            double lost = d.MaxStrength * rate;
            double floor = d.MaxStrength * 0.05;
            double now = Math.Max(Math.Min(d.Strength, floor), d.Strength - lost);
            StrengthLost += d.Strength - now;
            d.Strength = now;
        }
        if (!_game.Navy.Enabled) return;
        int away = (int)_game.Data.Defines.Get("fleet_away_days", 30);
        foreach (var f in _game.Navy.Fleets)
        {
            if (InFriendlyRange(f)) { _atSea[f] = 0; continue; }
            int days = _atSea.GetValueOrDefault(f) + 1;
            _atSea[f] = days;
            if (days <= away) continue;
            double loss = Rules.NavalAttrition(_game, f);
            foreach (var s in f.Ships)
            {
                double hp = Math.Max(s.Type.Hp * 0.05, s.Hp - s.Type.Hp * loss);
                ShipsHpLost += s.Hp - hp;
                s.Hp = hp;
            }
        }
        foreach (var f in _atSea.Keys.Where(f => !_game.Navy.Fleets.Contains(f)).ToList()) _atSea.Remove(f);
    }

    public void RegisterScript()
    {
        _game.Script.RegisterValue("supply_limit", (ctx, scope) => scope is Location l && Enabled && l.Owner != null ? SupplyLimit(l.Owner, l) : null);
    }
}
