using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Services;
using Droppa.Services.Api;

namespace Droppa.ViewModels;

/// <summary>
/// Backs the parcel-fee confirmation popup. When the driver weighs a parcel and sends its fee,
/// this shows the weight and the charge breakdown (weight amount + VAT) and lets the customer
/// pay the extra amount. On a successful payment the charge is recorded against the delivery.
/// </summary>
public partial class ParcelChargeViewModel : ObservableObject
{
    private readonly DroppaApiClient _api;

    public ParcelChargeViewModel(DroppaApiClient api, PaymentViewModel payment)
    {
        _api = api;
        Payment = payment;
    }

    /// <summary>Payment panel for the weight-based parcel charge (the customer's second payment).</summary>
    public PaymentViewModel Payment { get; }

    [ObservableProperty] private int _deliveryId;
    [ObservableProperty] private string _reference = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WeightText))]
    private double _weightGrams;

    /// <summary>Pre-VAT weight charge.</summary>
    [ObservableProperty] private decimal _weightAmount;

    /// <summary>VAT applied to the weight charge.</summary>
    [ObservableProperty] private decimal _vat;

    /// <summary>Total fee the customer pays (weight amount + VAT) — authoritative from the server.</summary>
    [ObservableProperty] private decimal _parcelCharge;

    [ObservableProperty] private string? _statusMessage;

    public string WeightText => $"{WeightGrams:N0} g";

    /// <summary>Raised when the popup should close. <c>true</c> when the fee was paid.</summary>
    public event Action<bool>? RequestClose;

    /// <summary>Primes the popup from the delivery the driver just sent a fee for.</summary>
    public void Load(DeliveryDto d)
    {
        DeliveryId = d.Id;
        Reference = d.Reference;
        WeightGrams = d.ParcelWeightGrams ?? 0;
        ParcelCharge = d.ParcelCharge ?? 0m;

        // Show the pre-VAT/VAT split from current pricing; the total stays the server's figure.
        WeightAmount = ParcelPricing.WeightAmount(WeightGrams);
        Vat = ParcelCharge - WeightAmount;
        if (Vat < 0) Vat = ParcelPricing.Vat(WeightAmount);

        StatusMessage = null;
        Payment.Paid -= OnPaid;
        Payment.Paid += OnPaid;
        Payment.Reset(ParcelCharge);
    }

    /// <summary>Dismiss without paying — the fee can still be paid later on the tracking screen.</summary>
    [RelayCommand]
    private void Later()
    {
        Payment.Paid -= OnPaid;
        RequestClose?.Invoke(false);
    }

    /// <summary>The customer paid — record it against the delivery, then close.</summary>
    private async void OnPaid()
    {
        try
        {
            await _api.PayParcelChargeAsync(new ParcelPaymentDto
            {
                DeliveryRequestId = DeliveryId,
                TransactionId = Payment.TransactionReference
            });
            Payment.Paid -= OnPaid;
            StatusMessage = $"Parcel fee paid · {Payment.TransactionReference}";
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
