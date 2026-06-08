using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;
using Droppa.Services.Api;

namespace Droppa.ViewModels;

/// <summary>
/// Customer-facing live tracking. Shows the assigned driver's details and motorcycle, and a
/// live-updating position / ETA / remaining-distance fed by the SignalR tracking hub. Falls
/// back to the REST snapshot for the initial state and when realtime isn't yet connected.
/// </summary>
[QueryProperty(nameof(DeliveryId), "deliveryId")]
public partial class TrackDeliveryViewModel : BaseViewModel
{
    private readonly DroppaApiClient _api;
    private readonly ITrackingService _tracking;
    private bool _subscribed;

    public TrackDeliveryViewModel(DroppaApiClient api, ITrackingService tracking, PaymentViewModel payment)
    {
        _api = api;
        _tracking = tracking;
        Payment = payment;
        Payment.Paid += OnParcelPaid;
        Title = "Track driver";
    }

    /// <summary>Payment panel for the weight-based parcel charge (the customer's second payment).</summary>
    public PaymentViewModel Payment { get; }

    [ObservableProperty] private int _deliveryId;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _statusText = "Pending";

    /// <summary>Raw API DeliveryStatus code; rebuilds the order summary when it changes.</summary>
    [ObservableProperty] private int _serverStatus;

    /// <summary>The order summary shown to the customer (placed → … → delivered), kept live.</summary>
    public ObservableCollection<OrderStage> Timeline { get; } = [];

    partial void OnServerStatusChanged(int value) => RebuildTimeline();

