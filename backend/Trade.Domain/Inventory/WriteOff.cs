using Trade.Domain.Common;
using Trade.Domain.Documents;

namespace Trade.Domain.Inventory;

public sealed record WriteOffLineInput(Guid? Id, int LineNo, Guid BatchId, decimal Quantity);

public sealed class WriteOff
{
    private WriteOff() { }
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid StorageLocationId { get; private set; }
    public Guid WriteOffReasonId { get; private set; }
    public List<WriteOffLine> Lines { get; private set; } = [];

    public static WriteOff Create(Document document, Guid locationId, Guid reasonId,
        IReadOnlyList<WriteOffLineInput> inputs, IReadOnlySet<Guid> ownedBatchIds)
    {
        Guard.Against(document.DocumentType != DocumentType.WRITE_OFF, "A write-off requires WRITE_OFF DocumentType.");
        var result = new WriteOff { DocumentId = document.Id, Document = document };
        result.Update(locationId, reasonId, inputs, ownedBatchIds);
        return result;
    }

    public void Update(Guid locationId, Guid reasonId, IReadOnlyList<WriteOffLineInput> inputs, IReadOnlySet<Guid> ownedBatchIds)
    {
        Document.RequireDraft();
        Guard.RequiredId(locationId, nameof(StorageLocationId));
        Guard.RequiredId(reasonId, nameof(WriteOffReasonId));
        Guard.Against(inputs.Select(x => x.LineNo).Distinct().Count() != inputs.Count, "LineNo must be unique.");
        Guard.Against(inputs.Select(x => x.BatchId).Distinct().Count() != inputs.Count, "A Batch cannot occur twice in a write-off.");
        var ids = inputs.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        Guard.Against(ids.Distinct().Count() != ids.Length || ids.Any(id => Lines.All(x => x.Id != id)),
            "Line IDs must be unique and belong to this write-off.");
        var newLines = new List<WriteOffLine>();
        foreach (var input in inputs)
        {
            ValidateLine(input.LineNo, input.BatchId, input.Quantity, ownedBatchIds);
            var line = input.Id is { } id ? Lines.Single(x => x.Id == id) : WriteOffLine.Create(DocumentId);
            line.Set(input.LineNo, input.BatchId, input.Quantity);
            newLines.Add(line);
        }
        StorageLocationId = locationId;
        WriteOffReasonId = reasonId;
        // No RemovedAt is part of the agreed schema; replace unselected DRAFT lines.
        Lines.Clear();
        Lines.AddRange(newLines);
    }

    public void ValidateForPost(IReadOnlySet<Guid> ownedBatchIds)
    {
        Document.RequireDraft();
        Guard.RequiredId(StorageLocationId, nameof(StorageLocationId));
        Guard.RequiredId(WriteOffReasonId, nameof(WriteOffReasonId));
        Guard.Against(Lines.Count == 0, "At least one line is required for posting.");
        Guard.Against(Lines.Select(x => x.BatchId).Distinct().Count() != Lines.Count, "A Batch cannot occur twice in a write-off.");
        Guard.Against(Lines.Select(x => x.LineNo).Distinct().Count() != Lines.Count, "LineNo must be unique.");
        foreach (var line in Lines) ValidateLine(line.LineNo, line.BatchId, line.Quantity, ownedBatchIds);
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

public sealed class WriteOffLine
{
    private WriteOffLine() { }
    public Guid Id { get; private set; }
    public Guid WriteOffId { get; private set; }
    public int LineNo { get; private set; }
    public Guid BatchId { get; private set; }
    public decimal Quantity { get; private set; }

    internal static WriteOffLine Create(Guid id) => new() { Id = Guid.CreateVersion7(), WriteOffId = id };
    internal void Set(int lineNo, Guid batchId, decimal quantity) { LineNo = lineNo; BatchId = batchId; Quantity = quantity; }
}
