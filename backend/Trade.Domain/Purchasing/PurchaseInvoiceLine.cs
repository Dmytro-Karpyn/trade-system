using Trade.Domain.Common;

namespace Trade.Domain.Purchasing;

public sealed class PurchaseInvoiceLine
{
    private PurchaseInvoiceLine() { }

    public Guid Id { get; private set; }
    public Guid PurchaseInvoiceId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPriceCurrency { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    internal static PurchaseInvoiceLine Create(Guid purchaseInvoiceId, PurchaseLineDetails input)
    {
        var line = new PurchaseInvoiceLine { Id = Guid.CreateVersion7(), PurchaseInvoiceId = purchaseInvoiceId };
        line.Update(input, false);
        return line;
    }

    internal void Update(PurchaseLineDetails input, bool hasBatch)
    {
        Guard.RequiredId(input.ProductId, nameof(ProductId));
        Guard.Against(hasBatch && input.ProductId != ProductId, "ProductId cannot change after a Batch has been created.");
        Guard.Against(input.LineNo <= 0, "LineNo must be positive.");
        Guard.Against(input.Quantity <= 0, "Quantity must be positive.");
        Guard.Against(input.UnitPriceCurrency < 0, "UnitPriceCurrency must be nonnegative.");
        Guard.Numeric(input.Quantity, 4, 10_000_000_000_000_000m, nameof(Quantity));
        Guard.Numeric(input.UnitPriceCurrency, 4, 10_000_000_000_000_000m, nameof(UnitPriceCurrency));
        LineNo = input.LineNo;
        ProductId = input.ProductId;
        Quantity = input.Quantity;
        UnitPriceCurrency = input.UnitPriceCurrency;
        RemovedAt = null;
    }

    internal void Remove(DateTimeOffset now) => RemovedAt ??= UtcTimestamp.Normalize(now);
}
