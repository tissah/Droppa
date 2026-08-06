using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;
using Droppa.Services.Api;

namespace Droppa.ViewModels;

/// <summary>
/// The driver's job board: lists open transport requests within a chosen radius of the driver
/// and lets them open a request to preview/accept it. A driver may hold up to <see cref="MaxTrips"/>
/// trips at once; accepting keeps them on the board so they can batch several pickups.
/// </summary>
public partial class DriverJobsViewModel : BaseViewModel
{
    /// <summary>The most trips a driver can hold (accepted but not yet delivered) at one time.</summary>
    public const int MaxTrips = 10;

    private readonly DroppaApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILocationService _location;

    // All open jobs fetched from the server, before the radius filter is applied.
    private readonly List<DriverJob> _allJobs = [];

    public DriverJobsViewModel(DroppaApiClient api, IAuthService auth, ILocationService location)
    {
        _api = api;
        _auth = auth;
        _location = location;
        Title = "Pickups";
    }

    /// <summary>
    /// Pickups tab — open "Send" requests (customer → courier): the parcel is going from a
    /// customer to a courier.
    /// </summary>
    public ObservableCollection<DriverJob> Pickups { get; } = [];

    /// <summary>
    /// Deliveries tab — open "Receive" requests (courier → customer): the parcel is going from a
    /// courier to a customer.
    /// </summary>
    public ObservableCollection<DriverJob> Deliveries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickupsTabText))]
    private int _pickupCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliveriesTabText))]
    private int _deliveryCount;

    public string PickupsTabText => $"Pickups ({PickupCount})";
    public string DeliveriesTabText => $"Deliveries ({DeliveryCount})";

    /// <summary>Which tab is active. True = Pickups (default), false = Deliveries.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeliveriesTab))]
    private bool _isPickupsTab = true;

    public bool IsDeliveriesTab => !IsPickupsTab;

    [RelayCommand] private void ShowPickups() => IsPickupsTab = true;
    [RelayCommand] private void ShowDeliveries() => IsPickupsTab = false;

    // Pull the job board on its own every 15s while the page is open, so new customer requests
    // appear without the driver tapping Refresh.
    protected override TimeSpan AutoRefreshInterval => TimeSpan.FromSeconds(15);
    protected override Task AutoRefreshAsync() => RefreshAsync();

    /// <summary>Pickup-radius choices (km). Only jobs within the selected radius are listed.</summary>
    public IReadOnlyList<double> RadiusOptions { get; } = [5, 10, 15, 20];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RadiusText))]
    private double _selectedRadiusKm = 10;

    public string RadiusText => $"Within {SelectedRadiusKm:F0} km";

    partial void OnSelectedRadiusKmChanged(double value) => ApplyFilter();

    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Greeting))]
    private string _driverHeader = "Driver";

    [ObservableProperty] private string? _motorcycleText;

    /// <summary>Home-style welcome line, matching the customer's home page header.</summary>
    public string Greeting => $"Hello, {DriverHeader}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TripCountText))]
    [NotifyPropertyChangedFor(nameof(IsAtTripLimit))]
    private int _acceptedTripCount;

    public string TripCountText => $"{AcceptedTripCount}/{MaxTrips} trips accepted";

    /// <summary>True when the driver already holds the maximum number of trips.</summary>
    public bool IsAtTripLimit => AcceptedTripCount >= MaxTrips;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            await LoadProfileAsync();
            await ReportLocationAsync();
            await LoadAcceptedCountAsync();

            var jobs = await _api.GetDriverJobsAsync();
            _allJobs.Clear();
            foreach (var j in jobs)
                _allJobs.Add(new DriverJob
                {
                    DeliveryRequestId = j.DeliveryRequestId,
                    Reference = j.Reference,
                    ServiceType = j.ServiceType,
                    CourierServiceName = j.CourierServiceName,
                    PickupLatitude = j.PickupLatitude,
                    PickupLongitude = j.PickupLongitude,
                    DestinationLatitude = j.DestinationLatitude,
                    DestinationLongitude = j.DestinationLongitude,
                    DistanceKm = j.DistanceKm,
                    TotalFee = j.TotalFee,
                    DistanceFromDriverKm = j.DistanceFromDriverKm
                });

            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Rebuilds the two tabs from the cached list, keeping only jobs within the radius.
    /// Send requests (customer → courier) go to the Pickups tab; Receive requests
    /// (courier → customer) go to the Deliveries tab.
    /// </summary>
    private void ApplyFilter()
    {
        Pickups.Clear();
        Deliveries.Clear();
        foreach (var j in _allJobs)
        {
            // Keep jobs within range. Jobs with an unknown distance are kept so work isn't hidden.
            if (j.DistanceFromDriverKm is double d && d > SelectedRadiusKm) continue;
            if (j.IsReceive) Deliveries.Add(j);
            else Pickups.Add(j);
        }
        PickupCount = Pickups.Count;
        DeliveryCount = Deliveries.Count;
    }

    /// <summary>
    /// Opens the read-only job preview: a map of the customer's location plus the trip details,
    /// where the driver accepts or denies the request. Accept/deny no longer happen on this list.
    /// </summary>
    [RelayCommand]
    private async Task ViewJobAsync(DriverJob? job)
    {
        if (job is null) return;
        await Shell.Current.GoToAsync("driverJobPreview", new Dictionary<string, object>
        {
            ["job"] = job
        });
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        _auth.SignOut();
        await Shell.Current.GoToAsync("//login");
    }

    /// <summary>Counts the driver's active (accepted but not delivered/cancelled) trips for the limit.</summary>
    private async Task LoadAcceptedCountAsync()
    {
        try
        {
            var mine = await _api.GetDriverDeliveriesAsync();
            AcceptedTripCount = mine.Count(IsActiveTrip);
        }
        catch
        {
            // Non-fatal: the board still works; the counter just won't update.
        }
    }

    /// <summary>Active = assigned…arriving, excluding rejected. Delivered/cancelled don't count.</summary>
    internal static bool IsActiveTrip(DeliveryDto d) =>
        d.Status is >= DriverTrip.DriverAssigned and <= DriverTrip.Arriving
        && d.Status != DriverTrip.Rejected;

    /// <summary>Pushes the driver's current GPS (no delivery attached) so the server can rank jobs by distance.</summary>
    private async Task ReportLocationAsync()
    {
        try
        {
            var loc = await _location.GetCurrentLocationAsync();
            if (loc is not null)
                await _api.UpdateDriverLocationAsync(new UpdateLocationDto
                {
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude
                });
        }
        catch
        {
            // Non-fatal: distances may be missing, but the board still lists jobs.
        }
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var me = await _api.GetDriverMeAsync();
            DriverHeader = string.IsNullOrWhiteSpace(me.FullName) ? "Driver" : me.FullName;
            MotorcycleText = me.MotorcycleMakeModel is { Length: > 0 }
                ? $"{me.MotorcycleMakeModel} · {me.MotorcycleRegistration}"
                : me.MotorcycleRegistration;
        }
        catch
        {
            // Non-fatal: the job board still works without the profile header.
        }
    }
}
