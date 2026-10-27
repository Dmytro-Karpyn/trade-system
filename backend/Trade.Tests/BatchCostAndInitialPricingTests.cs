using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Xunit;

namespace Trade.Tests;

public sealed record BatchCostData(Guid BatchId, Guid PurchaseInvoiceLineId, Guid ProductId, decimal UnitCostUah);

[Collection("PostgreSQL")]
public sealed class BatchCostAndInitialPricingTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("100", "10", "40")]
    [InlineData("12.3456", "7.5", "39.12345678")]
    [InlineData("100", "100", "40")]
    public async Task Cost_view_computes_current_unit_purchase_price_discount_and_exchange_rate(string price, string discount, string rate)
    {
        await using var api = await fixture.CreateApiAsync();
        var (unitPrice, discountPercent, exchangeRate) = (Parse(price), Parse(discount), Parse(rate));
        var purchase = await CreatePurchaseAsync(api, unitPrice, discountPercent, exchangeRate);
        Assert.Empty(await api.QueryAsync(db => db.Database.SqlQueryRaw<BatchCostData>("SELECT * FROM v_batch_cost").ToListAsync()));
        await api.PostAsync(purchase.Document.Id);
        var cost = await CostAsync(api, purchase.Lines[0].Id);
        Assert.Equal(purchase.Lines[0].ProductId, cost.ProductId);
        Assert.Equal(unitPrice * (1 - discountPercent / 100) * exchangeRate, cost.UnitCostUah);
        Assert.Equal(await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync()), cost.BatchId);
    }

    [Fact]
    public async Task First_POST_creates_prices_with_markup_for_every_active_PriceType_of_its_organization()
    {
        await using var api = await fixture.CreateApiAsync();
        var active = new[]
        {
            await CreateTypeAsync(api, "Retail", 25m),
            await CreateTypeAsync(api, "Wholesale", 10.1234m),
            await CreateTypeAsync(api, "Base", 0m)
        };
        var inactive = await CreateTypeAsync(api, "Inactive", 50m, false);
        SetOrganization(api, Guid.NewGuid());
        var other = await CreateTypeAsync(api, "Other organization", 100m);
        SetOrganization(api, api.OrganizationId);
        var purchase = await CreatePurchaseAsync(api);
        Assert.Empty(await api.QueryAsync(db => db.BatchPrices.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        await api.PostAsync(purchase.Document.Id);
        var cost = await CostAsync(api, purchase.Lines[0].Id);
        Assert.Equal(3600m, cost.UnitCostUah);
        var prices = await api.QueryAsync(db => db.BatchPrices.OrderBy(x => x.PriceTypeId).ToListAsync());
        Assert.Equal(3, prices.Count);
        foreach (var type in active)
        {
            var price = Assert.Single(prices, x => x.PriceTypeId == type.Id);
            Assert.Equal(cost.BatchId, price.BatchId);
            Assert.Equal(decimal.Round(3600m * (1 + type.DefaultMarkupPercent / 100), 4, MidpointRounding.AwayFromZero), price.UnitPrice);
        }
        Assert.DoesNotContain(prices, x => x.PriceTypeId == inactive.Id || x.PriceTypeId == other.Id);
    }

    [Fact]
    public async Task UNPOST_and_repeated_POST_preserve_existing_manually_changed_prices_without_backfilling_new_types()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await CreateTypeAsync(api, "Retail", 25m);
        var purchase = await CreatePurchaseAsync(api);
        var posted = await api.PostAsync(purchase.Document.Id);
        var cost = await CostAsync(api, purchase.Lines[0].Id);
        await api.ReadAsync<BatchPriceResponse>(await api.Client.PutAsJsonAsync($"/batches/{cost.BatchId}/prices/{type.Id}", new PriceRequest(9999m), ApiTestHost.Json));
        Assert.Equal(posted.Document, (await api.PostAsync(purchase.Document.Id)).Document);
        Assert.Equal(9999m, (await PricesAsync(api)).Single().UnitPrice);
        await api.UnpostAsync(purchase.Document.Id);
        Assert.Equal(9999m, (await PricesAsync(api)).Single().UnitPrice);
        await CreateTypeAsync(api, "Added after batch creation", 30m);
        var reposted = await api.PostAsync(purchase.Document.Id);
        Assert.Equal(posted.Document.Number, reposted.Document.Number);
        Assert.Equal(9999m, (await PricesAsync(api)).Single().UnitPrice);
        Assert.Equal(cost.BatchId, await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync()));
    }

    [Theory]
    [InlineData("price", "4320")]
    [InlineData("exchange-rate", "4500")]
    [InlineData("discount", "3200")]
    public async Task Editing_unposted_purchase_changes_current_cost_but_preserves_initial_price(string change, string expected)
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateTypeAsync(api, "Retail", 25m);
        var purchase = await CreatePurchaseAsync(api);
        await api.PostAsync(purchase.Document.Id);
        var original = (await PricesAsync(api)).Single();
        Assert.Equal(4500m, original.UnitPrice);
        purchase = await api.UnpostAsync(purchase.Document.Id);
        var edit = ApiTestHost.Edit(purchase);
        edit = change switch
        {
            "price" => edit with { Lines = [edit.Lines[0] with { UnitPriceCurrency = 120m }] },
            "exchange-rate" => edit with { ExchangeRate = 50m },
            _ => edit with { DiscountPercent = 20m }
        };
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit, ApiTestHost.Json));
        Assert.Equal(Parse(expected), (await CostAsync(api, purchase.Lines[0].Id)).UnitCostUah);
        Assert.Equal(original, (await PricesAsync(api)).Single());
        await api.PostAsync(purchase.Document.Id);
        Assert.Equal(Parse(expected), (await CostAsync(api, purchase.Lines[0].Id)).UnitCostUah);
        Assert.Equal(original, (await PricesAsync(api)).Single());
    }

    [Fact]
    public async Task New_markup_only_affects_new_batches_including_new_lines_on_a_reposted_purchase()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await CreateTypeAsync(api, "Retail", 10m);
        var purchase = await CreatePurchaseAsync(api, 100m, 0m, 1m);
        await api.PostAsync(purchase.Document.Id);
        var original = (await PricesAsync(api)).Single();
        Assert.Equal(110m, original.UnitPrice);
        var updated = await api.ReadAsync<PriceTypeResponse>(await api.Client.PutAsJsonAsync($"/price-types/{type.Id}",
            new PriceTypeRequest(type.Name, 50m, true, 7), ApiTestHost.Json));
        Assert.Equal((50m, true, 7), (updated.DefaultMarkupPercent, updated.Active, updated.SortOrder));
        Assert.Equal(original, (await PricesAsync(api)).Single());
        purchase = await api.UnpostAsync(purchase.Document.Id);
        var edit = ApiTestHost.Edit(purchase);
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0], new PurchaseLineDetails(null, 2, edit.Lines[0].ProductId, 2m, 200m)] }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        var prices = await PricesAsync(api);
        Assert.Equal(2, prices.Length);
        Assert.Contains(original, prices);
        Assert.Equal(300m, Assert.Single(prices, x => x.BatchId != original.BatchId).UnitPrice);
    }

    [Fact]
    public async Task Inactive_type_is_skipped_and_activation_does_not_backfill_an_existing_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await CreateTypeAsync(api, "Inactive", 20m, false);
        var purchase = await CreatePurchaseAsync(api);
        await api.PostAsync(purchase.Document.Id);
        Assert.Empty(await PricesAsync(api));
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PutAsJsonAsync($"/price-types/{type.Id}",
            new PriceTypeRequest(type.Name, type.DefaultMarkupPercent, true), ApiTestHost.Json));
        await api.UnpostAsync(purchase.Document.Id);
        await api.PostAsync(purchase.Document.Id);
        Assert.Empty(await PricesAsync(api));
        var another = await CreatePurchaseAsync(api);
        await api.PostAsync(another.Document.Id);
        Assert.Equal(4320m, (await PricesAsync(api)).Single().UnitPrice);
    }

    [Fact]
    public async Task Concurrent_first_POSTs_create_one_initial_price_per_Batch_and_type()
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateTypeAsync(api, "Retail", 20m);
        await CreateTypeAsync(api, "Wholesale", 10m);
        var purchase = await CreatePurchaseAsync(api);
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostAsync(purchase.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Equal(new[] { 3960m, 4320m }, (await PricesAsync(api)).Select(x => x.UnitPrice).Order().ToArray());
    }

    [Fact]
    public async Task Failed_initial_price_insert_rolls_back_document_batch_movement_and_prices()
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateTypeAsync(api, "Retail", 20m);
        var purchase = await CreatePurchaseAsync(api);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_initial_price() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected price failure' USING ERRCODE = '23514'; END;
            $$;
            CREATE TRIGGER fail_initial_price BEFORE INSERT ON batch_prices
            FOR EACH ROW EXECUTE FUNCTION fail_initial_price();
            """));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/purchases/{purchase.Document.Id}/post", null)).StatusCode);
        var current = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"));
        Assert.Equal(purchase.Document, current.Document);
        Assert.Equal(DocumentStatus.DRAFT, current.Document.Status);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Empty(await PricesAsync(api));
        Assert.Empty(await api.SettlementsAsync(purchase.Document.Id));
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER fail_initial_price ON batch_prices;
            DROP FUNCTION fail_initial_price();
            """));
        await api.PostAsync(purchase.Document.Id);
        Assert.Single(await PricesAsync(api));
    }

    [Fact]
    public async Task Cost_view_keeps_full_precision_and_only_final_initial_price_is_rounded_to_four_places()
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateTypeAsync(api, "Retail", 0m);
        var purchase = await CreatePurchaseAsync(api, 0.0001m, 0m, 1.5m);
        await api.PostAsync(purchase.Document.Id);
        Assert.Equal(0.00015m, (await CostAsync(api, purchase.Lines[0].Id)).UnitCostUah);
        Assert.Equal(0.0002m, (await PricesAsync(api)).Single().UnitPrice);
    }

    [Fact]
    public async Task Unrepresentable_initial_price_rejects_POST_and_rolls_back_all_business_rows()
    {
        await using var api = await fixture.CreateApiAsync();
        await CreateTypeAsync(api, "Retail", 0m);
        var purchase = await CreatePurchaseAsync(api, 9_999_999_999_999_999.9999m, 0m, 2m);
        var edit = ApiTestHost.Edit(purchase);
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = 0.0001m }] }, ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/purchases/{purchase.Document.Id}/post", null)).StatusCode);
        Assert.Equal(purchase.Document, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"))).Document);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Empty(await PricesAsync(api));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("1000")]
    public async Task Invalid_default_markup_is_rejected_on_create_and_update(string value)
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await CreateTypeAsync(api, "Retail", 10m);
        var input = new PriceTypeRequest("Invalid", Parse(value));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/price-types", input, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/price-types/{type.Id}", input, ApiTestHost.Json)).StatusCode);
        Assert.Equal(type, await api.ReadAsync<PriceTypeResponse>(await api.Client.GetAsync($"/price-types/{type.Id}")));
        Assert.Single(await api.QueryAsync(db => db.PriceTypes.ToListAsync()));
    }

    [Fact]
    public async Task PriceType_Active_and_SortOrder_round_trip_and_list_respects_SortOrder()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await CreateTypeAsync(api, "A", 999.9999m, false, 10);
        var z = await CreateTypeAsync(api, "Z", 1.2345m, true, -1);
        var list = await api.ReadAsync<PriceTypeResponse[]>(await api.Client.GetAsync("/price-types"));
        Assert.Equal(new[] { z, a }, list);
        Assert.False((await api.ReadAsync<PriceTypeResponse>(await api.Client.GetAsync($"/price-types/{a.Id}"))).Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_GOODS_and_SERVICE_recommendations_survive_price_changes_edits_and_posting(bool isService)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api, "Retail", 0m);
        var productId = isService ? (await api.CreateProductAsync(ItemType.SERVICE)).Id : stock.ProductId;
        Guid? batchId = isService ? null : stock.BatchId;
        var path = isService ? $"/products/{productId}/prices/{type.Id}" : $"/batches/{batchId}/prices/{type.Id}";
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync(path, new PriceRequest(10m), ApiTestHost.Json)).StatusCode);
        var input = api.NewInput(stock.LocationId, [new(null, 1, productId, batchId, 2m, 12m)]) with { PriceTypeId = type.Id };
        var sale = await api.CreateSaleAsync(input);
        Assert.Equal((10m, 12m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        Assert.Equal(HttpStatusCode.OK, (await api.Client.PutAsJsonAsync(path, new PriceRequest(20m), ApiTestHost.Json)).StatusCode);
        var read = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(sale.Lines, read.Lines);
        var edit = SalesTestSupport.Edit(sale);
        sale = await api.UpdateSaleAsync(sale.Document.Id, edit with
            { Comment = "Quantity changed without repricing", Lines = [edit.Lines[0] with { Quantity = 3m, UnitPrice = null }] });
        Assert.Equal((10m, 12m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        var posted = await api.PostSaleAsync(sale.Document.Id);
        await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Equal(posted.Lines, (await api.PostSaleAsync(sale.Document.Id)).Lines);
        var newSale = await api.CreateSaleAsync(input with { Lines = [new(null, 1, productId, batchId, 1m)] });
        Assert.Equal((20m, 20m), (newSale.Lines[0].RecommendedUnitPrice, newSale.Lines[0].UnitPrice));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        if (isService)
            Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id || x.DocumentId == newSale.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task Migration_preserves_existing_prices_and_types_and_exposes_only_a_regular_computed_cost_view()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api, "Existing");
        await api.ReadAsync<BatchPriceResponse>(await api.Client.PutAsJsonAsync($"/batches/{stock.BatchId}/prices/{type.Id}", new PriceRequest(77m), ApiTestHost.Json));
        await api.WithDbAsync(async db =>
        {
            await PricingMigrationTestSupport.RoundTripAsync(db);
            Assert.False(db.Database.HasPendingModelChanges());
            var kind = await db.Database.SqlQueryRaw<string>("""
                SELECT relkind::text AS "Value" FROM pg_class WHERE oid = 'v_batch_cost'::regclass
                """).SingleAsync();
            Assert.Equal("v", kind);
            Assert.Equal(new[] { "BatchId", "PurchaseInvoiceLineId", "ProductId", "UnitCostUah" }, await db.Database.SqlQueryRaw<string>("""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'v_batch_cost' ORDER BY ordinal_position
                """).ToArrayAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM information_schema.columns c JOIN information_schema.tables t
                  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND c.column_name = 'UnitCostUah'
                """).SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_schema = 'public'
                  AND table_name = 'price_types' AND column_name = 'DefaultMarkupPercent' AND numeric_precision = 7 AND numeric_scale = 4
                """).SingleAsync());
        });
        Assert.Equal(type, await api.ReadAsync<PriceTypeResponse>(await api.Client.GetAsync($"/price-types/{type.Id}")));
        Assert.Equal(77m, (await PricesAsync(api)).Single().UnitPrice);
        Assert.Equal(10m, (await CostAsync(api, stock.Purchase.Lines[0].Id)).UnitCostUah);
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE price_types SET "DefaultMarkupPercent" = -1 WHERE "Id" = {type.Id}
                """));
            Assert.Equal("23514", error.SqlState);
        });
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static void SetOrganization(ApiTestHost api, Guid organizationId)
    {
        api.Client.DefaultRequestHeaders.Remove("X-Test-Organization-Id");
        api.Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", organizationId.ToString());
    }

    private static async Task<PriceTypeResponse> CreateTypeAsync(ApiTestHost api, string name, decimal markup = 0m,
        bool active = true, int sortOrder = 0) => await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types",
            new PriceTypeRequest(name, markup, active, sortOrder), ApiTestHost.Json), HttpStatusCode.Created);

    private static async Task<PurchaseResponse> CreatePurchaseAsync(ApiTestHost api, decimal price = 100m,
        decimal discount = 10m, decimal rate = 40m)
    {
        var purchase = await api.CreatePurchaseAsync(quantity: 10m);
        var edit = ApiTestHost.Edit(purchase);
        return await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { DiscountPercent = discount, ExchangeRate = rate, Lines = [edit.Lines[0] with { UnitPriceCurrency = price }] }, ApiTestHost.Json));
    }

    private static Task<BatchCostData> CostAsync(ApiTestHost api, Guid purchaseLineId) => api.QueryAsync(db => db.Database.SqlQuery<BatchCostData>($"""
        SELECT * FROM v_batch_cost WHERE "PurchaseInvoiceLineId" = {purchaseLineId}
        """).SingleAsync());

    private static async Task<BatchPriceResponse[]> PricesAsync(ApiTestHost api) => await api.QueryAsync(db => db.BatchPrices
        .OrderBy(x => x.BatchId).ThenBy(x => x.PriceTypeId).Select(x => new BatchPriceResponse(x.BatchId, x.PriceTypeId, x.UnitPrice)).ToArrayAsync());
}
