using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Purchasing;
using Trade.Domain.Returns;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SupplierReturnPersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_stock_or_settlement_write_rolls_back_POST_or_UNPOST_and_reservations(bool unpost, bool settlement)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        if (unpost) result = await api.PostSupplierReturnAsync(result.Document.Id);
        var before = await api.BalanceAsync(stock);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_supplier_return() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected supplier return failure' USING ERRCODE = '23514'; END;
            $$;
            """));
        var trigger = (unpost, settlement) switch
        {
            (false, false) => "CREATE TRIGGER fail_supplier_return BEFORE INSERT ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_supplier_return();",
            (false, true) => "CREATE TRIGGER fail_supplier_return BEFORE INSERT ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_supplier_return();",
            (true, false) => "CREATE TRIGGER fail_supplier_return BEFORE DELETE ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_supplier_return();",
            _ => "CREATE TRIGGER fail_supplier_return BEFORE DELETE ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_supplier_return();"
        };
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        var action = unpost ? "unpost" : "post";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/supplier-returns/{result.Document.Id}/{action}", null)).StatusCode);
        Assert.Equal(result.Document, (await api.ReadAsync<SupplierReturnResponse>(await api.Client.GetAsync($"/supplier-returns/{result.Document.Id}"))).Document);
        Assert.Equal(before, await api.BalanceAsync(stock));
        Assert.Equal(unpost ? 1 : 0, (await api.SettlementsAsync(result.Document.Id)).Length);
        Assert.Equal(unpost ? 1 : 0, await api.QueryAsync(db => db.StockMovements.CountAsync(x => x.DocumentId == result.Document.Id)));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
    }

    [Fact]
    public async Task Schema_has_only_requested_supplier_return_fields_and_an_ordinary_shared_stock_view()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("supplier_returns", new[] { "DocumentId", "SourcePurchaseInvoiceId", "StorageLocationId" }),
            ("supplier_return_lines", new[] { "Id", "SupplierReturnId", "SourcePurchaseLineId", "Quantity" })
        })
        {
            var actual = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {table}
                """).ToArrayAsync());
            Assert.Equal(expected.Order(), actual.Order());
        }
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT relkind::text AS "Value" FROM pg_class WHERE oid = 'v_stock_balance'::regclass
            """).SingleAsync()));
        Assert.Equal(1, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'supplier_return_lines'
                AND column_name = 'Quantity' AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_enforces_line_scope_storage_scope_and_purchase_dependency()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        var other = await api.ReceiveAsync();
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.ReceiveAsync();
        api.SetOrganization(api.OrganizationId);
        await api.WithDbAsync(async db =>
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE supplier_return_lines SET "SourcePurchaseLineId" = {other.Purchase.Lines.Single().Id} WHERE "Id" = {result.Lines.Single().Id}
                """));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE supplier_returns SET "StorageLocationId" = {foreign.LocationId} WHERE "DocumentId" = {result.Document.Id}
                """));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE documents SET "Status" = 'DRAFT', "PostedAt" = NULL, "PostedBy" = NULL WHERE "Id" = {stock.Purchase.Document.Id}
                """));
        });
        static async Task RejectAsync(Func<Task<int>> operation) =>
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(operation)).SqlState);
    }

    [Fact]
    public async Task Multi_line_refund_rounds_only_document_total_and_DRAFT_can_swap_source_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), quantity: 1m, price: 0.0001m, discount: 50m, rate: 2m);
        var edit = ApiTestHost.Edit(purchase);
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0], new PurchaseLineDetails(null, 2, edit.Lines[0].ProductId, 1m, 0.0001m)] }, ApiTestHost.Json));
        purchase = await api.PostAsync(purchase.Document.Id);
        var result = await api.CreateSupplierReturnAsync(new(new DateOnly(2026, 10, 20), purchase.Document.Id, purchase.StorageLocationId,
            purchase.Lines.Select(x => new SupplierReturnLineInput(null, x.Id, 1m)).ToArray()));
        var update = result.EditSupplierReturn();
        result = await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", update with
        {
            Lines = [update.Lines[0] with { SourcePurchaseLineId = update.Lines[1].SourcePurchaseLineId },
                update.Lines[1] with { SourcePurchaseLineId = update.Lines[0].SourcePurchaseLineId }]
        }, ApiTestHost.Json));
        await api.PostSupplierReturnAsync(result.Document.Id);
        var settlement = Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Equal((0.0001m, 0.0002m), (settlement.Amount, settlement.BaseAmountUah));
        Assert.Equal(2, await api.QueryAsync(db => db.StockMovements.CountAsync(x => x.DocumentId == result.Document.Id)));
    }

    [Fact]
    public async Task Migration_preserves_previous_stock_sales_reserves_and_cost_and_adds_supplier_reserves_on_upgrade()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        await api.CreateSaleAsync(stock, 3m);
        var balance = await api.BalanceAsync(stock);
        var costs = await api.BatchCostsAsync();
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020135808_CustomerReturns");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(balance, await api.BalanceAsync(stock));
        Assert.Equal(costs, await api.BatchCostsAsync());
        await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        Assert.Equal(5m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }
}
