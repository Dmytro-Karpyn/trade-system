using System.Data;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

internal sealed class ExpenseQueries(TradeDbContext db, IFinanceQueries finance) : IExpenseQueries
{
    public async Task<ExpenseLookups> LookupsAsync(Guid organizationId, CancellationToken ct)
    {
        var parties = await db.Counterparties.AsNoTracking().Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Name)
            .Select(p => new MoneyPartyOption(p.Id, p.Name, p.Active)).ToListAsync(ct);
        var categories = await db.ExpenseCategories.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name)
            .Select(x => new ExpenseCategoryResponse(x.Id, x.OrganizationId, x.ParentId, x.Name, x.Active)).ToListAsync(ct);
        var purchases = await db.PurchaseInvoices.AsNoTracking().Where(p => p.Document.OrganizationId == organizationId
            && p.Document.DocumentType == DocumentType.PURCHASE_INVOICE && p.Document.Status == DocumentStatus.POSTED && p.Document.DeletedAt == null)
            .OrderByDescending(p => p.Document.DocumentDate).ThenBy(p => p.DocumentId)
            .Select(p => new ExpensePurchaseOption(p.DocumentId, p.Document.Number, p.Document.DocumentDate,
                db.Counterparties.Where(c => c.Id == p.CounterpartyId && c.OrganizationId == organizationId).Select(c => c.Name).FirstOrDefault(),
                p.CurrencyId, p.Lines.Where(l => l.RemovedAt == null).Sum(l => l.Quantity * l.UnitPriceCurrency) * (1m - p.DiscountPercent / 100m),
                p.DiscountPercent < 100m && p.Lines.Any(l => l.RemovedAt == null && l.UnitPriceCurrency > 0m))).ToListAsync(ct);
        return new(parties, await finance.CurrenciesAsync(organizationId, ct), categories, purchases);
    }

    public async Task<ExpenseCostImpact> CostImpactAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        // Keep totals and all batch costs in the same snapshot while expenses can be posted concurrently.
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var expense = await db.Expenses.AsNoTracking().Where(x => x.DocumentId == id && x.Document.OrganizationId == organizationId && x.Document.DeletedAt == null)
            .Select(x => new { x.PurchaseInvoiceId }).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Expense not found.");
        if (expense.PurchaseInvoiceId is not { } purchaseId) throw new NotFoundException("Expense is not linked to a purchase.");
        var source = await db.PurchaseInvoices.AsNoTracking().Where(x => x.DocumentId == purchaseId && x.Document.OrganizationId == organizationId)
            .Select(x => new { x.Document.Number, x.Document.DocumentDate, x.CurrencyId, x.ExchangeRate,
                Total = x.Lines.Where(l => l.RemovedAt == null).Sum(l => l.Quantity * l.UnitPriceCurrency) * (1m - x.DiscountPercent / 100m) })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Purchase not found.");
        var direct = await db.Expenses.Where(x => x.PurchaseInvoiceId == purchaseId && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null && x.Document.Status == DocumentStatus.POSTED).SumAsync(x => x.Amount * x.ExchangeRate, ct);
        var batches = await (from batch in db.Batches join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
            join product in db.Products on line.ProductId equals product.Id join cost in db.BatchCosts on batch.Id equals cost.BatchId
            where line.PurchaseInvoiceId == purchaseId && line.RemovedAt == null && product.OrganizationId == organizationId
            orderby line.LineNo, batch.Id select new ExpenseCostBatch(batch.Id, batch.BatchNumber, product.Name, line.Quantity, cost.UnitCostUah)).ToListAsync(ct);
        await snapshot.CommitAsync(ct);
        return new(purchaseId, source.Number, source.DocumentDate, source.CurrencyId, source.Total,
            source.Total * source.ExchangeRate, direct, batches);
    }
}
