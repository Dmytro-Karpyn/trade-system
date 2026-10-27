using Trade.Application;

namespace Trade.Api;

public static class PricingEndpoints
{
    public static void MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/price-types").WithTags("Pricing");
        types.MapPost("", async (PriceTypeRequest request, RequestContext context, PricingService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var type = await service.CreateTypeAsync(context.OrganizationId, request, ct);
            return Results.Created($"/price-types/{type.Id}", type);
        }).Produces<PriceTypeResponse>(201);
        types.MapGet("", async (RequestContext context, PricingService service, CancellationToken ct) =>
            Results.Ok(await service.ListTypesAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<PriceTypeResponse>>();
        types.MapGet("/{id:guid}", async (Guid id, RequestContext context, PricingService service, CancellationToken ct) =>
            Results.Ok(await service.GetTypeAsync(context.OrganizationId, id, ct))).Produces<PriceTypeResponse>();
        types.MapPut("/{id:guid}", async (Guid id, PriceTypeRequest request, RequestContext context, PricingService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateTypeAsync(context.OrganizationId, id, request, ct));
        }).Produces<PriceTypeResponse>();

        app.MapGet("/batches/{batchId:guid}/prices/{priceTypeId:guid}", async (Guid batchId, Guid priceTypeId,
            RequestContext context, PricingService service, CancellationToken ct) =>
            Results.Ok(await service.GetBatchPriceAsync(context.OrganizationId, batchId, priceTypeId, ct)))
            .WithTags("Pricing").Produces<BatchPriceResponse>();
        app.MapPut("/batches/{batchId:guid}/prices/{priceTypeId:guid}", async (Guid batchId, Guid priceTypeId,
            PriceRequest request, RequestContext context, PricingService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.SetBatchPriceAsync(context.OrganizationId, batchId, priceTypeId, request, ct));
        }).WithTags("Pricing").Produces<BatchPriceResponse>();

        app.MapGet("/products/{productId:guid}/prices/{priceTypeId:guid}", async (Guid productId, Guid priceTypeId,
            RequestContext context, PricingService service, CancellationToken ct) =>
            Results.Ok(await service.GetProductPriceAsync(context.OrganizationId, productId, priceTypeId, ct)))
            .WithTags("Pricing").Produces<ProductPriceResponse>();
        app.MapPut("/products/{productId:guid}/prices/{priceTypeId:guid}", async (Guid productId, Guid priceTypeId,
            PriceRequest request, RequestContext context, PricingService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.SetProductPriceAsync(context.OrganizationId, productId, priceTypeId, request, ct));
        }).WithTags("Pricing").Produces<ProductPriceResponse>();
    }
}
