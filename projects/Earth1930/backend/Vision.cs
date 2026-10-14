// Fog of war: which locations each country can see (its land, allies, and around its armies).
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>
/// Fog of war: which locations a country can see. Replace with a subclass from C# (Game.Rules.Vision = new MyVision())
/// to change how far armies see, add spies, reveal the whole map during peace...
/// </summary>
public class GameVisionRules : VisionRules
{
}

/// <summary>Installs the vision rules.</summary>
public sealed class VisionSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Vision = new GameVisionRules();
    }
}
