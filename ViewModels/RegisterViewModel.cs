using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Droppa.Models;
using Droppa.Services;

namespace Droppa.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ILocationService _location;

    public RegisterViewModel(IAuthService auth, ILocationService location)
    {
        _auth = auth;
        _location = location;
        Title = "Create account";
    }

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _phoneNumber = string.Empty;
    [ObservableProperty] private string? _selectedDistrict;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>True while the district is being detected from the device GPS.</summary>
    [ObservableProperty] private bool _isResolvingDistrict;

    /// <summary>
    /// Detects the resident district from the device's current location. Called when the
    /// register page appears so the read-only district field is filled automatically.
    /// </summary>
    [RelayCommand]
    private async Task LoadDistrictAsync()
    {
        if (IsResolvingDistrict) return;

        try
        {
            IsResolvingDistrict = true;
            SelectedDistrict = "Detecting district…";

            var district = await ResolveCurrentDistrictAsync();
            SelectedDistrict = district; // null when unresolved; register will guard on it
        }
        catch (Exception)
        {
            SelectedDistrict = null;
        }
        finally
        {
            IsResolvingDistrict = false;
        }
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            // District is normally resolved on page open; re-resolve if it isn't ready yet.
            var district = SelectedDistrict;
            if (string.IsNullOrWhiteSpace(district) || district == "Detecting district…")
                district = await ResolveCurrentDistrictAsync();

            if (string.IsNullOrWhiteSpace(district))
            {
                ErrorMessage = "Could not determine your district. Please enable location and try again.";
                return;
            }

            SelectedDistrict = district;
            await _auth.RegisterWithEmailAsync(FullName, Email, Password, PhoneNumber, district);
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

    /// <summary>
    /// Reads the device's current GPS position and reverse-geocodes it into a district.
    /// Returns null when location is unavailable (permission denied, GPS off) or the
    /// district cannot be resolved.
    /// </summary>
    private async Task<string?> ResolveCurrentDistrictAsync()
    {
        var location = await _location.GetCurrentLocationAsync();
        if (location is null)
            return null;

        var district = await GetDistrictAsync(location.Latitude, location.Longitude);
        if (string.IsNullOrWhiteSpace(district) || district == "District not found")
            return null;

        // Google returns names like "Lilongwe District"; store the canonical district so it
        // matches how courier branches record theirs and the pickers can filter on it.
        return Districts.Normalize(district);
    }

    [RelayCommand]
    private Task GoToLoginAsync() => Shell.Current.GoToAsync("..");

    private static async Task<string> GetDistrictAsync(double latitude, double longitude)
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
                        return component.GetProperty("long_name").GetString() ?? "District not found";
                    }
                }
            }
        }

        return "District not found";
    }
}
