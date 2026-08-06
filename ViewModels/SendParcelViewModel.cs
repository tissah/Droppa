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
        IBookingService booking, IAuthService auth)
    {
        _couriers = couriers;
        _location = location;
        _booking = booking;
        _auth = auth;
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

    public ObservableCollection<CourierService> Couriers { get; } = [];

    /// <summary>The district the loaded courier list was filtered to; reloads when the account changes.</summary>
    private string? _loadedDistrict;

    /// <summary>The customer's registration district — the only district couriers are offered from.</summary>
    public string? District => _auth.CurrentUser?.District;

    /// <summary>Caption under the courier picker, explaining why only some couriers are listed.</summary>
    public string? DistrictHint => string.IsNullOrWhiteSpace(District)
        ? null
        : $"Couriers in {District}, the district you registered in.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        var district = District;
        OnPropertyChanged(nameof(District));
        OnPropertyChanged(nameof(DistrictHint));
        if (Couriers.Count > 0 && Districts.Match(_loadedDistrict, district)) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;
            var list = await _couriers.GetAllAsync();

            SelectedCourier = null;
            Couriers.Clear();
            foreach (var c in CourierDirectory.InDistrict(list, district))
                Couriers.Add(c);
            _loadedDistrict = district;

            if (Couriers.Count == 0)
                StatusMessage = string.IsNullOrWhiteSpace(district)
                    ? "No courier services are available right now."
                    : $"No courier service operates in {district} yet.";
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

    /// <summary>Branches of the selected courier; the customer picks which branch is the destination.</summary>
    public ObservableCollection<Branch> Branches { get; } = [];

    [ObservableProperty] private Branch? _selectedBranch;

    /// <summary>True when the selected courier has branches to choose from — drives the dropdown's visibility.</summary>
    public bool HasBranches => Branches.Count > 0;

    /// <summary>
    /// When the courier changes, refresh the branch list and clear any earlier branch choice.
    /// Only branches in the customer's district are offered.
    /// </summary>
    partial void OnSelectedCourierChanged(CourierService? value)
    {
        SelectedBranch = null;
        Branches.Clear();
        if (value is not null)
            foreach (var b in CourierDirectory.BranchesInDistrict(value, District))
                Branches.Add(b);
        OnPropertyChanged(nameof(HasBranches));
    }

    [ObservableProperty] private GeoLocation? _pickupLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    [NotifyPropertyChangedFor(nameof(BranchSummary))]
    private Booking? _quote;

    [ObservableProperty] private string? _statusMessage;

    public bool HasQuote => Quote is not null;

    /// <summary>The chosen branch and its district for the quote summary, or null when the courier has no branch.</summary>
    public string? BranchSummary => Quote?.Branch is { } b
        ? string.IsNullOrWhiteSpace(b.District) ? b.Name : $"{b.Name} · {b.District}"
        : null;

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

        if (Branches.Count > 0 && SelectedBranch is null)
        {
            StatusMessage = "Please choose the courier branch.";
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

            // The chosen branch's office is the courier end of the route; fall back to the
            // courier's single office when it has no branches.
            var courierOffice = SelectedBranch?.Office ?? SelectedCourier.Office;

            Quote = await _booking.QuoteAsync(
                ServiceType.SendParcel,
                SelectedCourier,
                parcels,
                PickupLocation,
                courierOffice);
            Quote.Branch = SelectedBranch;

            // No payment at booking. The customer confirms the ride on the distance estimate; the
            // combined total (ride + parcel weight) is paid later, once a rider accepts and the
            // customer enters the parcel weight on the tracking screen.
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
        if (Quote is null || IsBusy) return;

        try
        {
            IsBusy = true;
            StatusMessage = null;

            await _booking.ConfirmAsync(Quote);
            var reference = Quote.Reference;
            Quote = null;
            await Shell.Current.DisplayAlert("Booking created",
                $"Your delivery {reference} has been requested. Once a rider accepts, open it under " +
                "\"My deliveries\" to enter your parcel weight and pay the total (ride + parcel fee) — " +
                "that confirms the pickup.", "OK");
            await Shell.Current.GoToAsync("//main");
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
}
