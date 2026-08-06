using Droppa.ViewModels;

namespace Droppa.Views;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _vm;

    public RegisterPage(RegisterViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Detect the resident district from GPS as soon as the page opens.
        if (_vm.LoadDistrictCommand.CanExecute(null))
            await _vm.LoadDistrictCommand.ExecuteAsync(null);
    }
}
