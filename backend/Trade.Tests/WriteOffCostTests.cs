using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Purchasing;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class WriteOffCostTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Cost_view_sums_multiple_lines_at_current_unit_cost_and_exposes_all_report_fields()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.ReceiveAsync();
        var b = await api.CreatePurchaseAsync(quantity: 10m, storageLocationId: a.LocationId);
        var edit = ApiTestHost.Edit(b);
        b = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{b.Document.Id}", edit with
            { Lines = [edit.Lines[0] with { UnitPriceCurrency = 25m }] }, ApiTestHost.Json));
        await api.PostAsync(b.Document.Id);
        var secondBatch = await api.QueryAsync(db => db.Batches.Where(x => x.PurchaseInvoiceLineId == b.Lines[0].Id).Select(x => x.Id).SingleAsync());
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(a.WriteOffInput(reason.Id) with
            { Lines = [new(null, 1, a.BatchId, 2m), new(null, 2, secondBatch, 3m)] });
        await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(new WriteOffCostData(writeOff.Document.Id, writeOff.Document.Id, writeOff.Document.DocumentDate,
            a.LocationId, reason.Id, reason.ExpenseCategoryId, 95m), Assert.Single(await api.WriteOffCostsAsync()));
        var batchCosts = (await api.BatchCostsAsync()).ToDictionary(x => x.BatchId, x => x.UnitCostUah);
        Assert.Equal(writeOff.Lines.Sum(x => x.Quantity * batchCosts[x.BatchId]), Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
    }

    [Fact]
    public async Task Posted_historical_cost_changes_with_direct_Expenses_without_snapshots_or_repricing()
    {
        await using var api = await fixture.CreateApiAsync();
        var type = await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        var stock = await api.ReceiveAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput((await api.CreateReasonAsync()).Id));
        writeOff = await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(20m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        var prices = await api.QueryAsync(db => db.BatchPrices.Where(x => x.PriceTypeId == type.Id).Select(x => x.UnitPrice).ToArrayAsync());
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 50m, 2m));
        Assert.Equal(20m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(40m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        expense = await api.UnpostExpenseAsync(expense.Document.Id);
        Assert.Equal(20m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        await api.UpdateExpenseAsync(expense, expense.EditExpense() with { Amount = 100m });
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(60m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        Assert.Equal(writeOff.Document, (await api.ReadAsync<WriteOffResponse>(await api.Client.GetAsync($"/write-offs/{writeOff.Document.Id}"))).Document);
        Assert.Equal(prices, await api.QueryAsync(db => db.BatchPrices.Where(x => x.PriceTypeId == type.Id).Select(x => x.UnitPrice).ToArrayAsync()));
        Assert.Equal((8m, 0m, 8m), await api.LocationStockAsync(stock.BatchId, stock.LocationId));
    }

    [Fact]
    public async Task Report_uses_current_reason_category_and_preserves_full_cost_precision()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, Guid.NewGuid(), quantity: 10m, price: 0.0001m, rate: 1.5m);
        await api.PostAsync(purchase.Document.Id);
        var batch = await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync());
        var stock = new ReceiptStock(purchase, batch);
        var reason = await api.CreateReasonAsync();
        var writeOff = await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id, 0.0001m));
        await api.PostWriteOffAsync(writeOff.Document.Id);
        Assert.Equal(0.000000015m, Assert.Single(await api.WriteOffCostsAsync()).TotalCostUah);
        var category = await api.CreateCategoryAsync("Internal use");
        await api.ReadAsync<WriteOffReasonResponse>(await api.Client.PutAsJsonAsync($"/write-off-reasons/{reason.Id}",
            new WriteOffReasonRequest("Reclassified", category.Id), ApiTestHost.Json));
        Assert.Equal(category.Id, Assert.Single(await api.WriteOffCostsAsync()).ExpenseCategoryId);
        Assert.Equal(writeOff.Document.Id, Assert.Single(await api.ReadAsync<WriteOffResponse[]>(await api.Client.GetAsync($"/write-offs?expenseCategoryId={category.Id}"))).Document.Id);
    }
}
