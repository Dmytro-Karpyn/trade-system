using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SalePersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Schema_preserves_exact_sale_fields_without_reservation_or_currency_fields()
    {
        await using var api = await fixture.CreateApiAsync();
        var tables = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name
            """).ToListAsync());
        Assert.Equal(new[] { "__EFMigrationsHistory", "batches", "documents", "products", "purchase_invoice_lines",
            "purchase_invoices", "sale_invoice_lines", "sale_invoices", "stock_movements", "price_types",
            "batch_prices", "product_prices", "counterparties", "settlement_movements", "money_accounts", "money_documents", "money_movements",
            "expense_categories", "expenses", "customer_returns", "customer_return_lines", "supplier_returns", "supplier_return_lines",
            "stock_transfers", "stock_transfer_lines", "write_off_reasons", "write_offs", "write_off_lines", "users", "roles", "user_roles", "permissions", "role_permissions", "units", "storage_locations" }.Order(), tables.Order());
        foreach (var (table, expected) in new[]
        {
            ("sale_invoices", new[] { "DocumentId", "CounterpartyId", "StorageLocationId", "PriceTypeId", "DiscountPercent" }),
            ("sale_invoice_lines", new[] { "Id", "SaleInvoiceId", "LineNo", "ProductId", "BatchId", "Quantity", "RecommendedUnitPrice", "UnitPrice", "RemovedAt" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = {table} ORDER BY ordinal_position
                """).ToListAsync());
            Assert.Equal(expected, columns);
        }
        var decimals = await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'sale_invoice_lines'
              AND column_name IN ('Quantity', 'RecommendedUnitPrice', 'UnitPrice')
              AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync());
        Assert.Equal(3, decimals);
    }

    [Fact]
    public async Task Database_deferred_constraint_rejects_duplicate_active_batches_at_commit()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(10m, a.ProductId, a.LocationId);
        var sale = await api.CreateSaleAsync(api.NewInput(a.LocationId,
            [new(null, 1, a.ProductId, a.BatchId, 2m, 1m), new(null, 2, a.ProductId, b.BatchId, 2m, 1m)]));
        await api.WithDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE sale_invoice_lines SET "BatchId" = {a.BatchId} WHERE "Id" = {sale.Lines[1].Id}
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal("23514", error.SqlState);
        });
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(b.BatchId, current.Lines[1].BatchId);
    }

    [Fact]
    public async Task Failed_negative_movement_rolls_back_POST_and_preserves_the_draft_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_sale_issue() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected issue failure' USING ERRCODE = '23514'; END;
            $$;
            CREATE TRIGGER fail_sale_issue BEFORE INSERT ON stock_movements
            FOR EACH ROW WHEN (NEW."Quantity" < 0) EXECUTE FUNCTION fail_sale_issue();
            """));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null)).StatusCode);
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(sale.Document, current.Document);
        Assert.Equal(DocumentStatus.DRAFT, current.Document.Status);
        var balance = await api.BalanceAsync(stock);
        Assert.Equal((10m, 4m, 6m), (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
    }
}
