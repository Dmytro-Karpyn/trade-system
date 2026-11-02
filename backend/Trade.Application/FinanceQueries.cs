using Trade.Domain.Documents;
using Trade.Domain.Money;

namespace Trade.Application;

public sealed record MoneyListFilter(DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null,
    Guid? CounterpartyId = null, MoneyPurpose? Purpose = null, DocumentStatus? Status = null);
public sealed record MoneyPartyOption(Guid Id, string Name, bool Active);
public sealed record MoneyAccountOption(Guid Id, string Name, MoneyAccountType AccountType, Guid CurrencyId, bool Active, bool CurrencyLocked);
public sealed record MoneyLookups(IReadOnlyList<MoneyAccountOption> Accounts, IReadOnlyList<MoneyPartyOption> Counterparties, IReadOnlyList<Guid> CurrencyIds);
public sealed record MoneyAccountOverview(MoneyAccountOption Account, decimal Balance);
public sealed record MoneyListRow(MoneyDocumentResponse Money, string? SourceAccountName, Guid? SourceCurrencyId,
    string? DestinationAccountName, Guid? DestinationCurrencyId, string? CounterpartyName);
public enum SettlementBalanceState { OWED_TO_US, WE_OWE, ZERO }
public sealed record SettlementOverviewRow(Guid CounterpartyId, string CounterpartyName, Guid CurrencyId, decimal Balance);

// Read ports; all balances come from the existing PostgreSQL views, with no new physical model.
public interface IFinanceQueries
{
    Task<MoneyLookups> LookupsAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> CurrenciesAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<MoneyAccountOverview>> AccountsAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<MoneyListRow>> MoneyListAsync(Guid organizationId, DocumentType type, MoneyListFilter filter, CancellationToken ct);
    Task<IReadOnlyList<SettlementOverviewRow>> SettlementsAsync(Guid organizationId, string? search, SettlementBalanceState? state, Guid? currencyId, CancellationToken ct);
}
