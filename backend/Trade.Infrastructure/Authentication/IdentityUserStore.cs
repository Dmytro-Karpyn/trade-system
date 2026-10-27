using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trade.Domain.Common;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

// Minimal Identity store: only the capabilities offered in V1, without unused login/claim/token tables.
internal sealed class IdentityUserStore(TradeDbContext db) : IUserPasswordStore<TradeUser>, IUserRoleStore<TradeUser>,
    IUserSecurityStampStore<TradeUser>, IUserLockoutStore<TradeUser>
{
    public void Dispose() { }
    public Task<string> GetUserIdAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.Id.ToString());
    public Task<string?> GetUserNameAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.UserName);
    public Task SetUserNameAsync(TradeUser user, string? name, CancellationToken ct) { user.UserName = name; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.NormalizedUserName);
    public Task SetNormalizedUserNameAsync(TradeUser user, string? name, CancellationToken ct) { user.NormalizedUserName = name; return Task.CompletedTask; }
    public Task<TradeUser?> FindByIdAsync(string id, CancellationToken ct) => Guid.TryParse(id, out var value)
        ? db.Users.SingleOrDefaultAsync(x => x.Id == value, ct) : Task.FromResult<TradeUser?>(null);
    public Task<TradeUser?> FindByNameAsync(string name, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.NormalizedUserName == name, ct);
    public async Task<IdentityResult> CreateAsync(TradeUser user, CancellationToken ct)
    {
        db.Users.Add(user); await db.SaveChangesAsync(ct); return IdentityResult.Success;
    }
    public async Task<IdentityResult> UpdateAsync(TradeUser user, CancellationToken ct)
    {
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        db.Users.Update(user);
        try { await db.SaveChangesAsync(ct); return IdentityResult.Success; }
        catch (DbUpdateConcurrencyException) { return IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Concurrent user change. Retry." }); }
    }
    public Task<IdentityResult> DeleteAsync(TradeUser user, CancellationToken ct) => throw new DomainException("Users cannot be physically deleted.");
    public Task SetPasswordHashAsync(TradeUser user, string? hash, CancellationToken ct) { user.PasswordHash = hash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.PasswordHash != null);
    public Task SetSecurityStampAsync(TradeUser user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(TradeUser user, DateTimeOffset? end, CancellationToken ct) { user.LockoutEnd = end; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(TradeUser user, CancellationToken ct) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(TradeUser user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(TradeUser user, CancellationToken ct) => Task.FromResult(user.LockoutEnabled);
    public Task SetLockoutEnabledAsync(TradeUser user, bool enabled, CancellationToken ct) { user.LockoutEnabled = enabled; return Task.CompletedTask; }
    public async Task AddToRoleAsync(TradeUser user, string normalizedName, CancellationToken ct)
    {
        var role = await db.Roles.SingleAsync(x => x.OrganizationId == user.OrganizationId && x.NormalizedName == normalizedName, ct);
        db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id });
    }
    public async Task RemoveFromRoleAsync(TradeUser user, string normalizedName, CancellationToken ct)
    {
        var ids = db.Roles.Where(x => x.OrganizationId == user.OrganizationId && x.NormalizedName == normalizedName).Select(x => x.Id);
        await db.UserRoles.Where(x => x.UserId == user.Id && ids.Contains(x.RoleId)).ExecuteDeleteAsync(ct);
    }
    public async Task<IList<string>> GetRolesAsync(TradeUser user, CancellationToken ct) =>
        await (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
               where link.UserId == user.Id && role.OrganizationId == user.OrganizationId select role.Name!).ToListAsync(ct);
    public Task<bool> IsInRoleAsync(TradeUser user, string normalizedName, CancellationToken ct) =>
        (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
         where link.UserId == user.Id && role.OrganizationId == user.OrganizationId && role.NormalizedName == normalizedName select role.Id).AnyAsync(ct);
    public async Task<IList<TradeUser>> GetUsersInRoleAsync(string normalizedName, CancellationToken ct) =>
        await (from user in db.Users join link in db.UserRoles on user.Id equals link.UserId
               join role in db.Roles on link.RoleId equals role.Id
               where role.NormalizedName == normalizedName && role.OrganizationId == user.OrganizationId select user).Distinct().ToListAsync(ct);
}
