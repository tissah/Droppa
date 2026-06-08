using Droppa.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace Droppa.Views;

public partial class DriverDeliveryPage : ContentPage
{
    private readonly DriverDeliveryViewModel _vm;

    public DriverDeliveryPage(DriverDeliveryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        // Subscribed for the page's lifetime — we deliberately do NOT unsubscribe on
        // disappearing, so a route that finishes computing while the page is hidden still
        // gets drawn when the driver comes back.
        _vm.RouteReady += OnRouteReady;
    }

    private void OnRouteReady() => MainThread.BeginInvokeOnMainThread(RenderRoute);

    /// <summary>
    /// Draws pickup/destination pins and the route line. Idempotent: it clears and rebuilds
    /// the map overlay each call, so it's safe to run on first load and every time the page
    /// reappears. This is what stops the route from vanishing when the driver leaves the page.
    /// </summary>
    private void RenderRoute()
    {
        if (!_vm.HasRoute) return;

        RouteMap.Pins.Clear();
        RouteMap.MapElements.Clear();

        var pickup = new Location(_vm.PickupLatitude, _vm.PickupLongitude);
        var dest = new Location(_vm.DestinationLatitude, _vm.DestinationLongitude);

        RouteMap.Pins.Add(new Pin { Label = "Pickup", Type = PinType.Place, Location = pickup });
        RouteMap.Pins.Add(new Pin { Label = "Destination", Type = PinType.Place, Location = dest });

        // The shortest motorcycle route, when the Directions API returned one.
        if (_vm.RoutePoints is { Count: > 1 } points)
        {
            var line = new Polyline { StrokeColor = Colors.MediumVioletRed, StrokeWidth = 6 };
            foreach (var p in points)
                line.Geopath.Add(p);
            RouteMap.MapElements.Add(line);
        }

        // Frame the map around the pickup→destination span.
        var centre = new Location((pickup.Latitude + dest.Latitude) / 2, (pickup.Longitude + dest.Longitude) / 2);
        var radiusKm = Math.Max(1, Location.CalculateDistance(pickup, dest, DistanceUnits.Kilometers));
        RouteMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartClock();
        _vm.StartLocationSharing();
        // Re-draw the route if it was already computed — leaving and returning to the page
        // must not lose the map overlay.
        RenderRoute();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
        // Only pause live GPS sharing while the page is hidden. The computed route and our
        // RouteReady subscription are kept so everything is still here when the driver returns.
        _vm.StopLocationSharing();
    }
}
