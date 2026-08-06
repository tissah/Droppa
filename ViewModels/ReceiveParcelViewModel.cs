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
        AddParcel(); // start with one parcel
    }

    /// <summary>The parcels being collected — one or more, each from its own sender.</summary>
    public ObservableCollection<ReceiveParcelEntryViewModel> Parcels { get; } = [];

    /// <summary>Adds a new blank parcel to collect.</summary>
    [RelayCommand]
    private void AddParcel()
    {
        var parcel = new ReceiveParcelEntryViewModel(Parcels.Count + 1);
        parcel.RemoveRequested += RemoveParcel;
        Parcels.Add(parcel);
        RefreshParcelState();
    }

    /// <summary>Removes a parcel and renumbers the remaining cards. Always keeps at least one.</summary>
    private void RemoveParcel(ReceiveParcelEntryViewModel parcel)
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

    /// <summary>The couriers with a branch in the customer's district, one entry per courier.</summary>
    public ObservableCollection<CourierService> Couriers { get; } = [];

    /// <summary>The district the loaded courier list was filtered to; reloads when the account changes.</summary>
    private string? _loadedDistrict;

    /// <summary>The customer's registration district — the only district couriers are offered from.</summary>
    public string? District => _auth.CurrentUser?.District;

    /// <summary>The id the district's branches are fetched by.</summary>
    private int? DistrictId => _auth.CurrentUser?.DistrictId;

    /// <summary>Caption under the courier picker, explaining why only some couriers are listed.</summary>
    public string? DistrictHint => string.IsNullOrWhiteSpace(District)
        ? null
        : $"Couriers in {District}, the district you registered in.";

    /// <summary>
    /// Loads the courier branches in the customer's district and lists the couriers holding
    /// them — one entry per courier, however many branches it has there. Each courier arrives
    /// carrying its own branches, which is what the branch picker fills from on selection.
    /// </summary>
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
            var list = await _couriers.GetInDistrictAsync(DistrictId, district);

            SelectedCourier = null;
            Couriers.Clear();
            foreach (var c in list)
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

    [ObservableProperty] private CourierService? _selectedCourier;

    /// <summary>Branches of the selected courier; the customer picks which branch holds the parcel.</summary>
    public ObservableCollection<Branch> Branches { get; } = [];

    [ObservableProperty] private Branch? _selectedBranch;

    /// <summary>True when the selected courier has branches to choose from — drives the dropdown's visibility.</summary>
    public bool HasBranches => Branches.Count > 0;

    /// <summary>
    /// When the courier changes, fill the branch picker with that courier's branches and clear
    /// any earlier branch choice. The courier was loaded for the customer's district, so its
    /// branches are already the ones in that district — a single branch is preselected.
    /// </summary>
    partial void OnSelectedCourierChanged(CourierService? value)
    {
        SelectedBranch = null;
        Branches.Clear();
        if (value is not null)
            foreach (var b in value.Branches)
                Branches.Add(b);

        OnPropertyChanged(nameof(HasBranches));

        // Nothing to choose when the courier has one office there — pick it for the customer.
        if (Branches.Count == 1) SelectedBranch = Branches[0];
    }

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
    [NotifyPropertyChangedFor(nameof(BranchSummary))]
    private Booking? _quote;

    [ObservableProperty] private string? _statusMessage;

    public bool HasQuote => Quote is not null;

    /// <summary>The chosen branch and its district for the quote summary, or null when the courier has no branch.</summary>
    public string? BranchSummary => Quote?.Branch is { } b
        ? string.IsNullOrWhiteSpace(b.District) ? b.Name : $"{b.Name} · {b.District}"
        : null;

    /// <summary>Total the customer pays: delivery fee + the amount due at the courier office.</summary>
    public decimal GrandTotal => (Quote?.TotalFee ?? 0m) + CourierAmount;

    [RelayCommand]
    private async Task GetQuoteAsync()
    {
        if (IsBusy) return;

        if (SelectedCourier is null)
        {
            StatusMessage = "Please choose the courier holding your parcel.";
            return;
        }
        if (Branches.Count > 0 && SelectedBranch is null)
        {
            StatusMessage = "Please choose the courier branch.";
            return;
        }
        for (var i = 0; i < Parcels.Count; i++)
        {
            if (!Parcels[i].HasProof)
            {
                StatusMessage = $"Parcel {i + 1}: enter a waybill number or attach a receipt image.";
                return;
            }
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

            var parcels = Parcels.Select(p => p.ToParcel()).ToList();

            // The chosen branch's office is where the parcel is collected; fall back to the
            // courier's single office when it has no branches.
            var courierOffice = SelectedBranch?.Office ?? SelectedCourier.Office;

            Quote = await _booking.QuoteAsync(
                ServiceType.ReceiveParcel,
                SelectedCourier,
                parcels,
                courierOffice,
                DestinationLocation);
            Quote.Branch = SelectedBranch;
            // Carry the customer-entered courier-office amount onto the booking so it's sent to the
            // server and, in turn, becomes what the driver remits to the courier on collection.
            Quote.CourierAmount = CourierAmount;

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
