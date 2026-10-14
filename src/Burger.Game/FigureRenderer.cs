using System.Numerics;
using Burger.Core.Figures;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Draws the figures of <see cref="FigureSystem"/>: uploads each model once and skins it on the GPU, so a figure costs one
/// draw call and a few dozen matrices however it is posed. Anything that shows a body (the map, a panel, a scene) draws
/// through this with the skin matrices of an <see cref="Animator"/>.
/// </summary>
public sealed class FigureRenderer : IDisposable
{
    // The four joints of a vertex travel in the tangent attribute and their weights in the two texture coordinate sets:
    // attributes raylib already uploads, and a figure has no use for them otherwise. Each joint is three rows of its matrix.
    private const string VertexShader = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec2 vertexTexCoord2;
in vec3 vertexNormal;
in vec4 vertexColor;
in vec4 vertexTangent;
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matView;
uniform mat4 matProjection;
uniform vec3 eyePos;
uniform float pull;
uniform vec4 bones[192];
out vec3 fragPos;
out vec3 fragNormal;
out vec4 fragColor;
void main()
{
    vec4 weights = vec4(vertexTexCoord, vertexTexCoord2);
    ivec4 joints = ivec4(vertexTangent + 0.5);
    vec4 p = vec4(vertexPosition, 1.0);
    vec3 pos = vec3(0.0), nrm = vec3(0.0);
    for (int i = 0; i < 4; i++)
    {
        if (weights[i] <= 0.0) continue;
        int b = joints[i] * 3;
        pos += weights[i] * vec3(dot(p, bones[b]), dot(p, bones[b + 1]), dot(p, bones[b + 2]));
        nrm += weights[i] * vec3(dot(vertexNormal, bones[b].xyz), dot(vertexNormal, bones[b + 1].xyz), dot(vertexNormal, bones[b + 2].xyz));
    }
    fragPos = (matModel * vec4(pos, 1.0)).xyz;
    fragNormal = mat3(matModel) * nrm;
    fragColor = vertexColor;
    gl_Position = mvp * vec4(pos, 1.0);
    // pull the depth towards the eye by about half a body: a figure on a slope is not cut away by the ground in front of its feet
    vec4 nearer = matProjection * matView * vec4(fragPos + normalize(eyePos - fragPos) * pull, 1.0);
    gl_Position.z = nearer.z / nearer.w * gl_Position.w;
}";

    private const string FragmentShader = @"#version 330
in vec3 fragPos;
in vec3 fragNormal;
in vec4 fragColor;
uniform vec4 tint;
uniform vec3 eye;
out vec4 finalColor;
void main()
{
    // halfway between smooth and faceted: the planes of a low-poly body read, without the triangles shouting
    vec3 smoothN = normalize(fragNormal);
    vec3 flatN = normalize(cross(dFdx(fragPos), dFdy(fragPos)));
    if (dot(flatN, smoothN) < 0.0) flatN = -flatN;
    vec3 n = normalize(mix(smoothN, flatN, 0.45));
    vec3 sunDir = normalize(vec3(-0.35, 1.0, -0.45)); // the map's sun
    float ndl = dot(n, sunDir);
    float sun = smoothstep(-0.25, 1.0, ndl);
    vec3 sky = mix(vec3(0.36, 0.33, 0.36), vec3(0.50, 0.54, 0.62), n.y * 0.5 + 0.5);
    vec3 view = normalize(eye - fragPos);
    float rim = pow(1.0 - max(dot(n, view), 0.0), 3.0) * 0.10;
    vec3 col = fragColor.rgb * tint.rgb * (sky + vec3(1.02, 0.96, 0.86) * sun * 0.78) + rim;
    finalColor = vec4(col, fragColor.a * tint.a);
}";

    private readonly Shader _shader;
    private readonly int _bonesLoc, _tintLoc, _eyeLoc, _eyePosLoc, _pullLoc;
    private Material _material;
    private readonly Dictionary<FigureModel, Mesh> _meshes = new();
    private readonly Vector4[] _bones = new Vector4[Rig.MaxJoints * 3];

