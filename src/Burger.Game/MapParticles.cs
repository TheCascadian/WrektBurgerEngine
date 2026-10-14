using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Particles;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Shows the effects of <c>common/particles.txt</c> in the 3D map: a pool of soft billboards. Effects arrive from the figures (clip
/// events), the console and scripts (<see cref="ParticleSystem.Request"/>); sizes and speeds in the file are metres at the scale of a
/// soldier and are scaled up with the figure.
/// </summary>
public sealed class MapParticles : IDisposable
{
    private const int Max = 3000;
    private struct P { public Vector3 Pos, Vel; public float Age, Life, Drag, Gravity, S0, S1; public ParticleDef Def; }
    private readonly P[] _pool = new P[Max];
    private int _count;
    private readonly Random _rng = new(7);
    private readonly Game _game;
    private readonly MapRenderer _map;
    private Texture2D _soft;
    private bool _hasTexture;

    public MapParticles(Game game, MapRenderer map) { _game = game; _map = map; }

    public int Alive => _count;

    /// <summary>Starts an effect by name at a world point. <paramref name="forward"/> is the way "forward" effects fly; <paramref name="scale"/> is world units per metre.</summary>
    public bool Emit(string effect, Vector3 at, Vector3 forward, float scale)
    {
        var def = _game.Particles.Find(effect);
        if (def == null) return false;
        if (forward.LengthSquared() < 1e-8f) forward = Vector3.UnitY; else forward = Vector3.Normalize(forward);
        var side = Vector3.Cross(forward, Vector3.UnitY);
        side = side.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(side);
        var up2 = Vector3.Cross(side, forward);
        float spread = Math.Clamp(def.Spread, 0, 180) * MathF.PI / 180f;
        for (int i = 0; i < def.Count && _count < Max; i++)
        {
            var axis = def.Direction switch { "forward" => forward, "up" => Vector3.UnitY, _ => Vector3.UnitY };
            Vector3 dir;
            if (def.Direction == "out" || spread >= MathF.PI - 1e-3f) dir = RandomUnit();
            else
            {
                // a cone of half-angle `spread` about the axis
                float cosT = 1 - (float)_rng.NextDouble() * (1 - MathF.Cos(spread)), sinT = MathF.Sqrt(1 - cosT * cosT), phi = (float)_rng.NextDouble() * MathF.Tau;
                var a = def.Direction == "forward" ? forward : Vector3.UnitY;
                var s2 = Vector3.Cross(a, Vector3.UnitX); s2 = s2.LengthSquared() < 1e-6f ? Vector3.UnitZ : Vector3.Normalize(s2);
                var s3 = Vector3.Cross(a, s2);
                dir = a * cosT + (s2 * MathF.Cos(phi) + s3 * MathF.Sin(phi)) * sinT;
            }
            float speed = Lerp(def.SpeedMin, def.SpeedMax, (float)_rng.NextDouble()) * scale;
            float life = Lerp(def.LifeMin, def.LifeMax, (float)_rng.NextDouble());
            _pool[_count++] = new P { Pos = at, Vel = dir * speed, Life = life, Drag = def.Drag, Gravity = def.Gravity * scale, S0 = def.SizeStart * scale, S1 = def.SizeEnd * scale, Def = def };
        }
        return true;
    }

    private Vector3 RandomUnit()
    {
        float z = (float)_rng.NextDouble() * 2 - 1, a = (float)_rng.NextDouble() * MathF.Tau, r = MathF.Sqrt(1 - z * z);
        return new Vector3(r * MathF.Cos(a), z, r * MathF.Sin(a));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Takes the requests waiting in the game (console, scripts), places them in the world and moves every particle on.</summary>
    public void Update(float dt, Camera3D cam)
    {
        while (_game.Particles.TryTake(out var req))
        {
            Vector3? at = req.At switch
            {
                Location l => _map.LocationPos(l),
                Country { Capital: { } cap } => _map.LocationPos(cap),
                _ => null,
            };
            at ??= cam.Target;
            if (!Emit(req.Effect, at.Value, Vector3.UnitY, MapFigures.HeightFor(cam) / 1.8f))
                Burger.Core.Log.Warn($"particles: unknown effect '{req.Effect}'");
        }
        for (int i = 0; i < _count; i++)
        {
            ref var p = ref _pool[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _pool[i--] = _pool[--_count]; continue; }
            p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
            p.Vel.Y -= p.Gravity * dt;
            p.Pos += p.Vel * dt;
        }
    }

    /// <summary>Draws the particles; call inside the 3D mode, after the figures.</summary>
    public void Draw(Camera3D cam)
    {
        if (_count == 0) return;
        if (!_hasTexture)
        {
            var img = Raylib.GenImageGradientRadial(64, 64, 0.1f, Color.White, new Color(255, 255, 255, 0));
            _soft = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            _hasTexture = true;
        }
        Rlgl.DisableDepthMask();
        bool additive = false;
        Raylib.BeginBlendMode(BlendMode.Alpha);
        // normal particles first, then the additive ones, so light is not hidden behind smoke
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) { Raylib.EndBlendMode(); Raylib.BeginBlendMode(BlendMode.Additive); additive = true; }
            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _pool[i];
                if (p.Def.Additive != additive) continue;
                float t = p.Age / p.Life;
                var c = p.Def.ColorAt(t);
                var tint = new Color((byte)(Math.Clamp(c.X, 0, 1) * 255), (byte)(Math.Clamp(c.Y, 0, 1) * 255), (byte)(Math.Clamp(c.Z, 0, 1) * 255), (byte)(Math.Clamp(c.W, 0, 1) * 255));
                Raylib.DrawBillboard(cam, _soft, p.Pos, Lerp(p.S0, p.S1, t), tint);
            }
        }
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }

    public void Dispose()
    {
        if (_hasTexture) Raylib.UnloadTexture(_soft);
        _hasTexture = false;
    }
}
