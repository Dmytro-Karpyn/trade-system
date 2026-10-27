using Trade.Domain.Documents;

namespace Trade.Application;

public sealed record PurchaseFilter(string? Search = null, DateOnly? From = null, DateOnly? To = null,
    Guid? CounterpartyId = null, Guid? StorageLocationId = null, Guid? CurrencyId = null, DocumentStatus? Status = null);
public sealed record PurchaseListResponse(Guid Id, string? Number, DateOnly DocumentDate, Guid? CounterpartyId,
    string? CounterpartyName, Guid StorageLocationId, Guid CurrencyId, decimal TotalCurrency, DocumentStatus Status,
    string? ExternalNumber);
public sealed record PurchaseProductResponse(Guid ProductId, string Name, string Code);
public sealed record PurchaseBatchResponse(Guid BatchId, Guid PurchaseInvoiceLineId, Guid ProductId,
    string ProductName, string ProductCode, string BatchNumber, DateTimeOffset CreatedAt);
public sealed record PurchaseBatchCostResponse(Guid BatchId, decimal UnitCostUah);
public sealed record PurchaseBatchPriceResponse(Guid BatchId, Guid PriceTypeId, string PriceTypeName, decimal UnitPrice);
public sealed record PurchaseLookupsResponse(IReadOnlyList<StorageLocationResponse> Locations, IReadOnlyList<Guid> CurrencyIds);
public sealed record StorageLocationRequest(string Name);
public sealed record StorageLocationResponse(Guid Id, string? Name);

// Read models only: totals/costs and lookup labels are never persisted with the document.
public sealed class PurchaseQueryService(ITradeStore store)
{
    public Task<IReadOnlyList<PurchaseListResponse>> ListAsync(Guid organizationId, PurchaseFilter filter, CancellationToken ct) =>
        store.ListPurchasesAsync(organizationId, filter, ct);
    public Task<PurchaseLookupsResponse> LookupsAsync(Guid organizationId, CancellationToken ct) => store.GetPurchaseLookupsAsync(organizationId, ct);

    public async Task<IReadOnlyList<PurchaseBatchResponse>> BatchesAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await RequirePurchaseAsync(organizationId, id, ct);
        return await store.GetPurchaseBatchesAsync(organizationId, id, ct);
    }
    public async Task<IReadOnlyList<PurchaseBatchCostResponse>> CostsAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await RequirePurchaseAsync(organizationId, id, ct);
        return await store.GetPurchaseBatchCostsAsync(organizationId, id, ct);
    }
    public async Task<IReadOnlyList<PurchaseBatchPriceResponse>> PricesAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await RequirePurchaseAsync(organizationId, id, ct);
        return await store.GetPurchaseBatchPricesAsync(organizationId, id, ct);
    }
    private async Task RequirePurchaseAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        _ = await store.FindPurchaseAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Purchase not found.");

    public Task<StorageLocationResponse> CreateLocationAsync(Guid organizationId, StorageLocationRequest request, CancellationToken ct) =>
        store.InTransactionAsync(_ =>
        {
            var location = Trade.Domain.Inventory.StorageLocation.Create(organizationId, request.Name);
            store.AddStorageLocation(location);
            return Task.FromResult(new StorageLocationResponse(location.Id, location.Name));
        }, ct);
}
