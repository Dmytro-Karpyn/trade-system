using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Expenses;
using Trade.Domain.Purchasing;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task LockExpenseCategoryTreeAsync(Guid organizationId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended({organizationId.ToString()}, 72831))
            """, ct);

    public Task<ExpenseCategory?> FindExpenseCategoryAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        db.ExpenseCategories.SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<ExpenseCategory>> ListExpenseCategoriesAsync(Guid organizationId, CancellationToken ct) =>
        await db.ExpenseCategories.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task<PurchaseInvoice?> LockExpensePurchaseAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var documents = await db.Documents.FromSql($"""
            SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                AND "DeletedAt" IS NULL FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        if (documents.Count == 0) return null;
        return await db.PurchaseInvoices.AsNoTracking().Include(x => x.Document).Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.DocumentId == id, ct);
    }

    public Task<bool> HasPurchaseExpensesAsync(Guid purchaseId, CancellationToken ct) =>
        db.Expenses.AnyAsync(x => x.PurchaseInvoiceId == purchaseId && x.Document.DeletedAt == null, ct);

    public async Task<Expense?> FindExpenseAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
        {
            var documents = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DocumentType" = 'EXPENSE' AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(ct);
            if (documents.Count == 0) return null;
        }
        var query = db.Expenses.Include(x => x.Document).AsQueryable();
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DocumentType == DocumentType.EXPENSE && x.Document.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<Expense>> ListExpensesAsync(Guid organizationId, ExpenseFilter filter, CancellationToken ct)
    {
        var query = db.Expenses.AsNoTracking().Include(x => x.Document)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.CounterpartyId is { } partyId) query = query.Where(x => x.CounterpartyId == partyId);
        if (filter.CategoryId is { } categoryId) query = query.Where(x => x.ExpenseCategoryId == categoryId);
        if (filter.PurchaseInvoiceId is { } purchaseId) query = query.Where(x => x.PurchaseInvoiceId == purchaseId);
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        if (filter.CurrencyId is { } currencyId) query = query.Where(x => x.CurrencyId == currencyId);
        return await query.OrderBy(x => x.Document.DocumentDate).ThenBy(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);
    }

    public void AddExpenseCategory(ExpenseCategory category) => db.ExpenseCategories.Add(category);
    public void AddExpense(Expense expense) => db.Expenses.Add(expense);
}
