using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// A stand-in payment gateway for development and demos. It validates the entered details the
/// way a real Airtel Money / TNM Mpamba / Visa gateway would, simulates a short processing delay,
/// then approves the charge — except for a couple of well-known "test" inputs that it declines so
/// the failure path can be exercised. No money moves and nothing leaves the device.
/// </summary>
public sealed class MockPaymentService : IPaymentService
{
    private static readonly TimeSpan ProcessingDelay = TimeSpan.FromMilliseconds(1200);

    // A test mobile number / card the demo can use to force a decline.
    private const string DeclineMobileSuffix = "0000";
    private const string DeclineCard = "4000000000000002";

    public async Task<PaymentResult> PayAsync(
        PaymentMethod method,
        decimal amount,
        PaymentDetails details,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return PaymentResult.Failed("Nothing to pay — get a quote first.");

        var validation = method switch
        {
            PaymentMethod.AirtelMoney => ValidateMobile(details.MobileNumber, details.Pin, "Airtel Money", "08, 09"),
            PaymentMethod.TnmMpamba => ValidateMobile(details.MobileNumber, details.Pin, "TNM Mpamba", "08, 09"),
            PaymentMethod.VisaCard => ValidateCard(details),
            _ => "Unsupported payment method."
        };

        if (validation is not null)
            return PaymentResult.Failed(validation);

        // Simulate the round-trip to the gateway.
        await Task.Delay(ProcessingDelay, ct);

        // Simulated declines for the documented test inputs.
        if (method is PaymentMethod.AirtelMoney or PaymentMethod.TnmMpamba
            && Digits(details.MobileNumber).EndsWith(DeclineMobileSuffix))
        {
            return PaymentResult.Failed("Payment declined by the mobile money provider.");
        }
        if (method == PaymentMethod.VisaCard && Digits(details.CardNumber) == DeclineCard)
        {
            return PaymentResult.Failed("Card declined. Please try a different card.");
        }

        return PaymentResult.Ok(GenerateTransactionId(method));
    }

    public async Task<PaymentResult> TransferAsync(
        string recipientMobileNumber,
        decimal amount,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return PaymentResult.Failed("Nothing to transfer.");

        var digits = Digits(recipientMobileNumber);
        // Accept a +265 country code by normalising it to a leading 0.
        if (digits.StartsWith("265") && digits.Length == 12)
            digits = "0" + digits[3..];

        if (digits.Length != 10 || !(digits.StartsWith("08") || digits.StartsWith("09")))
            return PaymentResult.Failed("The courier has no valid mobile money number on file.");

        // Simulate the round-trip to the payout gateway.
        await Task.Delay(ProcessingDelay, ct);

        // Simulated decline for the documented test number.
        if (digits.EndsWith(DeclineMobileSuffix))
            return PaymentResult.Failed("Transfer declined by the mobile money provider.");

        return PaymentResult.Ok($"PAYOUT-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}");
    }

    public async Task<PaymentResult> RefundAsync(
        decimal amount,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            return PaymentResult.Failed("Nothing to refund.");

        // Simulate the round-trip to the gateway crediting the customer back.
        await Task.Delay(ProcessingDelay, ct);

        return PaymentResult.Ok($"REFUND-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}");
    }

    private static string? ValidateMobile(string? number, string? pin, string label, string prefixes)
    {
        var digits = Digits(number);
        // Accept a +265 country code by normalising it to a leading 0.
        if (digits.StartsWith("265") && digits.Length == 12)
            digits = "0" + digits[3..];

        if (digits.Length != 10 || !(digits.StartsWith("08") || digits.StartsWith("09")))
            return $"Enter a valid {label} number (10 digits starting with {prefixes}).";

        if (Digits(pin).Length != 4)
            return $"Enter your 4-digit {label} PIN.";

        return null;
    }

    private static string? ValidateCard(PaymentDetails details)
    {
        var pan = Digits(details.CardNumber);
        if (pan.Length is < 13 or > 19)
            return "Enter a valid card number.";
        if (!pan.StartsWith('4'))
            return "Only Visa cards (starting with 4) are accepted.";

        if (!IsValidExpiry(details.CardExpiry))
            return "Enter a valid expiry date as MM/YY.";

        var cvv = Digits(details.CardCvv);
        if (cvv.Length is not (3 or 4))
            return "Enter the 3-digit CVV from the back of the card.";

        return null;
    }

    private static bool IsValidExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return false;
        var parts = expiry.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var month) || month is < 1 or > 12) return false;
        if (!int.TryParse(parts[1], out var year)) return false;

        // Treat "YY" as 20YY and require the card not to be already expired.
        var fullYear = 2000 + year;
        var now = DateTime.Now;
        var lastValid = new DateTime(fullYear, month, 1).AddMonths(1).AddDays(-1);
        return lastValid >= now.Date;
    }

    private static string Digits(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    private static string GenerateTransactionId(PaymentMethod method)
    {
        var prefix = method switch
        {
            PaymentMethod.AirtelMoney => "AIRTEL",
            PaymentMethod.TnmMpamba => "MPAMBA",
            _ => "VISA"
        };
        return $"{prefix}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
    }
}
