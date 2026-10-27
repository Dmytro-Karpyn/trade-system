using Trade.Domain.Counterparties;

namespace Trade.Application;

public sealed record CounterpartyRequest(string Name, LegalType? LegalType = null, string? TaxId = null,
    string? RegistrationCode = null, string? Phone = null, string? Email = null, string? Address = null,
    Guid? DefaultPriceTypeId = null, bool IsCustomer = false, bool IsSupplier = false, bool IsEmployee = false,
    bool IsSystem = false, bool Active = true, string? Note = null)
{
    public CounterpartyDetails ToDetails() => new(Name, LegalType, TaxId, RegistrationCode, Phone, Email, Address,
        DefaultPriceTypeId, IsCustomer, IsSupplier, IsEmployee, IsSystem, Active, Note);
}

public sealed record CounterpartyResponse(Guid Id, Guid OrganizationId, string Name, LegalType? LegalType,
    string? TaxId, string? RegistrationCode, string? Phone, string? Email, string? Address, Guid? DefaultPriceTypeId,
    bool IsCustomer, bool IsSupplier, bool IsEmployee, bool IsSystem, bool Active, string? Note);

public sealed record CounterpartyFilter(bool? IsCustomer = null, bool? IsSupplier = null, bool? IsEmployee = null, bool? Active = null, string? Search = null, bool? IsSystem = null);

internal static class CounterpartyResponses
{
    public static CounterpartyResponse ToResponse(this Counterparty x) => new(x.Id, x.OrganizationId, x.Name,
        x.LegalType, x.TaxId, x.RegistrationCode, x.Phone, x.Email, x.Address, x.DefaultPriceTypeId,
        x.IsCustomer, x.IsSupplier, x.IsEmployee, x.IsSystem, x.Active, x.Note);
}
