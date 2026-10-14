// Commodity rules: monthly output of a state, what a country needs besides its military lines, who sells to whom and how
// a shortage slows production.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

public class GameCommodityRules : CommodityRules
{
}

/// <summary>Installs these rules.</summary>
public sealed class CommoditiesSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Commodities = new GameCommodityRules();
    }
}
