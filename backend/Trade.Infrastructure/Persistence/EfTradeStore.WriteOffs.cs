using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<WriteOffReason?> FindWriteOffReasonAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        var query = forUpdate ? db.WriteOffReasons.FromSql($"""
            SELECT * FROM write_off_reasons WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR UPDATE
            """) : db.WriteOffReasons.FromSql($"""
            SELECT * FROM write_off_reasons WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR SHARE
            """).AsNoTracking();
        return (await query.ToListAsync(ct)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<WriteOffReason>> ListWriteOffReasonsAsync(Guid organizationId, CancellationToken ct) =>
        await db.WriteOffReasons.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<WriteOff?> FindWriteOffAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var rows = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = 'WRITE_OFF' AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (rows.Count == 0) return null;
        }
        var query = db.WriteOffs.Include(x => x.Document).Include(x => x.Lines).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<WriteOff>> ListWriteOffsAsync(Guid organizationId, WriteOffFilter filter, CancellationToken ct)
    {
        var query = db.WriteOffs.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.StorageLocationId is { } locationId) query = query.Where(x => x.StorageLocationId == locationId);
        if (filter.WriteOffReasonId is { } reasonId) query = query.Where(x => x.WriteOffReasonId == reasonId);
        if (filter.ExpenseCategoryId is { } categoryId) query = query.Where(x => db.WriteOffReasons.Any(r =>
            r.Id == x.WriteOffReasonId && r.OrganizationId == organizationId && r.ExpenseCategoryId == categoryId));
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        return await query.OrderBy(x => x.Document.DocumentDate).ThenBy(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);
    }

    public void AddWriteOffReason(WriteOffReason reason) => db.WriteOffReasons.Add(reason);
    public void AddWriteOff(WriteOff writeOff) => db.WriteOffs.Add(writeOff);
}
