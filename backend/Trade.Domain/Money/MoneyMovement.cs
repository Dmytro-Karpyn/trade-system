using Trade.Domain.Common;

namespace Trade.Domain.Money;

public sealed class MoneyMovement
{
    private MoneyMovement() { }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid MoneyAccountId { get; private set; }
    public decimal Amount { get; private set; }

    internal static MoneyMovement Create(Guid documentId, Guid accountId, decimal amount)
    {
        Guard.RequiredId(documentId, nameof(DocumentId));
        Guard.RequiredId(accountId, nameof(MoneyAccountId));
        Guard.Against(amount == 0, "MoneyMovement Amount must be nonzero.");
        Guard.Numeric(amount, 4, 10_000_000_000_000_000m, nameof(Amount));
        return new MoneyMovement { Id = Guid.CreateVersion7(), DocumentId = documentId, MoneyAccountId = accountId, Amount = amount };
    }
}
