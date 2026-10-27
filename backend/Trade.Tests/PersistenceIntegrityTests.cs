using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Inventory;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PersistenceIntegrityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Database_prevents_duplicate_batches_and_changing_a_batched_lines_product()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var other = await api.CreateProductAsync();
        await api.PostAsync(p.Document.Id);
        await api.UnpostAsync(p.Document.Id);
        await api.WithDbAsync(async db =>
        {
            db.Batches.Add(Batch.Create(p.Lines[0].Id, DateTimeOffset.UtcNow));
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        });
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE purchase_invoice_lines SET "ProductId" = {other.Id} WHERE "Id" = {p.Lines[0].Id}
                """));
            Assert.Equal("23514", error.SqlState);
        });
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Fact]
    public async Task Failed_movement_insert_rolls_back_document_batch_and_all_other_writes()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_test_movement() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected persistence failure' USING ERRCODE = '23514'; END;
            $$;
            CREATE TRIGGER fail_test_movement BEFORE INSERT ON stock_movements
                FOR EACH ROW EXECUTE FUNCTION fail_test_movement();
            """));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/purchases/{p.Document.Id}/post", null)).StatusCode);
        var unchanged = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{p.Document.Id}"));
        Assert.Equal(p.Document, unchanged.Document);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER fail_test_movement ON stock_movements;
            DROP FUNCTION fail_test_movement();
            """));
        await api.PostAsync(p.Document.Id);
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Schema_has_exact_batch_movement_and_view_columns_and_required_precision()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("batches", new[] { "Id", "PurchaseInvoiceLineId", "BatchNumber", "CreatedAt" }),
            ("stock_movements", new[] { "Id", "DocumentId", "BatchId", "StorageLocationId", "Quantity" }),
            ("v_stock_balance", new[] { "BatchId", "StorageLocationId", "PhysicalQuantity", "ReservedQuantity", "AvailableQuantity" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = {table} ORDER BY ordinal_position
                """).ToListAsync());
            Assert.Equal(expected, columns);
        }
        var numericColumns = await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND numeric_precision = 20 AND numeric_scale = 4
              AND ((table_name = 'purchase_invoice_lines' AND column_name IN ('Quantity', 'UnitPriceCurrency'))
                OR (table_name = 'stock_movements' AND column_name = 'Quantity'))
            """).SingleAsync());
        Assert.Equal(3, numericColumns);
        var attributesType = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT data_type AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'products' AND column_name = 'Attributes'
            """).SingleAsync());
        Assert.Equal("jsonb", attributesType);
    }

    [Fact]
    public async Task Migration_down_and_up_recreates_view_and_has_no_model_drift()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.WithDbAsync(async db =>
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("0");
            await migrator.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var exists = await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('v_stock_balance') IS NOT NULL AS "Value"
                """).SingleAsync();
            Assert.True(exists);
        });
    }
}
