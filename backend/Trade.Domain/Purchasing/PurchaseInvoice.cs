using Trade.Domain.Common;
using Trade.Domain.Documents;

namespace Trade.Domain.Purchasing;

public sealed record PurchaseDetails(Guid? CounterpartyId, Guid StorageLocationId, Guid CurrencyId,
    decimal ExchangeRate, string? ExternalNumber, DateOnly? ExternalDate, decimal DiscountPercent);

public sealed record PurchaseLineDetails(Guid? Id, int LineNo, Guid ProductId, decimal Quantity, decimal UnitPriceCurrency);

public sealed class PurchaseInvoice
{
    private PurchaseInvoice() { }

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public Guid? CounterpartyId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid CurrencyId { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public string? ExternalNumber { get; private set; }
    public DateOnly? ExternalDate { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public List<PurchaseInvoiceLine> Lines { get; private set; } = [];

    public static PurchaseInvoice Create(Document document, PurchaseDetails details,
        IReadOnlyList<PurchaseLineDetails> lines, DateTimeOffset now)
    {
        Guard.Against(document.DocumentType is not (DocumentType.PURCHASE_INVOICE or DocumentType.OPENING_STOCK),
            "A purchase requires PURCHASE_INVOICE or OPENING_STOCK DocumentType.");
        var invoice = new PurchaseInvoice { DocumentId = document.Id, Document = document };
        invoice.SetDetails(details);
        invoice.ReplaceLines(lines, new HashSet<Guid>(), now);
        return invoice;
    }

    public void Update(PurchaseDetails details, IReadOnlyList<PurchaseLineDetails> lines,
        IReadOnlySet<Guid> linesWithBatch, DateTimeOffset now)
    {
        Document.RequireDraft();
        SetDetails(details);
        ReplaceLines(lines, linesWithBatch, now);
    }

    public void ValidateForPost()
    {
        Document.RequireDraft();
        Guard.Against(!Lines.Any(x => x.RemovedAt is null), "At least one active line is required for posting.");
    }

    private void SetDetails(PurchaseDetails details)
    {
        Guard.Against(details.CounterpartyId == Guid.Empty, "CounterpartyId must be null or a nonempty ID.");
        Guard.Against(Document.DocumentType != DocumentType.OPENING_STOCK && details.CounterpartyId is null,
            "CounterpartyId is required unless DocumentType is OPENING_STOCK.");
        Guard.RequiredId(details.StorageLocationId, nameof(StorageLocationId));
        Guard.RequiredId(details.CurrencyId, nameof(CurrencyId));
        Guard.Against(details.ExchangeRate <= 0, "ExchangeRate must be positive.");
        Guard.Numeric(details.ExchangeRate, 8, 1_000_000_000_000m, nameof(ExchangeRate));
        Guard.Against(details.DiscountPercent is < 0 or > 100, "DiscountPercent must be between 0 and 100.");
        Guard.Numeric(details.DiscountPercent, 4, 1000m, nameof(DiscountPercent));
        Guard.Against(details.ExternalDate == default(DateOnly), "ExternalDate must be null or a valid date.");
        CounterpartyId = details.CounterpartyId;
        StorageLocationId = details.StorageLocationId;
        CurrencyId = details.CurrencyId;
        ExchangeRate = details.ExchangeRate;
        ExternalNumber = details.ExternalNumber;
        ExternalDate = details.ExternalDate;
        DiscountPercent = details.DiscountPercent;
    }

    private void ReplaceLines(IReadOnlyList<PurchaseLineDetails> inputs, IReadOnlySet<Guid> linesWithBatch, DateTimeOffset now)
    {
        Guard.Against(inputs.Select(x => x.LineNo).Distinct().Count() != inputs.Count, "LineNo must be unique among active lines.");
        var suppliedIds = inputs.Where(x => x.Id is not null).Select(x => x.Id!.Value).ToArray();
        Guard.Against(suppliedIds.Distinct().Count() != suppliedIds.Length, "Line IDs must be unique.");
        Guard.Against(suppliedIds.Any(id => Lines.All(x => x.Id != id)), "A line does not belong to this purchase.");

        foreach (var existing in Lines.Where(x => !suppliedIds.Contains(x.Id))) existing.Remove(now);
        foreach (var input in inputs)
        {
            if (input.Id is { } id)
                Lines.Single(x => x.Id == id).Update(input, linesWithBatch.Contains(id));
            else
                Lines.Add(PurchaseInvoiceLine.Create(DocumentId, input));
        }
    }
}
