using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Services;

namespace Droppa.ViewModels;

public partial class HomeViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ParcelChargeNotifier _parcelChargeNotifier;

    public HomeViewModel(IAuthService auth, ParcelChargeNotifier parcelChargeNotifier)
    {
        _auth = auth;
        _parcelChargeNotifier = parcelChargeNotifier;
        Title = "Droppa";
    }

    public string Greeting =>
        _auth.CurrentUser is { } user ? $"Hello, {user.FullName}" : "Welcome";

    [RelayCommand]
    private Task SendParcelAsync() => Shell.Current.GoToAsync("send");

    [RelayCommand]
    private Task ReceiveParcelAsync() => Shell.Current.GoToAsync("receive");

    [RelayCommand]
    private async Task SignOutAsync()
    {
        _parcelChargeNotifier.Stop();
        _auth.SignOut();
        await Shell.Current.GoToAsync("//login");
    }
}
