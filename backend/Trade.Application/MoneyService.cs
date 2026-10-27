using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Money;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class MoneyService(ITradeStore store)
{
    public Task<MoneyAccountResponse> CreateAccountAsync(Guid organizationId, MoneyAccountRequest request, CancellationToken ct) =>
        store.InTransactionAsync(_ =>
        {
            var account = MoneyAccount.Create(organizationId, request.ToDetails());
            store.AddMoneyAccount(account);
            return Task.FromResult(account.ToResponse());
        }, ct);

    public async Task<IReadOnlyList<MoneyAccountResponse>> AccountsAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListMoneyAccountsAsync(organizationId, ct)).Select(x => x.ToResponse()).ToArray();

    public async Task<MoneyAccountResponse> AccountAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await store.FindMoneyAccountAsync(organizationId, id, false, ct) ?? throw new NotFoundException("MoneyAccount not found.")).ToResponse();

    public Task<MoneyAccountResponse> UpdateAccountAsync(Guid organizationId, Guid id, MoneyAccountRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var account = await store.FindMoneyAccountAsync(organizationId, id, true, token)
                ?? throw new NotFoundException("MoneyAccount not found.");
            var hasReferences = request.CurrencyId != account.CurrencyId && await store.HasMoneyAccountReferencesAsync(id, token);
            account.Update(request.ToDetails(), hasReferences);
            return account.ToResponse();
        }, ct);

    public Task<MoneyDocumentResponse> CreateAsync(Guid organizationId, Guid actorId, DocumentType type,
        MoneyDocumentRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            var accounts = await AccountsAsync(organizationId, request.SourceAccountId, request.DestinationAccountId, token);
            var document = Document.Create(organizationId, type, request.DocumentDate, request.Comment, request.InternalNote,
                actorId, DateTimeOffset.UtcNow);
            var money = MoneyDocument.Create(document, request.ToDetails(), accounts);
            store.AddMoneyDocument(money);
            return money.ToResponse();
        }, ct);

    public async Task<MoneyDocumentResponse> GetAsync(Guid organizationId, Guid id, DocumentType type, CancellationToken ct) =>
        (await FindAsync(organizationId, id, type, false, ct)).ToResponse();

    public Task<MoneyDocumentResponse> UpdateAsync(Guid organizationId, Guid id, DocumentType type,
        UpdateMoneyDocumentRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            var money = await FindAsync(organizationId, id, type, true, token);
            money.Document.RequireDraft();
            if (money.Document.Version != request.Version) throw new ConflictException("Document Version is stale. Reload before editing.");
            await CounterpartyReferences.RequireAsync(store, organizationId, request.CounterpartyId, token);
            var accounts = await AccountsAsync(organizationId, request.SourceAccountId, request.DestinationAccountId, token);
            money.Update(request.ToDetails(), accounts);
            money.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return money.ToResponse();
        }, ct);

    public Task<MoneyDocumentResponse> PostAsync(Guid organizationId, Guid actorId, Guid id, DocumentType type, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var money = await FindAsync(organizationId, id, type, true, token);
            if (money.Document.Status == DocumentStatus.POSTED) return money.ToResponse();
            await CounterpartyReferences.RequireAsync(store, organizationId, money.CounterpartyId, token);
            var accounts = await AccountsAsync(organizationId, money.SourceAccountId, money.DestinationAccountId, token);
            money.ValidateForPost(accounts);
            var prefix = type switch { DocumentType.MONEY_RECEIPT => "MR", DocumentType.MONEY_PAYMENT => "MP", _ => "MT" };
            var number = money.Document.Number ?? $"{prefix}-{await store.NextDocumentSequenceAsync(token):D8}";
            money.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            foreach (var movement in money.CreateMovements()) store.AddMoneyMovement(movement);
            if (money.Purpose == MoneyPurpose.SETTLEMENT) store.AddSettlementMovement(SettlementMovement.Money(money));
            return money.ToResponse();
        }, ct);

    public Task<MoneyDocumentResponse> UnpostAsync(Guid organizationId, Guid id, DocumentType type, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var money = await FindAsync(organizationId, id, type, true, token);
            if (money.Document.Status == DocumentStatus.DRAFT) return money.ToResponse();
            await store.DeleteMoneyMovementsAsync(id, token);
            if (money.Purpose == MoneyPurpose.SETTLEMENT) await store.DeleteSettlementMovementsAsync(id, token);
            money.Document.Unpost(DateTimeOffset.UtcNow);
            return money.ToResponse();
        }, ct);

    public Task<IReadOnlyList<MoneyBalanceResponse>> BalancesAsync(Guid organizationId, CancellationToken ct) =>
        store.GetMoneyBalancesAsync(organizationId, ct);

    private async Task<MoneyDocument> FindAsync(Guid organizationId, Guid id, DocumentType type, bool forUpdate, CancellationToken ct) =>
        await store.FindMoneyDocumentAsync(organizationId, id, type, forUpdate, ct) ?? throw new NotFoundException("Money document not found.");

    private async Task<Dictionary<Guid, MoneyAccount>> AccountsAsync(Guid organizationId, Guid? sourceId, Guid? destinationId, CancellationToken ct)
    {
        var ids = new[] { sourceId, destinationId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().Order().ToArray();
        var accounts = await store.LockMoneyAccountsAsync(organizationId, ids, ct);
        Guard.Against(accounts.Count != ids.Length, "All MoneyAccounts must belong to the current organization.");
        return accounts.ToDictionary(x => x.Id);
    }
}
