// Supply rules: which land carries supply, how far it reaches (terrain, infrastructure), the capital, local supply,
// supply hubs and railways (hub mode) and how much supply a division uses.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>
/// Overridable supply rules. Two modes come with the engine (choose one feature):
/// <list type="bullet">
/// <item><b>supply_range</b>: supply spreads from the capital and from every own, controlled location, getting weaker
/// with every step (rough terrain makes steps longer). It only travels through friendly land, so encircled armies starve.</item>
/// <item><b>supply_hubs</b>: supply comes from the capital and from supply hubs; a hub only works while railways
/// (or friendly land next to the capital) connect it to the capital. Cut the railway and the hub goes dark.</item>
/// </list>
/// Subclass and assign to <c>Game.Rules.Supply</c> (the feature puts an editable copy in backend/Supply.cs).
/// </summary>
public class GameSupplyRules : SupplyRules
{
}

/// <summary>Installs the supply rules.</summary>
public sealed class SupplySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Supply = new GameSupplyRules();
    }
}
