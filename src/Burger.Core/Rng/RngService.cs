using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core;

/// <summary>
/// A random stream with one 64-bit state (SplitMix64), so its position can be saved and restored. Not thread-safe: one thread at a time.
/// </summary>
public sealed class RngStream : Random
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    public ulong State;

    public RngStream(ulong state) { State = state; }

    private ulong Next64()
    {
        ulong z = State += Gamma;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    protected override double Sample() => (Next64() >> 11) * (1.0 / 9007199254740992.0);
    public override double NextDouble() => Sample();
    public override float NextSingle() => (Next64() >> 40) * (1f / 16777216f);
    public override int Next() => (int)(Next64() >> 33);
    public override int Next(int maxValue)
    {
        if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return maxValue == 0 ? 0 : (int)(Next64() % (ulong)maxValue);
    }
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        return minValue + (int)(Next64() % (ulong)((long)maxValue - minValue == 0 ? 1 : (long)maxValue - minValue));
    }
    public override long NextInt64() => (long)(Next64() >> 1);
    public override long NextInt64(long maxValue) => maxValue <= 0 ? 0 : (long)(Next64() % (ulong)maxValue);
    public override long NextInt64(long minValue, long maxValue) => minValue + NextInt64(maxValue - minValue);
    public override void NextBytes(byte[] buffer) => NextBytes(buffer.AsSpan());
    public override void NextBytes(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i += 8)
        {
            ulong v = Next64();
            for (int j = 0; j < 8 && i + j < buffer.Length; j++) buffer[i + j] = (byte)(v >> (8 * j));
        }
    }
}

/// <summary>
/// Named, seeded random streams. Every system draws from its own stream, so a change in one system (a new draw, a different order)
/// cannot shift the dice of another. A stream's seed is a hash of the game seed and its name only, and its position is saved with the game.
/// Inside a per-country AI task every accessor returns that country's own stream (<c>ai:TAG</c>), so parallel ticks stay deterministic.
/// </summary>
public sealed class RngService
{
    private readonly Dictionary<string, RngStream> _streams = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    [ThreadStatic] private static RngStream? _ambient;

    public int Seed { get; private set; }

    public RngService(int seed) { Reset(seed); }

    /// <summary>Forgets every stream position and reseeds from <paramref name="seed"/>.</summary>
    public void Reset(int seed)
    {
        lock (_lock) { Seed = seed; _streams.Clear(); }
    }

    private static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>The stream with this exact name (created on first use).</summary>
    public RngStream Stream(string name)
    {
        lock (_lock)
        {
            if (!_streams.TryGetValue(name, out var s))
                _streams[name] = s = new RngStream(Mix(Fnv(name) ^ Mix((ulong)(uint)Seed * 0x9E3779B97F4A7C15UL)));
            return s;
        }
    }

    /// <summary>The AI stream of one country.</summary>
    public RngStream Ai(Country c) => Stream("ai:" + c.Tag);

    /// <summary>Makes <paramref name="stream"/> the stream of every accessor on this thread until the result is disposed.</summary>
    public static IDisposable Enter(RngStream stream)
    {
        var prev = _ambient;
        _ambient = stream;
        return new Scope(prev);
    }

    private sealed class Scope : IDisposable
    {
        private readonly RngStream? _prev;
        public Scope(RngStream? prev) { _prev = prev; }
        public void Dispose() => _ambient = _prev;
    }

    private Random Pick(ref RngStream? cache, string name) => _ambient ?? (cache ??= Stream(name));

    private RngStream? _combat, _events, _characters, _navy, _air, _politics, _population, _decisions, _focus, _tech, _diplomacy, _script;
    public Random Combat => Pick(ref _combat, "combat");
    public Random Events => Pick(ref _events, "events");
    public Random Characters => Pick(ref _characters, "characters");
    public Random Navy => Pick(ref _navy, "navy");
    public Random Air => Pick(ref _air, "air");
    public Random Politics => Pick(ref _politics, "politics");
    public Random Population => Pick(ref _population, "population");
    public Random Decisions => Pick(ref _decisions, "decisions");
    public Random Focus => Pick(ref _focus, "focus");
    public Random Tech => Pick(ref _tech, "tech");
    public Random Diplomacy => Pick(ref _diplomacy, "diplomacy");
    public Random Script => Pick(ref _script, "script");

    internal void ClearCache()
    {
        _combat = _events = _characters = _navy = _air = _politics = _population = _decisions = _focus = _tech = _diplomacy = _script = null;
    }

    /// <summary>Every stream's name and position, in name order (for state digests and tests).</summary>
    public List<(string Name, ulong State)> Snapshot()
    {
        lock (_lock) return _streams.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => (k.Key, k.Value.State)).ToList();
    }

    // ---- saves: the stream positions ----
    public void Write(Node root)
    {
        lock (_lock)
        {
            root.Add("seed", Seed.ToString(CultureInfo.InvariantCulture));
            foreach (var kv in _streams.OrderBy(k => k.Key, StringComparer.Ordinal))
                root.Add("s", kv.Key + " " + kv.Value.State.ToString(CultureInfo.InvariantCulture), true);
        }
    }

    public void Read(Node root)
    {
        lock (_lock)
        {
            _streams.Clear();
            ClearCache();
            if (root.Has("seed")) Seed = root.GetInt("seed", Seed);
            foreach (var n in root.GetAll("s"))
            {
                var parts = (n.Value ?? "").Split(' ', 2);
                if (parts.Length == 2 && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var st))
                    _streams[parts[0]] = new RngStream(st);
            }
        }
    }
}
