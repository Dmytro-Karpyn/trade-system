using Trade.Application;

namespace Trade.Api;

public static class SettlementEndpoints
{
    public static void MapSettlementEndpoints(this IEndpointRouteBuilder app)
    {
        var parties = app.MapGroup("/counterparties").WithTags("Counterparties");
        parties.MapPost("", async (CounterpartyRequest request, RequestContext context, CounterpartyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            var party = await service.CreateAsync(context.OrganizationId, request, ct);
            return Results.Created($"/counterparties/{party.Id}", party);
        }).Produces<CounterpartyResponse>(201);
        parties.MapGet("", async (bool? isCustomer, bool? isSupplier, bool? isEmployee, bool? active, string? search, bool? isSystem,
            RequestContext context, CounterpartyService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(context.OrganizationId, new(isCustomer, isSupplier, isEmployee, active, search, isSystem), ct)))
            .Produces<IReadOnlyList<CounterpartyResponse>>();
        parties.MapGet("/{id:guid}", async (Guid id, RequestContext context, CounterpartyService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(context.OrganizationId, id, ct))).Produces<CounterpartyResponse>();
        parties.MapPut("/{id:guid}", async (Guid id, CounterpartyRequest request, RequestContext context,
            CounterpartyService service, CancellationToken ct) =>
        {
            _ = context.ActorId;
            return Results.Ok(await service.UpdateAsync(context.OrganizationId, id, request, ct));
        }).Produces<CounterpartyResponse>();

        var settlements = app.MapGroup("/settlements").WithTags("Settlements");
        settlements.MapGet("/balances", async (Guid? counterpartyId, Guid? currencyId, RequestContext context,
            SettlementService service, CancellationToken ct) =>
            Results.Ok(await service.BalancesAsync(context.OrganizationId, counterpartyId, currencyId, ct)))
            .Produces<IReadOnlyList<SettlementBalanceResponse>>();
        settlements.MapGet("/counterparties/{id:guid}", async (Guid id, RequestContext context,
            SettlementService service, CancellationToken ct) =>
            Results.Ok(await service.CounterpartyAsync(context.OrganizationId, id, ct))).Produces<CounterpartySettlementResponse>();
    }
}
