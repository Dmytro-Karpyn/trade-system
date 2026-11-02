using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
namespace Trade.Infrastructure.Persistence;

// Unmapped query projections: never DbSets, tables, snapshots or cached totals.
internal sealed class ReportSaleFact
{
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string DocumentType { get; set; } = "";
    public DateOnly Date { get; set; }
    public Guid ProductId { get; set; }
    public string Product { get; set; } = "";
    public Guid? BatchId { get; set; }
    public string? Batch { get; set; }
    public Guid CounterpartyId { get; set; }
    public string Customer { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal RecommendedPrice { get; set; }
    public decimal ActualPrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal NetAmount { get; set; }
    public string CostText { get; set; } = "0";
    [NotMapped] public decimal CurrentCost => decimal.Parse(CostText,CultureInfo.InvariantCulture);
    public bool IsReturn { get; set; }
    public bool Goods { get; set; }
}
internal sealed class ReportMoneyFact
{
    public Guid AccountId { get; set; }
    public string Account { get; set; } = "";
    public Guid CurrencyId { get; set; }
    public decimal Opening { get; set; }
    public decimal Receipts { get; set; }
    public decimal Payments { get; set; }
    public decimal TransfersIn { get; set; }
    public decimal TransfersOut { get; set; }
    public decimal Closing { get; set; }
}
internal sealed class ReportStockFact
{
    public int Products { get; set; }
    public decimal Physical { get; set; }
    public decimal Reserved { get; set; }
    public decimal Available { get; set; }
    public decimal? Valuation { get; set; }
}
internal sealed class ReportMovementFact
{
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string DocumentType { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Product { get; set; } = "";
    public string Batch { get; set; } = "";
    public string? Location { get; set; }
    public decimal Quantity { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? CostText { get; set; }
    [NotMapped] public decimal? CurrentCost => CostText is null ? null : decimal.Parse(CostText,CultureInfo.InvariantCulture);
}
internal sealed class ReportCostFact
{
    public Guid BatchId { get; set; }
    public string CostText { get; set; } = "0";
}
