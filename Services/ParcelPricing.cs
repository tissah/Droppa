namespace Droppa.Services;

/// <summary>
/// The parcel (weight) charge, computed in the driver module once the rider weighs the parcel and
/// sent to the customer as a second payment (the first being the distance-based ride booking).
/// Rate is MWK 3 per gram with a MWK 3,000 floor; 17.5% VAT is added on top.
/// </summary>
public static class ParcelPricing
{
    /// <summary>MWK charged per gram of parcel weight.</summary>
    public const decimal RatePerGram = 3m;

    /// <summary>The lowest weight amount charged, before VAT.</summary>
    public const decimal MinimumAmount = 3000m;

    /// <summary>Value-added tax applied to the parcel charge.</summary>
    public const decimal VatRate = 0.175m;

    /// <summary>The pre-VAT weight amount: weight (grams) × rate, floored at the minimum.</summary>
    public static decimal WeightAmount(double weightGrams) =>
        Math.Max((decimal)weightGrams * RatePerGram, MinimumAmount);

    /// <summary>The VAT due on a given weight amount.</summary>
    public static decimal Vat(decimal weightAmount) =>
        Math.Round(weightAmount * VatRate, 0, MidpointRounding.AwayFromZero);

    /// <summary>The total parcel charge the customer pays: weight amount + VAT.</summary>
    public static decimal Total(double weightGrams)
    {
        var amount = WeightAmount(weightGrams);
        return amount + Vat(amount);
    }
}
