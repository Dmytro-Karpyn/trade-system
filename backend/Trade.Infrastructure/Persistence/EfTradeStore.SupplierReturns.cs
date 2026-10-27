using Microsoft.EntityFrameworkCore;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Domain.Returns;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<PurchaseInvoice?> LockSupplierReturnSourceAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var rows = await db.Documents.FromSql($"""
            SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                AND "DeletedAt" IS NULL FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        return rows.Count == 0 ? null : await db.PurchaseInvoices.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DocumentId == id, ct);
    }

    public Task<bool> HasSupplierReturnsAsync(Guid purchaseId, CancellationToken ct) =>
        db.SupplierReturns.AnyAsync(x => x.SourcePurchaseInvoiceId == purchaseId && x.Document.DeletedAt == null, ct);

    public async Task<IReadOnlyList<SupplierReturnedQuantity>> GetSupplierReturnedQuantitiesAsync(Guid sourcePurchaseId, CancellationToken ct) =>
        await (from line in db.SupplierReturnLines
               join header in db.SupplierReturns on line.SupplierReturnId equals header.DocumentId
               where header.SourcePurchaseInvoiceId == sourcePurchaseId && header.Document.Status == DocumentStatus.POSTED
                   && header.Document.DeletedAt == null
               group line by line.SourcePurchaseLineId into grouped
               select new SupplierReturnedQuantity(grouped.Key, grouped.Sum(x => x.Quantity))).ToListAsync(ct);

    public async Task<SupplierReturn?> FindSupplierReturnAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var rows = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = 'SUPPLIER_RETURN' AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (rows.Count == 0) return null;
        }
        var query = db.SupplierReturns.Include(x => x.Document).Include(x => x.Lines).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<SupplierReturn>> ListSupplierReturnsAsync(Guid organizationId, CancellationToken ct) =>
        await db.SupplierReturns.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null)
            .OrderBy(x => x.Document.DocumentDate).ThenBy(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);

    public void AddSupplierReturn(SupplierReturn result) => db.SupplierReturns.Add(result);
}
