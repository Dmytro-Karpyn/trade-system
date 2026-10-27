using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Inventory;

namespace Trade.Tests;

internal static class StockTransferTestSupport
{
    public static async Task<Guid> KnownLocationAsync(this ApiTestHost api) => (await api.CreatePurchaseAsync()).StorageLocationId;
    public static StockTransferRequest TransferInput(this ReceiptStock stock, Guid destination, decimal quantity = 2m) =>
        new(new DateOnly(2026, 10, 20), stock.LocationId, destination, [new(null, 1, stock.BatchId, quantity)], "Transfer", "Note");
    public static async Task<StockTransferResponse> CreateTransferAsync(this ApiTestHost api, StockTransferRequest input) =>
        await api.ReadAsync<StockTransferResponse>(await api.Client.PostAsJsonAsync("/stock-transfers", input, ApiTestHost.Json), HttpStatusCode.Created);
    public static async Task<StockTransferResponse> PostTransferAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<StockTransferResponse>(await api.Client.PostAsync($"/stock-transfers/{id}/post", null));
    public static async Task<StockTransferResponse> UnpostTransferAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<StockTransferResponse>(await api.Client.PostAsync($"/stock-transfers/{id}/unpost", null));
    public static UpdateStockTransferRequest EditTransfer(this StockTransferResponse t) =>
        new(t.Document.Version, t.Document.DocumentDate, t.FromLocationId, t.ToLocationId,
            t.Lines.Select(x => new StockTransferLineInput(x.Id, x.LineNo, x.BatchId, x.Quantity)).ToArray(),
            t.Document.Comment, t.Document.InternalNote);
    public static Task<StockMovement[]> TransferMovementsAsync(this ApiTestHost api, Guid id) =>
        api.QueryAsync(db => db.StockMovements.AsNoTracking().Where(x => x.DocumentId == id).OrderBy(x => x.StorageLocationId).ToArrayAsync());
    public static async Task<(decimal Physical, decimal Reserved, decimal Available)> LocationStockAsync(this ApiTestHost api, Guid batchId, Guid locationId)
    {
        var rows = await api.ReadAsync<StockBalanceResponse[]>(await api.Client.GetAsync($"/stock/balances?batchId={batchId}&storageLocationId={locationId}"));
        var row = rows.SingleOrDefault();
        return row is null ? (0m, 0m, 0m) : (row.PhysicalQuantity, row.ReservedQuantity, row.AvailableQuantity);
    }
}
