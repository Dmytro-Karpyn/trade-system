using Trade.Domain.Money;

namespace Trade.Application;

public sealed record MoneyAccountRequest(string Name, MoneyAccountType AccountType, Guid CurrencyId, bool Active = true)
{
    public MoneyAccountDetails ToDetails() => new(Name, AccountType, CurrencyId, Active);
}
public sealed record MoneyAccountResponse(Guid Id, Guid OrganizationId, string Name, MoneyAccountType AccountType, Guid CurrencyId, bool Active);

public sealed record MoneyDocumentRequest(DateOnly DocumentDate, string? Comment = null, string? InternalNote = null,
    MoneyPurpose? Purpose = null, Guid? CounterpartyId = null, Guid? SourceAccountId = null, Guid? DestinationAccountId = null,
    decimal? SourceAmount = null, decimal? DestinationAmount = null, Guid? SettlementCurrencyId = null,
    decimal? SettlementAmount = null, decimal? SettlementExchangeRate = null)
{
    public MoneyDocumentDetails ToDetails() => new(Purpose, CounterpartyId, SourceAccountId, DestinationAccountId,
        SourceAmount, DestinationAmount, SettlementCurrencyId, SettlementAmount, SettlementExchangeRate);
}

public sealed record UpdateMoneyDocumentRequest(long Version, DateOnly DocumentDate, string? Comment = null, string? InternalNote = null,
    MoneyPurpose? Purpose = null, Guid? CounterpartyId = null, Guid? SourceAccountId = null, Guid? DestinationAccountId = null,
    decimal? SourceAmount = null, decimal? DestinationAmount = null, Guid? SettlementCurrencyId = null,
    decimal? SettlementAmount = null, decimal? SettlementExchangeRate = null)
{
    public MoneyDocumentDetails ToDetails() => new(Purpose, CounterpartyId, SourceAccountId, DestinationAccountId,
        SourceAmount, DestinationAmount, SettlementCurrencyId, SettlementAmount, SettlementExchangeRate);
}

public sealed record MoneyDocumentResponse(DocumentResponse Document, MoneyPurpose? Purpose, Guid? CounterpartyId,
    Guid? SourceAccountId, Guid? DestinationAccountId, decimal? SourceAmount, decimal? DestinationAmount,
    Guid? SettlementCurrencyId, decimal? SettlementAmount, decimal? SettlementExchangeRate);
public sealed record MoneyBalanceResponse(Guid MoneyAccountId, Guid CurrencyId, decimal Balance);

internal static class MoneyResponses
{
    public static MoneyAccountResponse ToResponse(this MoneyAccount x) => new(x.Id, x.OrganizationId, x.Name, x.AccountType, x.CurrencyId, x.Active);

    public static MoneyDocumentResponse ToResponse(this MoneyDocument x)
    {
        var d = x.Document;
        return new(new(d.Id, d.OrganizationId, d.DocumentType, d.Number, d.DocumentDate, d.Status, d.Comment,
            d.InternalNote, d.CreatedBy, d.CreatedAt, d.UpdatedAt, d.PostedBy, d.PostedAt, d.DeletedBy, d.DeletedAt, d.Version),
            x.Purpose, x.CounterpartyId, x.SourceAccountId, x.DestinationAccountId, x.SourceAmount, x.DestinationAmount,
            x.SettlementCurrencyId, x.SettlementAmount, x.SettlementExchangeRate);
    }
}
