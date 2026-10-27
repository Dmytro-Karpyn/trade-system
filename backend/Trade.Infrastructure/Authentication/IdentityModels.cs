using Microsoft.AspNetCore.Identity;

namespace Trade.Infrastructure.Authentication;

public sealed class TradeUser : IdentityUser<Guid>
{
    public TradeUser() { Id = Guid.CreateVersion7(); }
    public Guid OrganizationId { get; set; }
    public string FullName { get; set; } = "";
    public Guid? CounterpartyId { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class TradeRole : IdentityRole<Guid>
{
    public TradeRole() { Id = Guid.CreateVersion7(); }
    public Guid OrganizationId { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
