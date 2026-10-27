using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Documents;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class ExpensePersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settlement_failure_rolls_back_POST_or_UNPOST_and_the_current_cost(bool unpost)
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.CreatePurchaseAsync(quantity: 10m);
        await api.PostAsync(purchase.Document.Id);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id));
        if (unpost) expense = await api.PostExpenseAsync(expense.Document.Id);
        var costs = await api.BatchCostsAsync();
        var settlements = await api.SettlementsAsync(expense.Document.Id);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_expense_settlement() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected expense settlement failure' USING ERRCODE = '23514'; END;
            $$;
            """));
        var trigger = unpost
            ? "CREATE TRIGGER fail_expense_settlement BEFORE DELETE ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_expense_settlement();"
            : "CREATE TRIGGER fail_expense_settlement BEFORE INSERT ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_expense_settlement();";
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        var action = unpost ? "unpost" : "post";
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/expenses/{expense.Document.Id}/{action}", null)).StatusCode);
        Assert.Equal(expense, await api.ReadAsync<ExpenseResponse>(await api.Client.GetAsync($"/expenses/{expense.Document.Id}")));
        Assert.Equal(costs, await api.BatchCostsAsync());
        Assert.Equal(settlements.Select(x => x.Id), (await api.SettlementsAsync(expense.Document.Id)).Select(x => x.Id));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
    }

    [Fact]
    public async Task Schema_contains_only_requested_expense_fields_and_an_ordinary_computed_cost_view()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("expense_categories", new[] { "Id", "OrganizationId", "ParentId", "Name", "Active" }),
            ("expenses", new[] { "DocumentId", "CounterpartyId", "ExpenseCategoryId", "CurrencyId", "Amount", "ExchangeRate", "PurchaseInvoiceId" })
        })
        {
            var columns = await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = {table}
                """).ToArrayAsync());
            Assert.Equal(expected.Order(), columns.Order());
        }
        Assert.Equal(new[] { "Amount:20:4", "ExchangeRate:20:6" }, await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT column_name || ':' || numeric_precision || ':' || numeric_scale AS "Value"
            FROM information_schema.columns WHERE table_name = 'expenses' AND data_type = 'numeric' ORDER BY column_name
            """).ToArrayAsync()));
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT relkind::text AS "Value" FROM pg_class WHERE oid = 'v_batch_cost'::regclass
            """).SingleAsync()));
        Assert.Equal(0, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_schema = 'public'
                AND lower(table_name) IN ('purchasecostallocation', 'purchase_cost_allocations', 'expense_allocations')
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_rejects_cross_organization_expense_references_category_cycles_and_purchase_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var root = await api.CreateCategoryAsync("Root");
        var child = await api.CreateCategoryAsync("Child", root.Id);
        var purchase = await api.CreatePurchaseAsync();
        await api.PostAsync(purchase.Document.Id);
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(purchase.Document.Id));
        api.SetOrganization(Guid.NewGuid());
        var foreignCategory = await api.CreateCategoryAsync();
        var foreignParty = await api.CreateCounterpartyAsync();
        api.SetOrganization(api.OrganizationId);
        await api.WithDbAsync(async db =>
        {
            await ConstraintAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE expense_categories SET "ParentId" = {child.Id} WHERE "Id" = {root.Id}
                """));
            await ConstraintAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE expense_categories SET "ParentId" = {foreignCategory.Id} WHERE "Id" = {root.Id}
                """));
            await ConstraintAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE expenses SET "ExpenseCategoryId" = {foreignCategory.Id} WHERE "DocumentId" = {expense.Document.Id}
                """));
            await ConstraintAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE expenses SET "CounterpartyId" = {foreignParty.Id} WHERE "DocumentId" = {expense.Document.Id}
                """));
            // Clear audit fields too, so dependency protection is the rejecting constraint.
            await ConstraintAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE documents SET "Status" = 'DRAFT', "PostedBy" = NULL, "PostedAt" = NULL WHERE "Id" = {purchase.Document.Id}
                """));
        });

        static async Task ConstraintAsync(Func<Task<int>> operation) =>
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(operation)).SqlState);
    }

    [Fact]
    public async Task Expense_migration_preserves_previous_ledgers_and_prices_and_replaces_cost_view_on_upgrade()
    {
        await using var api = await fixture.CreateApiAsync();
        var stock = await api.ReceiveAsync();
        var purchaseSettlements = await api.SettlementsAsync(stock.Purchase.Document.Id);
        var account = await api.CreateAccountAsync();
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(30m));
        await api.PostMoneyAsync("receipts", receipt.Document.Id);
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020122749_MoneyAccountsAndDocuments");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(stock.BatchId, await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync()));
        Assert.Equal(purchaseSettlements.Select(x => x.Id), (await api.SettlementsAsync(stock.Purchase.Document.Id)).Select(x => x.Id));
        Assert.Equal(30m, await api.MoneyBalanceAsync(account.Id));
        var expense = await api.CreateExpenseAsync(await api.ExpenseInputAsync(stock.Purchase.Document.Id, 20m));
        await api.PostExpenseAsync(expense.Document.Id);
        Assert.Equal(12m, Assert.Single(await api.BatchCostsAsync()).UnitCostUah);
        Assert.Equal(DocumentStatus.POSTED, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{stock.Purchase.Document.Id}"))).Document.Status);
    }
}
