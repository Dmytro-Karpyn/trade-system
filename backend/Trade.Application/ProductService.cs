using Trade.Domain.Catalog;

namespace Trade.Application;

public sealed class ProductService(ITradeStore store)
{
    public Task<ProductResponse> CreateAsync(Guid organizationId, ProductRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            await RequireUnitScopeAsync(organizationId, request.UnitId, token);
            var product = Product.Create(organizationId, request.ToDetails());
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
            var product = await store.FindProductAsync(organizationId, id, true, token)
                ?? throw new NotFoundException("Product not found.");
            await RequireUnitScopeAsync(organizationId, request.UnitId, token);
            var hasLines = request.ItemType == ItemType.SERVICE && await store.HasPurchaseLinesAsync(id, token);
            var hasSaleLines = request.ItemType != product.ItemType && await store.HasSaleLinesAsync(id, token);
            var hasPrices = request.ItemType != product.ItemType && await store.HasProductPricesAsync(id, token);
            product.Update(request.ToDetails(), hasLines, hasSaleLines, hasPrices);
            return product.ToResponse();
        }, ct);

    private async Task RequireUnitScopeAsync(Guid organizationId, Guid unitId, CancellationToken ct)
    {
        // Preserve earlier externally supplied UnitId values; known directory entries cannot cross tenants.
        if (await store.IsForeignUnitAsync(organizationId, unitId, ct))
            throw new Trade.Domain.Common.DomainException("Unit must belong to the current organization.");
    }
}
