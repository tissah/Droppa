using Droppa.ViewModels;

namespace Droppa.Views;

public partial class DriverJobsPage : ContentPage
{
    private readonly DriverJobsViewModel _vm;

    public DriverJobsPage(DriverJobsViewModel vm)
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
