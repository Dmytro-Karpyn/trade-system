using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class StockTransferLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task DRAFT_reserves_only_source_and_does_not_create_movements_or_destination_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination));
        Assert.Equal(DocumentType.STOCK_TRANSFER, transfer.Document.DocumentType);
        Assert.Equal(DocumentStatus.DRAFT, transfer.Document.Status);
        Assert.Null(transfer.Document.Number);
        Assert.Equal((10m, 2m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Empty(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync($"/stock/balances?batchId={stock.BatchId}&storageLocationId={destination}")));
        Assert.Empty(await api.TransferMovementsAsync(transfer.Document.Id));
        Assert.Empty(await api.SettlementsAsync(transfer.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Fact]
    public async Task POST_creates_exact_opposite_movements_in_same_batch_without_new_batches_or_financial_movements()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination, 3m));
        var costs = await api.BatchCostsAsync();
        transfer = await api.PostTransferAsync(transfer.Document.Id);
        Assert.StartsWith("ST-", transfer.Document.Number);
        var movements = await api.TransferMovementsAsync(transfer.Document.Id);
        Assert.Equal(2, movements.Length);
        Assert.All(movements, x => Assert.Equal(stock.BatchId, x.BatchId));
        Assert.Equal(-3m, Assert.Single(movements, x => x.StorageLocationId == stock.LocationId).Quantity);
        Assert.Equal(3m, Assert.Single(movements, x => x.StorageLocationId == destination).Quantity);
        Assert.Equal(0m, movements.Sum(x => x.Quantity));
        Assert.Equal((7m, 0m, 7m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal((3m, 0m, 3m), await api.LocationStockAsync(stock.BatchId, destination));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.SettlementsAsync(transfer.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        Assert.Equal(costs, await api.BatchCostsAsync());
    }

    [Fact]
    public async Task Own_DRAFT_reserve_does_not_block_editing_and_version_and_POSTED_edit_guards_remain_enforced()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync(), 8m));
        var edit = transfer.EditTransfer();
        transfer = await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = 10m }], Comment = "Updated" }, ApiTestHost.Json));
        Assert.Equal("Updated", transfer.Document.Comment);
        Assert.Equal((10m, 10m, 0m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        transfer = await api.PostTransferAsync(transfer.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer(), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_POST_is_idempotent_and_UNPOST_repost_keeps_number_and_restores_only_source_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(destination));
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostTransferAsync(transfer.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Equal(2, (await api.TransferMovementsAsync(transfer.Document.Id)).Length);
        var draft = await api.UnpostTransferAsync(transfer.Document.Id);
        Assert.Equal(DocumentStatus.DRAFT, draft.Document.Status);
        Assert.Empty(await api.TransferMovementsAsync(transfer.Document.Id));
        Assert.Equal((10m, 2m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal((0m, 0m, 0m), await api.LocationStockAsync(stock.BatchId, destination));
        Assert.Equal(draft.Document, (await api.UnpostTransferAsync(transfer.Document.Id)).Document);
        Assert.Equal(posts[0].Document.Number, (await api.PostTransferAsync(transfer.Document.Id)).Document.Number);
        var second = await api.CreateTransferAsync(stock.TransferInput(destination, 1m));
        Assert.NotEqual(posts[0].Document.Number, (await api.PostTransferAsync(second.Document.Id)).Document.Number);
    }

    [Fact]
    public async Task DRAFT_delete_is_logical_and_releases_reserve_while_POSTED_delete_requires_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var transfer = await api.CreateTransferAsync(stock.TransferInput(await api.KnownLocationAsync()));
        transfer = await api.PostTransferAsync(transfer.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/stock-transfers/{transfer.Document.Id}")).StatusCode);
        Assert.Equal(2, (await api.TransferMovementsAsync(transfer.Document.Id)).Length);
        transfer = await api.UnpostTransferAsync(transfer.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/stock-transfers/{transfer.Document.Id}")).StatusCode);
        var stored = await api.QueryAsync(db => db.StockTransfers.Include(x => x.Document).Include(x => x.Lines).SingleAsync());
        Assert.NotNull(stored.Document.DeletedAt);
        Assert.Equal(api.ActorId, stored.Document.DeletedBy);
        Assert.Single(stored.Lines);
        Assert.Equal((10m, 0m, 10m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Empty(await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync("/stock-transfers")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/stock-transfers/{transfer.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/unpost", null)).StatusCode);
    }

    [Fact]
    public async Task DRAFT_can_swap_batches_and_line_numbers_replace_lines_and_release_removed_reserves()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(quantity: 7m, locationId: a.LocationId);
        var transfer = await api.CreateTransferAsync(a.TransferInput(await api.KnownLocationAsync()) with
            { Lines = [new(null, 1, a.BatchId, 2m), new(null, 2, b.BatchId, 3m)] });
        var edit = transfer.EditTransfer();
        transfer = await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
        {
            Lines = [edit.Lines[0] with { BatchId = b.BatchId, LineNo = 2 }, edit.Lines[1] with { BatchId = a.BatchId, LineNo = 1 }]
        }, ApiTestHost.Json));
        Assert.Equal(3m, (await api.LocationStockAsync(a.BatchId, a.LocationId)).Reserved);
        Assert.Equal(2m, (await api.LocationStockAsync(b.BatchId, a.LocationId)).Reserved);
        edit = transfer.EditTransfer();
        transfer = await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", edit with
            { Lines = [edit.Lines.Single(x => x.BatchId == a.BatchId)] }, ApiTestHost.Json));
        Assert.Single(await api.QueryAsync(db => db.StockTransferLines.ToListAsync()));
        Assert.Equal(0m, (await api.LocationStockAsync(b.BatchId, a.LocationId)).Reserved);
        transfer = await api.ReadAsync<StockTransferResponse>(await api.Client.PutAsJsonAsync($"/stock-transfers/{transfer.Document.Id}", transfer.EditTransfer() with
            { Lines = [] }, ApiTestHost.Json));
        Assert.Empty(transfer.Lines);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/stock-transfers/{transfer.Document.Id}/post", null)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.StockTransferLines.ToListAsync()));
        Assert.Equal(0m, (await api.LocationStockAsync(a.BatchId, a.LocationId)).Reserved);
    }

    [Fact]
    public async Task List_filters_are_inclusive_compose_and_exclude_deleted_documents()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var destination = await api.KnownLocationAsync();
        var next = await api.KnownLocationAsync();
        var a = await api.CreateTransferAsync(stock.TransferInput(destination));
        a = await api.PostTransferAsync(a.Document.Id);
        var b = await api.CreateTransferAsync(stock.TransferInput(next, 1m) with { FromLocationId = destination, DocumentDate = new(2026, 10, 19) });
        var deleted = await api.CreateTransferAsync(stock.TransferInput(destination, 1m));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/stock-transfers/{deleted.Document.Id}")).StatusCode);
        foreach (var filter in new[] { "from=2026-10-20", $"fromLocationId={stock.LocationId}", $"toLocationId={destination}", "status=POSTED",
            $"from=2026-10-20&to=2026-10-20&fromLocationId={stock.LocationId}&toLocationId={destination}&status=POSTED" })
            Assert.Equal(a.Document.Id, Assert.Single(await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync($"/stock-transfers?{filter}"))).Document.Id);
        Assert.Equal(b.Document.Id, Assert.Single(await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync("/stock-transfers?to=2026-10-19"))).Document.Id);
        Assert.Equal(2, (await api.ReadAsync<StockTransferResponse[]>(await api.Client.GetAsync("/stock-transfers"))).Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/stock-transfers?from=2026-10-21&to=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/stock-transfers?status=UNKNOWN")).StatusCode);
    }
}
