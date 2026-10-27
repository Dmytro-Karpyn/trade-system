using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trade.Application.Authentication;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

public static class IdentityServices
{
    public static IServiceCollection AddTradeIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<TradeUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddUserStore<IdentityUserStore>().AddSignInManager();
        services.AddScoped<IUserClaimsPrincipalFactory<TradeUser>, TradeClaimsFactory>();
        services.AddScoped<ICurrentPermissions, CurrentPermissions>();
        services.AddScoped<IdentityAdministration>();
        return services;
    }
}

internal sealed class TradeClaimsFactory(IOptions<IdentityOptions> options) : IUserClaimsPrincipalFactory<TradeUser>
{
    public Task<ClaimsPrincipal> CreateAsync(TradeUser user)
    {
        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme, options.Value.ClaimsIdentity.UserNameClaimType,
            options.Value.ClaimsIdentity.RoleClaimType);
        identity.AddClaim(new(options.Value.ClaimsIdentity.UserIdClaimType, user.Id.ToString()));
        identity.AddClaim(new(options.Value.ClaimsIdentity.UserNameClaimType, user.UserName!));
        identity.AddClaim(new(options.Value.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!));
        identity.AddClaim(new("organization_id", user.OrganizationId.ToString()));
        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}

internal sealed class CurrentPermissions(TradeDbContext db) : ICurrentPermissions
{
    public IReadOnlySet<string> Codes { get; private set; } = new HashSet<string>();
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public async Task LoadAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Active, ct);
        if (user is null) return;
        var roles = await (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
                           where link.UserId == id && role.OrganizationId == user.OrganizationId && role.Active
                           select new { role.Id, role.Name }).AsNoTracking().ToArrayAsync(ct);
        var ids = roles.Select(x => x.Id).ToArray();
        Roles = roles.Select(x => x.Name!).Order(StringComparer.Ordinal).ToArray();
        Codes = (await (from link in db.RolePermissions join permission in db.Permissions on link.PermissionId equals permission.Id
                        where ids.Contains(link.RoleId) select permission.Code).Distinct().ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
    }
}
