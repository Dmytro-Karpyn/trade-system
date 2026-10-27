namespace Trade.Application.Authentication;

public sealed record LoginRequest(string Username, string Password, bool RememberMe = false);
public sealed record CurrentUserResponse(Guid UserId, string Username, string FullName, Guid OrganizationId,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
public sealed record CreateUserRequest(string Username, string Password, string FullName = "", Guid? CounterpartyId = null,
    bool Active = true, IReadOnlyList<Guid>? RoleIds = null);
public sealed record UpdateUserRequest(string Username, string FullName = "", Guid? CounterpartyId = null,
    bool Active = true, IReadOnlyList<Guid>? RoleIds = null, string? Password = null);
public sealed record UserResponse(Guid UserId, string Username, string FullName, Guid OrganizationId, Guid? CounterpartyId,
    bool Active, DateTimeOffset? LastLoginAt, IReadOnlyList<Guid> RoleIds);
public sealed record RoleRequest(string Name, bool Active = true);
public sealed record RolePermissionsRequest(IReadOnlyList<string> PermissionCodes);
public sealed record RoleResponse(Guid Id, Guid OrganizationId, string Name, bool Active, IReadOnlyList<string> Permissions);
