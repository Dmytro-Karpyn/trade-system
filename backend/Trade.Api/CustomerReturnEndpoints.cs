using Trade.Application;

namespace Trade.Api;

public static class CustomerReturnEndpoints
{
    public static void MapCustomerReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var returns = app.MapGroup("/customer-returns").WithTags("Customer returns");
        returns.MapPost("", async (CustomerReturnRequest request, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(context.OrganizationId, context.ActorId, request, ct);
            return Results.Created($"/customer-returns/{result.Document.Id}", result);
        }).Produces<CustomerReturnResponse>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapGet("", async (RequestContext context, CustomerReturnService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<CustomerReturnResponse>>();
        returns.MapGet("/{id:guid}", async (Guid id, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<CustomerReturnResponse>().ProducesProblem(404);
        returns.MapPut("/{id:guid}", async (Guid id, UpdateCustomerReturnRequest request, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<CustomerReturnResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapPost("/{id:guid}/post", async (Guid id, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
            Results.Ok(await service.PostAsync(context.OrganizationId, id, context.ActorId, ct)))
            .Produces<CustomerReturnResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        returns.MapPost("/{id:guid}/unpost", async (Guid id, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UnpostAsync(context.OrganizationId, id, ct));
        }).Produces<CustomerReturnResponse>().ProducesProblem(404).ProducesProblem(409);
        returns.MapDelete("/{id:guid}", async (Guid id, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(context.OrganizationId, id, context.ActorId, ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(404);
        app.MapPost("/sales/{id:guid}/customer-return", async (Guid id, CustomerReturnFromSaleRequest request,
            RequestContext context, CustomerReturnService service, CancellationToken ct) =>
        {
            var result = await service.FromSaleAsync(context.OrganizationId, context.ActorId, id, request, ct);
            return Results.Created($"/customer-returns/{result.Document.Id}", result);
        }).WithTags("Customer returns").Produces<CustomerReturnResponse>(201).ProducesProblem(400).ProducesProblem(404);
        app.MapGet("/sales/{id:guid}/returnable-lines", async (Guid id, RequestContext context, CustomerReturnService service, CancellationToken ct) =>
            Results.Ok(await service.ReturnableLinesAsync(context.OrganizationId, id, ct)))
            .WithTags("Customer returns").Produces<IReadOnlyList<ReturnableLineResponse>>().ProducesProblem(400).ProducesProblem(404);
    }
}
