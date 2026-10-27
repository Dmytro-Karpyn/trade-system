using System.Text.Json;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;

namespace Trade.Application;

public sealed record ProductFilter(string? Search = null, ItemType? ItemType = null, bool? Active = null);

public sealed record ProductRequest(Guid? CategoryId, Guid UnitId, ItemType ItemType, string Code,
    string? Barcode, string Name, string? Description, JsonElement? Attributes, bool Active = true)
{
    public ProductDetails ToDetails() => new(CategoryId, UnitId, ItemType, Code, Barcode, Name, Description,
        Attributes is { ValueKind: not JsonValueKind.Null } json ? json.GetRawText() : null, Active);
}

public sealed record ProductResponse(Guid Id, Guid OrganizationId, Guid? CategoryId, Guid UnitId,
    ItemType ItemType, string Code, string? Barcode, string Name, string? Description, JsonElement? Attributes, bool Active);

public sealed record PurchaseRequest(DocumentType DocumentType, DateOnly DocumentDate, string? Comment,
    string? InternalNote, Guid? CounterpartyId, Guid StorageLocationId, Guid CurrencyId, decimal ExchangeRate,
    string? ExternalNumber, DateOnly? ExternalDate, decimal DiscountPercent, IReadOnlyList<PurchaseLineDetails> Lines)
{
    public PurchaseDetails ToDetails() => new(CounterpartyId, StorageLocationId, CurrencyId,
        ExchangeRate, ExternalNumber, ExternalDate, DiscountPercent);
}

public sealed record UpdatePurchaseRequest(long Version, DateOnly DocumentDate, string? Comment,
    string? InternalNote, Guid? CounterpartyId, Guid StorageLocationId, Guid CurrencyId, decimal ExchangeRate,
    string? ExternalNumber, DateOnly? ExternalDate, decimal DiscountPercent, IReadOnlyList<PurchaseLineDetails> Lines)
{
    public PurchaseDetails ToDetails() => new(CounterpartyId, StorageLocationId, CurrencyId,
        ExchangeRate, ExternalNumber, ExternalDate, DiscountPercent);
}

public sealed record DocumentResponse(Guid Id, Guid OrganizationId, DocumentType DocumentType, string? Number,
    DateOnly DocumentDate, DocumentStatus Status, string? Comment, string? InternalNote, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid? PostedBy, DateTimeOffset? PostedAt,
    Guid? DeletedBy, DateTimeOffset? DeletedAt, long Version);

public sealed record PurchaseLineResponse(Guid Id, Guid PurchaseInvoiceId, int LineNo, Guid ProductId,
    decimal Quantity, decimal UnitPriceCurrency, DateTimeOffset? RemovedAt);

public sealed record PurchaseResponse(DocumentResponse Document, Guid? CounterpartyId, Guid StorageLocationId,
    Guid CurrencyId, decimal ExchangeRate, string? ExternalNumber, DateOnly? ExternalDate,
    decimal DiscountPercent, IReadOnlyList<PurchaseLineResponse> Lines,
    IReadOnlyList<PurchaseProductResponse>? Products = null, string? PostedByName = null);

public sealed record StockBalanceResponse(Guid BatchId, Guid StorageLocationId, decimal PhysicalQuantity,
    decimal ReservedQuantity, decimal AvailableQuantity);

internal static class Responses
{
    public static ProductResponse ToResponse(this Product p) => new(p.Id, p.OrganizationId, p.CategoryId,
        p.UnitId, p.ItemType, p.Code, p.Barcode, p.Name, p.Description,
        p.Attributes is null ? null : JsonSerializer.Deserialize<JsonElement>(p.Attributes), p.Active);

    public static PurchaseResponse ToResponse(this PurchaseInvoice p)
    {
        var d = p.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), p.CounterpartyId, p.StorageLocationId, p.CurrencyId,
            p.ExchangeRate, p.ExternalNumber, p.ExternalDate, p.DiscountPercent,
            p.Lines.OrderBy(x => x.LineNo).ThenBy(x => x.Id).Select(x => new PurchaseLineResponse(
                x.Id, x.PurchaseInvoiceId, x.LineNo, x.ProductId, x.Quantity, x.UnitPriceCurrency, x.RemovedAt)).ToArray());
    }
}
