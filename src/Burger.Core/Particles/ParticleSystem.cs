using System.Globalization;
using System.Numerics;
using Burger.Core.Script;

namespace Burger.Core.Particles;

/// <summary>
/// One particle effect from <c>common/particles.txt</c>: a short burst of soft billboards. Everything is optional.
/// <code>
/// muzzle_smoke = {
///     count = 6            # particles per burst
///     life = { 0.6 1.2 }   # seconds, a random value between the two
///     speed = { 0.2 0.6 }  # metres per second (a soldier is about 1.8 m tall; the effect scales with whatever it is attached to)
///     spread = 20          # degrees of random scatter around the direction
///     direction = forward  # forward (the way the figure or gun points), up, or out (any way)
///     gravity = -0.3       # metres per second squared: positive falls, negative rises
///     drag = 1.5           # how quickly particles slow down
///     size = { 0.05 0.35 } # metres: at birth and at death
///     color = { c8c8c8c0 b0b0b080 90909000 }   # RRGGBBAA hex, spread evenly over a particle's life
///     additive = no        # yes for light (flashes, sparks), no for smoke
/// }
/// </code>
/// </summary>
public sealed class ParticleDef
{
    public string Key = "";
    public int Count = 5;
    public float LifeMin = 0.5f, LifeMax = 1f, SpeedMin = 0, SpeedMax = 1, Spread = 180, Gravity, Drag, SizeStart = 0.1f, SizeEnd = 0.1f;
    public string Direction = "out";
    public bool Additive;
    public Vector4[] Colors = { new(1, 1, 1, 1), new(1, 1, 1, 0) };

    /// <summary>The colour (0-1 per channel, with alpha) at <paramref name="t"/> from 0 (born) to 1 (gone).</summary>
    public Vector4 ColorAt(float t)
    {
        if (Colors.Length == 1) return Colors[0];
        float f = Math.Clamp(t, 0, 1) * (Colors.Length - 1);
        int i = Math.Min((int)f, Colors.Length - 2);
        return Vector4.Lerp(Colors[i], Colors[i + 1], f - i);
    }

    public static ParticleDef Read(Node n)
    {
        var d = new ParticleDef { Key = n.Key };
        d.Count = Math.Max(0, n.GetInt("count", d.Count));
        (d.LifeMin, d.LifeMax) = Range(n, "life", d.LifeMin, d.LifeMax);
        (d.SpeedMin, d.SpeedMax) = Range(n, "speed", d.SpeedMin, d.SpeedMax);
        (d.SizeStart, d.SizeEnd) = Range(n, "size", d.SizeStart, d.SizeEnd);
        d.Spread = (float)n.GetDouble("spread", d.Spread);
        d.Gravity = (float)n.GetDouble("gravity", 0);
        d.Drag = (float)n.GetDouble("drag", 0);
        d.Direction = (n.GetString("direction") ?? "out").ToLowerInvariant();
        d.Additive = n.GetBool("additive");
        var colors = n.GetList("color").Select(Hex).Where(c => c.HasValue).Select(c => c!.Value).ToArray();
        if (colors.Length > 0) d.Colors = colors;
        if (d.LifeMax < d.LifeMin) (d.LifeMin, d.LifeMax) = (d.LifeMax, d.LifeMin);
        if (d.LifeMax <= 0) d.LifeMax = d.LifeMin = 0.1f;
        return d;
    }

    // a block of one or two numbers; one number is the same for both ends
    private static (float, float) Range(Node n, string key, float a, float b)
    {
        var v = n.GetList(key).Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? (float?)x : null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return v.Count == 0 ? (a, b) : v.Count == 1 ? (v[0], v[0]) : (v[0], v[1]);
    }

