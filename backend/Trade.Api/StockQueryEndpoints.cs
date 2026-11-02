using Trade.Application;
using Trade.Application.Authentication;

namespace Trade.Api;

public static class StockQueryEndpoints
{
    public static void MapStockQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/stock").WithTags("Stock reads");
        stock.MapGet("/lookups", async (RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.LookupsAsync(context.OrganizationId, ct))).Produces<StockLookups>();
        stock.MapGet("/overview", async (string? search, Guid? categoryId, Guid? storageLocationId, bool? withStock, bool? withReservations,
            bool? byBatch, Guid? productId, Guid? retailPriceTypeId, Guid? wholesalePriceTypeId,
            RequestContext context, ICurrentPermissions permissions, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.OverviewAsync(context.OrganizationId, new(search, categoryId, storageLocationId, withStock ?? false,
                withReservations ?? false, byBatch ?? false, productId, retailPriceTypeId, wholesalePriceTypeId), permissions.Has("cost.view"), ct)))
            .Produces<IReadOnlyList<StockRow>>();
        stock.MapGet("/products/{id:guid}", async (Guid id, Guid? retailPriceTypeId, Guid? wholesalePriceTypeId, RequestContext context,
            ICurrentPermissions permissions, IStockQueries queries, CancellationToken ct) => Results.Ok(await queries.ProductAsync(context.OrganizationId,
                id, retailPriceTypeId, wholesalePriceTypeId, permissions.Has("cost.view"), ct))).Produces<StockProductDetail>();
        stock.MapGet("/batches/{id:guid}", async (Guid id, RequestContext context, ICurrentPermissions permissions, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.BatchAsync(context.OrganizationId, id, permissions.Has("cost.view"), ct))).Produces<StockBatchDetail>();
        stock.MapGet("/movements", async (Guid? productId, Guid? batchId, Guid? storageLocationId, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.MovementsAsync(context.OrganizationId, productId, batchId, storageLocationId, ct))).Produces<IReadOnlyList<StockMovementEntry>>();
        stock.MapGet("/reservations", async (Guid? productId, Guid? batchId, Guid? storageLocationId, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ReservationsAsync(context.OrganizationId, productId, batchId, storageLocationId, ct))).Produces<IReadOnlyList<StockReservationEntry>>();
        stock.MapGet("/operation-batches", async (Guid storageLocationId, Guid? excludeDocumentId, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.CandidatesAsync(context.OrganizationId, storageLocationId, excludeDocumentId, ct))).Produces<IReadOnlyList<StockCandidate>>();
        app.MapGet("/write-offs/{id:guid}/cost", async (Guid id, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.WriteOffCostAsync(context.OrganizationId, id, ct))).Produces<StockWriteOffCost>();
        app.MapGet("/stock-transfers/{id:guid}/stock-lines", async (Guid id, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.OperationLinesAsync(context.OrganizationId, id, true, ct))).Produces<IReadOnlyList<StockRow>>();
        app.MapGet("/write-offs/{id:guid}/stock-lines", async (Guid id, RequestContext context, IStockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.OperationLinesAsync(context.OrganizationId, id, false, ct))).Produces<IReadOnlyList<StockRow>>();
    }
}
