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

    public ObservableCollection<CourierService> Couriers { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Couriers.Count > 0) return;

        // The customer's resident district drives which couriers are shown. Without it we
        // can't filter, so prompt the customer to set it rather than list every courier.
        var district = _auth.CurrentUser?.District;
        if (string.IsNullOrWhiteSpace(district))
        {
            StatusMessage = "Set your resident district to see couriers that serve your area.";
            return;
        }

        try
        {
            IsBusy = true;
            var list = await _couriers.GetAllAsync();
            // Only couriers with a branch in the customer's district.
            foreach (var c in list.Where(c => c.Branches.Any(b => SameDistrict(b, district))))
                Couriers.Add(c);

            if (Couriers.Count == 0)
                StatusMessage = $"No couriers currently operate in {district}.";
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

    /// <summary>True when the branch is in the given district (case-insensitive).</summary>
    private static bool SameDistrict(Branch branch, string? district) =>
        !string.IsNullOrWhiteSpace(district) &&
        string.Equals(branch.District, district, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty] private CourierService? _selectedCourier;

    /// <summary>Branches of the selected courier; the customer picks which branch holds the parcel.</summary>
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
        var district = _auth.CurrentUser?.District;
        if (value is not null)
            foreach (var b in value.Branches.Where(b => SameDistrict(b, district)))
                Branches.Add(b);
        OnPropertyChanged(nameof(HasBranches));
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
