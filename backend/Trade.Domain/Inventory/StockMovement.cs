using Trade.Domain.Common;

namespace Trade.Domain.Inventory;

public sealed class StockMovement
{
    private StockMovement() { }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public decimal Quantity { get; private set; }

    public static StockMovement Receipt(Guid documentId, Guid batchId, Guid storageLocationId, decimal quantity)
    {
        Guard.RequiredId(documentId, nameof(DocumentId));
        Guard.RequiredId(batchId, nameof(BatchId));
        Guard.RequiredId(storageLocationId, nameof(StorageLocationId));
        Guard.Against(quantity <= 0, "A purchase receipt must have positive Quantity.");
        Guard.Numeric(quantity, 4, 10_000_000_000_000_000m, nameof(Quantity));
        return new StockMovement { Id = Guid.CreateVersion7(), DocumentId = documentId,
            BatchId = batchId, StorageLocationId = storageLocationId, Quantity = quantity };
    }

    public static StockMovement Issue(Guid documentId, Guid batchId, Guid storageLocationId, decimal quantity)
    {
        var movement = Receipt(documentId, batchId, storageLocationId, quantity);
        movement.Quantity = -quantity;
        return movement;
    }
}
