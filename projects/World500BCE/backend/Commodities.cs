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
        double farms = country.OwnedStates.Sum(s => Levels(s, "farm"));
        double workshops = country.OwnedStates.Sum(s => Levels(s, "workshop"));
        double infrastructure = country.OwnedStates.Sum(s => Levels(s, "infrastructure"));
        return commodity.Key switch
        {
            "timber" => workshops * 0.25 + infrastructure * 0.10,
            "charcoal" => workshops * 0.20,
            "iron_ore" => workshops * 0.18,
            "copper" => workshops * 0.08,
            "tin" => workshops * 0.04,
            "clay" => workshops * 0.10,
            "wool" => workshops * 0.12,
            "tools" => farms * 0.05 + workshops * 0.05,
            _ => 0,
        };
    }

    public override bool WillTrade(Game game, Country seller, Country buyer)
    {
        if (seller == buyer || game.World.AtWar(seller, buyer)) return false;
        return !seller.Vars.HasFlag("embargo_" + buyer.Tag);
    }

    public override double Output(Game game, Country country, State state, CommodityDef commodity, double deposit)
    {
        double infrastructure = Levels(state, "infrastructure");
        double farms = Levels(state, "farm");
        double workshops = Levels(state, "workshop");
        double infrastructureFactor = 1 + infrastructure * 0.05;
        double farmFactor = 1 + farms * 0.25;
        double workshopFactor = 1 + workshops * 0.25;
        double deposits = base.Output(game, country, state, commodity, deposit) * infrastructureFactor;
        double people = game.Population.TotalOf(state) / 1000.0 * state.ControlledShare(country);
        bool coastal = state.Locations.Any(location => location.IsCoastal);
        double local = commodity.Key switch
        {
            "grain" => people * 1.20 * infrastructureFactor * farmFactor,
            "fish" => coastal ? people * 0.30 * infrastructureFactor : 0,
            "animal_products" => people * 0.30 * farmFactor,
            "wool" => people * 0.20 * farmFactor,
            "hides" => people * 0.10 * farmFactor,
            "salt" => people * (coastal ? 0.10 : 0.02) * infrastructureFactor,
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
            "textiles" => people * (coastal ? 0.12 : 0.08) * workshopFactor * InputFactor(game, country, "wool"),
            "pottery" => people * 0.05 * workshopFactor * InputFactor(game, country, "clay", "charcoal"),
            "glass" => people * 0.01 * workshopFactor * InputFactor(game, country, "charcoal"),
            "tools" => people * 0.01 * workshopFactor * InputFactor(game, country, "iron_ore", "charcoal"),
            "bronze" => people * 0.005 * workshopFactor * InputFactor(game, country, "copper", "tin", "charcoal"),
            "iron_tools" => people * 0.004 * workshopFactor * InputFactor(game, country, "iron_ore", "charcoal"),
            "luxury_goods" => people * (coastal ? 0.015 : 0.003) * workshopFactor,
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
