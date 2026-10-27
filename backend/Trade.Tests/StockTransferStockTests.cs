using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class StockTransferStockTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("sales", false)]
    [InlineData("sales", true)]
    [InlineData("supplier-returns", false)]
    [InlineData("supplier-returns", true)]
    [InlineData("stock-transfers", false)]
    [InlineData("stock-transfers", true)]
    public async Task UNPOST_rejects_destination_consumption_or_reservations_and_succeeds_after_they_are_released(string kind, bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 10m));
        transfer = await api.PostTransferAsync(transfer.Document.Id);
        Guid outgoingId;
        if (kind == "sales") outgoingId = (await api.CreateSaleAsync(stock.SaleInput(2m) with { StorageLocationId = destination })).Document.Id;
        else if (kind == "supplier-returns") outgoingId = (await api.CreateSupplierReturnAsync(stock.SupplierInput(2m) with { StorageLocationId = destination })).Document.Id;
        else outgoingId = (await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 2m) with { FromLocationId = destination })).Document.Id;
        if (posted) Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync($"/{kind}/{outgoingId}/post", null)).StatusCode);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/unpost", null));
        Assert.Equal(transfer.Document, (await api.ReadAsync<StockTransferResponse>(await api.Client.GetAsync($"/stock-transfers/{transfer.Document.Id}"))).Document);
        Assert.Equal(2, (await api.TransferMovementsAsync(transfer.Document.Id)).Length);
        if (posted) Assert.Equal(HttpStatusCode.OK, (await api.Client.PostAsync($"/{kind}/{outgoingId}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/{kind}/{outgoingId}")).StatusCode);
        await api.UnpostTransferAsync(transfer.Document.Id);
        Assert.Equal((10m, 10m, 0m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal((0m, 0m, 0m), await api.LocationStockAsync(stock.BatchId, destination));
    }

    [Fact]
    public async Task Shared_reserves_and_available_batches_include_sales_supplier_returns_and_transfers()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var sale = await api.CreateSaleAsync(stock, 2m);
        var supplier = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 3m));
        Assert.Equal((10m, 7m, 3m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        var available = Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(
            $"/stock/available-batches?productId={stock.ProductId}&storageLocationId={stock.LocationId}")));
        Assert.Equal((10m, 7m, 3m), (available.PhysicalQuantity, available.ReservedQuantity, available.AvailableQuantity));
        Assert.Empty(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync($"/stock/available-batches?productId={stock.ProductId}&storageLocationId={destination}")));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/stock-transfers", stock.TransferInput(destination, 4m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/sales", stock.SaleInput(4m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(4m), ApiTestHost.Json));
        await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale));
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{supplier.Document.Id}", supplier.EditSupplierReturn(), ApiTestHost.Json));
        await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer(), ApiTestHost.Json));
        await api.PostTransferAsync(transfer.Document.Id);
        await api.PostSaleAsync(sale.Document.Id);
        await api.PostSupplierReturnAsync(supplier.Document.Id);
        Assert.Equal((3m, 0m, 3m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal((3m, 0m, 3m), await api.LocationStockAsync(stock.BatchId, destination));
    }

    [Fact]
    public async Task Concurrent_outgoing_documents_cannot_overbook_one_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/stock-transfers", stock.TransferInput(destination, 6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/sales", stock.SaleInput(6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(6m), ApiTestHost.Json));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        foreach (var denied in responses.Where(x => x.StatusCode != HttpStatusCode.Created)) await SalesTestSupport.InsufficientAsync(denied);
        Assert.Equal((10m, 6m, 4m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Fact]
    public async Task Changing_source_location_excludes_old_reserve_without_subtracting_it_from_the_new_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var b = await api.KnownLocationAsync();
        var c = await api.KnownLocationAsync();
        await api.PostTransferAsync((await api.CreateTransferAsync(stock.TransferInput(b, 4m))).Document.Id);
        var transfer = await api.CreateTransferAsync(stock.TransferInput(c, 2m));
        var edit = transfer.EditTransfer();
        transfer = await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { FromLocationId = b, Lines = [edit.Lines[0] with { Quantity = 4m }] }, ApiTestHost.Json));
        Assert.Equal((6m, 0m, 6m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal((4m, 4m, 0m), await api.LocationStockAsync(stock.BatchId, b));
        await api.PostTransferAsync(transfer.Document.Id);
        Assert.Equal((0m, 0m, 0m), await api.LocationStockAsync(stock.BatchId, b));
        Assert.Equal((4m, 0m, 4m), await api.LocationStockAsync(stock.BatchId, c));
    }

    [Fact]
    public async Task UNPOST_is_allowed_when_current_destination_stock_remains_sufficient_after_consumption()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var first = await api.CreateTransferAsync(stock.TransferInput(destination, 2m));
        await api.PostTransferAsync(first.Document.Id);
        await api.PostTransferAsync((await api.CreateTransferAsync(stock.TransferInput(destination, 5m))).Document.Id);
        var sale = await api.CreateSaleAsync(stock.SaleInput(3m) with { StorageLocationId = destination });
        await api.PostSaleAsync(sale.Document.Id);
        await api.UnpostTransferAsync(first.Document.Id);
        Assert.Equal((2m, 0m, 2m), await api.LocationStockAsync(stock.BatchId, destination));
        Assert.Equal((5m, 2m, 3m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Fact]
    public async Task Transfer_reserve_blocks_unsafe_CustomerReturn_and_purchase_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(quantity: 10m);
        var returned = await api.CreateReturnAsync(sale.ReturnInput(2m));
        await api.PostReturnAsync(returned.Document.Id);
        var transfer = await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 2m));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/customer-returns/{returned.Document.Id}/unpost", null));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/stock-transfers/{transfer.Document.Id}")).StatusCode);
        await api.UnpostReturnAsync(returned.Document.Id);
        var other = await api.ReceiveAsync();
        await api.CreateTransferAsync(other.TransferInput(await api.KnownLocationAsync(), 1m));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/purchases/{other.Purchase.Document.Id}/unpost", null));
    }

    [Fact]
    public async Task POST_rechecks_current_physical_stock_and_failed_edit_preserves_previous_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 2m));
        var edit = transfer.EditTransfer();
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = 11m }] }, ApiTestHost.Json));
        Assert.Equal(2m, (await api.LocationStockAsync(stock.BatchId, stock.LocationId)).Reserved);
        // Simulate an external stock correction; ordinary API writers cannot create this inconsistent state.
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE stock_movements SET "Quantity" = 1 WHERE "DocumentId" = {stock.Purchase.Document.Id}
            """));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/post", null));
        Assert.Empty(await api.TransferMovementsAsync(transfer.Document.Id));
        Assert.Equal(transfer.Document, (await api.ReadAsync<StockTransferResponse>(await api.Client.GetAsync($"/stock-transfers/{transfer.Document.Id}"))).Document);
    }
}
