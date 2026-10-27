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
public sealed class MoneyLifecycleTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(MoneyAccountType.CASH)]
    [InlineData(MoneyAccountType.BANK)]
    [InlineData(MoneyAccountType.SAFE)]
    [InlineData(MoneyAccountType.OTHER)]
    public async Task Account_CRUD_round_trips_type_currency_and_active_without_persisted_balance(MoneyAccountType type)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync(type: type, name: "  Account  ");
        Assert.Equal(("Account", type, api.OrganizationId), (account.Name, account.AccountType, account.OrganizationId));
        Assert.Equal(account, await api.ReadAsync<MoneyAccountResponse>(await api.Client.GetAsync($"/money/accounts/{account.Id}")));
        Assert.Equal(account, Assert.Single(await api.ReadAsync<MoneyAccountResponse[]>(await api.Client.GetAsync("/money/accounts"))));
        var currency = Guid.NewGuid();
        var updated = await api.ReadAsync<MoneyAccountResponse>(await api.Client.PutAsJsonAsync($"/money/accounts/{account.Id}",
            new MoneyAccountRequest("Renamed", type, currency, false), ApiTestHost.Json));
        Assert.Equal(account with { Name = "Renamed", CurrencyId = currency, Active = false }, updated);
        Assert.Empty(await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")));
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    [InlineData("transfers")]
    public async Task DRAFT_has_no_money_or_settlement_movements(string kind)
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateAccountAsync();
        var b = await api.CreateAccountAsync(name: "Bank");
        var input = kind switch { "receipts" => a.Receipt().Settlement(api), "payments" => a.Payment().Settlement(api), _ => a.Transfer(b) };
        var money = await api.CreateMoneyAsync(kind, input);
        Assert.Equal(DocumentStatus.DRAFT, money.Document.Status);
        Assert.Null(money.Document.Number);
        Assert.Equal(1, money.Document.Version);
        Assert.Empty(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Empty(await api.SettlementsAsync(money.Document.Id));
        Assert.Empty(await api.QueryAsync(db => db.StockMovements.ToListAsync()));
        Assert.Empty(await api.QueryAsync(db => db.Batches.ToListAsync()));
    }

    [Fact]
    public async Task Receipt_increases_money_and_payment_can_spend_more_than_available()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(100m));
        var posted = await api.PostMoneyAsync("receipts", receipt.Document.Id);
        Assert.StartsWith("MR-", posted.Document.Number);
        Assert.Equal(100m, Assert.Single(await api.MoneyMovementsAsync(receipt.Document.Id)).Amount);
        Assert.Equal(100m, await api.MoneyBalanceAsync(account.Id));
        var payment = await api.CreateMoneyAsync("payments", account.Payment(150m));
        var paid = await api.PostMoneyAsync("payments", payment.Document.Id);
        Assert.StartsWith("MP-", paid.Document.Number);
        Assert.Equal(-150m, Assert.Single(await api.MoneyMovementsAsync(payment.Document.Id)).Amount);
        Assert.Equal(-50m, await api.MoneyBalanceAsync(account.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_records_two_actual_amounts_without_settlement_or_overdraft_checks(bool crossCurrency)
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateAccountAsync();
        var b = await api.CreateAccountAsync(crossCurrency ? Guid.NewGuid() : a.CurrencyId, MoneyAccountType.BANK, "Bank");
        var destinationAmount = crossCurrency ? 400m : 10m;
        var transfer = await api.CreateMoneyAsync("transfers", a.Transfer(b, 10m, destinationAmount));
        var posted = await api.PostMoneyAsync("transfers", transfer.Document.Id);
        Assert.StartsWith("MT-", posted.Document.Number);
        var movements = await api.MoneyMovementsAsync(transfer.Document.Id);
        Assert.Equal(2, movements.Length);
        Assert.Equal(-10m, movements.Single(x => x.MoneyAccountId == a.Id).Amount);
        Assert.Equal(destinationAmount, movements.Single(x => x.MoneyAccountId == b.Id).Amount);
        Assert.Equal(-10m, await api.MoneyBalanceAsync(a.Id));
        Assert.Equal(destinationAmount, await api.MoneyBalanceAsync(b.Id));
        Assert.Empty(await api.SettlementsAsync(transfer.Document.Id));
    }

    [Theory]
    [InlineData("receipts", MoneyPurpose.OWNER)]
    [InlineData("payments", MoneyPurpose.OWNER)]
    [InlineData("receipts", MoneyPurpose.OPENING_BALANCE)]
    [InlineData("payments", MoneyPurpose.OPENING_BALANCE)]
    [InlineData("receipts", null)]
    [InlineData("payments", null)]
    public async Task Non_settlement_purpose_needs_no_counterparty_and_creates_no_settlement(string kind, MoneyPurpose? purpose)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var input = kind == "receipts" ? account.Receipt(purpose: purpose) : account.Payment(purpose: purpose);
        var money = await api.CreateMoneyAsync(kind, input);
        Assert.Null(money.CounterpartyId);
        await api.PostMoneyAsync(kind, money.Document.Id);
        Assert.Single(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Empty(await api.SettlementsAsync(money.Document.Id));
    }

    [Theory]
    [InlineData("receipts", -1)]
    [InlineData("payments", 1)]
    public async Task Foreign_settlement_uses_its_own_amount_currency_and_rate_with_the_correct_sign(string kind, int sign)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var currency = Guid.NewGuid();
        var input = (kind == "receipts" ? account.Receipt(100m) : account.Payment(100m))
            .Settlement(api, currency, 2.5m, 40.12345678m);
        var money = await api.CreateMoneyAsync(kind, input);
        await api.PostMoneyAsync(kind, money.Document.Id);
        var settlement = Assert.Single(await api.SettlementsAsync(money.Document.Id));
        Assert.Equal((api.CounterpartyId, currency, sign * 2.5m, sign * 100.3086m),
            (settlement.CounterpartyId, settlement.CurrencyId, settlement.Amount, settlement.BaseAmountUah));
        Assert.Equal(kind == "receipts" ? 100m : -100m, Assert.Single(await api.MoneyMovementsAsync(money.Document.Id)).Amount);
        var history = await api.ReadAsync<CounterpartySettlementResponse>(await api.Client.GetAsync($"/settlements/counterparties/{api.CounterpartyId}"));
        Assert.Equal(money.Document.DocumentType, Assert.Single(history.Movements).DocumentType);
        Assert.Equal(sign * 2.5m, history.Balances[0].Balance);
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    public async Task UAH_settlement_defaults_its_rate_to_one(string kind)
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var input = (kind == "receipts" ? account.Receipt() : account.Payment()).Settlement(api, amount: 55m);
        var money = await api.CreateMoneyAsync(kind, input);
        Assert.Equal(1m, money.SettlementExchangeRate);
        await api.PostMoneyAsync(kind, money.Document.Id);
        var settlement = Assert.Single(await api.SettlementsAsync(money.Document.Id));
        Assert.Equal(settlement.Amount, settlement.BaseAmountUah);
        Assert.Equal(SettlementCurrencies.Uah, settlement.CurrencyId);
    }

    [Theory]
    [InlineData("receipts")]
    [InlineData("payments")]
    [InlineData("transfers")]
    public async Task Concurrent_POST_is_idempotent_and_UNPOST_repost_preserves_number(string kind)
    {
        await using var api = await fixture.CreateApiAsync();
        var a = await api.CreateAccountAsync();
        var b = await api.CreateAccountAsync(name: "Bank");
        var input = kind switch { "receipts" => a.Receipt().Settlement(api), "payments" => a.Payment().Settlement(api), _ => a.Transfer(b) };
        var money = await api.CreateMoneyAsync(kind, input);
        var posts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostMoneyAsync(kind, money.Document.Id)));
        Assert.All(posts, x => Assert.Equal(posts[0], x));
        Assert.Equal(kind == "transfers" ? 2 : 1, (await api.MoneyMovementsAsync(money.Document.Id)).Length);
        Assert.Equal(kind == "transfers" ? 0 : 1, (await api.SettlementsAsync(money.Document.Id)).Length);
        var draft = await api.UnpostMoneyAsync(kind, money.Document.Id);
        Assert.Equal(DocumentStatus.DRAFT, draft.Document.Status);
        Assert.Equal(posts[0].Document.Number, draft.Document.Number);
        Assert.Empty(await api.MoneyMovementsAsync(money.Document.Id));
        Assert.Empty(await api.SettlementsAsync(money.Document.Id));
        Assert.Equal(draft, await api.UnpostMoneyAsync(kind, money.Document.Id));
        var reposted = await api.PostMoneyAsync(kind, money.Document.Id);
        Assert.Equal(posts[0].Document.Number, reposted.Document.Number);
        Assert.Equal(kind == "transfers" ? 2 : 1, (await api.MoneyMovementsAsync(money.Document.Id)).Length);
        Assert.Equal(kind == "transfers" ? 0 : 1, (await api.SettlementsAsync(money.Document.Id)).Length);
    }

    [Fact]
    public async Task UNPOST_of_a_receipt_can_leave_account_negative_and_balance_view_sums_only_current_movements()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(100m));
        var payment = await api.CreateMoneyAsync("payments", account.Payment(80m));
        await api.PostMoneyAsync("receipts", receipt.Document.Id);
        await api.PostMoneyAsync("payments", payment.Document.Id);
        Assert.Equal(20m, await api.MoneyBalanceAsync(account.Id));
        await api.UnpostMoneyAsync("receipts", receipt.Document.Id);
        Assert.Equal(-80m, await api.MoneyBalanceAsync(account.Id));
        var row = Assert.Single(await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")));
        Assert.Equal((account.Id, account.CurrencyId, -80m), (row.MoneyAccountId, row.CurrencyId, row.Balance));
    }

    [Fact]
    public async Task Draft_edit_requires_current_version_and_posted_documents_cannot_be_edited()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt(100m));
        var edit = MoneyTestSupport.Edit(receipt);
        var updated = await api.ReadAsync<MoneyDocumentResponse>(await api.Client.PutAsJsonAsync($"/money/receipts/{receipt.Document.Id}",
            edit with { DestinationAmount = 120m, Comment = "Edited" }, ApiTestHost.Json));
        Assert.Equal(120m, updated.DestinationAmount);
        Assert.Empty(await api.MoneyMovementsAsync(receipt.Document.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await api.Client.PutAsJsonAsync($"/money/receipts/{receipt.Document.Id}", edit, ApiTestHost.Json)).StatusCode);
        var posted = await api.PostMoneyAsync("receipts", receipt.Document.Id);
        Assert.Equal(120m, Assert.Single(await api.MoneyMovementsAsync(receipt.Document.Id)).Amount);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client.PutAsJsonAsync($"/money/receipts/{receipt.Document.Id}", MoneyTestSupport.Edit(posted), ApiTestHost.Json)).StatusCode);
        Assert.Equal(posted, await api.ReadMoneyAsync("receipts", receipt.Document.Id));
        foreach (var other in new[] { "payments", "transfers" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.GetAsync($"/money/{other}/{receipt.Document.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PutAsJsonAsync($"/money/{other}/{receipt.Document.Id}", MoneyTestSupport.Edit(posted), ApiTestHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/money/{other}/{receipt.Document.Id}/post", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await api.Client.PostAsync($"/money/{other}/{receipt.Document.Id}/unpost", null)).StatusCode);
        }
    }

    [Fact]
    public async Task Customer_receipt_and_supplier_payment_offset_invoice_settlements_without_allocation_tables()
    {
        await using var api = await fixture.CreateApiAsync();
        var account = await api.CreateAccountAsync();
        var sale = await api.ServiceSaleForAsync(api.CounterpartyId, 100m);
        await api.PostSaleAsync(sale.Document.Id);
        var receipt = await api.CreateMoneyAsync("receipts", account.Receipt().Settlement(api));
        await api.PostMoneyAsync("receipts", receipt.Document.Id);
        Assert.Equal(0m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"))).Balance);
        var purchase = await api.PurchaseForAsync(api.CounterpartyId, SettlementCurrencies.Uah, price: 60m);
        await api.PostAsync(purchase.Document.Id);
        var payment = await api.CreateMoneyAsync("payments", account.Payment(60m).Settlement(api, amount: 60m));
        await api.PostMoneyAsync("payments", payment.Document.Id);
        Assert.Equal(0m, Assert.Single(await api.ReadAsync<SettlementBalanceResponse[]>(await api.Client.GetAsync("/settlements/balances"))).Balance);
        Assert.Equal(40m, await api.MoneyBalanceAsync(account.Id));
    }
}
