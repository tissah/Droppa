using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;
using Droppa.Services.Api;

namespace Droppa.ViewModels;

/// <summary>
/// The driver's "My Trips" hub. Lists every accepted delivery, split into two groups:
///   • To pick up   — accepted but the parcel hasn't been collected yet.
///   • To deliver   — collected ("frozen" out of pickups) and on its way to the destination.
/// Trips can be at different couriers and bound for different customers; tapping one opens the
/// per-trip workspace (weigh / collect / transit / deliver / remit).
/// </summary>
public partial class DriverTripsViewModel : BaseViewModel
{
    private readonly DroppaApiClient _api;
    private readonly IAuthService _auth;

    public DriverTripsViewModel(DroppaApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "My trips";
    }

    /// <summary>Full name of the signed-in driver, shown in the top-right of the page header.</summary>
    public string UserName => _auth.CurrentUser?.FullName ?? "Driver";

    // Refresh the trip lists on their own every 15s while the page is open, so status changes and
    // newly-accepted trips stay current without a manual refresh.
    protected override TimeSpan AutoRefreshInterval => TimeSpan.FromSeconds(15);
    protected override Task AutoRefreshAsync() => RefreshAsync();

    /// <summary>Accepted trips awaiting pickup.</summary>
    public ObservableCollection<DriverTrip> ToPickUp { get; } = [];

    /// <summary>Collected trips awaiting delivery to their destination.</summary>
    public ObservableCollection<DriverTrip> ToDeliver { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickUpHeader))]
    [NotifyPropertyChangedFor(nameof(HasNoPickups))]
    private int _pickUpCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliverHeader))]
    [NotifyPropertyChangedFor(nameof(HasNoDeliveries))]
    private int _deliverCount;

    public string PickUpHeader => $"To pick up ({PickUpCount})";
    public string DeliverHeader => $"To deliver ({DeliverCount})";
    public bool HasNoPickups => PickUpCount == 0;
    public bool HasNoDeliveries => DeliverCount == 0;

    [ObservableProperty] private string? _errorMessage;
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

            var deliveries = await _api.GetDriverDeliveriesAsync();
            var trips = deliveries.Select(d => new DriverTrip
            {
                Id = d.Id,
                Reference = d.Reference,
                ServiceType = d.ServiceType,
                Status = d.Status,
                CourierServiceName = d.CourierServiceName,
                PickupLatitude = d.PickupLatitude,
                PickupLongitude = d.PickupLongitude,
                DestinationLatitude = d.DestinationLatitude,
                DestinationLongitude = d.DestinationLongitude,
                TotalFee = d.TotalFee,
                ParcelWeightGrams = d.ParcelWeightGrams
            }).ToList();

            ToPickUp.Clear();
            foreach (var t in trips.Where(t => t.NeedsPickup))
                ToPickUp.Add(t);

            ToDeliver.Clear();
            foreach (var t in trips.Where(t => t.NeedsDelivery))
                ToDeliver.Add(t);

            PickUpCount = ToPickUp.Count;
            DeliverCount = ToDeliver.Count;
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

    /// <summary>Opens the per-trip workspace for the tapped trip.</summary>
    [RelayCommand]
    private async Task OpenTripAsync(DriverTrip? trip)
    {
        if (trip is null) return;
        await Shell.Current.GoToAsync($"driverDelivery?deliveryId={trip.Id}");
    }
}
