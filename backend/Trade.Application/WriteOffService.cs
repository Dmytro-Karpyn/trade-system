using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;

namespace Trade.Application;

public sealed class WriteOffService(ITradeStore store)
{
    public Task<WriteOffReasonResponse> CreateReasonAsync(Guid organizationId, WriteOffReasonRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await RequireCategoryAsync(organizationId, request.ExpenseCategoryId, token);
            var reason = WriteOffReason.Create(organizationId, request.ToDetails());
            store.AddWriteOffReason(reason);
            return reason.ToResponse();
        }, ct);

    public async Task<IReadOnlyList<WriteOffReasonResponse>> ReasonsAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListWriteOffReasonsAsync(organizationId, ct)).Select(x => x.ToResponse()).ToArray();

    public async Task<WriteOffReasonResponse> ReasonAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindReasonAsync(organizationId, id, false, ct)).ToResponse();

    public Task<WriteOffReasonResponse> UpdateReasonAsync(Guid organizationId, Guid id, WriteOffReasonRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var reason = await FindReasonAsync(organizationId, id, true, token);
            await RequireCategoryAsync(organizationId, request.ExpenseCategoryId, token);
            reason.Update(request.ToDetails());
            return reason.ToResponse();
        }, ct);

    public Task<WriteOffResponse> CreateAsync(Guid organizationId, Guid actorId, WriteOffRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var lines = RequireLines(request.Lines);
            await RequireReferencesAsync(organizationId, request.StorageLocationId, request.WriteOffReasonId, token);
            var owned = await LockBatchesAsync(organizationId, lines.Select(x => x.BatchId), token);
            var document = Document.Create(organizationId, DocumentType.WRITE_OFF, request.DocumentDate,
                request.Comment, request.InternalNote, actorId, DateTimeOffset.UtcNow);
            var writeOff = WriteOff.Create(document, request.StorageLocationId, request.WriteOffReasonId, lines, owned);
            StockRules.RequireAvailable(Demands(writeOff), await store.GetStockAsync(owned.Order().ToArray(), null, token));
            store.AddWriteOff(writeOff);
            return writeOff.ToResponse();
        }, ct);

    public async Task<WriteOffResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public async Task<IReadOnlyList<WriteOffResponse>> ListAsync(Guid organizationId, WriteOffFilter filter, CancellationToken ct)
    {
        Guard.Against(filter.From > filter.To, "from must not be after to.");
        Guard.Against(filter.Status is { } status && !Enum.IsDefined(status), "Unsupported status.");
        return (await store.ListWriteOffsAsync(organizationId, filter, ct)).Select(x => x.ToResponse()).ToArray();
    }

    public Task<WriteOffResponse> UpdateAsync(Guid organizationId, Guid id, UpdateWriteOffRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var writeOff = await FindAsync(organizationId, id, true, token);
            writeOff.Document.RequireDraft();
            if (writeOff.Document.Version != request.Version) throw new ConflictException("Document Version is stale. Reload before editing.");
            var lines = RequireLines(request.Lines);
            await RequireReferencesAsync(organizationId, request.StorageLocationId, request.WriteOffReasonId, token);
            var owned = await LockBatchesAsync(organizationId, writeOff.Lines.Select(x => x.BatchId).Concat(lines.Select(x => x.BatchId)), token);
            writeOff.Update(request.StorageLocationId, request.WriteOffReasonId, lines, owned);
            StockRules.RequireAvailable(Demands(writeOff), await store.GetStockAsync(owned.Order().ToArray(), id, token));
            writeOff.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return writeOff.ToResponse();
        }, ct);

    public Task<WriteOffResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var writeOff = await FindAsync(organizationId, id, true, token);
            if (writeOff.Document.Status == DocumentStatus.POSTED) return writeOff.ToResponse();
            await RequireReferencesAsync(organizationId, writeOff.StorageLocationId, writeOff.WriteOffReasonId, token);
            var owned = await LockBatchesAsync(organizationId, writeOff.Lines.Select(x => x.BatchId), token);
            writeOff.ValidateForPost(owned);
            StockRules.RequireAvailable(Demands(writeOff), await store.GetStockAsync(owned.Order().ToArray(), id, token), posting: true);
            var number = writeOff.Document.Number ?? $"WO-{await store.NextDocumentSequenceAsync(token):D8}";
            writeOff.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            foreach (var line in writeOff.Lines)
                store.AddMovement(StockMovement.Issue(id, line.BatchId, writeOff.StorageLocationId, line.Quantity));
            return writeOff.ToResponse();
        }, ct);

    public Task<WriteOffResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var writeOff = await FindAsync(organizationId, id, true, token);
            if (writeOff.Document.Status == DocumentStatus.DRAFT) return writeOff.ToResponse();
            var movements = await store.GetMovementsAsync(id, token);
            var owned = await LockBatchesAsync(organizationId, writeOff.Lines.Select(x => x.BatchId).Concat(movements.Select(x => x.BatchId)), token);
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(owned.Order().ToArray(), id, token), Demands(writeOff));
            await store.DeleteMovementsAsync(id, token);
            writeOff.Document.Unpost(DateTimeOffset.UtcNow);
            return writeOff.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var writeOff = await FindAsync(organizationId, id, true, token);
            writeOff.Document.RequireDraft();
            await LockBatchesAsync(organizationId, writeOff.Lines.Select(x => x.BatchId), token);
            writeOff.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    private async Task RequireCategoryAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        Guard.RequiredId(id, "ExpenseCategoryId");
        Guard.Against(await store.FindExpenseCategoryAsync(organizationId, id, ct) is null,
            "ExpenseCategory must belong to the current organization.");
    }

    private async Task RequireReferencesAsync(Guid organizationId, Guid locationId, Guid reasonId, CancellationToken ct)
    {
        Guard.RequiredId(locationId, "StorageLocationId");
        Guard.RequiredId(reasonId, "WriteOffReasonId");
        Guard.Against(!await store.IsKnownStorageLocationAsync(organizationId, locationId, ct),
            "StorageLocation must already be used by a purchase or sale in the current organization.");
        var reason = await FindReasonAsync(organizationId, reasonId, false, ct);
        await RequireCategoryAsync(organizationId, reason.ExpenseCategoryId, ct);
    }

    private async Task<HashSet<Guid>> LockBatchesAsync(Guid organizationId, IEnumerable<Guid> batchIds, CancellationToken ct)
    {
        var ids = batchIds.Distinct().Order().ToArray();
        var batches = await store.LockBatchesAsync(organizationId, ids, ct);
        Guard.Against(batches.Count != ids.Length, "Every Batch must exist in the current organization.");
        return batches.Select(x => x.BatchId).ToHashSet();
    }
    private async Task<WriteOffReason> FindReasonAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindWriteOffReasonAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("WriteOffReason not found.");
    private async Task<WriteOff> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindWriteOffAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("WriteOff not found.");
    private static Dictionary<(Guid BatchId, Guid StorageLocationId), decimal> Demands(WriteOff w) =>
        w.Lines.GroupBy(x => (x.BatchId, w.StorageLocationId)).ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
    private static IReadOnlyList<WriteOffLineInput> RequireLines(IReadOnlyList<WriteOffLineInput>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
