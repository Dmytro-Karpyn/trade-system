using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SaleReservationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Cannot_reserve_more_than_available_and_no_partial_draft_is_saved()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/sales", stock.SaleInput(10.0001m), ApiTestHost.Json));
        Assert.Empty(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Documents.Where(x => x.DocumentType == DocumentType.SALE_INVOICE).ToListAsync()));
        Assert.Equal(0m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Own_full_reserve_does_not_block_editing_the_same_draft()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock, 10m);
        Assert.Equal(0m, (await api.BalanceAsync(stock)).AvailableQuantity);
        var input = SalesTestSupport.Edit(sale) with
        {
            Comment = "Edited", DiscountPercent = 5m,
            Lines = [new(sale.Lines[0].Id, 1, stock.ProductId, stock.BatchId, 10m, 15m)]
        };
        var edited = await api.UpdateSaleAsync(sale.Document.Id, input);
        Assert.Equal(2, edited.Document.Version);
        Assert.Equal("Edited", edited.Document.Comment);
        Assert.Equal(15m, edited.Lines[0].UnitPrice);
        var balance = await api.BalanceAsync(stock);
        Assert.Equal((10m, 10m, 0m), (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity));
    }

    [Fact]
    public async Task Editing_excludes_only_own_reserve_and_preserves_it_on_failure()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock, 4m);
        await api.CreateSaleAsync(stock, 3m);
        var updated = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
            { Lines = [new(sale.Lines[0].Id, 1, stock.ProductId, stock.BatchId, 7m, 12m)] });
        Assert.Equal(10m, (await api.BalanceAsync(stock)).ReservedQuantity);
        var invalid = SalesTestSupport.Edit(updated) with
            { Lines = [new(updated.Lines[0].Id, 1, stock.ProductId, stock.BatchId, 8m, 12m)] };
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}", invalid, ApiTestHost.Json));
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(updated.Document, current.Document);
        Assert.Equal(7m, current.Lines[0].Quantity);
        Assert.Equal(10m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Removed_lines_do_not_reserve_and_a_batch_can_be_reused_by_a_new_active_line()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var updated = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
            { Lines = [new(null, 1, stock.ProductId, stock.BatchId, 6m, 12m)] });
        Assert.Equal(2, updated.Lines.Count);
        Assert.NotNull(updated.Lines.Single(x => x.Id == sale.Lines[0].Id).RemovedAt);
        Assert.Equal(6m, (await api.BalanceAsync(stock)).ReservedQuantity);
        var empty = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(updated) with { Lines = [] });
        Assert.All(empty.Lines, x => Assert.NotNull(x.RemovedAt));
        Assert.Equal(0m, (await api.BalanceAsync(stock)).ReservedQuantity);
        var restored = await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(empty) with
            { Lines = [new(sale.Lines[0].Id, 1, stock.ProductId, stock.BatchId, 4m, 12m)] });
        Assert.Null(restored.Lines.Single(x => x.Id == sale.Lines[0].Id).RemovedAt);
        Assert.Equal(4m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Storage_location_is_part_of_availability_and_failed_location_edit_keeps_old_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var emptyLocation = Guid.NewGuid();
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsJsonAsync("/sales",
            stock.SaleInput() with { StorageLocationId = emptyLocation }, ApiTestHost.Json));
        var sale = await api.CreateSaleAsync(stock);
        await SalesTestSupport.InsufficientAsync(await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}",
            SalesTestSupport.Edit(sale) with { StorageLocationId = emptyLocation }, ApiTestHost.Json));
        Assert.Equal(4m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    [Fact]
    public async Task Available_batches_return_balances_and_oldest_purchase_dates_first()
    {
        await using var api = await fixture.CreateApiAsync();
        var recent = await api.ReceiveAsync(date: new DateOnly(2026, 10, 22));
        var old = await api.ReceiveAsync(6m, recent.ProductId, recent.LocationId, new DateOnly(2026, 10, 1));
        await api.CreateSaleAsync(old, 2m);
        var url = $"/stock/available-batches?productId={old.ProductId}&storageLocationId={old.LocationId}";
        var batches = await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(url));
        Assert.Equal(new[] { old.BatchId, recent.BatchId }, batches.Select(x => x.BatchId));
        Assert.Equal(new DateOnly(2026, 10, 1), batches[0].PurchaseDocumentDate);
        Assert.Equal((6m, 2m, 4m), (batches[0].PhysicalQuantity, batches[0].ReservedQuantity, batches[0].AvailableQuantity));
        Assert.All(batches, x =>
        {
            Assert.Equal(old.ProductId, x.ProductId);
            Assert.Equal(old.LocationId, x.StorageLocationId);
            Assert.False(string.IsNullOrWhiteSpace(x.BatchNumber));
        });
        await api.CreateSaleAsync(recent, 10m);
        Assert.Equal(old.BatchId, Assert.Single(await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(url))).BatchId);
        var otherLocation = await api.ReadAsync<AvailableBatchResponse[]>(await api.Client.GetAsync(
            $"/stock/available-batches?productId={old.ProductId}&storageLocationId={Guid.NewGuid()}"));
        Assert.Empty(otherLocation);
    }

    [Fact]
    public async Task Atomic_edit_can_swap_batches_between_two_lines()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(10m, a.ProductId, a.LocationId);
        var sale = await api.CreateSaleAsync(api.NewInput(a.LocationId,
            [new(null, 1, a.ProductId, a.BatchId, 2m, 1m), new(null, 2, a.ProductId, b.BatchId, 3m, 1m)]));
        await api.UpdateSaleAsync(sale.Document.Id, SalesTestSupport.Edit(sale) with
        {
            Lines = [new(sale.Lines[0].Id, 1, a.ProductId, b.BatchId, 2m, 1m),
                     new(sale.Lines[1].Id, 2, a.ProductId, a.BatchId, 3m, 1m)]
        });
        Assert.Equal(3m, (await api.BalanceAsync(a)).ReservedQuantity);
        Assert.Equal(2m, (await api.BalanceAsync(b)).ReservedQuantity);
    }

    [Fact]
    public async Task Purchase_UNPOST_cannot_remove_reserved_stock_and_works_after_deleting_the_draft()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null));
        var purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}"));
        Assert.Equal(DocumentStatus.POSTED, purchase.Document.Status);
        Assert.Equal(10m, (await api.BalanceAsync(stock)).PhysicalQuantity);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        await api.UnpostAsync(stock.Purchase.Document.Id);
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances")));
    }

    [Fact]
    public async Task Purchase_UNPOST_cannot_make_physical_stock_negative_after_a_sale()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        await api.PostSaleAsync(sale.Document.Id);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null));
        Assert.Equal(6m, (await api.BalanceAsync(stock)).PhysicalQuantity);
        await api.UnpostSaleAsync(sale.Document.Id);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null));
    }

    [Fact]
    public async Task Concurrent_draft_creation_cannot_overreserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            api.Client.PostAsJsonAsync("/sales", stock.SaleInput(7m), ApiTestHost.Json)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        foreach (var rejected in responses.Where(x => x.StatusCode != HttpStatusCode.Created))
            await SalesTestSupport.InsufficientAsync(rejected);
        Assert.Single(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        Assert.Equal((7m, 3m), ((await api.BalanceAsync(stock)).ReservedQuantity, (await api.BalanceAsync(stock)).AvailableQuantity));
    }

    [Fact]
    public async Task Concurrent_edits_exclude_only_their_own_reserve_and_cannot_overreserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var a = await api.CreateSaleAsync(stock, 3m);
        var b = await api.CreateSaleAsync(stock, 3m);
        var responses = await Task.WhenAll(new[] { a, b }.Select(sale => api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}",
            SalesTestSupport.Edit(sale) with { Lines = [new(sale.Lines[0].Id, 1, stock.ProductId, stock.BatchId, 6m, 12m)] }, ApiTestHost.Json)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await SalesTestSupport.InsufficientAsync(Assert.Single(responses, x => x.StatusCode != HttpStatusCode.OK));
        Assert.Equal((9m, 1m), ((await api.BalanceAsync(stock)).ReservedQuantity, (await api.BalanceAsync(stock)).AvailableQuantity));
    }

    [Fact]
    public async Task Concurrent_purchase_UNPOST_and_reservation_cannot_both_succeed()
    {
        await using var api = await fixture.CreateApiAsync();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var stock = await api.ReceiveAsync();
            var responses = await Task.WhenAll(api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null),
                api.Client.PostAsJsonAsync("/sales", stock.SaleInput(), ApiTestHost.Json));
            var unposted = responses[0].StatusCode == HttpStatusCode.OK;
            var reserved = responses[1].StatusCode == HttpStatusCode.Created;
            Assert.NotEqual(unposted, reserved);
            await SalesTestSupport.InsufficientAsync(responses[unposted ? 1 : 0]);
            if (reserved) Assert.Equal((10m, 4m), ((await api.BalanceAsync(stock)).PhysicalQuantity, (await api.BalanceAsync(stock)).ReservedQuantity));
        }
    }
}
