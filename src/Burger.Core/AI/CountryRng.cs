using System.Runtime.CompilerServices;

namespace Burger.Core.AI;

/// <summary>
/// Deterministic per-country random streams, derived from <see cref="Game.Rng"/>'s seed.
/// Used by the parallel AI ticks (<see cref="AiSystem.DailyTick"/> / <see cref="AiSystem.MonthlyTick"/>):
/// <see cref="Random"/> is not thread-safe and drawing from one shared instance in parallel would both
/// race and make results depend on thread scheduling. Each country gets its own stream, so AI decisions
/// stay reproducible for a given game seed no matter how the work is scheduled.
/// </summary>
public sealed class CountryRng
{
    // SplitMix64 finalizer: mixes (seed, countryIndex) into an independent 64-bit stream seed.
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong MixK1 = 0xBF58476D1CE4E5B9UL;
    private const ulong MixK2 = 0x94D049BB133111EBUL;

    private readonly ulong _base;

    /// <summary>Builds the streams for one game from <paramref name="gameSeed"/> (the value that seeded <see cref="Game.Rng"/>).</summary>
    public CountryRng(int gameSeed)
    {
        _base = (ulong)gameSeed * Gamma;
    }

    /// <summary>A fresh, deterministic RNG for the given country index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Random For(int countryIndex)
    {
        ulong z = _base + (ulong)(uint)countryIndex * Gamma;
        z = (z ^ (z >> 30)) * MixK1;
        z = (z ^ (z >> 27)) * MixK2;
        z ^= z >> 31;
        return new Random(unchecked((int)(z ^ (z >> 32))));
    }
}
