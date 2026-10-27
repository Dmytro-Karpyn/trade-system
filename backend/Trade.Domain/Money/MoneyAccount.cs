using Trade.Domain.Common;

namespace Trade.Domain.Money;

public enum MoneyAccountType { CASH, BANK, SAFE, OTHER }
public sealed record MoneyAccountDetails(string Name, MoneyAccountType AccountType, Guid CurrencyId, bool Active);

public sealed class MoneyAccount
{
    private MoneyAccount() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public MoneyAccountType AccountType { get; private set; }
    public Guid CurrencyId { get; private set; }
    public bool Active { get; private set; }

    public static MoneyAccount Create(Guid organizationId, MoneyAccountDetails details)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var account = new MoneyAccount { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        account.Update(details, false);
        return account;
    }

    public void Update(MoneyAccountDetails details, bool hasReferences)
    {
        Guard.Against(string.IsNullOrWhiteSpace(details.Name), "MoneyAccount Name is required.");
        Guard.Against(!Enum.IsDefined(details.AccountType), "Unsupported AccountType.");
        Guard.RequiredId(details.CurrencyId, nameof(CurrencyId));
        Guard.Against(hasReferences && CurrencyId != details.CurrencyId,
            "CurrencyId cannot change after a MoneyAccount is used by a document or movement.");
        Name = details.Name.Trim();
        AccountType = details.AccountType;
        CurrencyId = details.CurrencyId;
        Active = details.Active;
    }
}
