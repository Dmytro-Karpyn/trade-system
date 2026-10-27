using Trade.Domain.Common;

namespace Trade.Domain.Inventory;

public sealed record WriteOffReasonDetails(string Name, Guid ExpenseCategoryId, bool Active);

public sealed class WriteOffReason
{
    private WriteOffReason() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid ExpenseCategoryId { get; private set; }
    public bool Active { get; private set; }

    public static WriteOffReason Create(Guid organizationId, WriteOffReasonDetails details)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var reason = new WriteOffReason { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        reason.Update(details);
        return reason;
    }

    public void Update(WriteOffReasonDetails details)
    {
        Guard.Against(string.IsNullOrWhiteSpace(details.Name), "Name is required.");
        Guard.RequiredId(details.ExpenseCategoryId, nameof(ExpenseCategoryId));
        Name = details.Name.Trim();
        ExpenseCategoryId = details.ExpenseCategoryId;
        Active = details.Active;
    }
}
