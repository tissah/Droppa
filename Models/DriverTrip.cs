namespace Droppa.Models;

/// <summary>
/// One of the driver's accepted deliveries, shown on the "My Trips" hub. Maps a
/// <c>DeliveryDto</c> and exposes phase-aware labels (pickup vs delivery) for the XAML.
///
/// Flow by service type:
///   Send    (1): pick up FROM the customer  → deliver TO the courier office.
///   Receive (2): pick up FROM the courier    → deliver TO the customer.
/// </summary>
public class DriverTrip
{
    // Mirrors the API's DeliveryStatus enum.
    public const int Pending = 0;
    public const int DriverAssigned = 1;
    public const int Accepted = 2;
    public const int Rejected = 3;
    public const int PickupInProgress = 4;
    public const int ParcelCollected = 5;
    public const int InTransit = 6;
    public const int Arriving = 7;
    public const int Delivered = 8;
    public const int Cancelled = 9;

    public int Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    public int ServiceType { get; init; }
    public int Status { get; init; }
    public string CourierServiceName { get; init; } = string.Empty;
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public double DestinationLatitude { get; init; }
    public double DestinationLongitude { get; init; }
    public decimal TotalFee { get; init; }
    public double? ParcelWeightGrams { get; init; }

    public bool IsReceive => ServiceType == 2;
    public string CategoryIcon => IsReceive ? "📥" : "📦";
    public string CategoryLabel => IsReceive ? "Receive parcel" : "Send parcel";

    /// <summary>Accepted but the parcel has not been collected yet — still needs a pickup.</summary>
    public bool NeedsPickup => Status is DriverAssigned or Accepted or PickupInProgress;

    /// <summary>The parcel has been collected ("frozen" out of pickups) but not yet delivered.</summary>
    public bool NeedsDelivery => Status is ParcelCollected or InTransit or Arriving;

    public bool IsDelivered => Status == Delivered;

    // ---- Pickup phase ----
    public string PickupTitle => IsReceive
        ? $"Pick up from {CourierServiceName}"
        : "Pick up from customer";

    public string PickupLocationText => $"{PickupLatitude:F5}, {PickupLongitude:F5}";

    // ---- Delivery phase ----
    public string DeliveryTitle => IsReceive
        ? "Deliver to customer"
        : $"Deliver to {CourierServiceName}";

    public string DeliveryLocationText => $"{DestinationLatitude:F5}, {DestinationLongitude:F5}";

    public string FeeText => $"MWK {TotalFee:N0}";

    public string StatusLabel => Status switch
    {
        Pending => "Pending",
        DriverAssigned => "Driver assigned",
        Accepted => "Accepted",
        Rejected => "Rejected",
        PickupInProgress => "Pickup in progress",
        ParcelCollected => "Parcel collected",
        InTransit => "In transit",
        Arriving => "Arriving",
        Delivered => "Delivered",
        Cancelled => "Cancelled",
        _ => "Unknown"
    };
}
