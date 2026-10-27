using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class WriteOffPersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Movement_failure_rolls_back_POST_or_UNPOST_document_and_reserve(bool unpost)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        if (unpost) writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        var before = await api.LocationStockAsync(stock.BatchId, stock.LocationId);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_write_off() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected write-off failure' USING ERRCODE = '23514'; END;
            $$;
            """));
        var trigger = unpost
            ? "CREATE TRIGGER fail_write_off BEFORE DELETE ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_write_off();"
            : "CREATE TRIGGER fail_write_off BEFORE INSERT ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_write_off();";
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        var action = unpost ? "unpost" : "post";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/write-offs/{writeOff.Document.Id}/{action}", null)).StatusCode);
        Assert.Equal(writeOff.Document, (await api.ReadAsync<WriteOffResponse>(await api.Client.GetAsync($"/write-offs/{writeOff.Document.Id}"))).Document);
        Assert.Equal(before, await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(unpost ? 1 : 0, (await api.TransferMovementsAsync(writeOff.Document.Id)).Length);
        Assert.Empty(await api.SettlementsAsync(writeOff.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.Expenses.ToListAsync()));
    }

    [Fact]
    public async Task Schema_contains_only_requested_fields_and_cost_is_an_ordinary_view()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("write_off_reasons", new[] { "Id", "OrganizationId", "Name", "ExpenseCategoryId", "Active" }),
            ("write_offs", new[] { "DocumentId", "StorageLocationId", "WriteOffReasonId" }),
            ("write_off_lines", new[] { "Id", "WriteOffId", "LineNo", "BatchId", "Quantity" }),
            ("v_write_off_cost", new[] { "WriteOffId", "DocumentId", "DocumentDate", "StorageLocationId", "WriteOffReasonId", "ExpenseCategoryId", "TotalCostUah" })
        })
        {
            var actual = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {table}
                """).ToArrayAsync());
            Assert.Equal(expected.Order(), actual.Order());
        }
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT relkind::text AS "Value" FROM pg_class WHERE oid = 'v_write_off_cost'::regclass
            """).SingleAsync()));
        Assert.Equal(1, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'write_off_lines'
                AND column_name = 'Quantity' AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_rejects_foreign_category_reason_location_and_batch_and_duplicate_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(locationId: a.LocationId);
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(a.WriteOffInput(reason.Id) with { Lines = [new(null, 1, a.BatchId, 1m), new(null, 2, b.BatchId, 1m)] });
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        var foreignReason = await api.CreateReasonAsync();
        api.SetOrganization(api.OrganizationId);
        await api.WithDbAsync(async db =>
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE write_off_reasons SET "ExpenseCategoryId" = {foreignReason.ExpenseCategoryId} WHERE "Id" = {reason.Id}
                """), "23514");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE write_offs SET "WriteOffReasonId" = {foreignReason.Id} WHERE "DocumentId" = {writeOff.Document.Id}
                """), "23514");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE write_offs SET "StorageLocationId" = {foreign.LocationId} WHERE "DocumentId" = {writeOff.Document.Id}
                """), "23514");
            var line = writeOff.Lines.Single(x => x.LineNo == 2);
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE write_off_lines SET "BatchId" = {foreign.BatchId} WHERE "Id" = {line.Id}
                """), "23514");
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE write_off_lines SET "BatchId" = {a.BatchId} WHERE "Id" = {line.Id}
                """), "23505");
        });
        static async Task RejectAsync(Func<Task<int>> operation, string state) =>
            Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(operation)).SqlState);
    }

    [Fact]
    public async Task Migration_preserves_prior_reserves_and_cost_and_adds_writeoff_report_and_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        await api.CreateSaleAsync(stock, 1m);
        await api.CreateSupplierReturnAsync(stock.SupplierInput(1m));
        await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 1m));
        var before = await api.LocationStockAsync(stock.BatchId, stock.LocationId);
        var costs = await api.BatchCostsAsync();
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020143645_StockTransfersAndReservations");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(before, await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(costs, await api.BatchCostsAsync());
        await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        Assert.Equal((10m, 5m, 5m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(20m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
    }

    [Fact]
    public async Task Targeted_pricing_roundtrip_restores_dependent_cost_report_and_preserves_posted_writeoff_and_expenses()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 100m));
        await api.PostExpenseAsync(expense.Document.Id);
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        var before = Assert.Single(await api.WriteOffCostsAsync());
        Assert.Equal(40m, before.TotalCostUah);
        await api.WithDbAsync(db => PricingMigrationTestSupport.RoundTripAsync(db));
        Assert.Equal(before, Assert.Single(await api.WriteOffCostsAsync()));
        Assert.Equal(writeOff.Document, (await api.ReadAsync<WriteOffResponse>(await api.Client.GetAsync($"/write-offs/{writeOff.Document.Id}"))).Document);
        Assert.Single(await api.TransferMovementsAsync(writeOff.Document.Id));
    }
}
