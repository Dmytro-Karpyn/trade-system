using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Trade.Application.Authentication;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

public static class DevelopmentIdentitySeed
{
    public static readonly Guid DemoOrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task RunAsync(IServiceProvider services, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct = default)
    {
        // No production bootstrap, and no built-in password. The organization is a GUID context, not a new business table.
        if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(configuration["DevelopmentSeed:AdminPassword"])) return;
        var db = (TradeDbContext)services.GetService(typeof(TradeDbContext))!;
        var manager = (UserManager<TradeUser>)services.GetService(typeof(UserManager<TradeUser>))!;
        var org = Guid.TryParse(configuration["DevelopmentSeed:OrganizationId"], out var configured) && configured != Guid.Empty ? configured : DemoOrganizationId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(812341902)", ct);
        var roles = await db.Roles.Where(x => x.OrganizationId == org).ToListAsync(ct);
        foreach (var name in PermissionCatalog.InitialRoles)
            if (!roles.Any(x => x.NormalizedName == manager.NormalizeName(name)))
            {
                var role = new TradeRole { OrganizationId = org, Name = name, NormalizedName = manager.NormalizeName(name) };
                db.Roles.Add(role); roles.Add(role);
            }
        await db.SaveChangesAsync(ct);
        var permissionIds = await db.Permissions.Select(x => x.Id).ToArrayAsync(ct);
        foreach (var role in roles.Where(x => x.NormalizedName is "OWNER" or "ADMIN"))
        {
            var existing = await db.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToArrayAsync(ct);
            db.RolePermissions.AddRange(permissionIds.Except(existing).Select(id => new RolePermission { RoleId = role.Id, PermissionId = id }));
        }
        var user = await manager.FindByNameAsync("admin");
        if (user is null)
        {
            user = new TradeUser { OrganizationId = org, UserName = "admin", FullName = "Demo Administrator" };
            IdentityAdministration.RequireSuccess(await manager.CreateAsync(user, configuration["DevelopmentSeed:AdminPassword"]!));
            db.UserRoles.Add(new() { UserId = user.Id, RoleId = roles.Single(x => x.NormalizedName == "OWNER").Id });
        }
        else if (user.OrganizationId != org) throw new InvalidOperationException("Development admin already belongs to another organization.");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
