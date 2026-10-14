// Warfare rules: who may enter where, movement speed, organisation recovery and division power.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>
/// Overridable movement and military rules. Subclass and assign to <c>Game.Rules.Warfare</c>.
/// (Combat itself is swapped with <see cref="ICombatModel"/>.)
/// </summary>
public class GameWarfareRules : WarfareRules
{
    /// <summary>May units of <paramref name="country"/> enter this location?</summary>
    public override bool CanEnter(Game game, Country country, Location l)
    {
        if (!l.IsPassable) return false;
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
}

/// <summary>Installs the warfare rules.</summary>
public sealed class WarfareSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Warfare = new GameWarfareRules();
    }
}
