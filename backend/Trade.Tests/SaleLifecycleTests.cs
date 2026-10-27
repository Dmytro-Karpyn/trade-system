using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Sales;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SaleLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Draft_GOODS_reserves_stock_without_creating_movements_or_batches()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        Assert.Equal(DocumentStatus.DRAFT, sale.Document.Status);
        Assert.Null(sale.Document.Number);
        Assert.Equal(1, sale.Document.Version);
        Assert.Equal(api.OrganizationId, sale.Document.OrganizationId);
        Assert.Equal(api.ActorId, sale.Document.CreatedBy);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        var balance = await api.BalanceAsync(stock);
        Assert.Equal((10m, 4m, 6m), (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity));
    }

    [Fact]
    public async Task SERVICE_draft_and_post_do_not_reserve_or_move_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(api.NewInput(stock.LocationId,
            [new(null, 1, service.Id, null, 100m, 0m)]));
        Assert.Equal((10m, 0m, 10m), State(await api.BalanceAsync(stock)));
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(DocumentStatus.POSTED, posted.Document.Status);
        Assert.Null(posted.Lines[0].BatchId);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((10m, 0m, 10m), State(await api.BalanceAsync(stock)));
        await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Equal((10m, 0m, 10m), State(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Post_GOODS_creates_negative_movement_reduces_physical_and_removes_own_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements
            .Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((-4m, stock.BatchId, stock.LocationId), (movement.Quantity, movement.BatchId, movement.StorageLocationId));
        Assert.Equal((6m, 0m, 6m), State(await api.BalanceAsync(stock)));
        Assert.Equal(DocumentStatus.POSTED, posted.Document.Status);
        Assert.StartsWith("SI-", posted.Document.Number);
        Assert.Equal(api.ActorId, posted.Document.PostedBy);
        Assert.NotNull(posted.Document.PostedAt);
        Assert.Equal(2, posted.Document.Version);
    }

    [Fact]
    public async Task Mixed_sale_posts_only_GOODS_and_keeps_other_drafts_reserved()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var other = await api.CreateSaleAsync(stock, 3m);
        var sale = await api.CreateSaleAsync(api.NewInput(stock.LocationId,
            [new(null, 1, stock.ProductId, stock.BatchId, 7m, 1m), new(null, 2, service.Id, null, 20m, 5m)]));
        await api.PostSaleAsync(sale.Document.Id);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((3m, 3m, 0m), State(await api.BalanceAsync(stock)));
        await api.PostSaleAsync(other.Document.Id);
        Assert.Equal((0m, 0m, 0m), State(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Repeated_and_concurrent_POST_is_idempotent()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostSaleAsync(sale.Document.Id)));
        Assert.All(results, r => Assert.Equal(results[0].Document, r.Document));
        var replay = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(results[0].Document, replay.Document);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((6m, 0m, 6m), State(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task UNPOST_deletes_sale_movements_restores_reserve_and_repost_keeps_number_and_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        var draft = await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Equal(DocumentStatus.DRAFT, draft.Document.Status);
        Assert.Equal(posted.Document.Number, draft.Document.Number);
        Assert.Null(draft.Document.PostedAt);
        Assert.Equal(3, draft.Document.Version);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((10m, 4m, 6m), State(await api.BalanceAsync(stock)));
        Assert.Equal(draft.Document, (await api.UnpostSaleAsync(sale.Document.Id)).Document);
        var reposted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(posted.Document.Number, reposted.Document.Number);
        Assert.Equal(stock.BatchId, reposted.Lines[0].BatchId);
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
        Assert.Equal((6m, 0m, 6m), State(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Logical_delete_keeps_business_data_sets_audit_and_releases_reserve()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/sales/{sale.Document.Id}")).StatusCode);
        var doc = await api.QueryAsync(db => db.Documents.SingleAsync(x => x.Id == sale.Document.Id));
        Assert.NotNull(doc.DeletedAt);
        Assert.Equal(api.ActorId, doc.DeletedBy);
        Assert.Equal(DocumentStatus.DRAFT, doc.Status);
        Assert.Equal(2, doc.Version);
        Assert.Single(await api.QueryAsync(db => db.SaleInvoices.ToListAsync()));
        var line = Assert.Single(await api.QueryAsync(db => db.SaleInvoiceLines.ToListAsync()));
        Assert.Equal(sale.Lines[0].Id, line.Id);
        Assert.Null(line.RemovedAt);
        Assert.Equal((10m, 0m, 10m), State(await api.BalanceAsync(stock)));
        await api.CreateSaleAsync(stock, 10m);
        Assert.Equal((10m, 10m, 0m), State(await api.BalanceAsync(stock)));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null)).StatusCode);
    }

    [Fact]
    public async Task POSTED_sale_cannot_be_deleted_until_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(posted.Document, current.Document);
        Assert.Null(current.Document.DeletedAt);
        await api.UnpostSaleAsync(sale.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/sales/{sale.Document.Id}")).StatusCode);
        Assert.Equal((10m, 0m, 10m), State(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Stale_Version_and_editing_POSTED_sale_are_rejected()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        var edit = SalesTestSupport.Edit(sale);
        var updated = await api.UpdateSaleAsync(sale.Document.Id, edit);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}",
            SalesTestSupport.Edit(posted), ApiTestHost.Json)).StatusCode);
        Assert.Equal(updated.Document.Version + 1, posted.Document.Version);
    }

    [Fact]
    public async Task POST_rechecks_physical_stock_and_rolls_back_on_insufficiency()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock, 4m);
        // Simulate an external inconsistency; public purchase UNPOST cannot bypass existing reservations.
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE stock_movements SET "Quantity" = 3 WHERE "DocumentId" = {stock.Purchase.Document.Id}
            """));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/sales/{sale.Document.Id}/post", null));
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(sale.Document, current.Document);
        Assert.Null(current.Document.Number);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task UNPOST_rejects_reinstating_a_reserve_when_current_stock_is_invalid()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock, 4m);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE stock_movements SET "Quantity" = 3 WHERE "DocumentId" = {stock.Purchase.Document.Id}
            """));
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/sales/{sale.Document.Id}/unpost", null));
        var current = await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"));
        Assert.Equal(posted.Document, current.Document);
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == sale.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task Backdated_sale_uses_current_stock_without_historical_validation()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var input = stock.SaleInput() with { DocumentDate = new DateOnly(2025, 1, 1) };
        var sale = await api.CreateSaleAsync(input);
        var posted = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(input.DocumentDate, posted.Document.DocumentDate);
        Assert.Equal((6m, 0m, 6m), State(await api.BalanceAsync(stock)));
    }

    private static (decimal, decimal, decimal) State(StockBalanceResponse balance) =>
        (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity);
}
