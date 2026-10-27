using Trade.Domain.Common;

namespace Trade.Domain.Inventory;

public sealed class Batch
{
    private Batch() { }
    public Guid Id { get; private set; }
    public Guid PurchaseInvoiceLineId { get; private set; }
    public string BatchNumber { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Batch Create(Guid purchaseInvoiceLineId, DateTimeOffset now)
    {
        Guard.RequiredId(purchaseInvoiceLineId, nameof(PurchaseInvoiceLineId));
        var id = Guid.CreateVersion7();
        return new Batch { Id = id, PurchaseInvoiceLineId = purchaseInvoiceLineId,
            BatchNumber = $"B-{id:N}", CreatedAt = UtcTimestamp.Normalize(now) };
    }
}
