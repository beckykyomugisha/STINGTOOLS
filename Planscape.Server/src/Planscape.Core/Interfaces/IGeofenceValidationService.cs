namespace Planscape.Core.Interfaces;

public interface IGeofenceValidationService
{
    bool IsInsideBoundary(string? geoJson, double latitude, double longitude);
}
