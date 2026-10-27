using Microsoft.EntityFrameworkCore;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

internal static class StockTransferModelConfiguration
{
    public static void ConfigureStockTransfers(this ModelBuilder model)
    {
        model.Entity<StockTransfer>(entity =>
        {
            entity.ToTable("stock_transfers", table =>
                table.HasCheckConstraint("ck_stock_transfer_locations", "\"FromLocationId\" <> \"ToLocationId\""));
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<StockTransfer>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<StockTransferLine>(entity =>
        {
            entity.ToTable("stock_transfer_lines", table =>
            {
                table.HasCheckConstraint("ck_stock_transfer_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("ck_stock_transfer_line_no", "\"LineNo\" > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            // PostgreSQL adds deferred uniqueness so a DRAFT PUT can swap batches or line numbers.
            entity.HasIndex(x => new { x.StockTransferId, x.BatchId });
            entity.HasIndex(x => new { x.StockTransferId, x.LineNo });
        });
    }
}
