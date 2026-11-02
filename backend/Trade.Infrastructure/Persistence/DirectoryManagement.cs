using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Pricing;
using Trade.Domain.Settlements;

namespace Trade.Infrastructure.Persistence;

internal sealed class DirectoryManagement(TradeDbContext db,IFinanceQueries finance) : IDirectoryManagement
{
    private static CategoryResponse Map(ProductCategory x)=>new(x.Id,x.OrganizationId,x.ParentId,x.Name,x.Description,x.SortOrder,x.Active);
    public async Task<IReadOnlyList<CategoryResponse>> CategoriesAsync(Guid org,CancellationToken ct)=>(await db.ProductCategories.AsNoTracking().Where(x=>x.OrganizationId==org).OrderBy(x=>x.SortOrder).ThenBy(x=>x.Name).ThenBy(x=>x.Id).ToListAsync(ct)).Select(Map).ToArray();
    public async Task<CategoryResponse> SaveCategoryAsync(Guid org,Guid? id,CategoryRequest input,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({org.ToString()}, 83931))",ct);
        var all=await db.ProductCategories.Where(x=>x.OrganizationId==org).ToDictionaryAsync(x=>x.Id,ct);
        var category=id is null ? null : all.GetValueOrDefault(id.Value) ?? throw new NotFoundException("Category not found.");
        var seen=new HashSet<Guid>();var parent=input.ParentId;
        while(parent is { } p) { if(!seen.Add(p)||p==id)throw new DomainException("CATEGORY_CYCLE"); if(!all.TryGetValue(p,out var node))throw new DomainException("Category parent must belong to current organization.");parent=node.ParentId; }
        var details=new ProductCategoryDetails(input.ParentId,input.Name,input.Description,input.SortOrder,input.Active);
        if(category is null){category=ProductCategory.Create(org,details);db.ProductCategories.Add(category);}else category.Update(details);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Map(category);
    }
    public async Task<UnitResponse> UpdateUnitAsync(Guid org,Guid id,DirectoryNameRequest input,CancellationToken ct)
    {
        var unit=await db.Units.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==org,ct)??throw new NotFoundException("Unit not found.");
        unit.Update(input.Name,input.Active);await db.SaveChangesAsync(ct);return new(unit.Id,unit.OrganizationId,unit.Name,unit.Active);
    }
    public async Task<IReadOnlyList<StorageLocationResponse>> LocationsAsync(Guid org,CancellationToken ct)=>await db.StorageLocations.AsNoTracking().Where(x=>x.OrganizationId==org).OrderBy(x=>x.Name).Select(x=>new StorageLocationResponse(x.Id,x.Name,x.Active)).ToListAsync(ct);
    public async Task<StorageLocationResponse> UpdateLocationAsync(Guid org,Guid id,DirectoryNameRequest input,CancellationToken ct)
    {
        var location=await db.StorageLocations.SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==org,ct)??throw new NotFoundException("StorageLocation not found.");
        location.Update(input.Name,input.Active);await db.SaveChangesAsync(ct);return new(location.Id,location.Name,location.Active);
    }
    public async Task<IReadOnlyList<CurrencyReference>> CurrenciesAsync(Guid org,CancellationToken ct)=>(await finance.CurrenciesAsync(org,ct)).Select(id=>new CurrencyReference(id,id==SettlementCurrencies.Uah?"UAH":null,id==SettlementCurrencies.Uah?"Українська гривня":null,null,id==SettlementCurrencies.Uah)).ToArray();
    public async Task<IReadOnlyList<PriceTypeResponse>> InitializePricesAsync(Guid org,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({org.ToString()}, 83932))",ct);
        foreach(var (name,sort) in new[]{("Роздрібна",0),("Гуртова",1)})if(!await db.PriceTypes.AnyAsync(x=>x.OrganizationId==org&&x.Name==name,ct))db.PriceTypes.Add(PriceType.Create(org,name,0m,true,sort));
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return await db.PriceTypes.AsNoTracking().Where(x=>x.OrganizationId==org).OrderBy(x=>x.SortOrder).ThenBy(x=>x.Name).Select(x=>new PriceTypeResponse(x.Id,x.OrganizationId,x.Name,x.DefaultMarkupPercent,x.Active,x.SortOrder)).ToListAsync(ct);
    }
}
