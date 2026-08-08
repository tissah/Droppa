using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;
using Droppa.Services.Api;
using Droppa.Services.Maps;
using Microsoft.Maui.Devices.Sensors;

namespace Droppa.ViewModels;

/// <summary>
/// Customer-facing live tracking. Shows the assigned driver's details and motorcycle, and a
/// live-updating position / ETA / remaining-distance fed by the SignalR tracking hub. Falls
/// back to the REST snapshot for the initial state and when realtime isn't yet connected.
/// </summary>
[QueryProperty(nameof(DeliveryId), "deliveryId")]
public partial class TrackDeliveryViewModel : BaseViewModel
{
    // The driver has to move at least this far from the point the road route was last computed
    // before we ask the Directions API for a fresh route. Keeps live tracking smooth without
    // firing a routing request on every 5-second GPS ping.
    private const double RouteRefreshMeters = 120;

    // The map auto-refreshes on this cadence by polling the REST tracking snapshot. This runs
    // alongside the SignalR feed: SignalR delivers positions instantly, the poll guarantees the
    // map still updates every few seconds even if the realtime hub drops or never connects.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly DroppaApiClient _api;
    private readonly ITrackingService _tracking;
    private readonly IDirectionsService _directions;
    private bool _subscribed;

    // Throttling state for the live road route from the driver's position to the destination.
    private Location? _lastRouteOrigin;
    private bool _routeBusy;

    public TrackDeliveryViewModel(
        DroppaApiClient api, ITrackingService tracking, IDirectionsService directions, PaymentViewModel payment)
    {
        _api = api;
        _tracking = tracking;
        _directions = directions;
        Payment = payment;
        Payment.Paid += OnParcelPaid;
        Title = "Track driver";
    }

