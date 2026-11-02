using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<IReadOnlyList<SaleListResponse>> ListSalesAsync(Guid organizationId, SaleFilter filter, CancellationToken ct)
    {
        var query = db.SaleInvoices.AsNoTracking().Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.CounterpartyId is { } party) query = query.Where(x => x.CounterpartyId == party);
        if (filter.StorageLocationId is { } location) query = query.Where(x => x.StorageLocationId == location);
        if (filter.PriceTypeId is { } priceType) query = query.Where(x => x.PriceTypeId == priceType);
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => x.Document.Number != null && EF.Functions.ILike(x.Document.Number, pattern, "\\")
                || x.Document.Comment != null && EF.Functions.ILike(x.Document.Comment, pattern, "\\")
                || db.Counterparties.Any(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId && EF.Functions.ILike(p.Name, pattern, "\\")));
        }
        return await query.OrderByDescending(x => x.Document.DocumentDate).ThenByDescending(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId)
            .Select(x => new SaleListResponse(x.DocumentId, x.Document.Number, x.Document.DocumentDate, x.CounterpartyId,
                db.Counterparties.Where(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId).Select(p => p.Name).First(),
                x.StorageLocationId, x.PriceTypeId, db.PriceTypes.Where(p => p.Id == x.PriceTypeId && p.OrganizationId == organizationId).Select(p => p.Name).FirstOrDefault(),
                Math.Round(x.Lines.Where(l => l.RemovedAt == null).Sum(l => l.Quantity * l.UnitPrice) * (1m - x.DiscountPercent / 100m), 4), x.DiscountPercent, x.Document.Status)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SaleProductResponse>> GetSaleProductsAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from line in db.SaleInvoiceLines join sale in db.SaleInvoices on line.SaleInvoiceId equals sale.DocumentId
               join product in db.Products on line.ProductId equals product.Id
               where sale.DocumentId == id && sale.Document.OrganizationId == organizationId && product.OrganizationId == organizationId
               select new SaleProductResponse(product.Id, product.Name, product.Code, product.ItemType)).Distinct().ToListAsync(ct);
    public async Task<IReadOnlyList<SaleBatchResponse>> GetSaleBatchesAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await (from line in db.SaleInvoiceLines join sale in db.SaleInvoices on line.SaleInvoiceId equals sale.DocumentId
               join batch in db.Batches on line.BatchId equals batch.Id
               join sourceLine in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals sourceLine.Id
               join source in db.PurchaseInvoices on sourceLine.PurchaseInvoiceId equals source.DocumentId
               where sale.DocumentId == id && sale.Document.OrganizationId == organizationId && source.Document.OrganizationId == organizationId
               select new SaleBatchResponse(batch.Id, batch.BatchNumber, source.Document.DocumentDate)).Distinct().ToListAsync(ct);
    public async Task<IReadOnlyList<SalePriceOption>> GetSalePriceOptionsAsync(Guid organizationId, Guid productId, Guid? priceTypeId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == productId && x.OrganizationId == organizationId, ct);
        if (product.ItemType == ItemType.SERVICE)
        {
            if (priceTypeId is null) return [new(null, 0m)];
            return await db.ProductPrices.AsNoTracking().Where(x => x.ProductId == productId && x.PriceTypeId == priceTypeId)
                .Select(x => new SalePriceOption(null, x.UnitPrice)).ToListAsync(ct);
        }
        var batches = from batch in db.Batches join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
                      join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
                      where line.ProductId == productId && purchase.Document.OrganizationId == organizationId select batch;
        return priceTypeId is null ? await batches.Select(batch => new SalePriceOption(batch.Id, 0m)).ToListAsync(ct)
            : await (from batch in batches join price in db.BatchPrices on batch.Id equals price.BatchId
                     where price.PriceTypeId == priceTypeId select new SalePriceOption(batch.Id, price.UnitPrice)).ToListAsync(ct);
    }
    public async Task<SaleAmounts> GetSaleAmountsAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SaleAmountsRow>($"""
            SELECT round(COALESCE(SUM(l."Quantity" * l."UnitPrice"), 0) * (1 - s."DiscountPercent" / 100::numeric), 4) AS "NetSale",
                   round(COALESCE(SUM(CASE WHEN p."ItemType" = 'GOODS' THEN l."Quantity" * cost."UnitCostUah" ELSE 0 END), 0), 4) AS "CurrentCost"
            FROM sale_invoices s JOIN documents d ON d."Id" = s."DocumentId"
            LEFT JOIN sale_invoice_lines l ON l."SaleInvoiceId" = s."DocumentId" AND l."RemovedAt" IS NULL
            LEFT JOIN products p ON p."Id" = l."ProductId" AND p."OrganizationId" = d."OrganizationId"
            LEFT JOIN v_batch_cost cost ON cost."BatchId" = l."BatchId"
            WHERE s."DocumentId" = {id} AND d."OrganizationId" = {organizationId} AND d."DeletedAt" IS NULL
            GROUP BY s."DocumentId", s."DiscountPercent"
            """).SingleAsync(ct);
        return new(row.NetSale, row.CurrentCost);
    }
}
internal sealed class SaleAmountsRow
{
    public decimal NetSale { get; set; }
    public decimal CurrentCost { get; set; }
}
