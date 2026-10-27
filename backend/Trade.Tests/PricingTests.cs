using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Pricing;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PricingTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task PriceType_create_list_read_and_update_preserve_fields_and_defaults()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await CreateTypeAsync(api, "  Retail  ");
        Assert.Equal(api.OrganizationId, type.OrganizationId);
        Assert.Equal("Retail", type.Name);
        Assert.Equal((0m, true, 0), (type.DefaultMarkupPercent, type.Active, type.SortOrder));
        Assert.Equal(type, Assert.Single(await api.ReadAsync<PriceTypeResponse[]>(await api.Client.GetAsync("/price-types"))));
        var updated = await api.ReadAsync<PriceTypeResponse>(await api.Client.PutAsJsonAsync($"/price-types/{type.Id}",
            new PriceTypeRequest("Wholesale"), ApiTestHost.Json));
        Assert.Equal(type with { Name = "Wholesale" }, updated);
        Assert.Equal(updated, await api.ReadAsync<PriceTypeResponse>(await api.Client.GetAsync($"/price-types/{type.Id}")));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest(" "), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/price-types/{type.Id}", new PriceTypeRequest(""), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task GOODS_prices_are_specific_to_Batch_and_PriceType_and_PUT_updates_the_same_key()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(productId: a.ProductId, locationId: a.LocationId);
        var retail = await CreateTypeAsync(api);
        var wholesale = await CreateTypeAsync(api, "Wholesale");
        await SetBatchAsync(api, a.BatchId, retail.Id, 10m);
        await SetBatchAsync(api, b.BatchId, retail.Id, 20m);
        await SetBatchAsync(api, a.BatchId, wholesale.Id, 8m);
        var price = await SetBatchAsync(api, a.BatchId, retail.Id, 11.1234m);
        Assert.Equal(price, await api.ReadAsync<BatchPriceResponse>(await api.Client.GetAsync($"/batches/{a.BatchId}/prices/{retail.Id}")));
        Assert.Equal(3, await api.QueryAsync(db => db.BatchPrices.CountAsync()));
        foreach (var (stock, type, expected) in new[] { (a, retail, 11.1234m), (b, retail, 20m), (a, wholesale, 8m) })
        {
            var sale = await api.CreateSaleAsync(stock.SaleInput(1m) with
            {
                PriceTypeId = type.Id, Lines = [new(null, 1, stock.ProductId, stock.BatchId, 1m)]
            });
            Assert.Equal((expected, expected), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        }
    }

    [Fact]
    public async Task SERVICE_uses_ProductPrice_and_mixed_sale_resolves_both_sources()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 15m);
        await SetProductAsync(api, service.Id, type.Id, 100m);
        var price = await SetProductAsync(api, service.Id, type.Id, 123.4567m);
        Assert.Equal(price, await api.ReadAsync<ProductPriceResponse>(await api.Client.GetAsync($"/products/{service.Id}/prices/{type.Id}")));
        Assert.Equal(1, await api.QueryAsync(db => db.ProductPrices.CountAsync()));
        var sale = await api.CreateSaleAsync(api.NewInput(stock.LocationId,
            [new(null, 1, stock.ProductId, stock.BatchId, 2m), new(null, 2, service.Id, null, 3m)]) with { PriceTypeId = type.Id });
        Assert.Equal((15m, 15m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        Assert.Equal((123.4567m, 123.4567m), (sale.Lines[1].RecommendedUnitPrice, sale.Lines[1].UnitPrice));
        await api.PostSaleAsync(sale.Document.Id);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((8m, 0m), ((await api.BalanceAsync(stock)).PhysicalQuantity, (await api.BalanceAsync(stock)).ReservedQuantity));
    }

    [Fact]
    public async Task GOODS_cannot_have_ProductPrice_and_priced_SERVICE_cannot_change_ItemType()
    {
        await using var api = await fixture.CreateApiAsync();
        var goods = await api.CreateProductAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{goods.Id}/prices/{type.Id}", new PriceRequest(1m), ApiTestHost.Json)).StatusCode);
        await SetProductAsync(api, service.Id, type.Id, 1m);
        var update = new ProductRequest(service.CategoryId, service.UnitId, ItemType.GOODS, service.Code, service.Barcode,
            service.Name, service.Description, service.Attributes, service.Active);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{service.Id}", update, ApiTestHost.Json)).StatusCode);
        Assert.Equal(ItemType.SERVICE, (await api.ReadAsync<ProductResponse>(await api.Client.GetAsync($"/products/{service.Id}"))).ItemType);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("10000000000000000")]
    public async Task Invalid_price_is_rejected_for_both_sources_without_partial_writes(string input)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        var price = new PriceRequest(decimal.Parse(input, CultureInfo.InvariantCulture));
        foreach (var path in new[] { $"/batches/{stock.BatchId}/prices/{type.Id}", $"/products/{service.Id}/prices/{type.Id}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync(path, price, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.BatchPrices.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.ProductPrices.ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_selected_price_is_an_error_and_zero_is_a_valid_configured_price(bool isService)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var productId = isService ? (await api.CreateProductAsync(ItemType.SERVICE)).Id : stock.ProductId;
        Guid? batchId = isService ? null : stock.BatchId;
        var type = await CreateTypeAsync(api);
        var input = api.NewInput(stock.LocationId, [new(null, 1, productId, batchId, 1m)]) with { PriceTypeId = type.Id };
        await ErrorAsync(await api.Client.PostAsJsonAsync("/sales", input, ApiTestHost.Json), HttpStatusCode.BadRequest, "PRICE_NOT_FOUND");
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        if (isService) await SetProductAsync(api, productId, type.Id, 0m);
        else await SetBatchAsync(api, stock.BatchId, type.Id, 0m);
        var sale = await api.CreateSaleAsync(input);
        Assert.Equal((0m, 0m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equal_or_higher_actual_price_is_allowed_but_lower_price_requires_permission(bool isService)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 10m);
        await SetProductAsync(api, service.Id, type.Id, 10m);
        var input = api.NewInput(stock.LocationId,
            [new(null, 1, isService ? service.Id : stock.ProductId, isService ? null : stock.BatchId, 1m, 9.9999m)]) with { PriceTypeId = type.Id };
        await ErrorAsync(await api.Client.PostAsJsonAsync("/sales", input, ApiTestHost.Json), HttpStatusCode.Forbidden, "PRICE_BELOW_RECOMMENDED");
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        var sale = await api.CreateSaleAsync(input with { Lines = [input.Lines[0] with { UnitPrice = 10m }] });
        var above = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
            { Lines = [SalesTestSupport.Edit(sale).Lines[0] with { UnitPrice = 12m }] });
        Assert.Equal((10m, 12m), (above.Lines[0].RecommendedUnitPrice, above.Lines[0].UnitPrice));
        await ErrorAsync(await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}", SalesTestSupport.Edit(above) with
            { Lines = [SalesTestSupport.Edit(above).Lines[0] with { UnitPrice = 9m }] }, ApiTestHost.Json),
            HttpStatusCode.Forbidden, "PRICE_BELOW_RECOMMENDED");
        var unchanged = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(above.Document, unchanged.Document);
        Assert.Equal(above.Lines, unchanged.Lines);
        await api.PostSaleAsync(sale.Document.Id);
    }

    [Fact]
    public async Task Trusted_permission_allows_lower_price_and_is_checked_again_at_POST()
    {
        var provider = new TestPricePermissions { Permissions = new(true) };
        await using var api = await fixture.CreateApiAsync(pricePermissions: provider);
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 10m);
        var sale = await api.CreateSaleAsync(stock.SaleInput() with
            { PriceTypeId = type.Id, Lines = [new(null, 1, stock.ProductId, stock.BatchId, 4m, 5m)] });
        sale = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
            { Lines = [SalesTestSupport.Edit(sale).Lines[0] with { UnitPrice = 0m }] });
        Assert.Equal(0m, sale.Lines[0].UnitPrice);
        provider.Permissions = SalePricePermissions.None;
        await ErrorAsync(await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null), HttpStatusCode.Forbidden, "PRICE_BELOW_RECOMMENDED");
        var unchanged = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(sale.Document, unchanged.Document);
        Assert.Equal(4m, (await api.BalanceAsync(stock)).ReservedQuantity);
        provider.Permissions = new(true);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(DocumentStatus.POSTED, posted.Document.Status);
        // Replaying an already committed POST remains idempotent even after capabilities change.
        provider.Permissions = SalePricePermissions.None;
        Assert.Equal(posted.Document, (await api.PostSaleAsync(sale.Document.Id)).Document);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task Client_cannot_forge_recommended_price_or_grant_itself_permission()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 10m);
        var input = stock.SaleInput() with { PriceTypeId = type.Id };
        var forged = new
        {
            input.DocumentDate, input.Comment, input.InternalNote, input.CounterpartyId, input.StorageLocationId,
            input.PriceTypeId, input.DiscountPercent, canSellBelowRecommendedPrice = true,
            lines = new[] { new { id = (Guid?)null, lineNo = 1, productId = stock.ProductId, batchId = stock.BatchId,
                quantity = 1m, recommendedUnitPrice = 0m, unitPrice = 1m, canSellBelowRecommendedPrice = true } }
        };
        api.Client.DefaultRequestHeaders.Add("X-Can-Sell-Below-Recommended-Price", "true");
        await ErrorAsync(await api.Client.PostAsJsonAsync("/sales", forged, ApiTestHost.Json), HttpStatusCode.Forbidden, "PRICE_BELOW_RECOMMENDED");
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
    }

    [Fact]
    public async Task PUT_preserves_saved_recommendation_and_actual_price_after_catalog_price_changes()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 10m);
        var sale = await api.CreateSaleAsync(stock.SaleInput() with { PriceTypeId = type.Id });
        Assert.Equal((10m, 12m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        await SetBatchAsync(api, stock.BatchId, type.Id, 15m);
        var read = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(10m, read.Lines[0].RecommendedUnitPrice);
        sale = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale));
        var edited = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
            { Lines = [SalesTestSupport.Edit(sale).Lines[0] with { UnitPrice = null }] });
        Assert.Equal((10m, 12m), (edited.Lines[0].RecommendedUnitPrice, edited.Lines[0].UnitPrice));
        Assert.Equal(sale.Lines[0].Id, edited.Lines[0].Id);
        Assert.Equal(4m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Changing_PriceType_or_Batch_uses_the_new_source_and_POST_preserves_the_saved_snapshot()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(productId: a.ProductId, locationId: a.LocationId);
        var retail = await CreateTypeAsync(api);
        var wholesale = await CreateTypeAsync(api, "Wholesale");
        await SetBatchAsync(api, a.BatchId, retail.Id, 10m);
        await SetBatchAsync(api, b.BatchId, wholesale.Id, 7m);
        var sale = await api.CreateSaleAsync(a.SaleInput() with { PriceTypeId = retail.Id });
        var edit = SalesTestSupport.Edit(sale);
        sale = await api.UpdateSaleAsync(sale.Document.Id, edit with
        {
            PriceTypeId = wholesale.Id, Lines = [edit.Lines[0] with { BatchId = b.BatchId, UnitPrice = null }]
        });
        Assert.Equal((7m, 7m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        Assert.Equal(0m, (await api.BalanceAsync(a)).ReservedQuantity);
        Assert.Equal(4m, (await api.BalanceAsync(b)).ReservedQuantity);
        await SetBatchAsync(api, b.BatchId, wholesale.Id, 50m);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal((7m, 7m), (posted.Lines[0].RecommendedUnitPrice, posted.Lines[0].UnitPrice));
        await api.UnpostSaleAsync(sale.Document.Id);
        var reposted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(posted.Document.Number, reposted.Document.Number);
        Assert.Equal(posted.Lines, reposted.Lines);
    }

    [Fact]
    public async Task Null_PriceType_preserves_manual_pricing_with_zero_recommendation()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock.SaleInput());
        Assert.Equal((0m, 12m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
        var edit = SalesTestSupport.Edit(sale);
        sale = await api.UpdateSaleAsync(sale.Document.Id, edit with { Lines = [edit.Lines[0] with { UnitPrice = null }] });
        Assert.Equal((0m, 12m), (sale.Lines[0].RecommendedUnitPrice, sale.Lines[0].UnitPrice));
    }

    [Fact]
    public async Task Prices_and_PriceTypes_cannot_be_read_assigned_or_used_across_organizations()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 10m);
        await SetProductAsync(api, service.Id, type.Id, 10m);
        api.Client.DefaultRequestHeaders.Remove("X-Test-Organization-Id");
        api.Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", Guid.NewGuid().ToString());
        Assert.Empty(await api.ReadAsync<PriceTypeResponse[]>(await api.Client.GetAsync("/price-types")));
        foreach (var path in new[] { $"/price-types/{type.Id}", $"/batches/{stock.BatchId}/prices/{type.Id}", $"/products/{service.Id}/prices/{type.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/price-types/{type.Id}", new PriceTypeRequest("Other"), ApiTestHost.Json)).StatusCode);
        foreach (var path in new[] { $"/batches/{stock.BatchId}/prices/{type.Id}", $"/products/{service.Id}/prices/{type.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync(path, new PriceRequest(1m), ApiTestHost.Json)).StatusCode);
        var ownStock = await api.ReceiveAsync();
        var ownService = await api.CreateProductAsync(ItemType.SERVICE);
        foreach (var path in new[] { $"/batches/{ownStock.BatchId}/prices/{type.Id}", $"/products/{ownService.Id}/prices/{type.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync(path, new PriceRequest(1m), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsJsonAsync("/sales", ownStock.SaleInput() with { PriceTypeId = type.Id }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(10m, await api.QueryAsync(db => db.BatchPrices.Where(x => x.BatchId == stock.BatchId).Select(x => x.UnitPrice).SingleAsync()));
    }

    [Fact]
    public async Task Concurrent_price_upserts_create_one_row_and_all_valid_writes_succeed()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await CreateTypeAsync(api);
        var batchWrites = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => SetBatchAsync(api, stock.BatchId, type.Id, i)));
        var productWrites = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => SetProductAsync(api, service.Id, type.Id, i)));
        Assert.Equal(8, batchWrites.Length);
        Assert.Equal(8, productWrites.Length);
        Assert.Single(await api.QueryAsync(db => db.BatchPrices.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.ProductPrices.ToListAsync()));
    }

    [Fact]
    public async Task Pricing_schema_has_only_required_fields_composite_keys_and_numeric_prices()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("price_types", new[] { "Id", "OrganizationId", "Name", "Active", "DefaultMarkupPercent", "SortOrder" }),
            ("batch_prices", new[] { "BatchId", "PriceTypeId", "UnitPrice" }),
            ("product_prices", new[] { "ProductId", "PriceTypeId", "UnitPrice" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = {table} ORDER BY ordinal_position
                """).ToListAsync());
            Assert.Equal(expected, columns);
        }
        Assert.Equal(2, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_schema = 'public'
              AND table_name IN ('batch_prices', 'product_prices') AND column_name = 'UnitPrice'
              AND numeric_precision = 20 AND numeric_scale = 4
            """).SingleAsync()));
        var stock = await api.ReceiveAsync();
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 1m);
        await api.WithDbAsync(async db =>
        {
            db.BatchPrices.Add(BatchPrice.Create(stock.BatchId, type.Id, 2m));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(error.InnerException).SqlState);
        });
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE batch_prices SET "UnitPrice" = -1 WHERE "BatchId" = {stock.BatchId} AND "PriceTypeId" = {type.Id}
                """));
            Assert.Equal("23514", error.SqlState);
        });
    }

    [Fact]
    public async Task Pricing_migration_preserves_existing_documents_batches_movements_and_reserves()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var posted = await api.CreateSaleAsync(stock, 2m);
        posted = await api.PostSaleAsync(posted.Document.Id);
        var draft = await api.CreateSaleAsync(stock, 3m);
        var balance = await api.BalanceAsync(stock);
        await api.WithDbAsync(async db =>
        {
            await PricingMigrationTestSupport.RoundTripAsync(db, recreatePricingTables: true);
            Assert.False(db.Database.HasPendingModelChanges());
        });
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{posted.Document.Id}"));
        Assert.Equal(posted.Document, current.Document);
        Assert.Equal(posted.Lines, current.Lines);
        Assert.Equal(draft.Document, (await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{draft.Document.Id}"))).Document);
        Assert.Equal(balance, await api.BalanceAsync(stock));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Equal(2, await api.QueryAsync(db => db.StockMovements.CountAsync()));
        var type = await CreateTypeAsync(api);
        await SetBatchAsync(api, stock.BatchId, type.Id, 25m);
        var edit = SalesTestSupport.Edit(draft);
        var priced = await api.UpdateSaleAsync(draft.Document.Id, edit with
            { PriceTypeId = type.Id, Lines = [edit.Lines[0] with { UnitPrice = null }] });
        Assert.Equal((25m, 25m), (priced.Lines[0].RecommendedUnitPrice, priced.Lines[0].UnitPrice));
        Assert.Equal(balance, await api.BalanceAsync(stock));
    }

    private static async Task<PriceTypeResponse> CreateTypeAsync(ApiTestHost api, string name = "Retail") =>
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest(name), ApiTestHost.Json), HttpStatusCode.Created);

    private static async Task<BatchPriceResponse> SetBatchAsync(ApiTestHost api, Guid batchId, Guid typeId, decimal price) =>
        await api.ReadAsync<BatchPriceResponse>(await api.Client.PutAsJsonAsync($"/batches/{batchId}/prices/{typeId}", new PriceRequest(price), ApiTestHost.Json));

    private static async Task<ProductPriceResponse> SetProductAsync(ApiTestHost api, Guid productId, Guid typeId, decimal price) =>
        await api.ReadAsync<ProductPriceResponse>(await api.Client.PutAsJsonAsync($"/products/{productId}/prices/{typeId}", new PriceRequest(price), ApiTestHost.Json));

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed class TestPricePermissions : ISalePricePermissionProvider
    {
        public SalePricePermissions Permissions { get; set; } = SalePricePermissions.None;
        public SalePricePermissions GetPermissions() => Permissions;
    }
}
