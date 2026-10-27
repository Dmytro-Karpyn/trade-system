using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Purchasing;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class PurchaseLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Draft_does_not_create_batches_movements_or_number()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        Assert.Equal(DocumentStatus.DRAFT, p.Document.Status);
        Assert.Null(p.Document.Number);
        Assert.Equal(api.ActorId, p.Document.CreatedBy);
        Assert.Equal(1, p.Document.Version);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Post_creates_batch_and_positive_stock_movement()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var posted = await api.PostAsync(p.Document.Id);
        var batch = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Equal(p.Lines[0].Id, batch.PurchaseInvoiceLineId);
        Assert.False(string.IsNullOrWhiteSpace(batch.BatchNumber));
        Assert.Equal(batch.Id, movement.BatchId);
        Assert.Equal(p.Document.Id, movement.DocumentId);
        Assert.Equal(p.StorageLocationId, movement.StorageLocationId);
        Assert.Equal(p.Lines[0].Quantity, movement.Quantity);
        Assert.Equal(DocumentStatus.POSTED, posted.Document.Status);
        Assert.StartsWith("PI-", posted.Document.Number);
        Assert.Equal(api.ActorId, posted.Document.PostedBy);
        Assert.NotNull(posted.Document.PostedAt);
        Assert.Equal(2, posted.Document.Version);
    }

    [Fact]
    public async Task Two_lines_of_the_same_product_create_distinct_batches_and_balances()
    {
        await using var api = await fixture.CreateApiAsync();
        var product = await api.CreateProductAsync();
        var input = new PurchaseRequest(DocumentType.PURCHASE_INVOICE, new DateOnly(2026, 10, 19), null, null,
            api.CounterpartyId, Guid.NewGuid(), Guid.NewGuid(), 1m, null, null, 0m,
            [new(null, 1, product.Id, 1.5m, 10m), new(null, 2, product.Id, 2.5m, 20m)]);
        var p = await api.ReadAsync<PurchaseResponse>(await api.Client.PostAsJsonAsync("/purchases", input, ApiTestHost.Json), HttpStatusCode.Created);
        await api.PostAsync(p.Document.Id);
        var batches = await api.QueryAsync(db => db.Batches.ToListAsync());
        Assert.Equal(2, batches.Count);
        Assert.Equal(p.Lines.Select(x => x.Id).Order(), batches.Select(x => x.PurchaseInvoiceLineId).Order());
        var balances = await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances"));
        Assert.Equal(new[] { 1.5m, 2.5m }, balances.Select(x => x.PhysicalQuantity).Order());
        Assert.Equal(2, balances.Select(x => x.BatchId).Distinct().Count());
    }

    [Fact]
    public async Task Repeated_post_is_idempotent_including_audit_and_version()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var first = await api.PostAsync(p.Document.Id);
        var second = await api.PostAsync(p.Document.Id);
        Assert.Equal(first.Document, second.Document);
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Concurrent_posts_create_exactly_one_batch_and_movement()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostAsync(p.Document.Id)));
        Assert.All(results, r => Assert.Equal(results[0].Document, r.Document));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Unpost_removes_movements_and_keeps_batches_and_number()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var posted = await api.PostAsync(p.Document.Id);
        var batch = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var draft = await api.UnpostAsync(p.Document.Id);
        Assert.Equal(DocumentStatus.DRAFT, draft.Document.Status);
        Assert.Equal(posted.Document.Number, draft.Document.Number);
        Assert.Null(draft.Document.PostedBy);
        Assert.Null(draft.Document.PostedAt);
        Assert.Equal(3, draft.Document.Version);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Equal(batch.Id, Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync())).Id);
        Assert.Empty(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances")));
        var repeated = await api.UnpostAsync(p.Document.Id);
        Assert.Equal(draft.Document, repeated.Document);
    }

    [Fact]
    public async Task Repost_reuses_batch_and_number_and_applies_edited_quantity_date_and_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var first = await api.PostAsync(p.Document.Id);
        var batch = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var draft = await api.UnpostAsync(p.Document.Id);
        var location = Guid.NewGuid();
        var update = ApiTestHost.Edit(draft) with
        {
            DocumentDate = new DateOnly(2026, 10, 20), StorageLocationId = location,
            Lines = [new(draft.Lines[0].Id, 1, draft.Lines[0].ProductId, 8.125m, 12m)]
        };
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", update, ApiTestHost.Json));
        var reposted = await api.PostAsync(p.Document.Id);
        var after = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Equal((batch.Id, batch.BatchNumber, batch.CreatedAt), (after.Id, after.BatchNumber, after.CreatedAt));
        Assert.Equal(first.Document.Number, reposted.Document.Number);
        Assert.Equal(update.DocumentDate, reposted.Document.DocumentDate);
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Equal((batch.Id, location, 8.125m), (movement.BatchId, movement.StorageLocationId, movement.Quantity));
    }

    [Fact]
    public async Task Product_cannot_change_after_batch_creation_even_after_unpost()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        await api.PostAsync(p.Document.Id);
        var draft = await api.UnpostAsync(p.Document.Id);
        var replacement = await api.CreateProductAsync();
        var update = ApiTestHost.Edit(draft) with
            { Lines = [new(draft.Lines[0].Id, 1, replacement.Id, 3m, 10m)] };
        var response = await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", update, ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var current = await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{p.Document.Id}"));
        Assert.Equal(draft.Document, current.Document);
        Assert.Equal(draft.Lines[0].ProductId, current.Lines[0].ProductId);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
    }

    [Fact]
    public async Task Product_can_change_in_a_never_posted_draft()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var replacement = await api.CreateProductAsync();
        var update = ApiTestHost.Edit(p) with { Lines = [new(p.Lines[0].Id, 1, replacement.Id, 4m, 10m)] };
        var edited = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", update, ApiTestHost.Json));
        Assert.Equal(replacement.Id, edited.Lines[0].ProductId);
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Fact]
    public async Task Removed_lines_keep_batches_but_do_not_create_movements_and_new_lines_get_new_batches()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        await api.PostAsync(p.Document.Id);
        var oldBatch = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var draft = await api.UnpostAsync(p.Document.Id);
        var product = await api.CreateProductAsync();
        var edited = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}",
            ApiTestHost.Edit(draft, [new(null, 1, product.Id, 2m, 0m)]), ApiTestHost.Json));
        Assert.NotNull(edited.Lines.Single(x => x.Id == p.Lines[0].Id).RemovedAt);
        await api.PostAsync(p.Document.Id);
        Assert.Equal(2, await api.QueryAsync(db => db.Batches.CountAsync()));
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.NotEqual(oldBatch.Id, movement.BatchId);
        Assert.Equal(2m, movement.Quantity);
    }

    [Fact]
    public async Task Posted_document_cannot_be_edited_and_stale_draft_version_returns_conflict()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync();
        var input = ApiTestHost.Edit(p);
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", input, ApiTestHost.Json));
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", input, ApiTestHost.Json)).StatusCode);
        var posted = await api.PostAsync(p.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/purchases/{p.Document.Id}", ApiTestHost.Edit(posted), ApiTestHost.Json)).StatusCode);
    }

    [Fact]
    public async Task Stock_view_sums_movements_by_batch_and_location_without_storing_derived_fields()
    {
        await using var api = await fixture.CreateApiAsync();
        var p = await api.CreatePurchaseAsync(quantity: 1.125m);
        await api.PostAsync(p.Document.Id);
        var batch = Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var otherLocation = Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            var document = Document.Create(api.OrganizationId, DocumentType.OPENING_STOCK, p.Document.DocumentDate,
                null, null, api.ActorId, DateTimeOffset.UtcNow);
            document.Post("VIEW-TEST", api.ActorId, DateTimeOffset.UtcNow);
            db.Documents.Add(document);
            db.StockMovements.AddRange(
                StockMovement.Receipt(document.Id, batch.Id, p.StorageLocationId, 2.25m),
                StockMovement.Receipt(document.Id, batch.Id, otherLocation, 4.5m));
            await db.SaveChangesAsync();
        });
        var balances = await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync("/stock/balances"));
        Assert.Equal(2, balances.Length);
        Assert.Equal(3.375m, balances.Single(x => x.StorageLocationId == p.StorageLocationId).PhysicalQuantity);
        Assert.Equal(4.5m, balances.Single(x => x.StorageLocationId == otherLocation).PhysicalQuantity);
        Assert.All(balances, x => Assert.Equal(batch.Id, x.BatchId));
        var filtered = await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync($"/stock/balances?storageLocationId={otherLocation}&batchId={batch.Id}"));
        Assert.Equal(4.5m, Assert.Single(filtered).PhysicalQuantity);
    }
}
