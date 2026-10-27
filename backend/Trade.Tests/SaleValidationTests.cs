using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Domain.Sales;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SaleValidationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("goods-without-batch")]
    [InlineData("service-with-batch")]
    [InlineData("wrong-product")]
    [InlineData("duplicate-batch")]
    public async Task Invalid_Product_Batch_combinations_are_rejected_without_a_draft(string invalid)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var input = stock.SaleInput();
        input = invalid switch
        {
            "goods-without-batch" => input with { Lines = [input.Lines[0] with { BatchId = null }] },
            "service-with-batch" => input with { Lines = [input.Lines[0] with { ProductId = (await api.CreateProductAsync(ItemType.SERVICE)).Id }] },
            "wrong-product" => input with { Lines = [input.Lines[0] with { ProductId = (await api.CreateProductAsync()).Id }] },
            _ => input with { Lines = [input.Lines[0], input.Lines[0] with { LineNo = 2 }] }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/sales", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        Assert.Equal(0m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.00001")]
    [InlineData("10000000000000000")]
    public async Task Invalid_quantity_is_rejected(string value)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var input = stock.SaleInput() with
            { Lines = [new(null, 1, stock.ProductId, stock.BatchId, decimal.Parse(value, CultureInfo.InvariantCulture), 1m)] };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/sales", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
    }

    [Theory]
    [InlineData("unit-negative")]
    [InlineData("unit-scale")]
    [InlineData("unit-overflow")]
    [InlineData("discount")]
    public async Task Invalid_price_precision_or_discount_is_rejected(string invalid)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var input = stock.SaleInput();
        input = invalid switch
        {
            "unit-negative" => input with { Lines = [input.Lines[0] with { UnitPrice = -1m }] },
            "unit-scale" => input with { Lines = [input.Lines[0] with { UnitPrice = 1.00001m }] },
            "unit-overflow" => input with { Lines = [input.Lines[0] with { UnitPrice = 10_000_000_000_000_000m }] },
            _ => input with { DiscountPercent = 100.0001m }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/sales", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
    }

    [Fact]
    public async Task Empty_draft_can_be_saved_but_cannot_be_posted()
    {
        await using var api = await fixture.CreateApiAsync();
        var sale = await api.CreateSaleAsync(api.NewInput(Guid.NewGuid(), []));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null)).StatusCode);
        Assert.Null((await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"))).Document.Number);
    }

    [Fact]
    public async Task Same_batch_can_be_reserved_by_different_sales_within_available_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        await api.CreateSaleAsync(stock, 4m);
        await api.CreateSaleAsync(stock, 6m);
        Assert.Equal(10m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task SERVICE_can_have_multiple_lines_and_has_no_available_batches()
    {
        await using var api = await fixture.CreateApiAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var location = Guid.NewGuid();
        var sale = await api.CreateSaleAsync(api.NewInput(location,
            [new(null, 1, service.Id, null, 1m, 1m), new(null, 2, service.Id, null, 2m, 2m)]));
        await api.PostSaleAsync(sale.Document.Id);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Empty(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(
            $"/stock/available-batches?productId={service.Id}&storageLocationId={location}")));
    }

    [Fact]
    public async Task Referenced_SERVICE_cannot_be_reclassified_into_GOODS()
    {
        await using var api = await fixture.CreateApiAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        await api.CreateSaleAsync(api.NewInput(Guid.NewGuid(), [new(null, 1, service.Id, null, 1m, 1m)]));
        var update = new ProductRequest(service.CategoryId, service.UnitId, ItemType.GOODS, service.Code,
            service.Barcode, service.Name, service.Description, service.Attributes, service.Active);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/products/{service.Id}", update, ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Sales_and_batch_selection_are_scoped_to_the_current_organization()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        api.Client.DefaultRequestHeaders.Remove("X-Test-Organization-Id");
        api.Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/{sale.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}", SalesTestSupport.Edit(sale), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/sales/{sale.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/sales", stock.SaleInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync(
            $"/stock/available-batches?productId={stock.ProductId}&storageLocationId={stock.LocationId}")).StatusCode);
        Assert.Empty(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances")));
    }

    [Fact]
    public async Task Extending_DocumentType_does_not_allow_a_purchase_to_become_a_sale()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        var input = new PurchaseRequest(DocumentType.SALE_INVOICE, new DateOnly(2026, 10, 20), null, null,
            api.CounterpartyId, Guid.NewGuid(), Guid.NewGuid(), 1m, null, null, 0m,
            [new PurchaseLineDetails(null, 1, product.Id, 1m, 1m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.PurchaseInvoices.ToListAsync()));
    }
}
