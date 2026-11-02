using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ExpenseUiQueryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task List_search_and_general_direct_filters_combine_with_existing_date_party_category_currency_and_status()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(); await api.PostAsync(purchase.Document.Id);
        var category = await api.CreateCategoryAsync("Transport 10%_");
        var input = await api.ExpenseInputAsync(purchase.Document.Id, amount: 15m);
        var direct = await api.CreateExpenseAsync(input with { ExpenseCategoryId = category.Id }); await api.PostExpenseAsync(direct.Document.Id);
        var general = await api.CreateExpenseAsync(input with { PurchaseInvoiceId = null, ExpenseCategoryId = category.Id });
        var filtered = Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync($"/expenses?search=%25_&from=2026-10-20&to=2026-10-20&counterpartyId={api.CounterpartyId}&categoryId={category.Id}&currencyId={SettlementCurrencies.Uah}&isPurchaseExpense=true&status=POSTED")));
        Assert.Equal(direct.Document.Id, filtered.Document.Id);
        Assert.Equal(general.Document.Id, Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses?isPurchaseExpense=false"))).Document.Id);
        Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync($"/expenses?search={filtered.Document.Number}")));
        Assert.Empty(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses?search=not-found")));
    }

    [Fact]
    public async Task Purchase_options_are_posted_non_deleted_real_purchases_with_derived_values_and_exact_zero_allocation_state()
    {
        await using var api = await fixture.CreateApiAsync();
        var posted = await api.CreatePurchaseAsync(quantity: 10m); await api.PostAsync(posted.Document.Id);
        var draft = await api.CreatePurchaseAsync();
        var opening = await api.CreatePurchaseAsync(type: DocumentType.OPENING_STOCK); await api.PostAsync(opening.Document.Id);
        var zero = await api.CreatePurchaseAsync();
        zero = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{zero.Document.Id}", ApiTestHost.Edit(zero) with { DiscountPercent = 100m }, ApiTestHost.Json)); await api.PostAsync(zero.Document.Id);
        var tiny = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah, quantity: 0.0001m, price: 0.0001m); await api.PostAsync(tiny.Document.Id);
        var lookups = await api.ReadAsync<ExpenseLookups>(await api.Client.GetAsync("/expenses/lookups"));
        Assert.DoesNotContain(lookups.Purchases, p => p.Id == draft.Document.Id || p.Id == opening.Document.Id);
        var option = Assert.Single(lookups.Purchases, x => x.Id == posted.Document.Id); Assert.True(option.CanAllocate); Assert.Equal(100m, option.TotalCurrency); Assert.NotNull(option.Supplier); Assert.StartsWith("PI-", option.Number);
        Assert.False(Assert.Single(lookups.Purchases, x => x.Id == zero.Document.Id).CanAllocate);
        var tinyOption = Assert.Single(lookups.Purchases, x => x.Id == tiny.Document.Id); Assert.True(tinyOption.CanAllocate); Assert.Equal(0.00000001m, tinyOption.TotalCurrency);
    }

    [Fact]
    public async Task Cost_impact_reads_current_view_and_posted_direct_expenses_without_repricing_batches()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.ReadAsync<PriceTypeResponse>(await api.Client.PostAsJsonAsync("/price-types", new PriceTypeRequest("Retail", 25m), ApiTestHost.Json), HttpStatusCode.Created);
        var stock = await api.ReceiveAsync(10m);
        var prices = await api.QueryAsync(db => db.BatchPrices.Select(x => x.UnitPrice).ToArrayAsync());
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 10m, 2m, Guid.NewGuid()));
        async Task<ExpenseCostImpact> Read() => await api.ReadAsync<ExpenseCostImpact>(await api.Client.GetAsync($"/expenses/{expense.Document.Id}/cost-impact"));
        var before = await Read(); Assert.Equal(100m, before.PurchaseTotalCurrency); Assert.Equal(100m, before.PurchaseBaseValueUah); Assert.Equal(0m, before.PostedDirectExpensesUah); Assert.Equal(10m, Assert.Single(before.Batches).CurrentUnitCostUah);
        await api.PostExpenseAsync(expense.Document.Id);
        var after = await Read(); Assert.Equal(20m, after.PostedDirectExpensesUah); Assert.Equal(12m, Assert.Single(after.Batches).CurrentUnitCostUah); Assert.Equal(stock.BatchId, Assert.Single(after.Batches).BatchId);
        await api.UnpostExpenseAsync(expense.Document.Id); Assert.Equal(10m, Assert.Single((await Read()).Batches).CurrentUnitCostUah);
        Assert.Equal(prices, await api.QueryAsync(db => db.BatchPrices.Select(x => x.UnitPrice).ToArrayAsync()));
        Assert.Empty(await api.MoneyMovementsAsync(expense.Document.Id));
    }

    [Fact]
    public async Task General_deleted_and_foreign_expenses_do_not_expose_cost_or_foreign_lookups()
    {
        await using var api = await fixture.CreateApiAsync(); var general = await api.CreateExpenseAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expenses/{general.Document.Id}/cost-impact")).StatusCode);
        var stock = await api.ReceiveAsync(); var direct = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id));
        await api.Client.DeleteAsync($"/expenses/{direct.Document.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expenses/{direct.Document.Id}/cost-impact")).StatusCode);
        api.SetOrganization(Guid.NewGuid());
        var lookups = await api.ReadAsync<ExpenseLookups>(await api.Client.GetAsync("/expenses/lookups"));
        Assert.Empty(lookups.Purchases); Assert.Empty(lookups.Categories); Assert.Empty(lookups.Counterparties);
        Assert.Equal(new[] { SettlementCurrencies.Uah }, lookups.CurrencyIds);
        Assert.Empty(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses?isPurchaseExpense=false")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expenses/{general.Document.Id}/cost-impact")).StatusCode);
    }

    [Fact]
    public async Task New_read_queries_leave_the_requested_schema_unchanged()
    {
        await using var api = await fixture.CreateApiAsync();
        await api.Client.GetAsync("/expenses/lookups");
        var columns = await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_name IN ('expenses', 'expense_categories')""").ToArrayAsync());
        Assert.DoesNotContain(columns, x => x.Contains("AmountUah") || x.Contains("Cost") || x.Contains("Path") || x.Contains("Depth"));
        await api.WithDbAsync(db => { Assert.False(db.Database.HasPendingModelChanges()); return Task.CompletedTask; });
    }

    [Fact]
    public async Task Expense_permissions_and_cost_permission_are_independent_and_form_lookups_do_not_grant_other_reads()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync();
        var (_, viewer, role) = await api.LimitedUserAsync("expense.view"); using var session = viewer;
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/expenses/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/expenses/{Guid.NewGuid()}/cost-impact")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["expense.create"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/expenses/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/expenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/counterparties")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["cost.view"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/expenses/{Guid.NewGuid()}/cost-impact")).StatusCode);
    }
}
