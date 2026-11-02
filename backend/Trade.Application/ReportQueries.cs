namespace Trade.Application;

public sealed record ReportFilter(DateOnly? From = null, DateOnly? To = null, Guid? ProductId = null, Guid? CategoryId = null,
    Guid? CounterpartyId = null, Guid? CreatedBy = null, Guid? BatchId = null, Guid? PriceTypeId = null,
    Guid? StorageLocationId = null, Guid? CurrencyId = null, Guid? AccountId = null, string? OperationType = null,
    bool? IsPurchaseExpense = null, bool WithStock = false, bool WithReservations = false, string? View = null,
    Guid? RetailPriceTypeId = null, Guid? WholesalePriceTypeId = null);
public sealed record ReportAccess(bool Reports, bool Profit, bool Cost, bool Money, bool Settlement, bool Sales,
    bool Purchases, bool Expenses, bool Stock, bool Transfers, bool WriteOffs);
public sealed record ReportColumn(string Key, string Label, string Format = "text");
public sealed record ReportMetric(string Key, string Label, decimal Value, Guid? CurrencyId = null, string Format = "number");
public sealed record ReportTable(string Key, string Title, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows, IReadOnlyList<ReportMetric> Metrics, IReadOnlyList<string> Notes);
public sealed record ReportOption(Guid Id, string Name);
public sealed record ReportLookups(IReadOnlyList<ReportOption> Products, IReadOnlyList<ReportOption> Categories,
    IReadOnlyList<ReportOption> Counterparties, IReadOnlyList<ReportOption> Managers, IReadOnlyList<ReportOption> Batches,
    IReadOnlyList<ReportOption> PriceTypes, IReadOnlyList<ReportOption> Locations, IReadOnlyList<ReportOption> Accounts,
    IReadOnlyList<ReportOption> ExpenseCategories, IReadOnlyList<Guid> CurrencyIds);
public sealed record ReportTrend(DateOnly Date, decimal Sales, decimal Returns, decimal NetSales, decimal? GrossProfit);
public sealed record DashboardSummary(DateOnly From, DateOnly To, IReadOnlyList<ReportMetric> Kpis,
    IReadOnlyList<ReportTrend> SalesTrend, IReadOnlyList<ReportTrend> ProfitTrend, IReadOnlyList<ReportTable> Blocks,
    IReadOnlyList<string> Notes);
public interface IReportQueries
{
    Task<ReportLookups> LookupsAsync(Guid organizationId, ReportAccess access, CancellationToken ct);
    Task<DashboardSummary> DashboardAsync(Guid organizationId, ReportFilter filter, ReportAccess access, CancellationToken ct);
    Task<ReportTable> ReportAsync(Guid organizationId, string kind, ReportFilter filter, ReportAccess access, CancellationToken ct);
}
