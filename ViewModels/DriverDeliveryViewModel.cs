using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Services;
using Droppa.Services.Api;
using Droppa.Services.Maps;
using Microsoft.Maui.Devices.Sensors;

namespace Droppa.ViewModels;

/// <summary>
/// The driver's active-delivery screen. While open, it shares the driver's live GPS with
/// the customer (one ping every few seconds, tagged to this delivery) and lets the driver
/// advance the status: collected → in transit → delivered. Stopping the page stops sharing.
/// </summary>
[QueryProperty(nameof(DeliveryId), "deliveryId")]
public partial class DriverDeliveryViewModel : BaseViewModel
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

    // API DeliveryStatus values reused here.
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
    // The fee the courier charged the customer, which the driver transfers to the courier's number.
    [ObservableProperty] private decimal _transferAmount;
    [ObservableProperty] private string? _courierPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTransfer))]
    private bool _isTransferred;

    [ObservableProperty] private string _transferStatusText = string.Empty;

    public string TransferAmountText => $"MWK {TransferAmount:N0}";
    partial void OnTransferAmountChanged(decimal value) => OnPropertyChanged(nameof(TransferAmountText));

    public bool HasCourierPhone => !string.IsNullOrWhiteSpace(CourierPhone);
    partial void OnCourierPhoneChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCourierPhone));
        OnPropertyChanged(nameof(CanTransfer));
    }

    /// <summary>True while a remittance can still be sent (a number is on file and it hasn't been sent yet).</summary>
    public bool CanTransfer => HasCourierPhone && !IsTransferred;

    // ---- Parcel weight charge ----
    // After accepting, the driver weighs the parcel; the charge (incl. VAT) is sent to the customer
    // to confirm and pay as a separate, second payment.
    // Bound to the weight Entry as text so the field starts empty (not "0") and partial typing
    // never fails a double conversion. The numeric weight is parsed from it on demand.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParcelWeightAmountText))]
    [NotifyPropertyChangedFor(nameof(ParcelVatText))]
    [NotifyPropertyChangedFor(nameof(ParcelChargeText))]
    [NotifyPropertyChangedFor(nameof(CanSetWeight))]
    private string? _weightText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetWeight))]
    [NotifyPropertyChangedFor(nameof(CanEditWeight))]
    [NotifyPropertyChangedFor(nameof(CanMarkCollected))]
    private bool _isParcelWeightSet;

    [ObservableProperty] private string _parcelStatusText =
        "Weigh the parcel, then send the parcel fee to the customer.";

    /// <summary>Parsed parcel weight in grams, or 0 when the field is empty/invalid.</summary>
    public double WeightGrams => double.TryParse(WeightText, out var g) && g > 0 ? g : 0;

    public string ParcelWeightAmountText => $"MWK {ParcelPricing.WeightAmount(WeightGrams):N0}";
    public string ParcelVatText => $"MWK {ParcelPricing.Vat(ParcelPricing.WeightAmount(WeightGrams)):N0}";
    public string ParcelChargeText => $"MWK {ParcelPricing.Total(WeightGrams):N0}";

    /// <summary>The weight field stays editable until the fee has been sent.</summary>
    public bool CanEditWeight => !IsParcelWeightSet;

    /// <summary>The driver can send the charge once a positive weight is entered and it hasn't been sent yet.</summary>
    public bool CanSetWeight => WeightGrams > 0 && !IsParcelWeightSet;

    /// <summary>
    /// The parcel can't be marked collected (picked up) until it has been weighed and the fee sent.
    /// This enforces "no pickup without a parcel weight".
    /// </summary>
    public bool CanMarkCollected => IsParcelWeightSet;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    // Raw coordinates + computed route for the map.
    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }
    public double DestinationLatitude { get; private set; }
    public double DestinationLongitude { get; private set; }
    public IReadOnlyList<Location>? RoutePoints { get; private set; }

    /// <summary>
    /// True once pickup/destination (and, when available, the route line) have been computed.
    /// Lets the page re-draw the map every time it reappears instead of only once.
    /// </summary>
    public bool HasRoute { get; private set; }

    /// <summary>Raised once pickup/destination/route are known so the map can render them.</summary>
    public event Action? RouteReady;

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
            CategoryLabel = d.ServiceType == 2 ? "Receive parcel" : "Send parcel";
            CourierName = d.CourierServiceName;
            PickupLatitude = d.PickupLatitude;
            PickupLongitude = d.PickupLongitude;
            DestinationLatitude = d.DestinationLatitude;
            DestinationLongitude = d.DestinationLongitude;
            PickupText = $"{d.PickupLatitude:F5}, {d.PickupLongitude:F5}";
            DestinationText = $"{d.DestinationLatitude:F5}, {d.DestinationLongitude:F5}";
            StatusText = StatusLabel(d.Status);

            TransferAmount = d.TotalFee;
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
            var mode = route.TravelMode == "two_wheeler" ? "motorcycle" : route.TravelMode;
            RouteInfoText = $"Shortest {mode} route · {route.DistanceKm:F1} km · ~{route.DurationMinutes:F0} min";
        }
        else
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

    /// <summary>
    /// Weighs the parcel, computes the charge (weight × rate, floored at the minimum, plus VAT),
    /// and sends it to the customer to confirm and pay as the second payment.
    /// </summary>
    [RelayCommand]
    private async Task SubmitParcelWeightAsync()
    {
        if (IsBusy || !CanSetWeight) return;

        var charge = ParcelPricing.Total(WeightGrams);
        var confirm = await Shell.Current.DisplayAlert(
            "Send parcel fee",
            $"Send a parcel fee of {ParcelChargeText} (incl. VAT) to the customer for a {WeightGrams:N0} g parcel?",
            "Send", "Cancel");
        if (!confirm) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ParcelStatusText = $"Sending {ParcelChargeText} to the customer…";

            await _api.SetParcelWeightAsync(new SetParcelWeightDto
            {
                DeliveryRequestId = DeliveryId,
                WeightGrams = WeightGrams,
                ParcelCharge = charge
            });

            IsParcelWeightSet = true;
            ParcelStatusText = $"Parcel fee {ParcelChargeText} sent to the customer to pay.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ParcelStatusText = "Could not send the parcel fee. Please try again.";
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
        // Guard: the parcel can't be picked up until it has been weighed and the fee sent.
        if (!IsParcelWeightSet)
        {
            ParcelStatusText = "Weigh the parcel and send the fee before marking it collected.";
            return;
        }
        await SetStatusAsync(StatusParcelCollected);
    }

    [RelayCommand] private Task MarkInTransitAsync() => SetStatusAsync(StatusInTransit);

    [RelayCommand]
    private async Task MarkDeliveredAsync()
    {
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
            StatusText = StatusLabel(status);
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
