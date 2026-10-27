using Trade.Domain.Catalog;
using Trade.Domain.Common;

namespace Trade.Domain.Pricing;

public sealed class ProductPrice
{
    private ProductPrice() { }

    public Guid ProductId { get; private set; }
    public Guid PriceTypeId { get; private set; }
    public decimal UnitPrice { get; private set; }

    public static ProductPrice Create(Product product, PriceType priceType, decimal unitPrice)
    {
        Guard.Against(product.ItemType != ItemType.SERVICE, "ProductPrice is only allowed for SERVICE.");
        Guard.Against(product.OrganizationId != priceType.OrganizationId,
            "Product and PriceType must belong to the same organization.");
        var price = new ProductPrice { ProductId = product.Id, PriceTypeId = priceType.Id };
        price.Change(unitPrice);
        return price;
    }

    public void Change(decimal unitPrice)
    {
        PriceRules.Validate(unitPrice);
        UnitPrice = unitPrice;
    }
}
