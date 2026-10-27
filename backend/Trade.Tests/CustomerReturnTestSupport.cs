using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Trade.Domain.Returns;

namespace Trade.Tests;

internal static class CustomerReturnTestSupport
{
    public static async Task<(ReceiptStock Stock, SaleResponse Sale)> SoldGoodsAsync(this ApiTestHost api, decimal quantity = 4m,
        decimal discount = 10m, decimal price = 12m)
    {
        var stock = await api.ReceiveAsync();
        var sale = await api.CreateSaleAsync(stock.SaleInput(quantity) with { DiscountPercent = discount,
            Lines = [new(null, 1, stock.ProductId, stock.BatchId, quantity, price)] });
        return (stock, await api.PostSaleAsync(sale.Document.Id));
    }

    public static CustomerReturnRequest ReturnInput(this SaleResponse sale, decimal quantity = 1m, decimal? refund = null) =>
        new(new DateOnly(2026, 10, 20), sale.Document.Id, sale.StorageLocationId,
            [new CustomerReturnLineInput(null, sale.Lines.Single(x => x.RemovedAt is null).Id, quantity, refund)], "Return", "Note");

    public static async Task<CustomerReturnResponse> CreateReturnAsync(this ApiTestHost api, CustomerReturnRequest input) =>
        await api.ReadAsync<CustomerReturnResponse>(await api.Client.PostAsJsonAsync("/customer-returns", input, ApiTestHost.Json), HttpStatusCode.Created);

    public static async Task<CustomerReturnResponse> PostReturnAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<CustomerReturnResponse>(await api.Client.PostAsync($"/customer-returns/{id}/post", null));

    public static async Task<CustomerReturnResponse> UnpostReturnAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<CustomerReturnResponse>(await api.Client.PostAsync($"/customer-returns/{id}/unpost", null));

    public static async Task<ReturnableLineResponse[]> ReturnableAsync(this ApiTestHost api, Guid saleId) =>
        await api.ReadAsync<ReturnableLineResponse[]>(await api.Client.GetAsync($"/sales/{saleId}/returnable-lines"));

    public static UpdateCustomerReturnRequest EditReturn(this CustomerReturnResponse r, bool goods = true) =>
        new(r.Document.Version, r.Document.DocumentDate, r.SourceSaleInvoiceId, r.StorageLocationId,
            r.Lines.Select(x => new CustomerReturnLineInput(x.Id, x.SourceSaleLineId, x.Quantity, goods ? null : x.RefundAmount)).ToArray(),
            r.Document.Comment, r.Document.InternalNote);
}
