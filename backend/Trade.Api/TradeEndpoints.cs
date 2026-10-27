using Trade.Application;
using Trade.Domain.Catalog;

namespace Trade.Api;

public static class TradeEndpoints
{
    public static void MapTradeEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/products").WithTags("Products");
        products.MapPost("", async (ProductRequest request, RequestContext context, ProductService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var product = await service.CreateAsync(context.OrganizationId, request, ct);
            return Results.Created($"/products/{product.Id}", product);
        }).Produces<ProductResponse>(201);
        products.MapGet("", async (string? search, ItemType? itemType, bool? active, RequestContext context, ProductService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, ct, new(search, itemType, active)))).Produces<IReadOnlyList<ProductResponse>>();
        products.MapGet("/{id:guid}", async (Guid id, RequestContext context, ProductService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<ProductResponse>();
        products.MapPut("/{id:guid}", async (Guid id, ProductRequest request, RequestContext context, ProductService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<ProductResponse>();

        var purchases = app.MapGroup("/purchases").WithTags("Purchases");
        purchases.MapGet("", async (string? search, DateOnly? from, DateOnly? to, Guid? counterpartyId, Guid? storageLocationId,
            Guid? currencyId, Trade.Domain.Documents.DocumentStatus? status, RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, new(search, from, to, counterpartyId, storageLocationId, currencyId, status), ct)))
            .Produces<IReadOnlyList<PurchaseListResponse>>();
        purchases.MapGet("/lookups", async (RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Ok(await service.LookupsAsync(context.OrganizationId, ct))).Produces<PurchaseLookupsResponse>();
        purchases.MapGet("/{id:guid}/batches", async (Guid id, RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Ok(await service.BatchesAsync(context.OrganizationId, id, ct))).Produces<IReadOnlyList<PurchaseBatchResponse>>();
        purchases.MapGet("/{id:guid}/batch-costs", async (Guid id, RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Ok(await service.CostsAsync(context.OrganizationId, id, ct))).Produces<IReadOnlyList<PurchaseBatchCostResponse>>();
        purchases.MapGet("/{id:guid}/batch-prices", async (Guid id, RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Ok(await service.PricesAsync(context.OrganizationId, id, ct))).Produces<IReadOnlyList<PurchaseBatchPriceResponse>>();
        app.MapPost("/storage-locations", async (StorageLocationRequest request, RequestContext context, PurchaseQueryService service, CancellationToken ct) =>
            Results.Json(await service.CreateLocationAsync(context.OrganizationId, request, ct), statusCode: 201)).WithTags("StorageLocations").Produces<StorageLocationResponse>(201);
        purchases.MapPost("", async (PurchaseRequest request, RequestContext context, PurchaseService service, CancellationToken ct) =>
        {
            var purchase = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/purchases/{purchase.Document.Id}", purchase);
        }).Produces<PurchaseResponse>(201);
        purchases.MapGet("/{id:guid}", async (Guid id, RequestContext context, PurchaseService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<PurchaseResponse>();
        purchases.MapPut("/{id:guid}", async (Guid id, UpdatePurchaseRequest request, RequestContext context,
            PurchaseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<PurchaseResponse>();
        purchases.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, PurchaseService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct))).Produces<PurchaseResponse>();
        purchases.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, PurchaseService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<PurchaseResponse>();
        purchases.MapDelete("/{id:guid}", async (Guid id, RequestContext context, PurchaseService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        });

        app.MapGet("/stock/balances", async (RequestContext context, ITradeStore store, Guid? storageLocationId,
            Guid? batchId, CancellationToken ct) =>
            Results.Ok(await store.GetBalancesAsync(context.OrganizationId, storageLocationId, batchId, ct)))
            .WithTags("Stock").Produces<IReadOnlyList<StockBalanceResponse>>();
    }
}
