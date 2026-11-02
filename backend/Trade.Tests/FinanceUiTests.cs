using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Money;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class FinanceUiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Account_overview_uses_the_balance_view_including_empty_and_negative_accounts_and_reports_currency_lock()
    {
        await using var api = await fixture.CreateApiAsync(); var account = await api.CreateAccountAsync();
        var row = Assert.Single(await api.ReadAsync<MoneyAccountOverview[]>(await api.Client.GetAsync("/money/account-overview")));
        Assert.Equal(0m, row.Balance); Assert.False(row.Account.CurrencyLocked);
        var payment = await api.CreateMoneyAsync("payments", account.Payment(150m));
        row = Assert.Single(await api.ReadAsync<MoneyAccountOverview[]>(await api.Client.GetAsync("/money/account-overview")));
        Assert.Equal(0m, row.Balance); Assert.True(row.Account.CurrencyLocked);
        await api.PostMoneyAsync("payments", payment.Document.Id);
        row = Assert.Single(await api.ReadAsync<MoneyAccountOverview[]>(await api.Client.GetAsync("/money/account-overview")));
        Assert.Equal(-150m, row.Balance);
        Assert.Equal(-150m, await api.MoneyBalanceAsync(account.Id));
    }

    [Fact]
    public async Task Money_lists_filter_by_date_account_counterparty_purpose_status_and_keep_operation_types_separate()
    {
        await using var api = await fixture.CreateApiAsync(); var account = await api.CreateAccountAsync(name: "Cash"); var destination = await api.CreateAccountAsync(name: "Bank");
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(25m).Settlement(api, amount: 25m)); await api.PostMoneyAsync("receipts", receipt.Document.Id);
        await api.CreateMoneyAsync("receipts", account.Receipt(40m, MoneyPurpose.OWNER));
        var payment = await api.CreateMoneyAsync("payments", account.Payment());
        var transfer = await api.CreateMoneyAsync("transfers", account.Transfer(destination));
        var row = Assert.Single(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync($"/money/receipts?from=2026-10-20&to=2026-10-20&accountId={account.Id}&counterpartyId={api.CounterpartyId}&purpose=SETTLEMENT&status=POSTED")));
        Assert.Equal(receipt.Document.Id, row.Money.Document.Id); Assert.Equal("Cash", row.DestinationAccountName); Assert.Equal(SettlementCurrencies.Uah, row.DestinationCurrencyId); Assert.NotNull(row.CounterpartyName);
        Assert.Equal(payment.Document.Id, Assert.Single(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync("/money/payments"))).Money.Document.Id);
        var transferRow = Assert.Single(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync($"/money/transfers?accountId={destination.Id}")));
        Assert.Equal(transfer.Document.Id, transferRow.Money.Document.Id); Assert.Equal("Cash", transferRow.SourceAccountName); Assert.Equal("Bank", transferRow.DestinationAccountName);
        Assert.Empty(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync("/money/receipts?to=2026-10-19")));
        Assert.Empty(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync("/money/transfers?purpose=SETTLEMENT")));
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    [InlineData("transfers")]
    public async Task Delete_is_logical_draft_only_preserves_business_data_and_never_creates_or_removes_posted_movements(string kind)
    {
        await using var api = await fixture.CreateApiAsync(); var a = await api.CreateAccountAsync(); var b = await api.CreateAccountAsync(name: "Other");
        var input = kind == "receipts" ? a.Receipt().Settlement(api) : kind == "payments" ? a.Payment().Settlement(api) : a.Transfer(b);
        var doc = await api.CreateMoneyAsync(kind, input);
        var posted = await api.PostMoneyAsync(kind, doc.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.DeleteAsync($"/money/{kind}/{doc.Document.Id}")).StatusCode);
        Assert.Equal(posted, await api.ReadMoneyAsync(kind, doc.Document.Id));
        Assert.NotEmpty(await api.MoneyMovementsAsync(doc.Document.Id));
        await api.UnpostMoneyAsync(kind, doc.Document.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.DeleteAsync($"/money/{kind}/{doc.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/money/{kind}/{doc.Document.Id}")).StatusCode);
        Assert.Empty(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync($"/money/{kind}")));
        var stored = await api.QueryAsync(db => db.MoneyDocuments.AsNoTracking().Include(x => x.Document).SingleAsync(x => x.DocumentId == doc.Document.Id));
        Assert.NotNull(stored.Document.DeletedAt); Assert.Equal(api.ActorId, stored.Document.DeletedBy);
        Assert.Equal(input.SourceAmount, stored.SourceAmount); Assert.Equal(input.DestinationAmount, stored.DestinationAmount);
        Assert.Empty(await api.MoneyMovementsAsync(doc.Document.Id)); Assert.Empty(await api.SettlementsAsync(doc.Document.Id));
    }

    [Fact]
    public async Task Concurrent_post_and_delete_are_serialized_by_the_same_document_lock()
    {
        await using var api = await fixture.CreateApiAsync(); var account = await api.CreateAccountAsync(); var doc = await api.CreateMoneyAsync("receipts", account.Receipt());
        var responses = await Task.WhenAll(api.Client.PostAsync($"/money/receipts/{doc.Document.Id}/post", null), api.Client.DeleteAsync($"/money/receipts/{doc.Document.Id}"));
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.BadRequest });
        var stored = await api.QueryAsync(db => db.Documents.AsNoTracking().SingleAsync(x => x.Id == doc.Document.Id));
        var movements = await api.MoneyMovementsAsync(doc.Document.Id);
        if (stored.DeletedAt is not null) { Assert.Equal(DocumentStatus.DRAFT, stored.Status); Assert.Empty(movements); }
        else { Assert.Equal(DocumentStatus.POSTED, stored.Status); Assert.Single(movements); }
    }

    [Fact]
    public async Task Unified_settlements_preserve_sign_currency_zero_rows_and_running_balance_and_search_is_literal()
    {
        await using var api = await fixture.CreateApiAsync(); var account = await api.CreateAccountAsync();
        var party = await api.CreateCounterpartyAsync(new CounterpartyRequest("Buyer 10%_", IsCustomer: true));
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(30m).Settlement(api, amount: 30m) with { CounterpartyId = party.Id }); await api.PostMoneyAsync("receipts", receipt.Document.Id);
        var payment = await api.CreateMoneyAsync("payments", account.Payment(10m).Settlement(api, amount: 10m) with { CounterpartyId = party.Id }); await api.PostMoneyAsync("payments", payment.Document.Id);
        var foreign = Guid.NewGuid(); var foreignPayment = await api.CreateMoneyAsync("payments", account.Payment(20m).Settlement(api, foreign, 2m, 10m) with { CounterpartyId = party.Id }); await api.PostMoneyAsync("payments", foreignPayment.Document.Id);
        var zeroParty = await api.CreateCounterpartyAsync(new CounterpartyRequest("Zero"));
        foreach (var kind in new[] { "receipts", "payments" })
        {
            var money = await api.CreateMoneyAsync(kind, (kind == "receipts" ? account.Receipt(5m) : account.Payment(5m)).Settlement(api, amount: 5m) with { CounterpartyId = zeroParty.Id });
            await api.PostMoneyAsync(kind, money.Document.Id);
        }
        var rows = await api.ReadAsync<SettlementOverviewRow[]>(await api.Client.GetAsync("/settlements/overview?search=%25_"));
        Assert.Equal(2, rows.Length); Assert.All(rows, x => Assert.Equal(party.Id, x.CounterpartyId));
        Assert.Equal(-20m, Assert.Single(rows, x => x.CurrencyId == SettlementCurrencies.Uah).Balance); Assert.Equal(2m, Assert.Single(rows, x => x.CurrencyId == foreign).Balance);
        Assert.Equal(zeroParty.Id, Assert.Single(await api.ReadAsync<SettlementOverviewRow[]>(await api.Client.GetAsync("/settlements/overview?state=ZERO"))).CounterpartyId);
        Assert.Equal(foreign, Assert.Single(await api.ReadAsync<SettlementOverviewRow[]>(await api.Client.GetAsync("/settlements/overview?state=OWED_TO_US"))).CurrencyId);
        Assert.Equal(-20m, Assert.Single(await api.ReadAsync<SettlementOverviewRow[]>(await api.Client.GetAsync($"/settlements/overview?state=WE_OWE&currencyId={SettlementCurrencies.Uah}"))).Balance);
        var history = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{party.Id}"));
        Assert.Equal(new[] { -30m, -20m }, history.Movements.Where(x => x.CurrencyId == SettlementCurrencies.Uah).Select(x => x.BalanceAfterMovement));
        Assert.All(history.Movements, x => Assert.NotNull(x.DocumentNumber));
    }

    [Fact]
    public async Task Finance_reads_and_delete_cannot_cross_organization_or_operation_type()
    {
        await using var api = await fixture.CreateApiAsync(); var currency = Guid.NewGuid(); var account = await api.CreateAccountAsync(currency); var doc = await api.CreateMoneyAsync("receipts", account.Receipt());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/money/payments/{doc.Document.Id}")).StatusCode);
        api.SetOrganization(Guid.NewGuid());
        Assert.Empty(await api.ReadAsync<MoneyAccountOverview[]>(await api.Client.GetAsync("/money/account-overview")));
        Assert.Empty(await api.ReadAsync<MoneyListRow[]>(await api.Client.GetAsync("/money/receipts")));
        var lookups = await api.ReadAsync<MoneyLookups>(await api.Client.GetAsync("/money/lookups")); Assert.Empty(lookups.Accounts); Assert.Empty(lookups.Counterparties); Assert.Equal(new[] { SettlementCurrencies.Uah }, lookups.CurrencyIds);
        Assert.Equal(new[] { SettlementCurrencies.Uah }, await api.ReadAsync<Guid[]>(await api.Client.GetAsync("/settlements/currencies")));
        Assert.Empty(await api.ReadAsync<SettlementOverviewRow[]>(await api.Client.GetAsync("/settlements/overview")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.DeleteAsync($"/money/receipts/{doc.Document.Id}")).StatusCode);
    }

    [Fact]
    public async Task Existing_money_permissions_protect_lists_writes_and_minimal_form_lookups_without_a_new_permission_model()
    {
        await using var api = await fixture.IdentityApiAsync(); await api.LoginAdminAsync(); var account = await api.CreateAccountAsync(); var doc = await api.CreateMoneyAsync("receipts", account.Receipt());
        var (_, viewer, role) = await api.LimitedUserAsync("money.view"); using var session = viewer;
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/money/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/money/receipts/{doc.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/settlements/overview")).StatusCode);
        await api.Client.PutAsJsonAsync($"/admin/roles/{role.Id}/permissions", new Trade.Application.Authentication.RolePermissionsRequest(["money.receipt"]), ApiTestHost.Json);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/money/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/money/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/money/payments/{doc.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.DeleteAsync($"/money/receipts/{doc.Document.Id}")).StatusCode);
    }
}
