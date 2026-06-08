namespace Droppa.Models;

/// <summary>
/// A simple geographic point with an optional human-readable address.
/// Kept independent of platform types so it can flow through services and view models.
/// </summary>
public record GeoLocation(double Latitude, double Longitude, string? Address = null)
{
    public override string ToString() =>
        Address ?? $"{Latitude:F5}, {Longitude:F5}";
}
