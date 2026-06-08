using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Uses MAUI Essentials Geolocation to read the device GPS. Requires the location
/// permissions declared in the Android manifest. Falls back to null on failure so
/// callers can prompt the user.
/// </summary>
public class LocationService : ILocationService
{
    /// <summary>Lilongwe city centre — used only as a dev fallback when GPS is unavailable.</summary>
    public static readonly GeoLocation LilongweCentre = new(-13.9626, 33.7741, "Lilongwe (approx.)");

    public async Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
                return null;

            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(15));
            var location = await Geolocation.GetLocationAsync(request, ct)
                           ?? await Geolocation.GetLastKnownLocationAsync();

            return location is null
                ? null
                : new GeoLocation(location.Latitude, location.Longitude, "Current location");
        }
        catch (Exception)
        {
            // Permission revoked, GPS off, or unsupported device.
            return null;
        }
    }
}