    public FigureRenderer()
    {
        _shader = Raylib.LoadShaderFromMemory(VertexShader, FragmentShader);
        _bonesLoc = Raylib.GetShaderLocation(_shader, "bones");
        _tintLoc = Raylib.GetShaderLocation(_shader, "tint");
        _eyeLoc = Raylib.GetShaderLocation(_shader, "eye");
        _eyePosLoc = Raylib.GetShaderLocation(_shader, "eyePos");
        _pullLoc = Raylib.GetShaderLocation(_shader, "pull");
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
    }

    /// <summary>Call once per frame before drawing figures, with the camera they are seen from.</summary>
    public void Begin(Camera3D cam)
    {
        Raylib.SetShaderValue(_shader, _eyeLoc, cam.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _eyePosLoc, cam.Position, ShaderUniformDataType.Vec3);
    }

    /// <summary>
    /// Draws a model posed by <paramref name="skin"/> (from <see cref="Animator.Skin"/>). <paramref name="world"/> places it:
    /// model space is metres, +Y up, facing +Z, standing on the origin. Must be called inside BeginMode3D.
    /// </summary>
    public void Draw(FigureModel model, Matrix4x4[] skin, Matrix4x4 world, Color tint)
    {
        int n = Math.Min(skin.Length, Rig.MaxJoints);
        for (int j = 0; j < n; j++)
        {
            var m = skin[j];
            _bones[j * 3] = new Vector4(m.M11, m.M21, m.M31, m.M41);
            _bones[j * 3 + 1] = new Vector4(m.M12, m.M22, m.M32, m.M42);
            _bones[j * 3 + 2] = new Vector4(m.M13, m.M23, m.M33, m.M43);
        }
        Raylib.SetShaderValueV(_shader, _bonesLoc, _bones, ShaderUniformDataType.Vec4, n * 3);
        Raylib.SetShaderValue(_shader, _pullLoc, world.M22 * model.Height * 0.5f, ShaderUniformDataType.Float); // half the drawn height
        Raylib.SetShaderValue(_shader, _tintLoc, new Vector4(tint.R, tint.G, tint.B, tint.A) / 255f, ShaderUniformDataType.Vec4);
        Raylib.DrawMesh(MeshOf(model), _material, Matrix4x4.Transpose(world)); // raylib's matrices are the transpose of System.Numerics'
    }

