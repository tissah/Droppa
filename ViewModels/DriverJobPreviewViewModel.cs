using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;
using Droppa.Services.Api;

namespace Droppa.ViewModels;

/// <summary>
/// Read-only preview of an open job, shown before the driver commits. It plots the customer's
/// pickup and drop-off on the map and exposes Accept / Deny. Accepting takes the ride and opens
/// the active-delivery screen; denying returns to the job board.
/// </summary>
[QueryProperty(nameof(Job), "job")]
public partial class DriverJobPreviewViewModel : BaseViewModel
{
    private readonly DroppaApiClient _api;
    private readonly IAuthService _auth;

    public DriverJobPreviewViewModel(DroppaApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Job preview";
    }

    /// <summary>Full name of the signed-in driver, shown in the top-right of the page header.</summary>
    public string UserName => _auth.CurrentUser?.FullName ?? "Driver";

    [ObservableProperty] private DriverJob? _job;

    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _courierName = string.Empty;
    [ObservableProperty] private string _pickupText = string.Empty;
    [ObservableProperty] private string _destinationText = string.Empty;
    [ObservableProperty] private string _tripDistanceText = string.Empty;
    [ObservableProperty] private string _distanceFromDriverText = string.Empty;
    [ObservableProperty] private string _feeText = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    // Raw coordinates for the map. Pickup is the customer's location for a Send; the drop-off is
    // the customer's location for a Receive — both pins are always shown.
    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }
    public double DestinationLatitude { get; private set; }
    public double DestinationLongitude { get; private set; }
    public bool HasRoute { get; private set; }

    /// <summary>Raised once the coordinates are known so the page can drop the map pins.</summary>
    public event Action? RouteReady;

    partial void OnJobChanged(DriverJob? value)
    {
        if (value is null) return;

        CategoryLabel = $"{value.CategoryIcon}  {value.CategoryLabel}";
        Reference = value.Reference;
        CourierName = $"Courier: {value.CourierServiceName}";
        PickupText = $"📍 Pickup: {value.PickupText}";
        DestinationText = $"🏁 Drop-off: {value.DestinationText}";
        TripDistanceText = $"Trip: {value.DistanceKm:F2} km";
        DistanceFromDriverText = value.DistanceFromDriverText;
        FeeText = $"MWK {value.TotalFee:N0}";

        PickupLatitude = value.PickupLatitude;
        PickupLongitude = value.PickupLongitude;
        DestinationLatitude = value.DestinationLatitude;
        DestinationLongitude = value.DestinationLongitude;

        HasRoute = true;
        RouteReady?.Invoke();
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Job is null || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            // Enforce the per-driver trip cap before taking another ride.
            var mine = await _api.GetDriverDeliveriesAsync();
            if (mine.Count(DriverJobsViewModel.IsActiveTrip) >= DriverJobsViewModel.MaxTrips)
            {
                ErrorMessage = $"You already have {DriverJobsViewModel.MaxTrips} active trips. " +
                               "Finish a delivery before accepting more.";
                return;
            }

            await _api.AcceptRideAsync(Job.DeliveryRequestId);

            var pickupInstruction = Job.IsReceive
                ? $"Pick up the parcel from {Job.CourierServiceName}."
                : "Pick up the parcel from the customer's location.";

            var viewTrips = await Shell.Current.DisplayAlert(
                "Trip accepted",
                $"{pickupInstruction}\n\nIt's been added to your pickups. " +
                $"You can keep accepting trips (up to {DriverJobsViewModel.MaxTrips}).",
                "View my trips", "Accept more");

            // Either way we leave the preview; pick the destination based on the driver's choice.
            if (viewTrips)
                await Shell.Current.GoToAsync("//driver/trips");
            else
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            // Most likely 409: another driver took it first.
            ErrorMessage = ex.Message;
            
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DenyAsync()
    {
        if (Job is null || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _api.RejectRideAsync(Job.DeliveryRequestId);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            // Whether or not the audit call succeeded, leave the preview and refresh the board.
            await Shell.Current.GoToAsync("..");
        }
    }
}
