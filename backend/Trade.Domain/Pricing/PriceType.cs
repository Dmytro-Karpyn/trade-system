using Trade.Domain.Common;

namespace Trade.Domain.Pricing;

public sealed class PriceType
{
    private PriceType() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal DefaultMarkupPercent { get; private set; }
    public bool Active { get; private set; }
    public int SortOrder { get; private set; }

    public static PriceType Create(Guid organizationId, string name, decimal defaultMarkupPercent = 0m,
        bool active = true, int sortOrder = 0)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var type = new PriceType { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        type.Update(name, defaultMarkupPercent, active, sortOrder);
        return type;
    }

    public void Update(string name, decimal defaultMarkupPercent, bool active, int sortOrder)
    {
        Guard.Against(string.IsNullOrWhiteSpace(name), "PriceType Name is required.");
        Guard.Against(defaultMarkupPercent < 0, "DefaultMarkupPercent must be nonnegative.");
        Guard.Numeric(defaultMarkupPercent, 4, 1000m, nameof(DefaultMarkupPercent));
        Name = name.Trim();
        DefaultMarkupPercent = defaultMarkupPercent;
        Active = active;
        SortOrder = sortOrder;
    }
}
