using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;

namespace Trade.Domain.Expenses;

public sealed record ExpenseDetails(Guid CounterpartyId, Guid ExpenseCategoryId, Guid CurrencyId,
    decimal Amount, decimal ExchangeRate, Guid? PurchaseInvoiceId);

public sealed class Expense
{
    private Expense() { }

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid CounterpartyId { get; private set; }
    public Guid ExpenseCategoryId { get; private set; }
    public Guid CurrencyId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public Guid? PurchaseInvoiceId { get; private set; }

    public static Expense Create(Document document, ExpenseDetails details)
    {
        Guard.Against(document.DocumentType != DocumentType.EXPENSE, "An expense requires EXPENSE DocumentType.");
        var expense = new Expense { DocumentId = document.Id, Document = document };
        expense.Update(details);
        return expense;
    }

    public void Update(ExpenseDetails details)
    {
        Document.RequireDraft();
        Guard.RequiredId(details.CounterpartyId, nameof(CounterpartyId));
        Guard.RequiredId(details.ExpenseCategoryId, nameof(ExpenseCategoryId));
        Guard.RequiredId(details.CurrencyId, nameof(CurrencyId));
        Guard.Against(details.Amount <= 0, "Amount must be positive.");
        Guard.Numeric(details.Amount, 4, 10_000_000_000_000_000m, nameof(Amount));
        Guard.Against(details.ExchangeRate <= 0, "ExchangeRate must be positive.");
        Guard.Numeric(details.ExchangeRate, 6, 100_000_000_000_000m, nameof(ExchangeRate));
        Guard.Against(details.PurchaseInvoiceId == Guid.Empty, "PurchaseInvoiceId must be null or a nonempty ID.");
        CounterpartyId = details.CounterpartyId;
        ExpenseCategoryId = details.ExpenseCategoryId;
        CurrencyId = details.CurrencyId;
        Amount = details.Amount;
        ExchangeRate = details.ExchangeRate;
        PurchaseInvoiceId = details.PurchaseInvoiceId;
    }

    public static void RequireAllocatablePurchase(PurchaseInvoice purchase)
    {
        Guard.Against(purchase.Document.DocumentType != DocumentType.PURCHASE_INVOICE
            || purchase.Document.Status != DocumentStatus.POSTED || purchase.Document.DeletedAt is not null,
            "A direct expense requires a nondeleted POSTED PURCHASE_INVOICE.");
        // Positive quantities/rate and nonnegative prices are already purchase invariants.
        // Check exact value, without rounding a small positive purchase to zero.
        if (purchase.DiscountPercent == 100 || !purchase.Lines.Any(x => x.RemovedAt is null && x.UnitPriceCurrency > 0))
            throw new InvalidPurchaseExpenseAllocationException();
    }
}

public sealed class InvalidPurchaseExpenseAllocationException() : Exception("INVALID_PURCHASE_EXPENSE_ALLOCATION");
