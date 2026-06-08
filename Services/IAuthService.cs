using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Authentication surface (spec section 1). The scaffold ships an in-memory stub;
/// swap in Firebase Authentication / Google / Facebook behind this interface later.
/// </summary>
public interface IAuthService
{
    AppUser? CurrentUser { get; }
    bool IsAuthenticated { get; }

    Task<AppUser> SignInWithEmailAsync(string email, string password, CancellationToken ct = default);
    Task<AppUser> RegisterWithEmailAsync(string fullName, string email, string password, string? phoneNumber = null, CancellationToken ct = default);
    Task<AppUser> SignInWithGoogleAsync(CancellationToken ct = default);
    Task<AppUser> SignInWithFacebookAsync(CancellationToken ct = default);
    Task SendPasswordResetAsync(string email, CancellationToken ct = default);
    void SignOut();
}
