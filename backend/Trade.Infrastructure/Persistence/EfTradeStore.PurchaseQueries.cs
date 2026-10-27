using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<IReadOnlyList<PurchaseListResponse>> ListPurchasesAsync(Guid organizationId, PurchaseFilter filter, CancellationToken ct)
    {
        var query = db.PurchaseInvoices.AsNoTracking().Where(x => x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null && x.Document.DocumentType == DocumentType.PURCHASE_INVOICE);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.CounterpartyId is { } party) query = query.Where(x => x.CounterpartyId == party);
        if (filter.StorageLocationId is { } location) query = query.Where(x => x.StorageLocationId == location);
        if (filter.CurrencyId is { } currency) query = query.Where(x => x.CurrencyId == currency);
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(x => x.Document.Number != null && EF.Functions.ILike(x.Document.Number, pattern, "\\")
                || x.ExternalNumber != null && EF.Functions.ILike(x.ExternalNumber, pattern, "\\")
                || db.Counterparties.Any(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId
                    && EF.Functions.ILike(p.Name, pattern, "\\")));
        }
        return await query.OrderByDescending(x => x.Document.DocumentDate).ThenByDescending(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId)
            .Select(x => new PurchaseListResponse(x.DocumentId, x.Document.Number, x.Document.DocumentDate, x.CounterpartyId,
                db.Counterparties.Where(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId).Select(p => p.Name).FirstOrDefault(),
                x.StorageLocationId, x.CurrencyId,
                Math.Round(x.Lines.Where(l => l.RemovedAt == null).Sum(l => l.Quantity * l.UnitPriceCurrency) * (1m - x.DiscountPercent / 100m), 4),
                x.Document.Status, x.ExternalNumber)).ToListAsync(ct);
    }

    public async Task<PurchaseLookupsResponse> GetPurchaseLookupsAsync(Guid organizationId, CancellationToken ct)
    {
        var known = await db.StorageLocations.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name)
            .Select(x => new StorageLocationResponse(x.Id, x.Name)).ToListAsync(ct);
        var used = await db.PurchaseInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)
            .Union(db.SaleInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)).ToListAsync(ct);
        // External GUIDs used before the minimal directory remain selectable; do not invent real names.
        var locations = known.Concat(used.Where(id => known.All(x => x.Id != id)).Order().Select(id => new StorageLocationResponse(id, null))).ToArray();
        var currencies = await db.PurchaseInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.CurrencyId)
            .Union(db.MoneyAccounts.Where(x => x.OrganizationId == organizationId).Select(x => x.CurrencyId))
            .Union(db.Expenses.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.CurrencyId)).ToListAsync(ct);
        return new(locations, currencies.Append(Guid.Parse("00000000-0000-0000-0000-000000000980")).Distinct().Order().ToArray());
    }

    public async Task<IReadOnlyList<PurchaseProductResponse>> GetPurchaseProductsAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from line in db.PurchaseInvoiceLines
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               join product in db.Products on line.ProductId equals product.Id
               where purchase.DocumentId == id && purchase.Document.OrganizationId == organizationId && product.OrganizationId == organizationId
               select new PurchaseProductResponse(product.Id, product.Name, product.Code)).Distinct().ToListAsync(ct);

    public async Task<string?> GetPurchasePosterNameAsync(Guid organizationId, Guid? actorId, CancellationToken ct) => actorId is null ? null :
        await db.Users.Where(x => x.Id == actorId && x.OrganizationId == organizationId).Select(x => x.FullName ?? x.UserName).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PurchaseBatchResponse>> GetPurchaseBatchesAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from batch in db.Batches
               join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               join product in db.Products on line.ProductId equals product.Id
               where purchase.DocumentId == id && purchase.Document.OrganizationId == organizationId && product.OrganizationId == organizationId
               orderby line.LineNo, batch.Id
               select new PurchaseBatchResponse(batch.Id, line.Id, product.Id, product.Name, product.Code, batch.BatchNumber, batch.CreatedAt)).ToListAsync(ct);

    public async Task<IReadOnlyList<PurchaseBatchCostResponse>> GetPurchaseBatchCostsAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from cost in db.BatchCosts
               join line in db.PurchaseInvoiceLines on cost.PurchaseInvoiceLineId equals line.Id
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               where purchase.DocumentId == id && purchase.Document.OrganizationId == organizationId
               select new PurchaseBatchCostResponse(cost.BatchId, cost.UnitCostUah)).ToListAsync(ct);

    public async Task<IReadOnlyList<PurchaseBatchPriceResponse>> GetPurchaseBatchPricesAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from price in db.BatchPrices
               join type in db.PriceTypes on price.PriceTypeId equals type.Id
               join batch in db.Batches on price.BatchId equals batch.Id
               join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               where purchase.DocumentId == id && purchase.Document.OrganizationId == organizationId && type.OrganizationId == organizationId
               orderby type.SortOrder, type.Name, batch.BatchNumber
               select new PurchaseBatchPriceResponse(batch.Id, type.Id, type.Name, price.UnitPrice)).ToListAsync(ct);

    public Task<bool> IsForeignStorageLocationAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        db.StorageLocations.AnyAsync(x => x.Id == id && x.OrganizationId != organizationId, ct);
    public void AddStorageLocation(StorageLocation location) => db.StorageLocations.Add(location);
}
