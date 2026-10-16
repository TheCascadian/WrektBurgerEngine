using System.Numerics;
using Burger.Core;
using Burger.Core.Figures;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// The soldiers standing on the map: one figure per stack of divisions the player can see, under its counter. It stands at
/// ease while the stack holds, walks towards the next location while it marches and runs while it retreats.
/// </summary>
public sealed class MapFigures : IDisposable
{
    /// <summary>Figures on the map (they also go when the Units button hides the counters).</summary>
    /// <summary>How much faster than authored the soldiers' drill plays while their unit is in battle.</summary>
    public const float CombatRate = 1.8f;

    public static bool Show = true;

    /// <summary>Soldiers are drawn far larger than life so they read on the map; a tank at true scale beside them would swallow a province, so it is drawn at this fraction of it (about 1.2 soldiers long).</summary>
    public const float TankMapScale = 0.42f;
    private const int MaxFigures = 400;

    /// <summary>A joke clip (<see cref="HumanMale.Gags"/>) every standing soldier plays instead of the idle, or null. Set only by the <c>soldier_gag</c> console command.</summary>
    public static string? StandingGag;
    private const float MinPixels = 12f; // smaller than this on screen a figure is only noise

    /// <summary>Where clip events (muzzle flashes) are shown; set by the app.</summary>
    public MapParticles? Particles;
    private readonly Game _game;
    private readonly MapRenderer _map;
    private readonly FigureRenderer _renderer = new();
    private int _version = -1, _frame;

    /// <summary>What is remembered about a stack between frames, so its figure does not start over when the camera moves.</summary>
    private sealed class Stand
    {
        public Animator Animator = null!;
        public string ModelKey = "";
        public float Yaw, TargetYaw;
        public Vector3 Up = Vector3.UnitY;
        public bool Placed;
        public Vector3 Pos;      // where the figure is drawn: eases towards where the stack is, so it never jumps
        public float Speed;      // smoothed ground speed in world units per second
        public bool Walking;
        public int Seen;
        public readonly List<ClipEvent> Events = new();
        public readonly Random Rng = new();
        public string? Attack;   // the melee attack being played in this fight
    }
    private readonly Dictionary<Division, Stand> _stands = new();

    public MapFigures(Game game, MapRenderer map)
    {
        _game = game;
        _map = map;
    }

    /// <summary>How tall figures are drawn, in world units: they grow as the camera rises so they stay readable, up to a limit.</summary>
    public static float HeightFor(Camera3D cam) => Math.Clamp(Viewport.HeightOf(cam) * 0.08f, 0.3f, 2f);

    /// <summary>Half the width of the patch of ground a figure of this height stands on.</summary>
    public static float StandRadius(float height) => height * 0.5f;

    /// <summary>Light armoured divisions (any light tank battalion in the template) roll out as tanks; everything else marches as soldiers.</summary>
    public static bool IsLightArmour(Division d) => d.Template.Units.Any(u => u.Type.Key == LightTank.Key);

    /// <summary>True while figures are on the map for this camera.</summary>
    public bool Visible(Camera3D cam)
    {
        if (!Show || !MapOverlay.ShowUnits || Viewport.HeightOf(cam) > _map.StrategicZoom) return false;
        // height on screen of a figure at the point the camera looks at
        float distance = Vector3.Distance(cam.Position, cam.Target);
        float pixels = HeightFor(cam) / (2 * distance * MathF.Tan(cam.FovY * MathF.PI / 360f)) * Viewport.H;
        return pixels >= MinPixels;
    }


