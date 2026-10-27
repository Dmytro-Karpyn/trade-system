using Trade.Domain.Returns;

namespace Trade.Application;

public sealed record SupplierReturnRequest(DateOnly DocumentDate, Guid SourcePurchaseInvoiceId, Guid StorageLocationId,
    IReadOnlyList<SupplierReturnLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record SupplierReturnFromPurchaseRequest(DateOnly DocumentDate, Guid? StorageLocationId = null,
    IReadOnlyList<SupplierReturnLineInput>? Lines = null, string? Comment = null, string? InternalNote = null);
public sealed record UpdateSupplierReturnRequest(long Version, DateOnly DocumentDate, Guid SourcePurchaseInvoiceId, Guid StorageLocationId,
    IReadOnlyList<SupplierReturnLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record SupplierReturnLineResponse(Guid Id, Guid SupplierReturnId, Guid SourcePurchaseLineId, decimal Quantity);
public sealed record SupplierReturnResponse(DocumentResponse Document, Guid SourcePurchaseInvoiceId, Guid StorageLocationId,
    IReadOnlyList<SupplierReturnLineResponse> Lines);
public sealed record SupplierReturnableLineResponse(Guid SourcePurchaseLineId, Guid ProductId, Guid BatchId,
    decimal PurchasedQuantity, decimal AlreadyReturnedQuantity, decimal RemainingReturnableByPurchase,
    decimal PhysicalQuantity, decimal ReservedQuantity, decimal AvailableQuantity, decimal MaxReturnableQuantity,
    decimal OriginalUnitPriceCurrency, decimal OriginalDiscountPercent, Guid CurrencyId, decimal ExchangeRate);

internal static class SupplierReturnResponses
{
    public static SupplierReturnResponse ToResponse(this SupplierReturn r)
    {
        var d = r.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), r.SourcePurchaseInvoiceId, r.StorageLocationId,
            r.Lines.OrderBy(x => x.Id).Select(x => new SupplierReturnLineResponse(x.Id, x.SupplierReturnId,
                x.SourcePurchaseLineId, x.Quantity)).ToArray());
    }
}
