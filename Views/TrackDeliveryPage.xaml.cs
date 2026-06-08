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
    private Polyline? _trail;           // the route the driver has actually taken
    private bool _centeredOnce;

    public TrackDeliveryPage(TrackDeliveryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.RouteContextReady += OnRouteContextReady;
        _vm.DriverPositionChanged += OnDriverPositionChanged;
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

        // Append to the trail polyline (the route taken).
        if (_trail is null)
        {
            _trail = new Polyline { StrokeColor = Colors.DodgerBlue, StrokeWidth = 6 };
            LiveMap.MapElements.Add(_trail);
        }
        _trail.Geopath.Add(here);

        // Keep the driver centred.
        LiveMap.MoveToRegion(MapSpan.FromCenterAndRadius(here, Distance.FromKilometers(2)));
    });

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.RouteContextReady -= OnRouteContextReady;
        _vm.DriverPositionChanged -= OnDriverPositionChanged;
        await _vm.StopAsync();
    }
}
