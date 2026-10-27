using Trade.Domain.Common;

namespace Trade.Domain.Inventory;

public sealed class StorageLocation
{
    private StorageLocation() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;

    public static StorageLocation Create(Guid organizationId, string name)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        Guard.Against(string.IsNullOrWhiteSpace(name), "StorageLocation Name is required.");
        return new StorageLocation { Id = Guid.CreateVersion7(), OrganizationId = organizationId, Name = name.Trim() };
    }
}
