using Microsoft.EntityFrameworkCore;
using Trade.Domain.Documents;
using Trade.Domain.Returns;
using Trade.Domain.Sales;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<SaleInvoice?> LockReturnSourceAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var rows = await db.Documents.FromSql($"""
            SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                AND "DocumentType" = 'SALE_INVOICE' AND "DeletedAt" IS NULL FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        return rows.Count == 0 ? null : await db.SaleInvoices.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DocumentId == id, ct);
    }

    public async Task<bool> IsKnownStorageLocationAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await db.PurchaseInvoices.AnyAsync(x => x.StorageLocationId == id && x.Document.OrganizationId == organizationId, ct)
        || await db.SaleInvoices.AnyAsync(x => x.StorageLocationId == id && x.Document.OrganizationId == organizationId, ct);

    public Task<bool> HasCustomerReturnsAsync(Guid saleId, CancellationToken ct) =>
        db.CustomerReturns.AnyAsync(x => x.SourceSaleInvoiceId == saleId && x.Document.DeletedAt == null, ct);

    public async Task<IReadOnlyList<ReturnedLineTotals>> GetReturnedTotalsAsync(Guid sourceSaleId, CancellationToken ct) =>
        await (from line in db.CustomerReturnLines
               join header in db.CustomerReturns on line.CustomerReturnId equals header.DocumentId
               where header.SourceSaleInvoiceId == sourceSaleId && header.Document.Status == DocumentStatus.POSTED
                   && header.Document.DeletedAt == null
               group line by line.SourceSaleLineId into grouped
               select new ReturnedLineTotals(grouped.Key, grouped.Sum(x => x.Quantity), grouped.Sum(x => x.RefundAmount))).ToListAsync(ct);

    public async Task<CustomerReturn?> FindCustomerReturnAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var rows = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = 'CUSTOMER_RETURN' AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (rows.Count == 0) return null;
        }
        var query = db.CustomerReturns.Include(x => x.Document).Include(x => x.Lines).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<CustomerReturn>> ListCustomerReturnsAsync(Guid organizationId, CancellationToken ct) =>
        await db.CustomerReturns.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null)
            .OrderBy(x => x.Document.DocumentDate).ThenBy(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);

    public void AddCustomerReturn(CustomerReturn result) => db.CustomerReturns.Add(result);
}
