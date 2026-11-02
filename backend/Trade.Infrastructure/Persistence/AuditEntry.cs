using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trade.Infrastructure.Persistence;

public sealed class AuditEntry
{
    private static readonly JsonSerializerOptions Json=new() { Converters={new JsonStringEnumConverter()} };
    private AuditEntry() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string EventType { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public Guid? DocumentId { get; private set; }
    public string Summary { get; private set; } = null!;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public static AuditEntry Create(Guid org,Guid? actor,string eventType,string entityType,string entityId,Guid? documentId,string summary,object? before,object? after) => new()
    {
        Id=Guid.CreateVersion7(),OrganizationId=org,ActorId=actor,OccurredAt=DateTimeOffset.UtcNow,EventType=eventType,
        EntityType=entityType,EntityId=entityId,DocumentId=documentId,Summary=summary,
        OldValues=before is null ? null : JsonSerializer.Serialize(before,Json),NewValues=after is null ? null : JsonSerializer.Serialize(after,Json)
    };
}
