using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Common;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

public sealed class IdentityAdministration(TradeDbContext db, UserManager<TradeUser> users, ILookupNormalizer normalizer,IAuditContext? audit=null)
{
    public async Task<IReadOnlyList<UserResponse>> ListUsersAsync(Guid org, CancellationToken ct) =>
        await MapUsersAsync(await db.Users.AsNoTracking().Where(x => x.OrganizationId == org)
            .OrderBy(x => x.UserName).ToArrayAsync(ct), ct);

    private async Task<IReadOnlyList<UserResponse>> MapUsersAsync(TradeUser[] found, CancellationToken ct)
    {
        var ids = found.Select(x => x.Id).ToArray();
        var links = await db.UserRoles.AsNoTracking().Where(x => ids.Contains(x.UserId)).ToArrayAsync(ct);
        var orgs=found.Select(x=>x.OrganizationId).Distinct().ToArray();
        var roles=await db.Roles.AsNoTracking().Where(x=>orgs.Contains(x.OrganizationId)).ToDictionaryAsync(x=>x.Id,x=>x.Name!,ct);
        var parties=await db.Counterparties.AsNoTracking().Where(x=>orgs.Contains(x.OrganizationId)).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        return found.Select(x => {var roleIds=links.Where(l=>l.UserId==x.Id).Select(l=>l.RoleId).ToArray();return Map(x,roleIds) with {RoleNames=roleIds.Select(id=>roles.GetValueOrDefault(id,"Role")).Order().ToArray(),CounterpartyName=x.CounterpartyId is { } party?parties.GetValueOrDefault(party):null};}).ToArray();
    }

    public async Task<UserResponse> GetUserAsync(Guid org, Guid id, CancellationToken ct) =>
        (await MapUsersAsync([await UserAsync(org, id, false, ct)], ct))[0];

    public async Task<UserResponse> CreateUserAsync(Guid org, CreateUserRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAdministration(org,ct);
        await ValidateLinksAsync(org, input.CounterpartyId, input.RoleIds ?? [], ct);
        var user = new TradeUser { OrganizationId = org, UserName = Username(input.Username), FullName = input.FullName.Trim(),
            CounterpartyId = input.CounterpartyId, Active = input.Active };
        RequireSuccess(await users.CreateAsync(user, input.Password));
        AddRoles(user.Id, input.RoleIds ?? []);
        Audit(org,"CREATE","User",user.Id,input.Username,null,UserValues(user,input.RoleIds??[]));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await GetUserAsync(org, user.Id, ct);
    }

    public async Task<UserResponse> UpdateUserAsync(Guid org, Guid id, UpdateUserRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAdministration(org,ct);var hadAdmin=await HasAdministrator(org,ct);
        var user = await UserAsync(org, id, true, ct);
        var before=UserValues(user,await db.UserRoles.Where(x=>x.UserId==id).Select(x=>x.RoleId).ToArrayAsync(ct));
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
        Audit(org,"UPDATE","User",id,input.Username,before,UserValues(user,input.RoleIds??[]));
        await db.SaveChangesAsync(ct);await KeepAdministrator(org,hadAdmin,ct); await tx.CommitAsync(ct);
        return await GetUserAsync(org, id, ct);
    }

