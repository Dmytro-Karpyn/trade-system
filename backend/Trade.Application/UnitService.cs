using Trade.Domain.Catalog;

namespace Trade.Application;

public sealed record UnitRequest(string Name,bool Active=true);
public sealed record UnitResponse(Guid Id, Guid OrganizationId, string Name,bool Active=true);

// Shared lookup and inline creation; directory updates use the same Unit entity.
public sealed class UnitService(ITradeStore store)
{
    public async Task<IReadOnlyList<UnitResponse>> ListAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListUnitsAsync(organizationId, ct)).Select(x => new UnitResponse(x.Id, x.OrganizationId, x.Name,x.Active)).ToArray();

    public Task<UnitResponse> CreateAsync(Guid organizationId, UnitRequest request, CancellationToken ct) =>
        store.InTransactionAsync(_ =>
        {
            var unit = Unit.Create(organizationId, request.Name,request.Active);
            store.AddUnit(unit);
            return Task.FromResult(new UnitResponse(unit.Id, unit.OrganizationId, unit.Name,unit.Active));
        }, ct);
}
