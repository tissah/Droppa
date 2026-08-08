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

    private void OnCourierTabClicked(object sender, EventArgs e) => SelectTab(courier: true);

    private void OnCustomerTabClicked(object sender, EventArgs e) => SelectTab(courier: false);

    /// <summary>Toggles between the "Send to Courier" flow and the "Send to Customer" placeholder.</summary>
    private void SelectTab(bool courier)
    {
        CourierTabContent.IsVisible = courier;
        CustomerTabContent.IsVisible = !courier;

        var primary = (Color)Application.Current!.Resources["Primary"];
        TabCourierBtn.BackgroundColor = courier ? primary : Colors.Transparent;
        TabCourierBtn.TextColor = courier ? Colors.White : primary;
        TabCustomerBtn.BackgroundColor = courier ? Colors.Transparent : primary;
        TabCustomerBtn.TextColor = courier ? primary : Colors.White;
    }
}
