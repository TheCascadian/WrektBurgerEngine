// 500 BCE production rules for the first Trade/Industry vertical slice.
// These are aggregate state outputs with early farms/workshops and input-sensitive artisan goods.

public class GameCommodityRules : CommodityRules
{
    private static double Levels(State state, string key) => state.Buildings.GetValueOrDefault(key);

    private static double InputFactor(Game game, Country country, params string[] keys)
    {
        double factor = 1;
        foreach (var key in keys)
            factor = Math.Min(factor, game.Commodities.BalanceOf(country, key).Satisfied);
        return Math.Clamp(0.25 + factor * 0.75, 0.25, 1);
    }

    public override double BaseDemand(Game game, Country country, CommodityDef commodity)
    {
        // (asked for every country and commodity each month: the buildings are only added up for the goods that need them)
        double Total(string key)
        {
            double sum = 0;
            foreach (var s in country.OwnedStates) sum += Levels(s, key);
            return sum;
        }
        return commodity.Key switch
        {
            "timber" => Total("workshop") * 0.25 + Total("infrastructure") * 0.10,
            "charcoal" => Total("workshop") * 0.20,
            "iron_ore" => Total("workshop") * 0.18,
            "copper" => Total("workshop") * 0.08,
            "tin" => Total("workshop") * 0.04,
            "clay" => Total("workshop") * 0.10,
            "wool" => Total("workshop") * 0.12,
            "tools" => Total("farm") * 0.05 + Total("workshop") * 0.05,
            _ => 0,
        };
    }

    public override bool WillTrade(Game game, Country seller, Country buyer)
    {
        if (seller == buyer || game.World.AtWar(seller, buyer)) return false;
        return !seller.Vars.HasFlag("embargo_" + buyer.Tag);
    }

    // the commodities the switch in Output gives a local (population-based) output; keep in step with it
    private static readonly HashSet<string> LocallyProduced = new(StringComparer.Ordinal)
    {
        "grain", "fish", "animal_products", "wool", "hides", "salt", "olive_oil", "wine", "timber", "charcoal", "clay", "stone", "copper", "tin",
        "iron_ore", "lead", "alum", "textiles", "pottery", "glass", "tools", "bronze", "iron_tools", "luxury_goods", "horses",
    };

    public override double Output(Game game, Country country, State state, CommodityDef commodity, double deposit)
    {
        if (deposit == 0 && !LocallyProduced.Contains(commodity.Key)) return 0; // nothing to extract and no local production: most states for most commodities
        double infrastructure = Levels(state, "infrastructure");
        double farms = Levels(state, "farm");
        double workshops = Levels(state, "workshop");
        double infrastructureFactor = 1 + infrastructure * 0.05;
        double farmFactor = 1 + farms * 0.25;
        double workshopFactor = 1 + workshops * 0.25;
        double controlled = state.ControlledShare(country);
        double deposits = deposit / 12.0 * controlled * OutputFactor(game, country, commodity) * infrastructureFactor; // as base.Output
        double people = game.Population.TotalOf(state) / 1000.0 * controlled;
        bool Coastal()
        {
            foreach (var location in state.Locations) if (location.IsCoastal) return true;
            return false;
        }
        double local = commodity.Key switch
        {
            "grain" => people * 1.20 * infrastructureFactor * farmFactor,
            "fish" => Coastal() ? people * 0.30 * infrastructureFactor : 0,
            "animal_products" => people * 0.30 * farmFactor,
            "wool" => people * 0.20 * farmFactor,
            "hides" => people * 0.10 * farmFactor,
            "salt" => people * (Coastal() ? 0.10 : 0.02) * infrastructureFactor,
            "olive_oil" => people * 0.08 * farmFactor,
            "wine" => people * 0.06 * farmFactor,
            "timber" => people * 0.10 * infrastructureFactor,
            "charcoal" => people * 0.08 * infrastructureFactor,
            "clay" => people * 0.20 * infrastructureFactor,
            "stone" => people * 0.20 * infrastructureFactor,
            "copper" => people * 0.03 * infrastructureFactor,
            "tin" => people * 0.015 * infrastructureFactor,
            "iron_ore" => people * 0.05 * infrastructureFactor,
            "lead" => people * 0.015 * infrastructureFactor,
            "alum" => people * 0.01 * infrastructureFactor,
            "textiles" => people * (Coastal() ? 0.12 : 0.08) * workshopFactor * InputFactor(game, country, "wool"),
            "pottery" => people * 0.05 * workshopFactor * InputFactor(game, country, "clay", "charcoal"),
            "glass" => people * 0.01 * workshopFactor * InputFactor(game, country, "charcoal"),
            "tools" => people * 0.01 * workshopFactor * InputFactor(game, country, "iron_ore", "charcoal"),
            "bronze" => people * 0.005 * workshopFactor * InputFactor(game, country, "copper", "tin", "charcoal"),
            "iron_tools" => people * 0.004 * workshopFactor * InputFactor(game, country, "iron_ore", "charcoal"),
            "luxury_goods" => people * (Coastal() ? 0.015 : 0.003) * workshopFactor,
            "horses" => people * 0.01 * farmFactor,
            _ => 0,
        };
        return deposits + local;
    }
}

public sealed class CommoditiesSetup : GameScript
{
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Commodities = new GameCommodityRules();
    }
}
