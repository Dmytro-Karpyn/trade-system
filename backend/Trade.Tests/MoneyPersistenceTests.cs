using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Trade.Application;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class MoneyPersistenceTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("receipts", "money-insert")]
    [InlineData("payments", "settlement-insert")]
    [InlineData("transfers", "transfer-credit")]
    public async Task Failed_POST_rolls_back_both_journals_document_and_partial_transfer(string kind, string failure)
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateAccountAsync();
        var b = await api.CreateAccountAsync(name: "Bank");
        var request = kind switch { "receipts" => a.Receipt().Settlement(api), "payments" => a.Payment().Settlement(api), _ => a.Transfer(b) };
        var money = await api.CreateMoneyAsync(kind, request);
        var table = failure == "settlement-insert" ? "settlement_movements" : "money_movements";
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_money_test() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected money failure' USING ERRCODE='23514'; END; $$;
            """));
        var trigger = failure switch
        {
            "settlement-insert" => "CREATE TRIGGER fail_money_test BEFORE INSERT ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_money_test();",
            "transfer-credit" => "CREATE TRIGGER fail_money_test BEFORE INSERT ON money_movements FOR EACH ROW WHEN (NEW.\"Amount\" > 0) EXECUTE FUNCTION fail_money_test();",
            _ => "CREATE TRIGGER fail_money_test BEFORE INSERT ON money_movements FOR EACH ROW EXECUTE FUNCTION fail_money_test();"
        };
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/money/{kind}/{money.Document.Id}/post", null)).StatusCode);
        Assert.Equal(money, await api.ReadMoneyAsync(kind, money.Document.Id));
        Assert.Empty(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Empty(await api.SettlementsAsync(money.Document.Id));
        Assert.Empty(await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")));
        var cleanup = table == "settlement_movements"
            ? "DROP TRIGGER fail_money_test ON settlement_movements; DROP FUNCTION fail_money_test();"
            : "DROP TRIGGER fail_money_test ON money_movements; DROP FUNCTION fail_money_test();";
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(cleanup));
        await api.PostMoneyAsync(kind, money.Document.Id);
        Assert.Equal(kind == "transfers" ? 2 : 1, (await api.MoneyMovementsAsync(money.Document.Id)).Length);
    }

    [Theory]
    [InlineData("money_movements")]
    [InlineData("settlement_movements")]
    public async Task Failed_UNPOST_rolls_back_already_deleted_rows_in_both_journals(string table)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var money = await api.CreateMoneyAsync("receipts", account.Receipt().Settlement(api));
        var posted = await api.PostMoneyAsync("receipts", money.Document.Id);
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_money_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected unpost failure' USING ERRCODE='23514'; END; $$;
            """));
        var trigger = table == "money_movements"
            ? "CREATE TRIGGER fail_money_delete BEFORE DELETE ON money_movements FOR EACH ROW EXECUTE FUNCTION fail_money_delete();"
            : "CREATE TRIGGER fail_money_delete BEFORE DELETE ON settlement_movements FOR EACH ROW EXECUTE FUNCTION fail_money_delete();";
        await api.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(trigger));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/money/receipts/{money.Document.Id}/unpost", null)).StatusCode);
        Assert.Equal(posted, await api.ReadMoneyAsync("receipts", money.Document.Id));
        Assert.Single(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Single(await api.SettlementsAsync(money.Document.Id));
        Assert.Equal(100m, await api.MoneyBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Schema_contains_only_required_fields_and_ordinary_SUM_balance_view()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var (table, expected) in new[]
        {
            ("money_accounts", new[] { "Id", "OrganizationId", "Name", "AccountType", "CurrencyId", "Active" }),
            ("money_documents", new[] { "DocumentId", "Purpose", "CounterpartyId", "SourceAccountId", "DestinationAccountId", "SourceAmount", "DestinationAmount", "SettlementCurrencyId", "SettlementAmount", "SettlementExchangeRate" }),
            ("money_movements", new[] { "Id", "DocumentId", "MoneyAccountId", "Amount" }),
            ("v_money_account_balance", new[] { "MoneyAccountId", "CurrencyId", "Balance" })
        })
            Assert.Equal(expected, await api.QueryAsync(db => db.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema='public' AND table_name={table} ORDER BY ordinal_position
                """).ToArrayAsync()));
        Assert.Equal("v", await api.QueryAsync(db => db.Database.SqlQueryRaw<string>("SELECT relkind::text AS \"Value\" FROM pg_class WHERE oid='v_money_account_balance'::regclass").SingleAsync()));
        Assert.Equal(4, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns WHERE table_schema='public' AND numeric_precision=20 AND numeric_scale=4
              AND ((table_name='money_movements' AND column_name='Amount') OR (table_name='money_documents' AND column_name IN ('SourceAmount','DestinationAmount','SettlementAmount')))
            """).SingleAsync()));
        Assert.Equal(0, await api.QueryAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns c JOIN information_schema.tables t ON c.table_schema=t.table_schema AND c.table_name=t.table_name
            WHERE c.table_schema='public' AND t.table_type='BASE TABLE' AND c.table_name LIKE 'money_%'
              AND lower(replace(c.column_name,'_','')) IN ('balance','allownegativebalance','operationtype','effectiverate','effectivexchangerate','movementdate')
            """).SingleAsync()));
    }

    [Fact]
    public async Task Database_rejects_duplicate_movements_and_cross_organization_account_references()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var money = await api.CreateMoneyAsync("receipts", account.Receipt());
        await api.PostMoneyAsync("receipts", money.Document.Id);
        api.SetOrganization(Guid.NewGuid());
        var foreign = await api.CreateAccountAsync();
        api.SetOrganization(api.OrganizationId);
        foreach (var sql in new[] { $"UPDATE money_documents SET \"DestinationAccountId\"='{foreign.Id}' WHERE \"DocumentId\"='{money.Document.Id}'",
            $"UPDATE money_movements SET \"MoneyAccountId\"='{foreign.Id}' WHERE \"DocumentId\"='{money.Document.Id}'" })
            await api.WithDbAsync(async db => Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState));
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO money_movements ("Id","DocumentId","MoneyAccountId","Amount")
                SELECT {Guid.NewGuid()}, "DocumentId", "MoneyAccountId", "Amount" FROM money_movements WHERE "DocumentId"={money.Document.Id}
                """));
            Assert.Equal("23505", error.SqlState);
        });
    }

    [Fact]
    public async Task Money_migration_preserves_existing_purchase_stock_prices_and_settlements()
    {
        await using var api = await fixture.CreateApiAsync();
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah, quantity: 2m, price: 20m);
        var posted = await api.PostAsync(purchase.Document.Id);
        var settlement = Assert.Single(await api.SettlementsAsync(purchase.Document.Id));
        var batch = await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync());
        await api.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261020112046_CounterpartiesAndSettlements");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        });
        Assert.Equal(posted.Document, (await api.ReadAsync<PurchaseResponse>(await api.Client.GetAsync($"/purchases/{purchase.Document.Id}"))).Document);
        Assert.Equal(settlement.Id, Assert.Single(await api.SettlementsAsync(purchase.Document.Id)).Id);
        Assert.Equal(batch, await api.QueryAsync(db => db.Batches.Select(x => x.Id).SingleAsync()));
        var account = await api.CreateAccountAsync();
        var payment = await api.CreateMoneyAsync("payments", account.Payment(40m).Settlement(api, amount: 40m));
        await api.PostMoneyAsync("payments", payment.Document.Id);
        Assert.Equal(0m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"))).Balance);
        Assert.Equal(-40m, await api.MoneyBalanceAsync(account.Id));
    }
}
