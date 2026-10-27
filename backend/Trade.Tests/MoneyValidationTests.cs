using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trade.Application;
using Trade.Domain.Money;
using Trade.Domain.Settlements;
using Xunit;

namespace Trade.Tests;

[Collection("PostgreSQL")]
public sealed class MoneyValidationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    [InlineData("transfers")]
    public async Task Invalid_account_sides_or_amounts_are_rejected_without_documents_or_movements(string kind)
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateAccountAsync();
        var b = await api.CreateAccountAsync(name: "Bank");
        var input = kind switch { "receipts" => a.Receipt(), "payments" => a.Payment(), _ => a.Transfer(b) };
        var invalid = kind switch
        {
            "receipts" => new[] { input with { DestinationAccountId = null }, input with { DestinationAmount = null },
                input with { SourceAccountId = b.Id }, input with { SourceAmount = 1m }, input with { DestinationAccountId = Guid.Empty } },
            "payments" => new[] { input with { SourceAccountId = null }, input with { SourceAmount = null },
                input with { DestinationAccountId = b.Id }, input with { DestinationAmount = 1m }, input with { SourceAccountId = Guid.Empty } },
            _ => [input with { SourceAccountId = null }, input with { DestinationAccountId = null }, input with { SourceAmount = null },
                input with { DestinationAmount = null }, input with { DestinationAccountId = a.Id },
                input with { DestinationAmount = 101m }, input.Settlement(api)]
        };
        foreach (var value in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", value, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", input with { Purpose = (MoneyPurpose)999 }, ApiTestHost.Json)).StatusCode);
        foreach (var amount in new[] { 0m, -1m, 1.00001m, 10_000_000_000_000_000m })
        {
            var value = kind == "receipts" ? input with { DestinationAmount = amount } : input with { SourceAmount = amount };
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", value, ApiTestHost.Json)).StatusCode);
            if (kind == "transfers")
                Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", input with { DestinationAmount = amount }, ApiTestHost.Json)).StatusCode);
        }
        Assert.Empty(await api.QueryAsync(db => db.MoneyDocuments.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.MoneyMovements.ToListAsync()));
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    public async Task Settlement_requires_owned_counterparty_currency_amount_and_valid_rate(string kind)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var input = (kind == "receipts" ? account.Receipt() : account.Payment()).Settlement(api, Guid.NewGuid(), 10m, 40m);
        var invalid = new[]
        {
            input with { CounterpartyId = null }, input with { CounterpartyId = Guid.Empty }, input with { CounterpartyId = Guid.NewGuid() },
            input with { SettlementCurrencyId = null }, input with { SettlementCurrencyId = Guid.Empty },
            input with { SettlementAmount = null }, input with { SettlementAmount = 0m }, input with { SettlementAmount = -1m },
            input with { SettlementAmount = 0.00001m }, input with { SettlementAmount = 10_000_000_000_000_000m },
            input with { SettlementExchangeRate = null }, input with { SettlementExchangeRate = 0m },
            input with { SettlementExchangeRate = -1m }, input with { SettlementExchangeRate = 1.000000001m },
            input with { SettlementExchangeRate = 1_000_000_000_000m },
            input with { SettlementCurrencyId = SettlementCurrencies.Uah, SettlementExchangeRate = 2m }
        };
        foreach (var value in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", value, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.MoneyDocuments.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.SettlementMovements.ToListAsync()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(MoneyPurpose.OWNER)]
    [InlineData(MoneyPurpose.OPENING_BALANCE)]
    public async Task Non_settlement_purposes_require_all_settlement_fields_to_be_null(MoneyPurpose? purpose)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        foreach (var kind in new[] { "receipts", "payments" })
        {
            var input = kind == "receipts" ? account.Receipt(purpose: purpose) : account.Payment(purpose: purpose);
            foreach (var invalid in new[] { input with { SettlementCurrencyId = SettlementCurrencies.Uah },
                input with { SettlementAmount = 1m }, input with { SettlementExchangeRate = 1m } })
                Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", invalid, ApiTestHost.Json)).StatusCode);
        }
    }

    [Fact]
    public async Task Currency_can_change_only_before_account_has_any_document_references_even_after_UNPOST()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var money = await api.CreateMoneyAsync("receipts", account.Receipt());
        var request = new MoneyAccountRequest("Renamed", MoneyAccountType.BANK, Guid.NewGuid(), false);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/money/accounts/{account.Id}", request, ApiTestHost.Json)).StatusCode);
        await api.PostMoneyAsync("receipts", money.Document.Id);
        await api.UnpostMoneyAsync("receipts", money.Document.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/money/accounts/{account.Id}", request, ApiTestHost.Json)).StatusCode);
        var updated = await api.ReadAsync<MoneyAccountResponse>(await api.Client.PutAsJsonAsync($"/money/accounts/{account.Id}", request with { CurrencyId = account.CurrencyId }, ApiTestHost.Json));
        Assert.Equal(("Renamed", false), (updated.Name, updated.Active));
        await api.WithDbAsync(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE money_accounts SET "CurrencyId" = {Guid.NewGuid()} WHERE "Id" = {account.Id}
                """));
            Assert.Equal("23514", error.SqlState);
        });
    }

    [Fact]
    public async Task Invalid_account_name_type_and_currency_are_rejected()
    {
        await using var api = await fixture.CreateApiAsync();
        foreach (var input in new[] { new MoneyAccountRequest(" ", MoneyAccountType.CASH, SettlementCurrencies.Uah),
            new MoneyAccountRequest("Account", (MoneyAccountType)999, SettlementCurrencies.Uah),
            new MoneyAccountRequest("Account", MoneyAccountType.CASH, Guid.Empty) })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/money/accounts", input, ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync("/money/accounts",
            new { name = (string?)null, accountType = "CASH", currencyId = SettlementCurrencies.Uah }, ApiTestHost.Json)).StatusCode);
        Assert.Empty(await api.QueryAsync(db => db.MoneyAccounts.ToListAsync()));
    }

    [Fact]
    public async Task Foreign_accounts_and_counterparties_cannot_be_used_and_money_routes_do_not_leak_other_organizations()
    {
        await using var api = await fixture.CreateApiAsync();
        var own = await api.CreateAccountAsync();
        var receipt = await api.CreateMoneyAsync("receipts", own.Receipt().Settlement(api));
        await api.PostMoneyAsync("receipts", receipt.Document.Id);
        api.SetOrganization(Guid.NewGuid());
        var other = await api.CreateAccountAsync();
        var party = await api.CreateCounterpartyAsync();
        Assert.Equal(other, Assert.Single(await api.ReadAsync<MoneyAccountResponse[]>(await api.Client.GetAsync("/money/accounts"))));
        Assert.Empty(await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/money/accounts/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/money/accounts/{own.Id}", new MoneyAccountRequest("Not ours", own.AccountType, own.CurrencyId), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/money/receipts/{receipt.Document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/money/receipts/{receipt.Document.Id}", MoneyTestSupport.Edit(receipt), ApiTestHost.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/money/receipts/{receipt.Document.Id}/post", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/money/receipts/{receipt.Document.Id}/unpost", null)).StatusCode);
        foreach (var (kind, input) in new[] { ("receipts", own.Receipt()), ("payments", own.Payment()),
            ("transfers", own.Transfer(other)), ("transfers", other.Transfer(own)),
            ("receipts", other.Receipt().Settlement(api)) })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsJsonAsync($"/money/{kind}", input, ApiTestHost.Json)).StatusCode);
        var otherReceipt = await api.CreateMoneyAsync("receipts", other.Receipt(7m).Settlement(api, amount: 7m) with { CounterpartyId = party.Id });
        await api.PostMoneyAsync("receipts", otherReceipt.Document.Id);
        Assert.Equal(7m, await api.MoneyBalanceAsync(other.Id));
        api.SetOrganization(api.OrganizationId);
        Assert.Equal(100m, await api.MoneyBalanceAsync(own.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/money/receipts/{otherReceipt.Document.Id}")).StatusCode);
        Assert.Single(await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")));
    }

    [Fact]
    public async Task Invalid_edit_rolls_back_and_overflowing_base_settlement_rejects_POST_without_movements()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var money = await api.CreateMoneyAsync("receipts", account.Receipt().Settlement(api, Guid.NewGuid(), 9_999_999_999_999_999.9999m, 2m));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/money/receipts/{money.Document.Id}", MoneyTestSupport.Edit(money) with
            { DestinationAmount = -1m }, ApiTestHost.Json)).StatusCode);
        Assert.Equal(money, await api.ReadMoneyAsync("receipts", money.Document.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PostAsync($"/money/receipts/{money.Document.Id}/post", null)).StatusCode);
        Assert.Equal(money, await api.ReadMoneyAsync("receipts", money.Document.Id));
        Assert.Empty(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Empty(await api.SettlementsAsync(money.Document.Id));
    }
}
