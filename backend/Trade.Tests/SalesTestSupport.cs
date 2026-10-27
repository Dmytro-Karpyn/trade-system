using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Sales;
using Xunit;

namespace Trade.Tests;

public sealed record ReceiptStock(PurchaseResponse Purchase, Guid BatchId)
{
    public Guid ProductId => Purchase.Lines.Single(x => x.RemovedAt is null).ProductId;
    public Guid LocationId => Purchase.StorageLocationId;
}

internal static class SalesTestSupport
{
    public static async Task<ReceiptStock> ReceiveAsync(this ApiTestHost api, decimal quantity = 10m,
        Guid? productId = null, Guid? locationId = null, DateOnly? date = null)
    {
        var purchase = await api.CreatePurchaseAsync(productId, quantity, storageLocationId: locationId);
        if (date is { } documentDate)
            purchase = await api.ReadAsync<PurchaseResponse>(await api.Client.PutAsJsonAsync($"/purchases/{purchase.Document.Id}",
                ApiTestHost.Edit(purchase) with { DocumentDate = documentDate }, ApiTestHost.Json));
        await api.PostAsync(purchase.Document.Id);
        var lineId = purchase.Lines.Single(x => x.RemovedAt is null).Id;
        var batchId = await api.QueryAsync(db => db.Batches.Where(x => x.PurchaseInvoiceLineId == lineId).Select(x => x.Id).SingleAsync());
        return new(purchase, batchId);
    }

    public static SaleRequest SaleInput(this ReceiptStock stock, decimal quantity = 4m) =>
        new(new DateOnly(2026, 10, 20), "Sale", null, stock.Purchase.CounterpartyId!.Value, stock.LocationId, null, 0m,
            [new(null, 1, stock.ProductId, stock.BatchId, quantity, 12m)]);

    public static SaleRequest NewInput(this ApiTestHost api, Guid locationId, IReadOnlyList<SaleLineRequest> lines) =>
        new(new DateOnly(2026, 10, 20), "Sale", null, api.CounterpartyId, locationId, null, 0m, lines);

    public static async Task<SaleResponse> CreateSaleAsync(this ApiTestHost api, SaleRequest request) =>
        await api.ReadAsync<SaleResponse>(await api.Client.PostAsJsonAsync("/sales", request, ApiTestHost.Json), HttpStatusCode.Created);

    public static Task<SaleResponse> CreateSaleAsync(this ApiTestHost api, ReceiptStock stock, decimal quantity = 4m) =>
        api.CreateSaleAsync(stock.SaleInput(quantity));

    public static async Task<SaleResponse> PostSaleAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<SaleResponse>(await api.Client.PostAsync($"/sales/{id}/post", null));

    public static async Task<SaleResponse> UnpostSaleAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<SaleResponse>(await api.Client.PostAsync($"/sales/{id}/unpost", null));

    public static UpdateSaleRequest Edit(SaleResponse sale) => new(sale.Document.Version, sale.Document.DocumentDate,
        sale.Document.Comment, sale.Document.InternalNote, sale.CounterpartyId, sale.StorageLocationId,
        sale.PriceTypeId, sale.DiscountPercent, sale.Lines.Where(x => x.RemovedAt is null).Select(x => new SaleLineRequest(
            x.Id, x.LineNo, x.ProductId, x.BatchId, x.Quantity, x.UnitPrice)).ToArray());

    public static async Task<SaleResponse> UpdateSaleAsync(this ApiTestHost api, Guid id, UpdateSaleRequest request) =>
        await api.ReadAsync<SaleResponse>(await api.Client.PutAsJsonAsync($"/sales/{id}", request, ApiTestHost.Json));

    public static async Task<StockBalanceResponse> BalanceAsync(this ApiTestHost api, ReceiptStock stock) =>
        Assert.Single(await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync(
            $"/stock/balances?batchId={stock.BatchId}&storageLocationId={stock.LocationId}")));

    public static async Task InsufficientAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"Expected 409, got {response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("INSUFFICIENT_STOCK", json.RootElement.GetProperty("code").GetString());
    }
}
