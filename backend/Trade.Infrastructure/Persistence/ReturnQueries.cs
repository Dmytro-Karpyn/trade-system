using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Returns;
using Trade.Domain.Settlements;

namespace Trade.Infrastructure.Persistence;

internal sealed class ReturnQueries(TradeDbContext db) : IReturnQueries
{
    public async Task<ReturnLookups> LookupsAsync(Guid organizationId, bool supplier, CancellationToken ct)
    {
        var sources = supplier
            ? await db.PurchaseInvoices.AsNoTracking().Where(x => x.Document.OrganizationId == organizationId
                && x.Document.Status == DocumentStatus.POSTED && x.Document.DeletedAt == null && x.Document.DocumentType == DocumentType.PURCHASE_INVOICE)
                .OrderByDescending(x => x.Document.DocumentDate).ThenBy(x => x.DocumentId)
                .Select(x => new ReturnSourceOption(x.DocumentId, x.Document.Number, x.Document.DocumentDate, x.CounterpartyId!.Value,
                    db.Counterparties.Where(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId).Select(p => p.Name).First(),
                    x.StorageLocationId, x.CurrencyId, x.ExchangeRate, x.DiscountPercent)).ToListAsync(ct)
            : await db.SaleInvoices.AsNoTracking().Where(x => x.Document.OrganizationId == organizationId
                && x.Document.Status == DocumentStatus.POSTED && x.Document.DeletedAt == null)
                .OrderByDescending(x => x.Document.DocumentDate).ThenBy(x => x.DocumentId)
                .Select(x => new ReturnSourceOption(x.DocumentId, x.Document.Number, x.Document.DocumentDate, x.CounterpartyId,
                    db.Counterparties.Where(p => p.Id == x.CounterpartyId && p.OrganizationId == organizationId).Select(p => p.Name).First(),
                    x.StorageLocationId, SettlementCurrencies.Uah, 1m, x.DiscountPercent)).ToListAsync(ct);
        // Return writers still accept only locations already used by this organization, including legacy external IDs.
        var used = await db.PurchaseInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)
            .Union(db.SaleInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)).ToListAsync(ct);
        var named = await db.StorageLocations.AsNoTracking().Where(x => x.OrganizationId == organizationId && used.Contains(x.Id))
            .Select(x => new StorageLocationResponse(x.Id, x.Name,x.Active)).ToListAsync(ct);
        var locations = named.Concat(used.Where(id => named.All(x => x.Id != id)).Order().Select(id => new StorageLocationResponse(id, null))).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name)
            .Select(x => new ReturnProductOption(x.Id, x.Name)).ToListAsync(ct);
        var batches = await (from b in db.Batches join l in db.PurchaseInvoiceLines on b.PurchaseInvoiceLineId equals l.Id
            join p in db.PurchaseInvoices on l.PurchaseInvoiceId equals p.DocumentId where p.Document.OrganizationId == organizationId
            select new ReturnBatchOption(b.Id, b.BatchNumber)).ToListAsync(ct);
        return new(sources, locations, products, batches);
    }

    public async Task<IReadOnlyList<ReturnListItem>> ListAsync(Guid organizationId, bool supplier, ReturnFilter filter, CancellationToken ct)
    {
        // Projections only: no persisted totals and no dependency on current cost/direct expenses.
        var query = supplier
            ? from r in db.SupplierReturns.AsNoTracking() join p in db.PurchaseInvoices on r.SourcePurchaseInvoiceId equals p.DocumentId
              join c in db.Counterparties on p.CounterpartyId equals c.Id
              where r.Document.OrganizationId == organizationId && p.Document.OrganizationId == organizationId
                  && c.OrganizationId == organizationId && r.Document.DeletedAt == null
              select new { Id = r.DocumentId, Number = r.Document.Number, DocumentDate = r.Document.DocumentDate, CounterpartyId = c.Id,
                  CounterpartyName = c.Name, SourceId = p.DocumentId, SourceNumber = p.Document.Number, p.CurrencyId,
                  TotalRefund = Math.Round(r.Lines.Sum(l => l.Quantity * db.PurchaseInvoiceLines.Where(s => s.Id == l.SourcePurchaseLineId)
                      .Select(s => s.UnitPriceCurrency).First()) * (1m - p.DiscountPercent / 100m), 4), Status = r.Document.Status }
            : from r in db.CustomerReturns.AsNoTracking() join s in db.SaleInvoices on r.SourceSaleInvoiceId equals s.DocumentId
              join c in db.Counterparties on s.CounterpartyId equals c.Id
              where r.Document.OrganizationId == organizationId && s.Document.OrganizationId == organizationId
                  && c.OrganizationId == organizationId && r.Document.DeletedAt == null
              select new { Id = r.DocumentId, Number = r.Document.Number, DocumentDate = r.Document.DocumentDate, CounterpartyId = c.Id,
                  CounterpartyName = c.Name, SourceId = s.DocumentId, SourceNumber = s.Document.Number, CurrencyId = SettlementCurrencies.Uah,
                  TotalRefund = r.Lines.Sum(l => l.RefundAmount), Status = r.Document.Status };
        if (filter.From is { } from) query = query.Where(x => x.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.DocumentDate <= to);
        if (filter.CounterpartyId is { } party) query = query.Where(x => x.CounterpartyId == party);
        if (filter.Status is { } status) query = query.Where(x => x.Status == status);
        if (filter.SourceId is { } source) query = query.Where(x => x.SourceId == source);
        return await query.OrderByDescending(x => x.DocumentDate).ThenBy(x => x.Id)
            .Select(x => new ReturnListItem(x.Id, x.Number, x.DocumentDate, x.CounterpartyId, x.CounterpartyName,
                x.SourceId, x.SourceNumber, x.CurrencyId, x.TotalRefund, x.Status)).ToListAsync(ct);
    }

    public async Task<ReturnPreview> SupplierRefundAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var result = await db.SupplierReturns.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null, ct)
            ?? throw new NotFoundException("SupplierReturn not found.");
        var source = await db.PurchaseInvoices.AsNoTracking().Include(x => x.Lines)
            .SingleAsync(x => x.DocumentId == result.SourcePurchaseInvoiceId && x.Document.OrganizationId == organizationId, ct);
        return SupplierReturnService.RefundPreview(result, source);
    }

    public async Task<ReturnPreview> CustomerRefundAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var result = await db.CustomerReturns.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null, ct)
            ?? throw new NotFoundException("CustomerReturn not found.");
        return new(ReturnAmounts.Customer(result.Lines.Select(x => x.RefundAmount)),
            result.Lines.Select(x => new ReturnPreviewLine(x.SourceSaleLineId, x.RefundAmount)).ToArray());
    }
}
