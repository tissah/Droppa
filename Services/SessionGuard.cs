using Droppa.Services.Api;

namespace Droppa.Services;

/// <summary>
/// Watches for expired sessions across the whole app. When an authenticated API call comes back
/// 401 (<see cref="DroppaApiClient.Unauthorized"/>), it signs the user out, tells the customer, and
/// returns them to the login page. Created once at startup so it's always listening, regardless of
/// which screen is open.
/// </summary>
public class SessionGuard
{
    private readonly IAuthService _auth;
    private bool _handling;

    public SessionGuard(DroppaApiClient api, IAuthService auth)
    {
        _auth = auth;
        api.Unauthorized += OnUnauthorized;
    }

    private void OnUnauthorized()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Single-threaded here: ignore once we've started (or already finished) handling,
            // so a burst of failing calls produces just one alert + redirect.
            if (_handling || !_auth.IsAuthenticated) return;
            _handling = true;
            try
            {
                _auth.SignOut();

                if (Shell.Current is not null)
                {
                    await Shell.Current.DisplayAlert(
                        "Session expired",
                        "Your session has expired. Please sign in again.",
                        "OK");
                    await Shell.Current.GoToAsync("//login");
                }
            }
            finally
            {
                _handling = false;
            }
        });
    }
}
