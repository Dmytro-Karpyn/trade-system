using Microsoft.EntityFrameworkCore;
using Trade.Domain.Expenses;
using Trade.Domain.Inventory;

namespace Trade.Infrastructure.Persistence;

internal static class WriteOffModelConfiguration
{
    public static void ConfigureWriteOffs(this ModelBuilder model)
    {
        model.Entity<WriteOffReason>(entity =>
        {
            entity.ToTable("write_off_reasons", table => table.HasCheckConstraint("ck_write_off_reason_name", "btrim(\"Name\") <> ''"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
        });
        model.Entity<WriteOff>(entity =>
        {
            entity.ToTable("write_offs");
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<WriteOff>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WriteOffReason>().WithMany().HasForeignKey(x => x.WriteOffReasonId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.WriteOffId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<WriteOffLine>(entity =>
        {
            entity.ToTable("write_off_lines", table =>
            {
                table.HasCheckConstraint("ck_write_off_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("ck_write_off_line_no", "\"LineNo\" > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            // Deferred uniqueness permits atomically swapping DRAFT line numbers/batches.
            entity.HasIndex(x => new { x.WriteOffId, x.BatchId });
            entity.HasIndex(x => new { x.WriteOffId, x.LineNo });
        });
    }
}
