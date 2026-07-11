namespace Droppa.Models;

/// <summary>
/// A partnered courier company and its office location.
/// Office coordinates drive the distance/price calculation.
/// Admins may update the location (see spec section 5).
/// </summary>
public class CourierService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = "Lilongwe";
    public GeoLocation Office { get; set; } = new(0, 0);

    /// <summary>
    /// The courier's mobile money number, captured when the courier is created.
    /// Drivers remit the fee the courier charged the customer to this number.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// The courier's branches. The customer chooses which branch handles their parcel;
    /// the chosen branch's location drives the route. Empty if the courier has a single office.
    /// </summary>
    public List<Branch> Branches { get; set; } = [];

    public override string ToString() => Name;
}
