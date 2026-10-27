using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Pricing;

namespace Trade.Domain.Sales;

public sealed class SaleInvoiceLine
{
    private SaleInvoiceLine() { }

    public Guid Id { get; private set; }
    public Guid SaleInvoiceId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BatchId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal RecommendedUnitPrice { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    internal static SaleInvoiceLine Create(Guid saleId, SaleLineDetails input, ItemType type, Guid? batchProductId,
        SalePricePermissions permissions)
    {
        var line = new SaleInvoiceLine { Id = Guid.CreateVersion7(), SaleInvoiceId = saleId };
        line.Update(input, type, batchProductId, permissions);
        return line;
    }

    internal void Update(SaleLineDetails input, ItemType type, Guid? batchProductId, SalePricePermissions permissions)
    {
        Guard.RequiredId(input.ProductId, nameof(ProductId));
        Guard.Against(input.LineNo <= 0, "LineNo must be positive.");
        Guard.Against(input.Quantity <= 0, "Quantity must be positive.");
        Guard.Against(input.UnitPrice < 0 || input.RecommendedUnitPrice < 0, "Prices must be nonnegative.");
        Guard.Numeric(input.Quantity, 4, 10_000_000_000_000_000m, nameof(Quantity));
        Guard.Numeric(input.UnitPrice, 4, 10_000_000_000_000_000m, nameof(UnitPrice));
        Guard.Numeric(input.RecommendedUnitPrice, 4, 10_000_000_000_000_000m, nameof(RecommendedUnitPrice));
        PriceRules.RequireAllowed(input.RecommendedUnitPrice, input.UnitPrice, permissions);
        LineNo = input.LineNo;
        ProductId = input.ProductId;
        BatchId = input.BatchId;
        Quantity = input.Quantity;
        RecommendedUnitPrice = input.RecommendedUnitPrice;
        UnitPrice = input.UnitPrice;
        RemovedAt = null;
        ValidateProduct(type, batchProductId);
    }

    public void ValidateProduct(ItemType type, Guid? batchProductId)
    {
        Guard.Against(type == ItemType.SERVICE && BatchId is not null, "SERVICE must not specify BatchId.");
        Guard.Against(type == ItemType.GOODS && (BatchId is null || BatchId == Guid.Empty), "GOODS requires BatchId.");
        Guard.Against(type == ItemType.GOODS && batchProductId != ProductId,
            "Batch must belong to this Product and organization.");
    }

    internal void Remove(DateTimeOffset now) => RemovedAt ??= UtcTimestamp.Normalize(now);
}
