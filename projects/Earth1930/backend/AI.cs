// AI: how computer-controlled countries manage armies and fronts, recruit, use diplomacy and make peace.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It overrides only what Earth1930 does differently from the engine default; the AI itself is the engine's
// (AiSystem). To change a part of it, override that method in GameAi; delete the file to use the engine default again.

/// <summary>Earth1930's AI: the engine default. Override any virtual method of <see cref="AiSystem"/> here.</summary>
public class GameAi : AiSystem
{
    public GameAi(Game game) : base(game) { }
}

/// <summary>Installs the AI.</summary>
public sealed class AiSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        // Earth1930's custom game-state mutations are not yet safe for parallel country AI.
        // Keep this project deterministic and profileable until those shared collections are isolated.
        AiSystem.Parallelism = 1;
        Game.Ai = new GameAi(Game);
    }
}
