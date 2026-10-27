using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Sales;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class SaleService(ITradeStore store, ISalePricePermissionProvider pricePermissions)
{
    public Task<SaleResponse> CreateAsync(Guid organizationId, Guid actorId, SaleRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var lines = RequireLines(request.Lines);
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            var (products, batches) = await LockReferencesAsync(organizationId, lines.Select(x => x.ProductId),
                BatchIds(lines), token);
            var pricedLines = await ResolvePricesAsync(organizationId, request.PriceTypeId, lines, products, token);
            var now = DateTimeOffset.UtcNow;
            var document = Document.Create(organizationId, DocumentType.SALE_INVOICE, request.DocumentDate,
                request.Comment, request.InternalNote, actorId, now);
            var sale = SaleInvoice.Create(document, request.ToDetails(), pricedLines, products, batches,
                pricePermissions.GetPermissions(), now);
            StockRules.RequireAvailable(sale.Lines, sale.StorageLocationId,
                await store.GetStockAsync(BatchIds(lines), null, token));
            store.AddSale(sale);
            return sale.ToResponse();
        }, ct);

    public async Task<SaleResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public Task<SaleResponse> UpdateAsync(Guid organizationId, Guid id, UpdateSaleRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var sale = await FindAsync(organizationId, id, true, token);
            sale.Document.RequireDraft();
            if (request.Version != sale.Document.Version)
                throw new ConflictException("Document Version is stale. Reload the document before editing.");
            var lines = RequireLines(request.Lines);
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            var batchIds = ActiveBatchIds(sale).Union(BatchIds(lines)).Order().ToArray();
            var (products, batches) = await LockReferencesAsync(organizationId, lines.Select(x => x.ProductId), batchIds, token);
            var pricedLines = await ResolvePricesAsync(organizationId, request.PriceTypeId, lines, products, token, sale);
            var now = DateTimeOffset.UtcNow;
            sale.Update(request.ToDetails(), pricedLines, products, batches, pricePermissions.GetPermissions(), now);
            StockRules.RequireAvailable(sale.Lines, sale.StorageLocationId,
                await store.GetStockAsync(batchIds, id, token));
            sale.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, now);
            return sale.ToResponse();
        }, ct);

    public Task<SaleResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var sale = await FindAsync(organizationId, id, true, token);
            if (sale.Document.Status == DocumentStatus.POSTED) return sale.ToResponse();
            await CounterpartyReferences.RequireAsync(store, organizationId, sale.CounterpartyId, token);
            var lines = sale.Lines.Where(x => x.RemovedAt is null).ToArray();
            var batchIds = ActiveBatchIds(sale);
            var (products, batches) = await LockReferencesAsync(organizationId, lines.Select(x => x.ProductId), batchIds, token);
            sale.ValidateForPost(products, batches, pricePermissions.GetPermissions());
            StockRules.RequireAvailable(lines, sale.StorageLocationId, await store.GetStockAsync(batchIds, id, token), posting: true);
            foreach (var line in lines.Where(x => x.BatchId is not null))
                store.AddMovement(StockMovement.Issue(id, line.BatchId!.Value, sale.StorageLocationId, line.Quantity));
            var number = sale.Document.Number ?? $"SI-{await store.NextDocumentSequenceAsync(token):D8}";
            sale.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            store.AddSettlementMovement(SettlementMovement.Sale(sale));
            return sale.ToResponse();
        }, ct);

    public Task<SaleResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var sale = await FindAsync(organizationId, id, true, token);
            if (sale.Document.Status == DocumentStatus.DRAFT) return sale.ToResponse();
            if (await store.HasCustomerReturnsAsync(id, token)) throw new DocumentHasDependenciesException();
            var movements = await store.GetMovementsAsync(id, token);
            var batchIds = ActiveBatchIds(sale).Union(movements.Select(x => x.BatchId)).Order().ToArray();
            await store.LockBatchesAsync(organizationId, batchIds, token);
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(batchIds, id, token), sale.Lines, sale.StorageLocationId);
            await store.DeleteMovementsAsync(id, token);
            await store.DeleteSettlementMovementsAsync(id, token);
            sale.Document.Unpost(DateTimeOffset.UtcNow);
            return sale.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var sale = await FindAsync(organizationId, id, true, token);
            sale.Document.RequireDraft();
            await store.LockBatchesAsync(organizationId, ActiveBatchIds(sale), token);
            sale.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    public async Task<IReadOnlyList<AvailableBatchResponse>> AvailableBatchesAsync(Guid organizationId,
        Guid productId, Guid storageLocationId, CancellationToken ct)
    {
        Guard.RequiredId(productId, nameof(productId));
        Guard.RequiredId(storageLocationId, nameof(storageLocationId));
        var product = await store.FindProductAsync(organizationId, productId, false, ct)
            ?? throw new NotFoundException("Product not found.");
        return product.ItemType == ItemType.SERVICE ? []
            : await store.GetAvailableBatchesAsync(organizationId, productId, storageLocationId, ct);
    }

    private async Task<(Dictionary<Guid, Product> Products, Dictionary<Guid, Guid> Batches)> LockReferencesAsync(
        Guid organizationId, IEnumerable<Guid> productIds, Guid[] batchIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().Order().ToArray();
        var products = await store.LockProductsAsync(organizationId, ids, ct);
        Guard.Against(products.Count != ids.Length, "Every ProductId must belong to the current organization.");
        var batches = await store.LockBatchesAsync(organizationId, batchIds, ct);
        return (products.ToDictionary(x => x.Id), batches.ToDictionary(x => x.BatchId, x => x.ProductId));
    }

    private async Task<SaleInvoice> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindSaleAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("Sale not found.");

    private async Task<SaleLineDetails[]> ResolvePricesAsync(Guid organizationId, Guid? priceTypeId,
        IReadOnlyList<SaleLineRequest> lines, IReadOnlyDictionary<Guid, Product> products, CancellationToken ct,
        SaleInvoice? existing = null)
    {
        var previousLines = existing?.Lines.ToDictionary(x => x.Id) ?? [];
        SaleInvoiceLine? Snapshot(SaleLineRequest line) => existing?.PriceTypeId == priceTypeId
            && line.Id is { } id && previousLines.TryGetValue(id, out var previous)
            && previous.ProductId == line.ProductId && previous.BatchId == line.BatchId ? previous : null;
        var newSelections = lines.Where(x => Snapshot(x) is null).ToArray();
        var batchPrices = new Dictionary<Guid, decimal>();
        var productPrices = new Dictionary<Guid, decimal>();
        if (priceTypeId is { } typeId)
        {
            Guard.RequiredId(typeId, nameof(priceTypeId));
            _ = await store.FindPriceTypeAsync(organizationId, typeId, false, ct)
                ?? throw new NotFoundException("PriceType not found.");
            batchPrices = (await store.GetBatchPricesAsync(BatchIds(newSelections), typeId, ct))
                .ToDictionary(x => x.BatchId, x => x.UnitPrice);
            productPrices = (await store.GetProductPricesAsync(newSelections.Select(x => x.ProductId).Distinct().ToArray(), typeId, ct))
                .ToDictionary(x => x.ProductId, x => x.UnitPrice);
        }
        return lines.Select(line =>
        {
            var snapshot = Snapshot(line);
            var recommended = snapshot?.RecommendedUnitPrice ?? 0m;
            if (snapshot is null && priceTypeId is not null)
            {
                var prices = products[line.ProductId].ItemType == ItemType.GOODS ? batchPrices : productPrices;
                var key = products[line.ProductId].ItemType == ItemType.GOODS ? line.BatchId ?? Guid.Empty : line.ProductId;
                if (!prices.TryGetValue(key, out recommended)) throw new PriceNotFoundException();
            }
            return new SaleLineDetails(line.Id, line.LineNo, line.ProductId, line.BatchId, line.Quantity,
                recommended, line.UnitPrice ?? snapshot?.UnitPrice ?? recommended);
        }).ToArray();
    }

    private static Guid[] BatchIds(IEnumerable<SaleLineRequest> lines) =>
        lines.Where(x => x.BatchId is not null).Select(x => x.BatchId!.Value).Distinct().Order().ToArray();

    private static Guid[] ActiveBatchIds(SaleInvoice sale) => sale.Lines.Where(x => x.RemovedAt is null && x.BatchId is not null)
        .Select(x => x.BatchId!.Value).Distinct().Order().ToArray();

    private static IReadOnlyList<SaleLineRequest> RequireLines(IReadOnlyList<SaleLineRequest>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
