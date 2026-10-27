using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class WriteOffLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Reason_CRUD_trims_name_and_maps_to_existing_expense_category_without_seed_data()
    {
        await using var api = await fixture.CreateApiAsync();
        Assert.Empty(await api.ReadAsync<WriteOffReasonResponse[]>(await api.Client.GetAsync("/write-off-reasons")));
        var category = await api.CreateCategoryAsync("Damage");
        var reason = await api.CreateReasonAsync(category.Id, " Damage ");
        Assert.Equal("Damage", reason.Name);
        Assert.Equal(category.Id, reason.ExpenseCategoryId);
        Assert.Equal(api.OrganizationId, reason.OrganizationId);
        Assert.Equal(reason, await api.ReadAsync<WriteOffReasonResponse>(await api.Client.GetAsync($"/write-off-reasons/{reason.Id}")));
        var other = await api.CreateCategoryAsync("Internal use");
        reason = await api.ReadAsync<WriteOffReasonResponse>(await api.Client.PutAsJsonAsync($"/write-off-reasons/{reason.Id}",
            new WriteOffReasonRequest("Changed", other.Id, false), ApiTestHost.Json));
        Assert.Equal(("Changed", other.Id, false), (reason.Name, reason.ExpenseCategoryId, reason.Active));
        Assert.Equal(reason, Assert.Single(await api.ReadAsync<WriteOffReasonResponse[]>(await api.Client.GetAsync("/write-off-reasons"))));
    }

    [Fact]
    public async Task DRAFT_only_reserves_and_POST_creates_negative_stock_without_financial_expense_or_batch_side_effects()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id));
        Assert.Equal(DocumentType.WRITE_OFF, writeOff.Document.DocumentType);
        Assert.Equal(DocumentStatus.DRAFT, writeOff.Document.Status);
        Assert.Null(writeOff.Document.Number);
        Assert.Equal((10m, 2m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Empty(await api.TransferMovementsAsync(writeOff.Document.Id));
        Assert.Empty(await api.SettlementsAsync(writeOff.Document.Id));
        var costs = await api.BatchCostsAsync();
        writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.StartsWith("WO-", writeOff.Document.Number);
        var movement = Assert.Single(await api.TransferMovementsAsync(writeOff.Document.Id));
        Assert.Equal((stock.BatchId, stock.LocationId, -2m), (movement.BatchId, movement.StorageLocationId, movement.Quantity));
        Assert.Equal((8m, 0m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Empty(await api.SettlementsAsync(writeOff.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Expenses.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Equal(costs, await api.BatchCostsAsync());
    }

    [Fact]
    public async Task Own_DRAFT_reserve_does_not_block_editing_and_version_and_POSTED_edit_checks_are_enforced()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id, 8m));
        var edit = writeOff.EditWriteOff();
        writeOff = await api.ReadAsync<WriteOffResponse>(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { Quantity = 10m }], Comment = "Changed" }, ApiTestHost.Json));
        Assert.Equal("Changed", writeOff.Document.Comment);
        Assert.Equal((10m, 10m, 0m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", writeOff.EditWriteOff(), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_POST_is_idempotent_and_UNPOST_restores_stock_and_reserve_without_changing_number()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostWriteOffAsync(writeOff.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Single(await api.TransferMovementsAsync(writeOff.Document.Id));
        var draft = await api.UnpostWriteOffAsync(writeOff.Document.Id);
        Assert.Empty(await api.TransferMovementsAsync(writeOff.Document.Id));
        Assert.Equal((10m, 2m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Equal(draft.Document, (await api.UnpostWriteOffAsync(writeOff.Document.Id)).Document);
        Assert.Equal(posts[0].Document.Number, (await api.PostWriteOffAsync(writeOff.Document.Id)).Document.Number);
        var other = await api.CreateWriteOffAsync(stock.WriteOffInput(writeOff.WriteOffReasonId, 1m));
        Assert.NotEqual(posts[0].Document.Number, (await api.PostWriteOffAsync(other.Document.Id)).Document.Number);
    }

    [Fact]
    public async Task POSTED_delete_is_forbidden_and_DRAFT_logical_delete_keeps_lines_and_releases_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/write-offs/{writeOff.Document.Id}")).StatusCode);
        Assert.Single(await api.TransferMovementsAsync(writeOff.Document.Id));
        writeOff = await api.UnpostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/write-offs/{writeOff.Document.Id}")).StatusCode);
        var stored = await api.QueryAsync(db => db.WriteOffs.Include(x => x.Document).Include(x => x.Lines).SingleAsync());
        Assert.NotNull(stored.Document.DeletedAt);
        Assert.Equal(api.ActorId, stored.Document.DeletedBy);
        Assert.Single(stored.Lines);
        Assert.Equal((10m, 0m, 10m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
        Assert.Empty(await api.WriteOffCostsAsync());
        Assert.Empty(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync("/write-offs")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/write-offs/{writeOff.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", writeOff.EditWriteOff(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/write-offs/{writeOff.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/write-offs/{writeOff.Document.Id}/unpost", null)).StatusCode);
    }

    [Fact]
    public async Task DRAFT_lines_can_swap_batches_and_numbers_and_be_cleared_without_stale_reserves()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync(locationId: a.LocationId);
        var writeOff = await api.CreateWriteOffAsync(a.WriteOffInput((await api.CreateReasonAsync()).Id) with
            { Lines = [new(null, 1, a.BatchId, 2m), new(null, 2, b.BatchId, 3m)] });
        var edit = writeOff.EditWriteOff();
        writeOff = await api.ReadAsync<WriteOffResponse>(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { BatchId = b.BatchId, LineNo = 2 }, edit.Lines[1] with { BatchId = a.BatchId, LineNo = 1 }] }, ApiTestHost.Json));
        Assert.Equal(3m, (await api.LocationStockAsync(a.BatchId, a.LocationId)).Reserved);
        Assert.Equal(2m, (await api.LocationStockAsync(b.BatchId, b.LocationId)).Reserved);
        writeOff = await api.ReadAsync<WriteOffResponse>(await api.Client.PutAsJsonAsync($"/write-offs/{writeOff.Document.Id}", writeOff.EditWriteOff() with
            { Lines = [] }, ApiTestHost.Json));
        Assert.Empty(await api.QueryAsync(db => db.WriteOffLines.ToListAsync()));
        Assert.Equal(0m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        Assert.Equal(0m, (await api.LocationStockAsync(a.BatchId, a.LocationId)).Reserved);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/write-offs/{writeOff.Document.Id}/post", null)).StatusCode);
    }

    [Fact]
    public async Task List_supports_all_six_filters_inclusively_and_excludes_deleted_documents()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.ReceiveAsync();
        var reasonA = await api.CreateReasonAsync();
        var reasonB = await api.CreateReasonAsync();
        var first = await api.CreateWriteOffAsync(a.WriteOffInput(reasonA.Id));
        await api.PostWriteOffAsync(first.Document.Id);
        var second = await api.CreateWriteOffAsync(b.WriteOffInput(reasonB.Id) with { DocumentDate = new(2026, 10, 23) });
        var deleted = await api.CreateWriteOffAsync(a.WriteOffInput(reasonA.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/write-offs/{deleted.Document.Id}")).StatusCode);
        var filters = new[] { "from=2026-10-24", $"storageLocationId={a.LocationId}", $"writeOffReasonId={reasonA.Id}",
            $"expenseCategoryId={reasonA.ExpenseCategoryId}", "status=POSTED", "from=2026-10-24&to=2026-10-24&status=POSTED" };
        foreach (var filter in filters)
            Assert.Equal(first.Document.Id, Assert.Single(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync($"/write-offs?{filter}"))).Document.Id);
        Assert.Equal(second.Document.Id, Assert.Single(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync("/write-offs?to=2026-10-23"))).Document.Id);
        Assert.Equal(2, (await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync("/write-offs"))).Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/write-offs?from=2026-10-25&to=2026-10-24")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/write-offs?status=UNKNOWN")).StatusCode);
    }
}
