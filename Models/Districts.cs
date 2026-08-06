namespace Droppa.Models;

/// <summary>
/// The districts of Malawi. Used for the customer's resident district (captured at
/// registration) and for a courier branch's location, so couriers can be filtered to
/// the customer's district.
/// </summary>
public static class Districts
{
    public static readonly IReadOnlyList<string> All =
    [
        "Balaka", "Blantyre", "Chikwawa", "Chiradzulu", "Chitipa", "Dedza",
        "Dowa", "Karonga", "Kasungu", "Likoma", "Lilongwe", "Machinga",
        "Mangochi", "Mchinji", "Mulanje", "Mwanza", "Mzimba", "Neno",
        "Nkhata Bay", "Nkhotakota", "Nsanje", "Ntcheu", "Ntchisi", "Phalombe",
        "Rumphi", "Salima", "Thyolo", "Zomba"
    ];

    /// <summary>
    /// Approximate centre of each district (the boma, or the main city where one sits in the
    /// district — Mzuzu for Mzimba). Used to place a courier branch on the map when the only
    /// thing known about it is which district it is in.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Latitude, double Longitude)> Centres =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Balaka"] = (-14.9800, 34.9500),
            ["Blantyre"] = (-15.7861, 35.0058),
            ["Chikwawa"] = (-16.0353, 34.8000),
            ["Chiradzulu"] = (-15.7000, 35.1500),
            ["Chitipa"] = (-9.7019, 33.2700),
            ["Dedza"] = (-14.3779, 34.3332),
            ["Dowa"] = (-13.6543, 33.9400),
            ["Karonga"] = (-9.9333, 33.9333),
            ["Kasungu"] = (-13.0333, 33.4833),
            ["Likoma"] = (-12.0667, 34.7333),
            ["Lilongwe"] = (-13.9626, 33.7741),
            ["Machinga"] = (-14.9667, 35.5167),
            ["Mangochi"] = (-14.4783, 35.2645),
            ["Mchinji"] = (-13.7986, 32.8800),
            ["Mulanje"] = (-16.0319, 35.5083),
            ["Mwanza"] = (-15.5983, 34.5178),
            ["Mzimba"] = (-11.4656, 34.0207), // Mzuzu
            ["Neno"] = (-15.3980, 34.6533),
            ["Nkhata Bay"] = (-11.6000, 34.3000),
            ["Nkhotakota"] = (-12.9274, 34.2958),
            ["Nsanje"] = (-16.9200, 35.2620),
            ["Ntcheu"] = (-14.8200, 34.6600),
            ["Ntchisi"] = (-13.3667, 33.9167),
            ["Phalombe"] = (-15.8060, 35.6530),
            ["Rumphi"] = (-11.0186, 33.8580),
            ["Salima"] = (-13.7804, 34.4587),
            ["Thyolo"] = (-16.0700, 35.1400),
            ["Zomba"] = (-15.3860, 35.3188)
        };

    /// <summary>
    /// The centre of the named district, or null when the name matches no known district.
    /// </summary>
    public static (double Latitude, double Longitude)? Centre(string? district)
    {
        var name = Normalize(district);
        return name is not null && Centres.TryGetValue(name, out var point) ? point : null;
    }

    /// <summary>Suffixes reverse-geocoding adds to a district name, e.g. "Lilongwe District".</summary>
    private static readonly string[] Suffixes = [" district", " city"];

    /// <summary>
    /// Reduces a district name to its canonical form so values from different sources
    /// compare equal — the GPS reverse-geocoder returns "Lilongwe District" or
    /// "Blantyre City" where a courier branch is recorded simply as "Lilongwe".
    /// Returns null for blank input, and the trimmed input when it matches no known
    /// district (so an unrecognised place still registers and still compares sanely).
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Trim();
        foreach (var suffix in Suffixes)
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length].TrimEnd();
                break;
            }

        // Exact match on a known district wins, so casing/spacing is canonicalised.
        var exact = All.FirstOrDefault(d => string.Equals(d, cleaned, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Otherwise look for a district named inside a longer string, e.g. "Central Region, Lilongwe".
        var mentioned = All.FirstOrDefault(d => cleaned.Contains(d, StringComparison.OrdinalIgnoreCase));
        return mentioned ?? cleaned;
    }

    /// <summary>
    /// The known district named in free text (an address such as "Area 47, Lilongwe"), or null
    /// when the text names none. Unlike <see cref="Normalize"/> this never echoes the input back,
    /// so it is safe to use where only a real district will do.
    /// </summary>
    public static string? Find(string? text)
    {
        var name = Normalize(text);
        return name is not null && All.Contains(name, StringComparer.OrdinalIgnoreCase) ? name : null;
    }

    /// <summary>True when two district names refer to the same district, ignoring case and suffixes.</summary>
    public static bool Match(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        return a is not null && b is not null &&
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when free text (a courier's address or city) refers to the given district —
    /// used for couriers that have a single office and so no branch district to match on.
    /// </summary>
    public static bool Mentions(string? text, string? district)
    {
        var target = Normalize(district);
        if (target is null || string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains(target, StringComparison.OrdinalIgnoreCase);
    }
}
