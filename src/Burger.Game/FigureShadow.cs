using System.Numerics;
using Burger.Core.Figures;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Everything a shadow needs to know about the figure it falls from (all in world space, drawn inside BeginMode3D).</summary>
public readonly record struct ShadowContext(
    FigureModel Model, Matrix4x4[] Skin, Matrix4x4 World,
    Vector3 Position, Vector3 Up, float Yaw, float Height, float Radius, Camera3D Camera);

/// <summary>
/// How one figure shades the ground under it. <see cref="FigureShadows"/> picks one per model, so a model can later get its own
/// (a projected silhouette from <see cref="ShadowContext.Skin"/>, a cast shadow along the sun, a long tank hull) without touching the map code.
/// Called before the figure is drawn; implementations must not write depth.
/// </summary>
public interface IFigureShadow
{
    void Draw(in ShadowContext ctx);
}

/// <summary>Chooses the shadow of each model: a soft circular blob unless a model has its own.</summary>
public static class FigureShadows
{
    /// <summary>Turns every shadow off (the <c>figure_shadows</c> setting).</summary>
    public static bool Enabled = true;

    /// <summary>Used by every model without its own shadow.</summary>
    public static IFigureShadow Default { get; set; } = new BlobShadow();

    private static readonly Dictionary<string, IFigureShadow> PerModel = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gives the model with this key its own shadow (null goes back to <see cref="Default"/>).</summary>
    public static void Set(string modelKey, IFigureShadow? shadow)
    {
        if (shadow == null) PerModel.Remove(modelKey); else PerModel[modelKey] = shadow;
    }

    public static IFigureShadow For(FigureModel model) => PerModel.TryGetValue(model.Key, out var s) ? s : Default;

    public static void Draw(in ShadowContext ctx)
    {
        if (Enabled) For(ctx.Model).Draw(ctx);
    }
}

/// <summary>A soft dark disc flat on the ground (lying on the slope under the feet), about as wide as the figure is tall.</summary>
public sealed class BlobShadow : IFigureShadow
{
    /// <summary>Diameter as a share of the figure's drawn height.</summary>
    public float Size = 0.9f;
    /// <summary>0-1 darkness in the middle.</summary>
    public float Strength = 0.5f;
    /// <summary>Lift above the ground as a share of the height, so the disc does not z-fight with the terrain.</summary>
    public float Lift = 0.012f;
    /// <summary>Stretch along the facing direction (1 = round).</summary>
    public float Length = 1f;

    private Texture2D _tex;
    private bool _ready;

    public void Draw(in ShadowContext c)
    {
        if (!_ready)
        {
            var img = Raylib.GenImageGradientRadial(64, 64, 0.15f, Color.White, new Color(255, 255, 255, 0));
            _tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(_tex, TextureFilter.Bilinear);
            _ready = true;
        }
        var up = c.Up;
        var fwd = new Vector3(MathF.Sin(c.Yaw), 0, MathF.Cos(c.Yaw));
        fwd = Vector3.Normalize(fwd - up * Vector3.Dot(fwd, up));
        var right = Vector3.Cross(up, fwd);
        float half = c.Height * Size * 0.5f;
        var o = c.Position + up * (c.Height * Lift);
        var f = fwd * half * Length;
        var r = right * half;
        byte a = (byte)(Math.Clamp(Strength, 0f, 1f) * 255);

        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        Rlgl.SetTexture(_tex.Id);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(0, 0, 0, a);
        Rlgl.Normal3f(up.X, up.Y, up.Z);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(o.X - r.X + f.X, o.Y - r.Y + f.Y, o.Z - r.Z + f.Z);
        Rlgl.TexCoord2f(0, 1); Rlgl.Vertex3f(o.X - r.X - f.X, o.Y - r.Y - f.Y, o.Z - r.Z - f.Z);
        Rlgl.TexCoord2f(1, 1); Rlgl.Vertex3f(o.X + r.X - f.X, o.Y + r.Y - f.Y, o.Z + r.Z - f.Z);
        Rlgl.TexCoord2f(1, 0); Rlgl.Vertex3f(o.X + r.X + f.X, o.Y + r.Y + f.Y, o.Z + r.Z + f.Z);
        Rlgl.End();
        Rlgl.SetTexture(0);
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
    }
}
