using Droppa.Services.Maps;
using Droppa.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace Droppa.Views;

public partial class DriverDeliveryPage : ContentPage
{
    // Google-Maps-style directions colours: the chosen route is a solid deep blue, the
    // alternatives sit behind it in a washed-out blue.
    private static readonly Color ChosenRouteColor = Color.FromArgb("#2B22CE");
    private static readonly Color AlternateRouteColor = Color.FromArgb("#A3B6EE");

    private readonly DriverDeliveryViewModel _vm;

    private Pin? _pickupPin;
    private Pin? _destinationPin;
    private Pin? _driverPin;
    private Polyline? _previewRoute;   // pickup → destination overview, shown until the live route is available
    private Polyline? _liveRoute;      // driver → pickup, refreshed as the driver moves out to collect

    // The pickup → drop-off directions drawn once the parcel is collected: the alternatives first,
    // then the chosen route on top of them.
    private readonly List<Polyline> _collectedRouteLines = new();

    public DriverDeliveryPage(DriverDeliveryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        // Subscribed for the page's lifetime — we deliberately do NOT unsubscribe on
        // disappearing, so map work that finishes while the page is hidden still gets drawn
        // when the driver comes back.
        _vm.RouteReady += OnRouteReady;
        _vm.DriverPositionChanged += OnDriverPositionChanged;
        _vm.DeliveryCollected += OnDeliveryCollected;
        _vm.LiveRouteReady += OnLiveRouteReady;
        _vm.CollectedRoutesReady += OnCollectedRoutesReady;
    }

    private void OnRouteReady() => MainThread.BeginInvokeOnMainThread(RenderRoute);

    /// <summary>
    /// Draws pickup/destination pins and — before the parcel is collected — the pickup→destination
    /// route overview. Idempotent: it moves existing pins rather than duplicating them, so it's safe
    /// to run on first load and every time the page reappears. This is what stops the map from
    /// losing its overlay when the driver leaves and returns.
    /// </summary>
    private void RenderRoute()
    {
        if (!_vm.HasRoute) return;

        var pickup = new Location(_vm.PickupLatitude, _vm.PickupLongitude);
        var dest = new Location(_vm.DestinationLatitude, _vm.DestinationLongitude);

        if (_pickupPin is null)
        {
            _pickupPin = new Pin { Label = "Pickup", Type = PinType.Place, Location = pickup };
            RouteMap.Pins.Add(_pickupPin);
        }
        else _pickupPin.Location = pickup;

        if (_destinationPin is null)
        {
            _destinationPin = new Pin { Label = "Destination", Type = PinType.Place, Location = dest };
            RouteMap.Pins.Add(_destinationPin);
        }
        else _destinationPin.Location = dest;

        // Show the shortest pickup→destination overview (when the Directions API returned one)
        // only until the live driver→target route is available; the live route then replaces it.
        if (_liveRoute is null && !_vm.IsCollected && _vm.RoutePoints is { Count: > 1 } points)
        {
            if (_previewRoute is null)
            {
                _previewRoute = new Polyline { StrokeColor = Colors.MediumVioletRed, StrokeWidth = 6 };
                RouteMap.MapElements.Add(_previewRoute);
            }
            _previewRoute.Geopath.Clear();
            foreach (var p in points) _previewRoute.Geopath.Add(p);
        }

        // Frame the pickup→destination span only until the live route takes over; from then on the
        // live-route handler frames driver→target instead.
        if (_liveRoute is null && !_vm.IsCollected)
        {
            var centre = new Location((pickup.Latitude + dest.Latitude) / 2, (pickup.Longitude + dest.Longitude) / 2);
            var radiusKm = Math.Max(1, Location.CalculateDistance(pickup, dest, DistanceUnits.Kilometers));
            RouteMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
        }
    }

