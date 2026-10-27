using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Returns;
using Trade.Domain.Sales;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class CustomerReturnService(ITradeStore store)
{
    public Task<CustomerReturnResponse> CreateAsync(Guid organizationId, Guid actorId, CustomerReturnRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var source = await SourceAsync(organizationId, request.SourceSaleInvoiceId, token);
            return await CreateAsync(organizationId, actorId, source, request.DocumentDate, request.StorageLocationId,
                RequireLines(request.Lines), request.Comment, request.InternalNote, token);
        }, ct);

    public Task<CustomerReturnResponse> FromSaleAsync(Guid organizationId, Guid actorId, Guid saleId,
        CustomerReturnFromSaleRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            var source = await SourceAsync(organizationId, saleId, token);
            return await CreateAsync(organizationId, actorId, source, request.DocumentDate,
                request.StorageLocationId ?? source.StorageLocationId, RequireLines(request.Lines ?? []),
                request.Comment, request.InternalNote, token);
        }, ct);

    private async Task<CustomerReturnResponse> CreateAsync(Guid organizationId, Guid actorId, SaleInvoice source, DateOnly date,
        Guid locationId, IReadOnlyList<CustomerReturnLineInput> lines, string? comment, string? note, CancellationToken ct)
    {
        await RequireLocationAsync(organizationId, locationId, ct);
        var products = await ProductsAsync(organizationId, source, ct);
        var totals = await TotalsAsync(source.DocumentId, ct);
        var document = Document.Create(organizationId, DocumentType.CUSTOMER_RETURN, date, comment, note, actorId, DateTimeOffset.UtcNow);
        var result = CustomerReturn.Create(document, source, locationId, lines, products, totals);
        store.AddCustomerReturn(result);
        return result.ToResponse();
    }

    public async Task<CustomerReturnResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public async Task<IReadOnlyList<CustomerReturnResponse>> ListAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListCustomerReturnsAsync(organizationId, ct)).Select(x => x.ToResponse()).ToArray();

    public Task<CustomerReturnResponse> UpdateAsync(Guid organizationId, Guid id, UpdateCustomerReturnRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            result.Document.RequireDraft();
            if (result.Document.Version != request.Version) throw new ConflictException("Document Version is stale. Reload before editing.");
            // Lock old/new parents in ID order when changing source. All return writers share this parent lock.
            SaleInvoice? source = null;
            foreach (var sourceId in new[] { result.SourceSaleInvoiceId, request.SourceSaleInvoiceId }.Distinct().Order())
            {
                var candidate = await SourceAsync(organizationId, sourceId, token);
                if (sourceId == request.SourceSaleInvoiceId) source = candidate;
            }
            await RequireLocationAsync(organizationId, request.StorageLocationId, token);
            result.Update(source!, request.StorageLocationId, RequireLines(request.Lines),
                await ProductsAsync(organizationId, source!, token), await TotalsAsync(source!.DocumentId, token));
            result.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return result.ToResponse();
        }, ct);

    public Task<CustomerReturnResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            if (result.Document.Status == DocumentStatus.POSTED) return result.ToResponse();
            var source = await SourceAsync(organizationId, result.SourceSaleInvoiceId, token);
            await RequireLocationAsync(organizationId, result.StorageLocationId, token);
            var products = await ProductsAsync(organizationId, source, token);
            result.ValidateForPost(source, products, await TotalsAsync(source.DocumentId, token));
            var sourceLines = source.Lines.ToDictionary(x => x.Id);
            var batchIds = result.Lines.Select(x => sourceLines[x.SourceSaleLineId].BatchId)
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct().Order().ToArray();
            await store.LockBatchesAsync(organizationId, batchIds, token);
            foreach (var line in result.Lines.Where(x => products[sourceLines[x.SourceSaleLineId].ProductId].ItemType == ItemType.GOODS))
                store.AddMovement(StockMovement.Receipt(id, sourceLines[line.SourceSaleLineId].BatchId!.Value,
                    result.StorageLocationId, line.Quantity));
            var number = result.Document.Number ?? $"CR-{await store.NextDocumentSequenceAsync(token):D8}";
            result.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            store.AddSettlementMovement(SettlementMovement.CustomerReturn(result, source));
            return result.ToResponse();
        }, ct);

    public Task<CustomerReturnResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            if (result.Document.Status == DocumentStatus.DRAFT) return result.ToResponse();
            await SourceAsync(organizationId, result.SourceSaleInvoiceId, token);
            var movements = await store.GetMovementsAsync(id, token);
            var batchIds = movements.Select(x => x.BatchId).Distinct().Order().ToArray();
            await store.LockBatchesAsync(organizationId, batchIds, token);
            StockRules.RequireSafeRemoval(movements, await store.GetStockAsync(batchIds, null, token));
            await store.DeleteMovementsAsync(id, token);
            await store.DeleteSettlementMovementsAsync(id, token);
            result.Document.Unpost(DateTimeOffset.UtcNow);
            return result.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var result = await FindAsync(organizationId, id, true, token);
            result.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    public Task<IReadOnlyList<ReturnableLineResponse>> ReturnableLinesAsync(Guid organizationId, Guid saleId, CancellationToken ct) =>
        store.InTransactionAsync<IReadOnlyList<ReturnableLineResponse>>(async token =>
        {
            // Read source and totals under the same parent lock used by POST/UNPOST, yielding a consistent response.
            var source = await SourceAsync(organizationId, saleId, token);
            var products = await ProductsAsync(organizationId, source, token);
            var totals = await TotalsAsync(saleId, token);
            return source.Lines.Where(x => x.RemovedAt is null).OrderBy(x => x.LineNo).ThenBy(x => x.Id).Select(line =>
            {
                var returned = totals.GetValueOrDefault(line.Id) ?? new(line.Id, 0m, 0m);
                var net = CustomerReturn.NetAmount(source, line);
                return new ReturnableLineResponse(line.Id, line.ProductId, products[line.ProductId].ItemType, line.BatchId,
                    line.Quantity, returned.Quantity, Math.Max(0m, line.Quantity - returned.Quantity), line.UnitPrice,
                    source.DiscountPercent, net, returned.RefundAmount, net - returned.RefundAmount);
            }).ToArray();
        }, ct);

    private async Task<SaleInvoice> SourceAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        Guard.RequiredId(id, "SourceSaleInvoiceId");
        var sale = await store.LockReturnSourceAsync(organizationId, id, ct) ?? throw new NotFoundException("Source sale not found.");
        CustomerReturn.RequireSource(sale, organizationId);
        return sale;
    }

    private async Task RequireLocationAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        Guard.RequiredId(id, "StorageLocationId");
        Guard.Against(!await store.IsKnownStorageLocationAsync(organizationId, id, ct),
            "StorageLocation must already be used by a purchase or sale in the current organization.");
    }

    private async Task<Dictionary<Guid, Product>> ProductsAsync(Guid organizationId, SaleInvoice source, CancellationToken ct)
    {
        var ids = source.Lines.Where(x => x.RemovedAt is null).Select(x => x.ProductId).Distinct().Order().ToArray();
        var products = await store.LockProductsAsync(organizationId, ids, ct);
        Guard.Against(products.Count != ids.Length, "Source products must belong to the current organization.");
        return products.ToDictionary(x => x.Id);
    }

    private async Task<Dictionary<Guid, ReturnedLineTotals>> TotalsAsync(Guid sourceId, CancellationToken ct) =>
        (await store.GetReturnedTotalsAsync(sourceId, ct)).ToDictionary(x => x.SourceSaleLineId);

    private async Task<CustomerReturn> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindCustomerReturnAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("CustomerReturn not found.");

    private static IReadOnlyList<CustomerReturnLineInput> RequireLines(IReadOnlyList<CustomerReturnLineInput>? lines)
    {
        Guard.Against(lines is null || lines.Any(x => x is null), "Lines must be an array of nonnull lines.");
        return lines!;
    }
}
