using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ExpenseLifecycleTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Category_CRUD_supports_a_deep_tree_and_rejects_self_and_ancestor_cycles()
    {
        await using var api = await fixture.CreateApiAsync();
        var root = await api.CreateCategoryAsync(" Root ");
        Assert.Equal("Root", root.Name);
        var leaf = root;
        for (var depth = 0; depth < 20; depth++) leaf = await api.CreateCategoryAsync($"Level {depth}", leaf.Id);
        Assert.Equal(leaf, await api.ReadAsync<ExpenseCategoryResponse>(await api.Client.GetAsync($"/expense-categories/{leaf.Id}")));
        Assert.Equal(21, (await api.ReadAsync<ExpenseCategoryResponse[]>(await api.Client.GetAsync("/expense-categories"))).Length);
        foreach (var parent in new[] { root.Id, leaf.Id })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expense-categories/{root.Id}",
                new ExpenseCategoryRequest("Root", parent), ApiTestHost.Json)).StatusCode);
        var edited = await api.ReadAsync<ExpenseCategoryResponse>(await api.Client.PutAsJsonAsync($"/expense-categories/{leaf.Id}",
            new ExpenseCategoryRequest("Detached", null, false), ApiTestHost.Json));
        Assert.Null(edited.ParentId);
        Assert.False(edited.Active);
    }

    [Fact]
    public async Task Concurrent_reparenting_cannot_create_a_cycle_between_different_categories()
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateCategoryAsync("A");
        var b = await api.CreateCategoryAsync("B");
        var responses = await Task.WhenAll(
            api.Client.PutAsJsonAsync($"/expense-categories/{a.Id}", new ExpenseCategoryRequest("A", b.Id), ApiTestHost.Json),
            api.Client.PutAsJsonAsync($"/expense-categories/{b.Id}", new ExpenseCategoryRequest("B", a.Id), ApiTestHost.Json));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        var categories = await api.ReadAsync<ExpenseCategoryResponse[]>(await api.Client.GetAsync("/expense-categories"));
        Assert.Single(categories, x => x.ParentId == null);
    }

    [Fact]
    public async Task DRAFT_has_no_movements_and_POST_creates_only_a_negative_settlement_using_its_own_rate()
    {
        await using var api = await fixture.CreateApiAsync();
        var currency = Guid.NewGuid();
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(amount: 12.3456m, rate: 39.123456m, currencyId: currency));
        Assert.Equal(DocumentType.EXPENSE, expense.Document.DocumentType);
        Assert.Equal(DocumentStatus.DRAFT, expense.Document.Status);
        Assert.Null(expense.Document.Number);
        Assert.Equal("Expense comment", expense.Document.Comment);
        Assert.Equal("Internal expense note", expense.Document.InternalNote);
        Assert.Empty(await api.SettlementsAsync(expense.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        var posted = await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(DocumentStatus.POSTED, posted.Document.Status);
        Assert.StartsWith("EX-", posted.Document.Number);
        var movement = Assert.Single(await api.SettlementsAsync(expense.Document.Id));
        Assert.Equal(api.CounterpartyId, movement.CounterpartyId);
        Assert.Equal(currency, movement.CurrencyId);
        Assert.Equal(-12.3456m, movement.Amount);
        Assert.Equal(-decimal.Round(12.3456m * 39.123456m, 4, MidpointRounding.AwayFromZero), movement.BaseAmountUah);
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        var balances = await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"));
        Assert.Equal(-12.3456m, Assert.Single(balances).Balance);
    }

    [Fact]
    public async Task Concurrent_POST_is_idempotent_and_UNPOST_repost_keeps_the_number_without_duplicate_settlements()
    {
        await using var api = await fixture.CreateApiAsync();
        var expense = await api.CreateExpenseAsync();
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostExpenseAsync(expense.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0].Document, x.Document));
        Assert.Single(await api.SettlementsAsync(expense.Document.Id));
        var draft = await api.UnpostExpenseAsync(expense.Document.Id);
        Assert.Equal(DocumentStatus.DRAFT, draft.Document.Status);
        Assert.Empty(await api.SettlementsAsync(expense.Document.Id));
        Assert.Equal(draft.Document, (await api.UnpostExpenseAsync(expense.Document.Id)).Document);
        var edited = await api.UpdateExpenseAsync(draft, draft.EditExpense() with { Amount = 125m, Comment = "Edited" });
        Assert.Equal("Edited", edited.Document.Comment);
        var reposted = await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(posts[0].Document.Number, reposted.Document.Number);
        Assert.Equal(-125m, Assert.Single(await api.SettlementsAsync(expense.Document.Id)).Amount);
        var second = await api.CreateExpenseAsync();
        Assert.NotEqual(reposted.Document.Number, (await api.PostExpenseAsync(second.Document.Id)).Document.Number);
    }

    [Fact]
    public async Task Editing_requires_a_current_version_and_DRAFT_status()
    {
        await using var api = await fixture.CreateApiAsync();
        var expense = await api.CreateExpenseAsync();
        var edit = expense.EditExpense();
        var changed = await api.UpdateExpenseAsync(expense, edit with { Amount = 250m, ExchangeRate = 1.234567m });
        Assert.Equal(1.234567m, changed.ExchangeRate);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/expenses/{expense.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        var posted = await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/expenses/{expense.Document.Id}", posted.EditExpense(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(posted, await api.ReadAsync<ExpenseResponse>(await api.Client.GetAsync($"/expenses/{expense.Document.Id}")));
    }

    [Fact]
    public async Task POSTED_cannot_be_deleted_and_DRAFT_delete_is_logical_and_hides_all_document_operations()
    {
        await using var api = await fixture.CreateApiAsync();
        var expense = await api.CreateExpenseAsync();
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/expenses/{expense.Document.Id}")).StatusCode);
        Assert.Single(await api.SettlementsAsync(expense.Document.Id));
        expense = await api.UnpostExpenseAsync(expense.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/expenses/{expense.Document.Id}")).StatusCode);
        var deleted = await api.QueryAsync(db => db.Expenses.Include(x => x.Document).SingleAsync());
        Assert.NotNull(deleted.Document.DeletedAt);
        Assert.Equal(api.ActorId, deleted.Document.DeletedBy);
        Assert.Equal(1, await api.QueryAsync(db => db.Expenses.CountAsync()));
        Assert.Empty(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/expenses/{expense.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/expenses/{expense.Document.Id}", expense.EditExpense(), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/expenses/{expense.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/expenses/{expense.Document.Id}/unpost", null)).StatusCode);
        Assert.Empty(await api.SettlementsAsync(expense.Document.Id));
    }

    [Fact]
    public async Task Expense_is_paid_separately_by_a_MoneyPayment_SETTLEMENT()
    {
        await using var api = await fixture.CreateApiAsync();
        var expense = await api.CreateExpenseAsync();
        await api.PostExpenseAsync(expense.Document.Id);
        var account = await api.CreateAccountAsync();
        var payment = await api.CreateMoneyAsync("payments", account.Payment(100m).Settlement(api, amount: 100m));
        await api.PostMoneyAsync("payments", payment.Document.Id);
        var balances = await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"));
        Assert.Equal(0m, Assert.Single(balances).Balance);
        Assert.Equal(-100m, await api.MoneyBalanceAsync(account.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.Where(x => x.DocumentId == expense.Document.Id).ToListAsync()));
    }

    [Fact]
    public async Task List_filters_are_inclusive_and_compose_without_leaking_deleted_expenses()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync();
        await api.PostAsync(purchase.Document.Id);
        var input = await api.ExpenseInputAsync(purchase.Document.Id);
        var target = await api.CreateExpenseAsync(input);
        await api.PostExpenseAsync(target.Document.Id);
        var otherParty = await api.CreateCounterpartyAsync();
        var other = await api.CreateExpenseAsync((await api.ExpenseInputAsync(currencyId: Guid.NewGuid())) with
            { DocumentDate = input.DocumentDate.AddDays(-1), CounterpartyId = otherParty.Id });
        var deleted = await api.CreateExpenseAsync(input);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/expenses/{deleted.Document.Id}")).StatusCode);
        var filters = new[] { $"from={input.DocumentDate:yyyy-MM-dd}", $"to={input.DocumentDate.AddDays(-1):yyyy-MM-dd}",
            $"counterpartyId={input.CounterpartyId}", $"categoryId={input.ExpenseCategoryId}", $"purchaseInvoiceId={purchase.Document.Id}",
            "status=POSTED", $"currencyId={input.CurrencyId}" };
        foreach (var filter in filters)
        {
            var expected = filter.StartsWith("to=", StringComparison.Ordinal) ? other.Document.Id : target.Document.Id;
            Assert.Equal(expected, Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync($"/expenses?{filter}"))).Document.Id);
        }
        var combined = string.Join('&', filters.Where(x => !x.StartsWith("to=", StringComparison.Ordinal))) + $"&to={input.DocumentDate:yyyy-MM-dd}";
        Assert.Equal(target.Document.Id, Assert.Single(await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync($"/expenses?{combined}"))).Document.Id);
        Assert.Equal(2, (await api.ReadAsync<ExpenseResponse[]>(await api.Client.GetAsync("/expenses"))).Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/expenses?from=2026-10-21&to=2026-10-20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.GetAsync("/expenses?status=UNKNOWN")).StatusCode);
    }
}