    /// <summary>Payment panel for the single combined total: ride fee (distance) + parcel fee (weight).</summary>
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterWeight))]
    private bool _hasDriver;

    [ObservableProperty] private string? _errorMessage;

    // ---- Parcel weight & combined payment (the customer weighs their own parcel) ----
    // Once a rider accepts, the customer enters the parcel weight; the app charges the ride fee
    // (distance) plus the parcel fee (weight) as a single payment. Paying confirms the pickup —
    // only then can the rider collect.

    /// <summary>The distance ride fee for this delivery, from the booking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RideFeeText))]
    [NotifyPropertyChangedFor(nameof(TotalPayable))]
    [NotifyPropertyChangedFor(nameof(TotalPayableText))]
    private decimal _rideFee;

    /// <summary>Weight entry text — kept as text so the field starts empty and partial typing is safe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParcelWeightText))]
    [NotifyPropertyChangedFor(nameof(ParcelFee))]
    [NotifyPropertyChangedFor(nameof(ParcelFeeText))]
    [NotifyPropertyChangedFor(nameof(TotalPayable))]
    [NotifyPropertyChangedFor(nameof(TotalPayableText))]
    [NotifyPropertyChangedFor(nameof(CanPrepare))]
    private string? _weightText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsPayment))]
    [NotifyPropertyChangedFor(nameof(CanEnterWeight))]
    private bool _parcelChargePaid;

    // The server prices the parcel from the weight we submit and returns the combined total, so its
    // figures win over the local ParcelPricing estimate. Null until the weight has been submitted.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParcelFee))]
    [NotifyPropertyChangedFor(nameof(ParcelFeeText))]
    [NotifyPropertyChangedFor(nameof(TotalPayable))]
    [NotifyPropertyChangedFor(nameof(TotalPayableText))]
    private decimal? _serverWeightCharge;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPayable))]
    [NotifyPropertyChangedFor(nameof(TotalPayableText))]
    private decimal? _serverAmountToPay;

    /// <summary>True once the customer has calculated the total; reveals the payment panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsPayment))]
    private bool _parcelReady;

    [ObservableProperty] private string _parcelStatusText =
        "Waiting for a rider to accept your booking.";

    /// <summary>Parsed parcel weight in grams, or 0 when the field is empty/invalid.</summary>
    public double WeightGrams => double.TryParse(WeightText, out var g) && g > 0 ? g : 0;

    /// <summary>
    /// The weight-based parcel fee (incl. VAT). Uses the server's figure once the weight has been
    /// submitted, otherwise the local estimate, or 0 until a valid weight is entered.
    /// </summary>
    public decimal ParcelFee =>
        ServerWeightCharge ?? (WeightGrams > 0 ? ParcelPricing.Total(WeightGrams) : 0m);

    /// <summary>The single amount the customer pays: ride fee (distance) + parcel fee (weight).</summary>
    public decimal TotalPayable => ServerAmountToPay ?? RideFee + ParcelFee;

    public string RideFeeText => $"MWK {RideFee:N0}";
    public string ParcelFeeText => $"MWK {ParcelFee:N0}";
    public string TotalPayableText => $"MWK {TotalPayable:N0}";
    public string ParcelWeightText => $"{WeightGrams:N0} g";

    // Editing the weight invalidates the price the server quoted for the previous one: fall back to
    // the local estimate and hide the payment panel until the new weight has been re-priced.
    partial void OnWeightTextChanged(string? value)
    {
        if (ParcelChargePaid) return;
        ServerWeightCharge = null;
        ServerAmountToPay = null;
        ParcelReady = false;
    }

    /// <summary>The customer can enter a weight once a rider has accepted and before they've paid.</summary>
    public bool CanEnterWeight => HasDriver && !ParcelChargePaid;

    /// <summary>The total can be calculated once a positive weight is entered and nothing's paid yet.</summary>
    public bool CanPrepare => WeightGrams > 0 && !ParcelChargePaid;

    /// <summary>The payment panel shows once the total is prepared and before it's paid.</summary>
    public bool NeedsPayment => ParcelReady && !ParcelChargePaid;

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

    /// <summary>
    /// Raised with the road-snapped route from the driver's current position to the destination,
    /// recomputed as the driver moves. The page draws these points as the live route polyline.
    /// </summary>
    public event Action<IReadOnlyList<Location>>? RouteToDestinationReady;

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

        RideFee = d.TotalFee;
        ParcelChargePaid = d.ParcelChargePaid;
        if (d.ParcelWeightGrams is double g && g > 0) WeightText = g.ToString("0.##");

        // Assigned after WeightText so its change handler doesn't clear the figures we just loaded.
        ServerWeightCharge = d.WeightCharge;
        ServerAmountToPay = d.AmountToPay;
        UpdateParcelStatus();
    }

    /// <summary>Sets the parcel section's guidance text for the current stage.</summary>
    private void UpdateParcelStatus()
    {
        if (ParcelChargePaid)
            ParcelStatusText = "Paid. Your rider can now collect the parcel.";
        else if (HasDriver)
            ParcelStatusText = "A rider has accepted. Enter your parcel weight to see the total and pay.";
        else
            ParcelStatusText = "Waiting for a rider to accept your booking.";
    }

    /// <summary>
    /// Submits the weight so the server can price it, then opens the payment panel for the
    /// server-computed total (ride + parcel).
    /// </summary>
    [RelayCommand]
    private async Task PrepareParcelPaymentAsync()
    {
        if (!CanPrepare)
        {
            ParcelStatusText = "Enter your parcel weight in grams to continue.";
            return;
        }

        try
        {
            ErrorMessage = null;
            await _api.SubmitParcelWeightAsync(DeliveryId, WeightGrams);
            // Re-fetch: the weight charge and the combined total are computed server-side.
            await LoadDeliveryAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ParcelStatusText = "We couldn't price your parcel just now. Please try again.";
            return;
        }

        Payment.Reset(TotalPayable);
        ParcelReady = true;
        ParcelStatusText =
            $"Ride {RideFeeText} + parcel {ParcelFeeText} = {TotalPayableText}. Pay to confirm the pickup.";
    }

    /// <summary>
    /// The customer paid the combined total. Confirm it against the delivery so the payment is
    /// recorded server-side and the rider is cleared to collect.
    /// </summary>
    private async void OnParcelPaid()
    {
        try
        {
            await _api.ConfirmPaymentAsync(DeliveryId);
            ParcelChargePaid = true;
            ParcelReady = false;
            ParcelStatusText =
                $"Paid {TotalPayableText} · {Payment.TransactionReference}. Your rider can now collect the parcel.";
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

    // Auto-refresh: while the tracking page is visible, poll the tracking snapshot every few
    // seconds so the map keeps up with the driver even without a live SignalR connection.
    protected override TimeSpan AutoRefreshInterval => PollInterval;
    protected override Task AutoRefreshAsync() => LoadSnapshotAsync();

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
            // A rider has accepted — prompt the customer to weigh the parcel and pay.
            UpdateParcelStatus();
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
        MaybeUpdateRouteToDestination(lat, lng);
    }

    /// <summary>
    /// Recomputes the road route from the driver's current position to the destination as the
    /// driver moves, and refines ETA/remaining distance from it. Throttled by
    /// <see cref="RouteRefreshMeters"/> and by a single in-flight request so a fast ping stream
    /// doesn't spam the Directions API.
    /// </summary>
    private async void MaybeUpdateRouteToDestination(double lat, double lng)
    {
        if (DestinationLatitude == 0 && DestinationLongitude == 0) return;
        if (_routeBusy) return;

        var origin = new Location(lat, lng);
        if (_lastRouteOrigin is not null &&
            Location.CalculateDistance(_lastRouteOrigin, origin, DistanceUnits.Kilometers) * 1000 < RouteRefreshMeters)
            return;

        _routeBusy = true;
        try
        {
            var route = await _directions.GetRouteAsync(lat, lng, DestinationLatitude, DestinationLongitude);
            if (route is { Points.Count: > 1 })
            {
                _lastRouteOrigin = origin;
                EtaText = $"ETA: {route.DurationMinutes:F0} min";
                RemainingText = $"Remaining: {route.DistanceKm:F2} km";
                MainThread.BeginInvokeOnMainThread(() => RouteToDestinationReady?.Invoke(route.Points));
            }
        }
        catch
        {
            // No key / transient failure — the breadcrumb trail and pins still track the driver.
        }
        finally
        {
            _routeBusy = false;
        }
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
