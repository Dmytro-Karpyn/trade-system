namespace Trade.Application;

// Read models only. Stock, prices, cost and running balances are never persisted here.
public sealed record StockFilter(string? Search = null, Guid? CategoryId = null, Guid? StorageLocationId = null,
    bool WithStock = false, bool WithReservations = false, bool ByBatch = false, Guid? ProductId = null,
    Guid? RetailPriceTypeId = null, Guid? WholesalePriceTypeId = null);
public sealed record StockPriceRange(decimal Min, decimal Max);
public sealed record StockProduct(Guid Id, string Name, string Code, string? Unit, string? Category, bool Active, Guid? CategoryId = null);
public sealed record StockRow(Guid ProductId, string ProductName, string Code, string? Unit, string? Category,
    Guid StorageLocationId, string? LocationName, Guid? BatchId, string? BatchNumber, DateOnly? PurchaseDocumentDate,
    string? Supplier, decimal PhysicalQuantity, decimal ReservedQuantity, decimal AvailableQuantity,
    StockPriceRange? RetailPrice, StockPriceRange? WholesalePrice, StockPriceRange? CurrentUnitCost, Guid? CategoryId = null);
public sealed record StockCategory(Guid Id, string? Name);
public sealed record StockPriceType(Guid Id, string Name, bool Active);
public sealed record StockLookups(IReadOnlyList<StorageLocationResponse> Locations,
    IReadOnlyList<StorageLocationResponse> OperationLocations, IReadOnlyList<StockPriceType> PriceTypes, IReadOnlyList<StockCategory> Categories);
public sealed record StockProductDetail(StockProduct Product, IReadOnlyList<StockRow> Balances, IReadOnlyList<StockRow> Batches);
public sealed record StockBatchPrice(Guid PriceTypeId, string PriceTypeName, decimal UnitPrice);
public sealed record StockPurchaseValuation(decimal UnitPriceCurrency, decimal ExchangeRate, decimal DiscountPercent, decimal UnitCostUah);
public sealed record StockBatchDetail(Guid BatchId, string BatchNumber, StockProduct Product, Guid PurchaseDocumentId,
    string? PurchaseNumber, string PurchaseDocumentType, DateOnly PurchaseDocumentDate, string? Supplier, Guid CurrencyId,
    StockPurchaseValuation? Valuation, IReadOnlyList<StockBatchPrice> Prices, IReadOnlyList<StockRow> Balances);
public sealed class StockMovementEntry
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string DocumentType { get; set; } = "";
    public DateOnly DocumentDate { get; set; }
    public Guid StorageLocationId { get; set; }
    public string? LocationName { get; set; }
    public decimal Quantity { get; set; }
    public decimal PhysicalBalanceAfter { get; set; }
}
public sealed class StockReservationEntry
{
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string DocumentType { get; set; } = "";
    public DateOnly DocumentDate { get; set; }
    public Guid BatchId { get; set; }
    public Guid StorageLocationId { get; set; }
    public decimal Quantity { get; set; }
    public string? CreatedByName { get; set; }
}
public sealed record StockCandidate(StockRow Stock, decimal OwnReservedQuantity, decimal AvailableForDocument);
public sealed record StockWriteOffCost(decimal TotalCostUah);

public interface IStockQueries
{
    Task<StockLookups> LookupsAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<StockRow>> OverviewAsync(Guid organizationId, StockFilter filter, bool includeCost, CancellationToken ct);
    Task<StockProductDetail> ProductAsync(Guid organizationId, Guid id, Guid? retail, Guid? wholesale, bool includeCost, CancellationToken ct);
    Task<StockBatchDetail> BatchAsync(Guid organizationId, Guid id, bool includeCost, CancellationToken ct);
    Task<IReadOnlyList<StockMovementEntry>> MovementsAsync(Guid organizationId, Guid? productId, Guid? batchId, Guid? locationId, CancellationToken ct);
    Task<IReadOnlyList<StockReservationEntry>> ReservationsAsync(Guid organizationId, Guid? productId, Guid? batchId, Guid? locationId, CancellationToken ct);
    Task<IReadOnlyList<StockCandidate>> CandidatesAsync(Guid organizationId, Guid locationId, Guid? excludeDocumentId, CancellationToken ct);
    Task<IReadOnlyList<StockRow>> OperationLinesAsync(Guid organizationId, Guid id, bool transfer, CancellationToken ct);
    Task<StockWriteOffCost> WriteOffCostAsync(Guid organizationId, Guid id, CancellationToken ct);
}
