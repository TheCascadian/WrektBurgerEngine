using System.Numerics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// The screen area the map is drawn into. The game uses the whole window; the editor (Burger Studio) draws the map
/// into a panel, so picking, labels and camera input must use the panel's size and mouse position.
/// </summary>
public static class Viewport
{
    /// <summary>Top-left corner of the map area on screen (mouse offset). 0,0 for the game.</summary>
    public static Vector2 Origin;
    /// <summary>Size of the map area; 0 = the whole window.</summary>
    public static int Width, Height;

    public static int W => Width > 0 ? Width : Raylib.GetScreenWidth();
    public static int H => Height > 0 ? Height : Raylib.GetScreenHeight();

    /// <summary>Mouse position relative to the map area.</summary>
    public static Vector2 Mouse => Raylib.GetMousePosition() - Origin;

    /// <summary>Radius of the globe the map is wrapped on (0 = the flat map). Set by the map renderer.</summary>
    public static float GlobeRadius;

    /// <summary>How high the camera is above the map: its height over the flat map, its altitude over the globe.</summary>
    public static float HeightOf(Camera3D cam) => GlobeRadius > 0 ? cam.Position.Length() - GlobeRadius : cam.Position.Y;

    /// <summary>True for a point on the far side of the globe (the planet hides it). Always false on the flat map.</summary>
    public static bool IsHidden(Vector3 world, Camera3D cam) =>
        GlobeRadius > 0 && Vector3.Dot(world, cam.Position) < GlobeRadius * GlobeRadius * 0.995f;

    public static Vector2 ToScreen(Vector3 world, Camera3D cam) => Raylib.GetWorldToScreenEx(world, cam, W, H);

    /// <summary>
    /// Unprojects a screen point onto the plane y = planeY. Returns null if the ray doesn't hit it ahead of the eye.
    /// </summary>
    public static Vector3? ScreenToPlane(Vector2 screen, Camera3D cam, float planeY)
    {
        var ray = Ray(screen, cam);
        if (Math.Abs(ray.Direction.Y) < 1e-4f) return null;
        float t = (planeY - ray.Position.Y) / ray.Direction.Y;
        if (t < 0) return null;
        return ray.Position + ray.Direction * t;
    }

    /// <summary>
    /// Projects a world point to screen space and snaps X/Y to whole pixels. Use for text/UI overlays:
    /// sub-pixel positions make glyphs shimmer while the camera moves.
    /// </summary>
    public static Vector2 SnapToScreen(Vector3 world, Camera3D cam)
    {
        if (IsHidden(world, cam)) return new Vector2(-100000, -100000); // far side of the globe: far off screen, never drawn
        var s = ToScreen(world, cam);
        return new Vector2(MathF.Round(s.X), MathF.Round(s.Y));
    }

    /// <summary>
    /// Nearest distance in front of the eye, in world units, at which <see cref="TryProjectSnapped"/> still accepts a point.
    /// Chosen well below the lowest label camera height so ordinary points never hit it.
    /// </summary>
    public const float MinProjectionDepth = 1f;

    /// <summary>Signed distance of a point in front of the camera plane (negative when it is behind the eye).</summary>
    public static float DepthAlong(Vector3 world, Vector3 eye, Vector3 forward) => Vector3.Dot(world - eye, forward);

    /// <summary>
    /// <see cref="SnapToScreen"/> for overlays that must not draw points the camera cannot see. Raylib's
    /// world-to-screen divides by a negative w for points behind the eye, which lands them mirrored and
    /// magnified inside the window; panning the tilted camera across a large map made country names
    /// flash in huge letters. Returns false for such points.
    /// </summary>
    public static bool TryProjectSnapped(Vector3 world, Camera3D cam, out Vector2 screen)
    {
        var forward = Vector3.Normalize(cam.Target - cam.Position);
        if (DepthAlong(world, cam.Position, forward) < MinProjectionDepth || IsHidden(world, cam))
        {
            screen = default;
            return false;
        }
        screen = SnapToScreen(world, cam);
        return true;
    }

    public static Ray Ray(Vector2 pos, Camera3D cam) => Raylib.GetScreenToWorldRayEx(pos, cam, W, H);

    /// <summary>Back to full-window mode.</summary>
    public static void Reset() { Origin = Vector2.Zero; Width = Height = 0; }
}
