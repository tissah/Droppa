using Droppa.ViewModels;

namespace Droppa.Views;

public partial class ReceiveParcelPage : ContentPage
{
    private readonly ReceiveParcelViewModel _vm;

    public ReceiveParcelPage(ReceiveParcelViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartClock();
        _vm.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
    }
}
