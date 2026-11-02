using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Domain.Documents;
using Trade.Domain.Money;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ReportQueryTests(DatabaseFixture fixture)
{
    private const string Period = "from=2026-10-01&to=2026-10-31";
    private static decimal Value(ReportTable table,string key) => Assert.Single(table.Metrics,x => x.Key==key).Value;
    private static decimal Cell(Dictionary<string,object?> row,string key) => ((JsonElement)row[key]!).GetDecimal();
    private static decimal Kpi(DashboardSummary summary,string key) => Assert.Single(summary.Kpis,x => x.Key==key).Value;
    private static async Task<ReportTable> Read(ApiTestHost api,string kind,string extra="") => await api.ReadAsync<ReportTable>(await api.Client.GetAsync($"/reports/{kind}?{Period}{extra}"));
    private static async Task<DashboardSummary> Dashboard(ApiTestHost api) => await api.ReadAsync<DashboardSummary>(await api.Client.GetAsync($"/dashboard/summary?{Period}"));

    [Theory]
    [InlineData("sales")][InlineData("profitability")][InlineData("stock")][InlineData("purchases")][InlineData("money")][InlineData("expenses")][InlineData("settlements")]
    public async Task Empty_reports_are_real_empty_queries_with_no_schema_changes(string kind)
    {
        await using var api=await fixture.CreateApiAsync();var table=await Read(api,kind);Assert.Empty(table.Rows);
        Assert.All(table.Metrics,m=>Assert.Equal(0,m.Value));
        Assert.Empty((await Dashboard(api)).SalesTrend);
        await api.WithDbAsync(db=>{Assert.False(db.Database.HasPendingModelChanges());return Task.CompletedTask;});
    }
    [Fact]
    public async Task Sales_discount_returns_and_filters_are_correct_and_drafts_are_excluded()
    {
        await using var api=await fixture.CreateApiAsync();var (stock,sale)=await api.SoldGoodsAsync();
        await api.PostReturnAsync((await api.CreateReturnAsync(sale.ReturnInput(1m))).Document.Id);
        await api.CreateSaleAsync(stock);
        var report=await Read(api,"sales");Assert.Equal(2,report.Rows.Count);Assert.Equal(48m,Value(report,"grossSales"));Assert.Equal(4.8m,Value(report,"discounts"));
        Assert.Equal(43.2m,Value(report,"sales"));Assert.Equal(10.8m,Value(report,"returns"));Assert.Equal(32.4m,Value(report,"netSales"));Assert.Equal(3m,Value(report,"quantity"));
        Assert.Equal(2,(await Read(api,"sales",$"&productId={stock.ProductId}&batchId={stock.BatchId}&counterpartyId={sale.CounterpartyId}&createdBy={sale.Document.CreatedBy}")).Rows.Count);
        Assert.Empty((await Read(api,"sales",$"&productId={Guid.NewGuid()}")).Rows);Assert.Empty((await Read(api,"sales",$"&categoryId={Guid.NewGuid()}&priceTypeId={Guid.NewGuid()}")).Rows);
        var dash=await Dashboard(api);Assert.Equal(32.4m,Kpi(dash,"netSales"));Assert.Equal(30m,Kpi(dash,"cogs"));Assert.Equal(2.4m,Kpi(dash,"grossProfit"));
        Assert.Equal(32.4m,Assert.Single(dash.SalesTrend).NetSales);
    }
    [Fact]
    public async Task Sales_summaries_round_each_document_once_and_match_posted_settlement_amounts()
    {
        await using var api=await fixture.CreateApiAsync();
        for(var i=0;i<2;i++) {
            var sale=await api.ServiceSaleForAsync(api.CounterpartyId,0.0001m);
            sale=await api.ReadAsync<SaleResponse>(await api.Client.PutAsJsonAsync($"/sales/{sale.Document.Id}",new UpdateSaleRequest(sale.Document.Version,
                sale.Document.DocumentDate,null,null,sale.CounterpartyId,sale.StorageLocationId,sale.PriceTypeId,0m,[new(sale.Lines[0].Id,1,sale.Lines[0].ProductId,null,0.5m,0.0001m)]),ApiTestHost.Json));
            await api.PostSaleAsync(sale.Document.Id);
        }
        var expected=await api.QueryAsync(db=>db.SettlementMovements.SumAsync(x=>x.Amount));Assert.Equal(0.0002m,expected);
        Assert.Equal(expected,Value(await Read(api,"sales"),"netSales"));Assert.Equal(expected,Kpi(await Dashboard(api),"sales"));
    }
    [Fact]
    public async Task Direct_expense_changes_historical_profit_and_writeoff_cost_but_is_not_deducted_twice_or_repriced()
    {
        await using var api=await fixture.CreateApiAsync();
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types",new PriceTypeRequest("Retail",25m),ApiTestHost.Json),HttpStatusCode.Created);
        var (stock,sale)=await api.SoldGoodsAsync();await api.PostReturnAsync((await api.CreateReturnAsync(sale.ReturnInput())).Document.Id);
        var general=await api.CreateExpenseAsync(await api.ExpenseInputAsync(amount:5m));await api.PostExpenseAsync(general.Document.Id);
        var reason=await api.CreateReasonAsync();await api.PostWriteOffAsync((await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id,1m))).Document.Id);
        var prices=await api.QueryAsync(db=>db.BatchPrices.Select(x=>x.UnitPrice).ToArrayAsync());
        var before=await Dashboard(api);Assert.Equal(-12.6m,Kpi(before,"managementResult"));
        var direct=await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id,20m));await api.PostExpenseAsync(direct.Document.Id);
        var after=await Dashboard(api);Assert.Equal(36m,Kpi(after,"cogs"));Assert.Equal(-3.6m,Kpi(after,"grossProfit"));Assert.Equal(5m,Kpi(after,"expenses"));Assert.Equal(12m,Kpi(after,"writeoffs"));Assert.Equal(-20.6m,Kpi(after,"managementResult"));
        Assert.Equal(prices,await api.QueryAsync(db=>db.BatchPrices.Select(x=>x.UnitPrice).ToArrayAsync()));
        var profit=await Read(api,"profitability");Assert.Equal(-3.6m,Value(profit,"grossProfit"));Assert.True(((JsonElement)Assert.Single(profit.Rows)["belowCost"]!).GetBoolean());
        var expenses=await Read(api,"expenses");Assert.Equal(5m,Value(expenses,"generalExpenses"));Assert.Equal(20m,Value(expenses,"directExpenses"));
        Assert.Single((await Read(api,"expenses","&isPurchaseExpense=true")).Rows);
        await api.UnpostExpenseAsync(direct.Document.Id);Assert.Equal(2.4m,Kpi(await Dashboard(api),"grossProfit"));
    }
    [Fact]
    public async Task Purchase_direct_expenses_are_counted_once_with_multiple_lines_and_currency_filter()
    {
        await using var api=await fixture.CreateApiAsync();var purchase=await api.CreatePurchaseAsync();var edit=ApiTestHost.Edit(purchase);
        purchase=await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}",edit with { DiscountPercent=10m,Lines=[edit.Lines[0],edit.Lines[0] with {Id=null,LineNo=2}] },ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);var expense=await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id,20m));await api.PostExpenseAsync(expense.Document.Id);
        var report=await Read(api,"purchases",$"&counterpartyId={purchase.CounterpartyId}&productId={purchase.Lines[0].ProductId}&currencyId={purchase.CurrencyId}");
        Assert.Equal(3,report.Rows.Count);Assert.Equal(20m,Value(report,"directExpenses"));Assert.Equal(94.5m,Value(report,"purchaseUah"));
        Assert.Single(report.Rows,x=>x.ContainsKey("isExpenseTotal"));
        Assert.Empty((await Read(api,"purchases",$"&currencyId={Guid.NewGuid()}")).Rows);
    }
    [Fact]
    public async Task Expense_categories_with_same_name_in_different_tree_branches_are_not_merged()
    {
        await using var api=await fixture.CreateApiAsync();var a=await api.CreateCategoryAsync("Office");var b=await api.CreateCategoryAsync("Warehouse");
        var first=await api.ReadAsync<ExpenseCategoryResponse>(await api.Client.PostAsJsonAsync("/expense-categories",new ExpenseCategoryRequest("Delivery",a.Id),ApiTestHost.Json),HttpStatusCode.Created);
        var second=await api.ReadAsync<ExpenseCategoryResponse>(await api.Client.PostAsJsonAsync("/expense-categories",new ExpenseCategoryRequest("Delivery",b.Id),ApiTestHost.Json),HttpStatusCode.Created);
        foreach(var category in new[]{first,second}) await api.PostExpenseAsync((await api.CreateExpenseAsync((await api.ExpenseInputAsync(amount:5m)) with {ExpenseCategoryId=category.Id})).Document.Id);
        var block=Assert.Single((await Dashboard(api)).Blocks,x=>x.Key=="expenseCategories");Assert.Equal(2,block.Rows.Count);
        Assert.Contains(block.Rows,x=>((JsonElement)x["category"]!).GetString()=="Office / Delivery");
        Assert.Contains(block.Rows,x=>((JsonElement)x["category"]!).GetString()=="Warehouse / Delivery");
    }
    [Fact]
    public async Task Stock_current_balance_reserve_history_and_writeoffs_use_current_views()
    {
        await using var api=await fixture.CreateApiAsync();var stock=await api.ReceiveAsync();await api.CreateSaleAsync(stock);
        var table=await Read(api,"stock","&view=batches&withReservations=true");var row=Assert.Single(table.Rows);Assert.Equal(10m,Cell(row,"physical"));Assert.Equal(4m,Cell(row,"reserved"));Assert.Equal(6m,Cell(row,"available"));Assert.Equal(100m,Value(table,"stockCost"));
        var history=await Read(api,"stock",$"&view=movements&batchId={stock.BatchId}");Assert.Equal(10m,Cell(Assert.Single(history.Rows),"balanceAfter"));
        var reason=await api.CreateReasonAsync();await api.PostWriteOffAsync((await api.CreateWriteOffAsync(stock.WriteOffInput(reason.Id,1m))).Document.Id);
        Assert.Equal(10m,Value(await Read(api,"stock","&view=writeoffs"),"writeoffs"));
    }
    [Fact]
    public async Task Money_opening_turnover_and_negative_closing_separate_transfers_and_currencies_and_owner_never_enters_profit()
    {
        await using var api=await fixture.CreateApiAsync();var a=await api.CreateAccountAsync(name:"A");var b=await api.CreateAccountAsync(name:"B");var c=await api.CreateAccountAsync(Guid.NewGuid(),name:"Foreign");
        await api.PostMoneyAsync("receipts",(await api.CreateMoneyAsync("receipts",a.Receipt(100m,MoneyPurpose.OWNER) with {DocumentDate=new(2026,9,30)})).Document.Id);
        await api.PostMoneyAsync("payments",(await api.CreateMoneyAsync("payments",a.Payment(150m,MoneyPurpose.OWNER))).Document.Id);
        await api.PostMoneyAsync("transfers",(await api.CreateMoneyAsync("transfers",a.Transfer(b,20m,20m))).Document.Id);
        await api.PostMoneyAsync("receipts",(await api.CreateMoneyAsync("receipts",c.Receipt(5m))).Document.Id);
        var table=await Read(api,"money");var row=Assert.Single(table.Rows,x=>((JsonElement)x["account"]!).GetString()=="A");
        Assert.Equal(100m,Cell(row,"opening"));Assert.Equal(150m,Cell(row,"payments"));Assert.Equal(0m,Cell(row,"receipts"));Assert.Equal(20m,Cell(row,"transfersOut"));Assert.Equal(-70m,Cell(row,"closing"));
        Assert.Equal(2,table.Metrics.Count);Assert.Equal(-50m,Assert.Single(table.Metrics,x=>x.CurrencyId==SettlementCurrencies.Uah).Value);
        Assert.Equal(0m,Kpi(await Dashboard(api),"managementResult"));Assert.Single((await Read(api,"money",$"&accountId={a.Id}&currencyId={a.CurrencyId}")).Rows);
    }
    [Fact]
    public async Task Settlements_keep_signs_and_currencies_separate_and_dashboard_recent_types_respect_scope()
    {
        await using var api=await fixture.CreateApiAsync();var (stock,sale)=await api.SoldGoodsAsync();
        var foreign=await api.PurchaseForAsync(api.CounterpartyId,Guid.NewGuid());await api.PostAsync(foreign.Document.Id);
        var table=await Read(api,"settlements");Assert.Equal(3,table.Rows.Count);Assert.Single(table.Rows,x=>Cell(x,"balance")>0);Assert.Equal(2,table.Rows.Count(x=>Cell(x,"balance")<0));
        Assert.Single((await Read(api,"settlements","&view=debtors")).Rows);Assert.Equal(2,(await Read(api,"settlements","&view=creditors")).Rows.Count);
        api.SetOrganization(Guid.NewGuid());foreach(var kind in new[]{"sales","profitability","purchases","money","stock","expenses","settlements"})Assert.Empty((await Read(api,kind)).Rows);
        Assert.Empty((await Dashboard(api)).Blocks.Single(x=>x.Key=="recent").Rows);
    }
    [Fact]
    public async Task Backend_enforces_report_special_permissions_and_never_returns_hidden_cost_keys_or_money_data()
    {
        await using var api=await fixture.IdentityApiAsync();await api.LoginAdminAsync();var (_,viewer,role)=await api.LimitedUserAsync("reports.view");using var session=viewer;
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/reports/profitability")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/reports/money")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/reports/settlements")).StatusCode);
        var dash=await api.ReadAsync<DashboardSummary>(await viewer.GetAsync($"/dashboard/summary?{Period}"));Assert.DoesNotContain(dash.Kpis,x=>x.Key is "cogs" or "grossProfit" or "managementResult" or "money" or "receivables" or "payables");Assert.DoesNotContain(dash.Blocks,x=>x.Key is "moneyAccounts" or "debtors" or "creditors");
        var table=await api.ReadAsync<ReportTable>(await viewer.GetAsync($"/reports/stock?{Period}"));Assert.DoesNotContain(table.Columns,x=>x.Key is "valuation" or "currentUnitCost");
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions",new RolePermissionsRequest(["reports.view","profit.view"]),ApiTestHost.Json);
        table=await api.ReadAsync<ReportTable>(await viewer.GetAsync($"/reports/profitability?{Period}"));Assert.DoesNotContain(table.Columns,x=>x.Key=="currentCost");
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions",new RolePermissionsRequest(["profit.view"]),ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.Forbidden,(await viewer.GetAsync("/reports/profitability")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync($"/dashboard/summary?{Period}")).StatusCode);
    }
    [Fact]
    public async Task Report_rejects_invalid_period_and_reads_never_write_business_data()
    {
        await using var api=await fixture.CreateApiAsync();await api.SoldGoodsAsync();var before=await api.QueryAsync(db=>db.Documents.CountAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await api.Client.GetAsync("/reports/sales?from=2026-10-31&to=2026-10-01")).StatusCode);
        await Dashboard(api);await Read(api,"profitability");Assert.Equal(before,await api.QueryAsync(db=>db.Documents.CountAsync()));
        await api.WithDbAsync(db=>{Assert.False(db.Database.HasPendingModelChanges());return Task.CompletedTask;});
    }
}
