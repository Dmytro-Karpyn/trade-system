using Trade.Domain.Common;

namespace Trade.Domain.Expenses;

public sealed record ExpenseCategoryDetails(Guid? ParentId, string Name, bool Active);

public sealed class ExpenseCategory
{
    private ExpenseCategory() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool Active { get; private set; }

    public static ExpenseCategory Create(Guid organizationId, ExpenseCategoryDetails details)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var category = new ExpenseCategory { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        category.Update(details);
        return category;
    }

    public void Update(ExpenseCategoryDetails details)
    {
        Guard.Against(string.IsNullOrWhiteSpace(details.Name), "ExpenseCategory.Name is required.");
        Guard.Against(details.ParentId == Guid.Empty || details.ParentId == Id, "Invalid parent category.");
        ParentId = details.ParentId;
        Name = details.Name.Trim();
        Active = details.Active;
    }
}