    /// <summary>Where a figure stands: <paramref name="height"/> is how tall it is drawn in world units, <paramref name="yaw"/> the direction it faces (0 = +Z, turning towards +X).</summary>
    public static Matrix4x4 Placement(FigureModel model, Vector3 position, float yaw, float height)
        => Matrix4x4.CreateScale(height / model.Height) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position);

    /// <summary>As <see cref="Placement(FigureModel, Vector3, float, float)"/>, leaning so the figure's up follows <paramref name="up"/> (the ground normal), by at most 18 degrees and 60% of the slope.</summary>
    public static Matrix4x4 Placement(FigureModel model, Vector3 position, float yaw, float height, Vector3 up)
    {
        float angle = MathF.Acos(Math.Clamp(up.Y, -1f, 1f));
        var axis = Vector3.Cross(Vector3.UnitY, up);
        var lean = Matrix4x4.Identity;
        if (angle > 1e-3f && axis.LengthSquared() > 1e-8f)
            lean = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), Math.Min(angle * 0.6f, MathF.PI / 10f));
        return Matrix4x4.CreateScale(height / model.Height) * Matrix4x4.CreateRotationY(yaw) * lean * Matrix4x4.CreateTranslation(position);
    }

    private Mesh MeshOf(FigureModel model)
    {
        if (_meshes.TryGetValue(model, out var mesh)) return mesh;
        var src = model.Mesh;
        if (src.VertexCount > ushort.MaxValue) throw new InvalidOperationException($"Figure '{model.Key}' has more than {ushort.MaxValue} vertices.");
        mesh = new Mesh(src.VertexCount, src.TriangleCount);
        mesh.AllocVertices(); mesh.AllocNormals(); mesh.AllocColors(); mesh.AllocTexCoords(); mesh.AllocTexCoords2(); mesh.AllocTangents(); mesh.AllocIndices();
        src.Positions.CopyTo(mesh.VerticesAs<Vector3>());
        src.Normals.CopyTo(mesh.NormalsAs<Vector3>());
        src.Colors.CopyTo(mesh.ColorsAs<byte>());
        var w01 = mesh.TexCoordsAs<Vector2>();
        var w23 = mesh.TexCoords2As<Vector2>();
        var joints = mesh.TangentsAs<Vector4>();
        for (int v = 0; v < src.VertexCount; v++)
        {
            w01[v] = new Vector2(src.Weights[v * 4], src.Weights[v * 4 + 1]);
            w23[v] = new Vector2(src.Weights[v * 4 + 2], src.Weights[v * 4 + 3]);
            joints[v] = new Vector4(src.Joints[v * 4], src.Joints[v * 4 + 1], src.Joints[v * 4 + 2], src.Joints[v * 4 + 3]);
        }
        var indices = mesh.IndicesAs<ushort>();
        for (int i = 0; i < src.Indices.Length; i++) indices[i] = (ushort)src.Indices[i];
        Raylib.UploadMesh(ref mesh, false);
        _meshes[model] = mesh;
        return mesh;
    }

    /// <summary>Forgets what was uploaded (after models were replaced).</summary>
    public void Clear()
    {
        foreach (var mesh in _meshes.Values) Raylib.UnloadMesh(mesh);
        _meshes.Clear();
    }

    public void Dispose()
    {
        Clear();
        Raylib.UnloadShader(_shader);
    }

    // ===============================================================================================
    // Contact sheet (Game.exe --figure-sheet out.png): a model at rest from eight sides and each of its clips at eight
    // moments, from the side and from the front quarter, over a ground line. For checking a body and its animations by eye.
    // ===============================================================================================
    public static void RenderSheet(FigureModel model, string path, int pixelsPerMetre = 220)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "figure sheet");
        var renderer = new FigureRenderer();
        const int columns = 8;
        // cells fit the model: a tank is five metres long, a man one and a half across
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var v in model.Mesh.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        float cellW = Math.Max(1.25f, Math.Max(hi.X - lo.X, hi.Z - lo.Z) + 0.3f), cellH = Math.Max(2.15f, model.Height + 0.35f);
        pixelsPerMetre = Math.Min(pixelsPerMetre, (int)(1100 / cellW));
        var rows = new List<(Clip? Clip, float Yaw)> { (null, 0) };
        foreach (var clip in model.Clips.Values) { rows.Add((clip, MathF.PI / 2)); rows.Add((clip, MathF.PI / 5)); }
        float sheetW = columns * cellW, sheetH = rows.Count * cellH;
        var target = Raylib.LoadRenderTexture((int)(sheetW * pixelsPerMetre), (int)(sheetH * pixelsPerMetre));
        var cam = new Camera3D(new Vector3(sheetW / 2, sheetH / 2, 20), new Vector3(sheetW / 2, sheetH / 2, 0), Vector3.UnitY, sheetH, CameraProjection.Orthographic);
        var pose = model.Rig.RestPose();
        var skin = new Matrix4x4[model.Rig.Count];

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(58, 64, 72, 255));
        Raylib.BeginMode3D(cam);
        renderer.Begin(cam);
        for (int r = 0; r < rows.Count; r++)
        {
            float ground = sheetH - (r + 1) * cellH + 0.12f;
            Raylib.DrawLine3D(new Vector3(0, ground, 0), new Vector3(sheetW, ground, 0), new Color(120, 128, 138, 255));
            for (int c = 0; c < columns; c++)
            {
                var (clip, yaw) = rows[r];
                model.Rig.ResetPose(pose);
                if (clip == null) yaw = c * MathF.PI * 2 / columns;
                else clip.Sample(clip.Duration * c / columns, pose);
                model.Rig.SkinMatrices(pose, skin);
                var at = new Vector3((c + 0.5f) * cellW, ground, 0);
                renderer.Draw(model, skin, Placement(model, at, yaw, model.Height), Color.White);
            }
        }
        Raylib.EndMode3D();
        Raylib.EndTextureMode();

        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>
    /// A contact sheet of every piece of kit of every soldier variation (--figure-gear file.png): one row per outfit (the generic soldier, then each
    /// country), one column per item, each piece alone in a three-quarter view. A column shares one scale, so the countries compare directly.
    /// </summary>
    public static void RenderGearSheet(FigureSystem figures, string path, int pixelsPerMetre = 210)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "gear sheet");
        var renderer = new FigureRenderer();
        var items = new[] { SoldierGear.Helmet, SoldierGear.Pack, SoldierGear.Belt, SoldierGear.Tunic, SoldierGear.Trousers, SoldierGear.Boots, SoldierGear.Puttees, SoldierGear.Rifle, SoldierGear.RifleSemi };
        var rows = new List<(string Label, string Suffix, Uniform? U)> { ("generic", "", null) };
        rows.AddRange(Uniforms.Tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).Select(t => (t, "_" + t.ToLowerInvariant(), Uniforms.Find(t))));
        // one scale per column: the biggest piece of it fills 80% of a cell
        var scale = new float[items.Length]; var centre = new Vector3[items.Length];
        for (int c = 0; c < items.Length; c++)
        {
            float big = 0.01f;
            foreach (var row in rows)
            {
                var m = figures.Find(items[c] + row.Suffix)?.Mesh;
                if (m == null || m.VertexCount == 0) continue;
                var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
                foreach (var v in m.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                big = Math.Max(big, Math.Max(hi.X - lo.X, Math.Max(hi.Y - lo.Y, hi.Z - lo.Z)));
                if (row.Suffix == "") centre[c] = (lo + hi) / 2;
            }
            scale[c] = 0.8f / big;
        }
        const float left = 0.8f, top = 0.4f;
        float sheetW = left + items.Length, sheetH = top + rows.Count;
        int w = (int)(sheetW * pixelsPerMetre), h = (int)(sheetH * pixelsPerMetre);
        var target = Raylib.LoadRenderTexture(w, h);
        var cam = new Camera3D(new Vector3(sheetW / 2, sheetH / 2, 20), new Vector3(sheetW / 2, sheetH / 2, 0), Vector3.UnitY, sheetH, CameraProjection.Orthographic);
        var skin = new Matrix4x4[figures.Human.Rig.Count];
        foreach (ref var k in skin.AsSpan()) k = Matrix4x4.Identity;
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(58, 64, 72, 255));
        Raylib.BeginMode3D(cam);
        renderer.Begin(cam);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < items.Length; c++)
            {
                var piece = figures.Find(items[c] + rows[r].Suffix);
                if (piece == null || piece.Mesh.VertexCount == 0) continue;
                var at = new Vector3(left + c + 0.5f, sheetH - top - r - 0.5f, 0);
                // the piece's own rest pose, about the middle of the generic one so a column's pieces sit in the same place
                var world = Matrix4x4.CreateTranslation(-centre[c]) * Matrix4x4.CreateRotationY(items[c].StartsWith(SoldierGear.Rifle) ? -MathF.PI / 2 : -0.6f) * Matrix4x4.CreateScale(scale[c]) * Matrix4x4.CreateTranslation(at);
                renderer.Draw(piece, skin, world, Color.White);
            }
        Raylib.EndMode3D();
        for (int c = 0; c < items.Length; c++)
            Raylib.DrawText(items[c].Replace("gear_", ""), (int)((left + c) * pixelsPerMetre) + 8, 10, 20, Color.White);
        for (int r = 0; r < rows.Count; r++)
        {
            Raylib.DrawText(rows[r].Label.ToUpperInvariant(), 10, (int)((top + r + 0.35f) * pixelsPerMetre), 22, Color.White);
            string note = rows[r].U is { } u ? (u.Rifle ?? "bolt") + (u.FireClip != null ? " / " + u.FireClip : "") : "bolt";
            Raylib.DrawText(note, 10, (int)((top + r + 0.35f) * pixelsPerMetre) + 28, 14, new Color(200, 205, 212, 255));
        }
        Raylib.EndTextureMode();
        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>
    /// A contact sheet of the peoples (--figure-people file.png): every look of <see cref="Ancestries"/> as the bare body (top) and as a soldier (below),
    /// eight to a row, so the differences can be judged side by side.
    /// </summary>
    public static void RenderPeopleSheet(FigureSystem figures, string path, string? tag = null, int pixelsPerMetre = 150)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "people sheet");
        var renderer = new FigureRenderer();
        // per people: a man and a woman as the bare body, then both as a soldier (each with a different hair style)
        var keys = tag != null && Ancestries.Rosters.TryGetValue(tag, out var roster) ? roster.ToList() : Ancestries.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        int perRow = tag != null ? 2 : 4; const float cellW = 1.1f, cellH = 2.0f;
        int bands = (keys.Count + perRow - 1) / perRow;
        float sheetW = perRow * cellW * 4, sheetH = bands * cellH;
        int w = (int)(sheetW * pixelsPerMetre), h = (int)(sheetH * pixelsPerMetre);
        var target = Raylib.LoadRenderTexture(w, h);
        var cam = new Camera3D(new Vector3(sheetW / 2, sheetH / 2, 20), new Vector3(sheetW / 2, sheetH / 2, 0), Vector3.UnitY, sheetH, CameraProjection.Orthographic);
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(58, 64, 72, 255));
        Raylib.BeginMode3D(cam);
        renderer.Begin(cam);
        for (int i = 0; i < keys.Count; i++)
            for (int c = 0; c < 4; c++)
            {
                bool female = c % 2 == 1, soldier = c >= 2;
                string spec = Ancestries.Person(keys[i], female, i * 7 + c);
                string baseKey = soldier ? (tag != null ? figures.SoldierOf(tag).Key : figures.Soldier.Key) : HumanMale.Key;
                var model = figures.Find(baseKey + "@" + spec);
                if (model == null) continue;
                var skin = new Matrix4x4[model.Rig.Count];
                for (int j = 0; j < skin.Length; j++) skin[j] = Matrix4x4.Identity;
                var at = new Vector3(((i % perRow) * 4 + c + 0.5f) * cellW, sheetH - (i / perRow + 1) * cellH + 0.05f, 0);
                renderer.Draw(model, skin, Matrix4x4.CreateRotationY(-0.35f) * Matrix4x4.CreateTranslation(at), Color.White);
            }
        Raylib.EndMode3D();
        for (int i = 0; i < keys.Count; i++)
            Raylib.DrawText(keys[i], (int)((i % perRow) * 4 * cellW * pixelsPerMetre) + 6, (int)((i / perRow) * cellH * pixelsPerMetre) + 4, 16, Color.White);
        Raylib.EndTextureMode();
        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>
    /// A contact sheet of one country's soldier in the given variants (spec = <see cref="Ancestries.Parse"/> text, label shown under each), in rows of up to <paramref name="perRow"/>.
    /// </summary>
    public static void RenderSoldierSheet(FigureSystem figures, string path, string tag, IReadOnlyList<(string Label, string Spec)> entries, int perRow = 6, int pixelsPerMetre = 220)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "soldier sheet");
        var renderer = new FigureRenderer();
        const float cellW = 1.2f, cellH = 2.2f, head = 0.35f;
        int cols = Math.Min(perRow, entries.Count), rows = (entries.Count + perRow - 1) / perRow;
        float sheetW = cols * cellW, sheetH = head + rows * cellH;
        int w = (int)(sheetW * pixelsPerMetre), h = (int)(sheetH * pixelsPerMetre);
        var target = Raylib.LoadRenderTexture(w, h);
        var cam = new Camera3D(new Vector3(sheetW / 2, sheetH / 2, 20), new Vector3(sheetW / 2, sheetH / 2, 0), Vector3.UnitY, sheetH, CameraProjection.Orthographic);
        string soldierKey = figures.SoldierOf(tag).Key;
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(58, 64, 72, 255));
        Raylib.BeginMode3D(cam);
        renderer.Begin(cam);
        for (int i = 0; i < entries.Count; i++)
        {
            var model = figures.Find(soldierKey + "@" + entries[i].Spec) ?? figures.Find(soldierKey);
            if (model == null) continue;
            var skin = new Matrix4x4[model.Rig.Count];
            for (int j = 0; j < skin.Length; j++) skin[j] = Matrix4x4.Identity;
            var at = new Vector3((i % perRow + 0.5f) * cellW, sheetH - head - (i / perRow + 1) * cellH + 0.30f, 0);
            renderer.Draw(model, skin, Matrix4x4.CreateRotationY(-0.35f) * Matrix4x4.CreateTranslation(at), Color.White);
        }
        Raylib.EndMode3D();
        Raylib.DrawText(tag.ToUpperInvariant(), 10, 8, 26, Color.White);
        for (int i = 0; i < entries.Count; i++)
            Raylib.DrawText(entries[i].Label, (int)((i % perRow) * cellW * pixelsPerMetre) + 10, (int)((head + (i / perRow + 1) * cellH - 0.12f) * pixelsPerMetre), 18, Color.White);
        Raylib.EndTextureMode();
        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    // ===============================================================================================
    // Turntable (Game.exe --figure-frames dir): every clip of a model, looping on a platform that turns once while the
    // clip plays. One folder per clip with numbered PNG frames; tools/figure-gifs.sh makes the .gif files.
    // ===============================================================================================
    public const int TurntableFps = 25;

    /// <summary>Seconds one turntable loop lasts: the clip repeated until it is at least this long (a clip longer than that plays once).</summary>
    public const float TurntableSeconds = 6f;

    public static void RenderTurntable(FigureModel model, string folder, int size = 960, string[]? clips = null)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "figure turntable");
        var renderer = new FigureRenderer();
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        foreach (var v in model.Mesh.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        float radius = Math.Max(Math.Max(Math.Abs(lo.X), Math.Abs(hi.X)), Math.Max(Math.Abs(lo.Z), Math.Abs(hi.Z))) + 0.15f;
        float reach = MathF.Max(radius * 2, model.Height * 1.1f); // what has to fit in view
        var target = Raylib.LoadRenderTexture(size, size);
        float fov = 28f;
        float dist = reach * 0.62f / MathF.Tan(fov * MathF.PI / 360f) + radius;
        var centre = new Vector3(0, model.Height * 0.45f, 0);
        var cam = new Camera3D(centre + new Vector3(0, dist * 0.30f, dist * 0.95f), centre, Vector3.UnitY, fov, CameraProjection.Perspective);
        var pose = model.Rig.RestPose();
        var skin = new Matrix4x4[model.Rig.Count];
        Directory.CreateDirectory(folder);
        foreach (var clip in model.Clips.Values)
        {
            if (clips != null && !clips.Contains(clip.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string dir = Path.Combine(folder, $"{model.Key}_{clip.Name}");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);
            int repeats = Math.Max(1, (int)MathF.Round(TurntableSeconds / clip.Duration));
            float length = clip.Duration * repeats;
            int frames = (int)MathF.Round(length * TurntableFps);
            for (int f = 0; f < frames; f++)
            {
                float t = length * f / frames, turn = MathF.Tau * f / frames;
                model.Rig.ResetPose(pose);
                clip.Sample(t % clip.Duration, pose);
                model.Rig.SkinMatrices(pose, skin);
                Raylib.BeginTextureMode(target);
                Raylib.ClearBackground(new Color(44, 50, 58, 255));
                Raylib.BeginMode3D(cam);
                renderer.Begin(cam);
                DrawPlatform(radius, turn);
                renderer.Draw(model, skin, Matrix4x4.CreateRotationY(turn), Color.White);
                Raylib.EndMode3D();
                Raylib.EndTextureMode();
                var image = Raylib.LoadImageFromTexture(target.Texture);
                Raylib.ImageFlipVertical(ref image);
                Raylib.ExportImage(image, Path.Combine(dir, $"{f:0000}.png"));
                Raylib.UnloadImage(image);
            }
            File.WriteAllText(Path.Combine(dir, "fps.txt"), TurntableFps.ToString());
            Console.WriteLine($"{model.Key}_{clip.Name}: {frames} frames ({length:0.0} s, {repeats}x)");
        }
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>Bump when the turntable's look changes (camera, light, plinth, encoder settings): every cached .gif is then made again.</summary>
    private const string GifLook = "turntable-3";

    /// <summary>
    /// One looping .gif per clip of each model at <c>dir/&lt;model&gt;/&lt;clip&gt;.gif</c>, rendered on the turntable and streamed as raw frames straight into
    /// ffmpeg (no PNG files). The window and the renderer are made once, ffmpeg encodes one clip while the next one renders, and a clip whose
    /// mesh, poses and settings hash to what the .gif was made from is skipped (<paramref name="force"/> makes everything again).
    /// </summary>
    public static unsafe void RenderGifs(IReadOnlyList<FigureModel> models, string dir, int size = 480, string[]? clips = null, bool force = false)
    {
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(320, 200, "figure turntable");
        var renderer = new FigureRenderer();
        int px = size * 3 / 2; // drawn at 1.5x and scaled down by the encoder: smooth edges
        var target = Raylib.LoadRenderTexture(px, px);
        var running = new List<(System.Diagnostics.Process P, string Name)>();
        int made = 0, skipped = 0;
        foreach (var model in models)
        {
            var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
            foreach (var v in model.Mesh.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            float radius = Math.Max(Math.Max(Math.Abs(lo.X), Math.Abs(hi.X)), Math.Max(Math.Abs(lo.Z), Math.Abs(hi.Z))) + 0.15f;
            float reach = MathF.Max(radius * 2, model.Height * 1.1f);
            float fov = 28f, dist = reach * 0.62f / MathF.Tan(fov * MathF.PI / 360f) + radius;
            var centre = new Vector3(0, model.Height * 0.45f, 0);
            var cam = new Camera3D(centre + new Vector3(0, dist * 0.30f, dist * 0.95f), centre, Vector3.UnitY, fov, CameraProjection.Perspective);
            var pose = model.Rig.RestPose();
            string folder = Path.Combine(dir, model.Key);
            Directory.CreateDirectory(folder);
            foreach (var clip in model.Clips.Values)
            {
                if (clips != null && !clips.Contains(clip.Name, StringComparer.OrdinalIgnoreCase)) continue;
                int repeats = Math.Max(1, (int)MathF.Round(TurntableSeconds / clip.Duration));
                float length = clip.Duration * repeats;
                int frames = (int)MathF.Round(length * TurntableFps);
                // every pose first (cheap): they, the mesh and the settings are what the picture is made from
                var skins = new Matrix4x4[frames][];
                using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
                void Feed<T>(T[] a) where T : unmanaged => sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(a.AsSpan()));
                sha.AppendData(System.Text.Encoding.UTF8.GetBytes($"{GifLook} {size} {frames} {TurntableFps} {radius} {model.Height}"));
                var m = model.Mesh;
                Feed(m.Positions); Feed(m.Normals); Feed(m.Colors); Feed(m.Joints); Feed(m.Weights); Feed(m.Indices);
                for (int f = 0; f < frames; f++)
                {
                    model.Rig.ResetPose(pose);
                    clip.Sample(length * f / frames % clip.Duration, pose);
                    skins[f] = new Matrix4x4[model.Rig.Count];
                    model.Rig.SkinMatrices(pose, skins[f]);
                    Feed(skins[f]);
                }
                string sig = Convert.ToHexString(sha.GetHashAndReset()), gif = Path.Combine(folder, clip.Name + ".gif"), sigFile = Path.Combine(folder, "." + clip.Name + ".sig");
                if (!force && File.Exists(gif) && File.Exists(sigFile) && File.ReadAllText(sigFile).Trim() == sig) { skipped++; continue; }

                while (running.Count >= 6) { running[0].P.WaitForExit(); running.RemoveAt(0); } // ffmpeg threads share the cores with nothing but this loop
                var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg")
                {
                    RedirectStandardInput = true, UseShellExecute = false,
                    ArgumentList =
                    {
                        "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{px}x{px}", "-framerate", TurntableFps.ToString(), "-i", "-",
                        "-vf", $"vflip,scale={size}:{size}:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                        "-loop", "0", gif,
                    },
                };
                var proc = System.Diagnostics.Process.Start(psi) ?? throw new IOException("Could not start ffmpeg");
                var pipe = proc.StandardInput.BaseStream;
                var buffer = new byte[px * px * 4];
                for (int f = 0; f < frames; f++)
                {
                    float turn = MathF.Tau * f / frames;
                    Raylib.BeginTextureMode(target);
                    Raylib.ClearBackground(new Color(44, 50, 58, 255));
                    Raylib.BeginMode3D(cam);
                    renderer.Begin(cam);
                    DrawPlatform(radius, turn);
                    renderer.Draw(model, skins[f], Matrix4x4.CreateRotationY(turn), Color.White);
                    Raylib.EndMode3D();
                    Raylib.EndTextureMode();
                    var image = Raylib.LoadImageFromTexture(target.Texture);
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)image.Data, buffer, 0, buffer.Length);
                    Raylib.UnloadImage(image);
                    pipe.Write(buffer, 0, buffer.Length);
                }
                pipe.Close();
                running.Add((proc, gif));
                File.WriteAllText(sigFile, sig);
                made++;
                Console.WriteLine($"{model.Key}/{clip.Name}.gif: {frames} frames ({length:0.0} s)");
            }
        }
        foreach (var (p, name) in running) { p.WaitForExit(); if (p.ExitCode != 0) Console.Error.WriteLine($"ffmpeg failed for {name}"); }
        Console.WriteLine($"{made} made, {skipped} unchanged");
        Raylib.UnloadRenderTexture(target);
        renderer.Dispose();
        Raylib.CloseWindow();
    }

    /// <summary>A round plinth with radial marks, turning with the model, so the turning is visible.</summary>
    private static void DrawPlatform(float radius, float turn)
    {
        var top = new Color(96, 104, 114, 255);
        Raylib.DrawCylinder(new Vector3(0, -0.06f, 0), radius, radius, 0.06f, 64, top);
        Raylib.DrawCylinderWires(new Vector3(0, -0.06f, 0), radius, radius, 0.06f, 64, new Color(150, 158, 168, 255));
        for (int i = 0; i < 12; i++)
        {
            float a = turn + i * MathF.Tau / 12;
            var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
            Raylib.DrawLine3D(dir * radius * 0.82f + new Vector3(0, 0.003f, 0), dir * radius * 0.98f + new Vector3(0, 0.003f, 0), i % 3 == 0 ? new Color(235, 200, 90, 255) : new Color(60, 66, 76, 255));
        }
    }
}
