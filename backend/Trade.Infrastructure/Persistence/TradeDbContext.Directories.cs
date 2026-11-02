using Microsoft.EntityFrameworkCore;
using Trade.Domain.Catalog;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

public sealed partial class TradeDbContext
{
    private static void ConfigureDirectoriesAndAudit(ModelBuilder model)
    {
        model.Entity<Unit>().Property(x=>x.Active).HasDefaultValue(true);
        model.Entity<StorageLocation>().Property(x=>x.Active).HasDefaultValue(true);
        model.Entity<ProductCategory>(e=> {
            e.ToTable("product_categories",t=>t.HasCheckConstraint("ck_product_category_name","btrim(\"Name\") <> ''"));
            e.HasKey(x=>x.Id);e.Property(x=>x.Id).ValueGeneratedNever();e.Property(x=>x.Name).IsRequired();
            e.HasIndex(x=>new{x.OrganizationId,x.ParentId,x.Name}).IsUnique().AreNullsDistinct(false);
            e.HasOne<ProductCategory>().WithMany().HasForeignKey(x=>x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AuditEntry>(e=> {
            e.ToTable("audit_logs");e.HasKey(x=>x.Id);e.Property(x=>x.Id).ValueGeneratedNever();
            e.Property(x=>x.OldValues).HasColumnType("jsonb");e.Property(x=>x.NewValues).HasColumnType("jsonb");
            e.HasIndex(x=>new{x.OrganizationId,x.OccurredAt,x.Id});e.HasIndex(x=>new{x.OrganizationId,x.ActorId,x.OccurredAt});e.HasIndex(x=>new{x.OrganizationId,x.DocumentId});
        });
    }
}
