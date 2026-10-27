using Trade.Domain.Common;

namespace Trade.Domain.Documents;

public enum DocumentType { PURCHASE_INVOICE, OPENING_STOCK, SALE_INVOICE, MONEY_RECEIPT, MONEY_PAYMENT, MONEY_TRANSFER, EXPENSE, CUSTOMER_RETURN, SUPPLIER_RETURN, STOCK_TRANSFER, WRITE_OFF }
public enum DocumentStatus { DRAFT, POSTED }

public sealed class Document
{
    private Document() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public string? Number { get; private set; }
    public DateOnly DocumentDate { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? Comment { get; private set; }
    public string? InternalNote { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? PostedBy { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public long Version { get; private set; }

    public static Document Create(Guid organizationId, DocumentType type, DateOnly date,
        string? comment, string? internalNote, Guid actorId, DateTimeOffset now)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        Guard.RequiredId(actorId, nameof(CreatedBy));
        Guard.Against(!Enum.IsDefined(type), "Unsupported DocumentType.");
        Guard.Against(date == default, "DocumentDate is required.");
        return new Document
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, DocumentType = type,
            DocumentDate = date, Comment = comment, InternalNote = internalNote,
            Status = DocumentStatus.DRAFT, CreatedBy = actorId, CreatedAt = UtcTimestamp.Normalize(now),
            UpdatedAt = UtcTimestamp.Normalize(now), Version = 1
        };
    }

    public void RequireDraft() => Guard.Against(Status != DocumentStatus.DRAFT || DeletedAt is not null,
        "Only a nondeleted DRAFT document can be modified or posted.");

    public void Edit(DateOnly date, string? comment, string? internalNote, DateTimeOffset now)
    {
        RequireDraft();
        Guard.Against(date == default, "DocumentDate is required.");
        DocumentDate = date;
        Comment = comment;
        InternalNote = internalNote;
        Touch(now);
    }

    public void Post(string number, Guid actorId, DateTimeOffset now)
    {
        RequireDraft();
        Guard.RequiredId(actorId, nameof(PostedBy));
        Guard.Against(string.IsNullOrWhiteSpace(number), "A document number is required for posting.");
        Number ??= number;
        Status = DocumentStatus.POSTED;
        PostedBy = actorId;
        PostedAt = UtcTimestamp.Normalize(now);
        Touch(now);
    }

    public void Unpost(DateTimeOffset now)
    {
        Guard.Against(Status != DocumentStatus.POSTED, "Only a POSTED document can be unposted.");
        Status = DocumentStatus.DRAFT;
        PostedBy = null;
        PostedAt = null;
        Touch(now);
    }

    public void Delete(Guid actorId, DateTimeOffset now)
    {
        RequireDraft();
        Guard.RequiredId(actorId, nameof(DeletedBy));
        DeletedBy = actorId;
        DeletedAt = UtcTimestamp.Normalize(now);
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = UtcTimestamp.Normalize(now);
        Version = checked(Version + 1);
    }
}

public sealed class DocumentHasDependenciesException() : Exception("DOCUMENT_HAS_DEPENDENCIES");
