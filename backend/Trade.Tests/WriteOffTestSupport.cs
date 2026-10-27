using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Inventory;

namespace Trade.Tests;

public sealed record WriteOffCostData(Guid WriteOffId, Guid DocumentId, DateOnly DocumentDate, Guid StorageLocationId,
    Guid WriteOffReasonId, Guid ExpenseCategoryId, decimal TotalCostUah);

internal static class WriteOffTestSupport
{
    public static async Task<WriteOffReasonResponse> CreateReasonAsync(this ApiTestHost api, Guid? categoryId = null,
        string name = "Damage", bool active = true) =>
        await api.ReadAsync<WriteOffReasonResponse>(await api.Client.PostAsJsonAsync("/write-off-reasons",
            new WriteOffReasonRequest(name, categoryId ?? (await api.CreateCategoryAsync()).Id, active), ApiTestHost.Json), HttpStatusCode.Created);
    public static WriteOffRequest WriteOffInput(this ReceiptStock stock, Guid reasonId, decimal quantity = 2m) =>
        new(new DateOnly(2026, 10, 24), stock.LocationId, reasonId, [new(null, 1, stock.BatchId, quantity)], "Write-off", "Note");
    public static async Task<WriteOffResponse> CreateWriteOffAsync(this ApiTestHost api, WriteOffRequest input) =>
        await api.ReadAsync<WriteOffResponse>(await api.Client.PostAsJsonAsync("/write-offs", input, ApiTestHost.Json), HttpStatusCode.Created);
    public static async Task<WriteOffResponse> PostWriteOffAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<WriteOffResponse>(await api.Client.PostAsync($"/write-offs/{id}/post", null));
    public static async Task<WriteOffResponse> UnpostWriteOffAsync(this ApiTestHost api, Guid id) =>
        await api.ReadAsync<WriteOffResponse>(await api.Client.PostAsync($"/write-offs/{id}/unpost", null));
    public static UpdateWriteOffRequest EditWriteOff(this WriteOffResponse w) =>
        new(w.Document.Version, w.Document.DocumentDate, w.StorageLocationId, w.WriteOffReasonId,
            w.Lines.Select(x => new WriteOffLineInput(x.Id, x.LineNo, x.BatchId, x.Quantity)).ToArray(), w.Document.Comment, w.Document.InternalNote);
    public static Task<WriteOffCostData[]> WriteOffCostsAsync(this ApiTestHost api) =>
        api.QueryAsync(db => db.Database.SqlQuery<WriteOffCostData>($"""
            SELECT cost.* FROM v_write_off_cost cost JOIN documents document ON document."Id" = cost."DocumentId"
            WHERE document."OrganizationId" = {api.OrganizationId} ORDER BY cost."WriteOffId"
            """).ToArrayAsync());
}
