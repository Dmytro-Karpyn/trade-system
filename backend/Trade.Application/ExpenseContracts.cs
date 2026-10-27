using Trade.Domain.Documents;
using Trade.Domain.Expenses;

namespace Trade.Application;

public sealed record ExpenseCategoryRequest(string Name, Guid? ParentId = null, bool Active = true)
{
    public ExpenseCategoryDetails ToDetails() => new(ParentId, Name, Active);
}

public sealed record ExpenseCategoryResponse(Guid Id, Guid OrganizationId, Guid? ParentId, string Name, bool Active);

public sealed record ExpenseRequest(DateOnly DocumentDate, Guid CounterpartyId, Guid ExpenseCategoryId,
    Guid CurrencyId, decimal Amount, decimal ExchangeRate, Guid? PurchaseInvoiceId = null,
    string? Comment = null, string? InternalNote = null)
{
    public ExpenseDetails ToDetails() => new(CounterpartyId, ExpenseCategoryId, CurrencyId, Amount, ExchangeRate, PurchaseInvoiceId);
}

public sealed record UpdateExpenseRequest(long Version, DateOnly DocumentDate, Guid CounterpartyId, Guid ExpenseCategoryId,
    Guid CurrencyId, decimal Amount, decimal ExchangeRate, Guid? PurchaseInvoiceId = null,
    string? Comment = null, string? InternalNote = null)
{
    public ExpenseDetails ToDetails() => new(CounterpartyId, ExpenseCategoryId, CurrencyId, Amount, ExchangeRate, PurchaseInvoiceId);
}

public sealed record ExpenseResponse(DocumentResponse Document, Guid CounterpartyId, Guid ExpenseCategoryId,
    Guid CurrencyId, decimal Amount, decimal ExchangeRate, Guid? PurchaseInvoiceId);

public sealed record ExpenseFilter(DateOnly? From = null, DateOnly? To = null, Guid? CounterpartyId = null,
    Guid? CategoryId = null, Guid? PurchaseInvoiceId = null, DocumentStatus? Status = null, Guid? CurrencyId = null);

internal static class ExpenseResponses
{
    public static ExpenseCategoryResponse ToResponse(this ExpenseCategory category) =>
        new(category.Id, category.OrganizationId, category.ParentId, category.Name, category.Active);

    public static ExpenseResponse ToResponse(this Expense expense)
    {
        var d = expense.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), expense.CounterpartyId, expense.ExpenseCategoryId,
            expense.CurrencyId, expense.Amount, expense.ExchangeRate, expense.PurchaseInvoiceId);
    }
}
