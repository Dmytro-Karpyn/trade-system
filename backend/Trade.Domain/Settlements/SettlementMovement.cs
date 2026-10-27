using System.Numerics;
using Trade.Domain.Common;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Domain.Sales;
using Trade.Domain.Money;
using Trade.Domain.Expenses;
using Trade.Domain.Returns;

namespace Trade.Domain.Settlements;

public static class SettlementCurrencies
{
    // Stable external currency ID; no Currency table or currency bootstrap is introduced.
    public static readonly Guid Uah = Guid.Parse("00000000-0000-0000-0000-000000000980");
}

public sealed class SettlementMovement
{
    private SettlementMovement() { }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public Guid CurrencyId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal BaseAmountUah { get; private set; }

    public static SettlementMovement Purchase(PurchaseInvoice purchase)
    {
        Guard.Against(purchase.Document.DocumentType != DocumentType.PURCHASE_INVOICE
            || purchase.Document.Status != DocumentStatus.POSTED, "A purchase settlement requires a posted PURCHASE_INVOICE.");
        var amount = DocumentAmount.Calculate(purchase.Lines.Where(x => x.RemovedAt is null)
            .Select(x => (x.Quantity, x.UnitPriceCurrency)), purchase.DiscountPercent);
        return Create(purchase.DocumentId, purchase.CounterpartyId!.Value, purchase.CurrencyId,
            -amount, -DocumentAmount.ConvertToUah(amount, purchase.ExchangeRate));
    }

    public static SettlementMovement Sale(SaleInvoice sale)
    {
        Guard.Against(sale.Document.DocumentType != DocumentType.SALE_INVOICE
            || sale.Document.Status != DocumentStatus.POSTED, "A sale settlement requires a posted SALE_INVOICE.");
        var amount = DocumentAmount.Calculate(sale.Lines.Where(x => x.RemovedAt is null)
            .Select(x => (x.Quantity, x.UnitPrice)), sale.DiscountPercent);
        return Create(sale.DocumentId, sale.CounterpartyId, SettlementCurrencies.Uah, amount, amount);
    }

    public static SettlementMovement Money(MoneyDocument money)
    {
        Guard.Against(money.Document.Status != DocumentStatus.POSTED || money.Purpose != MoneyPurpose.SETTLEMENT
            || money.Document.DocumentType is not (DocumentType.MONEY_RECEIPT or DocumentType.MONEY_PAYMENT),
            "Money settlement requires a posted SETTLEMENT receipt or payment.");
        var sign = money.Document.DocumentType == DocumentType.MONEY_RECEIPT ? -1m : 1m;
        var amount = money.SettlementAmount!.Value;
        return Create(money.DocumentId, money.CounterpartyId!.Value, money.SettlementCurrencyId!.Value,
            sign * amount, sign * DocumentAmount.ConvertToUah(amount, money.SettlementExchangeRate!.Value));
    }

    public static SettlementMovement Expense(Expense expense)
    {
        Guard.Against(expense.Document.DocumentType != DocumentType.EXPENSE
            || expense.Document.Status != DocumentStatus.POSTED, "Expense settlement requires a posted EXPENSE.");
        return Create(expense.DocumentId, expense.CounterpartyId, expense.CurrencyId,
            -expense.Amount, -DocumentAmount.ConvertToUah(expense.Amount, expense.ExchangeRate));
    }

    public static SettlementMovement CustomerReturn(CustomerReturn customerReturn, SaleInvoice source)
    {
        Guard.Against(customerReturn.Document.Status != DocumentStatus.POSTED
            || customerReturn.SourceSaleInvoiceId != source.DocumentId, "Return settlement requires a posted return and its source sale.");
        var total = DocumentAmount.Calculate(customerReturn.Lines.Select(x => (1m, x.RefundAmount)), 0m);
        return Create(customerReturn.DocumentId, source.CounterpartyId, SettlementCurrencies.Uah, -total, -total);
    }

    public static SettlementMovement SupplierReturn(SupplierReturn supplierReturn, PurchaseInvoice source)
    {
        Guard.Against(supplierReturn.Document.Status != DocumentStatus.POSTED
            || supplierReturn.SourcePurchaseInvoiceId != source.DocumentId, "Supplier settlement requires a posted return and its source purchase.");
        var originals = source.Lines.ToDictionary(x => x.Id);
        // Refund original purchase value only. Direct Expenses/current batch cost are deliberately not inputs.
        var amount = DocumentAmount.Calculate(supplierReturn.Lines.Select(x =>
            (x.Quantity, originals[x.SourcePurchaseLineId].UnitPriceCurrency)), source.DiscountPercent);
        return Create(supplierReturn.DocumentId, source.CounterpartyId!.Value, source.CurrencyId,
            amount, DocumentAmount.ConvertToUah(amount, source.ExchangeRate));
    }

    private static SettlementMovement Create(Guid documentId, Guid counterpartyId, Guid currencyId, decimal amount, decimal baseAmount)
    {
        Guard.RequiredId(documentId, nameof(DocumentId));
        Guard.RequiredId(counterpartyId, nameof(CounterpartyId));
        Guard.RequiredId(currencyId, nameof(CurrencyId));
        Guard.Numeric(amount, 4, 10_000_000_000_000_000m, nameof(Amount));
        Guard.Numeric(baseAmount, 4, 10_000_000_000_000_000m, nameof(BaseAmountUah));
        return new SettlementMovement { Id = Guid.CreateVersion7(), DocumentId = documentId,
            CounterpartyId = counterpartyId, CurrencyId = currencyId, Amount = amount, BaseAmountUah = baseAmount };
    }
}

internal static class DocumentAmount
{
    // Inputs have at most four decimal places (rate: eight). Integer scaling keeps products/sums exact,
    // even when an intermediate value exceeds decimal's range. Only the final document amount is rounded.
    public static decimal Calculate(IEnumerable<(decimal Quantity, decimal Price)> lines, decimal discount)
    {
        var gross = lines.Aggregate(BigInteger.Zero, (sum, line) =>
            sum + (BigInteger)(line.Quantity * 10_000m) * (BigInteger)(line.Price * 10_000m));
        var discounted = gross * (1_000_000 - (BigInteger)(discount * 10_000m));
        return ToDecimal(RoundDivide(discounted, 10_000_000_000));
    }

    public static decimal ConvertToUah(decimal amount, decimal rate) =>
        ToDecimal(RoundDivide((BigInteger)(amount * 10_000m) * (BigInteger)(rate * 100_000_000m), 100_000_000));

    private static BigInteger RoundDivide(BigInteger value, BigInteger divisor)
    {
        var quotient = BigInteger.DivRem(value, divisor, out var remainder);
        return remainder * 2 >= divisor ? quotient + 1 : quotient;
    }

    private static decimal ToDecimal(BigInteger scaled)
    {
        Guard.Against(scaled < 0 || scaled >= BigInteger.Pow(10, 20), "The document settlement exceeds numeric(20,4).");
        return (decimal)scaled / 10_000m;
    }
}
