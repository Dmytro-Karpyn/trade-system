using Trade.Domain.Documents;
using Trade.Domain.Inventory;

namespace Trade.Application;

public sealed record WriteOffReasonRequest(string Name, Guid ExpenseCategoryId, bool Active = true)
{
    public WriteOffReasonDetails ToDetails() => new(Name, ExpenseCategoryId, Active);
}
public sealed record WriteOffReasonResponse(Guid Id, Guid OrganizationId, string Name, Guid ExpenseCategoryId, bool Active);
public sealed record WriteOffRequest(DateOnly DocumentDate, Guid StorageLocationId, Guid WriteOffReasonId,
    IReadOnlyList<WriteOffLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record UpdateWriteOffRequest(long Version, DateOnly DocumentDate, Guid StorageLocationId, Guid WriteOffReasonId,
    IReadOnlyList<WriteOffLineInput> Lines, string? Comment = null, string? InternalNote = null);
public sealed record WriteOffLineResponse(Guid Id, Guid WriteOffId, int LineNo, Guid BatchId, decimal Quantity);
public sealed record WriteOffResponse(DocumentResponse Document, Guid StorageLocationId, Guid WriteOffReasonId,
    IReadOnlyList<WriteOffLineResponse> Lines);
public sealed record WriteOffFilter(DateOnly? From = null, DateOnly? To = null, Guid? StorageLocationId = null,
    Guid? WriteOffReasonId = null, Guid? ExpenseCategoryId = null, DocumentStatus? Status = null);

internal static class WriteOffResponses
{
    public static WriteOffReasonResponse ToResponse(this WriteOffReason r) => new(r.Id, r.OrganizationId, r.Name, r.ExpenseCategoryId, r.Active);
    public static WriteOffResponse ToResponse(this WriteOff w)
    {
        var d = w.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status,
            d.Comment, d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt,
            d.DeletedBy, d.DeletedAt, d.Version), w.StorageLocationId, w.WriteOffReasonId,
            w.Lines.OrderBy(x => x.LineNo).ThenBy(x => x.Id)
                .Select(x => new WriteOffLineResponse(x.Id, x.WriteOffId, x.LineNo, x.BatchId, x.Quantity)).ToArray());
    }
}
