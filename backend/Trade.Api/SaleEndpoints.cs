using Trade.Application;

namespace Trade.Api;

public static class SaleEndpoints
{
    public static void MapSaleEndpoints(this IEndpointRouteBuilder app)
    {
        var sales = app.MapGroup("/sales").WithTags("Sales");
        sales.MapGet("", async (string? search, DateOnly? from, DateOnly? to, Guid? counterpartyId, Guid? storageLocationId,
            Guid? priceTypeId, Trade.Domain.Documents.DocumentStatus? status, RequestContext context, SaleQueryService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, new(search, from, to, counterpartyId, storageLocationId, priceTypeId, status), ct)))
            .Produces<IReadOnlyList<SaleListResponse>>();
        sales.MapGet("/lookups", async (RequestContext context, SaleQueryService service, CancellationToken ct) =>
            Results.Ok(await service.LookupsAsync(context.OrganizationId, ct))).Produces<SaleLookupsResponse>();
        sales.MapGet("/price-options", async (Guid productId, Guid? priceTypeId, RequestContext context, SaleQueryService service, CancellationToken ct) =>
            Results.Ok(await service.PricesAsync(context.OrganizationId, productId, priceTypeId, ct))).Produces<IReadOnlyList<SalePriceOption>>();
        sales.MapGet("/{id:guid}/cost", async (Guid id, RequestContext context, SaleQueryService service, CancellationToken ct) =>
            Results.Ok(await service.CostAsync(context.OrganizationId, id, ct))).Produces<SaleCostResponse>();
        sales.MapGet("/{id:guid}/profit", async (Guid id, RequestContext context, SaleQueryService service, CancellationToken ct) =>
            Results.Ok(await service.ProfitAsync(context.OrganizationId, id, ct))).Produces<SaleProfitResponse>();
        sales.MapPost("", async (SaleRequest request, RequestContext context, SaleService service, CancellationToken ct) =>
        {
            var sale = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/sales/{sale.Document.Id}", sale);
        }).Produces<SaleResponse>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
        sales.MapGet("/{id:guid}", async (Guid id, RequestContext context, SaleService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<SaleResponse>();
        sales.MapPut("/{id:guid}", async (Guid id, UpdateSaleRequest request, RequestContext context, SaleService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<SaleResponse>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
        sales.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, SaleService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<SaleResponse>().ProducesProblem(403).ProducesProblem(409);
        sales.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, SaleService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<SaleResponse>().ProducesProblem(409);
        sales.MapDelete("/{id:guid}", async (Guid id, RequestContext context, SaleService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204);

        app.MapGet("/stock/available-batches", async (Guid productId, Guid storageLocationId, Guid? excludeSaleId,
            RequestContext context, SaleService service, CancellationToken ct) =>
            Results.Ok(await service.AvailableBatchesAsync(context.OrganizationId, productId, storageLocationId, ct, excludeSaleId)))
            .WithTags("Stock").Produces<IReadOnlyList<AvailableBatchResponse>>();
    }
}
