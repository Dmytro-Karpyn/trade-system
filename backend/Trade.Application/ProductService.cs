using System.Globalization;
using Trade.Domain.Catalog;
using Trade.Domain.Common;

namespace Trade.Application;

public sealed class ProductService(ITradeStore store)
{
    public Task<ProductResponse> CreateAsync(Guid organizationId, ProductRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await store.LockProductCodesAsync(organizationId, token);
            await RequireUnitScopeAsync(organizationId, request.UnitId, token);
            await RequireCategoryScopeAsync(organizationId,request.CategoryId,token);
            var code = request.Code?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                var number = await store.NextProductCodeNumberAsync(organizationId, token);
                if (number > 999999) throw new DomainException("Automatic product codes are exhausted. Enter a manual code.");
                code = "P" + number.ToString("D6", CultureInfo.InvariantCulture);
            }
            var product = Product.Create(organizationId, (request with { Code = code }).ToDetails());
            await RequireUniqueCodeAsync(organizationId, product.Code, null, token);
            store.AddProduct(product);
            return product.ToResponse();
        }, ct);

    public async Task<IReadOnlyList<ProductResponse>> ListAsync(Guid organizationId, CancellationToken ct, ProductFilter? filter = null) =>
        (await store.ListProductsAsync(organizationId, ct, filter)).Select(x => x.ToResponse()).ToArray();

    public async Task<ProductResponse> GetAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        (await store.FindProductAsync(organizationId, id, false, ct)
            ?? throw new NotFoundException("Product not found.")).ToResponse();

    public Task<ProductResponse> UpdateAsync(Guid organizationId, Guid id, ProductRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await store.LockProductCodesAsync(organizationId, token);
            var product = await store.FindProductAsync(organizationId, id, true, token)
                ?? throw new NotFoundException("Product not found.");
            await RequireUnitScopeAsync(organizationId, request.UnitId, token);
            await RequireCategoryScopeAsync(organizationId,request.CategoryId,token);
            var hasLines = request.ItemType == ItemType.SERVICE && await store.HasPurchaseLinesAsync(id, token);
            var hasSaleLines = request.ItemType != product.ItemType && await store.HasSaleLinesAsync(id, token);
            var hasPrices = request.ItemType != product.ItemType && await store.HasProductPricesAsync(id, token);
            product.Update(request.ToDetails(), hasLines, hasSaleLines, hasPrices);
            await RequireUniqueCodeAsync(organizationId, product.Code, id, token);
            return product.ToResponse();
        }, ct);

    private async Task RequireUniqueCodeAsync(Guid organizationId, string code, Guid? exceptProductId, CancellationToken ct)
    {
        if (await store.ProductCodeExistsAsync(organizationId, code, exceptProductId, ct))
            throw new ConflictException("PRODUCT_CODE_EXISTS");
    }

    private async Task RequireUnitScopeAsync(Guid organizationId, Guid unitId, CancellationToken ct)
    {
        // Preserve earlier externally supplied UnitId values; known directory entries cannot cross tenants.
        if (await store.IsForeignUnitAsync(organizationId, unitId, ct))
            throw new Trade.Domain.Common.DomainException("Unit must belong to the current organization.");
    }
    private async Task RequireCategoryScopeAsync(Guid org,Guid? category,CancellationToken ct)
    {
        if(category is { } id && await store.IsForeignCategoryAsync(org,id,ct))throw new Trade.Domain.Common.DomainException("Category must belong to current organization.");
    }
}
