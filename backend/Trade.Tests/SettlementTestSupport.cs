using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Settlements;

namespace Trade.Tests;

internal static class SettlementTestSupport
{
    public static void SetOrganization(this ApiTestHost api, Guid organizationId)
    {
        api.Client.DefaultRequestHeaders.Remove("X-Test-Organization-Id");
        api.Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", organizationId.ToString());
    }

    public static Task<SettlementMovement[]> SettlementsAsync(this ApiTestHost api, Guid documentId) =>
        api.QueryAsync(db => db.SettlementMovements.AsNoTracking().Where(x => x.DocumentId == documentId).ToArrayAsync());

    public static async Task<PurchaseResponse> PurchaseForAsync(this ApiTestHost api, Guid partyId, Guid currencyId,
        decimal quantity = 1m, decimal price = 10m, decimal discount = 0m, decimal rate = 1m, DateOnly? date = null)
    {
        var purchase = await api.CreatePurchaseAsync(quantity: quantity);
        var edit = ApiTestHost.Edit(purchase);
        return await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}", edit with
        {
            CounterpartyId = partyId, CurrencyId = currencyId, DiscountPercent = discount, ExchangeRate = rate,
            DocumentDate = date ?? edit.DocumentDate, Lines = [edit.Lines[0] with { UnitPriceCurrency = price }]
        }, ApiTestHost.Json));
    }

    public static async Task<SaleResponse> ServiceSaleForAsync(this ApiTestHost api, Guid partyId, decimal price,
        decimal quantity = 1m, decimal discount = 0m, DateOnly? date = null)
    {
        var product = await api.CreateProductAsync(ItemType.SERVICE);
        return await api.CreateSaleAsync(api.NewInput(Guid.NewGuid(), [new(null, 1, product.Id, null, quantity, price)]) with
            { CounterpartyId = partyId, DiscountPercent = discount, DocumentDate = date ?? new DateOnly(2026, 10, 20) });
    }
}
