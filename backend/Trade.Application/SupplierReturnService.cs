using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Purchasing;
using Trade.Domain.Returns;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class SupplierReturnService(ITradeStore store)
{
    public Task<SupplierReturnResponse> CreateAsync(Guid organizationId, Guid actorId, SupplierReturnRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var source = await SourceAsync(organizationId, request.SourcePurchaseInvoiceId, token);
            return await CreateAsync(organizationId, actorId, source, request.DocumentDate, request.StorageLocationId,
                RequireLines(request.Lines), request.Comment, request.InternalNote, token);
        }, ct);

    public Task<SupplierReturnResponse> FromPurchaseAsync(Guid organizationId, Guid actorId, Guid purchaseId,
        SupplierReturnFromPurchaseRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            var source = await SourceAsync(organizationId, purchaseId, token);
            return await CreateAsync(organizationId, actorId, source, request.DocumentDate,
                request.StorageLocationId ?? source.StorageLocationId, RequireLines(request.Lines ?? []),
                request.Comment, request.InternalNote, token);
        }, ct);

    private async Task<SupplierReturnResponse> CreateAsync(Guid organizationId, Guid actorId, PurchaseInvoice source,
        DateOnly date, Guid locationId, IReadOnlyList<SupplierReturnLineInput> inputs, string? comment, string? note, CancellationToken ct)
    {
        await RequireLocationAsync(organizationId, locationId, ct);
        var (products, batches) = await ReferencesAsync(organizationId, source, ct);
        var document = Document.Create(organizationId, DocumentType.SUPPLIER_RETURN, date, comment, note, actorId, DateTimeOffset.UtcNow);
        var result = SupplierReturn.Create(document, source, locationId, inputs, products, await TotalsAsync(source.DocumentId, ct));
        StockRules.RequireAvailable(Demands(result, batches), await store.GetStockAsync(BatchIds(batches), null, ct));
        store.AddSupplierReturn(result);
        return result.ToResponse();
    }

    public async Task<SupplierReturnResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public async Task<IReadOnlyList<SupplierReturnResponse>> ListAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListSupplierReturnsAsync(organizationId, ct)).Select(x => x.ToResponse()).ToArray();

    public Task<SupplierReturnResponse> UpdateAsync(Guid organizationId, Guid id, UpdateSupplierReturnRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            result.Document.RequireDraft();
            if (result.Document.Version != request.Version) throw new ConflictException("Document Version is stale. Reload before editing.");
            PurchaseInvoice? source = null;
            foreach (var sourceId in new[] { result.SourcePurchaseInvoiceId, request.SourcePurchaseInvoiceId }.Distinct().Order())
            {
                var candidate = await SourceAsync(organizationId, sourceId, token);
                if (sourceId == request.SourcePurchaseInvoiceId) source = candidate;
            }
            await RequireLocationAsync(organizationId, request.StorageLocationId, token);
            // Include old batches when replacing the source/lines so releasing old reserves shares the same stock lock.
            var oldBatches = await store.FindBatchesAsync(result.Lines.Select(x => x.SourcePurchaseLineId).ToArray(), token);
            var (products, batches) = await ReferencesAsync(organizationId, source!, token, oldBatches.Select(x => x.Id).ToArray());
            result.Update(source!, request.StorageLocationId, RequireLines(request.Lines), products, await TotalsAsync(source!.DocumentId, token));
            var ids = BatchIds(batches).Union(oldBatches.Select(x => x.Id)).Order().ToArray();
            StockRules.RequireAvailable(Demands(result, batches), await store.GetStockAsync(ids, id, token));
            result.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return result.ToResponse();
        }, ct);

    public Task<SupplierReturnResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            if (result.Document.Status == DocumentStatus.POSTED) return result.ToResponse();
            var source = await SourceAsync(organizationId, result.SourcePurchaseInvoiceId, token);
            await RequireLocationAsync(organizationId, result.StorageLocationId, token);
            var (products, batches) = await ReferencesAsync(organizationId, source, token);
            result.ValidateForPost(source, products, await TotalsAsync(source.DocumentId, token));
            StockRules.RequireAvailable(Demands(result, batches), await store.GetStockAsync(BatchIds(batches), id, token), posting: true);
            foreach (var line in result.Lines)
                store.AddMovement(StockMovement.Issue(id, batches[line.SourcePurchaseLineId].Id, result.StorageLocationId, line.Quantity));
            var number = result.Document.Number ?? $"SR-{await store.NextDocumentSequenceAsync(token):D8}";
            result.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            store.AddSettlementMovement(SettlementMovement.SupplierReturn(result, source));
            return result.ToResponse();
        }, ct);

    public Task<SupplierReturnResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            if (result.Document.Status == DocumentStatus.DRAFT) return result.ToResponse();
            var source = await SourceAsync(organizationId, result.SourcePurchaseInvoiceId, token);
            var (_, batches) = await ReferencesAsync(organizationId, source, token);
            var movements = await store.GetMovementsAsync(id, token);
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(BatchIds(batches), id, token), Demands(result, batches));
            await store.DeleteMovementsAsync(id, token);
            await store.DeleteSettlementMovementsAsync(id, token);
            result.Document.Unpost(DateTimeOffset.UtcNow);
            return result.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            result.Document.RequireDraft();
            var source = await SourceAsync(organizationId, result.SourcePurchaseInvoiceId, token);
            await ReferencesAsync(organizationId, source, token);
            result.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    public Task<IReadOnlyList<SupplierReturnableLineResponse>> ReturnableLinesAsync(Guid organizationId, Guid purchaseId,
        Guid? storageLocationId, CancellationToken ct) => store.InTransactionAsync<IReadOnlyList<SupplierReturnableLineResponse>>(async token =>
        {
            var source = await SourceAsync(organizationId, purchaseId, token);
            var location = storageLocationId ?? source.StorageLocationId;
            await RequireLocationAsync(organizationId, location, token);
            var (_, batches) = await ReferencesAsync(organizationId, source, token);
            var totals = await TotalsAsync(purchaseId, token);
            var stocks = (await store.GetStockAsync(BatchIds(batches), null, token))
                .Where(x => x.StorageLocationId == location).ToDictionary(x => x.BatchId);
            return source.Lines.Where(x => x.RemovedAt is null).OrderBy(x => x.LineNo).ThenBy(x => x.Id).Select(line =>
            {
                var batch = batches[line.Id];
                var returned = totals.GetValueOrDefault(line.Id);
                var remaining = line.Quantity - returned;
                stocks.TryGetValue(batch.Id, out var stock);
                return new SupplierReturnableLineResponse(line.Id, line.ProductId, batch.Id, line.Quantity, returned, remaining,
                    stock?.PhysicalQuantity ?? 0m, stock?.ReservedQuantity ?? 0m, stock?.AvailableQuantity ?? 0m,
                    Math.Min(remaining, stock?.AvailableQuantity ?? 0m), line.UnitPriceCurrency, source.DiscountPercent,
                    source.CurrencyId, source.ExchangeRate);
            }).ToArray();
        }, ct);

    private async Task<PurchaseInvoice> SourceAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        Guard.RequiredId(id, "SourcePurchaseInvoiceId");
        var source = await store.LockSupplierReturnSourceAsync(organizationId, id, ct) ?? throw new NotFoundException("Source purchase not found.");
        SupplierReturn.RequireSource(source, organizationId);
        return source;
    }

    private async Task RequireLocationAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        Guard.RequiredId(id, "StorageLocationId");
        Guard.Against(!await store.IsKnownStorageLocationAsync(organizationId, id, ct),
            "StorageLocation must already be used by a purchase or sale in the current organization.");
    }

    private async Task<(Dictionary<Guid, Product> Products, Dictionary<Guid, Batch> Batches)> ReferencesAsync(Guid organizationId,
        PurchaseInvoice source, CancellationToken ct, Guid[]? additionalBatchIds = null)
    {
        var lines = source.Lines.Where(x => x.RemovedAt is null).ToArray();
        var productIds = lines.Select(x => x.ProductId).Distinct().Order().ToArray();
        var products = await store.LockProductsAsync(organizationId, productIds, ct);
        Guard.Against(products.Count != productIds.Length || products.Any(x => x.ItemType != ItemType.GOODS),
            "SupplierReturn supports only GOODS in the current organization.");
        var batches = (await store.FindBatchesAsync(lines.Select(x => x.Id).ToArray(), ct)).ToDictionary(x => x.PurchaseInvoiceLineId);
        Guard.Against(batches.Count != lines.Length, "Every active source line must have its original Batch.");
        var ids = BatchIds(batches).Union(additionalBatchIds ?? []).Order().ToArray();
        var locked = await store.LockBatchesAsync(organizationId, ids, ct);
        Guard.Against(locked.Count != ids.Length || lines.Any(line => locked.All(x => x.BatchId != batches[line.Id].Id || x.ProductId != line.ProductId)),
            "Batch must belong to its SourcePurchaseLine and organization.");
        return (products.ToDictionary(x => x.Id), batches);
    }

    private async Task<Dictionary<Guid, decimal>> TotalsAsync(Guid sourceId, CancellationToken ct) =>
        (await store.GetSupplierReturnedQuantitiesAsync(sourceId, ct)).ToDictionary(x => x.SourcePurchaseLineId, x => x.Quantity);
    private async Task<SupplierReturn> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindSupplierReturnAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("SupplierReturn not found.");
    private static Guid[] BatchIds(IReadOnlyDictionary<Guid, Batch> batches) => batches.Values.Select(x => x.Id).Distinct().Order().ToArray();
    private static Dictionary<(Guid BatchId, Guid StorageLocationId), decimal> Demands(SupplierReturn result, IReadOnlyDictionary<Guid, Batch> batches) =>
        result.Lines.GroupBy(x => (BatchId: batches[x.SourcePurchaseLineId].Id, result.StorageLocationId))
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
    private static IReadOnlyList<SupplierReturnLineInput> RequireLines(IReadOnlyList<SupplierReturnLineInput>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
