using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

/// <summary>
/// Send flow (spec 2A): pickup = current GPS location, destination = selected courier office.
/// </summary>
public partial class SendParcelViewModel : BaseViewModel
{
    private readonly ICourierRepository _couriers;
    private readonly ILocationService _location;
    private readonly IBookingService _booking;
    private readonly IAuthService _auth;

    public SendParcelViewModel(ICourierRepository couriers, ILocationService location,
        IBookingService booking, PaymentViewModel payment, IAuthService auth)
    {
        _couriers = couriers;
        _location = location;
        _booking = booking;
        _auth = auth;
        Payment = payment;
        Title = "Send a parcel";
    }

    /// <summary>Full name of the signed-in user, shown in the top-right of the page header.</summary>
    public string UserName => _auth.CurrentUser?.FullName ?? "Guest";

    /// <summary>Pickup point options. "Current location" reads the device GPS when chosen.</summary>
    public ObservableCollection<string> PickupOptions { get; } = ["Current location"];

    [ObservableProperty] private string? _selectedPickupOption;

    partial void OnSelectedPickupOptionChanged(string? value)
    {
        if (value == "Current location")
            _ = CaptureLocationAsync();
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
    // Parcel details
    [ObservableProperty] private string _itemName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private string? _specialInstructions;

    // Receiver
    [ObservableProperty] private string _receiverName = string.Empty;
    [ObservableProperty] private string _receiverPhone = string.Empty;

    [ObservableProperty] private CourierService? _selectedCourier;
    [ObservableProperty] private GeoLocation? _pickupLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    private Booking? _quote;

    [ObservableProperty] private string? _statusMessage;

    public bool HasQuote => Quote is not null;

    [RelayCommand]
    private async Task CaptureLocationAsync()
    {
        StatusMessage = "Getting your location…";
        PickupLocation = await _location.GetCurrentLocationAsync()
                         ?? LocationService.LilongweCentre;
        StatusMessage = $"Pickup: {PickupLocation}";
    }

    [RelayCommand]
    private async Task GetQuoteAsync()
    {
        if (IsBusy) return;

        if (SelectedCourier is null)
        {
            StatusMessage = "Please choose a destination courier.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            StatusMessage = "Please enter the item name.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ReceiverName))
        {
            StatusMessage = "Please enter the receiver's name.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ReceiverPhone))
        {
            StatusMessage = "Please enter the receiver's phone number.";
            return;
        }

        try
        {
            IsBusy = true;
            PickupLocation ??= await _location.GetCurrentLocationAsync() ?? LocationService.LilongweCentre;

            var parcel = new Parcel
            {
                ItemName = ItemName,
                Description = Description,
                Quantity = Quantity,
                SpecialInstructions = SpecialInstructions,
                ReceiverName = ReceiverName,
                ReceiverPhone = ReceiverPhone
            };

            Quote = await _booking.QuoteAsync(
                ServiceType.SendParcel,
                SelectedCourier,
                parcel,
                PickupLocation,
                SelectedCourier.Office);

            // A new quote means a new amount due — reset any earlier payment.
            Payment.Reset(Quote.TotalFee);
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
