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

    public PaymentViewModel(IPaymentService payments)
    {
        _payments = payments;
    }

    public IReadOnlyList<string> Methods { get; } = [AirtelMoney, TnmMpamba, VisaCard];

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

    /// <summary>Reset the panel for a freshly-quoted amount (clears any previous payment).</summary>
    public void Reset(decimal amount)
    {
        Amount = amount;
        IsPaid = false;
        IsProcessing = false;
        StatusMessage = null;
        TransactionReference = null;
        MobileNumber = string.Empty;
        CardNumber = string.Empty;
        CardExpiry = string.Empty;
        CardCvv = string.Empty;
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
            pin = await Shell.Current.DisplayPromptAsync(
                $"{SelectedMethod} PIN",
                $"Enter your {SelectedMethod} PIN to authorise MWK {Amount:N0}.",
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
}
