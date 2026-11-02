using Trade.Domain.Common;

namespace Trade.Domain.Catalog;

public sealed class Unit
{
    private Unit() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool Active { get; private set; } = true;

    public static Unit Create(Guid organizationId, string name, bool active = true)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        Guard.Against(string.IsNullOrWhiteSpace(name), "Unit Name is required.");
        return new Unit { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = name.Trim(), Active = active };
    }
    public void Update(string name,bool active) { Guard.Against(string.IsNullOrWhiteSpace(name),"Unit Name is required.");Name=name.Trim();Active=active; }
}
