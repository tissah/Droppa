using Droppa.ViewModels;

namespace Droppa.Views;

public partial class SendParcelPage : ContentPage
{
    private readonly SendParcelViewModel _vm;

    public SendParcelPage(SendParcelViewModel vm)
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
