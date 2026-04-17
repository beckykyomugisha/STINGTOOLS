using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Point-in-polygon validator for project geofences.
///
/// NEW-LOGIC-03/04 — Behaviour on missing / malformed boundaries is
/// configurable. By default the service now fails OPEN only when no boundary
/// is defined at all (allow), and fails CLOSED on malformed JSON (deny + log).
/// Projects that explicitly opt into stricter mode via
/// <c>Geofence:RequireBoundary=true</c> deny even the "no boundary" case.
/// </summary>
public class GeofenceValidationService : IGeofenceValidationService
{
    private readonly ILogger<GeofenceValidationService> _logger;
    private readonly bool _requireBoundary;

    public GeofenceValidationService(ILogger<GeofenceValidationService> logger, IConfiguration config)
    {
        _logger = logger;
        _requireBoundary = bool.TryParse(config["Geofence:RequireBoundary"], out var req) && req;
    }

    public bool IsInsideBoundary(string? geoJson, double latitude, double longitude)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            if (_requireBoundary)
            {
                _logger.LogInformation("Geofence check denied: project has no BoundaryPolygon and Geofence:RequireBoundary is true");
                return false;
            }
            return true; // no boundary defined — allow (legacy behaviour)
        }

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            var root = doc.RootElement;

            // Support both bare coordinate arrays and GeoJSON Polygon
            JsonElement coordinates;
            if (root.TryGetProperty("coordinates", out var coords))
            {
                if (coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Geofence boundary has empty coordinates — failing closed");
                    return false;
                }
                coordinates = coords[0]; // outer ring of Polygon
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                coordinates = root;
            }
            else
            {
                _logger.LogWarning("Geofence boundary is not a coordinate array — failing closed");
                return false;
            }

            var ring = new List<(double Lon, double Lat)>();
            foreach (var point in coordinates.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
                    throw new JsonException("Boundary point missing lon/lat pair");
                double lon = point[0].GetDouble();
                double lat = point[1].GetDouble();
                ring.Add((lon, lat));
            }

            if (ring.Count < 3)
            {
                _logger.LogWarning("Geofence boundary has fewer than 3 vertices — failing closed");
                return false;
            }

            return PointInPolygon(ring, longitude, latitude);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geofence boundary parsing failed — failing closed");
            return false; // NEW-LOGIC-04: fail-closed on malformed JSON
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
