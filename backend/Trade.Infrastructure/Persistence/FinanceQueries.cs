using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

internal sealed class FinanceQueries(TradeDbContext db) : IFinanceQueries
{
    private Task<List<MoneyAccountOption>> AccountOptions(Guid organizationId, CancellationToken ct) =>
        db.MoneyAccounts.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new MoneyAccountOption(x.Id, x.Name, x.AccountType, x.CurrencyId, x.Active,
                db.MoneyDocuments.Any(d => d.SourceAccountId == x.Id || d.DestinationAccountId == x.Id)
                    || db.MoneyMovements.Any(m => m.MoneyAccountId == x.Id))).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> CurrenciesAsync(Guid organizationId, CancellationToken ct) =>
        (await db.MoneyAccounts.Where(x => x.OrganizationId == organizationId).Select(x => x.CurrencyId)
            .Union(db.PurchaseInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.CurrencyId))
            .Union(db.Expenses.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.CurrencyId))
            .Union(db.SettlementMovements.Where(x => db.Documents.Any(d => d.Id == x.DocumentId && d.OrganizationId == organizationId)).Select(x => x.CurrencyId))
            .Union(db.MoneyDocuments.Where(x => x.Document.OrganizationId == organizationId && x.SettlementCurrencyId != null).Select(x => x.SettlementCurrencyId!.Value))
            .ToListAsync(ct)).Append(Trade.Domain.Settlements.SettlementCurrencies.Uah).Distinct().Order().ToArray();

    public async Task<MoneyLookups> LookupsAsync(Guid organizationId, CancellationToken ct) =>
        new(await AccountOptions(organizationId, ct), await db.Counterparties.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new MoneyPartyOption(x.Id, x.Name, x.Active)).ToListAsync(ct),
            await CurrenciesAsync(organizationId, ct));

    public async Task<IReadOnlyList<MoneyAccountOverview>> AccountsAsync(Guid organizationId, CancellationToken ct)
    {
        var accounts = await AccountOptions(organizationId, ct);
        var balances = await (from balance in db.MoneyAccountBalances join account in db.MoneyAccounts on balance.MoneyAccountId equals account.Id
            where account.OrganizationId == organizationId select balance).ToDictionaryAsync(x => x.MoneyAccountId, x => x.Balance, ct);
        // SUM over an account with no movements is zero. Never add a persisted Balance property.
        return accounts.Select(account => new MoneyAccountOverview(account, balances.GetValueOrDefault(account.Id))).ToArray();
    }

    public async Task<IReadOnlyList<MoneyListRow>> MoneyListAsync(Guid organizationId, DocumentType type, MoneyListFilter filter, CancellationToken ct)
    {
        var query = db.MoneyDocuments.AsNoTracking().Include(x => x.Document)
            .Where(x => x.Document.OrganizationId == organizationId && x.Document.DocumentType == type && x.Document.DeletedAt == null);
        if (filter.From is { } from) query = query.Where(x => x.Document.DocumentDate >= from);
        if (filter.To is { } to) query = query.Where(x => x.Document.DocumentDate <= to);
        if (filter.AccountId is { } account) query = query.Where(x => x.SourceAccountId == account || x.DestinationAccountId == account);
        if (filter.CounterpartyId is { } party) query = query.Where(x => x.CounterpartyId == party);
        if (filter.Purpose is { } purpose) query = query.Where(x => x.Purpose == purpose);
        if (filter.Status is { } status) query = query.Where(x => x.Document.Status == status);
        var docs = await query.OrderByDescending(x => x.Document.DocumentDate).ThenByDescending(x => x.Document.CreatedAt).ThenBy(x => x.DocumentId).ToListAsync(ct);
        var accounts = await db.MoneyAccounts.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, ct);
        var parties = await db.Counterparties.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return docs.Select(x => new MoneyListRow(x.ToResponse(), x.SourceAccountId is { } s ? accounts.GetValueOrDefault(s)?.Name : null,
            x.SourceAccountId is { } sc ? accounts.GetValueOrDefault(sc)?.CurrencyId : null,
            x.DestinationAccountId is { } d ? accounts.GetValueOrDefault(d)?.Name : null,
            x.DestinationAccountId is { } dc ? accounts.GetValueOrDefault(dc)?.CurrencyId : null,
            x.CounterpartyId is { } p ? parties.GetValueOrDefault(p) : null)).ToArray();
    }

    public async Task<IReadOnlyList<SettlementOverviewRow>> SettlementsAsync(Guid organizationId, string? search, SettlementBalanceState? state, Guid? currencyId, CancellationToken ct)
    {
        var query = from balance in db.CounterpartyBalances join party in db.Counterparties on balance.CounterpartyId equals party.Id
                    where party.OrganizationId == organizationId select new { party, balance };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.party.Name, pattern, "\\"));
        }
        if (currencyId is { } currency) query = query.Where(x => x.balance.CurrencyId == currency);
        if (state == SettlementBalanceState.OWED_TO_US) query = query.Where(x => x.balance.Balance > 0);
        if (state == SettlementBalanceState.WE_OWE) query = query.Where(x => x.balance.Balance < 0);
        if (state == SettlementBalanceState.ZERO) query = query.Where(x => x.balance.Balance == 0);
        return await query.OrderBy(x => x.party.Name).ThenBy(x => x.party.Id).ThenBy(x => x.balance.CurrencyId)
            .Select(x => new SettlementOverviewRow(x.party.Id, x.party.Name, x.balance.CurrencyId, x.balance.Balance)).ToListAsync(ct);
    }
}
