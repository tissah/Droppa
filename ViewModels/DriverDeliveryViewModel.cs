using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Services;
using Droppa.Services.Api;
using Droppa.Services.Maps;
using Microsoft.Maui.Devices.Sensors;

namespace Droppa.ViewModels;

/// <summary>
/// One selectable route between pickup and drop-off, shown under the map as a chip — the same
/// "25 min · 12.6 km" summary Google Maps prints beside each alternative.
/// </summary>
/// <param name="DurationText">e.g. "25 min".</param>
/// <param name="DistanceText">e.g. "12.6 km".</param>
/// <param name="ViaText">e.g. "via M1", when Google supplied a summary.</param>
/// <param name="IsChosen">True for the shortest route — the one drawn as the solid blue line.</param>
public record RouteOption(string DurationText, string DistanceText, string ViaText, bool IsChosen);

/// <summary>
/// The driver's active-delivery screen. While open, it shares the driver's live GPS with
/// the customer (one ping every few seconds, tagged to this delivery) and lets the driver
/// advance the status: collected → in transit → delivered. Stopping the page stops sharing.
///
/// The advance actions are strictly chronological: exactly one step is offered at a time, and
/// the next one only appears once the server has confirmed the previous one. Straight after
/// acceptance the only action is "collected" (preceded, on a Send, by weighing the parcel).
/// </summary>
[QueryProperty(nameof(DeliveryId), "deliveryId")]
public partial class DriverDeliveryViewModel : BaseViewModel
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

    // The driver has to move at least this far from the point the live route was last computed
    // before we re-query the Directions API. Keeps the preview smooth without firing a routing
    // request on every 5-second ping.
    private const double RouteRefreshMeters = 120;

    // API DeliveryStatus values reused here.
    private const int StatusAccepted = 2;
    private const int StatusParcelCollected = 5;
    private const int StatusInTransit = 6;
    private const int StatusDelivered = 8;

    private readonly DroppaApiClient _api;
    private readonly ILocationService _location;
    private readonly IDirectionsService _directions;
    private readonly IPaymentService _payments;
    private readonly ICourierRepository _couriers;
    private readonly IAuthService _auth;
    private CancellationTokenSource? _pingCts;

    // Throttling state for the live route from the driver's position to the current target.
    private Location? _lastRouteOrigin;
    private bool _routeBusy;

    // The driver's most recent GPS fix, so we can reroute immediately on collection without
    // waiting for the next ping.
    private Location? _lastDriverLocation;

    public DriverDeliveryViewModel(
        DroppaApiClient api,
        ILocationService location,
        IDirectionsService directions,
        IPaymentService payments,
        ICourierRepository couriers,
        IAuthService auth)
    {
        _api = api;
        _location = location;
        _directions = directions;
        _payments = payments;
        _couriers = couriers;
        _auth = auth;
        Title = "Active delivery";
    }

    /// <summary>Full name of the signed-in driver, shown in the top-right of the page header.</summary>
    public string UserName => _auth.CurrentUser?.FullName ?? "Driver";

    [ObservableProperty] private int _deliveryId;
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _categoryLabel = string.Empty;
    [ObservableProperty] private string _courierName = string.Empty;
    [ObservableProperty] private string _pickupText = string.Empty;
    [ObservableProperty] private string _destinationText = string.Empty;
    [ObservableProperty] private string _statusText = "Accepted";
    [ObservableProperty] private string _sharingText = "Live location is off.";
    [ObservableProperty] private string _routeInfoText = "Calculating route…";
    [ObservableProperty] private string? _errorMessage;

    // ---- Courier remittance ----
    // The amount the customer entered as owed at the courier office (Receive only), which the
    // driver transfers to the courier's number on collection. This is NOT the distance ride fee.
    [ObservableProperty] private decimal _transferAmount;
    [ObservableProperty] private string? _courierPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTransfer))]
    private bool _isTransferred;

    [ObservableProperty] private string _transferStatusText = string.Empty;

    public string TransferAmountText => $"MWK {TransferAmount:N0}";
    partial void OnTransferAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(TransferAmountText));
        OnPropertyChanged(nameof(ShowRemitSection));
    }

    public bool HasCourierPhone => !string.IsNullOrWhiteSpace(CourierPhone);
    partial void OnCourierPhoneChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCourierPhone));
        OnPropertyChanged(nameof(CanTransfer));
    }

    /// <summary>True while a remittance can still be sent (a number is on file and it hasn't been sent yet).</summary>
    public bool CanTransfer => HasCourierPhone && !IsTransferred;

    // ---- Parcel collection gate ----
    // The driver no longer weighs the parcel: the customer enters the weight and pays the combined
    // total (ride + parcel) in their own module. Paying is what unlocks collection for a Send.

    /// <summary>
    /// True for a Receive delivery (courier → customer): the driver collects straight from the
    /// courier. A Send is collected from the customer, once they've weighed the parcel and paid.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSend))]
    [NotifyPropertyChangedFor(nameof(CanMarkCollected))]
    [NotifyPropertyChangedFor(nameof(ShowCollectStep))]
    [NotifyPropertyChangedFor(nameof(ShowAdvanceSection))]
    [NotifyPropertyChangedFor(nameof(StepHintText))]
    private bool _isReceive;

    /// <summary>True for a Send delivery (customer → courier).</summary>
    public bool IsSend => !IsReceive;

    /// <summary>
    /// When can the parcel be marked collected? For a Send, the customer must have entered the parcel
    /// weight and paid the combined total first ("no pickup without the customer's payment"); a
    /// Receive has no weight/payment step, so it can be collected straight away.
    /// </summary>
    public bool CanMarkCollected => IsReceive || ParcelChargePaid;

    /// <summary>
    /// True once the customer has entered the parcel weight and paid the combined total (Send).
    /// This — not the driver weighing — unlocks collection. Receive deliveries have no such step.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkCollected))]
    [NotifyPropertyChangedFor(nameof(ShowCollectStep))]
    [NotifyPropertyChangedFor(nameof(ShowAdvanceSection))]
    [NotifyPropertyChangedFor(nameof(StepHintText))]
    private bool _parcelChargePaid;

    // ---- Chronological step gate ----
    // The delivery's current server-confirmed status drives which single action is offered.
    // Nothing is shown speculatively: each step only appears after the previous one came back OK.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollected))]
    [NotifyPropertyChangedFor(nameof(ShowCollectStep))]
    [NotifyPropertyChangedFor(nameof(ShowInTransitStep))]
    [NotifyPropertyChangedFor(nameof(ShowDeliveredStep))]
    [NotifyPropertyChangedFor(nameof(ShowAdvanceSection))]
    [NotifyPropertyChangedFor(nameof(ShowRemitSection))]
    [NotifyPropertyChangedFor(nameof(StepHintText))]
    private int _deliveryStatus = StatusAccepted;

    /// <summary>Step 1 — the parcel is ready to collect (Send: the customer has paid), so it can be marked.</summary>
    public bool ShowCollectStep => DeliveryStatus < StatusParcelCollected && CanMarkCollected;

    /// <summary>Step 2 — the parcel is collected, so the trip can be started.</summary>
    public bool ShowInTransitStep =>
        DeliveryStatus >= StatusParcelCollected && DeliveryStatus < StatusInTransit;

    /// <summary>Step 3 — the trip is under way, so it can be completed.</summary>
    public bool ShowDeliveredStep =>
        DeliveryStatus >= StatusInTransit && DeliveryStatus < StatusDelivered;

    /// <summary>Hides the whole "advance" block when there is nothing to do yet (or nothing left).</summary>
    public bool ShowAdvanceSection => ShowCollectStep || ShowInTransitStep || ShowDeliveredStep;

    /// <summary>
    /// The remit-to-courier card appears only once the parcel is in hand AND there's actually an
    /// amount to remit — the customer-entered courier-office amount (Receive only). Send deliveries,
    /// and receives with nothing owed, show no remittance.
    /// </summary>
    public bool ShowRemitSection => DeliveryStatus >= StatusParcelCollected && TransferAmount > 0;

    /// <summary>One line telling the driver what the current step is.</summary>
    public string StepHintText => DeliveryStatus switch
    {
        >= StatusDelivered => "Delivered. Nothing further to do.",
        >= StatusInTransit => "Step 3 of 3 — complete the drop-off, then mark the parcel delivered.",
        >= StatusParcelCollected => "Step 2 of 3 — head to the drop-off and mark the trip in transit.",
        _ when IsSend && !ParcelChargePaid =>
            "Waiting for the customer to enter the parcel weight and pay. Collection unlocks once they've paid.",
        _ => "Step 1 of 3 — collect the parcel to start the trip."
    };

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    // Raw coordinates + computed route for the map.
    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }
    public double DestinationLatitude { get; private set; }
    public double DestinationLongitude { get; private set; }
    public IReadOnlyList<Location>? RoutePoints { get; private set; }

    /// <summary>
    /// The pickup → drop-off alternatives drawn once the parcel is collected, shortest first.
    /// Index 0 is the chosen route; the rest are the lighter lines behind it.
    /// </summary>
    public IReadOnlyList<RouteResult> CollectedRoutes { get; private set; } = Array.Empty<RouteResult>();

    /// <summary>Summary chips for the routes above, shown under the map.</summary>
    public ObservableCollection<RouteOption> RouteOptions { get; } = new();

    /// <summary>True once the chips have something to show, so the strip can stay hidden until then.</summary>
    [ObservableProperty] private bool _hasRouteOptions;

    /// <summary>
    /// True once pickup/destination (and, when available, the route line) have been computed.
    /// Lets the page re-draw the map every time it reappears instead of only once.
    /// </summary>
    public bool HasRoute { get; private set; }

    /// <summary>
    /// True once the parcel has been collected. From this point the map previews the live route
    /// from the driver's current position to the destination instead of the pickup→destination
    /// overview.
    /// </summary>
    public bool IsCollected => DeliveryStatus >= StatusParcelCollected;

    // Guards <see cref="EnterCollectedState"/> so the map only switches legs once.
    private bool _collectedRouteApplied;

    /// <summary>Raised once pickup/destination/route are known so the map can render them.</summary>
    public event Action? RouteReady;

    /// <summary>Raised on every GPS ping with the driver's current lat/lng so the map can move the marker.</summary>
    public event Action<double, double>? DriverPositionChanged;

    /// <summary>Raised the moment the parcel is collected, so the map can switch to live-route mode.</summary>
    public event Action? DeliveryCollected;

    /// <summary>
    /// Raised once the parcel is collected with the full pickup → drop-off route set (shortest
    /// first). The page draws it Google-Maps style: the chosen route as a solid blue line, the
    /// alternatives behind it in a lighter shade, framed so the whole journey is visible.
    /// </summary>
    public event Action<IReadOnlyList<RouteResult>>? CollectedRoutesReady;

    /// <summary>
    /// Raised with the road-snapped route from the driver's current position to the current
    /// target — the pickup while heading out to collect, the destination once the parcel is
    /// collected — recomputed as the driver moves. The page draws it as the live route.
    /// </summary>
    public event Action<IReadOnlyList<Location>>? LiveRouteReady;

    partial void OnDeliveryIdChanged(int value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (DeliveryId == 0) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var mine = await _api.GetDriverDeliveriesAsync();
            var d = mine.FirstOrDefault(x => x.Id == DeliveryId);
            if (d is null)
            {
                ErrorMessage = "This delivery is no longer assigned to you.";
                return;
            }

            Reference = d.Reference;
            IsReceive = d.ServiceType == 2;
            CategoryLabel = IsReceive ? "Receive parcel" : "Send parcel";
            CourierName = d.CourierServiceName;
            PickupLatitude = d.PickupLatitude;
            PickupLongitude = d.PickupLongitude;
            DestinationLatitude = d.DestinationLatitude;
            DestinationLongitude = d.DestinationLongitude;
            PickupText = $"{d.PickupLatitude:F5}, {d.PickupLongitude:F5}";
            DestinationText = $"{d.DestinationLatitude:F5}, {d.DestinationLongitude:F5}";
            DeliveryStatus = d.Status;
            StatusText = StatusLabel(d.Status);
            ParcelChargePaid = d.ParcelChargePaid;

            // If the parcel was already collected before this screen opened, start in live-route mode.
            if (d.Status >= StatusParcelCollected && d.Status < StatusDelivered)
                EnterCollectedState();

            // Remit the customer-entered courier-office amount (COD / handling) — NOT the distance
            // ride fee. Send deliveries carry none, so this is zero and the remit card stays hidden.
            TransferAmount = d.CourierAmount ?? 0m;
            await LoadCourierPayoutAsync(d.CourierServiceName);

            await LoadRouteAsync();
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

    private async Task LoadRouteAsync()
    {
        var route = await _directions.GetRouteAsync(
            PickupLatitude, PickupLongitude, DestinationLatitude, DestinationLongitude);

        if (route is not null)
        {
            RoutePoints = route.Points;
            // Once collected, the pickup → drop-off route set owns this line; don't overwrite it.
            if (!IsCollected)
            {
                var mode = route.TravelMode == "two_wheeler" ? "motorcycle" : route.TravelMode;
                RouteInfoText = $"Shortest {mode} route · {route.DistanceKm:F1} km · ~{route.DurationMinutes:F0} min";
            }
        }
        else if (!IsCollected)
        {
            // No key configured or no route returned — the map still shows pickup/destination pins.
            RouteInfoText = "Route preview unavailable.";
        }

        HasRoute = true;
        RouteReady?.Invoke();
    }

    /// <summary>
    /// Looks up the courier's payout number from the catalogue (matched by name) so the driver
    /// can remit the fee. If the courier has no number on file, the transfer card stays disabled.
    /// </summary>
    private async Task LoadCourierPayoutAsync(string courierName)
    {
        try
        {
            var couriers = await _couriers.GetAllAsync();
            var match = couriers.FirstOrDefault(c =>
                string.Equals(c.Name, courierName, StringComparison.OrdinalIgnoreCase));

            CourierPhone = match?.PhoneNumber;
            TransferStatusText = HasCourierPhone
                ? $"Remit {TransferAmountText} to {CourierName} · {CourierPhone}"
                : "This courier has no payout number on file.";
        }
        catch
        {
            // Non-fatal: the delivery still works; the driver just can't remit from here.
            CourierPhone = null;
            TransferStatusText = "Courier payout number unavailable.";
        }
    }

    [RelayCommand]
    private async Task TransferToCourierAsync()
    {
        if (IsBusy || !CanTransfer) return;
        if (string.IsNullOrWhiteSpace(CourierPhone)) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Transfer to courier",
            $"Transfer {TransferAmountText} to {CourierName} ({CourierPhone})?",
            "Transfer", "Cancel");
        if (!confirm) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            TransferStatusText = $"Transferring {TransferAmountText}…";

            var result = await _payments.TransferAsync(CourierPhone, TransferAmount);
            if (result.Success)
            {
                IsTransferred = true;
                TransferStatusText = $"Transferred {TransferAmountText} to {CourierName} · {result.TransactionId}";
            }
            else
            {
                TransferStatusText = result.Message ?? "Transfer failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            TransferStatusText = "Transfer failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Begins the background GPS ping loop. Called when the page appears.</summary>
    public void StartLocationSharing()
    {
        if (_pingCts is not null || DeliveryId == 0) return;
        _pingCts = new CancellationTokenSource();
        SharingText = "Sharing live location with the customer…";
        _ = RunPingLoopAsync(_pingCts.Token);
    }

    /// <summary>Stops the GPS ping loop. Called when the page disappears.</summary>
    public void StopLocationSharing()
    {
        _pingCts?.Cancel();
        _pingCts?.Dispose();
        _pingCts = null;
        SharingText = "Live location is off.";
    }

    // Poll while the page is visible so the driver picks up the customer's payment (which unlocks
    // collection) and any server status change without having to reopen the screen.
    protected override TimeSpan AutoRefreshInterval => TimeSpan.FromSeconds(5);
    protected override async Task AutoRefreshAsync()
    {
        if (DeliveryId == 0) return;
        try
        {
            var mine = await _api.GetDriverDeliveriesAsync();
            var d = mine.FirstOrDefault(x => x.Id == DeliveryId);
            if (d is null) return;

            ParcelChargePaid = d.ParcelChargePaid;
            if (d.Status != DeliveryStatus)
            {
                DeliveryStatus = d.Status;
                StatusText = StatusLabel(d.Status);
                if (d.Status >= StatusParcelCollected && d.Status < StatusDelivered)
                    EnterCollectedState();
            }
        }
        catch
        {
            // Transient — try again on the next tick.
        }
    }

    private async Task RunPingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var loc = await _location.GetCurrentLocationAsync(ct);
                if (loc is not null)
                {
                    await _api.UpdateDriverLocationAsync(new UpdateLocationDto
                    {
                        Latitude = loc.Latitude,
                        Longitude = loc.Longitude,
                        DeliveryRequestId = DeliveryId
                    }, ct);

                    // Reflect the new position on the driver's own map and refresh the live route
                    // to the current target: the pickup while heading out to collect, the
                    // destination once the parcel has been collected.
                    var (lat, lng) = (loc.Latitude, loc.Longitude);
                    _lastDriverLocation = new Location(lat, lng);
                    MainThread.BeginInvokeOnMainThread(() => DriverPositionChanged?.Invoke(lat, lng));
                    MaybeUpdateLiveRoute(lat, lng);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient GPS/network error — keep trying on the next tick.
            }

            try { await Task.Delay(PingInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    [RelayCommand]
    private async Task MarkCollectedAsync()
    {
        // Belt-and-braces: for a Send the customer must have paid (which covers their parcel weight)
        // before collection. The button is already hidden until then via ShowCollectStep.
        if (!CanMarkCollected) return;
        await SetStatusAsync(StatusParcelCollected);
    }

    [RelayCommand]
    private async Task MarkInTransitAsync()
    {
        if (!ShowInTransitStep) return;
        await SetStatusAsync(StatusInTransit);
    }

    [RelayCommand]
    private async Task MarkDeliveredAsync()
    {
        if (!ShowDeliveredStep) return;
        await SetStatusAsync(StatusDelivered);
        if (!HasError)
        {
            StopLocationSharing();
            // Return to the trips hub so the driver can carry on with their other deliveries.
            await Shell.Current.GoToAsync("..");
        }
    }

    private async Task SetStatusAsync(int status)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _api.UpdateDeliveryStatusAsync(new UpdateDeliveryStatusDto
            {
                DeliveryRequestId = DeliveryId,
                Status = status
            });

            // Only now — after the server confirmed it — does the step advance and the next
            // button appear. A failed call leaves the driver on the current step.
            DeliveryStatus = status;
            StatusText = StatusLabel(status);

            // Once collected (and still in progress), the map previews the live route to the drop-off.
            if (status >= StatusParcelCollected && status < StatusDelivered)
                EnterCollectedState();
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
    /// The parcel has been collected: the map stops previewing the trip out to the pickup and
    /// switches to the full journey the driver now has to make — pickup (source) → drop-off —
    /// drawn the way Google Maps shows directions, with the alternatives behind the chosen route.
    /// </summary>
    private void EnterCollectedState()
    {
        if (_collectedRouteApplied) return;
        _collectedRouteApplied = true;
        _lastRouteOrigin = null;
        RouteInfoText = "Loading the route to the drop-off…";
        DeliveryCollected?.Invoke();
        _ = LoadCollectedRoutesAsync();
    }

    /// <summary>
    /// Fetches every pickup → destination alternative and hands them to the map, shortest first.
    /// Falls back to the overview route computed on load if the Directions API gives us nothing.
    /// </summary>
    private async Task LoadCollectedRoutesAsync()
    {
        try
        {
            var routes = await _directions.GetRoutesAsync(
                PickupLatitude, PickupLongitude, DestinationLatitude, DestinationLongitude);

            if (routes.Count == 0)
            {
                // No key or no answer — keep whatever the load-time overview drew.
                RouteInfoText = RoutePoints is { Count: > 1 }
                    ? "Route to the drop-off (offline estimate)."
                    : "Route preview unavailable.";
                return;
            }

            CollectedRoutes = routes;

            var best = routes[0];
            var mode = best.TravelMode == "two_wheeler" ? "motorcycle" : best.TravelMode;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                RouteInfoText =
                    $"Pickup → drop-off · {mode} · {best.DistanceKm:F1} km · ~{best.DurationMinutes:F0} min";

                RouteOptions.Clear();
                for (var i = 0; i < routes.Count; i++)
                {
                    var r = routes[i];
                    RouteOptions.Add(new RouteOption(
                        $"{r.DurationMinutes:F0} min",
                        $"{r.DistanceKm:F1} km",
                        string.IsNullOrWhiteSpace(r.Summary) ? string.Empty : $"via {r.Summary}",
                        i == 0));
                }
                HasRouteOptions = RouteOptions.Count > 0;
                CollectedRoutesReady?.Invoke(routes);
            });
        }
        catch
        {
            // Transient — the pins and the driver marker still work, so don't disturb the driver.
            RouteInfoText = "Route preview unavailable.";
        }
    }

    /// <summary>
    /// Recomputes the road route from the driver's current position to the pickup and raises it for
    /// the map, so the driver can see the way out to the collection point. Throttled by
    /// <see cref="RouteRefreshMeters"/> and a single in-flight request so the 5-second ping stream
    /// doesn't spam the Directions API. Once the parcel is collected this stops: the map then shows
    /// the fixed pickup → drop-off journey instead, and only the driver marker keeps moving.
    /// </summary>
    private async void MaybeUpdateLiveRoute(double lat, double lng)
    {
        if (IsCollected) return;

        var targetLat = PickupLatitude;
        var targetLng = PickupLongitude;
        if (targetLat == 0 && targetLng == 0) return;
        if (_routeBusy) return;

        var origin = new Location(lat, lng);
        if (_lastRouteOrigin is not null &&
            Location.CalculateDistance(_lastRouteOrigin, origin, DistanceUnits.Kilometers) * 1000 < RouteRefreshMeters)
            return;

        _routeBusy = true;
        try
        {
            var route = await _directions.GetRouteAsync(lat, lng, targetLat, targetLng);
            if (route is { Points.Count: > 1 })
            {
                _lastRouteOrigin = origin;
                var mode = route.TravelMode == "two_wheeler" ? "motorcycle" : route.TravelMode;
                RouteInfoText = $"To pickup · {mode} · {route.DistanceKm:F1} km · ~{route.DurationMinutes:F0} min";
                var points = route.Points;
                MainThread.BeginInvokeOnMainThread(() => LiveRouteReady?.Invoke(points));
            }
        }
        catch
        {
            // No key / transient failure — the driver marker still tracks the position.
        }
        finally
        {
            _routeBusy = false;
        }
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
}
