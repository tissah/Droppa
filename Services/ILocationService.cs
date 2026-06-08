using Droppa.Models;

namespace Droppa.Services;

/// <summary>Resolves the device's current GPS position (spec section 3).</summary>
public interface ILocationService
{
    /// <summary>
    /// Returns the current location, or null if permission was denied or GPS is unavailable.
    /// </summary>
    Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken ct = default);
}
