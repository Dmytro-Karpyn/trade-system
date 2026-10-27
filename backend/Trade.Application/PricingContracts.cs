using Trade.Domain.Pricing;

namespace Trade.Application;

public sealed record PriceTypeRequest(string Name, decimal DefaultMarkupPercent = 0m, bool Active = true, int SortOrder = 0);
public sealed record PriceTypeResponse(Guid Id, Guid OrganizationId, string Name, decimal DefaultMarkupPercent,
    bool Active, int SortOrder);
public sealed record PriceRequest(decimal UnitPrice);
public sealed record BatchPriceResponse(Guid BatchId, Guid PriceTypeId, decimal UnitPrice);
public sealed record ProductPriceResponse(Guid ProductId, Guid PriceTypeId, decimal UnitPrice);

// The trusted caller supplies capabilities. HTTP bodies/headers cannot grant them.
// The API authorization adapter resolves sale.change_price from the current user's active roles.
public interface ISalePricePermissionProvider
{
    SalePricePermissions GetPermissions();
}

public sealed class PriceNotFoundException() : Exception("PRICE_NOT_FOUND");
