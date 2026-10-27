using Trade.Domain.Common;

namespace Trade.Domain.Counterparties;

public enum LegalType { PERSON, ENTREPRENEUR, COMPANY }

public sealed record CounterpartyDetails(string Name, LegalType? LegalType, string? TaxId, string? RegistrationCode,
    string? Phone, string? Email, string? Address, Guid? DefaultPriceTypeId, bool IsCustomer, bool IsSupplier,
    bool IsEmployee, bool IsSystem, bool Active, string? Note);

public sealed class Counterparty
{
    private Counterparty() { }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public LegalType? LegalType { get; private set; }
    public string? TaxId { get; private set; }
    public string? RegistrationCode { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public Guid? DefaultPriceTypeId { get; private set; }
    public bool IsCustomer { get; private set; }
    public bool IsSupplier { get; private set; }
    public bool IsEmployee { get; private set; }
    public bool IsSystem { get; private set; }
    public bool Active { get; private set; }
    public string? Note { get; private set; }

    public static Counterparty Create(Guid organizationId, CounterpartyDetails details)
    {
        Guard.RequiredId(organizationId, nameof(OrganizationId));
        var party = new Counterparty { Id = Guid.CreateVersion7(), OrganizationId = organizationId };
        party.Update(details);
        return party;
    }

    public void Update(CounterpartyDetails details)
    {
        Guard.Against(string.IsNullOrWhiteSpace(details.Name), "Counterparty Name is required.");
        Guard.Against(details.LegalType is { } type && !Enum.IsDefined(type), "Unsupported LegalType.");
        Guard.Against(details.DefaultPriceTypeId == Guid.Empty, "DefaultPriceTypeId must be null or a nonempty ID.");
        Name = details.Name.Trim();
        LegalType = details.LegalType;
        TaxId = details.TaxId;
        RegistrationCode = details.RegistrationCode;
        Phone = details.Phone;
        Email = details.Email;
        Address = details.Address;
        DefaultPriceTypeId = details.DefaultPriceTypeId;
        IsCustomer = details.IsCustomer;
        IsSupplier = details.IsSupplier;
        IsEmployee = details.IsEmployee;
        IsSystem = details.IsSystem;
        Active = details.Active;
        Note = details.Note;
    }

    public void RequirePhysicalDeletionAllowed() => Guard.Against(IsSystem, "A system Counterparty cannot be physically deleted.");
}
