using Trade.Domain.Catalog;
using Trade.Domain.Returns;

namespace Trade.Application;

public sealed record CustomerReturnRequest(DateOnly DocumentDate, Guid SourceSaleInvoiceId, Guid StorageLocationId,
    IReadOnlyList<CustomerReturnLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record CustomerReturnFromSaleRequest(DateOnly DocumentDate, Guid? StorageLocationId = null,
    IReadOnlyList<CustomerReturnLineInput>? Lines = null, string? Comment = null, string? InternalNote = null);
public sealed record UpdateCustomerReturnRequest(long Version, DateOnly DocumentDate, Guid SourceSaleInvoiceId, Guid StorageLocationId,
    IReadOnlyList<CustomerReturnLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record CustomerReturnLineResponse(Guid Id, Guid CustomerReturnId, Guid SourceSaleLineId, decimal Quantity, decimal RefundAmount);
public sealed record CustomerReturnResponse(DocumentResponse Document, Guid SourceSaleInvoiceId, Guid StorageLocationId,
    IReadOnlyList<CustomerReturnLineResponse> Lines);
public sealed record ReturnableLineResponse(Guid SourceSaleLineId, Guid ProductId, ItemType ItemType, Guid? BatchId,
    decimal SoldQuantity, decimal AlreadyReturnedQuantity, decimal ReturnableQuantity, decimal OriginalUnitPrice,
    decimal OriginalDiscountPercent, decimal OriginalNetAmount, decimal AlreadyRefundedAmount, decimal RefundableAmount);

internal static class CustomerReturnResponses
{
    public static CustomerReturnResponse ToResponse(this CustomerReturn r)
    {
        var d = r.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), r.SourceSaleInvoiceId, r.StorageLocationId,
            r.Lines.OrderBy(x => x.Id).Select(x => new CustomerReturnLineResponse(x.Id, x.CustomerReturnId,
                x.SourceSaleLineId, x.Quantity, x.RefundAmount)).ToArray());
    }
}
