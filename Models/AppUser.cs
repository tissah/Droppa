namespace Droppa.Models;

/// <summary>The authenticated account. Auth provider details live in the auth service.</summary>
public class AppUser
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>The customer's resident district (Malawi). Drives courier/branch filtering.</summary>
    public string? District { get; set; }

    public UserRole Role { get; set; } = UserRole.Customer;
}
