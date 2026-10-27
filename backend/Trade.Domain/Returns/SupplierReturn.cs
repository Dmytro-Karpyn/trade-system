using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;

namespace Trade.Domain.Returns;

public sealed record SupplierReturnLineInput(Guid? Id, Guid SourcePurchaseLineId, decimal Quantity);
public sealed record SupplierReturnedQuantity(Guid SourcePurchaseLineId, decimal Quantity);

public sealed class SupplierReturn
{
    private SupplierReturn() { }
    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid SourcePurchaseInvoiceId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public List<SupplierReturnLine> Lines { get; private set; } = [];

    public static SupplierReturn Create(Document document, PurchaseInvoice source, Guid locationId,
        IReadOnlyList<SupplierReturnLineInput> inputs, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, decimal> returned)
    {
        Guard.Against(document.DocumentType != DocumentType.SUPPLIER_RETURN, "A supplier return requires SUPPLIER_RETURN DocumentType.");
        var result = new SupplierReturn { DocumentId = document.Id, Document = document };
        result.Update(source, locationId, inputs, products, returned);
        return result;
    }

    public void Update(PurchaseInvoice source, Guid locationId, IReadOnlyList<SupplierReturnLineInput> inputs,
        IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, decimal> returned)
    {
        Document.RequireDraft();
        RequireSource(source, Document.OrganizationId);
        Guard.RequiredId(locationId, nameof(StorageLocationId));
        Guard.Against(inputs.Select(x => x.SourcePurchaseLineId).Distinct().Count() != inputs.Count,
            "A source purchase line can occur only once in a supplier return.");
        var ids = inputs.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        Guard.Against(ids.Distinct().Count() != ids.Length || ids.Any(id => Lines.All(x => x.Id != id)),
            "Line IDs must be unique and belong to this supplier return.");
        var newLines = new List<SupplierReturnLine>();
        foreach (var input in inputs)
        {
            ValidateLine(source, input.SourcePurchaseLineId, input.Quantity, products, returned);
            var line = input.Id is { } id ? Lines.Single(x => x.Id == id) : SupplierReturnLine.Create(DocumentId);
            line.Set(input.SourcePurchaseLineId, input.Quantity);
            newLines.Add(line);
        }
        SourcePurchaseInvoiceId = source.DocumentId;
        StorageLocationId = locationId;
        // The requested schema has no RemovedAt: replace unselected DRAFT lines.
        Lines.Clear();
        Lines.AddRange(newLines);
    }

    public void ValidateForPost(PurchaseInvoice source, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, decimal> returned)
    {
        Document.RequireDraft();
        RequireSource(source, Document.OrganizationId);
        Guard.Against(source.DocumentId != SourcePurchaseInvoiceId, "Invalid source purchase.");
        Guard.Against(Lines.Count == 0, "At least one line is required for posting.");
        foreach (var line in Lines) ValidateLine(source, line.SourcePurchaseLineId, line.Quantity, products, returned);
    }

    public static void RequireSource(PurchaseInvoice source, Guid organizationId) =>
        Guard.Against(source.Document.OrganizationId != organizationId || source.Document.DocumentType != DocumentType.PURCHASE_INVOICE
            || source.Document.Status != DocumentStatus.POSTED || source.Document.DeletedAt is not null,
            "SupplierReturn requires a nondeleted POSTED PURCHASE_INVOICE in the current organization; OPENING_STOCK is not allowed.");

    private static void ValidateLine(PurchaseInvoice source, Guid sourceId, decimal quantity,
        IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, decimal> returned)
    {
        Guard.Against(quantity <= 0, "Quantity must be positive.");
        Guard.Numeric(quantity, 4, 10_000_000_000_000_000m, "Quantity");
        var original = source.Lines.SingleOrDefault(x => x.Id == sourceId && x.RemovedAt is null);
        Guard.Against(original is null, "Every source line must be active and belong to SourcePurchaseInvoice.");
        Guard.Against(!products.TryGetValue(original!.ProductId, out var product) || product.ItemType != ItemType.GOODS,
            "SupplierReturn supports only GOODS of the current organization.");
        if (quantity > original.Quantity - returned.GetValueOrDefault(sourceId)) throw new ReturnQuantityExceededException();
    }
}

public sealed class SupplierReturnLine
{
    private SupplierReturnLine() { }
    public Guid Id { get; private set; }
    public Guid SupplierReturnId { get; private set; }
    public Guid SourcePurchaseLineId { get; private set; }
    public decimal Quantity { get; private set; }

    internal static SupplierReturnLine Create(Guid id) => new() { Id = Guid.CreateVersion7(), SupplierReturnId = id };
    internal void Set(Guid sourceId, decimal quantity) { SourcePurchaseLineId = sourceId; Quantity = quantity; }
}
