// Population rules: starting people, pop growth and state capacity, assimilation, religious conversion, class promotion,
// migration, how people turn into tax and manpower (accepted cultures, faith, social classes, cores), unrest, accepting
// cultures, core gain/loss and claims, what counts as a colony, colonization (cost, range, speed, natives, settlers),
// independence (who wants to leave, suppression, the war and how it ends, decolonisation) and religious opinion.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default (see the engine's rules class for the rest).
// To change a rule, override its method here; delete the file to use the engine default again.

/// <summary>Overridable rules for population: growth, capacity, assimilation, conversion, migration, how people turn into tax and manpower, cores and colonies. Subclass and assign to <c>Game.Rules.Population</c> (the population feature puts a copy in backend/Population.cs).</summary>
public class GamePopulationRules : PopulationRules
{
}

/// <summary>Installs the population rules.</summary>
public sealed class PopulationSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Population = new GamePopulationRules();
    }
}
