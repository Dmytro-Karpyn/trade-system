namespace Trade.Application;

public interface IAuditContext { Guid? OrganizationId { get; } Guid? ActorId { get; } }
public sealed record AuditFilter(DateTimeOffset? From=null,DateTimeOffset? To=null,Guid? UserId=null,string? EventType=null,
    string? EntityType=null,Guid? DocumentId=null,string? Search=null,int Skip=0,int Take=100);
public sealed record AuditRow(Guid Id,DateTimeOffset OccurredAt,Guid? ActorId,string? Username,string EventType,string EntityType,
    string EntityId,Guid? DocumentId,string? DocumentNumber,string? DocumentType,string Summary);
public sealed record AuditDetail(AuditRow Entry,object? OldValues,object? NewValues);
public sealed record AuditPage(IReadOnlyList<AuditRow> Items,int Total);
public sealed record AuditLookups(IReadOnlyList<ReportOption> Users,IReadOnlyList<string> EventTypes,IReadOnlyList<string> EntityTypes,IReadOnlyList<ReportOption> Documents);
public interface IAuditQueries
{
    Task<AuditPage> ListAsync(Guid org,AuditFilter filter,CancellationToken ct);
    Task<AuditDetail> DetailAsync(Guid org,Guid id,CancellationToken ct);
    Task<AuditLookups> LookupsAsync(Guid org,CancellationToken ct);
}
