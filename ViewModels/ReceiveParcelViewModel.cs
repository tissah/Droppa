using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

/// <summary>
/// Receive flow (spec 2B): pickup = selected courier office, destination = current GPS location.
/// Requires either a waybill number or an uploaded receipt image.
/// </summary>
public partial class ReceiveParcelViewModel : BaseViewModel
{
    private readonly ICourierRepository _couriers;
    private readonly ILocationService _location;
    private readonly IBookingService _booking;
    private readonly IAuthService _auth;

    public ReceiveParcelViewModel(ICourierRepository couriers, ILocationService location,
        IBookingService booking, PaymentViewModel payment, IAuthService auth)
    {
        _couriers = couriers;
        _location = location;
        _booking = booking;
        _auth = auth;
        Payment = payment;
        Title = "Receive a parcel";
    }

    /// <summary>Full name of the signed-in user, shown in the top-right of the page header.</summary>
    public string UserName => _auth.CurrentUser?.FullName ?? "Guest";

    /// <summary>Drop-off destination options. "Current location" reads the device GPS when chosen.</summary>
    public ObservableCollection<string> DropoffOptions { get; } = ["Current location"];

    [ObservableProperty] private string? _selectedDropoffOption;

    partial void OnSelectedDropoffOptionChanged(string? value)
    {
        if (value == "Current location")
            _ = CaptureDestinationAsync();
    }

    [RelayCommand]
    private async Task CaptureDestinationAsync()
    {
        StatusMessage = "Getting your location…";
        DestinationLocation = await _location.GetCurrentLocationAsync()
                              ?? LocationService.LilongweCentre;
        StatusMessage = $"Drop-off: {DestinationLocation}";
    }

    /// <summary>Payment panel shown with the quote; the booking can't be confirmed until it's paid.</summary>
    public PaymentViewModel Payment { get; }

    public ObservableCollection<CourierService> Couriers { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Couriers.Count > 0) return;
        try
        {
            IsBusy = true;
            var list = await _couriers.GetAllAsync();
            foreach (var c in list) Couriers.Add(c);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty] private CourierService? _selectedCourier;
    [ObservableProperty] private string? _waybillNumber;
    [ObservableProperty] private string? _receiptImagePath;
    [ObservableProperty] private GeoLocation? _destinationLocation;

    /// <summary>Mandatory: the amount the customer must settle at the courier office (e.g. COD / handling).</summary>
    [ObservableProperty] private string? _courierAmountText;

    /// <summary>Parsed courier-office charge that gets added to the delivery fee.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrandTotal))]
    private decimal _courierAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    [NotifyPropertyChangedFor(nameof(GrandTotal))]
    private Booking? _quote;

    [ObservableProperty] private string? _statusMessage;

    public bool HasQuote => Quote is not null;

    /// <summary>Total the customer pays: delivery fee + the amount due at the courier office.</summary>
    public decimal GrandTotal => (Quote?.TotalFee ?? 0m) + CourierAmount;

    [RelayCommand]
    private async Task PickReceiptAsync()
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo is not null)
            {
                ReceiptImagePath = photo.FullPath;
                StatusMessage = $"Receipt attached: {photo.FileName}";
            }
        }
        catch (FeatureNotSupportedException)
        {
            StatusMessage = "Photo picking isn't supported on this device.";
        }
    }

    [RelayCommand]
    private async Task GetQuoteAsync()
    {
        if (IsBusy) return;

        if (SelectedCourier is null)
        {
            StatusMessage = "Please choose the courier holding your parcel.";
            return;
        }
        if (string.IsNullOrWhiteSpace(WaybillNumber) && string.IsNullOrWhiteSpace(ReceiptImagePath))
        {
            StatusMessage = "Enter a waybill number or attach a receipt image.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CourierAmountText) ||
            !decimal.TryParse(CourierAmountText, out var courierAmount) || courierAmount < 0)
        {
            StatusMessage = "Enter the amount to be paid at the courier service.";
            return;
        }

        try
        {
            IsBusy = true;
            CourierAmount = courierAmount;
            DestinationLocation ??= await _location.GetCurrentLocationAsync() ?? LocationService.LilongweCentre;

            var parcel = new Parcel
            {
                WaybillNumber = WaybillNumber,
                ReceiptImagePath = ReceiptImagePath
            };

            Quote = await _booking.QuoteAsync(
                ServiceType.ReceiveParcel,
                SelectedCourier,
                parcel,
                SelectedCourier.Office,
                DestinationLocation);

            // A new quote means a new amount due — reset any earlier payment.
            // The customer pays the delivery fee plus the amount owed at the courier office.
            Payment.Reset(GrandTotal);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Quote is null) return;
        if (!Payment.IsPaid)
        {
            StatusMessage = "Please complete payment before confirming the booking.";
            return;
        }

        await _booking.ConfirmAsync(Quote);
        var reference = Quote.Reference;
        var transaction = Payment.TransactionReference;
        Quote = null;
        Payment.Reset(0);
        await Shell.Current.DisplayAlert("Booking created",
            $"Your delivery {reference} has been requested and paid (ref {transaction}). " +
            "A rider will be assigned shortly.", "OK");
        await Shell.Current.GoToAsync("//main");
    }
}
