using Burger.Core.Model;

namespace Burger.Core.Warfare;

/// <summary>
/// Overridable movement and military rules. Subclass and assign to <c>Game.Rules.Warfare</c>.
/// (Combat itself is swapped with <see cref="ICombatModel"/>.)
/// </summary>
public class WarfareRules
{
    /// <summary>May units of <paramref name="country"/> enter this location?</summary>
    public virtual bool CanEnter(Game game, Country country, Location l)
    {
        if (!l.IsPassable) return false;
        if (l.State?.Demilitarized == true) return false; // no armed forces in a demilitarized zone
        var ctrl = l.Controller ?? l.Owner;
        if (ctrl == null || ctrl == country) return true;
        var w = game.World;
        var r = w.Relations;
        if (r.AreAllied(country, ctrl)) return true;
        if (r.AreOverlordAndSubject(country, ctrl)) return true;
        if (r.HasAccess(country, ctrl)) return true;
        foreach (var war in w.Wars)
        {
            if (!war.Involves(country) || !war.Involves(ctrl)) continue;
            return true; // enemies (attack) or co-belligerents (move through)
        }
        return false;
    }

    /// <summary>Hours a division needs to cross from one location to the next.</summary>
    public virtual double MoveHours(Game game, Division d, Adjacency adj)
    {
        double speed = Math.Max(0.5, d.Stat("speed", 4));
        double h = adj.DistanceKm / (speed * Math.Max(0.1, game.Modifiers.Factor(d.Owner, "speed"))) * Math.Max(0.1, adj.To.Terrain.MovementCost);
        if (adj.River) h *= 1.25;
        if (adj.Kind == AdjacencyKind.Strait) h *= 2;
        if (d.Retreating) h *= 0.75;
        return Math.Max(1, h);
    }

    /// <summary>Daily organisation recovery for a division not in battle.</summary>
    public virtual double OrgRecovery(Game game, Division d)
    {
        double r = game.ScriptValues.TryEval("division_org_recovery", d.Owner, out var scripted)
            ? d.MaxOrg * scripted
            : d.MaxOrg * game.Data.Defines.OrgRecoveryRate * d.Stat("org_recovery", 1) * game.Modifiers.Factor(d.Owner, "org_recovery");
        if (d.IsMoving) r *= 0.5;
        return r;
    }

    /// <summary>
    /// Multiplier on a division's combat power (set every hour before combat): supply, and anything features add
    /// through <see cref="Game"/> (air superiority, landing from the sea...).
    /// </summary>
    public virtual double CombatFactor(Game game, Division d)
    {
        double f = 1;
        if (game.Features.HasSupply) f *= 0.5 + 0.5 * d.Supply;
        foreach (var extra in game.Warfare.CombatFactorSources) f *= extra(d);
        return Math.Max(0.05, f);
    }

    /// <summary>Rough combat power of a division, used by the AI and front planning.</summary>
    public virtual double DivisionPower(Game game, Division d)
    {
        double s = d.Stat("soft_attack") + d.Stat("hard_attack") + d.Stat("defense") * 0.5 + d.Stat("breakthrough") * 0.5
                   + 10 * (d.Stat("fire") + d.Stat("shock") + d.Stat("offense") + d.Stat("defensive"));
        if (s <= 0) s = d.MaxStrength;
        return s * (0.4 * d.StrengthFraction + 0.6 * d.OrgFraction + 0.05);
    }

    /// <summary>Divisions a place can feed (logistics feature): 6, +4 for a port or the capital, + the infrastructure of its state.</summary>
    public virtual double SupplyLimit(Game game, Country owner, Location l)
    {
        var d = game.Data.Defines;
        double limit = d.Get("supply_limit_base", 6);
        bool hub = owner.Capital == l || (l.IsCoastal && (!game.Navy.Enabled || game.Navy.IsPort(owner, l)));
        if (hub) limit += d.Get("supply_limit_hub", 4);
        limit += l.State?.Working("infrastructure") ?? 0;
        return limit;
    }

    /// <summary>Daily strength share lost for each division above the limit, at most 3%.</summary>
    public virtual double OverLimitAttrition(Game game, Division d, double over)
        => Math.Min(game.Data.Defines.Get("supply_attrition_max", 0.03), over * game.Data.Defines.Get("supply_attrition", 0.003));

    /// <summary>Daily strength share lost to hostile terrain (the terrain's attrition) outside friendly land.</summary>
    public virtual double TerrainAttrition(Game game, Division d)
    {
        var t = d.Location.Terrain;
        if (t.Modifiers.TryGetValue("attrition", out var v)) return v;
        return t.Name.ToLowerInvariant() switch { "desert" or "jungle" or "tundra" or "mountains" or "mountain" => 0.005, _ => 0 };
    }

    /// <summary>Daily strength share lost in winter outside friendly land.</summary>
    public virtual double WinterAttrition(Game game, Division d) => game.Data.Defines.Get("winter_attrition", 0.005);

    /// <summary>Daily share of a ship's hit points lost when the fleet is far from any friendly port.</summary>
    public virtual double NavalAttrition(Game game, Fleet fleet) => game.Data.Defines.Get("naval_attrition", 0.002);
}
