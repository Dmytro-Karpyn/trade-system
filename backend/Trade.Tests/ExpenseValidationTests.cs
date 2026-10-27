using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ExpenseValidationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("amount", "0")]
    [InlineData("amount", "-1")]
    [InlineData("amount", "0.00001")]
    [InlineData("amount", "10000000000000000")]
    [InlineData("rate", "0")]
    [InlineData("rate", "-1")]
    [InlineData("rate", "1.0000001")]
    [InlineData("rate", "100000000000000")]
    public async Task Invalid_numeric_values_are_rejected_on_create_and_edit_without_changing_existing_data(string field, string raw)
    {
        await using var api = await fixture.CreateApiAsync();
        var input = await api.ExpenseInputAsync();
        var value = decimal.Parse(raw, CultureInfo.InvariantCulture);
        var bad = field == "amount" ? input with { Amount = value } : input with { ExchangeRate = value };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expenses", bad, ApiTestHost.Json)).StatusCode);
        var expense = await api.CreateExpenseAsync(input);
        var edit = expense.EditExpense();
        edit = field == "amount" ? edit with { Amount = value } : edit with { ExchangeRate = value };
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expenses/{expense.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        Assert.Equal(expense, await api.ReadAsync<ExpenseResponse>(await api.Client.GetAsync($"/expenses/{expense.Document.Id}")));
    }

    [Fact]
    public async Task Required_identifiers_dates_and_category_names_are_validated()
    {
        await using var api = await fixture.CreateApiAsync();
        var category = await api.CreateCategoryAsync();
        foreach (var name in new[] { "", "   ", null! })
        {
            var input = new ExpenseCategoryRequest(name);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expense-categories", input, ApiTestHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expense-categories/{category.Id}", input, ApiTestHost.Json)).StatusCode);
        }
        foreach (var parent in new[] { Guid.Empty, Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expense-categories",
                new ExpenseCategoryRequest("Invalid", parent), ApiTestHost.Json)).StatusCode);
        var valid = await api.ExpenseInputAsync();
        var invalid = new[]
        {
            valid with { CounterpartyId = Guid.Empty }, valid with { CounterpartyId = Guid.NewGuid() },
            valid with { ExpenseCategoryId = Guid.Empty }, valid with { ExpenseCategoryId = Guid.NewGuid() },
            valid with { CurrencyId = Guid.Empty }, valid with { PurchaseInvoiceId = Guid.Empty },
            valid with { PurchaseInvoiceId = Guid.NewGuid() }, valid with { DocumentDate = default }
        };
        foreach (var input in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expenses", input, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.Expenses.ToListAsync()));
    }

    [Fact]
    public async Task Organization_isolation_covers_categories_documents_filters_and_all_references()
    {
        await using var api = await fixture.CreateApiAsync();
        var localCategory = await api.CreateCategoryAsync();
        var local = await api.CreateExpenseAsync();
        var otherOrganization = Guid.NewGuid();
        api.SetOrganization(otherOrganization);
        var foreignParty = await api.CreateCounterpartyAsync();
        var foreignCategory = await api.CreateCategoryAsync();
        var foreignPurchase = await api.CreatePurchaseAsync();
        await api.PostAsync(foreignPurchase.Document.Id);
        var foreign = await api.CreateExpenseAsync((await api.ExpenseInputAsync(foreignPurchase.Document.Id)) with { CounterpartyId = foreignParty.Id });
        api.SetOrganization(api.OrganizationId);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expense-categories",
            new ExpenseCategoryRequest("Cross organization", foreignCategory.Id), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expense-categories/{localCategory.Id}",
            new ExpenseCategoryRequest("Cross organization", foreignCategory.Id), ApiTestHost.Json)).StatusCode);
        var valid = await api.ExpenseInputAsync();
        foreach (var bad in new[] { valid with { CounterpartyId = foreignParty.Id }, valid with { ExpenseCategoryId = foreignCategory.Id },
            valid with { PurchaseInvoiceId = foreignPurchase.Document.Id } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/expenses", bad, ApiTestHost.Json)).StatusCode);
            var edit = local.EditExpense() with { CounterpartyId = bad.CounterpartyId, ExpenseCategoryId = bad.ExpenseCategoryId,
                PurchaseInvoiceId = bad.PurchaseInvoiceId };
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expenses/{local.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expense-categories/{foreignCategory.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/expense-categories/{foreignCategory.Id}",
            new ExpenseCategoryRequest("Hidden"), ApiTestHost.Json)).StatusCode);
        Assert.DoesNotContain(await api.ReadAsync<ExpenseCategoryResponse[]>(await api.Client.GetAsync("/expense-categories")), x => x.Id == foreignCategory.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expenses/{foreign.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/expenses/{foreign.Document.Id}", foreign.EditExpense(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/expenses/{foreign.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/expenses/{foreign.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/expenses/{foreign.Document.Id}")).StatusCode);
        Assert.Equal(local.Document.Id, Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses"))).Document.Id);
        foreach (var filter in new[] { $"counterpartyId={foreignParty.Id}", $"categoryId={foreignCategory.Id}", $"purchaseInvoiceId={foreignPurchase.Document.Id}" })
            Assert.Empty(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync($"/expenses?{filter}")));
    }

    [Fact]
    public async Task Unrepresentable_base_amount_rejects_POST_and_rolls_back_number_audit_and_settlement()
    {
        await using var api = await fixture.CreateApiAsync();
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(amount: 9_999_999_999_999_999.9999m, rate: 2m));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/expenses/{expense.Document.Id}/post", null)).StatusCode);
        Assert.Equal(expense, await api.ReadAsync<ExpenseResponse>(await api.Client.GetAsync($"/expenses/{expense.Document.Id}")));
        Assert.Empty(await api.SettlementsAsync(expense.Document.Id));
        Assert.Equal(DocumentStatus.DRAFT, expense.Document.Status);
    }
}
