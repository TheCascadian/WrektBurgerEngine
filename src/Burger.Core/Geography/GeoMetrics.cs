using Burger.Core.Data;
using Burger.Core.Map;
using Burger.Core.Model;

namespace Burger.Core.Geography;

/// <summary>
/// Physical measurements that are valid on a sphere. The province map is an equirectangular world image (x = longitude, y = latitude, the same
/// projection the globe view uses), so a pixel's real area shrinks with the cosine of its latitude; summing pixel counts would overstate polar land.
/// </summary>
public static class GeoMetrics
{
    public const double EarthRadiusKm = 6371.0088;

    /// <summary>Area in km2 of one pixel of row <paramref name="y"/> on a map of <paramref name="width"/> x <paramref name="height"/> pixels between the two latitudes (default: the whole globe).</summary>
    public static double PixelAreaKm2(int y, int width, int height, double northDeg = 90, double southDeg = -90)
    {
        double n = northDeg * Math.PI / 180, s = southDeg * Math.PI / 180;
        double lat1 = n - (n - s) * y / height;
        double lat2 = n - (n - s) * (y + 1) / height;
        double dLon = 2 * Math.PI / width;
        return EarthRadiusKm * EarthRadiusKm * dLon * Math.Abs(Math.Sin(lat1) - Math.Sin(lat2));
    }

    /// <summary>Spherical area of every location (by index) in km2. One pass over the map.</summary>
    public static double[] LocationAreasKm2(World world)
    {
        var map = world.Map;
        var area = new double[world.Locations.Count];
        if (map == null || map.Width == 0) return area;
        int w = map.Width, h = map.Height;
        var pl = map.PixelLocation;
        for (int y = 0; y < h; y++)
        {
            double a = PixelAreaKm2(y, w, h, map.Settings.NorthLat, map.Settings.SouthLat);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int li = pl[row + x];
                if (li >= 0) area[li] += a;
            }
        }
        return area;
    }

    /// <summary>True when two locations share a traversable border: a land (or sea) border of at least <paramref name="minBorderPixels"/>; a single touching corner does not count.</summary>
    public static bool SharesBorder(Adjacency a, int minBorderPixels = 2)
        => a.Kind != AdjacencyKind.Strait ? a.BorderPixels >= minBorderPixels : true;
}
