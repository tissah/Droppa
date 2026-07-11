using Droppa.Models;
using Droppa.Services.Api;

namespace Droppa.Services;

/// <summary>Loads the courier catalogue from the API.</summary>
public class ApiCourierService : ICourierRepository
{
    private readonly DroppaApiClient _api;

    public ApiCourierService(DroppaApiClient api) => _api = api;

    public async Task<IReadOnlyList<CourierService>> GetAllAsync(CancellationToken ct = default)
    {
        var dtos = await _api.GetCourierServicesAsync(ct);
        return dtos.Select(d => new CourierService
        {
            Id = d.Id,
            Name = d.Name,
            City = d.Address ?? "Lilongwe",
            Office = new GeoLocation(d.Latitude, d.Longitude, d.Address),
            PhoneNumber = d.PhoneNumber,
            Branches = d.Branches.Select(b => new Branch
            {
                Id = b.Id,
                Name = b.Name,
                District = b.District,
                Office = new GeoLocation(b.Latitude, b.Longitude, b.Address),
                PhoneNumber = b.PhoneNumber
            }).ToList()
        }).ToList();
    }
}
