using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Straight-line ("as the crow flies") distance using the Haversine formula.
/// This works offline with no API key and is a stand-in for the Google Distance
/// Matrix API. Replace with <c>GoogleDistanceService</c> when a key is available to
/// get real road distances; callers depend only on <see cref="IDistanceService"/>.
/// </summary>
public class HaversineDistanceService : IDistanceService
{
    private const double EarthRadiusKm = 6371.0;

    public Task<double> GetDistanceKmAsync(GeoLocation origin, GeoLocation destination, CancellationToken ct = default)
    {
        var dLat = DegreesToRadians(destination.Latitude - origin.Latitude);
        var dLon = DegreesToRadians(destination.Longitude - origin.Longitude);

        var lat1 = DegreesToRadians(origin.Latitude);
        var lat2 = DegreesToRadians(destination.Latitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return Task.FromResult(EarthRadiusKm * c);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
