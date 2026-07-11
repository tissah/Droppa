using Droppa.Models;
using Droppa.Services.Api;

namespace Droppa.Services;

/// <summary>Authenticates against the Droppa API and stores the issued tokens.</summary>
public class ApiAuthService : IAuthService
{
    private readonly DroppaApiClient _api;
    private readonly ITokenStore _tokens;

    public ApiAuthService(DroppaApiClient api, ITokenStore tokens)
    {
        _api = api;
        _tokens = tokens;
    }

    public AppUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null && !string.IsNullOrEmpty(_tokens.AccessToken);

    public async Task<AppUser> SignInWithEmailAsync(string email, string password, CancellationToken ct = default)
    {
        var auth = await _api.LoginAsync(new LoginRequestDto(email, password), ct);
        return await AcceptAsync(auth);
    }

    public async Task<AppUser> RegisterWithEmailAsync(string fullName, string email, string password, string? phoneNumber = null, string? district = null, CancellationToken ct = default)
    {
        // Phone is optional: send null when blank so the API's [Phone] format check is skipped.
        var phone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        var residentDistrict = string.IsNullOrWhiteSpace(district) ? null : district.Trim();
        var auth = await _api.RegisterAsync(new RegisterRequestDto(fullName, email, password, phone, residentDistrict), ct);
        return await AcceptAsync(auth);
    }

    // Social sign-in needs the native Google/Facebook SDK to obtain a provider token,
    // which can then be exchanged at POST /api/Auth/social-login. Not wired in this app yet.
    public Task<AppUser> SignInWithGoogleAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Google sign-in requires the native Google SDK, which isn't configured in this build. Use email sign-in.");

    public Task<AppUser> SignInWithFacebookAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("Facebook sign-in requires the native Facebook SDK, which isn't configured in this build. Use email sign-in.");

    public Task SendPasswordResetAsync(string email, CancellationToken ct = default) => Task.CompletedTask;

    public void SignOut()
    {
        var refresh = _tokens.RefreshToken;
        CurrentUser = null;
        _ = _tokens.ClearAsync();
        if (!string.IsNullOrEmpty(refresh))
            _ = _api.LogoutAsync(refresh);
    }

    private async Task<AppUser> AcceptAsync(AuthResponseDto auth)
    {
        await _tokens.SetAsync(auth.AccessToken, auth.RefreshToken);
        CurrentUser = new AppUser
        {
            Id = auth.User.Id.ToString(),
            FullName = auth.User.FullName,
            Email = auth.User.Email,
            Phone = auth.User.PhoneNumber,
            District = auth.User.District,
            Role = MapRole(auth.User.Roles)
        };
        return CurrentUser;
    }

    /// <summary>
    /// Picks the account's effective role from the server's role list. A user can hold
    /// several roles; the most privileged operational role wins (Administrator → Driver → Customer).
    /// </summary>
    private static UserRole MapRole(IEnumerable<string>? roles)
    {
        var set = roles is null ? [] : new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        if (set.Contains("Administrator")) return UserRole.Administrator;
        if (set.Contains("Driver")) return UserRole.Driver;
        return UserRole.Customer;
    }
}
