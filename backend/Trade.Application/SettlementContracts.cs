using Trade.Domain.Documents;

namespace Trade.Application;

public sealed record SettlementBalanceResponse(Guid CounterpartyId, Guid CurrencyId, decimal Balance);
public sealed record SettlementHistoryResponse(Guid Id, Guid DocumentId, DocumentType DocumentType, DateOnly DocumentDate,
    Guid CounterpartyId, Guid CurrencyId, decimal Amount, decimal BaseAmountUah, decimal BalanceAfterMovement, string? DocumentNumber = null);
public sealed record CounterpartySettlementResponse(Guid CounterpartyId, IReadOnlyList<SettlementBalanceResponse> Balances,
    IReadOnlyList<SettlementHistoryResponse> Movements);

public sealed class SettlementService(ITradeStore store)
{
    public Task<IReadOnlyList<SettlementBalanceResponse>> BalancesAsync(Guid organizationId, Guid? counterpartyId,
        Guid? currencyId, CancellationToken ct) => store.GetSettlementBalancesAsync(organizationId, counterpartyId, currencyId, ct);

    public Task<CounterpartySettlementResponse> CounterpartyAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.GetCounterpartySettlementAsync(organizationId, id, ct);
}
