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
        AddParcel(); // start with one parcel
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
    /// <summary>The parcels being sent — one or more, each to its own receiver.</summary>
    public ObservableCollection<ParcelEntryViewModel> Parcels { get; } = [];

    /// <summary>Adds a new blank parcel to the booking.</summary>
    [RelayCommand]
    private void AddParcel()
    {
        var parcel = new ParcelEntryViewModel(Parcels.Count + 1);
        parcel.RemoveRequested += RemoveParcel;
        Parcels.Add(parcel);
        RefreshParcelState();
    }

    /// <summary>Removes a parcel and renumbers the remaining cards. Always keeps at least one.</summary>
    private void RemoveParcel(ParcelEntryViewModel parcel)
    {
        if (Parcels.Count <= 1) return;
        parcel.RemoveRequested -= RemoveParcel;
        Parcels.Remove(parcel);
        RefreshParcelState();
    }

    /// <summary>Renumbers cards and updates whether each parcel can be removed (only when &gt; 1 remains).</summary>
    private void RefreshParcelState()
    {
        var canRemove = Parcels.Count > 1;
        for (var i = 0; i < Parcels.Count; i++)
        {
            Parcels[i].Number = i + 1;
            Parcels[i].CanRemove = canRemove;
        }
    }

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

        for (var i = 0; i < Parcels.Count; i++)
        {
            var p = Parcels[i];
            var label = $"Parcel {i + 1}";
            if (string.IsNullOrWhiteSpace(p.ItemName))
            {
                StatusMessage = $"{label}: please enter the item name.";
                return;
            }
            if (string.IsNullOrWhiteSpace(p.ReceiverName))
            {
                StatusMessage = $"{label}: please enter the receiver's name.";
                return;
            }
            if (string.IsNullOrWhiteSpace(p.ReceiverPhone))
            {
                StatusMessage = $"{label}: please enter the receiver's phone number.";
                return;
            }
        }

        try
        {
            IsBusy = true;
            PickupLocation ??= await _location.GetCurrentLocationAsync() ?? LocationService.LilongweCentre;

            var parcels = Parcels.Select(p => p.ToParcel()).ToList();

            Quote = await _booking.QuoteAsync(
                ServiceType.SendParcel,
                SelectedCourier,
                parcels,
                PickupLocation,
                SelectedCourier.Office);

            // A new quote means a new amount due — reset any earlier payment.
            // The customer pays the distance ride fee only; each parcel's weight charge is
            // added later by the driver and paid as a separate, second payment.
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
