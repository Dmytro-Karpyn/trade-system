using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Expenses;
using Trade.Domain.Settlements;

namespace Trade.Application;

public sealed class ExpenseService(ITradeStore store)
{
    public Task<ExpenseCategoryResponse> CreateCategoryAsync(Guid organizationId, ExpenseCategoryRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await store.LockExpenseCategoryTreeAsync(organizationId, token);
            var category = ExpenseCategory.Create(organizationId, request.ToDetails());
            await ValidateParentAsync(organizationId, category.Id, request.ParentId, token);
            store.AddExpenseCategory(category);
            return category.ToResponse();
        }, ct);

    public async Task<IReadOnlyList<ExpenseCategoryResponse>> CategoriesAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListExpenseCategoriesAsync(organizationId, ct)).Select(x => x.ToResponse()).ToArray();

    public async Task<ExpenseCategoryResponse> CategoryAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindCategoryAsync(organizationId, id, ct)).ToResponse();

    public Task<ExpenseCategoryResponse> UpdateCategoryAsync(Guid organizationId, Guid id, ExpenseCategoryRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            // Serialize reparenting within an organization, including concurrent edits of different nodes.
            await store.LockExpenseCategoryTreeAsync(organizationId, token);
            var category = await FindCategoryAsync(organizationId, id, token);
            await ValidateParentAsync(organizationId, id, request.ParentId, token);
            category.Update(request.ToDetails());
            return category.ToResponse();
        }, ct);

    public Task<ExpenseResponse> CreateAsync(Guid organizationId, Guid actorId, ExpenseRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await ValidateReferencesAsync(organizationId, request.ToDetails(), token);
            var document = Document.Create(organizationId, DocumentType.EXPENSE, request.DocumentDate,
                request.Comment, request.InternalNote, actorId, DateTimeOffset.UtcNow);
            var expense = Expense.Create(document, request.ToDetails());
            store.AddExpense(expense);
            return expense.ToResponse();
        }, ct);

    public async Task<IReadOnlyList<ExpenseResponse>> ListAsync(Guid organizationId, ExpenseFilter filter, CancellationToken ct)
    {
        Guard.Against(filter.From > filter.To, "from must not be after to.");
        Guard.Against(filter.Status is { } status && !Enum.IsDefined(status), "Unsupported status.");
        return (await store.ListExpensesAsync(organizationId, filter, ct)).Select(x => x.ToResponse()).ToArray();
    }

    public async Task<ExpenseResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await FindAsync(organizationId, id, false, ct)).ToResponse();

    public Task<ExpenseResponse> UpdateAsync(Guid organizationId, Guid id, UpdateExpenseRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var expense = await FindAsync(organizationId, id, true, token);
            expense.Document.RequireDraft();
            if (expense.Document.Version != request.Version)
                throw new ConflictException("Document Version is stale. Reload before editing.");
            await ValidateReferencesAsync(organizationId, request.ToDetails(), token);
            expense.Update(request.ToDetails());
            expense.Document.Edit(request.DocumentDate, request.Comment, request.InternalNote, DateTimeOffset.UtcNow);
            return expense.ToResponse();
        }, ct);

    public Task<ExpenseResponse> PostAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var expense = await FindAsync(organizationId, id, true, token);
            if (expense.Document.Status == DocumentStatus.POSTED) return expense.ToResponse();
            expense.Document.RequireDraft();
            await ValidateReferencesAsync(organizationId, Details(expense), token);
            var number = expense.Document.Number ?? $"EX-{await store.NextDocumentSequenceAsync(token):D8}";
            expense.Document.Post(number, actorId, DateTimeOffset.UtcNow);
            store.AddSettlementMovement(SettlementMovement.Expense(expense));
            return expense.ToResponse();
        }, ct);

    public Task<ExpenseResponse> UnpostAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var expense = await FindAsync(organizationId, id, true, token);
            if (expense.Document.Status == DocumentStatus.DRAFT) return expense.ToResponse();
            await store.DeleteSettlementMovementsAsync(id, token);
            expense.Document.Unpost(DateTimeOffset.UtcNow);
            return expense.ToResponse();
        }, ct);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, Guid actorId, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var expense = await FindAsync(organizationId, id, true, token);
            expense.Document.Delete(actorId, DateTimeOffset.UtcNow);
            return true;
        }, ct);

    private async Task ValidateReferencesAsync(Guid organizationId, ExpenseDetails details, CancellationToken ct)
    {
        Guard.RequiredId(details.CounterpartyId, nameof(details.CounterpartyId));
        Guard.RequiredId(details.ExpenseCategoryId, nameof(details.ExpenseCategoryId));
        await CounterpartyReferences.RequireAsync(store, organizationId, details.CounterpartyId, ct);
        Guard.Against(await store.FindExpenseCategoryAsync(organizationId, details.ExpenseCategoryId, ct) is null,
            "ExpenseCategory must exist in the current organization.");
        if (details.PurchaseInvoiceId is not { } purchaseId) return;
        // Holds a shared parent Document lock until commit; purchase UNPOST holds its exclusive lock.
        var purchase = await store.LockExpensePurchaseAsync(organizationId, purchaseId, ct);
        Guard.Against(purchase is null, "PurchaseInvoice must exist in the current organization.");
        Expense.RequireAllocatablePurchase(purchase!);
    }

    private async Task ValidateParentAsync(Guid organizationId, Guid id, Guid? parentId, CancellationToken ct)
    {
        if (parentId is null) return;
        var tree = (await store.ListExpenseCategoriesAsync(organizationId, ct)).ToDictionary(x => x.Id);
        var visited = new HashSet<Guid> { id };
        while (parentId is { } parent)
        {
            Guard.Against(!visited.Add(parent), "ExpenseCategory tree cannot contain cycles.");
            Guard.Against(!tree.TryGetValue(parent, out var category), "Parent category must belong to the current organization.");
            parentId = category!.ParentId;
        }
    }

    private async Task<ExpenseCategory> FindCategoryAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        await store.FindExpenseCategoryAsync(organizationId, id, ct) ?? throw new NotFoundException("ExpenseCategory not found.");

    private async Task<Expense> FindAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindExpenseAsync(organizationId, id, forUpdate, ct) ?? throw new NotFoundException("Expense not found.");

    private static ExpenseDetails Details(Expense expense) => new(expense.CounterpartyId, expense.ExpenseCategoryId,
        expense.CurrencyId, expense.Amount, expense.ExchangeRate, expense.PurchaseInvoiceId);
}
