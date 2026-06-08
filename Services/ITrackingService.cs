namespace Droppa.Services;

/// <summary>A live driver position pushed over the tracking hub.</summary>
public record DriverLocationUpdate(int DeliveryRequestId, double Lat, double Lng, double? EtaMinutes);

/// <summary>Driver + motorcycle details delivered to the customer when a ride is accepted.</summary>
public record RideAcceptedInfo(
    int DeliveryRequestId,
    string? DriverName,
    string? DriverPhone,
    string? DriverPhoto,
    string? Motorcycle,
    string? Registration);

/// <summary>A delivery status change pushed over the tracking hub.</summary>
public record DeliveryStatusUpdate(int DeliveryRequestId, string Status);

/// <summary>
/// Real-time channel over the API's SignalR tracking hub. Customers subscribe to a
/// delivery to receive live driver positions; both sides receive ride/status events on
/// their personal user group. One shared connection is reused across the app.
/// </summary>
public interface ITrackingService
{
    /// <summary>Raised when the assigned driver's position moves (for a subscribed delivery).</summary>
    event Action<DriverLocationUpdate>? DriverLocationUpdated;

    /// <summary>Raised on the customer's connection when a driver accepts their request.</summary>
    event Action<RideAcceptedInfo>? RideAccepted;

    /// <summary>Raised when a subscribed delivery changes status.</summary>
    event Action<DeliveryStatusUpdate>? DeliveryStatusChanged;

    /// <summary>Opens the hub connection if not already connected. Safe to call repeatedly.</summary>
    Task EnsureConnectedAsync(CancellationToken ct = default);

    /// <summary>Joins the per-delivery group to receive its live location/status updates.</summary>
    Task SubscribeToDeliveryAsync(int deliveryRequestId, CancellationToken ct = default);

    /// <summary>Leaves the per-delivery group.</summary>
    Task UnsubscribeFromDeliveryAsync(int deliveryRequestId, CancellationToken ct = default);

    /// <summary>Tears the connection down (e.g. on sign-out).</summary>
    Task StopAsync();
}
