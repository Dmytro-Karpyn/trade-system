using Trade.Domain.Common;

namespace Trade.Domain.Catalog;

public sealed record ProductCategoryDetails(Guid? ParentId,string Name,string? Description,int SortOrder,bool Active);
public sealed class ProductCategory
{
    private ProductCategory() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool Active { get; private set; }
    public static ProductCategory Create(Guid org,ProductCategoryDetails input)
    {
        Guard.RequiredId(org,nameof(OrganizationId));var result=new ProductCategory { Id=Guid.CreateVersion7(),OrganizationId=org };result.Update(input);return result;
    }
    public void Update(ProductCategoryDetails input)
    {
        Guard.Against(string.IsNullOrWhiteSpace(input.Name),"Category Name is required.");
        Guard.Against(input.ParentId==Guid.Empty || input.ParentId==Id,"CATEGORY_CYCLE");
        ParentId=input.ParentId;Name=input.Name.Trim();Description=input.Description;SortOrder=input.SortOrder;Active=input.Active;
    }
}
