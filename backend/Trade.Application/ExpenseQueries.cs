namespace Trade.Application;

public sealed record ExpensePurchaseOption(Guid Id, string? Number, DateOnly DocumentDate, string? Supplier,
    Guid CurrencyId, decimal TotalCurrency, bool CanAllocate);
public sealed record ExpenseLookups(IReadOnlyList<MoneyPartyOption> Counterparties, IReadOnlyList<Guid> CurrencyIds,
    IReadOnlyList<ExpenseCategoryResponse> Categories, IReadOnlyList<ExpensePurchaseOption> Purchases);
public sealed record ExpenseCostBatch(Guid BatchId, string BatchNumber, string ProductName, decimal Quantity, decimal CurrentUnitCostUah);
public sealed record ExpenseCostImpact(Guid PurchaseInvoiceId, string? PurchaseNumber, DateOnly PurchaseDate, Guid CurrencyId,
    decimal PurchaseTotalCurrency, decimal PurchaseBaseValueUah, decimal PostedDirectExpensesUah, IReadOnlyList<ExpenseCostBatch> Batches);
public interface IExpenseQueries
{
    Task<ExpenseLookups> LookupsAsync(Guid organizationId, CancellationToken ct);
    Task<ExpenseCostImpact> CostImpactAsync(Guid organizationId, Guid id, CancellationToken ct);
}
