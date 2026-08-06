using System.Text.Json.Serialization;

namespace Droppa.Services.Api;

// Request/response shapes matching the Droppa ASP.NET Core API.
// Deserialization is case-insensitive, so these PascalCase names map the API's camelCase JSON.

public record RegisterRequestDto(string FullName, string Email, string Password, string? PhoneNumber, string? District);
public record LoginRequestDto(string Email, string Password);
public record RefreshRequestDto(string RefreshToken);

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public ApiUserDto User { get; set; } = new();
}

public class ApiUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }

    // The API sends the resident district as districtId/districtName, so these must match
    // those names — a property called "District" gets nothing and reads back as null.
    public int? DistrictId { get; set; }
    public string? DistrictName { get; set; }

    /// <summary>The customer's resident district (Malawi), as the rest of the app refers to it.</summary>
    [JsonIgnore]
    public string? District => DistrictName;

    public List<string> Roles { get; set; } = [];
}

/// <summary>A district Droppa serves, from <c>GET /api/districts</c>.</summary>
public class DistrictDto
{
    public int Id { get; set; }
    public string DistrictName { get; set; } = string.Empty;

    /// <summary>1 = Droppa serves this district, 0 = closed.</summary>
    public int IsDistrictAllowed { get; set; }

    public int BranchCount { get; set; }
    public int CourierServicesCount { get; set; }
}

/// <summary>
/// A courier company from <c>GET /api/courier-services</c>. The company record carries no
/// location — offices are branches, so <see cref="CourierBranchDto"/> is what the pickers need.
/// </summary>
public class CourierServiceDto
{
    public int Id { get; set; }
    public string CourierName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>How many branches this courier has registered, across all districts.</summary>
    public int BranchCount { get; set; }
}

/// <summary>
/// A single branch (office) of a courier service, from <c>GET /api/courier-branches</c>. Each
/// branch names its own courier and district, so one call gives both pickers their contents.
/// </summary>
public class CourierBranchDto
{
    public int Id { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public int CourierId { get; set; }
    public string CourierName { get; set; } = string.Empty;

    /// <summary>The Malawi district this branch is in; couriers are filtered to the customer's district.</summary>
    public int DistrictId { get; set; }
    public string DistrictName { get; set; } = string.Empty;

    public string? Address { get; set; }

    // Note the plurals — the API's own property names.
    public double Latitudes { get; set; }
    public double Longitudes { get; set; }

    public string? ContactPhoneNumber { get; set; }
    public string? Email { get; set; }

    /// <summary>Mobile money number the branch uses to receive driver remittances.</summary>
    public string? PaymentPhoneNumber { get; set; }

    public bool IsActive { get; set; }
}

public class PricingDto
{
    public decimal CostPerKm { get; set; }
    public decimal BaseFee { get; set; }
    public decimal MinimumFee { get; set; }
    public decimal PromoDiscountPercent { get; set; }
    public string Currency { get; set; } = "MWK";
}

public class QuoteDto
{
    public double DistanceKm { get; set; }
    public decimal RatePerKm { get; set; }
    public decimal BaseFee { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalFee { get; set; }
    public string Currency { get; set; } = "MWK";
}

/// <summary>One parcel within a multi-parcel send: item details, weight and its own receiver.</summary>
public class SendParcelItemDto
{
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SpecialInstructions { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
}

public class CreateSendDto
{
    public int CourierServiceId { get; set; }

    /// <summary>The chosen branch of the courier, when it has branches.</summary>
    public int? CourierBranchId { get; set; }

    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public string? PickupAddress { get; set; }

    // Primary parcel — kept for the single-parcel API contract; mirrors the first entry of Parcels.
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SpecialInstructions { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }

