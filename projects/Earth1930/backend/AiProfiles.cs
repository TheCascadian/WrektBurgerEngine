// AI profile rules: the generic archetype of a country, how much its history counts (the historical bias of the game), how readily
// it picks fights (readiness) and what its rivals, friends and ambitions add to the desire for each diplomatic action.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

public class GameAiProfileRules : AiProfileRules
{
}

/// <summary>Installs these rules.</summary>
public sealed class AiProfilesSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.AiProfile = new GameAiProfileRules();
    }
}
