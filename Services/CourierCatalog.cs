using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// The courier catalogue the app ships with. It is a fallback, not a source of truth: it is
/// used only when <c>/api/courier-services</c> returns nothing or can't be reached, so the
/// send and receive pickers always have couriers to offer for the customer's district.
/// Anything the API returns replaces it wholesale.
/// </summary>
/// <remarks>
/// Branch coordinates are district centres (see <see cref="Districts.Centres"/>), not surveyed
/// office locations — good enough to quote a distance from, and corrected as soon as the API
/// serves real branches.
/// </remarks>
public static class CourierCatalog
{
    /// <summary>A courier and the districts it has an office in.</summary>
    private record Entry(int Id, string Name, string? PhoneNumber, string[] Districts);

    private static readonly Entry[] Entries =
    [
        // Ids are negative so a fallback courier can never be mistaken for an API record.
        new(-1, "Malawi Posts Corporation", "+265 1 750 100", [.. Models.Districts.All]),
        new(-2, "Speed Couriers", "+265 999 000 111",
            [
                "Lilongwe", "Blantyre", "Mzimba", "Zomba", "Mangochi", "Kasungu",
                "Salima", "Dedza", "Mchinji", "Balaka", "Ntcheu", "Karonga", "Nkhotakota"
            ]),
        new(-3, "Sky Couriers", "+265 888 000 222",
            ["Lilongwe", "Blantyre", "Mzimba", "Zomba", "Mangochi"]),
        new(-4, "Nationwide Express", "+265 991 000 333",
            ["Lilongwe", "Blantyre", "Mzimba", "Kasungu", "Zomba", "Thyolo", "Mulanje", "Rumphi"]),
        new(-5, "Kwithu Logistics", "+265 997 000 444",
            ["Lilongwe", "Blantyre", "Dowa", "Ntchisi", "Chikwawa", "Machinga"])
    ];

    /// <summary>Every courier in the bundled catalogue, each with one branch per district it serves.</summary>
    public static IReadOnlyList<CourierService> All { get; } = [.. Entries.Select(Build)];

    private static CourierService Build(Entry entry)
    {
        var branches = entry.Districts
            .Select((district, index) => new Branch
            {
                // Distinct within the courier and still negative, for the same reason as the courier id.
                Id = entry.Id * 1000 - index,
                Name = $"{district} branch",
                District = district,
                Office = Office(district),
                PhoneNumber = entry.PhoneNumber
            })
            .ToList();

        // The head office stands in for the courier itself, so a courier still has a usable
        // location if the customer's district somehow yields no branch.
        var head = branches[0];
        return new CourierService
        {
            Id = entry.Id,
            Name = entry.Name,
            City = head.District!,
            Office = head.Office,
            PhoneNumber = entry.PhoneNumber,
            Branches = branches
        };
    }

    private static GeoLocation Office(string district)
    {
        var centre = Models.Districts.Centre(district) ?? (Latitude: 0d, Longitude: 0d);
        return new GeoLocation(centre.Latitude, centre.Longitude, $"{district}, Malawi");
    }
}
