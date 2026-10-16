using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Society;

/// <summary>Reads modifier blocks the four society features share (<c>modifier = { income = 0.05 ... }</c>).</summary>
public static class SocietyData
{
    public static Dictionary<string, double> ReadMods(Node? n)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (n?.Children == null) return d;
        foreach (var c in n.All)
            if (c.Value != null && Node.TryParseDouble(c.Value, out var v)) d[c.Key] = v;
        return d;
    }

    public static string Describe(Dictionary<string, double> m, double weight = 1)
        => m.Count == 0 ? "no modifiers" : string.Join(", ", m.Select(kv => $"{Localisation.GetOrPretty(kv.Key)} {kv.Value * weight:+0.##;-0.##}"));

    public static void Add(Dictionary<string, double> into, Dictionary<string, double> from, double weight)
    {
        foreach (var kv in from) into[kv.Key] = into.GetValueOrDefault(kv.Key) + kv.Value * weight;
    }
}

/// <summary>
/// Numbers of the culture, government, succession and dynasty features. Every figure reads a define first (the name is in each summary),
/// so a game tunes them in <c>common/defines.txt</c>; subclass and assign to <c>Game.Rules.Society</c> to change a formula.
/// </summary>
public class SocietyRules
{
    // ---- cultures
    /// <summary>Share of a state two cultures must each hold (culture_evolution_share, 0.30) for culture_evolution_years (50) before they become a new culture.</summary>
    public virtual double EvolutionShare(Game game) => game.Data.Defines.Get("culture_evolution_share", 0.30);
    public virtual int EvolutionMonths(Game game) => (int)(game.Data.Defines.Get("culture_evolution_years", 50) * 12);
    /// <summary>Weight of each parent's traditions in a new culture (culture_parent_weight, 0.5).</summary>
    public virtual double ParentWeight(Game game) => game.Data.Defines.Get("culture_parent_weight", 0.5);

    // ---- governments
    public virtual int MaxReforms(Game game) => (int)game.Data.Defines.Get("max_reforms", 5);
    /// <summary>Political power a reform change costs (reform_cost, 100) unless the reform names its own cost.</summary>
    public virtual double ReformCost(Game game, ReformDef r) => r.Cost > 0 ? r.Cost : game.Data.Defines.Get("reform_cost", 100);
    public virtual int ReformCooldownDays(Game game) => (int)(game.Data.Defines.Get("reform_cooldown_years", 1) * 365);
    public virtual int IdeaSlots(Game game) => (int)game.Data.Defines.Get("idea_slots", 7);
    public virtual double IdeaCost(Game game) => game.Data.Defines.Get("idea_points_per_idea", 50);
    /// <summary>Idea points a month: idea_points_per_stability (1.0) times stability/100, plus idea_points_per_research (0.1) per research point, times 1 + idea_points_gain.</summary>
    public virtual double IdeaPointsPerMonth(Game game, Country c)
    {
        var d = game.Data.Defines;
        double stab = game.Politics.Enabled ? game.Rules.Politics.StabilityOf(game, c) : 50;
        double research = game.Tech.Enabled ? game.Rules.Tech.MonthlyPoints(game, c) : 0;
        return (d.Get("idea_points_per_stability", 1.0) * stab / 100 + d.Get("idea_points_per_research", 0.1) * research) * Math.Max(0, 1 + game.Modifiers.Sum(c, "idea_points_gain"));
    }
    /// <summary>Corruption a month: corruption_per_million (0.05) per million people farther than corruption_range_km (1000) from the capital, less corruption_per_step (0.2) per centralisation step.</summary>
    public virtual double CorruptionChange(Game game, Country c, double millionsBeyondRange, double centralisationSteps)
        => game.Data.Defines.Get("corruption_per_million", 0.05) * millionsBeyondRange - game.Data.Defines.Get("corruption_per_step", 0.2) * centralisationSteps + game.Modifiers.Sum(c, "corruption_gain");
    public virtual double CorruptionRangeKm(Game game) => game.Data.Defines.Get("corruption_range_km", 1000);
    /// <summary>Income lost per point of corruption (corruption_income_per_point, 0.005).</summary>
    public virtual double CorruptionIncomePerPoint(Game game) => game.Data.Defines.Get("corruption_income_per_point", 0.005);
    public virtual double CorruptionEventAt(Game game) => game.Data.Defines.Get("corruption_event_at", 50);

    // ---- succession
    /// <summary>Age under which a ruler needs a regent (define adult_age, 16).</summary>
    public virtual int RegencyAge(Game game) => game.Data.Defines.AdultAge;
    /// <summary>Stability target lost while a regent rules (regency_stability, -3; stands for slower recovery).</summary>
    public virtual double RegencyStability(Game game) => game.Data.Defines.Get("regency_stability", -3);
    public virtual int ElectorCount(Game game) => (int)game.Data.Defines.Get("electors", 5);
    public virtual int LawChangeQuietYears(Game game) => (int)game.Data.Defines.Get("succession_law_quiet_years", 2);

    // ---- dynasties
    public virtual double PrestigePerRuler(Game game) => game.Data.Defines.Get("dynasty_prestige_per_ruler", 5);
    public virtual double PrestigePerEvent(Game game) => game.Data.Defines.Get("dynasty_prestige_per_event", 1);
    /// <summary>Prestige a year for each throne the line holds (dynasty_prestige_per_throne_year, 5).</summary>
    public virtual double PrestigePerThroneYear(Game game) => game.Data.Defines.Get("dynasty_prestige_per_throne_year", 5);
    public virtual double BastardChance(Game game) => game.Data.Defines.Get("bastard_chance", 0.05);
    public virtual double ConcubineChance(Game game) => game.Data.Defines.Get("concubine_chance", 0.10);
    public virtual double ConcubineBirthChance(Game game) => game.Data.Defines.Get("concubine_birth_chance", 0.15);
    /// <summary>Yearly chance to fall ill: disease_base (1%) plus age/disease_age_divisor (200), less with the health trait.</summary>
    public virtual double DiseaseChance(Game game, Character c)
    {
        var d = game.Data.Defines;
        double chance = d.Get("disease_base", 0.01) + c.Age(game.World.Date) / d.Get("disease_age_divisor", 200.0);
        return Math.Clamp(chance * Math.Max(0.1, 1 - c.TraitModifier("health") / 100.0), 0, 0.9);
    }
    /// <summary>Chance an ill character dies when the illness runs out (disease_death_chance, 0.30 over disease_months, 6).</summary>
    public virtual double DiseaseDeathChance(Game game, Character c, DiseaseDef d)
        => Math.Clamp((d.DeathChance > 0 ? d.DeathChance : game.Data.Defines.Get("disease_death_chance", 0.30)) * Math.Max(0.1, 1 - c.TraitModifier("health") / 100.0), 0, 1);
}
