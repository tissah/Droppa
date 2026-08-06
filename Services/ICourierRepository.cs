using Droppa.Models;

namespace Droppa.Services;

/// <summary>Provides the catalogue of partnered courier services (loaded from the API).</summary>
public interface ICourierRepository
{
    Task<IReadOnlyList<CourierService>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// The couriers that have a branch in the customer's district, each carrying only its branches
    /// in that district. Built from <c>/api/courier-branches?districtId=…</c>, so a courier appears
    /// once no matter how many offices it has there.
    /// </summary>
    /// <param name="districtId">The API's district id — how the branches are looked up.</param>
    /// <param name="district">
    /// The district name, used only when <paramref name="districtId"/> is unknown: it is matched
    /// against the district list to recover the id. The whole catalogue is returned when neither
    /// is known (an account registered before district capture).
    /// </param>
    Task<IReadOnlyList<CourierService>> GetInDistrictAsync(
        int? districtId, string? district = null, CancellationToken ct = default);
}
