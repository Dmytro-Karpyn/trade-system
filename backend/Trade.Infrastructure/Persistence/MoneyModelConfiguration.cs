using Microsoft.EntityFrameworkCore;
using Trade.Domain.Counterparties;
using Trade.Domain.Money;

namespace Trade.Infrastructure.Persistence;

internal static class MoneyModelConfiguration
{
    public static void ConfigureMoney(this ModelBuilder model)
    {
        model.Entity<MoneyAccount>(entity =>
        {
            entity.ToTable("money_accounts", table =>
            {
                table.HasCheckConstraint("ck_money_account_name", "btrim(\"Name\") <> ''");
                table.HasCheckConstraint("ck_money_account_type", "\"AccountType\" IN ('CASH', 'BANK', 'SAFE', 'OTHER')");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.AccountType).HasConversion<string>();
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
        });
        model.Entity<MoneyDocument>(entity =>
        {
            entity.ToTable("money_documents", table =>
            {
                table.HasCheckConstraint("ck_money_purpose", "\"Purpose\" IS NULL OR \"Purpose\" IN ('SETTLEMENT', 'OWNER', 'OPENING_BALANCE')");
                table.HasCheckConstraint("ck_money_amounts", "(\"SourceAmount\" IS NULL OR \"SourceAmount\" > 0) AND " +
                    "(\"DestinationAmount\" IS NULL OR \"DestinationAmount\" > 0) AND (\"SettlementAmount\" IS NULL OR \"SettlementAmount\" > 0)");
                table.HasCheckConstraint("ck_money_settlement_fields", "(COALESCE(\"Purpose\" = 'SETTLEMENT', false) AND " +
                    "\"CounterpartyId\" IS NOT NULL AND \"SettlementCurrencyId\" IS NOT NULL AND \"SettlementAmount\" IS NOT NULL AND " +
                    "\"SettlementExchangeRate\" IS NOT NULL AND \"SettlementExchangeRate\" > 0 AND " +
                    "(\"SettlementCurrencyId\" <> '00000000-0000-0000-0000-000000000980'::uuid OR \"SettlementExchangeRate\" = 1)) OR " +
                    "(NOT COALESCE(\"Purpose\" = 'SETTLEMENT', false) AND \"SettlementCurrencyId\" IS NULL AND " +
                    "\"SettlementAmount\" IS NULL AND \"SettlementExchangeRate\" IS NULL)");
            });
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.Property(x => x.Purpose).HasConversion<string>();
            entity.Property(x => x.SourceAmount).HasPrecision(20, 4);
            entity.Property(x => x.DestinationAmount).HasPrecision(20, 4);
            entity.Property(x => x.SettlementAmount).HasPrecision(20, 4);
            entity.Property(x => x.SettlementExchangeRate).HasPrecision(20, 8);
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<MoneyDocument>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MoneyAccount>().WithMany().HasForeignKey(x => x.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MoneyAccount>().WithMany().HasForeignKey(x => x.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MoneyMovement>(entity =>
        {
            entity.ToTable("money_movements", table => table.HasCheckConstraint("ck_money_movement_amount", "\"Amount\" <> 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.HasOne<MoneyDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MoneyAccount>().WithMany().HasForeignKey(x => x.MoneyAccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.DocumentId, x.MoneyAccountId }).IsUnique();
        });
        model.Entity<MoneyAccountBalanceRow>(entity =>
        {
            entity.HasNoKey().ToView("v_money_account_balance");
            entity.Property(x => x.Balance).HasColumnType("numeric");
        });
    }
}

internal sealed class MoneyAccountBalanceRow
{
    public Guid MoneyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal Balance { get; set; }
}
