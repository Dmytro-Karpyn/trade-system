using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class StockTransferPersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failure_in_either_movement_rolls_back_entire_POST_or_UNPOST_and_reservations(bool unpost, bool positive)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination));
        if (unpost) transfer = await api.PostTransferAsync(transfer.Document.Id);
        var fromBefore = await api.LocationStockAsync(stock.BatchId, stock.LocationId);
        var toBefore = await api.LocationStockAsync(stock.BatchId, destination);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_stock_transfer() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected transfer movement failure' USING ERRCODE = '23514'; END;
            $$;
            """));
        var trigger = (unpost, positive) switch
        {
            (false, false) => "CREATE TRIGGER fail_stock_transfer BEFORE INSERT ON stock_movements FOR EACH ROW WHEN (NEW.\"Quantity\" < 0) EXECUTE FUNCTION fail_stock_transfer();",
            (false, true) => "CREATE TRIGGER fail_stock_transfer BEFORE INSERT ON stock_movements FOR EACH ROW WHEN (NEW.\"Quantity\" > 0) EXECUTE FUNCTION fail_stock_transfer();",
            (true, false) => "CREATE TRIGGER fail_stock_transfer BEFORE DELETE ON stock_movements FOR EACH ROW WHEN (OLD.\"Quantity\" < 0) EXECUTE FUNCTION fail_stock_transfer();",
            _ => "CREATE TRIGGER fail_stock_transfer BEFORE DELETE ON stock_movements FOR EACH ROW WHEN (OLD.\"Quantity\" > 0) EXECUTE FUNCTION fail_stock_transfer();"
        };
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        var action = unpost ? "unpost" : "post";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/{action}", null)).StatusCode);
        Assert.Equal(transfer.Document, (await api.ReadAsync<StockTransferResponse>(await api.Client.GetAsync($"/stock-transfers/{transfer.Document.Id}"))).Document);
        Assert.Equal(unpost ? 2 : 0, (await api.TransferMovementsAsync(transfer.Document.Id)).Length);
        Assert.Equal(fromBefore, await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(toBefore, await api.LocationStockAsync(stock.BatchId, destination));
        Assert.Empty(await api.SettlementsAsync(transfer.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
    }

    [Fact]
    public async Task Schema_has_exact_transfer_fields_and_ordinary_stock_view_without_cached_quantities()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("stock_transfers", new[] { "DocumentId", "FromLocationId", "ToLocationId" }),
            ("stock_transfer_lines", new[] { "Id", "StockTransferId", "LineNo", "BatchId", "Quantity" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {table}
                """).ToArrayAsync());
            Assert.Equal(expected.Order(), columns.Order());
        }
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT relkind::text AS "Value" FROM pg_class WHERE oid = 'v_stock_balance'::regclass
            """).SingleAsync()));
        Assert.Equal(1, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'stock_transfer_lines'
                AND column_name = 'Quantity' AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_enforces_location_batch_organization_and_deferred_uniqueness()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(locationId: a.LocationId);
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(a.TransferInput(destination) with
            { Lines = [new(null, 1, a.BatchId, 1m), new(null, 2, b.BatchId, 1m)] });
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        api.SetOrganization(api.OrganizationId);
        await api.WithDbAsync(async db =>
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE stock_transfers SET "ToLocationId" = {foreign.LocationId} WHERE "DocumentId" = {transfer.Document.Id}
                """), "23514");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE stock_transfers SET "FromLocationId" = "ToLocationId" WHERE "DocumentId" = {transfer.Document.Id}
                """), "23514");
            var line = transfer.Lines.Single(x => x.LineNo == 2);
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE stock_transfer_lines SET "BatchId" = {foreign.BatchId} WHERE "Id" = {line.Id}
                """), "23514");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE stock_transfer_lines SET "BatchId" = {a.BatchId} WHERE "Id" = {line.Id}
                """), "23505");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE stock_transfer_lines SET "LineNo" = 1 WHERE "Id" = {line.Id}
                """), "23505");
        });
        static async Task RejectAsync(Func<Task<int>> operation, string state) =>
            Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(operation)).SqlState);
    }

    [Fact]
    public async Task Migration_preserves_existing_sale_supplier_reserves_and_cost_and_adds_transfer_reserve_on_upgrade()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        await api.CreateSaleAsync(stock, 2m);
        await api.CreateSupplierReturnAsync(stock.SupplierInput(3m));
        var before = await api.LocationStockAsync(stock.BatchId, stock.LocationId);
        var costs = await api.BatchCostsAsync();
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020141737_SupplierReturnsAndReservations");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(before, await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(costs, await api.BatchCostsAsync());
        await api.CreateTransferAsync(stock.TransferInput(destination, 2m));
        Assert.Equal((10m, 7m, 3m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }
}