    private void RebuildTimeline()
    {
        var stages = OrderTimeline.Build(ServerStatus);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Timeline.Clear();
            foreach (var s in stages) Timeline.Add(s);
        });
    }

    [ObservableProperty] private string _driverName = "Awaiting a driver…";
    [ObservableProperty] private string? _driverPhone;
    [ObservableProperty] private string _motorcycleText = "—";

    [ObservableProperty] private string _positionText = "Waiting for the driver's location…";
    [ObservableProperty] private string _etaText = "ETA: —";
    [ObservableProperty] private string _remainingText = "Remaining: —";
    [ObservableProperty] private string _lastUpdatedText = string.Empty;
    [ObservableProperty] private bool _hasDriver;
    [ObservableProperty] private string? _errorMessage;

    // ---- Parcel (weight) charge: the second payment, sent by the driver after weighing ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParcelWeightText))]
    private double _parcelWeightGrams;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParcelCharge))]
    [NotifyPropertyChangedFor(nameof(NeedsParcelPayment))]
    [NotifyPropertyChangedFor(nameof(AwaitingParcelCharge))]
    private decimal _parcelCharge;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsParcelPayment))]
    private bool _parcelChargePaid;

    [ObservableProperty] private string _parcelStatusText =
        "Awaiting the rider to weigh your parcel.";

    public bool HasParcelCharge => ParcelCharge > 0;
    public bool NeedsParcelPayment => HasParcelCharge && !ParcelChargePaid;
    public bool AwaitingParcelCharge => !HasParcelCharge;
    public string ParcelWeightText => $"{ParcelWeightGrams:N0} g";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    // Route context for the map (pickup/destination), populated once the delivery loads.
    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }
    public double DestinationLatitude { get; private set; }
    public double DestinationLongitude { get; private set; }

    /// <summary>Raised once pickup/destination coordinates are known, so the map can place pins.</summary>
    public event Action? RouteContextReady;

    /// <summary>Raised on every driver position (snapshot + live) with lat/lng for the map.</summary>
    public event Action<double, double>? DriverPositionChanged;

    partial void OnDeliveryIdChanged(int value) => _ = StartAsync();

    private async Task StartAsync()
    {
        if (DeliveryId == 0 || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            await LoadDeliveryAsync();
            await LoadSnapshotAsync();
            await SubscribeRealtimeAsync();
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

    private async Task LoadDeliveryAsync()
    {
        var d = await _api.GetDeliveryAsync(DeliveryId);
        Reference = d.Reference;
        ServerStatus = d.Status;
        StatusText = StatusLabel(d.Status);

        PickupLatitude = d.PickupLatitude;
        PickupLongitude = d.PickupLongitude;
        DestinationLatitude = d.DestinationLatitude;
        DestinationLongitude = d.DestinationLongitude;
        RouteContextReady?.Invoke();

        HasDriver = d.AssignedDriverId is not null;
        if (HasDriver)
        {
            DriverName = string.IsNullOrWhiteSpace(d.DriverName) ? "Driver assigned" : d.DriverName!;
            DriverPhone = d.DriverPhone;
            MotorcycleText = BuildMotorcycle(d.MotorcycleMakeModel, d.MotorcycleRegistration);
        }

        ApplyParcelCharge(d);
    }

    /// <summary>Reflects the parcel (weight) charge the driver sent, and primes the payment panel.</summary>
    private void ApplyParcelCharge(DeliveryDto d)
    {
        ParcelWeightGrams = d.ParcelWeightGrams ?? 0;
        ParcelCharge = d.ParcelCharge ?? 0m;
        ParcelChargePaid = d.ParcelChargePaid;

        if (ParcelChargePaid)
            ParcelStatusText = "Parcel fee paid. Thank you!";
        else if (HasParcelCharge)
        {
            ParcelStatusText = $"Parcel fee for {ParcelWeightText} (incl. VAT). Please confirm and pay.";
            Payment.Reset(ParcelCharge);
        }
        else
            ParcelStatusText = "Awaiting the rider to weigh your parcel.";
    }

    /// <summary>Re-fetches the delivery to pick up a parcel fee the rider may have just sent.</summary>
    [RelayCommand]
    private async Task RefreshParcelChargeAsync()
    {
        if (DeliveryId == 0) return;
        try
        {
            var d = await _api.GetDeliveryAsync(DeliveryId);
            ApplyParcelCharge(d);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>The customer paid the parcel charge — record it against the delivery.</summary>
    private async void OnParcelPaid()
    {
        try
        {
            await _api.PayParcelChargeAsync(new ParcelPaymentDto
            {
                DeliveryRequestId = DeliveryId,
                TransactionId = Payment.TransactionReference
            });
            ParcelChargePaid = true;
            ParcelStatusText = $"Parcel fee paid · {Payment.TransactionReference}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadSnapshotAsync()
    {
        try
        {
            var t = await _api.GetTrackingAsync(DeliveryId);
            ServerStatus = t.Status;
            StatusText = StatusLabel(t.Status);
            if (t.DriverLatitude is double lat && t.DriverLongitude is double lng)
                ApplyPosition(lat, lng, t.EtaMinutes, t.RemainingDistanceKm, t.UpdatedAt);
        }
        catch
        {
            // No tracking row yet (driver hasn't pinged). The realtime feed will fill it in.
        }
    }

    private async Task SubscribeRealtimeAsync()
    {
        if (_subscribed) return;

        _tracking.DriverLocationUpdated += OnDriverLocationUpdated;
        _tracking.DeliveryStatusChanged += OnDeliveryStatusChanged;
        _tracking.RideAccepted += OnRideAccepted;

        await _tracking.SubscribeToDeliveryAsync(DeliveryId);
        _subscribed = true;
    }

    /// <summary>Detaches handlers and leaves the delivery group. Call when the page closes.</summary>
    public async Task StopAsync()
    {
        if (!_subscribed) return;
        _tracking.DriverLocationUpdated -= OnDriverLocationUpdated;
        _tracking.DeliveryStatusChanged -= OnDeliveryStatusChanged;
        _tracking.RideAccepted -= OnRideAccepted;
        Payment.Paid -= OnParcelPaid;
        _subscribed = false;
        try { await _tracking.UnsubscribeFromDeliveryAsync(DeliveryId); } catch { /* best effort */ }
    }

    private void OnDriverLocationUpdated(DriverLocationUpdate u)
    {
        if (u.DeliveryRequestId != DeliveryId) return;
        MainThread.BeginInvokeOnMainThread(() =>
            ApplyPosition(u.Lat, u.Lng, u.EtaMinutes, null, DateTimeOffset.Now));
    }

    private void OnDeliveryStatusChanged(DeliveryStatusUpdate u)
    {
        if (u.DeliveryRequestId != DeliveryId) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusText = u.Status;
            var code = StatusCode(u.Status);
            if (code >= 0) ServerStatus = code;
        });
    }

    private void OnRideAccepted(RideAcceptedInfo info)
    {
        if (info.DeliveryRequestId != DeliveryId) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            HasDriver = true;
            // A driver accepting the ride moves the order to at least "Accepted".
            if (ServerStatus < 2) ServerStatus = 2;
            if (!string.IsNullOrWhiteSpace(info.DriverName)) DriverName = info.DriverName!;
            DriverPhone = info.DriverPhone;
            MotorcycleText = BuildMotorcycle(info.Motorcycle, info.Registration);
        });
    }

    private void ApplyPosition(double lat, double lng, double? etaMinutes, double? remainingKm, DateTimeOffset? updatedAt)
    {
        PositionText = $"Driver at {lat:F5}, {lng:F5}";
        EtaText = etaMinutes is double e ? $"ETA: {e:F0} min" : "ETA: —";
        RemainingText = remainingKm is double r ? $"Remaining: {r:F2} km" : RemainingText;
        var when = (updatedAt ?? DateTimeOffset.Now).ToLocalTime();
        LastUpdatedText = $"Updated {when:HH:mm:ss}";

        DriverPositionChanged?.Invoke(lat, lng);
    }

    private static string BuildMotorcycle(string? makeModel, string? registration)
    {
        if (!string.IsNullOrWhiteSpace(makeModel) && !string.IsNullOrWhiteSpace(registration))
            return $"{makeModel} · {registration}";
        return makeModel ?? registration ?? "—";
    }

    [RelayCommand]
    private async Task CallDriverAsync()
    {
        if (string.IsNullOrWhiteSpace(DriverPhone)) return;
        try { PhoneDialer.Default.Open(DriverPhone); }
        catch (Exception ex) { ErrorMessage = ex.Message; await Task.CompletedTask; }
    }

    private static string StatusLabel(int status) => status switch
    {
        0 => "Pending",
        1 => "Driver assigned",
        2 => "Accepted",
        3 => "Rejected",
        4 => "Pickup in progress",
        5 => "Parcel collected",
        6 => "In transit",
        7 => "Arriving",
        8 => "Delivered",
        9 => "Cancelled",
        _ => "Unknown"
    };

    /// <summary>Reverse of <see cref="StatusLabel"/> for the string statuses pushed over SignalR.</summary>
    private static int StatusCode(string status) => status switch
    {
        "Pending" => 0,
        "Driver assigned" => 1,
        "Accepted" => 2,
        "Rejected" => 3,
        "Pickup in progress" => 4,
        "Parcel collected" => 5,
        "In transit" => 6,
        "Arriving" => 7,
        "Delivered" => 8,
        "Cancelled" => 9,
        _ => -1
    };
}
