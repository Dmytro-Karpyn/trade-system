using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SupplierReturnStockTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Sales_and_supplier_returns_share_reserves_and_each_excludes_only_its_own_DRAFT()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput(4m));
        var sale = await api.CreateSaleAsync(stock, 3m);
        Assert.Equal((10m, 7m, 3m), Values(await api.BalanceAsync(stock)));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(4m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/sales", stock.SaleInput(4m), ApiTestHost.Json));
        var edit = SalesTestSupport.Edit(sale);
        sale = await api.UpdateSaleAsync(sale.Document.Id, edit with { Lines = [edit.Lines[0] with { Quantity = 6m }] });
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}",
            result.EditSupplierReturn(), ApiTestHost.Json));
        Assert.Equal((10m, 10m, 0m), Values(await api.BalanceAsync(stock)));
        await api.PostSupplierReturnAsync(result.Document.Id);
        Assert.Equal((6m, 6m, 0m), Values(await api.BalanceAsync(stock)));
        await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal((0m, 0m, 0m), Values(await api.BalanceAsync(stock)));
        await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Equal((6m, 6m, 0m), Values(await api.BalanceAsync(stock)));
        await api.UnpostSupplierReturnAsync(result.Document.Id);
        Assert.Equal((10m, 10m, 0m), Values(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Concurrent_sale_and_supplier_reservation_cannot_overbook_the_same_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/sales", stock.SaleInput(6m), ApiTestHost.Json));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await SalesTestSupport.InsufficientAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal((10m, 6m, 4m), Values(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Concurrent_supplier_DRAFT_reservations_do_not_overbook_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(6m), ApiTestHost.Json)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        await SalesTestSupport.InsufficientAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await api.QueryAsync(db => db.SupplierReturns.ToListAsync()));
    }

    [Fact]
    public async Task DRAFT_edit_does_not_exclude_other_documents_reserves_and_failed_update_preserves_old_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        await api.CreateSaleAsync(stock, 6m);
        var edit = result.EditSupplierReturn();
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}",
            edit with { Lines = [edit.Lines[0] with { Quantity = 5m }] }, ApiTestHost.Json));
        Assert.Equal((10m, 8m, 2m), Values(await api.BalanceAsync(stock)));
        Assert.Equal(result.Document, (await api.ReadAsync<SupplierReturnResponse>(await api.Client.GetAsync($"/supplier-returns/{result.Document.Id}"))).Document);
    }

    [Fact]
    public async Task Customer_return_UNPOST_respects_supplier_return_reservations()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(quantity: 10m);
        var customerReturn = await api.CreateReturnAsync(sale.ReturnInput(2m));
        await api.PostReturnAsync(customerReturn.Document.Id);
        var supplierReturn = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/customer-returns/{customerReturn.Document.Id}/unpost", null));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/supplier-returns/{supplierReturn.Document.Id}")).StatusCode);
        await api.UnpostReturnAsync(customerReturn.Document.Id);
        Assert.Equal((0m, 0m, 0m), Values(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task POST_rechecks_physical_stock_and_rolls_back_when_a_stored_DRAFT_is_no_longer_fulfillable()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        // Simulate an external correction/import; API writers are already prevented from making this state.
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE stock_movements SET "Quantity" = 1 WHERE "DocumentId" = {stock.Purchase.Document.Id}
            """));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/supplier-returns/{result.Document.Id}/post", null));
        Assert.Equal(result.Document, (await api.ReadAsync<SupplierReturnResponse>(await api.Client.GetAsync($"/supplier-returns/{result.Document.Id}"))).Document);
        Assert.Empty(await api.SettlementsAsync(result.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task Changing_source_and_location_releases_old_reserve_and_validates_only_new_source_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(a.SupplierInput(4m));
        result = await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", result.EditSupplierReturn() with
        {
            SourcePurchaseInvoiceId = b.Purchase.Document.Id, StorageLocationId = b.LocationId, Lines = b.SupplierInput(6m).Lines
        }, ApiTestHost.Json));
        Assert.Equal((10m, 0m, 10m), Values(await api.BalanceAsync(a)));
        Assert.Equal((10m, 6m, 4m), Values(await api.BalanceAsync(b)));
        await api.PostSupplierReturnAsync(result.Document.Id);
        Assert.Equal((4m, 0m, 4m), Values(await api.BalanceAsync(b)));
    }

    [Fact]
    public async Task Stock_is_checked_in_the_selected_location_rather_than_the_source_purchase_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(quantity: 4m);
        var destination = (await api.CreatePurchaseAsync()).StorageLocationId;
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/supplier-returns",
            stock.SupplierInput(2m) with { StorageLocationId = destination }, ApiTestHost.Json));
        var customerReturn = await api.CreateReturnAsync(sale.ReturnInput(2m) with { StorageLocationId = destination });
        await api.PostReturnAsync(customerReturn.Document.Id);
        var supplierReturn = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m) with { StorageLocationId = destination });
        Assert.Equal(0m, Assert.Single(await api.SupplierReturnableAsync(stock.Purchase.Document.Id, destination)).MaxReturnableQuantity);
        await api.PostSupplierReturnAsync(supplierReturn.Document.Id);
        Assert.Equal(6m, (await api.BalanceAsync(stock)).PhysicalQuantity);
    }

    private static (decimal Physical, decimal Reserved, decimal Available) Values(StockBalanceResponse x) =>
        (x.PhysicalQuantity, x.ReservedQuantity, x.AvailableQuantity);
}
