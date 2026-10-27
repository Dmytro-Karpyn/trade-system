using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Pricing;

namespace Trade.Domain.Sales;

public sealed record SaleDetails(Guid CounterpartyId, Guid StorageLocationId, Guid? PriceTypeId, decimal DiscountPercent);

public sealed record SaleLineDetails(Guid? Id, int LineNo, Guid ProductId, Guid? BatchId, decimal Quantity,
    decimal RecommendedUnitPrice, decimal UnitPrice);

public sealed class SaleInvoice
{
    private SaleInvoice() { }

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid CounterpartyId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid? PriceTypeId { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public List<SaleInvoiceLine> Lines { get; private set; } = [];

    public static SaleInvoice Create(Document document, SaleDetails details, IReadOnlyList<SaleLineDetails> lines,
        IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, Guid> batchProducts,
        SalePricePermissions permissions, DateTimeOffset now)
    {
        Guard.Against(document.DocumentType != DocumentType.SALE_INVOICE, "A sale requires SALE_INVOICE DocumentType.");
        var sale = new SaleInvoice { DocumentId = document.Id, Document = document };
        sale.Update(details, lines, products, batchProducts, permissions, now);
        return sale;
    }

    public void Update(SaleDetails details, IReadOnlyList<SaleLineDetails> inputs,
        IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, Guid> batchProducts,
        SalePricePermissions permissions, DateTimeOffset now)
    {
        Document.RequireDraft();
        Guard.RequiredId(details.CounterpartyId, nameof(CounterpartyId));
        Guard.RequiredId(details.StorageLocationId, nameof(StorageLocationId));
        Guard.Against(details.PriceTypeId == Guid.Empty, "PriceTypeId must be null or a nonempty ID.");
        Guard.Against(details.DiscountPercent is < 0 or > 100, "DiscountPercent must be between 0 and 100.");
        Guard.Numeric(details.DiscountPercent, 4, 1000m, nameof(DiscountPercent));
        Guard.Against(inputs.Select(x => x.LineNo).Distinct().Count() != inputs.Count, "LineNo must be unique among active lines.");
        var batches = inputs.Where(x => x.BatchId is not null).Select(x => x.BatchId!.Value).ToArray();
        Guard.Against(batches.Distinct().Count() != batches.Length, "A Batch cannot occur twice among active sale lines.");
        var ids = inputs.Where(x => x.Id is not null).Select(x => x.Id!.Value).ToArray();
        Guard.Against(ids.Distinct().Count() != ids.Length, "Line IDs must be unique.");
        Guard.Against(ids.Any(id => Lines.All(x => x.Id != id)), "A line does not belong to this sale.");

        CounterpartyId = details.CounterpartyId;
        StorageLocationId = details.StorageLocationId;
        PriceTypeId = details.PriceTypeId;
        DiscountPercent = details.DiscountPercent;
        foreach (var line in Lines.Where(x => !ids.Contains(x.Id))) line.Remove(now);
        foreach (var input in inputs)
        {
            Guard.Against(!products.ContainsKey(input.ProductId), "Every ProductId must belong to the current organization.");
            var type = products[input.ProductId].ItemType;
            var batchProduct = input.BatchId is { } batchId && batchProducts.TryGetValue(batchId, out var productId)
                ? productId : (Guid?)null;
            if (input.Id is { } id)
                Lines.Single(x => x.Id == id).Update(input, type, batchProduct, permissions);
            else
                Lines.Add(SaleInvoiceLine.Create(DocumentId, input, type, batchProduct, permissions));
        }
    }

    public void ValidateForPost(IReadOnlyDictionary<Guid, Product> products, IReadOnlyDictionary<Guid, Guid> batchProducts,
        SalePricePermissions permissions)
    {
        Document.RequireDraft();
        var lines = Lines.Where(x => x.RemovedAt is null).ToArray();
        Guard.Against(lines.Length == 0, "At least one active line is required for posting.");
        foreach (var line in lines)
        {
            PriceRules.RequireAllowed(line.RecommendedUnitPrice, line.UnitPrice, permissions);
            Guard.Against(!products.ContainsKey(line.ProductId), "Every ProductId must belong to the current organization.");
            line.ValidateProduct(products[line.ProductId].ItemType,
                line.BatchId is { } id && batchProducts.TryGetValue(id, out var productId) ? productId : null);
        }
    }
}
