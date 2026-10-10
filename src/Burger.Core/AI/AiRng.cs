namespace Burger.Core.AI;

/// <summary>
/// A country-scoped random stream for the AI. Passed to the per-country AI methods by
/// <see cref="AiSystem"/> during parallel ticks so no worker touches the shared, not thread-safe
/// <see cref="Game.Rng"/> (and results stay reproducible for a given game seed).
/// Falls back to <see cref="Game.Rng"/> when an override runs outside a tick (console, mods).
/// </summary>
public readonly struct AiRng
{
    private readonly Random? _rng;
    private readonly Game _game;

    internal AiRng(Game game, Random? rng) { _game = game; _rng = rng; }

    /// <summary>The RNG for this AI decision: the country's own stream inside a tick, <see cref="Game.Rng"/> otherwise.</summary>
    public Random Rng => _rng ?? _game.Rng;

    public double NextDouble() => Rng.NextDouble();
    public int Next(int maxValue) => Rng.Next(maxValue);
    public int Next() => Rng.Next();
}
