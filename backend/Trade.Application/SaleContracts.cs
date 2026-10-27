using Trade.Domain.Sales;

namespace Trade.Application;

// Every sale price is denominated in UAH. There is no currency selection/conversion in this slice.
public sealed record SaleLineRequest(Guid? Id, int LineNo, Guid ProductId, Guid? BatchId, decimal Quantity,
    decimal? UnitPrice = null);

public sealed record SaleRequest(DateOnly DocumentDate, string? Comment, string? InternalNote,
    Guid CounterpartyId, Guid StorageLocationId, Guid? PriceTypeId, decimal DiscountPercent,
    IReadOnlyList<SaleLineRequest> Lines)
{
    public SaleDetails ToDetails() => new(CounterpartyId, StorageLocationId, PriceTypeId, DiscountPercent);
}

public sealed record UpdateSaleRequest(long Version, DateOnly DocumentDate, string? Comment, string? InternalNote,
    Guid CounterpartyId, Guid StorageLocationId, Guid? PriceTypeId, decimal DiscountPercent,
    IReadOnlyList<SaleLineRequest> Lines)
{
    public SaleDetails ToDetails() => new(CounterpartyId, StorageLocationId, PriceTypeId, DiscountPercent);
}

public sealed record SaleLineResponse(Guid Id, Guid SaleInvoiceId, int LineNo, Guid ProductId, Guid? BatchId,
    decimal Quantity, decimal RecommendedUnitPrice, decimal UnitPrice, DateTimeOffset? RemovedAt);

public sealed record SaleResponse(DocumentResponse Document, Guid CounterpartyId, Guid StorageLocationId,
    Guid? PriceTypeId, decimal DiscountPercent, IReadOnlyList<SaleLineResponse> Lines);

public sealed record AvailableBatchResponse(Guid BatchId, Guid ProductId, Guid StorageLocationId,
    decimal PhysicalQuantity, decimal ReservedQuantity, decimal AvailableQuantity,
    DateOnly PurchaseDocumentDate, string BatchNumber);

public sealed record BatchReference(Guid BatchId, Guid ProductId);

// ReservedQuantity here can exclude one document for write validation; this is not a persisted balance.
public sealed record StockState(Guid BatchId, Guid StorageLocationId, decimal PhysicalQuantity, decimal ReservedQuantity)
{
    public decimal AvailableQuantity => PhysicalQuantity - ReservedQuantity;
}

internal static class SaleResponses
{
    public static SaleResponse ToResponse(this SaleInvoice sale)
    {
        var d = sale.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), sale.CounterpartyId, sale.StorageLocationId,
            sale.PriceTypeId, sale.DiscountPercent, sale.Lines.OrderBy(x => x.LineNo).ThenBy(x => x.Id)
                .Select(x => new SaleLineResponse(x.Id, x.SaleInvoiceId, x.LineNo, x.ProductId, x.BatchId,
                    x.Quantity, x.RecommendedUnitPrice, x.UnitPrice, x.RemovedAt)).ToArray());
    }
}
