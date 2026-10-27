using Microsoft.EntityFrameworkCore;
using Trade.Domain.Counterparties;
using Trade.Domain.Expenses;
using Trade.Domain.Purchasing;

namespace Trade.Infrastructure.Persistence;

internal static class ExpenseModelConfiguration
{
    public static void ConfigureExpenses(this ModelBuilder model)
    {
        model.Entity<ExpenseCategory>(entity =>
        {
            entity.ToTable("expense_categories", table =>
            {
                table.HasCheckConstraint("ck_expense_category_name", "btrim(\"Name\") <> ''");
                table.HasCheckConstraint("ck_expense_category_parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
        });
        model.Entity<Expense>(entity =>
        {
            entity.ToTable("expenses", table =>
            {
                table.HasCheckConstraint("ck_expense_amount", "\"Amount\" > 0");
                table.HasCheckConstraint("ck_expense_exchange_rate", "\"ExchangeRate\" > 0");
            });
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.Property(x => x.ExchangeRate).HasPrecision(20, 6);
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<Expense>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
