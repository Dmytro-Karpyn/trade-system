using Trade.Domain.Catalog;
using Trade.Domain.Common;
using Trade.Domain.Pricing;

namespace Trade.Application;

public sealed class PricingService(ITradeStore store)
{
    public Task<PriceTypeResponse> CreateTypeAsync(Guid organizationId, PriceTypeRequest request, CancellationToken ct) =>
        store.InTransactionAsync(_ =>
        {
            var type = PriceType.Create(organizationId, request.Name, request.DefaultMarkupPercent, request.Active, request.SortOrder);
            store.AddPriceType(type);
            return Task.FromResult(Response(type));
        }, ct);

    public async Task<IReadOnlyList<PriceTypeResponse>> ListTypesAsync(Guid organizationId, CancellationToken ct) =>
        (await store.ListPriceTypesAsync(organizationId, ct)).Select(Response).ToArray();

    public async Task<PriceTypeResponse> GetTypeAsync(Guid organizationId, Guid id, CancellationToken ct) =>
        Response(await FindTypeAsync(organizationId, id, false, ct));

    public Task<PriceTypeResponse> UpdateTypeAsync(Guid organizationId, Guid id, PriceTypeRequest request, CancellationToken ct) =>
        store.InTransactionAsync(async token =>
        {
            var type = await FindTypeAsync(organizationId, id, true, token);
            type.Update(request.Name, request.DefaultMarkupPercent, request.Active, request.SortOrder);
            return Response(type);
        }, ct);

    public Task<BatchPriceResponse> SetBatchPriceAsync(Guid organizationId, Guid batchId, Guid priceTypeId,
        PriceRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            // Keep the same Batch -> PriceType lock order as sale writes.
            await RequireBatchAsync(organizationId, batchId, token);
            await FindTypeAsync(organizationId, priceTypeId, true, token);
            var price = await store.FindBatchPriceAsync(batchId, priceTypeId, token);
            if (price is null)
            {
                price = BatchPrice.Create(batchId, priceTypeId, request.UnitPrice);
                store.AddBatchPrice(price);
            }
            else price.Change(request.UnitPrice);
            return new BatchPriceResponse(price.BatchId, price.PriceTypeId, price.UnitPrice);
        }, ct);

    public Task<BatchPriceResponse> GetBatchPriceAsync(Guid organizationId, Guid batchId, Guid priceTypeId,
        CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            await RequireBatchAsync(organizationId, batchId, token);
            await FindTypeAsync(organizationId, priceTypeId, false, token);
            var price = await store.FindBatchPriceAsync(batchId, priceTypeId, token)
                ?? throw new NotFoundException("BatchPrice not found.");
            return new BatchPriceResponse(price.BatchId, price.PriceTypeId, price.UnitPrice);
        }, ct);

    public Task<ProductPriceResponse> SetProductPriceAsync(Guid organizationId, Guid productId, Guid priceTypeId,
        PriceRequest request, CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            var product = await RequireServiceAsync(organizationId, productId, token);
            var type = await FindTypeAsync(organizationId, priceTypeId, true, token);
            var price = await store.FindProductPriceAsync(productId, priceTypeId, token);
            if (price is null)
            {
                price = ProductPrice.Create(product, type, request.UnitPrice);
                store.AddProductPrice(price);
            }
            else price.Change(request.UnitPrice);
            return new ProductPriceResponse(price.ProductId, price.PriceTypeId, price.UnitPrice);
        }, ct);

    public Task<ProductPriceResponse> GetProductPriceAsync(Guid organizationId, Guid productId, Guid priceTypeId,
        CancellationToken ct) => store.InTransactionAsync(async token =>
        {
            await RequireServiceAsync(organizationId, productId, token);
            await FindTypeAsync(organizationId, priceTypeId, false, token);
            var price = await store.FindProductPriceAsync(productId, priceTypeId, token)
                ?? throw new NotFoundException("ProductPrice not found.");
            return new ProductPriceResponse(price.ProductId, price.PriceTypeId, price.UnitPrice);
        }, ct);

    private async Task<PriceType> FindTypeAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct) =>
        await store.FindPriceTypeAsync(organizationId, id, forUpdate, ct)
            ?? throw new NotFoundException("PriceType not found.");

    private async Task RequireBatchAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        if ((await store.LockBatchesAsync(organizationId, [id], ct)).Count == 0)
            throw new NotFoundException("Batch not found.");
    }

    private async Task<Product> RequireServiceAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var product = (await store.LockProductsAsync(organizationId, [id], ct)).SingleOrDefault()
            ?? throw new NotFoundException("Product not found.");
        Guard.Against(product.ItemType != ItemType.SERVICE, "ProductPrice is only allowed for SERVICE; GOODS uses BatchPrice.");
        return product;
    }

    private static PriceTypeResponse Response(PriceType type) => new(type.Id, type.OrganizationId, type.Name,
        type.DefaultMarkupPercent, type.Active, type.SortOrder);
}
