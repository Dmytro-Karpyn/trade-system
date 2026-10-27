using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class CustomerReturnPersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_stock_or_settlement_write_rolls_back_entire_POST_or_UNPOST(bool unpost, bool settlement)
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        if (unpost) result = await api.PostReturnAsync(result.Document.Id);
        var before = Assert.Single(await api.ReturnableAsync(sale.Document.Id));
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_customer_return() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected return failure' USING ERRCODE = '23514'; END;
            $$;
            """));
        var trigger = (unpost, settlement) switch
        {
            (false, false) => "CREATE TRIGGER fail_customer_return BEFORE INSERT ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_customer_return();",
            (false, true) => "CREATE TRIGGER fail_customer_return BEFORE INSERT ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_customer_return();",
            (true, false) => "CREATE TRIGGER fail_customer_return BEFORE DELETE ON stock_movements FOR EACH ROW EXECUTE FUNCTION fail_customer_return();",
            _ => "CREATE TRIGGER fail_customer_return BEFORE DELETE ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_customer_return();"
        };
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        var action = unpost ? "unpost" : "post";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/customer-returns/{result.Document.Id}/{action}", null)).StatusCode);
        Assert.Equal(result.Document, (await api.ReadAsync<CustomerReturnResponse>(await api.Client.GetAsync($"/customer-returns/{result.Document.Id}"))).Document);
        Assert.Equal(unpost ? 1 : 0, (await api.SettlementsAsync(result.Document.Id)).Length);
        Assert.Equal(unpost ? 1 : 0, await api.QueryAsync(db => db.StockMovements.CountAsync(x => x.DocumentId == result.Document.Id)));
        Assert.Equal(before, Assert.Single(await api.ReturnableAsync(sale.Document.Id)));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
    }

    [Fact]
    public async Task Schema_contains_exact_requested_fields_without_copied_product_batch_counterparty_prices_or_cached_limits()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("customer_returns", new[] { "DocumentId", "SourceSaleInvoiceId", "StorageLocationId" }),
            ("customer_return_lines", new[] { "Id", "CustomerReturnId", "SourceSaleLineId", "Quantity", "RefundAmount" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = {table}
                """).ToArrayAsync());
            Assert.Equal(expected.Order(), columns.Order());
        }
        Assert.Equal(2, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'customer_return_lines'
                AND column_name IN ('Quantity', 'RefundAmount') AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_rejects_cross_sale_lines_cross_organization_storage_and_source_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        var (_, otherSale) = await api.SoldGoodsAsync();
        api.SetOrganization(Guid.NewGuid());
        var (_, foreignSale) = await api.SoldGoodsAsync();
        api.SetOrganization(api.OrganizationId);
        await api.WithDbAsync(async db =>
        {
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE customer_return_lines SET "SourceSaleLineId" = {otherSale.Lines.Single().Id} WHERE "Id" = {result.Lines.Single().Id}
                """));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE customer_returns SET "StorageLocationId" = {foreignSale.StorageLocationId} WHERE "DocumentId" = {result.Document.Id}
                """));
            await RejectAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE documents SET "Status" = 'DRAFT', "PostedAt" = NULL, "PostedBy" = NULL WHERE "Id" = {sale.Document.Id}
                """));
        });
        static async Task RejectAsync(Func<Task<int>> operation) =>
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(operation)).SqlState);
    }

    [Fact]
    public async Task DRAFT_can_swap_source_lines_atomically_without_duplicate_references()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateProductAsync(ItemType.SERVICE);
        var b = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(api.NewInput(Guid.NewGuid(),
            [new(null, 1, a.Id, null, 1m, 100m), new(null, 2, b.Id, null, 1m, 100m)]));
        sale = await api.PostSaleAsync(sale.Document.Id);
        var result = await api.CreateReturnAsync(new(new DateOnly(2026, 10, 20), sale.Document.Id, sale.StorageLocationId,
            sale.Lines.Select(x => new Trade.Domain.Returns.CustomerReturnLineInput(null, x.Id, 0m, 10m)).ToArray()));
        var edit = result.EditReturn(goods: false);
        var changed = await api.ReadAsync<CustomerReturnResponse>(await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}", edit with
        {
            Lines = [edit.Lines[0] with { SourceSaleLineId = edit.Lines[1].SourceSaleLineId },
                edit.Lines[1] with { SourceSaleLineId = edit.Lines[0].SourceSaleLineId }]
        }, ApiTestHost.Json));
        Assert.Equal(2, changed.Lines.Select(x => x.SourceSaleLineId).Distinct().Count());
        await api.PostReturnAsync(result.Document.Id);
        Assert.Equal(-20m, Assert.Single(await api.SettlementsAsync(result.Document.Id)).Amount);
    }

    [Fact]
    public async Task Migration_upgrade_preserves_previous_stock_settlements_and_current_cost()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync();
        var balance = await api.BalanceAsync(stock);
        var costs = await api.BatchCostsAsync();
        var settlement = Assert.Single(await api.SettlementsAsync(sale.Document.Id));
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020130111_ExpensesAndDirectPurchaseCost");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(balance, await api.BalanceAsync(stock));
        Assert.Equal(costs, await api.BatchCostsAsync());
        Assert.Equal(settlement.Id, Assert.Single(await api.SettlementsAsync(sale.Document.Id)).Id);
        await api.PostReturnAsync((await api.CreateReturnAsync(sale.ReturnInput())).Document.Id);
        Assert.Equal(balance.PhysicalQuantity + 1m, (await api.BalanceAsync(stock)).PhysicalQuantity);
    }
}
