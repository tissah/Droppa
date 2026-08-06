using Droppa.Models;
using Droppa.Services.Api;

namespace Droppa.Services;

/// <summary>
/// Loads the courier catalogue from the API. Branches are the unit the API serves — each one
/// names its courier and district — so the catalogue is assembled by grouping branches under
/// their courier rather than asking for couriers and hoping they carry their offices.
/// </summary>
public class ApiCourierService : ICourierRepository
{
    private readonly DroppaApiClient _api;

    /// <summary>Districts are reference data, so the name → id map is fetched once per app run.</summary>
    private IReadOnlyList<DistrictDto>? _districts;

    public ApiCourierService(DroppaApiClient api) => _api = api;

    public async Task<IReadOnlyList<CourierService>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            var couriers = Group(await _api.GetCourierBranchesAsync(ct: ct));
            if (couriers.Count > 0) return couriers;
        }
        catch (DroppaApiException)
        {
            // The API is unreachable or refused the request. The pickers still need couriers
            // to offer, so fall through to the bundled catalogue rather than showing nothing.
        }

        return CourierCatalog.All;
    }

    public async Task<IReadOnlyList<CourierService>> GetInDistrictAsync(
        int? districtId, string? district = null, CancellationToken ct = default)
    {
        if (districtId is not > 0 && string.IsNullOrWhiteSpace(district)) return await GetAllAsync(ct);

        try
        {
            var id = districtId is > 0 ? districtId : await ResolveDistrictIdAsync(district!, ct);

            // Filtering server-side keeps the response to the one district; with no id to filter
            // on we ask for every branch and match the district name each branch reports.
            var branches = await _api.GetCourierBranchesAsync(id, ct: ct);

            var couriers = Group(id is > 0
                ? branches
                : branches.Where(b => Districts.Match(b.DistrictName, district)));

            if (couriers.Count > 0) return couriers;
        }
        catch (DroppaApiException)
        {
            // Same reason as GetAllAsync: offer the bundled catalogue instead of an empty picker.
        }

        return CourierDirectory.InDistrict(CourierCatalog.All, district);
    }

    /// <summary>
    /// The API's id for the named district, or null when it serves no district by that name.
    /// Only needed for accounts whose sign-in carried no district id.
    /// </summary>
    private async Task<int?> ResolveDistrictIdAsync(string district, CancellationToken ct)
    {
        _districts ??= await _api.GetDistrictsAsync(ct);
        return _districts.FirstOrDefault(d => Districts.Match(d.DistrictName, district))?.Id;
    }

    /// <summary>
    /// Collapses a branch list into one <see cref="CourierService"/> per courier. Branches are
    /// grouped by courier id — the name is what the picker shows, but two couriers may share one.
    /// </summary>
    private static List<CourierService> Group(IEnumerable<CourierBranchDto> branches) =>
        branches
            .Where(b => b.IsActive)
            .GroupBy(b => b.CourierId)
            .Select(g =>
            {
                var offices = g.Select(Map).ToList();

                // The first branch stands in for the courier itself, so the courier still has a
                // usable location and remittance number if no branch ends up being chosen.
                var head = offices[0];
                return new CourierService
                {
                    Id = g.Key,
                    Name = g.First().CourierName,
                    City = head.District ?? head.Office.Address ?? "Malawi",
                    Office = head.Office,
                    PhoneNumber = g.Select(b => b.PaymentPhoneNumber)
                                   .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
                    Branches = offices
                };
            })
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static Branch Map(CourierBranchDto b) => new()
    {
        Id = b.Id,
        Name = b.BranchName,
        // The API doesn't always record a branch's district. Recovering it from the address
        // keeps the branch in the picker instead of dropping it from every district.
        District = string.IsNullOrWhiteSpace(b.DistrictName) ? Districts.Find(b.Address) : b.DistrictName,
        Office = Office(b),
        PhoneNumber = b.ContactPhoneNumber
    };

    /// <summary>
    /// The branch's coordinates, or its district centre when the branch hasn't been surveyed —
    /// (0, 0) is in the Gulf of Guinea and would quote a nonsense distance.
    /// </summary>
    private static GeoLocation Office(CourierBranchDto b)
    {
        if (b.Latitudes is not 0 || b.Longitudes is not 0)
            return new GeoLocation(b.Latitudes, b.Longitudes, b.Address);

        var centre = Districts.Centre(b.DistrictName) ?? Districts.Centre(Districts.Find(b.Address));
        return centre is null
            ? new GeoLocation(b.Latitudes, b.Longitudes, b.Address)
            : new GeoLocation(centre.Value.Latitude, centre.Value.Longitude, b.Address);
    }
}
