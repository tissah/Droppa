namespace Droppa.Models;

/// <summary>
/// A bindable open delivery request shown on the driver's job board. Maps a
/// <c>DriverJobDto</c> and exposes display-friendly labels for the XAML.
/// </summary>
public class DriverJob
{
    public int DeliveryRequestId { get; init; }
    public string Reference { get; init; } = string.Empty;

    /// <summary>1 = Send parcel, 2 = Receive parcel (matches the API's ServiceType).</summary>
    public int ServiceType { get; init; }

    public string CourierServiceName { get; init; } = string.Empty;
    public double PickupLatitude { get; init; }
    public double PickupLongitude { get; init; }
    public double DestinationLatitude { get; init; }
    public double DestinationLongitude { get; init; }
    public double DistanceKm { get; init; }
    public decimal TotalFee { get; init; }
    public double? DistanceFromDriverKm { get; init; }

    public bool IsReceive => ServiceType == 2;
    public string CategoryLabel => IsReceive ? "Receive parcel" : "Send parcel";
    public string CategoryIcon => IsReceive ? "📥" : "📦";

    /// <summary>Where the driver collects the parcel.</summary>
    public string PickupText => $"{PickupLatitude:F5}, {PickupLongitude:F5}";

    /// <summary>Where the driver drops the parcel.</summary>
    public string DestinationText => $"{DestinationLatitude:F5}, {DestinationLongitude:F5}";

    public string DistanceFromDriverText =>
        DistanceFromDriverKm is double d ? $"{d:F1} km from you" : "Distance unknown";
}
