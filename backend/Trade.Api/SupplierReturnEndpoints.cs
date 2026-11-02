using Trade.Application;

namespace Trade.Api;

public static class SupplierReturnEndpoints
{
    public static void MapSupplierReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var returns = app.MapGroup("/supplier-returns").WithTags("Supplier returns");
        returns.MapGet("/lookups", async (RequestContext context, IReturnQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.LookupsAsync(context.OrganizationId, true, ct))).Produces<ReturnLookups>();
        returns.MapGet("/overview", async (DateOnly? from, DateOnly? to, Guid? counterpartyId, Trade.Domain.Documents.DocumentStatus? status,
            Guid? sourcePurchaseInvoiceId, RequestContext context, IReturnQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(context.OrganizationId, true, new(from, to, counterpartyId, status, sourcePurchaseInvoiceId), ct)))
            .Produces<IReadOnlyList<ReturnListItem>>();
        returns.MapPost("/preview", async (SupplierReturnRequest request, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(context.OrganizationId, context.ActorId, request, ct))).Produces<ReturnPreview>();
        returns.MapGet("/{id:guid}/refund", async (Guid id, RequestContext context, IReturnQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.SupplierRefundAsync(context.OrganizationId, id, ct))).Produces<ReturnPreview>();
        returns.MapPost("", async (SupplierReturnRequest request, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/supplier-returns/{result.Document.Id}", result);
        }).Produces<SupplierReturnResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapGet("", async (RequestContext context, SupplierReturnService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<SupplierReturnResponse>>();
        returns.MapGet("/{id:guid}", async (Guid id, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<SupplierReturnResponse>().ProducesProblem(404);
        returns.MapPut("/{id:guid}", async (Guid id, UpdateSupplierReturnRequest request, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<SupplierReturnResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<SupplierReturnResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<SupplierReturnResponse>().ProducesProblem(404).ProducesProblem(409);
        returns.MapDelete("/{id:guid}", async (Guid id, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
        app.MapPost("/purchases/{id:guid}/supplier-return", async (Guid id, SupplierReturnFromPurchaseRequest request,
            RequestContext context, SupplierReturnService service, CancellationToken ct) =>
        {
            var result = await service.FromPurchaseAsync(context.OrganizationId, context.ActorId, id, request, ct);
            return Results.Created($"/supplier-returns/{result.Document.Id}", result);
        }).WithTags("Supplier returns").Produces<SupplierReturnResponse>(201).ProducesProblem(400).ProducesProblem(404);
        app.MapGet("/purchases/{id:guid}/returnable-lines", async (Guid id, Guid? storageLocationId, Guid? excludeSupplierReturnId, RequestContext context, SupplierReturnService service, CancellationToken ct) =>
            Results.Ok(await service.ReturnableLinesAsync(context.OrganizationId, id, storageLocationId, ct, excludeSupplierReturnId)))
            .WithTags("Supplier returns").Produces<IReadOnlyList<SupplierReturnableLineResponse>>().ProducesProblem(400).ProducesProblem(404);
    }
}
