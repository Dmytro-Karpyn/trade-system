using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Inventory;
using Trade.Domain.Returns;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class SupplierReturnLifecycleTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(DocumentType.PURCHASE_INVOICE, false)]
    [InlineData(DocumentType.OPENING_STOCK, false)]
    [InlineData(DocumentType.OPENING_STOCK, true)]
    public async Task Source_must_be_POSTED_purchase_and_never_OPENING_STOCK(DocumentType type, bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(type: type);
        if (posted) await api.PostAsync(purchase.Document.Id);
        var input = new SupplierReturnRequest(new DateOnly(2026, 10, 20), purchase.Document.Id, purchase.StorageLocationId,
            [new(null, purchase.Lines.Single().Id, 1m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/supplier-returns", input, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/purchases/{purchase.Document.Id}/returnable-lines")).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.SupplierReturns.ToListAsync()));
    }

    [Fact]
    public async Task DRAFT_reserves_stock_without_any_movements_and_POST_replaces_reserve_with_issue_and_positive_settlement()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        Assert.Equal(DocumentStatus.DRAFT, result.Document.Status);
        Assert.Null(result.Document.Number);
        Assert.Empty(await api.SettlementsAsync(result.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        Assert.Equal((10m, 2m, 8m), Quantities(await api.BalanceAsync(stock)));
        result = await api.PostSupplierReturnAsync(result.Document.Id);
        Assert.Equal(DocumentStatus.POSTED, result.Document.Status);
        Assert.StartsWith("SR-", result.Document.Number);
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        Assert.Equal((stock.BatchId, stock.LocationId, -2m), (movement.BatchId, movement.StorageLocationId, movement.Quantity));
        var settlement = Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Equal(stock.Purchase.CounterpartyId, settlement.CounterpartyId);
        Assert.Equal(stock.Purchase.CurrencyId, settlement.CurrencyId);
        Assert.Equal((20m, 20m), (settlement.Amount, settlement.BaseAmountUah));
        Assert.Equal((8m, 0m, 8m), Quantities(await api.BalanceAsync(stock)));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        Assert.Single(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Fact]
    public async Task Foreign_refund_uses_original_discount_currency_and_rate_and_excludes_direct_Expenses_without_changing_cost()
    {
        await using var api = await fixture.CreateApiAsync();
        var currency = Guid.NewGuid();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, currency, quantity: 10m, price: 100m, discount: 10m, rate: 40m);
        purchase = await api.PostAsync(purchase.Document.Id);
        var batch = await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync());
        var stock = new ReceiptStock(purchase, batch);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id, 100m));
        await api.PostExpenseAsync(expense.Document.Id);
        var costs = await api.BatchCostsAsync();
        Assert.Equal(3610m, Assert.Single(costs).UnitCostUah);
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        await api.PostSupplierReturnAsync(result.Document.Id);
        var settlement = Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Equal(currency, settlement.CurrencyId);
        Assert.Equal((180m, 7200m), (settlement.Amount, settlement.BaseAmountUah));
        Assert.Equal(costs, await api.BatchCostsAsync());
        await api.UnpostSupplierReturnAsync(result.Document.Id);
        Assert.Equal(costs, await api.BatchCostsAsync());
    }

    [Fact]
    public async Task Editing_excludes_own_DRAFT_reserve_and_checks_current_version_and_POSTED_status()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput(8m));
        var edit = result.EditSupplierReturn();
        result = await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}",
            edit with { Lines = [edit.Lines[0] with { Quantity = 10m }], Comment = "Changed" }, ApiTestHost.Json));
        Assert.Equal((10m, 10m, 0m), Quantities(await api.BalanceAsync(stock)));
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        result = await api.PostSupplierReturnAsync(result.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}", result.EditSupplierReturn(), ApiTestHost.Json)).StatusCode);
        Assert.Equal((0m, 0m, 0m), Quantities(await api.BalanceAsync(stock)));
    }

    [Fact]
    public async Task Multiple_POSTED_returns_reduce_purchase_limit_and_query_combines_it_with_available_stock()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var a = await api.CreateSupplierReturnAsync(stock.SupplierInput(3m));
        await api.PostSupplierReturnAsync(a.Document.Id);
        var b = await api.CreateSupplierReturnAsync(stock.SupplierInput(2m));
        await api.PostSupplierReturnAsync(b.Document.Id);
        await api.CreateSupplierReturnAsync(stock.SupplierInput(1m));
        var line = stock.Purchase.Lines.Single();
        Assert.Equal(new SupplierReturnableLineResponse(line.Id, stock.ProductId, stock.BatchId, 10m, 5m, 5m,
            5m, 1m, 4m, 4m, 10m, 0m, stock.Purchase.CurrencyId, 1m), Assert.Single(await api.SupplierReturnableAsync(stock.Purchase.Document.Id)));
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsJsonAsync("/supplier-returns", stock.SupplierInput(6m), ApiTestHost.Json),
            "RETURN_QUANTITY_EXCEEDED", HttpStatusCode.Conflict);
        var otherLocation = (await api.CreatePurchaseAsync()).StorageLocationId;
        var emptyLocation = Assert.Single(await api.SupplierReturnableAsync(stock.Purchase.Document.Id, otherLocation));
        Assert.Equal(0m, emptyLocation.MaxReturnableQuantity);
        Assert.Equal(5m, emptyLocation.RemainingReturnableByPurchase);
        await api.UnpostSupplierReturnAsync(b.Document.Id);
        var after = Assert.Single(await api.SupplierReturnableAsync(stock.Purchase.Document.Id));
        Assert.Equal((3m, 7m, 7m, 3m, 4m), (after.AlreadyReturnedQuantity, after.RemainingReturnableByPurchase,
            after.PhysicalQuantity, after.ReservedQuantity, after.MaxReturnableQuantity));
    }

    [Fact]
    public async Task Concurrent_POST_is_idempotent_and_UNPOST_restores_reserve_and_preserves_number_for_repost()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostSupplierReturnAsync(result.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        var draft = await api.UnpostSupplierReturnAsync(result.Document.Id);
        Assert.Equal((10m, 2m, 8m), Quantities(await api.BalanceAsync(stock)));
        Assert.Empty(await api.SettlementsAsync(result.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        Assert.Equal(draft.Document, (await api.UnpostSupplierReturnAsync(result.Document.Id)).Document);
        Assert.Equal(posts[0].Document.Number, (await api.PostSupplierReturnAsync(result.Document.Id)).Document.Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_DRAFT_or_POSTED_return_blocks_purchase_UNPOST_and_DRAFT_logical_delete_removes_reserve(bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.CreateSupplierReturnAsync(stock.SupplierInput());
        if (posted)
        {
            result = await api.PostSupplierReturnAsync(result.Document.Id);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/supplier-returns/{result.Document.Id}")).StatusCode);
        }
        await BlockedAsync();
        if (posted) { result = await api.UnpostSupplierReturnAsync(result.Document.Id); await BlockedAsync(); }
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/supplier-returns/{result.Document.Id}")).StatusCode);
        Assert.Equal((10m, 0m, 10m), Quantities(await api.BalanceAsync(stock)));
        var stored = await api.QueryAsync(db => db.SupplierReturns.Include(x => x.Document).Include(x => x.Lines).SingleAsync());
        Assert.NotNull(stored.Document.DeletedAt);
        Assert.Equal(api.ActorId, stored.Document.DeletedBy);
        Assert.Single(stored.Lines);
        Assert.Empty(await api.ReadAsync<SupplierReturnResponse[]>(await api.Client.GetAsync("/supplier-returns")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/supplier-returns/{result.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/supplier-returns/{result.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/supplier-returns/{result.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(DocumentStatus.DRAFT, (await api.UnpostAsync(stock.Purchase.Document.Id)).Document.Status);
        async Task BlockedAsync() => await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsync($"/purchases/{stock.Purchase.Document.Id}/unpost", null),
            "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Shortcut_prefills_source_and_location_and_empty_DRAFT_can_be_filled_or_cleared()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var result = await api.ReadAsync<SupplierReturnResponse>(await api.Client.PostAsJsonAsync($"/purchases/{stock.Purchase.Document.Id}/supplier-return",
            new SupplierReturnFromPurchaseRequest(new DateOnly(2026, 10, 20)), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(stock.Purchase.Document.Id, result.SourcePurchaseInvoiceId);
        Assert.Equal(stock.LocationId, result.StorageLocationId);
        Assert.Empty(result.Lines);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/supplier-returns/{result.Document.Id}/post", null)).StatusCode);
        result = await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}",
            result.EditSupplierReturn() with { Lines = stock.SupplierInput().Lines }, ApiTestHost.Json));
        Assert.Equal(2m, (await api.BalanceAsync(stock)).ReservedQuantity);
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PutAsJsonAsync($"/supplier-returns/{result.Document.Id}",
            result.EditSupplierReturn() with { Lines = [] }, ApiTestHost.Json));
        Assert.Empty(await api.QueryAsync(db => db.SupplierReturnLines.ToListAsync()));
        Assert.Equal(0m, (await api.BalanceAsync(stock)).ReservedQuantity);
    }

    private static (decimal Physical, decimal Reserved, decimal Available) Quantities(StockBalanceResponse x) =>
        (x.PhysicalQuantity, x.ReservedQuantity, x.AvailableQuantity);
}
