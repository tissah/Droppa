using Droppa.ViewModels;

namespace Droppa.Views;

public partial class DriverTripsPage : ContentPage
{
    private readonly DriverTripsViewModel _vm;

    public DriverTripsPage(DriverTripsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartClock();
        await _vm.RefreshCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
    }
}
