using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Api;

public static class StockTransferEndpoints
{
    public static void MapStockTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/stock-transfers").WithTags("Stock transfers");
        transfers.MapPost("", async (StockTransferRequest request, RequestContext context, StockTransferService service, CancellationToken ct) =>
        {
            var transfer = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/stock-transfers/{transfer.Document.Id}", transfer);
        }).Produces<StockTransferResponse>(201).ProducesProblem(400).ProducesProblem(409);
        transfers.MapGet("", async (DateOnly? from, DateOnly? to, Guid? fromLocationId, Guid? toLocationId,
            DocumentStatus? status, RequestContext context, StockTransferService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, new(from, to, fromLocationId, toLocationId, status), ct)))
            .Produces<IReadOnlyList<StockTransferResponse>>().ProducesProblem(400);
        transfers.MapGet("/{id:guid}", async (Guid id, RequestContext context, StockTransferService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<StockTransferResponse>().ProducesProblem(404);
        transfers.MapPut("/{id:guid}", async (Guid id, UpdateStockTransferRequest request, RequestContext context, StockTransferService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<StockTransferResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        transfers.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, StockTransferService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<StockTransferResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        transfers.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, StockTransferService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<StockTransferResponse>().ProducesProblem(404).ProducesProblem(409);
        transfers.MapDelete("/{id:guid}", async (Guid id, RequestContext context, StockTransferService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
    }
}
