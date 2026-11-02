using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SaleUiQueryTests(DatabaseFixture fixture)
{
    private static async Task<(PurchaseResponse Purchase, AvailableBatchResponse Batch, ProductResponse Product)> StockAsync(ApiTestHost api)
    {
        var product = await api.CreateProductAsync();
        var purchase = await api.CreatePurchaseAsync(product.Id, quantity: 10m);
        await api.PostAsync(purchase.Document.Id);
        var batch = Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync($"/stock/available-batches?productId={product.Id}&storageLocationId={purchase.StorageLocationId}")));
        return (purchase, batch, product);
    }
    private static async Task<SaleResponse> CreateAsync(ApiTestHost api, Guid productId, Guid? batchId, Guid location, decimal quantity = 2m, decimal price = 20m) =>
        await api.ReadAsync<SaleResponse>(await api.Client.PostAsJsonAsync("/sales", new SaleRequest(new(2026, 10, 28), "Sale 10%_", null,
            api.CounterpartyId, location, null, 10m, [new(null, 1, productId, batchId, quantity, price)]), ApiTestHost.Json), HttpStatusCode.Created);

    [Fact]
    public async Task Sale_list_filters_search_totals_and_detail_metadata_are_organization_scoped()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await StockAsync(api);
        var sale = await CreateAsync(api, stock.Product.Id, stock.Batch.BatchId, stock.Purchase.StorageLocationId);
        var row = Assert.Single(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync($"/sales?from=2026-10-28&to=2026-10-28&counterpartyId={api.CounterpartyId}&storageLocationId={stock.Purchase.StorageLocationId}&status=DRAFT&search=%25_")));
        Assert.Equal(sale.Document.Id, row.Id); Assert.Equal(36m, row.TotalUah); Assert.NotEmpty(row.CounterpartyName);
        var detail = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(stock.Product.Name, Assert.Single(detail.Products!).Name);
        Assert.Equal(ItemType.GOODS, Assert.Single(detail.Products!).ItemType);
        Assert.Equal(stock.Batch.BatchNumber, Assert.Single(detail.Batches!).BatchNumber);
        Assert.NotNull(detail.CounterpartyName);
        Assert.Empty(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync("/sales?status=POSTED")));
        Assert.Empty(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync("/sales?from=2026-10-29")));
        Assert.Empty(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync($"/sales?priceTypeId={Guid.NewGuid()}")));
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync("/sales")));
        Assert.Empty((await api.ReadAsync<SaleLookupsResponse>(await api.Client.GetAsync("/sales/lookups"))).Locations);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/{sale.Document.Id}/profit")).StatusCode);
    }

    [Fact]
    public async Task Available_batches_preserve_real_balances_and_expose_capacity_without_only_the_owned_draft_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await StockAsync(api);
        var sale = await CreateAsync(api, stock.Product.Id, stock.Batch.BatchId, stock.Purchase.StorageLocationId, quantity: 10m);
        var path = $"/stock/available-batches?productId={stock.Product.Id}&storageLocationId={stock.Purchase.StorageLocationId}";
        Assert.Empty(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(path)));
        var own = Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(path + $"&excludeSaleId={sale.Document.Id}")));
        Assert.Equal(10m, own.PhysicalQuantity); Assert.Equal(10m, own.ReservedQuantity);
        Assert.Equal(0m, own.AvailableQuantity); Assert.Equal(10m, own.OwnReservedQuantity); Assert.Equal(10m, own.AvailableForDocument);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path + $"&excludeSaleId={Guid.NewGuid()}")).StatusCode);
        await api.ReadAsync<SaleResponse>(await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync(path + $"&excludeSaleId={sale.Document.Id}")).StatusCode);
        api.SetOrganization(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(path + $"&excludeSaleId={sale.Document.Id}")).StatusCode);
    }

    [Fact]
    public async Task Own_reserve_does_not_increase_capacity_at_a_different_location_or_exclude_another_draft()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await StockAsync(api);
        var own = await CreateAsync(api, stock.Product.Id, stock.Batch.BatchId, stock.Purchase.StorageLocationId, quantity: 3m);
        await CreateAsync(api, stock.Product.Id, stock.Batch.BatchId, stock.Purchase.StorageLocationId, quantity: 2m);
        var path = $"/stock/available-batches?productId={stock.Product.Id}&storageLocationId={stock.Purchase.StorageLocationId}&excludeSaleId={own.Document.Id}";
        var row = Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(path)));
        Assert.Equal(5m, row.ReservedQuantity); Assert.Equal(5m, row.AvailableQuantity); Assert.Equal(8m, row.AvailableForDocument);
        Assert.Empty(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync($"/stock/available-batches?productId={stock.Product.Id}&storageLocationId={Guid.NewGuid()}&excludeSaleId={own.Document.Id}")));
    }

    [Fact]
    public async Task Cost_profit_and_prices_are_computed_from_existing_facts_without_overwriting_snapshots()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await StockAsync(api);
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        await api.Client.PutAsJsonAsync($"/batches/{stock.Batch.BatchId}/prices/{type.Id}", new PriceRequest(20m), ApiTestHost.Json);
        await api.Client.PutAsJsonAsync($"/products/{service.Id}/prices/{type.Id}", new PriceRequest(50m), ApiTestHost.Json);
        var goodsPrice = Assert.Single(await api.ReadAsync<SalePriceOption[]>(await api.Client.GetAsync($"/sales/price-options?productId={stock.Product.Id}&priceTypeId={type.Id}")));
        Assert.Equal(20m, goodsPrice.UnitPrice); Assert.Equal(stock.Batch.BatchId, goodsPrice.BatchId);
        Assert.Equal(50m, Assert.Single(await api.ReadAsync<SalePriceOption[]>(await api.Client.GetAsync($"/sales/price-options?productId={service.Id}&priceTypeId={type.Id}"))).UnitPrice);
        var sale = await api.ReadAsync<SaleResponse>(await api.Client.PostAsJsonAsync("/sales", new SaleRequest(new(2026, 10, 28), null, null,
            api.CounterpartyId, stock.Purchase.StorageLocationId, type.Id, 10m,
            [new(null, 1, stock.Product.Id, stock.Batch.BatchId, 2m), new(null, 2, service.Id, null, 1m)]), ApiTestHost.Json), HttpStatusCode.Created);
        await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null);
        var cost = await api.ReadAsync<SaleCostResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}/cost"));
        var profit = await api.ReadAsync<SaleProfitResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}/profit"));
        Assert.Equal(20m, cost.CurrentCost); Assert.Equal(81m, profit.NetSale); Assert.Equal(61m, profit.GrossProfit);
        Assert.Equal(61m / 81m * 100m, profit.Margin);
        await api.Client.PutAsJsonAsync($"/batches/{stock.Batch.BatchId}/prices/{type.Id}", new PriceRequest(30m), ApiTestHost.Json);
        var detail = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(20m, detail.Lines[0].RecommendedUnitPrice);
        Assert.Equal(20m, (await api.ReadAsync<SaleCostResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}/cost"))).CurrentCost);
        api.SetOrganization(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/price-options?productId={service.Id}&priceTypeId={type.Id}")).StatusCode);
    }

    [Fact]
    public async Task Service_without_price_type_has_zero_recommendation_and_zero_current_cost_with_null_zero_sale_margin()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await CreateAsync(api, product.Id, null, Guid.NewGuid(), price: 0m);
        Assert.Equal(0m, Assert.Single(await api.ReadAsync<SalePriceOption[]>(await api.Client.GetAsync($"/sales/price-options?productId={product.Id}"))).UnitPrice);
        var profit = await api.ReadAsync<SaleProfitResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}/profit"));
        Assert.Null(profit.Margin); Assert.Equal(0m, profit.NetSale); Assert.Equal(0m, profit.GrossProfit);
    }

    [Fact]
    public async Task Deleted_sales_are_excluded_and_no_read_totals_or_profit_are_stored_as_columns()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await CreateAsync(api, product.Id, null, Guid.NewGuid());
        await api.Client.DeleteAsync($"/sales/{sale.Document.Id}");
        Assert.Empty(await api.ReadAsync<SaleListResponse[]>(await api.Client.GetAsync("/sales")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/{sale.Document.Id}/cost")).StatusCode);
        var columns = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name IN ('sale_invoices', 'sale_invoice_lines')""").ToListAsync());
        Assert.DoesNotContain(columns, c => c.Contains("Total") || c.Contains("Profit") || c.Contains("CurrentCost"));
    }

    [Fact]
    public async Task Cost_and_profit_are_authorized_independently_from_sale_view()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, viewer, role) = await api.LimitedUserAsync("sale.view"); using var session = viewer;
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/sales/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/sales/{id}/cost")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/sales/{id}/profit")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["cost.view"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/sales/{id}/cost")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/sales/{id}/profit")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["profit.view"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/sales/{id}/profit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/sales/{id}/cost")).StatusCode);
    }
}
