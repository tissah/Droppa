using Microsoft.Maui.Devices.Sensors;

namespace Droppa.Services.Maps;

/// <summary>The computed route between two points.</summary>
/// <param name="Points">Ordered coordinates to draw as a polyline.</param>
/// <param name="DistanceKm">Total route distance in kilometres.</param>
/// <param name="DurationMinutes">Estimated travel time in minutes.</param>
/// <param name="TravelMode">The mode Google actually routed with ("two_wheeler" or "driving").</param>
public record RouteResult(IReadOnlyList<Location> Points, double DistanceKm, double DurationMinutes, string TravelMode);

/// <summary>
/// Computes a driving route between two coordinates using the Google Directions API.
/// Prefers motorcycle mode (<c>two_wheeler</c>) and the shortest of the returned alternatives.
/// </summary>
public interface IDirectionsService
{
    /// <summary>
    /// Returns the shortest motorcycle route from origin to destination, or null if no key is
    /// configured or the API returns no route.
    /// </summary>
    Task<RouteResult?> GetRouteAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        CancellationToken ct = default);
}
