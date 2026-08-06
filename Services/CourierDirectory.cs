using Droppa.Models;

namespace Droppa.Services;

/// <summary>
/// Narrows the courier catalogue to the district the customer registered with. Both the
/// send and receive flows use this so the courier and branch pickers agree on one rule.
/// </summary>
public static class CourierDirectory
{
    /// <summary>
    /// The couriers that serve <paramref name="district"/>. Filtering is strict: a courier
    /// the customer's district isn't served by is left out of the picker entirely. The only
    /// exception is an unknown district (an account registered before district capture), where
    /// there is nothing to filter on and the full catalogue is returned.
    /// </summary>
    public static IReadOnlyList<CourierService> InDistrict(
        IEnumerable<CourierService> couriers, string? district)
    {
        var all = couriers as IReadOnlyList<CourierService> ?? couriers.ToList();
        if (string.IsNullOrWhiteSpace(district))
            return all;

        return all.Where(c => ServesDistrict(c, district)).ToList();
    }

    /// <summary>
    /// True when the courier has an office in the district: a branch recorded in it, or —
    /// when no branch says where it is — an address that names it.
    /// </summary>
    public static bool ServesDistrict(CourierService courier, string? district)
    {
        if (string.IsNullOrWhiteSpace(district)) return true;

        if (courier.Branches.Any(b => BranchIsIn(b, district)))
            return true;

        // Branches that place themselves elsewhere are an answer: this courier isn't in the
        // district. Branches that record no location at all are not — fall back to the
        // courier's own address, or every courier would be filtered out.
        if (courier.Branches.Any(KnowsWhereItIs))
            return false;

        return Districts.Mentions(courier.City, district) ||
               Districts.Mentions(courier.Office.Address, district);
    }

    /// <summary>
    /// The courier's branches in the customer's district. Strict for the same reason as
    /// <see cref="InDistrict"/> — a branch in another district is not a destination the
    /// customer can be offered. All branches are returned when the district is unknown.
    /// </summary>
    public static IReadOnlyList<Branch> BranchesInDistrict(CourierService courier, string? district)
    {
        if (string.IsNullOrWhiteSpace(district))
            return courier.Branches;

        // Nothing to filter on when no branch records where it is — offer them all rather
        // than leaving the customer a courier with no branch to pick.
        if (!courier.Branches.Any(KnowsWhereItIs))
            return courier.Branches;

        return courier.Branches.Where(b => BranchIsIn(b, district)).ToList();
    }

    /// <summary>
    /// True when the branch is in the district. The recorded district decides it; a branch that
    /// has none is judged on its address, which is how API records that omit the district still
    /// reach the right customers.
    /// </summary>
    private static bool BranchIsIn(Branch branch, string district) =>
        string.IsNullOrWhiteSpace(branch.District)
            ? Districts.Mentions(branch.Office.Address, district)
            : Districts.Match(branch.District, district);

    /// <summary>True when the branch records anything that can place it in a district.</summary>
    private static bool KnowsWhereItIs(Branch branch) =>
        !string.IsNullOrWhiteSpace(branch.District) ||
        !string.IsNullOrWhiteSpace(branch.Office.Address);
}
