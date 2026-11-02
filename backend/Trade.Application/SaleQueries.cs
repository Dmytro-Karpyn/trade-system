using Trade.Domain.Catalog;
using Trade.Domain.Documents;

namespace Trade.Application;

public sealed record SaleFilter(string? Search = null, DateOnly? From = null, DateOnly? To = null,
    Guid? CounterpartyId = null, Guid? StorageLocationId = null, Guid? PriceTypeId = null, DocumentStatus? Status = null);
public sealed record SaleListResponse(Guid Id, string? Number, DateOnly DocumentDate, Guid CounterpartyId,
    string CounterpartyName, Guid StorageLocationId, Guid? PriceTypeId, string? PriceTypeName,
    decimal TotalUah, decimal DiscountPercent, DocumentStatus Status);
public sealed record SaleProductResponse(Guid ProductId, string Name, string Code, ItemType ItemType);
public sealed record SaleBatchResponse(Guid BatchId, string BatchNumber, DateOnly PurchaseDocumentDate);
public sealed record SaleLookupsResponse(IReadOnlyList<StorageLocationResponse> Locations);
public sealed record SalePriceOption(Guid? BatchId, decimal UnitPrice);
public sealed record SaleCostResponse(decimal CurrentCost);
public sealed record SaleProfitResponse(decimal NetSale, decimal GrossProfit, decimal? Margin);
public sealed record SaleAmounts(decimal NetSale, decimal CurrentCost);

public sealed class SaleQueryService(ITradeStore store)
{
    public Task<IReadOnlyList<SaleListResponse>> ListAsync(Guid organizationId, SaleFilter filter, CancellationToken ct) =>
        store.ListSalesAsync(organizationId, filter, ct);
    public async Task<SaleLookupsResponse> LookupsAsync(Guid organizationId, CancellationToken ct) =>
        new((await store.GetPurchaseLookupsAsync(organizationId, ct)).Locations);
    public async Task<IReadOnlyList<SalePriceOption>> PricesAsync(Guid organizationId, Guid productId, Guid? priceTypeId, CancellationToken ct)
    {
        _ = await store.FindProductAsync(organizationId, productId, false, ct) ?? throw new NotFoundException("Product not found.");
        if (priceTypeId is { } type)
            _ = await store.FindPriceTypeAsync(organizationId, type, false, ct) ?? throw new NotFoundException("PriceType not found.");
        return await store.GetSalePriceOptionsAsync(organizationId, productId, priceTypeId, ct);
    }
    public async Task<SaleCostResponse> CostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        new((await AmountsAsync(organizationId, id, ct)).CurrentCost);
    public async Task<SaleProfitResponse> ProfitAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var amounts = await AmountsAsync(organizationId, id, ct);
        var profit = amounts.NetSale - amounts.CurrentCost;
        return new(amounts.NetSale, profit, amounts.NetSale == 0 ? null : profit / amounts.NetSale * 100m);
    }
    private async Task<SaleAmounts> AmountsAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        _ = await store.FindSaleAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Sale not found.");
        return await store.GetSaleAmountsAsync(organizationId, id, ct);
    }
}
