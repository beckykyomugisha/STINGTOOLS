using System.Text.Json;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

public class GeofenceValidationService : IGeofenceValidationService
{
    public bool IsInsideBoundary(string? geoJson, double latitude, double longitude)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
            return true; // no boundary defined — allow

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;

            // Support both bare coordinate arrays and GeoJSON Polygon
            JsonElement coordinates;
            if (root.TryGetProperty("coordinates", out var coords))
                coordinates = coords[0]; // outer ring of Polygon
            else if (root.ValueKind == JsonValueKind.Array)
                coordinates = root;
            else
                return true;

            // Build ring
            var ring = new List<(double Lon, double Lat)>();
            foreach (var point in coordinates.EnumerateArray())
            {
                double lon = point[0].GetDouble();
                double lat = point[1].GetDouble();
                ring.Add((lon, lat));
            }

            return PointInPolygon(ring, longitude, latitude);
        }
        catch
        {
            return true; // malformed GeoJSON — fail open
        }
    }

    private static bool PointInPolygon(List<(double Lon, double Lat)> ring, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            double xi = ring[i].Lon, yi = ring[i].Lat;
            double xj = ring[j].Lon, yj = ring[j].Lat;

            if (((yi > y) != (yj > y)) &&
                (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
