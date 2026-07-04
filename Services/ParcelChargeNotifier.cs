using CommunityToolkit.Maui.Views;
using Droppa.Services.Api;
using Droppa.ViewModels;
using Droppa.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Droppa.Services;

/// <summary>
/// App-wide listener that, while a customer is signed in, watches the tracking hub for the
/// driver sending a weighed-parcel fee (<see cref="ITrackingService.ParcelChargeRequested"/>).
/// When one arrives it re-fetches the delivery for the authoritative charge and pops up a
/// confirm-and-pay dialog over whatever screen the customer is on.
/// </summary>
public class ParcelChargeNotifier
{
    private readonly ITrackingService _tracking;
    private readonly DroppaApiClient _api;
    private readonly IServiceProvider _services;
    private bool _started;
    private bool _showing;

    public ParcelChargeNotifier(ITrackingService tracking, DroppaApiClient api, IServiceProvider services)
    {
        _tracking = tracking;
        _api = api;
        _services = services;
    }

    /// <summary>Begins listening (and opens the hub connection). Idempotent; call after a customer signs in.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _tracking.ParcelChargeRequested += OnParcelChargeRequested;
        _ = SafeConnectAsync();
    }

    /// <summary>Stops listening. Call on sign-out.</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _tracking.ParcelChargeRequested -= OnParcelChargeRequested;
    }

    private async Task SafeConnectAsync()
    {
        try { await _tracking.EnsureConnectedAsync(); }
        catch { /* the hub auto-reconnects; the popup still works once connected */ }
    }

    private async void OnParcelChargeRequested(ParcelChargeRequest request)
    {
        if (_showing) return; // one fee popup at a time
        try
        {
            var delivery = await _api.GetDeliveryAsync(request.DeliveryRequestId);
            if (delivery.ParcelCharge is not > 0 || delivery.ParcelChargePaid) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var page = Shell.Current?.CurrentPage;
                if (page is null) return;

                var popup = _services.GetRequiredService<ParcelChargePopup>();
                popup.Closed += (_, _) => _showing = false;
                ((ParcelChargeViewModel)popup.BindingContext).Load(delivery);

                _showing = true;
                page.ShowPopup(popup);
            });
        }
        catch
        {
            _showing = false; // best effort — let a later event try again
        }
    }
}
