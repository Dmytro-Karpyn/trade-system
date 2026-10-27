using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;

namespace Trade.Application;

public sealed class StockTransferService(ITradeStore store)
{
    public Task<StockTransferResponse> CreateAsync(Guid organizationId, Guid actorId, StockTransferRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var lines = RequireLines(request.Lines);
            await RequireLocationsAsync(organizationId, request.FromLocationId, request.ToLocationId, token);
            var owned = await LockBatchesAsync(organizationId, lines.Select(x => x.BatchId), token);
            var document = Document.Create(organizationId, DocumentType.STOCK_TRANSFER, request.DocumentDate,
                request.Comment, request.InternalNote, actorId, DateTimeOffset.UtcNow);
            var transfer = StockTransfer.Create(document, request.FromLocationId, request.ToLocationId, lines, owned);
            StockRules.RequireAvailable(Demands(transfer), await store.GetStockAsync(owned.Order().ToArray(), null, token));
            store.AddStockTransfer(transfer);
            return transfer.ToResponse();
        }, ct);

    public async Task<StockTransferResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public async Task<IReadOnlyList<StockTransferResponse>> ListAsync(Guid organizationId, StockTransferFilter filter, CancellationToken ct)
    {
        Guard.Against(filter.From > filter.To, "from must not be after to.");
        Guard.Against(filter.Status is { } status && !Enum.IsDefined(status), "Unsupported status.");
        return (await store.ListStockTransfersAsync(organizationId, filter, ct)).Select(x => x.ToResponse()).ToArray();
    }

    public Task<StockTransferResponse> UpdateAsync(Guid organizationId, Guid id, UpdateStockTransferRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var transfer = await FindAsync(organizationId, id, true, token);
            transfer.Document.RequireDraft();
            if (transfer.Document.Version != request.Version) throw new ConflictException("Document Version is stale. Reload before editing.");
            var lines = RequireLines(request.Lines);
            await RequireLocationsAsync(organizationId, request.FromLocationId, request.ToLocationId, token);
            var owned = await LockBatchesAsync(organizationId, transfer.Lines.Select(x => x.BatchId).Concat(lines.Select(x => x.BatchId)), token);
            transfer.Update(request.FromLocationId, request.ToLocationId, lines, owned);
            // SQL still sees the saved DRAFT: subtract its old reserve, including its old source location/batches.
            StockRules.RequireAvailable(Demands(transfer), await store.GetStockAsync(owned.Order().ToArray(), id, token));
            transfer.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return transfer.ToResponse();
        }, ct);

    public Task<StockTransferResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var transfer = await FindAsync(organizationId, id, true, token);
            if (transfer.Document.Status == DocumentStatus.POSTED) return transfer.ToResponse();
            await RequireLocationsAsync(organizationId, transfer.FromLocationId, transfer.ToLocationId, token);
            var owned = await LockBatchesAsync(organizationId, transfer.Lines.Select(x => x.BatchId), token);
            transfer.ValidateForPost(owned);
            StockRules.RequireAvailable(Demands(transfer), await store.GetStockAsync(owned.Order().ToArray(), id, token), posting: true);
            var number = transfer.Document.Number ?? $"ST-{await store.NextDocumentSequenceAsync(token):D8}";
            transfer.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            foreach (var line in transfer.Lines)
            {
                store.AddMovement(StockMovement.Issue(id, line.BatchId, transfer.FromLocationId, line.Quantity));
                store.AddMovement(StockMovement.Receipt(id, line.BatchId, transfer.ToLocationId, line.Quantity));
            }
            return transfer.ToResponse();
        }, ct);

    public Task<StockTransferResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var transfer = await FindAsync(organizationId, id, true, token);
            if (transfer.Document.Status == DocumentStatus.DRAFT) return transfer.ToResponse();
            var movements = await store.GetMovementsAsync(id, token);
            var owned = await LockBatchesAsync(organizationId, transfer.Lines.Select(x => x.BatchId).Concat(movements.Select(x => x.BatchId)), token);
            // Remove both directions and restore only FromLocation's DRAFT reserve. Destination must remain usable.
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(owned.Order().ToArray(), id, token), Demands(transfer));
            await store.DeleteMovementsAsync(id, token);
            transfer.Document.Unpost(DateTimeOffset.UtcNow);
            return transfer.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var transfer = await FindAsync(organizationId, id, true, token);
            transfer.Document.RequireDraft();
            await LockBatchesAsync(organizationId, transfer.Lines.Select(x => x.BatchId), token);
            transfer.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    private async Task RequireLocationsAsync(Guid organizationId, Guid from, Guid to, CancellationToken ct)
    {
        StockTransfer.RequireLocations(from, to);
        Guard.Against(!await store.IsKnownStorageLocationAsync(organizationId, from, ct)
            || !await store.IsKnownStorageLocationAsync(organizationId, to, ct),
            "Both locations must already be used by a purchase or sale in the current organization.");
    }

    private async Task<HashSet<Guid>> LockBatchesAsync(Guid organizationId, IEnumerable<Guid> batchIds, CancellationToken ct)
    {
        var ids = batchIds.Distinct().Order().ToArray();
        var batches = await store.LockBatchesAsync(organizationId, ids, ct);
        Guard.Against(batches.Count != ids.Length, "Every Batch must exist in the current organization.");
        return batches.Select(x => x.BatchId).ToHashSet();
    }

    private async Task<StockTransfer> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindStockTransferAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("StockTransfer not found.");
    private static Dictionary<(Guid BatchId, Guid StorageLocationId), decimal> Demands(StockTransfer transfer) =>
        transfer.Lines.GroupBy(x => (x.BatchId, StorageLocationId: transfer.FromLocationId)).ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
    private static IReadOnlyList<StockTransferLineInput> RequireLines(IReadOnlyList<StockTransferLineInput>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
