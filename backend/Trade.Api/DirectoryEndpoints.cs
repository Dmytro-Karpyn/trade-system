using Microsoft.AspNetCore.Mvc;
using Trade.Application;

namespace Trade.Api;

public static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        var categories=app.MapGroup("/categories").WithTags("Product categories");
        categories.MapGet("",(RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.CategoriesAsync(c.OrganizationId,ct));
        categories.MapPost("",async(CategoryRequest input,RequestContext c,IDirectoryManagement s,CancellationToken ct)=>{var x=await s.SaveCategoryAsync(c.OrganizationId,null,input,ct);return Results.Created($"/categories/{x.Id}",x);});
        categories.MapPut("/{id:guid}",(Guid id,CategoryRequest input,RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.SaveCategoryAsync(c.OrganizationId,id,input,ct));
        app.MapPut("/units/{id:guid}",(Guid id,DirectoryNameRequest input,RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.UpdateUnitAsync(c.OrganizationId,id,input,ct)).WithTags("Units");
        app.MapGet("/storage-locations",(RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.LocationsAsync(c.OrganizationId,ct)).WithTags("Storage locations");
        app.MapPut("/storage-locations/{id:guid}",(Guid id,DirectoryNameRequest input,RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.UpdateLocationAsync(c.OrganizationId,id,input,ct)).WithTags("Storage locations");
        app.MapGet("/currencies",(RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.CurrenciesAsync(c.OrganizationId,ct)).WithTags("Currencies");
        app.MapPost("/price-types/defaults",(RequestContext c,IDirectoryManagement s,CancellationToken ct)=>s.InitializePricesAsync(c.OrganizationId,ct)).WithTags("Pricing");
        app.MapGet("/admin/audit",([AsParameters] AuditFilter filter,RequestContext c,IAuditQueries s,CancellationToken ct)=>s.ListAsync(c.OrganizationId,filter,ct)).WithTags("Audit");
        app.MapGet("/admin/audit/lookups",(RequestContext c,IAuditQueries s,CancellationToken ct)=>s.LookupsAsync(c.OrganizationId,ct)).WithTags("Audit");
        app.MapGet("/admin/audit/{id:guid}",(Guid id,RequestContext c,IAuditQueries s,CancellationToken ct)=>s.DetailAsync(c.OrganizationId,id,ct)).WithTags("Audit");
    }
}
