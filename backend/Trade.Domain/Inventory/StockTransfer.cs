using Trade.Domain.Common;
using Trade.Domain.Documents;

namespace Trade.Domain.Inventory;

public sealed record StockTransferLineInput(Guid? Id, int LineNo, Guid BatchId, decimal Quantity);

public sealed class StockTransfer
{
    private StockTransfer() { }
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid FromLocationId { get; private set; }
    public Guid ToLocationId { get; private set; }
    public List<StockTransferLine> Lines { get; private set; } = [];

    public static StockTransfer Create(Document document, Guid fromLocationId, Guid toLocationId,
        IReadOnlyList<StockTransferLineInput> inputs, IReadOnlySet<Guid> ownedBatchIds)
    {
        Guard.Against(document.DocumentType != DocumentType.STOCK_TRANSFER, "A transfer requires STOCK_TRANSFER DocumentType.");
        var result = new StockTransfer { DocumentId = document.Id, Document = document };
        result.Update(fromLocationId, toLocationId, inputs, ownedBatchIds);
        return result;
    }

    public void Update(Guid fromLocationId, Guid toLocationId, IReadOnlyList<StockTransferLineInput> inputs,
        IReadOnlySet<Guid> ownedBatchIds)
    {
        Document.RequireDraft();
        RequireLocations(fromLocationId, toLocationId);
        Guard.Against(inputs.Select(x => x.LineNo).Distinct().Count() != inputs.Count, "LineNo must be unique.");
        Guard.Against(inputs.Select(x => x.BatchId).Distinct().Count() != inputs.Count, "A Batch cannot occur twice in a transfer.");
        var ids = inputs.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        Guard.Against(ids.Distinct().Count() != ids.Length || ids.Any(id => Lines.All(x => x.Id != id)),
            "Line IDs must be unique and belong to this transfer.");
        var newLines = new List<StockTransferLine>();
        foreach (var input in inputs)
        {
            ValidateLine(input.LineNo, input.BatchId, input.Quantity, ownedBatchIds);
            var line = input.Id is { } id ? Lines.Single(x => x.Id == id) : StockTransferLine.Create(DocumentId);
            line.Set(input.LineNo, input.BatchId, input.Quantity);
            newLines.Add(line);
        }
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        // No RemovedAt is part of this slice; replace unselected DRAFT lines.
        Lines.Clear();
        Lines.AddRange(newLines);
    }

    public void ValidateForPost(IReadOnlySet<Guid> ownedBatchIds)
    {
        Document.RequireDraft();
        RequireLocations(FromLocationId, ToLocationId);
        Guard.Against(Lines.Count == 0, "At least one line is required for posting.");
        Guard.Against(Lines.Select(x => x.BatchId).Distinct().Count() != Lines.Count, "A Batch cannot occur twice in a transfer.");
        Guard.Against(Lines.Select(x => x.LineNo).Distinct().Count() != Lines.Count, "LineNo must be unique.");
        foreach (var line in Lines) ValidateLine(line.LineNo, line.BatchId, line.Quantity, ownedBatchIds);
    }

    public static void RequireLocations(Guid fromLocationId, Guid toLocationId)
    {
        Guard.RequiredId(fromLocationId, nameof(FromLocationId));
        Guard.RequiredId(toLocationId, nameof(ToLocationId));
        Guard.Against(fromLocationId == toLocationId, "FromLocationId and ToLocationId must differ.");
    }

    private static void ValidateLine(int lineNo, Guid batchId, decimal quantity, IReadOnlySet<Guid> ownedBatchIds)
    {
        Guard.Against(lineNo <= 0, "LineNo must be positive.");
        Guard.RequiredId(batchId, "BatchId");
        Guard.Against(!ownedBatchIds.Contains(batchId), "Batch must exist in the current organization.");
        Guard.Against(quantity <= 0, "Quantity must be positive.");
        Guard.Numeric(quantity, 4, 10_000_000_000_000_000m, "Quantity");
    }
}

public sealed class StockTransferLine
{
    private StockTransferLine() { }
    public Guid Id { get; private set; }
    public Guid StockTransferId { get; private set; }
    public int LineNo { get; private set; }
    public Guid BatchId { get; private set; }
    public decimal Quantity { get; private set; }

    internal static StockTransferLine Create(Guid id) => new() { Id = Guid.CreateVersion7(), StockTransferId = id };
    internal void Set(int lineNo, Guid batchId, decimal quantity) { LineNo = lineNo; BatchId = batchId; Quantity = quantity; }
}
