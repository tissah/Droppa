using Droppa.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace Droppa.Views;

public partial class TrackDeliveryPage : ContentPage
{
    private readonly TrackDeliveryViewModel _vm;

    private Pin? _driverPin;
    private Pin? _destinationPin;
    private Polyline? _trail;           // the route the driver has actually taken (breadcrumb)
    private Polyline? _route;           // the road route ahead, from the driver to the destination
    private bool _centeredOnce;

    public TrackDeliveryPage(TrackDeliveryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.RouteContextReady += OnRouteContextReady;
        _vm.DriverPositionChanged += OnDriverPositionChanged;
        _vm.RouteToDestinationReady += OnRouteToDestinationReady;
    }

    private void OnRouteContextReady() => MainThread.BeginInvokeOnMainThread(() =>
    {
        // Drop a destination pin and frame the map around it.
        var dest = new Location(_vm.DestinationLatitude, _vm.DestinationLongitude);
        if (_destinationPin is null)
        {
            _destinationPin = new Pin { Label = "Destination", Type = PinType.Place, Location = dest };
            LiveMap.Pins.Add(_destinationPin);
        }
        else
        {
            _destinationPin.Location = dest;
        }

        if (!_centeredOnce)
        {
            LiveMap.MoveToRegion(MapSpan.FromCenterAndRadius(dest, Distance.FromKilometers(3)));
            _centeredOnce = true;
        }
    });

    private void OnDriverPositionChanged(double lat, double lng) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var here = new Location(lat, lng);

        // Move (or create) the live driver marker.
        if (_driverPin is null)
        {
            _driverPin = new Pin { Label = "Driver", Type = PinType.SearchResult, Location = here };
            LiveMap.Pins.Add(_driverPin);
        }
        else
        {
            _driverPin.Location = here;
        }

        // Append to the trail polyline (the route already taken).
        if (_trail is null)
        {
            _trail = new Polyline { StrokeColor = Colors.DodgerBlue, StrokeWidth = 6 };
            LiveMap.MapElements.Add(_trail);
        }
        _trail.Geopath.Add(here);

        // Follow the driver until the road route ahead is available; once it is, the route
        // handler frames driver→destination instead so the whole remaining path stays visible.
        if (_route is null)
            LiveMap.MoveToRegion(MapSpan.FromCenterAndRadius(here, Distance.FromKilometers(2)));
    });

    /// <summary>
    /// Draws (or refreshes) the road route ahead — from the driver's current position to the
    /// destination — and frames the map so the whole remaining route is visible.
    /// </summary>
    private void OnRouteToDestinationReady(IReadOnlyList<Location> points) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (points.Count < 2) return;

        if (_route is null)
        {
            _route = new Polyline { StrokeColor = Colors.MediumVioletRed, StrokeWidth = 6 };
            LiveMap.MapElements.Add(_route);
        }
        _route.Geopath.Clear();
        foreach (var p in points) _route.Geopath.Add(p);

        // Frame driver (first point) → destination (last point) with a little padding.
        var driver = points[0];
        var dest = points[^1];
        var centre = new Location((driver.Latitude + dest.Latitude) / 2, (driver.Longitude + dest.Longitude) / 2);
        var radiusKm = Math.Max(0.5, Location.CalculateDistance(driver, dest, DistanceUnits.Kilometers) * 0.7);
        LiveMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
    });

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Starts the header clock and the 5-second tracking auto-refresh while the page is visible.
        _vm.StartClock();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
        _vm.RouteContextReady -= OnRouteContextReady;
        _vm.DriverPositionChanged -= OnDriverPositionChanged;
        _vm.RouteToDestinationReady -= OnRouteToDestinationReady;
        await _vm.StopAsync();
    }
}
