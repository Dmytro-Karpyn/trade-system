using Trade.Application;

namespace Trade.Api;

public static class UnitEndpoints
{
    public static void MapUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var units = app.MapGroup("/units").WithTags("Units");
        units.MapGet("", async (RequestContext context, UnitService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, ct))).Produces<IReadOnlyList<UnitResponse>>();
        units.MapPost("", async (UnitRequest request, RequestContext context, UnitService service, CancellationToken ct) =>
            Results.Json(await service.CreateAsync(context.OrganizationId, request, ct), statusCode: 201)).Produces<UnitResponse>(201);
    }
}
