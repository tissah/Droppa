namespace Droppa.Models;

/// <summary>
/// Rules for a customer cancelling a delivery before it's completed.
///
/// A trip can be cancelled any time before the parcel is delivered. What the customer gets back
/// depends on how far the trip has progressed when they cancel:
///  • Before the parcel is picked up — the trip charge is refunded less a 30% cancellation fee.
///  • After pickup — no refund (the driver is already carrying the item).
/// </summary>
public static class CancellationPolicy
{
    /// <summary>Percentage of the trip charge kept as a fee when cancelling before pickup.</summary>
    public const decimal CancellationFeePercent = 30m;

    // API DeliveryStatus codes: 5 = parcel collected (the pickup point), 8 = delivered.
    private const int ParcelCollected = 5;
    private const int Delivered = 8;

    /// <summary>True while the trip can still be cancelled — not yet delivered and not already cancelled.</summary>
    public static bool CanCancel(int serverStatus) =>
        serverStatus != Delivered && !OrderTimeline.IsCancelled(serverStatus);

    /// <summary>
    /// True once the driver has collected the parcel. After this point a cancellation is
    /// non-refundable.
    /// </summary>
    public static bool IsPickedUp(int serverStatus) =>
        !OrderTimeline.IsCancelled(serverStatus) && serverStatus >= ParcelCollected;

    /// <summary>
    /// The amount (MWK) refunded to the customer if they cancel now, given the trip charge they paid.
    /// Before pickup: the charge less the 30% cancellation fee. After pickup: nothing.
    /// </summary>
    public static decimal RefundAmount(int serverStatus, decimal tripCharge)
    {
        if (tripCharge <= 0 || IsPickedUp(serverStatus)) return 0m;
        var refund = tripCharge * (1 - CancellationFeePercent / 100m);
        return Math.Round(refund, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>The 30% cancellation fee kept when cancelling before pickup (0 once picked up).</summary>
    public static decimal CancellationFee(int serverStatus, decimal tripCharge)
    {
        if (tripCharge <= 0 || IsPickedUp(serverStatus)) return 0m;
        return tripCharge - RefundAmount(serverStatus, tripCharge);
    }
}
