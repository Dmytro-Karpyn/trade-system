using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trade.Application.Authentication;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Authentication;

// Operator-only, one-shot CLI. This is never called by normal application startup or migrations.
public static class ProductionAdminBootstrap
{
    public static async Task RunAsync(IServiceProvider services, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!environment.IsProduction() && !environment.IsStaging())
            throw new InvalidOperationException("Admin bootstrap requires Production or Staging and an explicit CLI command.");
        var name = configuration["BootstrapAdmin:Username"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password) || password.Length < 16 ||
            password is "Test123!Test" or "trade_local_password" || password.StartsWith("replace-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BootstrapAdmin requires an explicit username and a unique password of at least 16 characters; values are not logged.");
        if (!Guid.TryParse(configuration["BootstrapAdmin:OrganizationId"], out var organizationId) || organizationId == Guid.Empty)
            throw new InvalidOperationException("BootstrapAdmin__OrganizationId must be an explicit nonempty GUID.");
        var db = services.GetRequiredService<TradeDbContext>();
        var users = services.GetRequiredService<UserManager<TradeUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(812341903)", ct);
        if (await db.Users.AnyAsync(x => x.OrganizationId == organizationId, ct) || await users.FindByNameAsync(name) is not null)
            throw new InvalidOperationException("Bootstrap refuses existing users. Use the current administrator; passwords and roles will not be overwritten.");
        var owner = await db.Roles.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.NormalizedName == "OWNER", ct);
        if (owner is null)
        {
            owner = new TradeRole { OrganizationId = organizationId, Name = "OWNER", NormalizedName = "OWNER" };
            db.Roles.Add(owner);
        }
        if (!owner.Active) throw new InvalidOperationException("Bootstrap refuses an inactive existing OWNER role.");
        await db.SaveChangesAsync(ct);
        var permissions = await db.Permissions.Select(x => x.Id).ToArrayAsync(ct);
        if (permissions.Length == 0) throw new InvalidOperationException("Apply migrations before bootstrapping an administrator.");
        var granted = await db.RolePermissions.Where(x => x.RoleId == owner.Id).Select(x => x.PermissionId).ToArrayAsync(ct);
        db.RolePermissions.AddRange(permissions.Except(granted).Select(id => new RolePermission { RoleId = owner.Id, PermissionId = id }));
        var user = new TradeUser { OrganizationId = organizationId, UserName = name, FullName = configuration["BootstrapAdmin:FullName"]?.Trim() ?? "Administrator" };
        IdentityAdministration.RequireSuccess(await users.CreateAsync(user, password));
        db.UserRoles.Add(new() { UserId = user.Id, RoleId = owner.Id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
