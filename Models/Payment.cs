namespace Droppa.Models;

/// <summary>How the customer pays for a delivery.</summary>
public enum PaymentMethod
{
    AirtelMoney,
    TnmMpamba,
    VisaCard
}

/// <summary>
/// The credentials the customer enters to pay. Only the fields relevant to the chosen
/// <see cref="PaymentMethod"/> are populated (mobile number for mobile money, card fields for Visa).
/// </summary>
public class PaymentDetails
{
    public string? MobileNumber { get; set; }

    /// <summary>Mobile money PIN entered by the customer to authorise the charge (Airtel Money / TNM Mpamba).</summary>
    public string? Pin { get; set; }

    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }   // "MM/YY"
    public string? CardCvv { get; set; }
}

/// <summary>Outcome of a payment attempt from the gateway.</summary>
public record PaymentResult(bool Success, string? TransactionId, string? Message)
{
    public static PaymentResult Ok(string transactionId) => new(true, transactionId, null);
    public static PaymentResult Failed(string message) => new(false, null, message);
}
