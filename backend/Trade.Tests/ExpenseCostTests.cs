using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ExpenseCostTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task General_POSTED_and_direct_DRAFT_expenses_do_not_change_batch_cost()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        await api.PostAsync(purchase.Document.Id);
        var before = await api.BatchCostsAsync();
        var general = await api.CreateExpenseAsync();
        await api.PostExpenseAsync(general.Document.Id);
        await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id));
        Assert.Equal(before, await api.BatchCostsAsync());
        Assert.Equal(10m, Assert.Single(before).UnitCostUah);
    }

    [Fact]
    public async Task Direct_expenses_sum_in_their_own_currencies_and_allocate_proportionally_without_changing_prices()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), quantity: 2m, price: 100m, discount: 20m, rate: 2m);
        var edit = ApiTestHost.Edit(purchase);
        var secondProduct = await api.CreateProductAsync();
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0], new PurchaseLineDetails(null, 2, secondProduct.Id, 4m, 25m)] }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        var prices = await api.QueryAsync(db => db.BatchPrices.OrderBy(x => x.BatchId).Select(x => x.UnitPrice).ToArrayAsync());
        await AssertCostsAsync(160m, 40m);
        var first = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id, 60m, 2m, Guid.NewGuid()));
        await api.PostExpenseAsync(first.Document.Id);
        // Line values 320 and 160 UAH: allocations 80 and 40, divided by quantities 2 and 4.
        await AssertCostsAsync(200m, 50m);
        var second = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id, 60m));
        await api.PostExpenseAsync(second.Document.Id);
        await AssertCostsAsync(220m, 55m);
        await api.UnpostExpenseAsync(first.Document.Id);
        await AssertCostsAsync(180m, 45m);
        await api.UnpostExpenseAsync(second.Document.Id);
        await AssertCostsAsync(160m, 40m);
        Assert.Equal(prices, await api.QueryAsync(db => db.BatchPrices.OrderBy(x => x.BatchId).Select(x => x.UnitPrice).ToArrayAsync()));

        async Task AssertCostsAsync(decimal firstCost, decimal secondCost)
        {
            var costs = (await api.BatchCostsAsync()).ToDictionary(x => x.PurchaseInvoiceLineId);
            Assert.Equal(firstCost, costs[purchase.Lines.Single(x => x.LineNo == 1).Id].UnitCostUah);
            Assert.Equal(secondCost, costs[purchase.Lines.Single(x => x.LineNo == 2).Id].UnitCostUah);
        }
    }

    [Fact]
    public async Task Direct_expense_after_sale_changes_current_cost_and_edits_recalculate_it_without_repricing()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types",
            new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        var stock = await api.ReceiveAsync(quantity: 10m);
        var sale = await api.CreateSaleAsync(stock.SaleInput(2m) with { PriceTypeId = type.Id,
            Lines = [new(null, 1, stock.ProductId, stock.BatchId, 2m)] });
        sale = await api.PostSaleAsync(sale.Document.Id);
        Assert.Equal(12.5m, sale.Lines[0].RecommendedUnitPrice);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 20m));
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(12m, Assert.Single(await api.BatchCostsAsync()).UnitCostUah);
        expense = await api.UnpostExpenseAsync(expense.Document.Id);
        Assert.Equal(10m, Assert.Single(await api.BatchCostsAsync()).UnitCostUah);
        await api.UpdateExpenseAsync(expense, expense.EditExpense() with { Amount = 30m, ExchangeRate = 2m });
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(16m, Assert.Single(await api.BatchCostsAsync()).UnitCostUah);
        Assert.Equal(12.5m, await api.QueryAsync(db => db.BatchPrices.Select(x => x.UnitPrice).SingleAsync()));
        Assert.Equal(sale.Lines, (await api.ReadAsync<SaleResponse>(await api.Client.GetAsync($"/sales/{sale.Document.Id}"))).Lines);
        Assert.Equal(8m, (await api.BalanceAsync(stock)).PhysicalQuantity);
    }

    [Fact]
    public async Task Allocation_ignores_removed_purchase_lines_and_keeps_their_existing_batch_identity()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(quantity: 2m);
        var edit = ApiTestHost.Edit(purchase);
        purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines[0], new(null, 2, edit.Lines[0].ProductId, 5m, 100m)] }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        purchase = await api.UnpostAsync(purchase.Document.Id);
        edit = ApiTestHost.Edit(purchase);
        await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
            { Lines = [edit.Lines.Single(x => x.LineNo == 1)] }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id, 20m));
        await api.PostExpenseAsync(expense.Document.Id);
        var costs = (await api.BatchCostsAsync()).ToDictionary(x => x.PurchaseInvoiceLineId);
        Assert.Equal(20m, costs[purchase.Lines.Single(x => x.LineNo == 1).Id].UnitCostUah);
        Assert.Equal(100m, costs[purchase.Lines.Single(x => x.LineNo == 2).Id].UnitCostUah);
        Assert.Equal(2, costs.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_total_purchase_rejects_direct_expense_with_a_domain_error(bool fullDiscount)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah,
            price: fullDiscount ? 10m : 0m, discount: fullDiscount ? 100m : 0m);
        await api.PostAsync(purchase.Document.Id);
        var input = await api.ExpenseInputAsync(purchase.Document.Id);
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsJsonAsync("/expenses", input, ApiTestHost.Json),
            "INVALID_PURCHASE_EXPENSE_ALLOCATION", HttpStatusCode.BadRequest);
        var general = await api.CreateExpenseAsync(input with { PurchaseInvoiceId = null });
        await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PutAsJsonAsync($"/expenses/{general.Document.Id}",
            general.EditExpense() with { PurchaseInvoiceId = purchase.Document.Id }, ApiTestHost.Json),
            "INVALID_PURCHASE_EXPENSE_ALLOCATION", HttpStatusCode.BadRequest);
        Assert.Null((await api.ReadAsync<ExpenseResponse>(await api.Client.GetAsync($"/expenses/{general.Document.Id}"))).PurchaseInvoiceId);
    }

    [Theory]
    [InlineData(DocumentType.OPENING_STOCK, false)]
    [InlineData(DocumentType.OPENING_STOCK, true)]
    [InlineData(DocumentType.PURCHASE_INVOICE, false)]
    public async Task Direct_expense_rejects_opening_stock_and_unposted_purchases(DocumentType type, bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(type: type);
        if (posted) await api.PostAsync(purchase.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expenses",
            await api.ExpenseInputAsync(purchase.Document.Id), ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Expenses.ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dependent_DRAFT_or_POSTED_expense_blocks_purchase_UNPOST_until_detached_or_deleted(bool posted)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        var postedPurchase = await api.PostAsync(purchase.Document.Id);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id));
        if (posted) expense = await api.PostExpenseAsync(expense.Document.Id);
        await CheckBlockedAsync();
        if (posted)
        {
            expense = await api.UnpostExpenseAsync(expense.Document.Id);
            await CheckBlockedAsync();
            await api.UpdateExpenseAsync(expense, expense.EditExpense() with { PurchaseInvoiceId = null });
        }
        else Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/expenses/{expense.Document.Id}")).StatusCode);
        Assert.Equal(DocumentStatus.DRAFT, (await api.UnpostAsync(purchase.Document.Id)).Document.Status);

        async Task CheckBlockedAsync()
        {
            await ExpenseTestSupport.ErrorCodeAsync(await api.Client.PostAsync($"/purchases/{purchase.Document.Id}/unpost", null),
                "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
            Assert.Equal(postedPurchase.Document, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"))).Document);
            Assert.Single(await api.QueryAsync(db => db.StockMovements.Where(x => x.DocumentId == purchase.Document.Id).ToListAsync()));
            Assert.Single(await api.SettlementsAsync(purchase.Document.Id));
        }
    }

    [Fact]
    public async Task Concurrent_direct_expense_creation_and_purchase_UNPOST_cannot_leave_an_invalid_reference()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        await api.PostAsync(purchase.Document.Id);
        var input = await api.ExpenseInputAsync(purchase.Document.Id);
        var responses = await Task.WhenAll(api.Client.PostAsJsonAsync("/expenses", input, ApiTestHost.Json),
            api.Client.PostAsync($"/purchases/{purchase.Document.Id}/unpost", null));
        if (responses[0].StatusCode == HttpStatusCode.Created)
        {
            await ExpenseTestSupport.ErrorCodeAsync(responses[1], "DOCUMENT_HAS_DEPENDENCIES", HttpStatusCode.Conflict);
            Assert.Equal(DocumentStatus.POSTED, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"))).Document.Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
            Assert.Empty(await api.QueryAsync(db => db.Expenses.ToListAsync()));
        }
    }

    [Fact]
    public async Task Tiny_positive_purchase_is_allocatable_without_rounding_its_total_to_zero()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah,
            quantity: 0.0001m, price: 0.0001m, rate: 0.00000001m);
        await api.PostAsync(purchase.Document.Id);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id, 0.0001m));
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(1.000000000001m, Assert.Single(await api.BatchCostsAsync()).UnitCostUah);
    }
}
