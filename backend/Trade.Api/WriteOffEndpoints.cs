using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Api;

public static class WriteOffEndpoints
{
    public static void MapWriteOffEndpoints(this IEndpointRouteBuilder app)
    {
        var reasons = app.MapGroup("/write-off-reasons").WithTags("Write-off reasons");
        reasons.MapPost("", async (WriteOffReasonRequest request, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var reason = await service.CreateReasonAsync(context.OrganizationId, request, ct);
            return Results.Created($"/write-off-reasons/{reason.Id}", reason);
        }).Produces<WriteOffReasonResponse>(201).ProducesProblem(400);
        reasons.MapGet("", async (RequestContext context, WriteOffService service, CancellationToken ct) =>
            Results.Ok(await service.ReasonsAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<WriteOffReasonResponse>>();
        reasons.MapGet("/{id:guid}", async (Guid id, RequestContext context, WriteOffService service, CancellationToken ct) =>
            Results.Ok(await service.ReasonAsync(context.OrganizationId, id, ct))).Produces<WriteOffReasonResponse>().ProducesProblem(404);
        reasons.MapPut("/{id:guid}", async (Guid id, WriteOffReasonRequest request, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateReasonAsync(context.OrganizationId, id, request, ct));
        }).Produces<WriteOffReasonResponse>().ProducesProblem(400).ProducesProblem(404);

        var writeOffs = app.MapGroup("/write-offs").WithTags("Write-offs");
        writeOffs.MapPost("", async (WriteOffRequest request, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            var writeOff = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/write-offs/{writeOff.Document.Id}", writeOff);
        }).Produces<WriteOffResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        writeOffs.MapGet("", async (DateOnly? from, DateOnly? to, Guid? storageLocationId, Guid? writeOffReasonId,
            Guid? expenseCategoryId, DocumentStatus? status, RequestContext context, WriteOffService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, new(from, to, storageLocationId, writeOffReasonId, expenseCategoryId, status), ct)))
            .Produces<IReadOnlyList<WriteOffResponse>>().ProducesProblem(400);
        writeOffs.MapGet("/{id:guid}", async (Guid id, RequestContext context, WriteOffService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<WriteOffResponse>().ProducesProblem(404);
        writeOffs.MapPut("/{id:guid}", async (Guid id, UpdateWriteOffRequest request, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<WriteOffResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        writeOffs.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, WriteOffService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<WriteOffResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        writeOffs.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<WriteOffResponse>().ProducesProblem(404).ProducesProblem(409);
        writeOffs.MapDelete("/{id:guid}", async (Guid id, RequestContext context, WriteOffService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
    }
}
