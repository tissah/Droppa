using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
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
    [ObservableProperty] private string? _selectedDistrict;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Malawi districts the customer chooses their residence from (required).</summary>
    public ObservableCollection<string> Districts { get; } = new(Models.Districts.All);

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(SelectedDistrict))
        {
            ErrorMessage = "Please select your resident district.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _auth.RegisterWithEmailAsync(FullName, Email, Password, PhoneNumber, SelectedDistrict);
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

    async Task<string> GetDistrictAsync(double latitude, double longitude)
{
    string apiKey = "AIzaSyCQNO0FPWAQOpku0E27ecOEKKFJPxEzFx8";

    using HttpClient client = new HttpClient();

    string url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={latitude},{longitude}&key={apiKey}";

    var response = await client.GetStringAsync(url);

    using JsonDocument doc = JsonDocument.Parse(response);

    var results = doc.RootElement.GetProperty("results");

    foreach (var result in results.EnumerateArray())
    {
        var components = result.GetProperty("address_components");

        foreach (var component in components.EnumerateArray())
        {
            var types = component.GetProperty("types");

            foreach (var type in types.EnumerateArray())
            {
                if (type.GetString() == "administrative_area_level_2")
                {
                    return component.GetProperty("long_name").GetString();
                }
            }
        }
    }

    return "District not found";
}
}
