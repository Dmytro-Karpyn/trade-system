using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Money;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<MoneyAccount?> FindMoneyAccountAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
            return (await db.MoneyAccounts.FromSql($"""
                SELECT * FROM money_accounts WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR UPDATE
                """).ToListAsync(ct)).SingleOrDefault();
        return await db.MoneyAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);
    }

    public async Task<IReadOnlyList<MoneyAccount>> ListMoneyAccountsAsync(Guid organizationId, CancellationToken ct) =>
        await db.MoneyAccounts.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<MoneyAccount>> LockMoneyAccountsAsync(Guid organizationId, Guid[] ids, CancellationToken ct) =>
        ids.Length == 0 ? [] : await db.MoneyAccounts.FromSql($"""
            SELECT * FROM money_accounts WHERE "OrganizationId" = {organizationId} AND "Id" = ANY ({ids}) ORDER BY "Id" FOR SHARE
            """).ToListAsync(ct);

    public async Task<bool> HasMoneyAccountReferencesAsync(Guid id, CancellationToken ct) =>
        await db.MoneyDocuments.AnyAsync(x => x.SourceAccountId == id || x.DestinationAccountId == id, ct)
            || await db.MoneyMovements.AnyAsync(x => x.MoneyAccountId == id, ct);

    public async Task<MoneyDocument?> FindMoneyDocumentAsync(Guid organizationId, Guid id, DocumentType type, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var typeName = type.ToString();
            var documents = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = {typeName} AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (documents.Count == 0) return null;
        }
        var query = db.MoneyDocuments.Include(x => x.Document).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DocumentType == type && x.Document.DeletedAt == null, ct);
    }

    public async Task DeleteMoneyMovementsAsync(Guid documentId, CancellationToken ct) =>
        await db.MoneyMovements.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<MoneyBalanceResponse>> GetMoneyBalancesAsync(Guid organizationId, CancellationToken ct) =>
        await (from balance in db.MoneyAccountBalances
               join account in db.MoneyAccounts on balance.MoneyAccountId equals account.Id
               where account.OrganizationId == organizationId
               orderby balance.MoneyAccountId
               select new MoneyBalanceResponse(balance.MoneyAccountId, balance.CurrencyId, balance.Balance)).ToListAsync(ct);

    public void AddMoneyAccount(MoneyAccount account) => db.MoneyAccounts.Add(account);
    public void AddMoneyDocument(MoneyDocument document) => db.MoneyDocuments.Add(document);
    public void AddMoneyMovement(MoneyMovement movement) => db.MoneyMovements.Add(movement);
}
