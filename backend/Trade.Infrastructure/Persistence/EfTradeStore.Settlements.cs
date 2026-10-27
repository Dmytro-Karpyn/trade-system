using System.Data;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Counterparties;
using Trade.Domain.Documents;
using Trade.Domain.Settlements;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore
{
    public async Task<Counterparty?> FindCounterpartyAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        var query = forUpdate ? db.Counterparties.FromSql($"""
            SELECT * FROM counterparties WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR UPDATE
            """) : db.Counterparties.FromSql($"""
            SELECT * FROM counterparties WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR SHARE
            """);
        return (await query.ToListAsync(ct)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<Counterparty>> ListCounterpartiesAsync(Guid organizationId, CounterpartyFilter filter, CancellationToken ct)
    {
        var query = db.Counterparties.AsNoTracking().Where(x => x.OrganizationId == organizationId
            && (filter.IsCustomer == null || x.IsCustomer == filter.IsCustomer)
            && (filter.IsSupplier == null || x.IsSupplier == filter.IsSupplier)
            && (filter.IsEmployee == null || x.IsEmployee == filter.IsEmployee)
            && (filter.Active == null || x.Active == filter.Active)
            && (filter.IsSystem == null || x.IsSystem == filter.IsSystem));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\")
                || x.Phone != null && EF.Functions.ILike(x.Phone, pattern, "\\")
                || x.Email != null && EF.Functions.ILike(x.Email, pattern, "\\")
                || x.TaxId != null && EF.Functions.ILike(x.TaxId, pattern, "\\")
                || x.RegistrationCode != null && EF.Functions.ILike(x.RegistrationCode, pattern, "\\"));
        }
        return await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
    }

    public async Task DeleteSettlementMovementsAsync(Guid documentId, CancellationToken ct) =>
        await db.SettlementMovements.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<SettlementBalanceResponse>> GetSettlementBalancesAsync(Guid organizationId, Guid? counterpartyId,
        Guid? currencyId, CancellationToken ct) =>
        await (from balance in db.CounterpartyBalances
               join party in db.Counterparties on balance.CounterpartyId equals party.Id
               where party.OrganizationId == organizationId && (counterpartyId == null || balance.CounterpartyId == counterpartyId)
                   && (currencyId == null || balance.CurrencyId == currencyId)
               orderby balance.CounterpartyId, balance.CurrencyId
               select new SettlementBalanceResponse(balance.CounterpartyId, balance.CurrencyId, balance.Balance)).ToListAsync(ct);

    public async Task<CounterpartySettlementResponse> GetCounterpartySettlementAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        // Both reads use one snapshot so concurrent POST/UNPOST cannot make totals disagree with the history.
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        _ = await FindCounterpartyAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Counterparty not found.");
        var balances = await GetSettlementBalancesAsync(organizationId, id, null, ct);
        var rows = await db.Database.SqlQuery<SettlementHistoryRow>($"""
            SELECT m."Id", m."DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate", m."CounterpartyId", m."CurrencyId",
                m."Amount", m."BaseAmountUah",
                SUM(m."Amount") OVER (PARTITION BY m."CurrencyId"
                    ORDER BY d."DocumentDate", d."CreatedAt", d."Id", m."Id"
                    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS "BalanceAfterMovement"
            FROM settlement_movements m
            JOIN documents d ON d."Id" = m."DocumentId"
            JOIN counterparties c ON c."Id" = m."CounterpartyId"
            WHERE m."CounterpartyId" = {id} AND c."OrganizationId" = {organizationId} AND d."OrganizationId" = {organizationId}
            ORDER BY d."DocumentDate", d."CreatedAt", d."Id", m."Id"
            """).ToListAsync(ct);
        await snapshot.CommitAsync(ct);
        return new(id, balances, rows.Select(x => new SettlementHistoryResponse(x.Id, x.DocumentId,
            Enum.Parse<DocumentType>(x.DocumentType), x.DocumentDate, x.CounterpartyId, x.CurrencyId,
            x.Amount, x.BaseAmountUah, x.BalanceAfterMovement, x.DocumentNumber)).ToArray());
    }

    public void AddCounterparty(Counterparty counterparty) => db.Counterparties.Add(counterparty);
    public void AddSettlementMovement(SettlementMovement movement) => db.SettlementMovements.Add(movement);
}

internal sealed class SettlementHistoryRow
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string DocumentType { get; set; } = null!;
    public DateOnly DocumentDate { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid CurrencyId { get; set; }
    public decimal Amount { get; set; }
    public decimal BaseAmountUah { get; set; }
    public decimal BalanceAfterMovement { get; set; }
}
