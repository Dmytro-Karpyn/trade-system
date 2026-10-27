using Trade.Domain.Documents;
using Trade.Domain.Inventory;

namespace Trade.Application;

public sealed record StockTransferRequest(DateOnly DocumentDate, Guid FromLocationId, Guid ToLocationId,
    IReadOnlyList<StockTransferLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record UpdateStockTransferRequest(long Version, DateOnly DocumentDate, Guid FromLocationId, Guid ToLocationId,
    IReadOnlyList<StockTransferLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record StockTransferLineResponse(Guid Id, Guid StockTransferId, int LineNo, Guid BatchId, decimal Quantity);
public sealed record StockTransferResponse(DocumentResponse Document, Guid FromLocationId, Guid ToLocationId,
    IReadOnlyList<StockTransferLineResponse> Lines);
public sealed record StockTransferFilter(DateOnly? From = null, DateOnly? To = null, Guid? FromLocationId = null,
    Guid? ToLocationId = null, DocumentStatus? Status = null);

internal static class StockTransferResponses
{
    public static StockTransferResponse ToResponse(this StockTransfer transfer)
    {
        var d = transfer.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), transfer.FromLocationId, transfer.ToLocationId,
            transfer.Lines.OrderBy(x => x.LineNo).ThenBy(x => x.Id)
                .Select(x => new StockTransferLineResponse(x.Id, x.StockTransferId, x.LineNo, x.BatchId, x.Quantity)).ToArray());
    }
}
