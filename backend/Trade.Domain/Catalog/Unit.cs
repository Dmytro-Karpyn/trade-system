using Trade.Domain.Common;

namespace Trade.Domain.Catalog;

public sealed class Unit
{
    private Unit() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;

    public static Unit Create(Guid organizationId, string name)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        Guard.Against(string.IsNullOrWhiteSpace(name), "Unit Name is required.");
        return new Unit { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = name.Trim() };
    }
}
