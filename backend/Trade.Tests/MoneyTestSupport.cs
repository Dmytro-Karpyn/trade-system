using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Money;
using Trade.Domain.Settlements;

namespace Trade.Tests;

internal static class MoneyTestSupport
{
    public static async Task<MoneyAccountResponse> CreateAccountAsync(this ApiTestHost api, Guid? currencyId = null,
        MoneyAccountType type = MoneyAccountType.CASH, string name = "Cash") =>
        await api.ReadAsync<MoneyAccountResponse>(await api.Client.PostAsJsonAsync("/money/accounts",
            new MoneyAccountRequest(name, type, currencyId ?? SettlementCurrencies.Uah), ApiTestHost.Json), HttpStatusCode.Created);

    public static MoneyDocumentRequest Receipt(this MoneyAccountResponse account, decimal amount = 100m, MoneyPurpose? purpose = null) =>
        new(new DateOnly(2026, 10, 20), "Receipt", Purpose: purpose, DestinationAccountId: account.Id, DestinationAmount: amount);
    public static MoneyDocumentRequest Payment(this MoneyAccountResponse account, decimal amount = 100m, MoneyPurpose? purpose = null) =>
        new(new DateOnly(2026, 10, 20), "Payment", Purpose: purpose, SourceAccountId: account.Id, SourceAmount: amount);
    public static MoneyDocumentRequest Transfer(this MoneyAccountResponse source, MoneyAccountResponse destination,
        decimal sourceAmount = 100m, decimal destinationAmount = 100m) =>
        new(new DateOnly(2026, 10, 20), "Transfer", SourceAccountId: source.Id, DestinationAccountId: destination.Id,
            SourceAmount: sourceAmount, DestinationAmount: destinationAmount);
    public static MoneyDocumentRequest Settlement(this MoneyDocumentRequest request, ApiTestHost api,
        Guid? currencyId = null, decimal amount = 100m, decimal? rate = null) => request with
        {
            Purpose = MoneyPurpose.SETTLEMENT, CounterpartyId = api.CounterpartyId,
            SettlementCurrencyId = currencyId ?? SettlementCurrencies.Uah, SettlementAmount = amount, SettlementExchangeRate = rate
        };

    public static async Task<MoneyDocumentResponse> CreateMoneyAsync(this ApiTestHost api, string kind, MoneyDocumentRequest request) =>
        await api.ReadAsync<MoneyDocumentResponse>(await api.Client.PostAsJsonAsync($"/money/{kind}", request, ApiTestHost.Json), HttpStatusCode.Created);
    public static async Task<MoneyDocumentResponse> PostMoneyAsync(this ApiTestHost api, string kind, Guid id) =>
        await api.ReadAsync<MoneyDocumentResponse>(await api.Client.PostAsync($"/money/{kind}/{id}/post", null));
    public static async Task<MoneyDocumentResponse> UnpostMoneyAsync(this ApiTestHost api, string kind, Guid id) =>
        await api.ReadAsync<MoneyDocumentResponse>(await api.Client.PostAsync($"/money/{kind}/{id}/unpost", null));
    public static async Task<MoneyDocumentResponse> ReadMoneyAsync(this ApiTestHost api, string kind, Guid id) =>
        await api.ReadAsync<MoneyDocumentResponse>(await api.Client.GetAsync($"/money/{kind}/{id}"));
    public static UpdateMoneyDocumentRequest Edit(MoneyDocumentResponse x) => new(x.Document.Version, x.Document.DocumentDate,
        x.Document.Comment, x.Document.InternalNote, x.Purpose, x.CounterpartyId, x.SourceAccountId, x.DestinationAccountId,
        x.SourceAmount, x.DestinationAmount, x.SettlementCurrencyId, x.SettlementAmount, x.SettlementExchangeRate);
    public static Task<MoneyMovement[]> MoneyMovementsAsync(this ApiTestHost api, Guid id) =>
        api.QueryAsync(db => db.MoneyMovements.AsNoTracking().Where(x => x.DocumentId == id).OrderBy(x => x.MoneyAccountId).ToArrayAsync());
    public static async Task<decimal> MoneyBalanceAsync(this ApiTestHost api, Guid accountId) =>
        (await api.ReadAsync<MoneyBalanceResponse[]>(await api.Client.GetAsync("/money/balances")))
            .SingleOrDefault(x => x.MoneyAccountId == accountId)?.Balance ?? 0m;
}
