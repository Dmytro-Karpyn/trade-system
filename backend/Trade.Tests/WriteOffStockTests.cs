using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class WriteOffStockTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task All_four_outgoing_types_share_stock_and_available_batches_and_only_own_reserve_is_excluded()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var reason = await api.CreateReasonAsync();
        var sale = await api.CreateSaleAsync(stock, 2m);
        var supplier = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        var transfer = await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 2m));
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id, 3m));
        Assert.Equal((10m, 9m, 1m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        var batch = Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(
            $"/stock/available-batches?productId={stock.ProductId}&storageLocationId={stock.LocationId}")));
        Assert.Equal((10m, 9m, 1m), (batch.PhysicalQuantity, batch.ReservedQuantity, batch.AvailableQuantity));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/write-offs", stock.WriteOffInput(reason.Id, 2m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/sales", stock.SaleInput(2m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(2m), ApiTestHost.Json));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/stock-transfers", stock.TransferInput(transfer.ToLocationId, 2m), ApiTestHost.Json));
        var edit = writeOff.EditWriteOff();
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = 5m }] }, ApiTestHost.Json));
        Assert.Equal((10m, 9m, 1m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale));
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{supplier.Document.Id}", supplier.EditSupplierReturn(), ApiTestHost.Json));
        await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer(), ApiTestHost.Json));
        await api.PostWriteOffAsync(writeOff.Document.Id);
        await api.PostSaleAsync(sale.Document.Id);
        await api.PostSupplierReturnAsync(supplier.Document.Id);
        await api.PostTransferAsync(transfer.Document.Id);
        Assert.Equal((1m, 0m, 1m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Fact]
    public async Task Concurrent_outgoing_reservations_cannot_overbook_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var reason = await api.CreateReasonAsync();
        var destination = await api.KnownLocationAsync();
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/write-offs", stock.WriteOffInput(reason.Id, 6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/sales", stock.SaleInput(6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(6m), ApiTestHost.Json),
            api.Client.PostAsJsonAsync("/stock-transfers", stock.TransferInput(destination, 6m), ApiTestHost.Json));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        foreach (var denied in responses.Where(x => x.StatusCode != HttpStatusCode.Created)) await SalesTestSupport.InsufficientAsync(denied);
        Assert.Equal((10m, 6m, 4m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Fact]
    public async Task Changing_location_and_batch_releases_old_reserve_and_checks_new_location_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(a.WriteOffInput((await api.CreateReasonAsync()).Id, 4m));
        var edit = writeOff.EditWriteOff();
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { StorageLocationId = b.LocationId }, ApiTestHost.Json));
        writeOff = await api.ReadAsync<WriteOffResponse>(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { StorageLocationId = b.LocationId, Lines = [edit.Lines[0] with { BatchId = b.BatchId, Quantity = 6m }] }, ApiTestHost.Json));
        Assert.Equal((10m, 0m, 10m), await api.LocationStockAsync(a.BatchId, a.LocationId));
        Assert.Equal((10m, 6m, 4m), await api.LocationStockAsync(b.BatchId, b.LocationId));
        await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal((4m, 0m, 4m), await api.LocationStockAsync(b.BatchId, b.LocationId));
    }

    [Fact]
    public async Task UNPOST_restores_own_reserve_without_disturbing_other_reservations()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        await api.PostWriteOffAsync(writeOff.Document.Id);
        await api.CreateSaleAsync(stock, 8m);
        Assert.Equal((8m, 8m, 0m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        await api.UnpostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal((10m, 10m, 0m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteOff_reserve_or_issue_blocks_unsafe_transfer_UNPOST(bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 10m));
        await api.PostTransferAsync(transfer.Document.Id);
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id) with { StorageLocationId = destination });
        if (posted) writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/unpost", null));
        if (posted) await api.UnpostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/write-offs/{writeOff.Document.Id}")).StatusCode);
        await api.UnpostTransferAsync(transfer.Document.Id);
    }

    [Fact]
    public async Task WriteOff_reserve_blocks_unsafe_customer_return_and_purchase_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(quantity: 10m);
        var returned = await api.CreateReturnAsync(sale.ReturnInput(2m));
        await api.PostReturnAsync(returned.Document.Id);
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/customer-returns/{returned.Document.Id}/unpost", null));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/write-offs/{writeOff.Document.Id}")).StatusCode);
        await api.UnpostReturnAsync(returned.Document.Id);
        var other = await api.ReceiveAsync();
        await api.CreateWriteOffAsync(other.WriteOffInput(reason.Id));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/purchases/{other.Purchase.Document.Id}/unpost", null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task POST_and_UNPOST_recheck_current_state_and_reject_unsafe_external_corrections(bool unpost)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        if (unpost) writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        // Isolated test DB only: simulate an external import bypassing normal stock locks and validation.
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE stock_movements SET "Quantity" = 1 WHERE "DocumentId" = {stock.Purchase.Document.Id}
            """));
        var action = unpost ? "unpost" : "post";
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/write-offs/{writeOff.Document.Id}/{action}", null));
        Assert.Equal(writeOff.Document, (await api.ReadAsync<WriteOffResponse>(await api.Client.GetAsync($"/write-offs/{writeOff.Document.Id}"))).Document);
        Assert.Equal(unpost ? 1 : 0, (await api.TransferMovementsAsync(writeOff.Document.Id)).Length);
    }
}
