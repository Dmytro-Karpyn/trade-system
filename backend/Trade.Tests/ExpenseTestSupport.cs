using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

internal static class ExpenseTestSupport
{
    public static async Task<ExpenseCategoryResponse> CreateCategoryAsync(this ApiTestHost api,
        string name = "Expense category", Guid? parentId = null) =>
        await api.ReadAsync<ExpenseCategoryResponse>(await api.Client.PostAsJsonAsync("/expense-categories",
            new ExpenseCategoryRequest(name, parentId), ApiTestHost.Json), HttpStatusCode.Created);

    public static async Task<ExpenseRequest> ExpenseInputAsync(this ApiTestHost api, Guid? purchaseId = null,
        decimal amount = 100m, decimal rate = 1m, Guid? currencyId = null) =>
        new(new DateOnly(2026, 10, 20), api.CounterpartyId, (await api.CreateCategoryAsync()).Id,
            currencyId ?? SettlementCurrencies.Uah, amount, rate, purchaseId, "Expense comment", "Internal expense note");

    public static async Task<ExpenseResponse> CreateExpenseAsync(this ApiTestHost api, ExpenseRequest? request = null) =>
        await api.ReadAsync<ExpenseResponse>(await api.Client.PostAsJsonAsync("/expenses",
            request ?? await api.ExpenseInputAsync(), ApiTestHost.Json), HttpStatusCode.Created);

    public static async Task<ExpenseResponse> PostExpenseAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<ExpenseResponse>(await api.Client.PostAsync($"/expenses/{id}/post", null));

    public static async Task<ExpenseResponse> UnpostExpenseAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<ExpenseResponse>(await api.Client.PostAsync($"/expenses/{id}/unpost", null));

    public static UpdateExpenseRequest EditExpense(this ExpenseResponse expense) => new(expense.Document.Version,
        expense.Document.DocumentDate, expense.CounterpartyId, expense.ExpenseCategoryId, expense.CurrencyId,
        expense.Amount, expense.ExchangeRate, expense.PurchaseInvoiceId, expense.Document.Comment, expense.Document.InternalNote);

    public static async Task<ExpenseResponse> UpdateExpenseAsync(this ApiTestHost api, ExpenseResponse expense, UpdateExpenseRequest edit) =>
        await api.ReadAsync<ExpenseResponse>(await api.Client.PutAsJsonAsync($"/expenses/{expense.Document.Id}", edit, ApiTestHost.Json));

    public static Task<BatchCostData[]> BatchCostsAsync(this ApiTestHost api) =>
        api.QueryAsync(db => db.Database.SqlQueryRaw<BatchCostData>("SELECT * FROM v_batch_cost").ToArrayAsync());

    public static async Task ErrorCodeAsync(HttpResponseMessage response, string code, HttpStatusCode status)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
}
