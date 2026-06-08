namespace Droppa.Services.Api;

// Request/response shapes matching the Droppa ASP.NET Core API.
// Deserialization is case-insensitive, so these PascalCase names map the API's camelCase JSON.

public record RegisterRequestDto(string FullName, string Email, string Password, string? PhoneNumber);
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
    public List<string> Roles { get; set; } = [];
}

public class CourierServiceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Mobile money number the courier uses to receive driver remittances.</summary>
    public string? PhoneNumber { get; set; }

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

public class CreateSendDto
{
    public int CourierServiceId { get; set; }
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public string? PickupAddress { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int Quantity { get; set; } = 1;
    public string? SpecialInstructions { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
}

public class CreateReceiveDto
{
    public int CourierServiceId { get; set; }
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public string? DestinationAddress { get; set; }
    public string? WaybillNumber { get; set; }
    public string? ReceiptImageUrl { get; set; }
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
