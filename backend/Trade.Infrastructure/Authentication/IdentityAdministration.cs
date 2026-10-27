using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Common;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

public sealed class IdentityAdministration(TradeDbContext db, UserManager<TradeUser> users, ILookupNormalizer normalizer)
{
    public async Task<IReadOnlyList<UserResponse>> ListUsersAsync(Guid org, CancellationToken ct) =>
        await MapUsersAsync(await db.Users.AsNoTracking().Where(x => x.OrganizationId == org)
            .OrderBy(x => x.UserName).ToArrayAsync(ct), ct);

    private async Task<IReadOnlyList<UserResponse>> MapUsersAsync(TradeUser[] found, CancellationToken ct)
    {
        var ids = found.Select(x => x.Id).ToArray();
        var links = await db.UserRoles.AsNoTracking().Where(x => ids.Contains(x.UserId)).ToArrayAsync(ct);
        return found.Select(x => Map(x, links.Where(l => l.UserId == x.Id).Select(l => l.RoleId).ToArray())).ToArray();
    }

    public async Task<UserResponse> GetUserAsync(Guid org, Guid id, CancellationToken ct) =>
        (await MapUsersAsync([await UserAsync(org, id, false, ct)], ct))[0];

    public async Task<UserResponse> CreateUserAsync(Guid org, CreateUserRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ValidateLinksAsync(org, input.CounterpartyId, input.RoleIds ?? [], ct);
        var user = new TradeUser { OrganizationId = org, UserName = Username(input.Username), FullName = input.FullName.Trim(),
            CounterpartyId = input.CounterpartyId, Active = input.Active };
        RequireSuccess(await users.CreateAsync(user, input.Password));
        AddRoles(user.Id, input.RoleIds ?? []);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetUserAsync(org, user.Id, ct);
    }

    public async Task<UserResponse> UpdateUserAsync(Guid org, Guid id, UpdateUserRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await UserAsync(org, id, true, ct);
        await ValidateLinksAsync(org, input.CounterpartyId, input.RoleIds ?? [], ct);
        user.UserName = Username(input.Username); user.FullName = input.FullName.Trim();
        user.CounterpartyId = input.CounterpartyId; user.Active = input.Active;
        // Revoke existing sessions on credential/activation changes. Permissions themselves are read each request.
        user.SecurityStamp = Guid.NewGuid().ToString();
        if (input.Password is not null)
        {
            foreach (var validator in users.PasswordValidators)
                RequireSuccess(await validator.ValidateAsync(users, user, input.Password));
            user.PasswordHash = users.PasswordHasher.HashPassword(user, input.Password);
        }
        RequireSuccess(await users.UpdateAsync(user));
        await db.UserRoles.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        AddRoles(id, input.RoleIds ?? []);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetUserAsync(org, id, ct);
    }

    public async Task<UserResponse> SetActiveAsync(Guid org, Guid id, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await UserAsync(org, id, true, ct);
        user.Active = active; user.SecurityStamp = Guid.NewGuid().ToString();
        RequireSuccess(await users.UpdateAsync(user)); await tx.CommitAsync(ct);
        return await GetUserAsync(org, id, ct);
    }

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(Guid org, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name).ToArrayAsync(ct);
        var result = new List<RoleResponse>();
        foreach (var role in roles) result.Add(await MapRoleAsync(role, ct));
        return result;
    }
    public async Task<RoleResponse> GetRoleAsync(Guid org, Guid id, CancellationToken ct) => await MapRoleAsync(await RoleAsync(org, id, false, ct), ct);
    public async Task<RoleResponse> CreateRoleAsync(Guid org, RoleRequest input, CancellationToken ct)
    {
        var name = RoleName(input.Name);
        var role = new TradeRole { OrganizationId = org, Name = name, NormalizedName = normalizer.NormalizeName(name), Active = input.Active };
        db.Roles.Add(role); await db.SaveChangesAsync(ct);
        return await MapRoleAsync(role, ct);
    }
    public async Task<RoleResponse> UpdateRoleAsync(Guid org, Guid id, RoleRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var role = await RoleAsync(org, id, true, ct);
        role.Name = RoleName(input.Name); role.NormalizedName = normalizer.NormalizeName(role.Name); role.Active = input.Active;
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await MapRoleAsync(role, ct);
    }
    public async Task<RoleResponse> SetPermissionsAsync(Guid org, Guid id, RolePermissionsRequest input, CancellationToken ct)
    {
        if (input.PermissionCodes.Any(x => !PermissionCatalog.Codes.Contains(x))) throw new DomainException("Unknown permission code.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var role = await RoleAsync(org, id, true, ct);
        var codes = input.PermissionCodes.Distinct().ToArray();
        var ids = await db.Permissions.Where(x => codes.Contains(x.Code)).Select(x => x.Id).ToArrayAsync(ct);
        await db.RolePermissions.Where(x => x.RoleId == id).ExecuteDeleteAsync(ct);
        db.RolePermissions.AddRange(ids.Select(p => new RolePermission { RoleId = id, PermissionId = p }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await MapRoleAsync(role, ct);
    }

    private async Task ValidateLinksAsync(Guid org, Guid? counterparty, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        if (org == Guid.Empty) throw new DomainException("Organization is required.");
        if (counterparty is not null && !await db.Counterparties.AnyAsync(x => x.Id == counterparty && x.OrganizationId == org, ct))
            throw new DomainException("Counterparty must belong to the user's organization.");
        foreach (var id in roleIds.Distinct().Order())
            if ((await db.Roles.FromSqlInterpolated($"SELECT * FROM roles WHERE \"Id\" = {id} AND \"OrganizationId\" = {org} FOR SHARE").ToArrayAsync(ct)).Length != 1)
                throw new DomainException("Every role must belong to the user's organization.");
    }
    private void AddRoles(Guid id, IReadOnlyList<Guid> roleIds) => db.UserRoles.AddRange(roleIds.Distinct().Select(r => new IdentityUserRole<Guid> { UserId = id, RoleId = r }));
    private async Task<TradeUser> UserAsync(Guid org, Guid id, bool locked, CancellationToken ct) => locked
        ? (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {id} AND \"OrganizationId\" = {org} FOR UPDATE").ToArrayAsync(ct)).SingleOrDefault() ?? throw new NotFoundException("User")
        : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org, ct) ?? throw new NotFoundException("User");
    private async Task<TradeRole> RoleAsync(Guid org, Guid id, bool locked, CancellationToken ct) => locked
        ? (await db.Roles.FromSqlInterpolated($"SELECT * FROM roles WHERE \"Id\" = {id} AND \"OrganizationId\" = {org} FOR UPDATE").ToArrayAsync(ct)).SingleOrDefault() ?? throw new NotFoundException("Role")
        : await db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == org, ct) ?? throw new NotFoundException("Role");
    private async Task<RoleResponse> MapRoleAsync(TradeRole role, CancellationToken ct) => new(role.Id, role.OrganizationId, role.Name!, role.Active,
        await (from link in db.RolePermissions join permission in db.Permissions on link.PermissionId equals permission.Id
               where link.RoleId == role.Id orderby permission.Code select permission.Code).ToArrayAsync(ct));
    private static UserResponse Map(TradeUser user, IReadOnlyList<Guid> ids) => new(user.Id, user.UserName!, user.FullName, user.OrganizationId, user.CounterpartyId, user.Active, user.LastLoginAt, ids);
    private static string Username(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256 ? value.Trim() : throw new DomainException("Username is required (maximum 256 characters).");
    private static string RoleName(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256 ? value.Trim() : throw new DomainException("Role name is required (maximum 256 characters).");
    public static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new DomainException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
