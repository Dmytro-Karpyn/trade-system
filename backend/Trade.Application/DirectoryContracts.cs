namespace Trade.Application;

public sealed record CategoryRequest(string Name,Guid? ParentId=null,string? Description=null,int SortOrder=0,bool Active=true);
public sealed record CategoryResponse(Guid Id,Guid OrganizationId,Guid? ParentId,string Name,string? Description,int SortOrder,bool Active);
public sealed record DirectoryNameRequest(string Name,bool Active=true);
public sealed record CurrencyReference(Guid Id,string? Code,string? Name,bool? Active,bool IsBase);
public interface IDirectoryManagement
{
    Task<IReadOnlyList<CategoryResponse>> CategoriesAsync(Guid org,CancellationToken ct);
    Task<CategoryResponse> SaveCategoryAsync(Guid org,Guid? id,CategoryRequest input,CancellationToken ct);
    Task<UnitResponse> UpdateUnitAsync(Guid org,Guid id,DirectoryNameRequest input,CancellationToken ct);
    Task<IReadOnlyList<StorageLocationResponse>> LocationsAsync(Guid org,CancellationToken ct);
    Task<StorageLocationResponse> UpdateLocationAsync(Guid org,Guid id,DirectoryNameRequest input,CancellationToken ct);
    Task<IReadOnlyList<CurrencyReference>> CurrenciesAsync(Guid org,CancellationToken ct);
    Task<IReadOnlyList<PriceTypeResponse>> InitializePricesAsync(Guid org,CancellationToken ct);
}