    /// <summary>Moves (or creates) the live driver marker as GPS pings arrive.</summary>
    private void OnDriverPositionChanged(double lat, double lng) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var here = new Location(lat, lng);
        if (_driverPin is null)
        {
            _driverPin = new Pin { Label = "You", Type = PinType.SearchResult, Location = here };
            RouteMap.Pins.Add(_driverPin);
        }
        else _driverPin.Location = here;
    });

    /// <summary>
    /// Parcel collected — clear the "on my way to the pickup" overlays. The pickup → drop-off
    /// directions replace them as soon as they arrive.
    /// </summary>
    private void OnDeliveryCollected() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_previewRoute is not null)
        {
            RouteMap.MapElements.Remove(_previewRoute);
            _previewRoute = null;
        }
        if (_liveRoute is not null)
        {
            RouteMap.MapElements.Remove(_liveRoute);
            _liveRoute = null;
        }
    });

    /// <summary>
    /// Draws the full journey now that the parcel is in hand: source (pickup) → destination, the
    /// way Google Maps shows directions. Alternatives go down first in a pale blue so the chosen
    /// (shortest) route sits on top of them in solid blue, then the map is framed around the whole
    /// journey with both end pins visible.
    /// </summary>
    private void OnCollectedRoutesReady(IReadOnlyList<RouteResult> routes) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (routes.Count == 0) return;

        // Whatever was on the map from the pickup leg is no longer relevant.
        if (_previewRoute is not null) { RouteMap.MapElements.Remove(_previewRoute); _previewRoute = null; }
        if (_liveRoute is not null) { RouteMap.MapElements.Remove(_liveRoute); _liveRoute = null; }
        foreach (var old in _collectedRouteLines) RouteMap.MapElements.Remove(old);
        _collectedRouteLines.Clear();

        // Alternatives first (index 1..n), so the chosen route added last draws over them.
        for (var i = routes.Count - 1; i >= 0; i--)
        {
            var points = routes[i].Points;
            if (points.Count < 2) continue;

            var chosen = i == 0;
            var line = new Polyline
            {
                StrokeColor = chosen ? ChosenRouteColor : AlternateRouteColor,
                StrokeWidth = chosen ? 8 : 6
            };
            foreach (var p in points) line.Geopath.Add(p);

            RouteMap.MapElements.Add(line);
            _collectedRouteLines.Add(line);
        }

        // The pins mark the two ends of the journey, exactly as in the directions view.
        RenderRoute();
        FrameJourney();
    });

    /// <summary>Zooms the map so the whole pickup → drop-off journey fits, with a little padding.</summary>
    private void FrameJourney()
    {
        var pickup = new Location(_vm.PickupLatitude, _vm.PickupLongitude);
        var dest = new Location(_vm.DestinationLatitude, _vm.DestinationLongitude);
        var centre = new Location((pickup.Latitude + dest.Latitude) / 2, (pickup.Longitude + dest.Longitude) / 2);
        var radiusKm = Math.Max(1, Location.CalculateDistance(pickup, dest, DistanceUnits.Kilometers) * 0.65);
        RouteMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
    }

    /// <summary>
    /// Draws (or refreshes) the live road route ahead — from the driver's current position to the
    /// current target (the pickup while heading out to collect, the destination once collected) —
    /// and frames the map so the whole remaining leg is visible.
    /// </summary>
    private void OnLiveRouteReady(IReadOnlyList<Location> points) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (points.Count < 2) return;
        // A request that was already in flight when the parcel got collected must not draw over
        // the pickup → drop-off directions.
        if (_vm.IsCollected) return;

        // The live route supersedes the static pickup→destination overview.
        if (_previewRoute is not null)
        {
            RouteMap.MapElements.Remove(_previewRoute);
            _previewRoute = null;
        }

        if (_liveRoute is null)
        {
            _liveRoute = new Polyline { StrokeColor = Colors.MediumVioletRed, StrokeWidth = 6 };
            RouteMap.MapElements.Add(_liveRoute);
        }
        _liveRoute.Geopath.Clear();
        foreach (var p in points) _liveRoute.Geopath.Add(p);

        // Frame driver (first point) → target (last point) with a little padding.
        var driver = points[0];
        var target = points[^1];
        var centre = new Location((driver.Latitude + target.Latitude) / 2, (driver.Longitude + target.Longitude) / 2);
        var radiusKm = Math.Max(0.5, Location.CalculateDistance(driver, target, DistanceUnits.Kilometers) * 0.7);
        RouteMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
    });

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartClock();
        _vm.StartLocationSharing();
        // Re-draw the overlay if it was already computed — leaving and returning to the page
        // must not lose the map.
        RenderRoute();
        // Same for the collected journey: redraw it if the lines were lost, otherwise just re-frame.
        if (_vm.IsCollected && _vm.CollectedRoutes.Count > 0)
        {
            if (_collectedRouteLines.Count == 0) OnCollectedRoutesReady(_vm.CollectedRoutes);
            else FrameJourney();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
        // Only pause live GPS sharing while the page is hidden. The computed route and our
        // event subscriptions are kept so everything is still here when the driver returns.
        _vm.StopLocationSharing();
    }
}