    private static Vector4? Hex(string s)
    {
        s = s.Trim('"', '#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        if (s.Length == 6) v = (v << 8) | 0xff;
        return new Vector4(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }
}

/// <summary>A request to show an effect where a scope is (a location, or a country's capital) or at a point the caller knows.</summary>
public readonly record struct ParticleRequest(string Effect, IScope? At);

/// <summary>
/// The Particles: the effects of <c>common/particles.txt</c> by name, and the requests waiting to be shown. Anything can ask for an
/// effect with <see cref="Request"/>: a clip event on a figure, the console (<c>particle name</c>), or a script effect
/// (<c>spawn_particles = name</c>, shown where the scope is). The renderer in the game drains the requests each frame.
/// </summary>
public sealed class ParticleSystem
{
    private readonly Dictionary<string, ParticleDef> _defs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ParticleRequest> _requests = new();

    /// <summary>Changes whenever definitions are loaded or added.</summary>
    public int Version { get; private set; }

    public IEnumerable<ParticleDef> All => _defs.Values;
    public ParticleDef? Find(string name) => _defs.GetValueOrDefault(name);

    /// <summary>Adds or replaces an effect (from C#).</summary>
    public void Set(ParticleDef def) { _defs[def.Key] = def; Version++; }

    /// <summary>Loads the effects of the game folder's <c>common/particles</c>; effects the folder does not define keep the built-in defaults.</summary>
    public void Load(Burger.Core.Data.GameData data)
    {
        foreach (var d in Defaults()) _defs[d.Key] = d;
        foreach (var root in Burger.Core.Data.DataLoader.ParseFileOrFolder(data, "common/particles"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                _defs[n.Key] = ParticleDef.Read(n);
            }
        Version++;
    }

    /// <summary>Asks for an effect to be shown where <paramref name="at"/> is. Unknown names are ignored by the renderer with a warning in the log.</summary>
    public void Request(string effect, IScope? at = null)
    {
        if (string.IsNullOrWhiteSpace(effect)) return;
        if (_requests.Count < 256) _requests.Enqueue(new ParticleRequest(effect.Trim(), at));
    }

    public bool TryTake(out ParticleRequest request) => _requests.TryDequeue(out request);

    // the effects the engine itself uses (a mod's particles.txt replaces any of them by name)
    private static IEnumerable<ParticleDef> Defaults()
    {
        yield return new ParticleDef
        {
            Key = "muzzle_flash", Count = 4, LifeMin = 0.04f, LifeMax = 0.09f, SpeedMin = 0.5f, SpeedMax = 3f, Spread = 18, Direction = "forward",
            SizeStart = 0.16f, SizeEnd = 0.30f, Additive = true, Drag = 4,
            Colors = new[] { new Vector4(1f, 0.95f, 0.7f, 1f), new Vector4(1f, 0.6f, 0.15f, 0.7f), new Vector4(0.6f, 0.2f, 0.05f, 0f) },
        };
        yield return new ParticleDef
        {
            Key = "muzzle_smoke", Count = 6, LifeMin = 0.7f, LifeMax = 1.5f, SpeedMin = 0.3f, SpeedMax = 1.2f, Spread = 25, Direction = "forward",
            Gravity = -0.25f, Drag = 1.6f, SizeStart = 0.06f, SizeEnd = 0.38f,
            Colors = new[] { new Vector4(0.78f, 0.78f, 0.76f, 0.55f), new Vector4(0.65f, 0.65f, 0.64f, 0.35f), new Vector4(0.55f, 0.55f, 0.55f, 0f) },
        };
        yield return new ParticleDef
        {
            Key = "dust_puff", Count = 8, LifeMin = 0.6f, LifeMax = 1.2f, SpeedMin = 0.2f, SpeedMax = 1f, Spread = 180, Direction = "up",
            Gravity = -0.1f, Drag = 2, SizeStart = 0.1f, SizeEnd = 0.45f,
            Colors = new[] { new Vector4(0.62f, 0.55f, 0.42f, 0.5f), new Vector4(0.62f, 0.55f, 0.42f, 0f) },
        };
    }
}
