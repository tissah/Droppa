using Droppa.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace Droppa.Views;

public partial class DriverJobPreviewPage : ContentPage
{
    private readonly DriverJobPreviewViewModel _vm;

    public DriverJobPreviewPage(DriverJobPreviewViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.RouteReady += OnRouteReady;
    }

    private void OnRouteReady() => MainThread.BeginInvokeOnMainThread(RenderPins);

    /// <summary>Drops the customer's pickup and drop-off pins and frames the map around them.</summary>
    private void RenderPins()
    {
        if (!_vm.HasRoute) return;

        JobMap.Pins.Clear();

        var pickup = new Location(_vm.PickupLatitude, _vm.PickupLongitude);
        var dest = new Location(_vm.DestinationLatitude, _vm.DestinationLongitude);

        JobMap.Pins.Add(new Pin { Label = "Pickup", Type = PinType.Place, Location = pickup });
        JobMap.Pins.Add(new Pin { Label = "Drop-off", Type = PinType.Place, Location = dest });

        var centre = new Location((pickup.Latitude + dest.Latitude) / 2, (pickup.Longitude + dest.Longitude) / 2);
        var radiusKm = Math.Max(1, Location.CalculateDistance(pickup, dest, DistanceUnits.Kilometers));
        JobMap.MoveToRegion(MapSpan.FromCenterAndRadius(centre, Distance.FromKilometers(radiusKm)));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartClock();
        RenderPins();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
    }
}
