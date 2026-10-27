using Microsoft.EntityFrameworkCore;
using Trade.Domain.Returns;
using Trade.Domain.Sales;

namespace Trade.Infrastructure.Persistence;

internal static class CustomerReturnModelConfiguration
{
    public static void ConfigureCustomerReturns(this ModelBuilder model)
    {
        model.Entity<CustomerReturn>(entity =>
        {
            entity.ToTable("customer_returns");
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<CustomerReturn>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SaleInvoice>().WithMany().HasForeignKey(x => x.SourceSaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.CustomerReturnId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CustomerReturnLine>(entity =>
        {
            entity.ToTable("customer_return_lines", table =>
            {
                table.HasCheckConstraint("ck_customer_return_quantity", "\"Quantity\" >= 0");
                table.HasCheckConstraint("ck_customer_return_refund", "\"RefundAmount\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.RefundAmount).HasPrecision(20, 4);
            entity.HasOne<SaleInvoiceLine>().WithMany().HasForeignKey(x => x.SourceSaleLineId).OnDelete(DeleteBehavior.Restrict);
            // Uniqueness is deferred in PostgreSQL so a DRAFT PUT can swap two source references atomically.
            entity.HasIndex(x => new { x.CustomerReturnId, x.SourceSaleLineId });
        });
    }
}
