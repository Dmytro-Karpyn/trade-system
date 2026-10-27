using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Sales;
using Trade.Domain.Settlements;

namespace Trade.Domain.Returns;

public sealed record CustomerReturnLineInput(Guid? Id, Guid SourceSaleLineId, decimal Quantity, decimal? RefundAmount = null);
public sealed record ReturnedLineTotals(Guid SourceSaleLineId, decimal Quantity, decimal RefundAmount);

public sealed class ReturnQuantityExceededException() : Exception("RETURN_QUANTITY_EXCEEDED");
public sealed class ReturnAmountExceededException() : Exception("RETURN_AMOUNT_EXCEEDED");

public sealed class CustomerReturn
{
    private CustomerReturn() { }

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid SourceSaleInvoiceId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public List<CustomerReturnLine> Lines { get; private set; } = [];

    public static CustomerReturn Create(Document document, SaleInvoice source, Guid locationId,
        IReadOnlyList<CustomerReturnLineInput> inputs, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, ReturnedLineTotals> returned)
    {
        Guard.Against(document.DocumentType != DocumentType.CUSTOMER_RETURN, "A return requires CUSTOMER_RETURN DocumentType.");
        var result = new CustomerReturn { DocumentId = document.Id, Document = document };
        result.Update(source, locationId, inputs, products, returned);
        return result;
    }

    public void Update(SaleInvoice source, Guid locationId, IReadOnlyList<CustomerReturnLineInput> inputs,
        IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, ReturnedLineTotals> returned)
    {
        Document.RequireDraft();
        RequireSource(source, Document.OrganizationId);
        Guard.RequiredId(locationId, nameof(StorageLocationId));
        Guard.Against(inputs.Select(x => x.SourceSaleLineId).Distinct().Count() != inputs.Count,
            "A source line can occur only once in a return.");
        var ids = inputs.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        Guard.Against(ids.Distinct().Count() != ids.Length || ids.Any(id => Lines.All(x => x.Id != id)),
            "Line IDs must be unique and belong to this return.");
        var sourceLines = source.Lines.Where(x => x.RemovedAt is null).ToDictionary(x => x.Id);
        var newLines = new List<CustomerReturnLine>();
        foreach (var input in inputs)
        {
            Guard.Against(!sourceLines.TryGetValue(input.SourceSaleLineId, out var line),
                "Every source line must be active and belong to SourceSaleInvoice.");
            var product = products[line!.ProductId];
            var totals = returned.GetValueOrDefault(line.Id) ?? new(line.Id, 0m, 0m);
            var refund = Refund(source, line, product.ItemType, input.Quantity, input.RefundAmount, totals);
            var existing = input.Id is { } id ? Lines.Single(x => x.Id == id) : null;
            var target = existing ?? CustomerReturnLine.Create(DocumentId);
            target.Set(input.SourceSaleLineId, input.Quantity, refund);
            newLines.Add(target);
        }
        SourceSaleInvoiceId = source.DocumentId;
        StorageLocationId = locationId;
        // No RemovedAt column is part of this slice: unselected DRAFT lines are replaced physically.
        Lines.Clear();
        Lines.AddRange(newLines);
    }

    public void ValidateForPost(SaleInvoice source, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, ReturnedLineTotals> returned)
    {
        Document.RequireDraft();
        RequireSource(source, Document.OrganizationId);
        Guard.Against(source.DocumentId != SourceSaleInvoiceId, "Invalid source sale.");
        Guard.Against(Lines.Count == 0, "At least one line is required for posting.");
        foreach (var line in Lines)
        {
            var original = source.Lines.SingleOrDefault(x => x.Id == line.SourceSaleLineId && x.RemovedAt is null);
            Guard.Against(original is null, "The source line must be active and belong to SourceSaleInvoice.");
            var type = products[original!.ProductId].ItemType;
            var refund = Refund(source, original, type, line.Quantity,
                type == ItemType.GOODS ? null : line.RefundAmount,
                returned.GetValueOrDefault(original.Id) ?? new(original.Id, 0m, 0m));
            line.Set(line.SourceSaleLineId, line.Quantity, refund);
        }
    }

    public static void RequireSource(SaleInvoice source, Guid organizationId) =>
        Guard.Against(source.Document.OrganizationId != organizationId || source.Document.DocumentType != DocumentType.SALE_INVOICE
            || source.Document.Status != DocumentStatus.POSTED || source.Document.DeletedAt is not null,
            "CustomerReturn requires a nondeleted POSTED SaleInvoice in the current organization.");

    public static decimal NetAmount(SaleInvoice sale, SaleInvoiceLine line, decimal? quantity = null) =>
        DocumentAmount.Calculate([(quantity ?? line.Quantity, line.UnitPrice)], sale.DiscountPercent);

    private static decimal Refund(SaleInvoice sale, SaleInvoiceLine line, ItemType type, decimal quantity,
        decimal? requested, ReturnedLineTotals returned)
    {
        Guard.Against(quantity < 0 || (type == ItemType.GOODS && quantity == 0), "GOODS quantity must be positive; SERVICE quantity may be zero.");
        Guard.Numeric(quantity, 4, 10_000_000_000_000_000m, "Quantity");
        var remainingAmount = NetAmount(sale, line) - returned.RefundAmount;
        if (type == ItemType.SERVICE)
        {
            var refund = requested ?? remainingAmount;
            Guard.Against(refund <= 0, "SERVICE RefundAmount must be positive.");
            Guard.Numeric(refund, 4, 10_000_000_000_000_000m, "RefundAmount");
            if (refund > remainingAmount) throw new ReturnAmountExceededException();
            return refund;
        }
        Guard.Against(requested is not null, "GOODS RefundAmount is calculated by the backend and must be omitted.");
        var remainingQuantity = line.Quantity - returned.Quantity;
        if (quantity > remainingQuantity) throw new ReturnQuantityExceededException();
        // Reconcile the last quantity with the remaining four-decimal line value. Clamp partial
        // rounding so many small returns can never refund more than the original net amount.
        return quantity == remainingQuantity ? remainingAmount : Math.Min(NetAmount(sale, line, quantity), remainingAmount);
    }
}

public sealed class CustomerReturnLine
{
    private CustomerReturnLine() { }
    public Guid Id { get; private set; }
    public Guid CustomerReturnId { get; private set; }
    public Guid SourceSaleLineId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal RefundAmount { get; private set; }

    internal static CustomerReturnLine Create(Guid id) => new() { Id = Guid.CreateVersion7(), CustomerReturnId = id };
    internal void Set(Guid sourceLineId, decimal quantity, decimal refund)
    {
        SourceSaleLineId = sourceLineId;
        Quantity = quantity;
        RefundAmount = refund;
    }
}
