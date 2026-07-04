using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ParcelChargeNotifier _parcelChargeNotifier;

    public LoginViewModel(IAuthService auth, ParcelChargeNotifier parcelChargeNotifier)
    {
        _auth = auth;
        _parcelChargeNotifier = parcelChargeNotifier;
        Title = "Sign in";
    }

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    private Task SignInAsync() => RunAuthAsync(() => _auth.SignInWithEmailAsync(Email, Password));

    [RelayCommand]
    private Task GoogleAsync() => RunAuthAsync(() => _auth.SignInWithGoogleAsync());

    [RelayCommand]
    private Task FacebookAsync() => RunAuthAsync(() => _auth.SignInWithFacebookAsync());

    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Enter your email first, then tap reset.";
            return;
        }

        await _auth.SendPasswordResetAsync(Email);
        ErrorMessage = "If that email exists, a reset link has been sent.";
    }

    [RelayCommand]
    private Task GoToRegisterAsync() => Shell.Current.GoToAsync("register");

    private async Task RunAuthAsync(Func<Task> signIn)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await signIn();
            // Drivers land on the job board; everyone else on the customer home.
            var isDriver = _auth.CurrentUser?.Role == UserRole.Driver;
            // Customers listen app-wide for the driver's parcel-fee request.
            if (!isDriver) _parcelChargeNotifier.Start();
            await Shell.Current.GoToAsync(isDriver ? "//driver" : "//main");
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
}
