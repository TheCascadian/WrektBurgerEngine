using System.Numerics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Paradox-style map camera: WASD/arrows/middle-drag to pan, mouse wheel to zoom towards the cursor.
/// Input only moves the logical target (Target/Distance); the rendered camera position and rotation are
/// linearly interpolated (damped) towards it every frame in Update(), which must be called once per frame
/// BEFORE BeginDrawing(). This decoupling removes the jitter/shakiness caused by moving the camera while
/// the frame is being drawn.
/// </summary>
public sealed class CameraController
{
    private readonly MapRenderer _map;
    /// <summary>Logical focus point the camera damps towards (input writes this; CurrentPos follows via Lerp).</summary>
    public Vector3 Target;
    /// <summary>Logical zoom distance the camera damps towards (input writes this; CurrentDistance follows via Lerp).</summary>
    public float Distance;
    public float MinDistance = 5f;
    public float MaxDistance;
    /// <summary>Damping strength per frame: currentPos += (targetPos - currentPos) * SmoothFactor. Default 0.15f.</summary>
    public float SmoothFactor = 0.15f;
    private Vector2? _dragAnchor; // screen-space cursor position while middle-dragging
    private Vector3 _dragTarget;  // logical Target captured at drag start / last re-anchor

    /// <summary>True while the camera moved this frame (input or damping). Hover picking can use its cheap path.</summary>
    public bool IsMoving { get; private set; }
    private Vector3 _lastCamPos;
    private Vector3 _lastCamTarget;
    private float _lastCamDist = -1f;

    // Internal smoothed state (the "current" side of the Lerp). Read-only from outside.
    private Vector3 _currentPos;
    private Vector3 _currentTarget;
    private Vector3 _currentUp = Vector3.UnitY;
    private float _currentDistance;
    private bool _smoothed;

    public Camera3D Camera;

    /// <summary>True while the map is drawn as a globe. The logical Target stays a point of the flat map (x east-west, z north-south) in both modes.</summary>
    public bool Globe { get; private set; }
    private float _tiltRef;
    private float _flatMax;
    private int _fitW = -1, _fitH = -1;
    // Globe view is a free trackball: where the camera looks (unit vector from the globe's center) and which way is up on screen.
    // No pole is special, so panning over a pole just keeps turning the globe under the cursor.
    private Vector3 _gFocus = Vector3.UnitZ, _gUp = Vector3.UnitY;

    public CameraController(MapRenderer map)
    {
        _map = map;
        Target = new Vector3(map.WorldWidth / 2, 0, map.WorldHeight / 2);
        SetGlobe(UserSettings.GlobeOn, true);
        Distance = MaxDistance * 0.6f;
        ApplyImmediate(); // snap so the first frame is already at the requested view
    }

    /// <summary>Switches between the flat (east-west wrapping) map and the globe, keeping what the camera looks at.</summary>
    public void SetGlobe(bool globe, bool initial = false)
    {
        Globe = globe;
        _map.Globe = globe;
        if (globe) GlobeFromTarget();
        Viewport.GlobeRadius = globe ? _map.GlobeRadius : 0f;
        _fitW = -1;
        RecomputeFit();
        if (!initial) { ClampLogical(); ApplyImmediate(); }
    }

    /// <summary>
    /// Zooming out stops where the screen shows the whole height of the map (never past the map image); on the globe, where the whole
    /// globe is in view. Tilt uses a fixed reference distance so it does not depend on the window size.
    /// </summary>
    private void RecomputeFit()
    {
        int w = Viewport.W, h = Viewport.H;
        if (w == _fitW && h == _fitH) return;
        _fitW = w; _fitH = h;
        _tiltRef = Math.Max(_map.WorldWidth, _map.WorldHeight) * 1.05f;
        float lo = MinDistance, hi = _tiltRef * 2;
        var center = new Vector3(_map.WorldWidth / 2, 0, _map.WorldHeight / 2);
        for (int i = 0; i < 28; i++)
        {
            float mid = (lo + hi) / 2;
            bool fits = !Footprint(PoseFlat(center, mid), out float minZ, out float maxZ) || maxZ - minZ <= _map.WorldHeight;
            if (fits) lo = mid; else hi = mid;
        }
        _flatMax = lo;
        MaxDistance = Globe ? _map.GlobeRadius * 3f : _flatMax;
        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
    }

