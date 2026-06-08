namespace Droppa.Services.Api;

/// <summary>Holds the current access/refresh tokens, persisted across launches.</summary>
public interface ITokenStore
{
    string? AccessToken { get; }
    string? RefreshToken { get; }
    Task SetAsync(string accessToken, string refreshToken);
    Task LoadAsync();
    Task ClearAsync();
}

/// <summary>
/// In-memory token cache backed by <see cref="SecureStorage"/> so the session survives
/// app restarts. SecureStorage failures degrade gracefully to in-memory only.
/// </summary>
public class TokenStore : ITokenStore
{
    private const string AccessKey = "droppa_access_token";
    private const string RefreshKey = "droppa_refresh_token";

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    public async Task SetAsync(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        try
        {
            await SecureStorage.Default.SetAsync(AccessKey, accessToken);
            await SecureStorage.Default.SetAsync(RefreshKey, refreshToken);
        }
        catch
        {
            // SecureStorage unavailable (e.g. emulator without keystore) — keep tokens in memory only.
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            AccessToken = await SecureStorage.Default.GetAsync(AccessKey);
            RefreshToken = await SecureStorage.Default.GetAsync(RefreshKey);
        }
        catch
        {
            // Ignore; tokens stay null and the user signs in again.
        }
    }

    public Task ClearAsync()
    {
        AccessToken = null;
        RefreshToken = null;
        try
        {
            SecureStorage.Default.Remove(AccessKey);
            SecureStorage.Default.Remove(RefreshKey);
        }
        catch { /* ignore */ }
        return Task.CompletedTask;
    }
}
