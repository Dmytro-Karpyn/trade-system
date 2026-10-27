using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Returns;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class CustomerReturnLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Return_requires_a_POSTED_source_and_shortcut_prefills_sale_and_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync($"/sales/{sale.Document.Id}/returnable-lines")).StatusCode);
        await api.PostSaleAsync(sale.Document.Id);
        var result = await api.ReadAsync<CustomerReturnResponse>(await api.Client.PostAsJsonAsync($"/sales/{sale.Document.Id}/customer-return",
            new CustomerReturnFromSaleRequest(new DateOnly(2026, 10, 20)), ApiTestHost.Json), HttpStatusCode.Created);
        Assert.Equal(sale.Document.Id, result.SourceSaleInvoiceId);
        Assert.Equal(sale.StorageLocationId, result.StorageLocationId);
        Assert.Empty(result.Lines);
        Assert.Equal(DocumentStatus.DRAFT, result.Document.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/customer-returns/{result.Document.Id}/post", null)).StatusCode);
    }

    [Fact]
    public async Task DRAFT_creates_no_movements_or_reservations_and_GOODS_POST_returns_same_batch_to_selected_own_location()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync();
        var newLocation = (await api.CreatePurchaseAsync()).StorageLocationId;
        var before = await api.BalanceAsync(stock);
        var result = await api.CreateReturnAsync(sale.ReturnInput(2m) with { StorageLocationId = newLocation });
        Assert.Null(result.Document.Number);
        Assert.Equal(21.6m, result.Lines.Single().RefundAmount);
        Assert.Equal(before, await api.BalanceAsync(stock));
        Assert.Empty(await api.SettlementsAsync(result.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        Assert.Equal(4m, Assert.Single(await api.ReturnableAsync(sale.Document.Id)).ReturnableQuantity);
        await api.PostReturnAsync(result.Document.Id);
        var movement = Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        Assert.Equal(stock.BatchId, movement.BatchId);
        Assert.Equal(newLocation, movement.StorageLocationId);
        Assert.Equal(2m, movement.Quantity);
        Assert.Equal(1, await api.QueryAsync(db => db.Batches.CountAsync(x => x.Id == stock.BatchId)));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        var settlement = Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Equal(sale.CounterpartyId, settlement.CounterpartyId);
        Assert.Equal(SettlementCurrencies.Uah, settlement.CurrencyId);
        Assert.Equal(-21.6m, settlement.Amount);
        Assert.Equal(settlement.Amount, settlement.BaseAmountUah);
        var balance = Assert.Single(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync($"/stock/balances?batchId={stock.BatchId}&storageLocationId={newLocation}")));
        Assert.Equal((2m, 0m, 2m), (balance.PhysicalQuantity, balance.ReservedQuantity, balance.AvailableQuantity));
    }

    [Fact]
    public async Task Multiple_partial_GOODS_returns_reduce_all_derived_values_and_UNPOST_restores_limits()
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var first = await api.CreateReturnAsync(sale.ReturnInput(1m));
        await api.PostReturnAsync(first.Document.Id);
        var second = await api.CreateReturnAsync(sale.ReturnInput(2m));
        await api.PostReturnAsync(second.Document.Id);
        var draft = await api.CreateReturnAsync(sale.ReturnInput());
        var deleted = await api.CreateReturnAsync(sale.ReturnInput());
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/customer-returns/{deleted.Document.Id}")).StatusCode);
        var line = sale.Lines.Single();
        Assert.Equal(new ReturnableLineResponse(line.Id, line.ProductId, ItemType.GOODS, line.BatchId,
            4m, 3m, 1m, 12m, 10m, 43.2m, 32.4m, 10.8m), Assert.Single(await api.ReturnableAsync(sale.Document.Id)));
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(2m), ApiTestHost.Json),
            "RETURN_QUANTITY_EXCEEDED", HttpStatusCode.Conflict);
        await api.UnpostReturnAsync(second.Document.Id);
        Assert.Equal(3m, Assert.Single(await api.ReturnableAsync(sale.Document.Id)).ReturnableQuantity);
        Assert.Empty(await api.SettlementsAsync(second.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == second.Document.Id).ToListAsync()));
        await api.PostReturnAsync(draft.Document.Id);
        Assert.Equal(2m, Assert.Single(await api.ReturnableAsync(sale.Document.Id)).ReturnableQuantity);
    }

    [Fact]
    public async Task SERVICE_supports_partial_financial_compensation_and_defaults_to_remaining_amount_without_stock_or_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var sale = await api.PostSaleAsync((await api.ServiceSaleForAsync(api.CounterpartyId, 100m, discount: 20m)).Document.Id);
        var first = await api.CreateReturnAsync(sale.ReturnInput(1m, 25m));
        await api.PostReturnAsync(first.Document.Id);
        var second = await api.CreateReturnAsync(sale.ReturnInput(0m));
        Assert.Equal(55m, second.Lines.Single().RefundAmount);
        await api.PostReturnAsync(second.Document.Id);
        Assert.Equal(-55m, Assert.Single(await api.SettlementsAsync(second.Document.Id)).Amount);
        var line = Assert.Single(await api.ReturnableAsync(sale.Document.Id));
        Assert.Equal((80m, 80m, 0m), (line.OriginalNetAmount, line.AlreadyRefundedAmount, line.RefundableAmount));
        Assert.Null(line.BatchId);
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsJsonAsync("/customer-returns", sale.ReturnInput(0m, 0.0001m), ApiTestHost.Json),
            "RETURN_AMOUNT_EXCEEDED", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mixed_return_creates_stock_for_GOODS_only_and_one_settlement_for_total_refund()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var service = await api.CreateProductAsync(ItemType.SERVICE);
        var sale = await api.CreateSaleAsync(stock.SaleInput() with { DiscountPercent = 10m,
            Lines = [new(null, 1, stock.ProductId, stock.BatchId, 4m, 12m), new(null, 2, service.Id, null, 1m, 100m)] });
        sale = await api.PostSaleAsync(sale.Document.Id);
        var input = new CustomerReturnRequest(new DateOnly(2026, 10, 20), sale.Document.Id, sale.StorageLocationId,
            [new(null, sale.Lines.Single(x => x.LineNo == 1).Id, 2m), new(null, sale.Lines.Single(x => x.LineNo == 2).Id, 0m, 30m)]);
        var result = await api.CreateReturnAsync(input);
        await api.PostReturnAsync(result.Document.Id);
        Assert.Equal(-51.6m, Assert.Single(await api.SettlementsAsync(result.Document.Id)).Amount);
        Assert.Equal(2m, Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync())).Quantity);
        Assert.Equal(2, (await api.ReturnableAsync(sale.Document.Id)).Length);
        Assert.Equal(8m, (await api.BalanceAsync(stock)).PhysicalQuantity);
    }

    [Fact]
    public async Task Parallel_POST_replays_are_idempotent_and_UNPOST_repost_keeps_number_and_batch()
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync();
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostReturnAsync(result.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        var draft = await api.UnpostReturnAsync(result.Document.Id);
        Assert.Equal(draft.Document, (await api.UnpostReturnAsync(result.Document.Id)).Document);
        Assert.Equal(posts[0].Document.Number, (await api.PostReturnAsync(result.Document.Id)).Document.Number);
        Assert.Equal(stock.BatchId, Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync())).BatchId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UNPOST_rejects_consumed_or_reserved_returned_stock_and_preserves_both_ledgers(bool postedDemand)
    {
        await using var api = await fixture.CreateApiAsync();
        var (stock, sale) = await api.SoldGoodsAsync(quantity: 10m);
        var result = await api.CreateReturnAsync(sale.ReturnInput(2m));
        result = await api.PostReturnAsync(result.Document.Id);
        var demand = await api.CreateSaleAsync(stock, 2m);
        if (postedDemand) demand = await api.PostSaleAsync(demand.Document.Id);
        await SalesTestSupport.InsufficientAsync(await api.Client.PostAsync($"/customer-returns/{result.Document.Id}/unpost", null));
        Assert.Equal(result.Document, (await api.ReadAsync<CustomerReturnResponse>(await api.Client.GetAsync($"/customer-returns/{result.Document.Id}"))).Document);
        Assert.Single(await api.SettlementsAsync(result.Document.Id));
        Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == result.Document.Id).ToListAsync()));
        if (postedDemand) await api.UnpostSaleAsync(demand.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/sales/{demand.Document.Id}")).StatusCode);
        await api.UnpostReturnAsync(result.Document.Id);
        Assert.Equal(0m, (await api.BalanceAsync(stock)).PhysicalQuantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_sale_UNPOST_is_blocked_by_DRAFT_or_POSTED_return_until_logical_delete(bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        if (posted)
        {
            result = await api.PostReturnAsync(result.Document.Id);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/customer-returns/{result.Document.Id}")).StatusCode);
        }
        await BlockedAsync();
        if (posted) { result = await api.UnpostReturnAsync(result.Document.Id); await BlockedAsync(); }
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/customer-returns/{result.Document.Id}")).StatusCode);
        var stored = await api.QueryAsync(db => db.CustomerReturns.Include(x => x.Document).Include(x => x.Lines).SingleAsync());
        Assert.NotNull(stored.Document.DeletedAt);
        Assert.Equal(api.ActorId, stored.Document.DeletedBy);
        Assert.Single(stored.Lines);
        Assert.Empty(await api.ReadAsync<CustomerReturnResponse[]>(await api.Client.GetAsync("/customer-returns")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/customer-returns/{result.Document.Id}")).StatusCode);
        Assert.Equal(DocumentStatus.DRAFT, (await api.UnpostSaleAsync(sale.Document.Id)).Document.Status);

        async Task BlockedAsync() => await ExpenseTestSupport.ErrorCodeAsync(
            await api.Client.PostAsync($"/sales/{sale.Document.Id}/unpost", null), "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DRAFT_edit_replaces_lines_requires_Version_and_cannot_edit_POSTED_return()
    {
        await using var api = await fixture.CreateApiAsync();
        var (_, sale) = await api.SoldGoodsAsync();
        var result = await api.CreateReturnAsync(sale.ReturnInput());
        var edit = result.EditReturn();
        result = await api.ReadAsync<CustomerReturnResponse>(await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}",
            edit with { Lines = [edit.Lines[0] with { Quantity = 2m }], Comment = "Updated" }, ApiTestHost.Json));
        Assert.Equal(21.6m, result.Lines.Single().RefundAmount);
        Assert.Equal("Updated", result.Document.Comment);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        var cleared = await api.ReadAsync<CustomerReturnResponse>(await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}",
            result.EditReturn() with { Lines = [] }, ApiTestHost.Json));
        Assert.Empty(cleared.Lines);
        Assert.Empty(await api.QueryAsync(db => db.CustomerReturnLines.ToListAsync()));
        result = await api.ReadAsync<CustomerReturnResponse>(await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}",
            cleared.EditReturn() with { Lines = [new(null, sale.Lines.Single().Id, 1m)] }, ApiTestHost.Json));
        result = await api.PostReturnAsync(result.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/customer-returns/{result.Document.Id}", result.EditReturn(), ApiTestHost.Json)).StatusCode);
    }
}
