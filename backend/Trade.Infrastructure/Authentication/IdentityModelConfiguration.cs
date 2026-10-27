using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trade.Application.Authentication;
using Trade.Domain.Counterparties;

namespace Trade.Infrastructure.Authentication;

internal static class IdentityModelConfiguration
{
    public static void ConfigureIdentity(this ModelBuilder model)
    {
        model.Entity<TradeUser>(e =>
        {
            e.ToTable("users", t => t.HasCheckConstraint("ck_user_username", "btrim(\"UserName\") <> ''"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserName).IsRequired().HasMaxLength(256);
            e.Property(x => x.NormalizedUserName).IsRequired().HasMaxLength(256);
            e.Property(x => x.FullName).IsRequired(); e.Property(x => x.SecurityStamp).IsRequired();
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.HasIndex(x => x.OrganizationId);
            e.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
            // V1 does not offer email/phone verification, MFA, external logins, claims or recovery tokens.
            e.Ignore(x => x.Email); e.Ignore(x => x.NormalizedEmail); e.Ignore(x => x.EmailConfirmed);
            e.Ignore(x => x.PhoneNumber); e.Ignore(x => x.PhoneNumberConfirmed); e.Ignore(x => x.TwoFactorEnabled);
        });
        model.Entity<TradeRole>(e =>
        {
            e.ToTable("roles", t => t.HasCheckConstraint("ck_role_name", "btrim(\"Name\") <> ''"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(256);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(x => new { x.OrganizationId, x.NormalizedName }).IsUnique();
        });
        model.Entity<IdentityUserRole<Guid>>(e =>
        {
            e.ToTable("user_roles"); e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne<TradeUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TradeRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Permission>(e =>
        {
            e.ToTable("permissions"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).IsRequired(); e.Property(x => x.Description).IsRequired(); e.HasIndex(x => x.Code).IsUnique();
            e.HasData(PermissionCatalog.All.Select(x => new Permission { Id = x.Id, Code = x.Code, Description = x.Description }));
        });
        model.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions"); e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne<TradeRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
