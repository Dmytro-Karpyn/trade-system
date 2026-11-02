using Trade.Domain.Documents;

namespace Trade.Application;

public sealed record ReturnFilter(DateOnly? From = null, DateOnly? To = null, Guid? CounterpartyId = null,
    DocumentStatus? Status = null, Guid? SourceId = null);
public sealed record ReturnListItem(Guid Id, string? Number, DateOnly DocumentDate, Guid CounterpartyId,
    string CounterpartyName, Guid SourceId, string? SourceNumber, Guid CurrencyId, decimal TotalRefund, DocumentStatus Status);
public sealed record ReturnSourceOption(Guid Id, string? Number, DateOnly DocumentDate, Guid CounterpartyId,
    string CounterpartyName, Guid StorageLocationId, Guid CurrencyId, decimal ExchangeRate, decimal DiscountPercent);
public sealed record ReturnProductOption(Guid ProductId, string Name);
public sealed record ReturnBatchOption(Guid BatchId, string BatchNumber);
public sealed record ReturnLookups(IReadOnlyList<ReturnSourceOption> Sources, IReadOnlyList<StorageLocationResponse> Locations,
    IReadOnlyList<ReturnProductOption> Products, IReadOnlyList<ReturnBatchOption> Batches);
public sealed record ReturnPreviewLine(Guid SourceLineId, decimal RefundAmount);
public sealed record ReturnPreview(decimal TotalRefund, IReadOnlyList<ReturnPreviewLine> Lines);
public interface IReturnQueries
{
    Task<ReturnLookups> LookupsAsync(Guid organizationId, bool supplier, CancellationToken ct);
    Task<IReadOnlyList<ReturnListItem>> ListAsync(Guid organizationId, bool supplier, ReturnFilter filter, CancellationToken ct);
    Task<ReturnPreview> SupplierRefundAsync(Guid organizationId, Guid id, CancellationToken ct);
    Task<ReturnPreview> CustomerRefundAsync(Guid organizationId, Guid id, CancellationToken ct);
}