    public async Task<UserResponse> SetActiveAsync(Guid org, Guid id, bool active, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAdministration(org,ct);var hadAdmin=await HasAdministrator(org,ct);
        var user = await UserAsync(org, id, true, ct);
        var before=new {user.Active};
        user.Active = active; user.SecurityStamp = Guid.NewGuid().ToString();
        Audit(org,active?"ACTIVATE":"DEACTIVATE","User",id,user.UserName!,before,new {user.Active});
        RequireSuccess(await users.UpdateAsync(user));await KeepAdministrator(org,hadAdmin,ct); await tx.CommitAsync(ct);
        return await GetUserAsync(org, id, ct);
    }

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(Guid org, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name).ToArrayAsync(ct);
        var ids=roles.Select(x=>x.Id).ToArray();
        var permissions=await (from link in db.RolePermissions join p in db.Permissions on link.PermissionId equals p.Id where ids.Contains(link.RoleId) select new {link.RoleId,p.Code}).ToListAsync(ct);
        var counts=await db.UserRoles.Where(x=>ids.Contains(x.RoleId)).GroupBy(x=>x.RoleId).Select(g=>new {Id=g.Key,Count=g.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,ct);
        return roles.Select(x=>new RoleResponse(x.Id,x.OrganizationId,x.Name!,x.Active,permissions.Where(p=>p.RoleId==x.Id).Select(p=>p.Code).Order().ToArray(),counts.GetValueOrDefault(x.Id))).ToArray();
    }
    public async Task<RoleResponse> GetRoleAsync(Guid org, Guid id, CancellationToken ct) => await MapRoleAsync(await RoleAsync(org, id, false, ct), ct);
    public async Task<RoleResponse> CreateRoleAsync(Guid org, RoleRequest input, CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);await LockAdministration(org,ct);
        var name = RoleName(input.Name);
        var role = new TradeRole { OrganizationId = org, Name = name, NormalizedName = normalizer.NormalizeName(name), Active = input.Active };
        db.Roles.Add(role);Audit(org,"CREATE","Role",role.Id,name,null,new {Name=name,input.Active}); await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return await MapRoleAsync(role, ct);
    }
    public async Task<RoleResponse> UpdateRoleAsync(Guid org, Guid id, RoleRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAdministration(org,ct);var hadAdmin=await HasAdministrator(org,ct);
        var role = await RoleAsync(org, id, true, ct);
        var before=new {role.Name,role.Active};
        role.Name = RoleName(input.Name); role.NormalizedName = normalizer.NormalizeName(role.Name); role.Active = input.Active;
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        Audit(org,"UPDATE","Role",id,role.Name!,before,new {role.Name,role.Active});
        await db.SaveChangesAsync(ct);await KeepAdministrator(org,hadAdmin,ct); await tx.CommitAsync(ct);
        return await MapRoleAsync(role, ct);
    }
    public async Task<RoleResponse> SetPermissionsAsync(Guid org, Guid id, RolePermissionsRequest input, CancellationToken ct)
    {
        if (input.PermissionCodes.Any(x => !PermissionCatalog.Codes.Contains(x))) throw new DomainException("Unknown permission code.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAdministration(org,ct);var hadAdmin=await HasAdministrator(org,ct);
        var role = await RoleAsync(org, id, true, ct);
        if(!role.Active)throw new DomainException("ROLE_INACTIVE");
        var before=(await MapRoleAsync(role,ct)).Permissions;
        var codes = input.PermissionCodes.Distinct().ToArray();
        var ids = await db.Permissions.Where(x => codes.Contains(x.Code)).Select(x => x.Id).ToArrayAsync(ct);
        await db.RolePermissions.Where(x => x.RoleId == id).ExecuteDeleteAsync(ct);
        db.RolePermissions.AddRange(ids.Select(p => new RolePermission { RoleId = id, PermissionId = p }));
        Audit(org,"PERMISSIONS","Role",id,role.Name!,new {Permissions=before},new {Permissions=codes});
        await db.SaveChangesAsync(ct);await KeepAdministrator(org,hadAdmin,ct); await tx.CommitAsync(ct);
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
               where link.RoleId == role.Id orderby permission.Code select permission.Code).ToArrayAsync(ct),await db.UserRoles.CountAsync(x=>x.RoleId==role.Id,ct));
    public async Task<UserLookups> LookupsAsync(Guid org,CancellationToken ct)=>new(await db.Roles.AsNoTracking().Where(x=>x.OrganizationId==org).OrderBy(x=>x.Name).Select(x=>new UserRoleOption(x.Id,x.Name!,x.Active)).ToListAsync(ct),
        await db.Counterparties.AsNoTracking().Where(x=>x.OrganizationId==org).OrderByDescending(x=>x.IsEmployee).ThenBy(x=>x.Name).Select(x=>new UserPartyOption(x.Id,x.Name,x.IsEmployee)).ToListAsync(ct));
    private Task LockAdministration(Guid org,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({org.ToString()}, 83933))",ct);
    private Task<bool> HasAdministrator(Guid org,CancellationToken ct)=>(from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id join rp in db.RolePermissions on r.Id equals rp.RoleId join p in db.Permissions on rp.PermissionId equals p.Id
        where u.OrganizationId==org && r.OrganizationId==org && u.Active && r.Active && (p.Code=="users.manage"||p.Code=="roles.manage") select u.Id).AnyAsync(ct);
    private async Task KeepAdministrator(Guid org,bool before,CancellationToken ct) { if(before&&!await HasAdministrator(org,ct))throw new ConflictException("LAST_ADMIN_REQUIRED"); }
    private static object UserValues(TradeUser u,IReadOnlyList<Guid> roles)=>new {Username=u.UserName,u.FullName,u.CounterpartyId,u.Active,RoleIds=roles};
    private void Audit(Guid org,string type,string entity,Guid id,string summary,object? before,object? after) { if(audit?.ActorId is { } actor)db.AuditEntries.Add(AuditEntry.Create(org,actor,type,entity,id.ToString(),null,summary,before,after)); }
    private static UserResponse Map(TradeUser user, IReadOnlyList<Guid> ids) => new(user.Id, user.UserName!, user.FullName, user.OrganizationId, user.CounterpartyId, user.Active, user.LastLoginAt, ids);
    private static string Username(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256 ? value.Trim() : throw new DomainException("Username is required (maximum 256 characters).");
    private static string RoleName(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256 ? value.Trim() : throw new DomainException("Role name is required (maximum 256 characters).");
    public static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new DomainException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}
