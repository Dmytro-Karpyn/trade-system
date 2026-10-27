using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Purchasing;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class PurchaseService(ITradeStore store)
{
    public Task<PurchaseResponse> CreateAsync(Guid organizationId, Guid actorId, PurchaseRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var lines = RequireLines(request.Lines);
            await RequireLocationAsync(organizationId, request.StorageLocationId, token);
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            await ValidateProductsAsync(organizationId, lines.Select(x => x.ProductId).ToArray(), token);
            var now = DateTimeOffset.UtcNow;
            var document = Document.Create(organizationId, request.DocumentType, request.DocumentDate,
                request.Comment, request.InternalNote, actorId, now);
            var purchase = PurchaseInvoice.Create(document, request.ToDetails(), lines, now);
            store.AddPurchase(purchase);
            return purchase.ToResponse();
        }, ct);

    public async Task<PurchaseResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var purchase = await FindAsync(organizationId, id, false, ct);
        return purchase.ToResponse() with
        {
            Products = await store.GetPurchaseProductsAsync(organizationId, id, ct),
            PostedByName = await store.GetPurchasePosterNameAsync(organizationId, purchase.Document.PostedBy, ct)
        };
    }

    public Task<PurchaseResponse> UpdateAsync(Guid organizationId, Guid id, UpdatePurchaseRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var purchase = await FindAsync(organizationId, id, true, token);
            purchase.Document.RequireDraft();
            if (request.Version != purchase.Document.Version)
                throw new ConflictException("Document Version is stale. Reload the document before editing.");
            var lines = RequireLines(request.Lines);
            await RequireLocationAsync(organizationId, request.StorageLocationId, token);
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            await ValidateProductsAsync(organizationId, lines.Select(x => x.ProductId).ToArray(), token);
            var batches = await store.FindBatchesAsync(purchase.Lines.Select(x => x.Id).ToArray(), token);
            var now = DateTimeOffset.UtcNow;
            purchase.Update(request.ToDetails(), lines, batches.Select(x => x.PurchaseInvoiceLineId).ToHashSet(), now);
            purchase.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, now);
            return purchase.ToResponse();
        }, ct);

    public Task<PurchaseResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var purchase = await FindAsync(organizationId, id, true, token);
            // A replay of an already completed POST must not change audit fields or movements.
            if (purchase.Document.Status == DocumentStatus.POSTED) return purchase.ToResponse();
            purchase.ValidateForPost();
            await CounterpartyReferences.RequireAsync(store, organizationId, purchase.CounterpartyId, token);
            var lines = purchase.Lines.Where(x => x.RemovedAt is null).ToArray();
            await ValidateProductsAsync(organizationId, lines.Select(x => x.ProductId).ToArray(), token);
            var batches = (await store.FindBatchesAsync(lines.Select(x => x.Id).ToArray(), token))
                .ToDictionary(x => x.PurchaseInvoiceLineId);
            await store.LockBatchesAsync(organizationId, batches.Values.Select(x => x.Id).Order().ToArray(), token);
            var now = DateTimeOffset.UtcNow;
            var newBatchIds = new List<Guid>();
            foreach (var line in lines)
            {
                if (!batches.TryGetValue(line.Id, out var batch))
                {
                    batch = Batch.Create(line.Id, now);
                    store.AddBatch(batch);
                    newBatchIds.Add(batch.Id);
                }
                store.AddMovement(StockMovement.Receipt(id, batch.Id, purchase.StorageLocationId, line.Quantity));
            }

            var number = purchase.Document.Number;
            if (number is null)
            {
                var sequence = await store.NextDocumentSequenceAsync(token);
                number = $"{(purchase.Document.DocumentType == DocumentType.OPENING_STOCK ? "OS" : "PI")}-{sequence:D8}";
            }
            purchase.Document.Post(number, actorId, now);
            if (purchase.Document.DocumentType == DocumentType.PURCHASE_INVOICE)
                store.AddSettlementMovement(SettlementMovement.Purchase(purchase));
            // Only batches first created by this POST receive initial prices. Reposting never reprices old batches.
            if (newBatchIds.Count > 0)
                await store.InitializeNewBatchPricesAsync(organizationId, newBatchIds.ToArray(), token);
            return purchase.ToResponse();
        }, ct);

    public Task<PurchaseResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var purchase = await FindAsync(organizationId, id, true, token);
            if (purchase.Document.Status == DocumentStatus.DRAFT) return purchase.ToResponse();
            if (await store.HasPurchaseExpensesAsync(id, token)) throw new DocumentHasDependenciesException();
            if (await store.HasSupplierReturnsAsync(id, token)) throw new DocumentHasDependenciesException();
            var movements = await store.GetMovementsAsync(id, token);
            var batchIds = movements.Select(x => x.BatchId).Distinct().Order().ToArray();
            await store.LockBatchesAsync(organizationId, batchIds, token);
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(batchIds, null, token));
            await store.DeleteMovementsAsync(id, token);
            await store.DeleteSettlementMovementsAsync(id, token);
            purchase.Document.Unpost(DateTimeOffset.UtcNow);
            return purchase.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var purchase = await FindAsync(organizationId, id, true, token);
            purchase.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    private async Task RequireLocationAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        Guard.Against(await store.IsForeignStorageLocationAsync(organizationId, id, ct),
            "StorageLocation must belong to the current organization.");

    private async Task<PurchaseInvoice> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindPurchaseAsync(organizationId, id, forUpdate, ct)
        ?? throw new NotFoundException("Purchase not found.");

    private async Task ValidateProductsAsync(Guid organizationId, Guid[] productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().Order().ToArray();
        var products = await store.LockProductsAsync(organizationId, ids, ct);
        Guard.Against(products.Count != ids.Length, "Every ProductId must belong to the current organization.");
        Guard.Against(products.Any(x => x.ItemType != ItemType.GOODS), "SERVICE cannot be a PurchaseInvoice line.");
    }

    private static IReadOnlyList<PurchaseLineDetails> RequireLines(IReadOnlyList<PurchaseLineDetails>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
