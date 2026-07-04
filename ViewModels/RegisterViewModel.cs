using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Services;

namespace Droppa.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ParcelChargeNotifier _parcelChargeNotifier;

    public RegisterViewModel(IAuthService auth, ParcelChargeNotifier parcelChargeNotifier)
    {
        _auth = auth;
        _parcelChargeNotifier = parcelChargeNotifier;
        Title = "Create account";
    }

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _phoneNumber = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _auth.RegisterWithEmailAsync(FullName, Email, Password, PhoneNumber);
            _parcelChargeNotifier.Start(); // new accounts are customers
            await Shell.Current.GoToAsync("//main");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoToLoginAsync() => Shell.Current.GoToAsync("..");
}
