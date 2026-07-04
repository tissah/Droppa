using Droppa.Models;
using Droppa.Services.Api;

namespace Droppa.Services;

/// <summary>
/// Creates and lists bookings through the Droppa API. "Send" quotes are authoritative
/// (server distance via Google Distance Matrix). "Receive" quotes are estimated locally
/// from current pricing + straight-line distance; the confirmed fee comes from the server.
/// </summary>
public class ApiBookingService : IBookingService
{
    private readonly DroppaApiClient _api;
    private readonly IDistanceService _distance;

    public ApiBookingService(DroppaApiClient api, IDistanceService distance)
    {
        _api = api;
        _distance = distance;
    }

    public async Task<Booking> QuoteAsync(
        ServiceType serviceType, CourierService courier, IReadOnlyList<Parcel> parcels,
        GeoLocation pickup, GeoLocation destination, CancellationToken ct = default)
    {
        if (parcels.Count == 0)
            throw new ArgumentException("A booking must have at least one parcel.", nameof(parcels));

        var booking = new Booking
        {
            ServiceType = serviceType,
            Courier = courier,
            Parcels = parcels.ToList(),
            Parcel = parcels[0],
            Pickup = pickup,
            Destination = destination,
            Status = DeliveryStatus.Pending,
            StatusText = "Pending"
        };

        if (serviceType == ServiceType.SendParcel)
        {
            // The distance-based ride fee is charged once for the route, independent of the
            // number of parcels. Each parcel's weight charge is added later by the driver and
            // paid as a separate, second payment.
            var quote = await _api.QuoteSendAsync(ToSendDto(booking), ct);
            booking.DistanceKm = quote.DistanceKm;
            booking.RatePerKm = quote.RatePerKm;
            booking.TotalFee = quote.TotalFee;
        }
        else
        {
            // No server quote endpoint for receive — estimate with current pricing + local distance.
            var pricing = await _api.GetPricingAsync(ct);
            var km = await _distance.GetDistanceKmAsync(pickup, destination, ct);
            booking.DistanceKm = Math.Round(km, 2);
            booking.RatePerKm = pricing.CostPerKm;
            booking.TotalFee = EstimateFee(km, pricing);
        }

        return booking;
    }

    public async Task ConfirmAsync(Booking booking, CancellationToken ct = default)
    {
        DeliveryDto created = booking.ServiceType == ServiceType.SendParcel
            ? await _api.CreateSendAsync(ToSendDto(booking), ct)
            : await _api.CreateReceiveAsync(ToReceiveDto(booking), ct);

        // Reflect the authoritative server result back onto the booking.
        booking.DeliveryId = created.Id;
        booking.Reference = created.Reference;
        booking.DistanceKm = created.DistanceKm;
        booking.TotalFee = created.TotalFee;
        booking.ServerStatus = created.Status;
        booking.StatusText = StatusText(created.Status);
    }

    public async Task CancelAsync(Booking booking, CancellationToken ct = default)
    {
        var updated = await _api.CancelDeliveryAsync(booking.DeliveryId, ct);
        booking.ServerStatus = updated.Status;
        booking.StatusText = StatusText(updated.Status);
        booking.Status = DeliveryStatus.Cancelled;
    }

    public async Task<IReadOnlyList<Booking>> GetHistoryAsync(CancellationToken ct = default)
    {
        var deliveries = await _api.GetDeliveriesAsync(ct);
        return deliveries.Select(d => new Booking
        {
            DeliveryId = d.Id,
            Reference = d.Reference,
            ServiceType = d.ServiceType == 2 ? ServiceType.ReceiveParcel : ServiceType.SendParcel,
            Courier = new CourierService { Name = d.CourierServiceName },
            DistanceKm = d.DistanceKm,
            TotalFee = d.TotalFee,
            ServerStatus = d.Status,
            StatusText = StatusText(d.Status),
            CreatedAt = d.CreatedAt
        }).ToList();
    }

    private static CreateSendDto ToSendDto(Booking b) => new()
    {
        CourierServiceId = b.Courier.Id,
        PickupLatitude = b.Pickup.Latitude,
        PickupLongitude = b.Pickup.Longitude,
        PickupAddress = b.Pickup.Address,
        ItemName = b.Parcel.ItemName,
        Description = b.Parcel.Description,
        Category = b.Parcel.Category,
        Quantity = b.Parcel.Quantity,
        SpecialInstructions = b.Parcel.SpecialInstructions,
        ReceiverName = b.Parcel.ReceiverName,
        ReceiverPhone = b.Parcel.ReceiverPhone,
        Parcels = b.Parcels.Select(p => new SendParcelItemDto
        {
            ItemName = p.ItemName,
            Description = p.Description,
            Category = p.Category,
            Quantity = p.Quantity,
            SpecialInstructions = p.SpecialInstructions,
            ReceiverName = p.ReceiverName,
            ReceiverPhone = p.ReceiverPhone
        }).ToList()
    };

    private static CreateReceiveDto ToReceiveDto(Booking b) => new()
    {
        CourierServiceId = b.Courier.Id,
        DestinationLatitude = b.Destination.Latitude,
        DestinationLongitude = b.Destination.Longitude,
        DestinationAddress = b.Destination.Address,
        WaybillNumber = b.Parcel.WaybillNumber,
        ReceiptImageUrl = b.Parcel.ReceiptImagePath,
        Parcels = b.Parcels.Select(p => new ReceiveParcelItemDto
        {
            SenderName = p.SenderName,
            WaybillNumber = p.WaybillNumber,
            ReceiptImageUrl = p.ReceiptImagePath
        }).ToList()
    };

    private static decimal EstimateFee(double km, PricingDto p)
    {
        var raw = p.BaseFee + (decimal)km * p.CostPerKm;
        var floored = Math.Max(raw, p.MinimumFee);
        var discounted = floored * (1 - p.PromoDiscountPercent / 100m);
        return Math.Round(discounted, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Maps the API's numeric DeliveryStatus to a display string.</summary>
    private static string StatusText(int status) => status switch
    {
        0 => "Pending",
        1 => "Driver assigned",
        2 => "Accepted",
        3 => "Rejected",
        4 => "Pickup in progress",
        5 => "Parcel collected",
        6 => "In transit",
        7 => "Arriving",
        8 => "Delivered",
        9 => "Cancelled",
        _ => "Unknown"
    };
}
