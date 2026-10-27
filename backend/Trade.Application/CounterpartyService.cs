using Trade.Domain.Common;
using Trade.Domain.Counterparties;

namespace Trade.Application;

public sealed class CounterpartyService(ITradeStore store)
{
    public Task<CounterpartyResponse> CreateAsync(Guid organizationId, CounterpartyRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await ValidatePriceTypeAsync(organizationId, request.DefaultPriceTypeId, token);
            var party = Counterparty.Create(organizationId, request.ToDetails());
            store.AddCounterparty(party);
            return party.ToResponse();
        }, ct);

    public async Task<IReadOnlyList<CounterpartyResponse>> ListAsync(Guid organizationId, CounterpartyFilter filter, CancellationToken ct) =>
        (await store.ListCounterpartiesAsync(organizationId, filter, ct)).Select(x => x.ToResponse()).ToArray();

    public async Task<CounterpartyResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public Task<CounterpartyResponse> UpdateAsync(Guid organizationId, Guid id, CounterpartyRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var party = await FindAsync(organizationId, id, true, token);
            await ValidatePriceTypeAsync(organizationId, request.DefaultPriceTypeId, token);
            party.Update(request.ToDetails());
            return party.ToResponse();
        }, ct);

    private async Task<Counterparty> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindCounterpartyAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("Counterparty not found.");

    private async Task ValidatePriceTypeAsync(Guid organizationId, Guid? priceTypeId, CancellationToken ct)
    {
        if (priceTypeId is not { } id) return;
        Guard.RequiredId(id, nameof(priceTypeId));
        _ = await store.FindPriceTypeAsync(organizationId, id, false, ct) ?? throw new NotFoundException("PriceType not found.");
    }
}

internal static class CounterpartyReferences
{
    public static async Task RequireAsync(ITradeStore store, Guid organizationId, Guid? id, CancellationToken ct)
    {
        if (id is not { } value) return;
        Guard.RequiredId(value, "CounterpartyId");
        Guard.Against(await store.FindCounterpartyAsync(organizationId, value, false, ct) is null,
            "Counterparty must exist in the current organization.");
    }
}
