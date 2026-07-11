using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

/// <summary>
/// Drives the in-page payment panel shown after a quote. The customer picks a method
/// (Airtel Money, TNM Mpamba or Visa card), enters their details and pays; only once
/// <see cref="IsPaid"/> is true may the host page submit the booking. Embedded as a
/// <c>Payment</c> property on the Send/Receive view models so both flows share one implementation.
/// </summary>
public partial class PaymentViewModel : ObservableObject
{
    public const string AirtelMoney = "Airtel Money";
    public const string TnmMpamba = "TNM Mpamba";
    public const string VisaCard = "Visa card";

    private readonly IPaymentService _payments;
    private readonly IAuthService _auth;

    public PaymentViewModel(IPaymentService payments, IAuthService auth)
    {
        _payments = payments;
        _auth = auth;
    }

    /// <summary>
    /// The payment methods the customer may use. The mobile money option is fixed by the
    /// network of their registered number (09… = Airtel Money, 08… = TNM Mpamba) — they can't
    /// switch networks — and Visa card is always available. Recomputed on each <see cref="Reset"/>.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<string> _methods = [AirtelMoney, TnmMpamba, VisaCard];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMobileMoney))]
    [NotifyPropertyChangedFor(nameof(IsCard))]
    private string _selectedMethod = AirtelMoney;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PayButtonText))]
    private decimal _amount;

    // Mobile money
    [ObservableProperty] private string _mobileNumber = string.Empty;

    // Card
    [ObservableProperty] private string _cardNumber = string.Empty;
    [ObservableProperty] private string _cardExpiry = string.Empty;
    [ObservableProperty] private string _cardCvv = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPaid))]
    private bool _isPaid;

    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _transactionReference;

    public bool IsNotPaid => !IsPaid;
    public bool IsMobileMoney => SelectedMethod is AirtelMoney or TnmMpamba;
    public bool IsCard => SelectedMethod == VisaCard;
    public string PayButtonText => $"Pay MWK {Amount:N0}";

    /// <summary>Raised after a successful payment so the host view model can react.</summary>
    public event Action? Paid;

    /// <summary>
    /// Reset the panel for a freshly-quoted amount (clears any previous payment).
    /// The mobile money number is pre-filled from the account the customer enrolled with —
    /// they never type it in — and shown read-only in the panel.
    /// </summary>
    public void Reset(decimal amount)
    {
        Amount = amount;
        IsPaid = false;
        IsProcessing = false;
        StatusMessage = null;
        TransactionReference = null;
        MobileNumber = NormalizeMsisdn(_auth.CurrentUser?.Phone);
        CardNumber = string.Empty;
        CardExpiry = string.Empty;
        CardCvv = string.Empty;

        // Offer only the wallet that matches the registered number, plus Visa card. When there's
        // no usable mobile money number on file, Visa card is the only option.
        Methods = NetworkFor(MobileNumber) switch
        {
            PaymentMethod.AirtelMoney => [AirtelMoney, VisaCard],
            PaymentMethod.TnmMpamba => [TnmMpamba, VisaCard],
            _ => [VisaCard]
        };
        SelectedMethod = Methods[0];
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        if (IsProcessing || IsPaid) return;

        var method = SelectedMethod switch
        {
            AirtelMoney => PaymentMethod.AirtelMoney,
            TnmMpamba => PaymentMethod.TnmMpamba,
            _ => PaymentMethod.VisaCard
        };

        // Mobile money requires the customer to authorise the charge with their PIN.
        string? pin = null;
        if (IsMobileMoney)
        {
            // The customer pays from the number they enrolled with; its prefix fixes the network
            // (09… = Airtel Money, 08… = TNM Mpamba) and therefore the wallet and PIN — mobile
            // money is only ever offered when the registered number is a valid Airtel/TNM number.
            var network = NetworkFor(MobileNumber);
            if (network is null)
            {
                StatusMessage = "No valid mobile money number on your profile. Pay by Visa card instead.";
                return;
            }

            var label = network == PaymentMethod.AirtelMoney ? AirtelMoney : TnmMpamba;
            pin = await Shell.Current.DisplayPromptAsync(
                $"{label} PIN",
                $"Enter your {label} PIN to authorise MWK {Amount:N0}.",
                accept: "Pay",
                cancel: "Cancel",
                placeholder: "PIN",
                maxLength: 4,
                keyboard: Keyboard.Numeric);

            if (pin is null)
            {
                StatusMessage = "Payment cancelled.";
                return;
            }
            if (pin.Length != 4 || !pin.All(char.IsDigit))
            {
                StatusMessage = "Enter your 4-digit mobile money PIN.";
                return;
            }
        }

        var details = new PaymentDetails
        {
            MobileNumber = MobileNumber,
            Pin = pin,
            CardNumber = CardNumber,
            CardExpiry = CardExpiry,
            CardCvv = CardCvv
        };

        try
        {
            IsProcessing = true;
            StatusMessage = "Processing payment…";

            var result = await _payments.PayAsync(method, Amount, details);
            if (result.Success)
            {
                IsPaid = true;
                TransactionReference = result.TransactionId;
                StatusMessage = $"Payment received · {result.TransactionId}";
                Paid?.Invoke();
            }
            else
            {
                StatusMessage = result.Message ?? "Payment failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Normalises a stored phone number to a local 10-digit MSISDN (e.g. "0991234567"),
    /// converting a +265 / 265 country code to a leading 0. Returns empty when there's nothing usable.
    /// </summary>
    private static string NormalizeMsisdn(string? phone)
    {
        var digits = string.IsNullOrEmpty(phone) ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("265") && digits.Length == 12)
            digits = "0" + digits[3..];
        return digits;
    }

    /// <summary>
    /// The mobile money network a number belongs to, from its prefix: 09… = Airtel Money,
    /// 08… = TNM Mpamba. Returns null for anything that isn't a valid 10-digit 08/09 number.
    /// </summary>
    private static PaymentMethod? NetworkFor(string? mobileNumber)
    {
        var digits = NormalizeMsisdn(mobileNumber);
        if (digits.Length != 10) return null;
        if (digits.StartsWith("09")) return PaymentMethod.AirtelMoney;
        if (digits.StartsWith("08")) return PaymentMethod.TnmMpamba;
        return null;
    }
}
