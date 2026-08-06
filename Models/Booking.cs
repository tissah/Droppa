namespace Droppa.Models;

/// <summary>
/// A delivery request created by a customer. Captures the route, the computed
/// distance/fee, and the current delivery status.
/// </summary>
public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Server delivery id (int) used for live tracking; 0 before the booking is created.</summary>
    public int DeliveryId { get; set; }

    public ServiceType ServiceType { get; set; }

    /// <summary>
    /// The primary parcel. For backward compatibility this mirrors the first entry of
    /// <see cref="Parcels"/> and is what the single-parcel API contract is built from.
    /// </summary>
    public Parcel Parcel { get; set; } = new();

    /// <summary>
    /// All parcels in this booking. A send can carry several parcels to different receivers;
    /// a receive can collect several parcels from different senders. Always has at least one.
    /// </summary>
    public List<Parcel> Parcels { get; set; } = [];

    public CourierService Courier { get; set; } = new();

    /// <summary>The specific courier branch handling this parcel, when the courier has branches.</summary>
    public Branch? Branch { get; set; }

    public GeoLocation Pickup { get; set; } = new(0, 0);
    public GeoLocation Destination { get; set; } = new(0, 0);

    public double DistanceKm { get; set; }
    public decimal RatePerKm { get; set; }

    /// <summary>The distance-based ride fee the customer pays to book. Charged once per route,
    /// regardless of how many parcels are sent. The parcel weight charge is separate and added
    /// later by the driver.</summary>
    public decimal TotalFee { get; set; }

    /// <summary>
    /// Receive only: the amount the customer entered as owed at the courier office (COD / handling).
    /// The customer prepays it to Droppa; the driver remits exactly this to the courier on
    /// collection. It is never the distance ride fee. Zero for a Send booking.
    /// </summary>
    public decimal CourierAmount { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>Raw API DeliveryStatus code (0=Pending … 8=Delivered). Drives the order summary.</summary>
    public int ServerStatus { get; set; }

    /// <summary>Human-readable status from the server (e.g. "In transit").</summary>
    public string StatusText { get; set; } = "Pending";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    private string? _reference;

    /// <summary>Server reference (e.g. "DRP-000001"); falls back to a local id before booking.</summary>
    public string Reference
    {
        get => _reference ?? $"DRP-{Id.ToString()[..8].ToUpperInvariant()}";
        set => _reference = value;
    }

    /// <summary>
    /// True only once a driver has confirmed (accepted) the trip and it's still active —
    /// before that there's no driver position to follow, so live tracking is hidden.
    /// </summary>
    public bool CanTrack => DeliveryId > 0 && OrderTimeline.DriverConfirmed(ServerStatus)
                            && ServerStatus != 8;

    /// <summary>The customer-facing order summary: placed → accepted → … → delivered.</summary>
    public IReadOnlyList<OrderStage> Timeline => OrderTimeline.Build(ServerStatus);

    /// <summary>True if the order was rejected or cancelled.</summary>
    public bool IsCancelled => OrderTimeline.IsCancelled(ServerStatus);

    /// <summary>
    /// True while the customer can still cancel this trip — once it's created on the server and
    /// before it's delivered or already cancelled.
    /// </summary>
    public bool CanCancel => DeliveryId > 0 && CancellationPolicy.CanCancel(ServerStatus);

    /// <summary>
    /// The amount refunded if the trip is cancelled right now: the trip charge less a 30%
    /// cancellation fee before pickup, and nothing once the parcel has been picked up.
    /// </summary>
    public decimal RefundIfCancelled => CancellationPolicy.RefundAmount(ServerStatus, TotalFee);
}
