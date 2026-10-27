using Trade.Domain.Common;

namespace Trade.Domain.Pricing;

public sealed record SalePricePermissions(bool CanSellBelowRecommendedPrice)
{
    public static SalePricePermissions None { get; } = new(false);
}

public sealed class PriceBelowRecommendedException() : Exception("PRICE_BELOW_RECOMMENDED");

public static class PriceRules
{
    public static void Validate(decimal unitPrice)
    {
        Guard.Against(unitPrice < 0, "UnitPrice must be nonnegative.");
        Guard.Numeric(unitPrice, 4, 10_000_000_000_000_000m, nameof(unitPrice));
    }

    public static void RequireAllowed(decimal recommended, decimal actual, SalePricePermissions permissions)
    {
        Validate(recommended);
        Validate(actual);
        if (actual < recommended && !permissions.CanSellBelowRecommendedPrice)
            throw new PriceBelowRecommendedException();
    }
}
