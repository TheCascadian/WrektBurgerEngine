using System.Numerics;
using Burger.Core;
using Burger.Core.Figures;
using Burger.Core.Sites;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// The 3D models of the largest places (see <see cref="SiteSystem"/>, docs/game/SITES.md): each site's baked model standing on its location. They are
/// drawn at a fixed scale, so they grow and shrink with the map, and appear once the camera is close enough.
/// </summary>
public sealed class MapSites : IDisposable
{
    public static bool Show = true;

    /// <summary>World units per metre of a site model (a soldier is drawn up to 2 units tall, so sites are small beside them on purpose: they stand for a whole town).</summary>
    public const float MetresToWorld = 0.05f;

    /// <summary>Sites appear when the camera is lower than this (the same measure as <see cref="Viewport.HeightOf"/>).</summary>
    public const float ShowBelow = 90f;

    private readonly Game _game;
    private readonly MapRenderer _map;
    private readonly FigureRenderer _renderer = new();
    private readonly Dictionary<FigureModel, Matrix4x4[]> _skins = new();
    private readonly HashSet<FigureModel> _uploaded = new();

    public MapSites(Game game, MapRenderer map) { _game = game; _map = map; }

    public void Draw(Camera3D cam)
    {
        if (!Show || _map.Globe || Viewport.HeightOf(cam) > ShowBelow || _game.Sites.Sites.Count == 0) return;
        _game.Sites.Refresh((loc, x, z) =>
        {
            var p = _map.LocationPos(loc);
            return (_map.HeightAt(p.X + x * MetresToWorld, p.Z + z * MetresToWorld) - p.Y) / MetresToWorld;
        });
        _renderer.Begin(cam);
        var live = new HashSet<FigureModel>();
        foreach (var site in _game.Sites.Sites)
        {
            if (site.Model == null || !_game.IsVisible(site.Location)) continue;
            live.Add(site.Model);
            var pos = _map.LocationPos(site.Location);
            if (Vector3.DistanceSquared(pos, cam.Target) > ShowBelow * ShowBelow * 4) continue;
            if (!_skins.TryGetValue(site.Model, out var skin))
            {
                skin = new Matrix4x4[site.Model.Rig.Count];
                Array.Fill(skin, Matrix4x4.Identity);
                _skins[site.Model] = skin;
            }
            _uploaded.Add(site.Model);
            _renderer.Draw(site.Model, skin, Matrix4x4.CreateScale(MetresToWorld) * Matrix4x4.CreateTranslation(pos), Color.White);
        }
        // a rebuilt site leaves its old model behind: free what the sites no longer use
        if (_uploaded.Count > live.Count + 8)
            foreach (var old in _uploaded.Where(m => !_game.Sites.Sites.Any(s => s.Model == m)).ToList()) { _renderer.Forget(old); _uploaded.Remove(old); _skins.Remove(old); }
    }

    public void Dispose() => _renderer.Dispose();
}
