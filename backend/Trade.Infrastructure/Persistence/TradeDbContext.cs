using Microsoft.EntityFrameworkCore;
using Trade.Domain.Expenses;
using Trade.Domain.Returns;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Purchasing;
using Trade.Domain.Sales;
using Trade.Domain.Pricing;
using Trade.Domain.Counterparties;
using Trade.Domain.Settlements;
using Trade.Domain.Money;
using Microsoft.AspNetCore.Identity;
using Trade.Infrastructure.Authentication;
using Trade.Domain.Common;

namespace Trade.Infrastructure.Persistence;

public sealed class TradeDbContext(DbContextOptions<TradeDbContext> options) : DbContext(options)
{
    public DbSet<TradeUser> Users => Set<TradeUser>();
    public DbSet<TradeRole> Roles => Set<TradeRole>();
    public DbSet<IdentityUserRole<Guid>> UserRoles => Set<IdentityUserRole<Guid>>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<CustomerReturnLine> CustomerReturnLines => Set<CustomerReturnLine>();
    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<SupplierReturnLine> SupplierReturnLines => Set<SupplierReturnLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<WriteOffReason> WriteOffReasons => Set<WriteOffReason>();
    public DbSet<WriteOff> WriteOffs => Set<WriteOff>();
    public DbSet<WriteOffLine> WriteOffLines => Set<WriteOffLine>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceLine> SaleInvoiceLines => Set<SaleInvoiceLine>();
    public DbSet<PriceType> PriceTypes => Set<PriceType>();
    public DbSet<BatchPrice> BatchPrices => Set<BatchPrice>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<SettlementMovement> SettlementMovements => Set<SettlementMovement>();
    public DbSet<MoneyAccount> MoneyAccounts => Set<MoneyAccount>();
    public DbSet<MoneyDocument> MoneyDocuments => Set<MoneyDocument>();
    public DbSet<MoneyMovement> MoneyMovements => Set<MoneyMovement>();
    internal DbSet<MoneyAccountBalanceRow> MoneyAccountBalances => Set<MoneyAccountBalanceRow>();
    internal DbSet<CounterpartyBalanceRow> CounterpartyBalances => Set<CounterpartyBalanceRow>();
    internal DbSet<StockBalanceRow> StockBalances => Set<StockBalanceRow>();
    internal DbSet<BatchCostRow> BatchCosts => Set<BatchCostRow>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateCounterpartyDeletes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateCounterpartyDeletes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateCounterpartyDeletes()
    {
        if (ChangeTracker.Entries<TradeUser>().Any(x => x.State == EntityState.Deleted))
            throw new DomainException("Users cannot be physically deleted. Deactivate them instead.");
        foreach (var entry in ChangeTracker.Entries<Counterparty>().Where(x => x.State == EntityState.Deleted))
            entry.Entity.RequirePhysicalDeletionAllowed();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureIdentity();
        model.ConfigureMoney();
        model.ConfigureExpenses();
        model.ConfigureCustomerReturns();
        model.ConfigureSupplierReturns();
        model.ConfigureStockTransfers();
        model.ConfigureWriteOffs();
        model.HasSequence<long>("document_numbers");
        model.Entity<Unit>(entity =>
        {
            entity.ToTable("units", table => table.HasCheckConstraint("ck_units_name", "btrim(\"Name\") <> ''"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        });
        model.Entity<StorageLocation>(entity =>
        {
            entity.ToTable("storage_locations", table => table.HasCheckConstraint("ck_storage_location_name", "btrim(\"Name\") <> ''"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        });
        model.Entity<Product>(entity =>
        {
            entity.ToTable("products", table =>
                table.HasCheckConstraint("ck_products_item_type", "\"ItemType\" IN ('GOODS', 'SERVICE')"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ItemType).HasConversion<string>();
            entity.Property(x => x.Code).IsRequired();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.Attributes).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
        });
        model.Entity<Document>(entity =>
        {
            entity.ToTable("documents", table =>
            {
                table.HasCheckConstraint("ck_documents_type", "\"DocumentType\" IN ('PURCHASE_INVOICE', 'OPENING_STOCK', 'SALE_INVOICE', 'MONEY_RECEIPT', 'MONEY_PAYMENT', 'MONEY_TRANSFER', 'EXPENSE', 'CUSTOMER_RETURN', 'SUPPLIER_RETURN', 'STOCK_TRANSFER', 'WRITE_OFF')");
                table.HasCheckConstraint("ck_documents_status", "\"Status\" IN ('DRAFT', 'POSTED')");
                table.HasCheckConstraint("ck_documents_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_documents_posted_audit",
                    "(\"Status\" = 'DRAFT' AND \"PostedAt\" IS NULL AND \"PostedBy\" IS NULL) OR " +
                    "(\"Status\" = 'POSTED' AND \"Number\" IS NOT NULL AND \"PostedAt\" IS NOT NULL AND \"PostedBy\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.DocumentType).HasConversion<string>();
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.Number }).IsUnique()
                .HasFilter("\"Number\" IS NOT NULL");
        });
        model.Entity<PurchaseInvoice>(entity =>
        {
            entity.ToTable("purchase_invoices", table =>
            {
                table.HasCheckConstraint("ck_purchase_exchange_rate", "\"ExchangeRate\" > 0");
                table.HasCheckConstraint("ck_purchase_discount", "\"DiscountPercent\" BETWEEN 0 AND 100");
            });
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<PurchaseInvoice>(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ExchangeRate).HasPrecision(20, 8);
            entity.Property(x => x.DiscountPercent).HasPrecision(7, 4);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PurchaseInvoiceLine>(entity =>
        {
            entity.ToTable("purchase_invoice_lines", table =>
            {
                table.HasCheckConstraint("ck_purchase_line_no", "\"LineNo\" > 0");
                table.HasCheckConstraint("ck_purchase_line_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("ck_purchase_line_price", "\"UnitPriceCurrency\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.UnitPriceCurrency).HasPrecision(20, 4);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Batch>(entity =>
        {
            entity.ToTable("batches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.BatchNumber).IsRequired();
            entity.HasIndex(x => x.PurchaseInvoiceLineId).IsUnique();
            entity.HasOne<PurchaseInvoiceLine>().WithOne().HasForeignKey<Batch>(x => x.PurchaseInvoiceLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.DocumentId, x.BatchId, x.StorageLocationId }).IsUnique();
        });
        model.Entity<SaleInvoice>(entity =>
        {
            entity.ToTable("sale_invoices", table =>
                table.HasCheckConstraint("ck_sale_discount", "\"DiscountPercent\" BETWEEN 0 AND 100"));
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.DocumentId).ValueGeneratedNever();
            entity.Property(x => x.DiscountPercent).HasPrecision(7, 4);
            entity.HasOne(x => x.Document).WithOne().HasForeignKey<SaleInvoice>(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceType>().WithMany().HasForeignKey(x => x.PriceTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SaleInvoiceLine>(entity =>
        {
            entity.ToTable("sale_invoice_lines", table =>
            {
                table.HasCheckConstraint("ck_sale_line_no", "\"LineNo\" > 0");
                table.HasCheckConstraint("ck_sale_line_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("ck_sale_line_prices", "\"UnitPrice\" >= 0 AND \"RecommendedUnitPrice\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Quantity).HasPrecision(20, 4);
            entity.Property(x => x.RecommendedUnitPrice).HasPrecision(20, 4);
            entity.Property(x => x.UnitPrice).HasPrecision(20, 4);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            // Active-batch uniqueness is a deferred constraint trigger, allowing an atomic swap during PUT.
            entity.HasIndex(x => new { x.SaleInvoiceId, x.BatchId });
        });
        model.Entity<PriceType>(entity =>
        {
            entity.ToTable("price_types", table =>
            {
                table.HasCheckConstraint("ck_price_type_name", "btrim(\"Name\") <> ''");
                table.HasCheckConstraint("ck_price_type_markup", "\"DefaultMarkupPercent\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.DefaultMarkupPercent).HasPrecision(7, 4);
            entity.HasIndex(x => x.OrganizationId);
        });
        model.Entity<BatchPrice>(entity =>
        {
            entity.ToTable("batch_prices", table => table.HasCheckConstraint("ck_batch_price", "\"UnitPrice\" >= 0"));
            entity.HasKey(x => new { x.BatchId, x.PriceTypeId });
            entity.Property(x => x.UnitPrice).HasPrecision(20, 4);
            entity.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceType>().WithMany().HasForeignKey(x => x.PriceTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProductPrice>(entity =>
        {
            entity.ToTable("product_prices", table => table.HasCheckConstraint("ck_product_price", "\"UnitPrice\" >= 0"));
            entity.HasKey(x => new { x.ProductId, x.PriceTypeId });
            entity.Property(x => x.UnitPrice).HasPrecision(20, 4);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PriceType>().WithMany().HasForeignKey(x => x.PriceTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Counterparty>(entity =>
        {
            entity.ToTable("counterparties", table =>
            {
                table.HasCheckConstraint("ck_counterparty_name", "btrim(\"Name\") <> ''");
                table.HasCheckConstraint("ck_counterparty_legal_type", "\"LegalType\" IS NULL OR \"LegalType\" IN ('PERSON', 'ENTREPRENEUR', 'COMPANY')");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.LegalType).HasConversion<string>();
            entity.Property(x => x.Name).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
            entity.HasOne<PriceType>().WithMany().HasForeignKey(x => x.DefaultPriceTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SettlementMovement>(entity =>
        {
            entity.ToTable("settlement_movements", table => table.HasCheckConstraint("ck_settlement_signs",
                "(\"Amount\" >= 0 AND \"BaseAmountUah\" >= 0) OR (\"Amount\" <= 0 AND \"BaseAmountUah\" <= 0)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Amount).HasPrecision(20, 4);
            entity.Property(x => x.BaseAmountUah).HasPrecision(20, 4);
            entity.HasIndex(x => x.DocumentId).IsUnique();
            entity.HasIndex(x => new { x.CounterpartyId, x.CurrencyId });
            entity.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Counterparty>().WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CounterpartyBalanceRow>(entity =>
        {
            entity.HasNoKey().ToView("v_counterparty_balance");
            entity.Property(x => x.Balance).HasColumnType("numeric");
        });
        model.Entity<BatchCostRow>(entity =>
        {
            entity.HasNoKey().ToView("v_batch_cost");
            entity.Property(x => x.UnitCostUah).HasColumnType("numeric");
        });
        model.Entity<StockBalanceRow>(entity =>
        {
            entity.HasNoKey().ToView("v_stock_balance");
            entity.Property(x => x.PhysicalQuantity).HasColumnType("numeric");
            entity.Property(x => x.ReservedQuantity).HasColumnType("numeric");
            entity.Property(x => x.AvailableQuantity).HasColumnType("numeric");
        });
    }
}

internal sealed class CounterpartyBalanceRow
{
    public Guid CounterpartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal Balance { get; set; }
}

internal sealed class BatchCostRow
{
    public Guid BatchId { get; set; }
    public Guid PurchaseInvoiceLineId { get; set; }
    public Guid ProductId { get; set; }
    public decimal UnitCostUah { get; set; }
}

internal sealed class StockBalanceRow
{
    public Guid BatchId { get; set; }
    public Guid StorageLocationId { get; set; }
    public decimal PhysicalQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
}
