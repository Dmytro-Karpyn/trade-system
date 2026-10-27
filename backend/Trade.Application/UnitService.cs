using Trade.Domain.Catalog;

namespace Trade.Application;

public sealed record UnitRequest(string Name);
public sealed record UnitResponse(Guid Id, Guid OrganizationId, string Name);

// Only the lookup and inline creation needed by Product forms; no directory management module.
public sealed class UnitService(ITradeStore store)
{
    public async Task<IReadOnlyList<UnitResponse>> ListAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListUnitsAsync(organizationId, ct)).Select(x => new UnitResponse(x.Id, x.OrganizationId, x.Name)).ToArray();

    public Task<UnitResponse> CreateAsync(Guid organizationId, UnitRequest request, CancellationToken ct) =>
        store.InTransactionAsync(_ =>
        {
            var unit = Unit.Create(organizationId, request.Name);
            store.AddUnit(unit);
            return Task.FromResult(new UnitResponse(unit.Id, unit.OrganizationId, unit.Name));
        }, ct);
}
