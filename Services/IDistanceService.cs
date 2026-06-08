using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Computes the travel distance between two points (spec section 3 / Distance Matrix API).
/// </summary>
public interface IDistanceService
{
    /// <summary>Returns the distance between origin and destination, in kilometres.</summary>
    Task<double> GetDistanceKmAsync(GeoLocation origin, GeoLocation destination, CancellationToken ct = default);
}
