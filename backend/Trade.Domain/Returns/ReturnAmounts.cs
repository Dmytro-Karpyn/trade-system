using Trade.Domain.Purchasing;
using Trade.Domain.Settlements;

namespace Trade.Domain.Returns;

// Read-only previews use the same exact, four-decimal document arithmetic as settlement posting.
public static class ReturnAmounts
{
    public static decimal Supplier(PurchaseInvoice source, IEnumerable<(Guid SourceLineId, decimal Quantity)> lines)
    {
        var originals = source.Lines.ToDictionary(x => x.Id);
        return DocumentAmount.Calculate(lines.Select(x => (x.Quantity, originals[x.SourceLineId].UnitPriceCurrency)), source.DiscountPercent);
    }
    public static decimal Customer(IEnumerable<decimal> refunds) => DocumentAmount.Calculate(refunds.Select(x => (1m, x)), 0m);
}
