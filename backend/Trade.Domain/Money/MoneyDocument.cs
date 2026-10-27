using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Settlements;

namespace Trade.Domain.Money;

public enum MoneyPurpose { SETTLEMENT, OWNER, OPENING_BALANCE }
public sealed record MoneyDocumentDetails(MoneyPurpose? Purpose, Guid? CounterpartyId, Guid? SourceAccountId,
    Guid? DestinationAccountId, decimal? SourceAmount, decimal? DestinationAmount, Guid? SettlementCurrencyId,
    decimal? SettlementAmount, decimal? SettlementExchangeRate);

public sealed class MoneyDocument
{
    private MoneyDocument() { }

    public Guid DocumentId { get; private set; }
    public Document Document { get; private set; } = null!;
    public MoneyPurpose? Purpose { get; private set; }
    public Guid? CounterpartyId { get; private set; }
    public Guid? SourceAccountId { get; private set; }
    public Guid? DestinationAccountId { get; private set; }
    public decimal? SourceAmount { get; private set; }
    public decimal? DestinationAmount { get; private set; }
    public Guid? SettlementCurrencyId { get; private set; }
    public decimal? SettlementAmount { get; private set; }
    public decimal? SettlementExchangeRate { get; private set; }

    public static bool IsMoneyType(DocumentType type) => type is DocumentType.MONEY_RECEIPT
        or DocumentType.MONEY_PAYMENT or DocumentType.MONEY_TRANSFER;

    public static MoneyDocument Create(Document document, MoneyDocumentDetails details, IReadOnlyDictionary<Guid, MoneyAccount> accounts)
    {
        Guard.Against(!IsMoneyType(document.DocumentType), "A money document requires a money DocumentType.");
        var money = new MoneyDocument { DocumentId = document.Id, Document = document };
        money.Update(details, accounts);
        return money;
    }

    public void Update(MoneyDocumentDetails details, IReadOnlyDictionary<Guid, MoneyAccount> accounts)
    {
        Document.RequireDraft();
        Purpose = details.Purpose;
        CounterpartyId = details.CounterpartyId;
        SourceAccountId = details.SourceAccountId;
        DestinationAccountId = details.DestinationAccountId;
        SourceAmount = details.SourceAmount;
        DestinationAmount = details.DestinationAmount;
        SettlementCurrencyId = details.SettlementCurrencyId;
        SettlementAmount = details.SettlementAmount;
        SettlementExchangeRate = details.SettlementExchangeRate;
        // UAH has no conversion. Omission means 1; an explicitly contradictory rate is rejected below.
        if (Purpose == MoneyPurpose.SETTLEMENT && SettlementCurrencyId == SettlementCurrencies.Uah && SettlementExchangeRate is null)
            SettlementExchangeRate = 1m;
        ValidateForPost(accounts);
    }

    public void ValidateForPost(IReadOnlyDictionary<Guid, MoneyAccount> accounts)
    {
        Document.RequireDraft();
        Guard.Against(Purpose is { } purpose && !Enum.IsDefined(purpose), "Unsupported Purpose.");
        Guard.Against(CounterpartyId == Guid.Empty, "CounterpartyId must be null or a nonempty ID.");
        var type = Document.DocumentType;
        Guard.Against(!IsMoneyType(type), "Unsupported money DocumentType.");
        if (type == DocumentType.MONEY_RECEIPT)
        {
            Guard.Against(SourceAccountId is not null || SourceAmount is not null, "Receipt must not specify source account or amount.");
            RequireAccount(DestinationAccountId, accounts);
            RequireAmount(DestinationAmount, nameof(DestinationAmount));
        }
        else if (type == DocumentType.MONEY_PAYMENT)
        {
            Guard.Against(DestinationAccountId is not null || DestinationAmount is not null, "Payment must not specify destination account or amount.");
            RequireAccount(SourceAccountId, accounts);
            RequireAmount(SourceAmount, nameof(SourceAmount));
        }
        else
        {
            RequireAccount(SourceAccountId, accounts);
            RequireAccount(DestinationAccountId, accounts);
            RequireAmount(SourceAmount, nameof(SourceAmount));
            RequireAmount(DestinationAmount, nameof(DestinationAmount));
            Guard.Against(SourceAccountId == DestinationAccountId, "Transfer requires two different accounts.");
            Guard.Against(accounts[SourceAccountId!.Value].CurrencyId == accounts[DestinationAccountId!.Value].CurrencyId
                && SourceAmount != DestinationAmount, "Same-currency transfer requires equal amounts.");
            Guard.Against(Purpose == MoneyPurpose.SETTLEMENT, "Transfer cannot have SETTLEMENT purpose.");
        }
        if (Purpose == MoneyPurpose.SETTLEMENT)
        {
            Guard.Against(CounterpartyId is null, "SETTLEMENT requires CounterpartyId.");
            Guard.Against(SettlementCurrencyId is null || SettlementCurrencyId == Guid.Empty,
                "SETTLEMENT requires a nonempty SettlementCurrencyId.");
            RequireAmount(SettlementAmount, nameof(SettlementAmount));
            Guard.Against(SettlementExchangeRate is null or <= 0, "SETTLEMENT requires positive SettlementExchangeRate.");
            Guard.Numeric(SettlementExchangeRate!.Value, 8, 1_000_000_000_000m, nameof(SettlementExchangeRate));
            Guard.Against(SettlementCurrencyId == SettlementCurrencies.Uah && SettlementExchangeRate != 1m,
                "UAH SettlementExchangeRate must be 1.");
        }
        else Guard.Against(SettlementCurrencyId is not null || SettlementAmount is not null || SettlementExchangeRate is not null,
            "Settlement fields must be null unless Purpose is SETTLEMENT.");
    }

    public IReadOnlyList<MoneyMovement> CreateMovements()
    {
        Guard.Against(Document.Status != DocumentStatus.POSTED, "Money movements require a POSTED document.");
        var movements = new List<MoneyMovement>();
        if (SourceAccountId is { } source) movements.Add(MoneyMovement.Create(DocumentId, source, -SourceAmount!.Value));
        if (DestinationAccountId is { } destination) movements.Add(MoneyMovement.Create(DocumentId, destination, DestinationAmount!.Value));
        return movements;
    }

    private void RequireAccount(Guid? id, IReadOnlyDictionary<Guid, MoneyAccount> accounts) =>
        Guard.Against(id is null || id == Guid.Empty || !accounts.TryGetValue(id.Value, out var account)
            || account.OrganizationId != Document.OrganizationId, "Account must exist in the document organization.");

    private static void RequireAmount(decimal? amount, string name)
    {
        Guard.Against(amount is null or <= 0, $"{name} must be positive.");
        Guard.Numeric(amount!.Value, 4, 10_000_000_000_000_000m, name);
    }
}