    /// <summary>All parcels in the booking, each to its own receiver.</summary>
    public List<SendParcelItemDto> Parcels { get; set; } = [];
}

/// <summary>One parcel within a multi-parcel receive: its sender and proof of parcel.</summary>
public class ReceiveParcelItemDto
{
    public string? SenderName { get; set; }
    public string? WaybillNumber { get; set; }
    public string? ReceiptImageUrl { get; set; }
}

public class CreateReceiveDto
{
    public int CourierServiceId { get; set; }

    /// <summary>The chosen branch of the courier, when it has branches.</summary>
    public int? CourierBranchId { get; set; }

    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public string? DestinationAddress { get; set; }

    /// <summary>Amount the customer owes at the courier office (COD / handling); remitted by the driver on collection.</summary>
    public decimal CourierAmount { get; set; }

    // Primary parcel — kept for the single-parcel API contract; mirrors the first entry of Parcels.
    public string? WaybillNumber { get; set; }
    public string? ReceiptImageUrl { get; set; }

    /// <summary>All parcels being collected, each from its own sender.</summary>
    public List<ReceiveParcelItemDto> Parcels { get; set; } = [];
}

public class DeliveryDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int ServiceType { get; set; }
    public int Status { get; set; }
    public string CourierServiceName { get; set; } = string.Empty;
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public double DistanceKm { get; set; }
    public decimal TotalFee { get; set; }

    /// <summary>
    /// Receive only: the amount the customer entered as owed at the courier office (COD / handling).
    /// This — not <see cref="TotalFee"/> — is what the driver remits to the courier. Null/0 for Send.
    /// </summary>
    public decimal? CourierAmount { get; set; }

    public int? AssignedDriverId { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? DriverPhotoUrl { get; set; }
    public string? MotorcycleRegistration { get; set; }
    public string? MotorcycleMakeModel { get; set; }

    // ---- Parcel (weight) charge, set by the driver after weighing; paid separately by the customer ----
    public double? ParcelWeightGrams { get; set; }
    public decimal? ParcelCharge { get; set; }
    public bool ParcelChargePaid { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// ---- Driver-side shapes (match Droppa.Application.DTOs; enums serialize as ints) ----

/// <summary>An open delivery request shown on the driver's job board.</summary>
public class DriverJobDto
{
    public int DeliveryRequestId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int ServiceType { get; set; }
    public int Status { get; set; }
    public string CourierServiceName { get; set; } = string.Empty;
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public double DistanceKm { get; set; }
    public decimal TotalFee { get; set; }
    public double? DistanceFromDriverKm { get; set; }
}

/// <summary>The signed-in driver's profile and assigned motorcycle.</summary>
public class DriverProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? LicenseNumber { get; set; }
    public int Status { get; set; }
    public double? Rating { get; set; }
    public string? MotorcycleRegistration { get; set; }
    public string? MotorcycleMakeModel { get; set; }
}

public class UpdateLocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Heading { get; set; }
    public double? SpeedKph { get; set; }
    public int? DeliveryRequestId { get; set; }
}

public record RideActionDto(int DeliveryRequestId);

public class UpdateDeliveryStatusDto
{
    public int DeliveryRequestId { get; set; }
    public int Status { get; set; }
    public string? Note { get; set; }
}

/// <summary>Driver submits the weighed parcel and the resulting charge (incl. VAT) for the customer to pay.</summary>
public class SetParcelWeightDto
{
    public int DeliveryRequestId { get; set; }
    public double WeightGrams { get; set; }
    public decimal ParcelCharge { get; set; }
}

/// <summary>Customer's payment for the weight-based parcel charge (the second payment).</summary>
public class ParcelPaymentDto
{
    public int DeliveryRequestId { get; set; }
    public string? TransactionId { get; set; }
}

/// <summary>Live tracking snapshot shown to the customer.</summary>
public class TrackingDto
{
    public int DeliveryRequestId { get; set; }
    public int Status { get; set; }
    public double? DriverLatitude { get; set; }
    public double? DriverLongitude { get; set; }
    public double? EtaMinutes { get; set; }
    public double? RemainingDistanceKm { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
