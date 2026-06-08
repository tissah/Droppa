using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Charges the customer for a delivery before the booking is submitted. The scaffold ships a
/// <see cref="MockPaymentService"/>; swap in a real Airtel Money / TNM Mpamba / card gateway later.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Attempts to collect <paramref name="amount"/> (MWK) using the given method and details.
    /// Returns a successful result with a transaction id, or a failed result with a reason.
    /// </summary>
    Task<PaymentResult> PayAsync(
        PaymentMethod method,
        decimal amount,
        PaymentDetails details,
        CancellationToken ct = default);

    /// <summary>
    /// Remits <paramref name="amount"/> (MWK) to a courier's mobile money number — the fee the
    /// courier charged the customer, transferred by the driver. Returns a successful result with a
    /// transaction id, or a failed result with a reason.
    /// </summary>
    Task<PaymentResult> TransferAsync(
        string recipientMobileNumber,
        decimal amount,
        CancellationToken ct = default);

    /// <summary>
    /// Refunds <paramref name="amount"/> (MWK) to the customer for a cancelled trip — the trip charge
    /// less any cancellation fee. Returns a successful result with a refund reference, or a failed
    /// result with a reason.
    /// </summary>
    Task<PaymentResult> RefundAsync(
        decimal amount,
        CancellationToken ct = default);
}