    /// <summary>Where the corners of the screen land on the ground (the top corners are the far ones). False if a corner looks past the horizon.</summary>
    private static bool Footprint(Camera3D cam, out float minZ, out float maxZ)
    {
        minZ = float.MaxValue; maxZ = float.MinValue;
        int w = Viewport.W, h = Viewport.H;
        foreach (var c in new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(0, h), new Vector2(w, h) })
        {
            var p = Viewport.ScreenToPlane(c, cam, cam.Target.Y);
            if (p == null) return false;
            minZ = Math.Min(minZ, p.Value.Z);
            maxZ = Math.Max(maxZ, p.Value.Z);
        }
        return true;
    }

    /// <summary>Moves a map point (flat map units) or a scene position into the camera: on the globe the scene position is converted back to the map.</summary>
    public void LookAt(Vector3 p, float? distance = null)
    {
        if (Globe) { var (x, z) = _map.FlatOf(p); LookAtFlat(x, z, distance); }
        else LookAtFlat(p.X, p.Z, distance);
    }

    /// <summary>Look at a point of the flat map (map units).</summary>
    public void LookAtFlat(float wx, float wz, float? distance = null)
    {
        Target = new Vector3(wx, 0, wz);
        if (Globe) GlobeFromTarget();
        if (distance != null) Distance = Math.Clamp(distance.Value, MinDistance, MaxDistance);
        ClampLogical();
        ApplyImmediate(); // programmatic jumps (selection, saves) move instantly, no damping
    }

    /// <summary>
    /// Advances input handling and damped camera motion. Call once per frame before Raylib.BeginDrawing();
    /// never mutate the camera from inside the draw loop.
    /// </summary>
    public void Update(float dt, bool mouseOverUi, bool wheelUsed, bool keyboardFree)
    {
        if (Globe != UserSettings.GlobeOn) SetGlobe(UserSettings.GlobeOn);
        RecomputeFit();

        // ------------------------------------------------ input modifies only the logical target/distance
        // on the globe a step is an arc along the surface: east-west steps widen towards the poles (the map is stretched there)
        float speed = Distance * (Globe ? 0.3f : 1.2f) * dt;
        if (keyboardFree)
        {
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) speed *= 2.5f;
            float up = 0, right = 0;
            if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) up += speed;
            if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) up -= speed;
            if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) right -= speed;
            if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) right += speed;
            if (Globe) MoveGlobe(right / _map.GlobeRadius, up / _map.GlobeRadius); // an arc along the surface, the same everywhere
            else { Target.Z -= up; Target.X += right; }
            if (Raylib.IsKeyDown(KeyboardKey.PageUp) || Raylib.IsKeyDown(KeyboardKey.Q)) Distance *= 1 - dt * 1.5f;
            if (Raylib.IsKeyDown(KeyboardKey.PageDown) || Raylib.IsKeyDown(KeyboardKey.E)) Distance *= 1 + dt * 1.5f;
        }
        _map.ViewCenterX = Globe ? Target.X : Camera.Target.X;

        var mouse = Viewport.Mouse;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && !mouseOverUi && !wheelUsed)
        {
            // Keep the world point under the cursor attached to the cursor while zooming.
            // IMPORTANT: all projections use the DESIRED (logical) camera, never the damped one.
            // The damped camera lags behind the logical pose, so anchoring against it makes each
            // frame's correction compound onto the previous lag - the focus point visibly jumps
            // around the map instead of staying pinned to the mouse. Anchoring against the logical
            // pose is self-consistent: damping only smooths the visual travel, never the anchor math.
            var desiredNow = BuildDesiredCamera(out _);
            var before = Globe ? _map.GlobeDir(mouse, desiredNow) : _map.GroundPoint(mouse, desiredNow);
            Distance = Math.Clamp(Distance * MathF.Pow(0.85f, wheel), MinDistance, MaxDistance);
            if (Globe)
            {
                var after = _map.GlobeDir(mouse, BuildDesiredCamera(out _));
                if (before != null && after != null) TurnGlobe(before.Value, after.Value);
            }
            else if (before != null)
            {
                var after = _map.GroundPoint(mouse, BuildDesiredCamera(out _)); // camera at the new logical zoom
                if (after != null) Target += before.Value - after.Value; // zoom towards cursor
            }
        }

        // Middle mouse drag: pan by tracking the cursor in SCREEN space against the logical camera.
        // Re-projecting through a moving camera (ground-point delta) feeds back into itself every
        // frame and causes the jumping described above; screen-space deltas are pure input and stable.
        if (Raylib.IsMouseButtonPressed(MouseButton.Middle) && !mouseOverUi)
        {
            _dragAnchor = mouse; // screen-space anchor
            _dragTarget = Target; // logical target at drag start
        }
        if (Raylib.IsMouseButtonReleased(MouseButton.Middle)) _dragAnchor = null;
        if (_dragAnchor != null && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            Vector2 delta = mouse - _dragAnchor.Value;
            if (delta != Vector2.Zero && Globe)
            {
                // the globe turns under the cursor: the point that was under it must be under it again
                var cam = BuildDesiredCamera(out _);
                var h0 = _map.GlobeDir(_dragAnchor.Value, cam);
                var h1 = _map.GlobeDir(mouse, cam);
                if (h0 != null && h1 != null)
                {
                    TurnGlobe(h0.Value, h1.Value);
                    _dragAnchor = mouse;
                }
            }
            else if (delta != Vector2.Zero)
            {
                var cam = BuildDesiredCamera(out _);
                // project the drag-start focus point, offset it by the cursor movement, and unproject
                // onto the same y-plane the focus sits on (terrain height is sampled once at capture,
                // so the math can't feed back into itself as the camera moves)
                var s0 = Viewport.ToScreen(_dragTarget, cam);
                var p1 = Viewport.ScreenToPlane(new Vector2(s0.X - delta.X, s0.Y - delta.Y), cam, _dragTarget.Y);
                if (p1 != null)
                {
                    Target = new Vector3(p1.Value.X, 0, p1.Value.Z);
                    _dragAnchor = mouse; // re-anchor each frame so drift can't accumulate
                    _dragTarget = Target;
                }
            }
        }

        if (Globe && _dragAnchor == null) AlignNorth(dt);
        ClampLogical();

        // ------------------------------------------------ damped follow: Lerp position + rotation (view target)
        // frame-rate independent smoothing derived from the per-frame SmoothFactor
        float f = 1f - MathF.Pow(1f - Math.Clamp(SmoothFactor, 0.001f, 1f), Math.Clamp(dt * 60f, 0f, 240f));
        var desired = BuildDesiredCamera(out float desiredDist);
        if (!_smoothed)
        {
            _currentPos = desired.Position;
            _currentTarget = desired.Target;
            _currentUp = desired.Up;
            _currentDistance = desiredDist;
            _smoothed = true;
        }
        else
        {
            _currentPos += (desired.Position - _currentPos) * f;   // currentPos += (targetPos - currentPos) * smoothFactor
            _currentTarget += (desired.Target - _currentTarget) * f; // rotation (up-vector direction) follows the target
            _currentUp = Vector3.Normalize(_currentUp + (desired.Up - _currentUp) * f);
            _currentDistance += (desiredDist - _currentDistance) * f;
        }
        Camera = BuildCamera(_currentPos, _currentTarget, _currentUp);
        _map.ViewCenterX = Globe ? Target.X : Camera.Target.X;

        // Track whether the rendered pose actually changed this frame. While it does, hover picking
        // runs on the cheap plane path (see MapRenderer.PickFast): a full heightfield ray-march every
        // frame during fast panning is what tanks the frame time, and its result would be stale anyway.
        float tol = Math.Max(0.01f, _currentDistance * 1e-4f);
        IsMoving = (Camera.Position - _lastCamPos).LengthSquared() > tol * tol
                || (Camera.Target - _lastCamTarget).LengthSquared() > tol * tol
                || Math.Abs(_currentDistance - _lastCamDist) > tol;
        _lastCamPos = Camera.Position;
        _lastCamTarget = Camera.Target;
        _lastCamDist = _currentDistance;
    }

    /// <summary>Keeps zoom and pan inside the map image: the view never shows past the north or south edge, east-west the map wraps.</summary>
    private void ClampLogical()
    {
        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
        if (Globe) { var (gx, gz) = _map.FlatOf(_gFocus); Target = new Vector3(gx, 0, gz); return; }
        Target.Z = Math.Clamp(Target.Z, 0, _map.WorldHeight);
        if (!Globe)
        {
            // pull the view back inside the map until all four screen corners are on it (the camera tilts, so this is the footprint, not the focus)
            for (int i = 0; i < 4; i++)
            {
                if (!Footprint(PoseFlat(Target, Distance), out float minZ, out float maxZ)) break;
                float dz = 0;
                if (maxZ - minZ >= _map.WorldHeight) dz = _map.WorldHeight / 2 - (minZ + maxZ) / 2;
                else if (minZ < 0) dz = -minZ;
                else if (maxZ > _map.WorldHeight) dz = _map.WorldHeight - maxZ;
                if (MathF.Abs(dz) < 1e-3f) break;
                Target.Z += dz;
            }
        }
        // east-west the map wraps: keep the focus on the first copy. On the flat map the camera's smoothed pose moves with it so nothing swings across the map.
        float wrapped = Target.X - _map.WorldWidth * MathF.Floor(Target.X / _map.WorldWidth);
        float shift = wrapped - Target.X;
        if (shift != 0)
        {
            Target.X = wrapped;
            _dragTarget.X += shift;
            if (!Globe)
            {
                _currentPos.X += shift; _currentTarget.X += shift;
                _lastCamPos.X += shift; _lastCamTarget.X += shift;
                Camera.Position.X += shift; Camera.Target.X += shift;
            }
        }
    }

    /// <summary>Builds the desired camera pose from the logical Target/Distance (no smoothing applied).</summary>
    private Camera3D BuildDesiredCamera(out float distance)
    {
        distance = Distance;
        return Pose(Target, Distance);
    }

    private Camera3D Pose(Vector3 target, float distance) => Globe ? PoseGlobe(target, distance) : PoseFlat(target, distance);

    // tilt more when zoomed in (like Paradox games), look straight down when far out
    private float Back(float distance) => distance * (0.75f - 0.55f * Math.Clamp((distance - MinDistance) / (_tiltRef - MinDistance), 0, 1));

    private Camera3D PoseFlat(Vector3 target, float distance)
    {
        var look = new Vector3(target.X, _map.HeightAt(target.X, target.Z), target.Z);
        return new Camera3D(look + new Vector3(0, distance, Back(distance)), look, Vector3.UnitY, 40f, CameraProjection.Perspective);
    }

    /// <summary>The same view over the globe: above the focus point, tilted back to the south, north up the screen.</summary>
    private Camera3D PoseGlobe(Vector3 target, float distance)
    {
        var (fx, fz) = _map.FlatOf(_gFocus);
        var look = _gFocus * (_map.GlobeRadius + _map.HeightAt(fx, fz));
        return new Camera3D(look + _gFocus * distance - _gUp * Back(distance), look, _gUp, 40f, CameraProjection.Perspective);
    }

    private void GlobeFromTarget()
    {
        var (up, south) = _map.GlobeFrame(Target.X, Target.Z);
        _gFocus = up; _gUp = -south;
    }

    private static Vector3 Rot(Vector3 v, Vector3 axis, float a) =>
        v * MathF.Cos(a) + Vector3.Cross(axis, v) * MathF.Sin(a) + axis * (Vector3.Dot(axis, v) * (1 - MathF.Cos(a)));

    /// <summary>Moves the view along the surface by angles (radians) to the screen's right and up.</summary>
    private void MoveGlobe(float right, float up)
    {
        if (right != 0)
        {
            _gFocus = Vector3.Normalize(Rot(_gFocus, _gUp, right));
        }
        if (up != 0)
        {
            var r = Vector3.Normalize(Vector3.Cross(_gUp, _gFocus));
            _gFocus = Vector3.Normalize(Rot(_gFocus, r, -up));
            _gUp = Vector3.Normalize(Rot(_gUp, r, -up));
        }
        Reortho();
    }

    private void Reortho()
    {
        _gUp = Vector3.Normalize(_gUp - _gFocus * Vector3.Dot(_gUp, _gFocus));
    }

    /// <summary>Turns the view so the globe point <paramref name="from"/> ends up where <paramref name="to"/> is (both unit vectors).</summary>
    private void TurnGlobe(Vector3 from, Vector3 to)
    {
        var axis = Vector3.Cross(from, to);
        float len = axis.Length();
        if (len < 1e-7f) return;
        axis /= len;
        float a = MathF.Atan2(len, Vector3.Dot(from, to));
        _gFocus = Vector3.Normalize(Rot(_gFocus, axis, -a));
        _gUp = Rot(_gUp, axis, -a);
        Reortho();
    }

    /// <summary>Eases the view back to north-up, but only away from the poles (there "north" is not a direction), so the player is never spun around.</summary>
    private void AlignNorth(float dt)
    {
        float ay = MathF.Abs(_gFocus.Y);
        float t = Math.Clamp((ay - 0.82f) / (0.966f - 0.82f), 0f, 1f);
        float w = 1f - t * t * (3f - 2f * t);
        if (w <= 0f) return;
        var n = Vector3.UnitY - _gFocus * _gFocus.Y;
        if (n.LengthSquared() < 1e-6f) return;
        n = Vector3.Normalize(n);
        float a = MathF.Atan2(Vector3.Dot(_gFocus, Vector3.Cross(_gUp, n)), Vector3.Dot(_gUp, n));
        _gUp = Rot(_gUp, _gFocus, a * w * Math.Min(1f, dt * 1.5f));
        Reortho();
    }

    private static Camera3D BuildCamera(Vector3 pos, Vector3 look, Vector3 up) =>
        new(pos, look, up, 40f, CameraProjection.Perspective);

    /// <summary>Snap the smoothed state to the logical pose (used for instant, non-damped placement).</summary>
    private void ApplyImmediate()
    {
        var cam = BuildDesiredCamera(out float dist);
        _currentPos = cam.Position;
        _currentTarget = cam.Target;
        _currentUp = cam.Up;
        _currentDistance = dist;
        _smoothed = true;
        Camera = cam;
        _map.ViewCenterX = Globe ? Target.X : cam.Target.X;
    }
}
