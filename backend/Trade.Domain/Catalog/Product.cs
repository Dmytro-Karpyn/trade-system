using System.Text.Json;
using Trade.Domain.Common;

namespace Trade.Domain.Catalog;

public enum ItemType { GOODS, SERVICE }

public sealed record ProductDetails(Guid? CategoryId, Guid UnitId, ItemType ItemType, string Code,
    string? Barcode, string Name, string? Description, string? Attributes, bool Active);

public sealed class Product
{
    private Product() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid UnitId { get; private set; }
    public ItemType ItemType { get; private set; }
    public string Code { get; private set; } = null!;
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Attributes { get; private set; }
    public bool Active { get; private set; }

    public static Product Create(Guid organizationId, ProductDetails details)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var product = new Product { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        product.Update(details, false);
        return product;
    }

    public void Update(ProductDetails details, bool hasPurchaseLines, bool hasSaleLines = false, bool hasProductPrices = false)
    {
        Guard.RequiredId(details.UnitId, nameof(UnitId));
        Guard.Against(details.CategoryId == Guid.Empty, "CategoryId must be null or a nonempty ID.");
        Guard.Against(!Enum.IsDefined(details.ItemType), "Unsupported ItemType.");
        Guard.Against(string.IsNullOrWhiteSpace(details.Code), "Code is required.");
        Guard.Against(string.IsNullOrWhiteSpace(details.Name), "Name is required.");
        Guard.Against(details.ItemType == ItemType.SERVICE && hasPurchaseLines,
            "A product referenced by purchase lines cannot become a SERVICE.");
        Guard.Against(details.ItemType != ItemType && hasSaleLines,
            "ItemType cannot change while a product is referenced by sale lines.");
        Guard.Against(details.ItemType != ItemType && hasProductPrices,
            "ItemType cannot change while a product has ProductPrice entries.");
        if (details.Attributes is not null)
        {
            try { using var json = JsonDocument.Parse(details.Attributes); }
            catch (JsonException) { throw new DomainException("Attributes must be valid JSON."); }
        }

        CategoryId = details.CategoryId;
        UnitId = details.UnitId;
        ItemType = details.ItemType;
        Code = details.Code.Trim();
        Barcode = details.Barcode;
        Name = details.Name.Trim();
        Description = details.Description;
        Attributes = details.Attributes;
        Active = details.Active;
    }
}
