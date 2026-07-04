using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Orchestrates creating a booking: computes distance + fee, then persists it.
/// In-memory in the scaffold; back it with the Node/Express API + DB later.
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Builds a priced booking for the given route and parcels. Does not yet persist it —
    /// call <see cref="ConfirmAsync"/> after the user confirms the summary. The distance/ride
    /// fee is computed once for the route, regardless of how many parcels are sent; the
    /// weight-based parcel charge is added later by the driver and paid separately.
    /// </summary>
    Task<Booking> QuoteAsync(
        ServiceType serviceType,
        CourierService courier,
        IReadOnlyList<Parcel> parcels,
        GeoLocation pickup,
        GeoLocation destination,
        CancellationToken ct = default);

    Task ConfirmAsync(Booking booking, CancellationToken ct = default);

    /// <summary>
    /// Cancels a delivery that hasn't been delivered yet and reflects the new (cancelled) status
    /// back onto the booking. The refund, if any, is handled separately by the caller.
    /// </summary>
    Task CancelAsync(Booking booking, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> GetHistoryAsync(CancellationToken ct = default);
}