    /// <summary>Draws the figures. Call inside BeginMode3D, after the map.</summary>
    public void Draw(Camera3D cam, float dt)
    {
        _frame++;
        if (!Visible(cam)) return;
        var figures = _game.Figures;
        if (_version != figures.Version)
        {
            // models were replaced: the uploaded meshes and the animators bound to them are stale
            _renderer.Clear();
            _stands.Clear();
            _version = figures.Version;
        }
        var human = figures.Soldier;
        float height = HeightFor(cam);
        var player = _game.World.Player;
        _renderer.Begin(cam);
        int drawn = 0;
        foreach (var g in _game.World.Divisions.GroupBy(d => (d.Location, d.Owner)))
        {
            var (loc, owner) = g.Key;
            if (loc.IsWater) continue; // troops at sea are aboard ships
            if (owner != player && !_game.IsVisible(loc)) continue; // fog of war hides other armies
            var first = g.First();
            var pos = _map.LocationPos(loc);
            if (!Viewport.TryProjectSnapped(pos, cam, out var screen)) continue;
            if (screen.X < -80 || screen.Y < -80 || screen.X > Viewport.W + 80 || screen.Y > Viewport.H + 120) continue;

            bool tank = IsLightArmour(first) && figures.Find(LightTank.Key) != null;
            var model = tank ? figures.Find(LightTank.Key)! : figures.SoldierFor(first);
            // to scale: the tank is drawn as many soldiers tall as it is in metres
            float drawHeight = tank ? height * model.Height / human.Height * TankMapScale : height;
            float radius = StandRadius(drawHeight) * (tank ? 1.5f : 1f);
            if (_stands.TryGetValue(first, out var old) && old.ModelKey != model.Key) _stands.Remove(first);
            if (!_stands.TryGetValue(first, out var stand))
            {
                stand = new Stand { Animator = figures.NewAnimator(model.Key), ModelKey = model.Key };
                { var st = stand; stand.Animator.OnEvent = ev => st.Events.Add(ev); }
                // stacks do not breathe in unison
                stand.Animator.Time = (first.Id * 0.618034f % 1f) * (stand.Animator.Current?.Duration ?? 1f);
                _stands[first] = stand;
            }
            stand.Seen = _frame;

            // a stack on the move is drawn part of the way to its next location, facing it (as its counter is)
            bool moving = first.Path.Count > 0 && first.Battle == null && first.Path[0].IsLand && g.All(d => d.Path.Count > 0 && d.Path[0] == first.Path[0]);
            if (moving)
            {
                var next = _map.LocationPos(first.Path[0]);
                var to = next - pos;
                if (to.X * to.X + to.Z * to.Z > 1e-6f) stand.TargetYaw = MathF.Atan2(to.X, to.Z);
                pos = Vector3.Lerp(pos, next, (float)Math.Clamp(first.MoveProgress, 0, 1) * 0.8f);
                pos.Y = _map.HeightAt(pos.X, pos.Z);
            }
            else if (first.Battle != null)
            {
                var enemy = first.Battle.Location == loc ? first.Battle.Attackers.FirstOrDefault()?.Location : first.Battle.Location;
                if (enemy != null && enemy != loc)
                {
                    var to = _map.LocationPos(enemy) - pos;
                    stand.TargetYaw = MathF.Atan2(to.X, to.Z);
                }
            }
            // ease towards the target instead of jumping to it (arriving at a location, the camera moving, a slow game speed)
            float perMetre = drawHeight / model.Height;
            if (!stand.Placed || Vector2.Distance(new(stand.Pos.X, stand.Pos.Z), new(pos.X, pos.Z)) > height * 6) { stand.Pos = pos; stand.Speed = 0; }
            else if (dt > 1e-4f)
            {
                var from = stand.Pos;
                float k = 1 - MathF.Exp(-dt * 6f);
                stand.Pos = new Vector3(from.X + (pos.X - from.X) * k, 0, from.Z + (pos.Z - from.Z) * k);
                float v = Vector2.Distance(new(from.X, from.Z), new(stand.Pos.X, stand.Pos.Z)) / dt;
                stand.Speed += (v - stand.Speed) * (1 - MathF.Exp(-dt * 4f));
            }
            pos = stand.Pos;
            pos.Y = _map.StandHeight(pos.X, pos.Z, radius); // feet on the highest ground under the body: never sunk into a slope
            // turn towards the heading instead of snapping, and lean with the ground under the feet
            if (!stand.Placed) { stand.Yaw = stand.TargetYaw; stand.Placed = true; }
            float turn = MathF.IEEERemainder(stand.TargetYaw - stand.Yaw, MathF.Tau);
            stand.Yaw += Math.Clamp(turn, -dt * 7f, dt * 7f);
            var up = _map.GroundNormal(pos.X, pos.Z, drawHeight * 0.6f);
            stand.Up = Vector3.Normalize(Vector3.Lerp(stand.Up, up, Math.Min(1f, dt * 8f)));
            // the gait plays at the pace the figure really travels, so slow game speeds do not make it skate or shuffle
            string gait = tank ? LightTank.Drive : first.Retreating ? HumanMale.Run : HumanMale.Walk;
            float gaitSpeed = model.Clips[gait].Speed;
            float metres = stand.Speed / perMetre;
            stand.Walking = metres > gaitSpeed * (stand.Walking ? 0.08f : 0.18f);
            if (stand.Walking)
            {
                stand.Animator.Play(gait, 0.3f);
                stand.Animator.Rate = Math.Clamp(metres / gaitSpeed, 0.1f, 1.6f);
            }
            else
            {
                // soldiers without a firearm pick one of their melee attacks at random, and another each time one has finished
                string? melee = null;
                if (!tank && first.Battle != null && Uniforms.MeleeClips(model) is { Count: > 0 } attacks)
                {
                    var cur = stand.Animator.Current;
                    bool done = cur != null && attacks.Contains(cur.Name) && stand.Animator.Time >= cur.Duration;
                    if (stand.Attack == null || !attacks.Contains(stand.Attack) || done)
                    {
                        stand.Attack = attacks[stand.Rng.Next(attacks.Count)];
                        if (done && cur!.Name == stand.Attack) stand.Animator.Time = 0;
                    }
                    melee = stand.Attack;
                }
                else stand.Attack = null;
                stand.Animator.Play(melee ?? (tank ? LightTank.Aim : first.Battle != null && model.Clips.ContainsKey(HumanMale.Fire) ? Uniforms.ClipFor(first.Owner.Tag, HumanMale.Fire, model) : StandingGag == HumanMale.Fire ? Uniforms.ClipFor(first.Owner.Tag, HumanMale.Fire, model) : StandingGag != null && model.Clips.ContainsKey(StandingGag) ? StandingGag : HumanMale.Idle), 0.4f); // standing: soldiers shift and look about, tanks sweep the turret
                // a battle does not last long: the drill plays faster than the idle's pace so a soldier gets through it before the fight is decided
                stand.Animator.Rate = !tank && first.Battle != null ? CombatRate : 1f;
            }
            stand.Events.Clear();
            stand.Animator.Advance(dt);
            var skin = stand.Animator.Skin();
            var place = FigureRenderer.Placement(model, pos, stand.Yaw, drawHeight, stand.Up);
            FigureShadows.Draw(new ShadowContext(model, skin, place, pos, stand.Up, stand.Yaw, drawHeight, radius, cam));
            _renderer.Draw(model, skin, place, Color.White);
            if (Particles != null && stand.Events.Count > 0 && model.Rig.Find("rifle") is var rj and >= 0)
            {
                var xf = skin[rj] * place; // rest-pose point of the rifle to the world
                foreach (var ev in stand.Events)
                    if (ev.Attach == HumanMale.MuzzleAttach)
                        Particles.Emit(ev.Effect, Vector3.Transform(HumanMale.MuzzleRest, xf), Vector3.Normalize(Vector3.TransformNormal(HumanMale.RifleAxis, xf)), perMetre);
            }
            if (++drawn >= MaxFigures) break;
        }
        if (_frame % 600 == 0)
            foreach (var gone in _stands.Where(kv => _frame - kv.Value.Seen > 600).Select(kv => kv.Key).ToList()) _stands.Remove(gone);
    }

    public void Dispose() => _renderer.Dispose();
}
