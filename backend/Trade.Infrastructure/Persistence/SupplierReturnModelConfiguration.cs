using Microsoft.EntityFrameworkCore;
using Trade.Domain.Purchasing;
using Trade.Domain.Returns;

namespace Trade.Infrastructure.Persistence;

internal static class SupplierReturnModelConfiguration
{
    public static void ConfigureSupplierReturns(this ModelBuilder model)
    {
        model.Entity<SupplierReturn>(entity =>
        {
            entity.ToTable("supplier_returns");
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<SupplierReturn>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(x => x.SourcePurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SupplierReturnId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SupplierReturnLine>(entity =>
        {
            entity.ToTable("supplier_return_lines", table => table.HasCheckConstraint("ck_supplier_return_quantity", "\"Quantity\" > 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.HasOne<PurchaseInvoiceLine>().WithMany().HasForeignKey(x => x.SourcePurchaseLineId).OnDelete(DeleteBehavior.Restrict);
            // Deferred uniqueness in PostgreSQL permits swapping two source references within a DRAFT PUT.
            entity.HasIndex(x => new { x.SupplierReturnId, x.SourcePurchaseLineId });
        });
    }
}
