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

    /// <summary>
    /// The API's id for <see cref="District"/> — what courier branches are looked up by
    /// (<c>/api/courier-branches?districtId=…</c>). Null on an account registered before
    /// district capture, where the name is all there is to go on.
    /// </summary>
    public int? DistrictId { get; set; }

    public UserRole Role { get; set; } = UserRole.Customer;
}
