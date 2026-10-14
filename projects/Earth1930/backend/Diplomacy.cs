// Diplomacy rules: opinion, diplomatic relations, declaring war, calls to arms, war score and peace costs.
// The diplomatic actions themselves are data (common/diplomatic_actions) and stay moddable.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>
/// Overridable diplomacy formulas (Imperator-style by default). Subclass and assign to
/// <c>Game.Rules.Diplomacy</c> from a C# mod to change opinions, war rules, call-to-arms or peace costs.
/// Data-driven diplomatic actions live in <c>common/diplomatic_actions</c>.
/// </summary>
public class GameDiplomacyRules : DiplomacyRules
{
}

/// <summary>Installs the diplomacy rules.</summary>
public sealed class DiplomacySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Diplomacy = new GameDiplomacyRules();
    }
}
