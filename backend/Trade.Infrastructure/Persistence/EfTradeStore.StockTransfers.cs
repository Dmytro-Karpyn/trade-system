using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<StockTransfer?> FindStockTransferAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var rows = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = 'STOCK_TRANSFER' AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (rows.Count == 0) return null;
        }
        var query = db.StockTransfers.Include(x => x.Document).Include(x => x.Lines).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<StockTransfer>> ListStockTransfersAsync(Guid organizationId, StockTransferFilter filter, CancellationToken ct)
    {
        var query = db.StockTransfers.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.FromLocationId is { } fromLocation) query = query.Where(x => x.FromLocationId == fromLocation);
        if (filter.ToLocationId is { } toLocation) query = query.Where(x => x.ToLocationId == toLocation);
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        return await query.OrderBy(x => x.Document.DocumentDate).ThenBy(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);
    }

    public void AddStockTransfer(StockTransfer transfer) => db.StockTransfers.Add(transfer);
}
