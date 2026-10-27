using Trade.Domain.Common;

namespace Trade.Domain.Pricing;

public sealed class BatchPrice
{
    private BatchPrice() { }

    public Guid BatchId { get; private set; }
    public Guid PriceTypeId { get; private set; }
    public decimal UnitPrice { get; private set; }

    public static BatchPrice Create(Guid batchId, Guid priceTypeId, decimal unitPrice)
    {
        Guard.RequiredId(batchId, nameof(BatchId));
        Guard.RequiredId(priceTypeId, nameof(PriceTypeId));
        var price = new BatchPrice { BatchId = batchId, PriceTypeId = priceTypeId };
        price.Change(unitPrice);
        return price;
    }

    public void Change(decimal unitPrice)
    {
        PriceRules.Validate(unitPrice);
        UnitPrice = unitPrice;
    }
}
