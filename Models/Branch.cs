namespace Droppa.Models;

/// <summary>
/// A physical branch (office) of a partnered courier service. A courier may operate
/// several branches; the customer picks which branch handles their parcel, and the
/// chosen branch's coordinates drive the distance/price calculation.
/// </summary>
public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The Malawi district this branch is in. Couriers/branches are filtered to the customer's district.</summary>
    public string? District { get; set; }

    /// <summary>Branch office coordinates — used as the courier end of the route.</summary>
    public GeoLocation Office { get; set; } = new(0, 0);

    /// <summary>Optional contact number for this specific branch.</summary>
    public string? PhoneNumber { get; set; }

    public override string ToString() => Name;
}
