using System.Net;
using System.Net.Http.Json;
using Trade.Application;
using Trade.Domain.Returns;

namespace Trade.Tests;

internal static class SupplierReturnTestSupport
{
    public static SupplierReturnRequest SupplierInput(this ReceiptStock stock, decimal quantity = 2m) =>
        new(new DateOnly(2026, 10, 20), stock.Purchase.Document.Id, stock.LocationId,
            [new SupplierReturnLineInput(null, stock.Purchase.Lines.Single(x => x.RemovedAt is null).Id, quantity)], "Supplier return", "Note");

    public static async Task<SupplierReturnResponse> CreateSupplierReturnAsync(this ApiTestHost api, SupplierReturnRequest input) =>
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PostAsJsonAsync("/supplier-returns", input, ApiTestHost.Json), HttpStatusCode.Created);
    public static async Task<SupplierReturnResponse> PostSupplierReturnAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PostAsync($"/supplier-returns/{id}/post", null));
    public static async Task<SupplierReturnResponse> UnpostSupplierReturnAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<SupplierReturnResponse>(await api.Client.PostAsync($"/supplier-returns/{id}/unpost", null));
    public static async Task<SupplierReturnableLineResponse[]> SupplierReturnableAsync(this ApiTestHost api, Guid purchaseId, Guid? location = null) =>
        await api.ReadAsync<SupplierReturnableLineResponse[]>(await api.Client.GetAsync($"/purchases/{purchaseId}/returnable-lines"
            + (location is { } id ? $"?storageLocationId={id}" : "")));
    public static UpdateSupplierReturnRequest EditSupplierReturn(this SupplierReturnResponse r) =>
        new(r.Document.Version, r.Document.DocumentDate, r.SourcePurchaseInvoiceId, r.StorageLocationId,
            r.Lines.Select(x => new SupplierReturnLineInput(x.Id, x.SourcePurchaseLineId, x.Quantity)).ToArray(),
            r.Document.Comment, r.Document.InternalNote);
}
