using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Inventory;
using Trade.Domain.Purchasing;
using Trade.Domain.Documents;
using Trade.Domain.Sales;
using Trade.Domain.Pricing;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class EfTradeStore(TradeDbContext db) : ITradeStore
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await operation(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<Product?> FindProductAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate)
            return (await db.Products.FromSql($"""
                SELECT * FROM products WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR UPDATE
                """).ToListAsync(cancellationToken)).SingleOrDefault();
        return await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> ListUnitsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Units.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);

    public void AddUnit(Unit unit) => db.Units.Add(unit);

    public Task<bool> IsForeignUnitAsync(Guid organizationId, Guid unitId, CancellationToken cancellationToken) =>
        db.Units.AnyAsync(x => x.Id == unitId && x.OrganizationId != organizationId, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListProductsAsync(Guid organizationId, CancellationToken cancellationToken, ProductFilter? filter = null)
    {
        var query = db.Products.AsNoTracking().Where(x => x.OrganizationId == organizationId);
        if (filter?.ItemType is { } itemType)
        {
            if (!Enum.IsDefined(itemType)) throw new Trade.Domain.Common.DomainException("Unsupported ItemType.");
            query = query.Where(x => x.ItemType == itemType);
        }
        if (filter?.Active is { } active) query = query.Where(x => x.Active == active);
        if (!string.IsNullOrWhiteSpace(filter?.Search))
        {
            // Search text is literal, so barcodes/codes containing % or _ do not become SQL wildcards.
            var escaped = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\") ||
                EF.Functions.ILike(x.Code, pattern, "\\") || x.Barcode != null && EF.Functions.ILike(x.Barcode, pattern, "\\"));
        }
        return await query.OrderBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public Task<bool> HasPurchaseLinesAsync(Guid productId, CancellationToken cancellationToken) =>
        db.PurchaseInvoiceLines.AnyAsync(x => x.ProductId == productId, cancellationToken);

    public Task<bool> HasSaleLinesAsync(Guid productId, CancellationToken cancellationToken) =>
        db.SaleInvoiceLines.AnyAsync(x => x.ProductId == productId, cancellationToken);

    public Task<bool> HasProductPricesAsync(Guid productId, CancellationToken cancellationToken) =>
        db.ProductPrices.AnyAsync(x => x.ProductId == productId, cancellationToken);

    public async Task<PriceType?> FindPriceTypeAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken ct)
    {
        // All price writers exclusively lock their PriceType, including inserts for an absent composite key.
        // Sale pricing holds a shared lock through commit, so its multi-line price snapshot is consistent.
        var query = forUpdate ? db.PriceTypes.FromSql($"""
            SELECT * FROM price_types WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR UPDATE
            """) : db.PriceTypes.FromSql($"""
            SELECT * FROM price_types WHERE "Id" = {id} AND "OrganizationId" = {organizationId} FOR SHARE
            """);
        return (await query.ToListAsync(ct)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<PriceType>> ListPriceTypesAsync(Guid organizationId, CancellationToken ct) =>
        await db.PriceTypes.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);

    public async Task InitializeNewBatchPricesAsync(Guid organizationId, Guid[] newBatchIds, CancellationToken ct)
    {
        if (newBatchIds.Length == 0) return;
        // Product/Batch locks have already been acquired. Lock PriceTypes in ID order before reading markups.
        var types = await db.PriceTypes.FromSql($"""
            SELECT * FROM price_types WHERE "OrganizationId" = {organizationId} ORDER BY "Id" FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        var activeTypeIds = types.Where(x => x.Active).Select(x => x.Id).ToArray();
        if (activeTypeIds.Length == 0) return;

        // Flush the new batches inside the SAME transaction so the ordinary view can read them.
        // A failure here or in the insert still rolls back the document, movements, batches and prices together.
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO batch_prices ("BatchId", "PriceTypeId", "UnitPrice")
            SELECT cost."BatchId", type."Id",
                round(cost."UnitCostUah" * (1 + type."DefaultMarkupPercent" / 100::numeric), 4)
            FROM v_batch_cost cost
            JOIN purchase_invoice_lines line ON line."Id" = cost."PurchaseInvoiceLineId"
            JOIN purchase_invoices purchase ON purchase."DocumentId" = line."PurchaseInvoiceId"
            JOIN documents document ON document."Id" = purchase."DocumentId"
            JOIN price_types type ON type."OrganizationId" = document."OrganizationId"
            WHERE document."OrganizationId" = {organizationId} AND cost."BatchId" = ANY ({newBatchIds})
                AND type."Id" = ANY ({activeTypeIds}) AND type."Active"
            ON CONFLICT ("BatchId", "PriceTypeId") DO NOTHING
            """, ct);
    }

    public Task<BatchPrice?> FindBatchPriceAsync(Guid batchId, Guid priceTypeId, CancellationToken ct) =>
        db.BatchPrices.SingleOrDefaultAsync(x => x.BatchId == batchId && x.PriceTypeId == priceTypeId, ct);

    public Task<ProductPrice?> FindProductPriceAsync(Guid productId, Guid priceTypeId, CancellationToken ct) =>
        db.ProductPrices.SingleOrDefaultAsync(x => x.ProductId == productId && x.PriceTypeId == priceTypeId, ct);

    public async Task<IReadOnlyList<BatchPrice>> GetBatchPricesAsync(Guid[] batchIds, Guid priceTypeId, CancellationToken ct) =>
        await db.BatchPrices.AsNoTracking().Where(x => batchIds.Contains(x.BatchId) && x.PriceTypeId == priceTypeId).ToListAsync(ct);

    public async Task<IReadOnlyList<ProductPrice>> GetProductPricesAsync(Guid[] productIds, Guid priceTypeId, CancellationToken ct) =>
        await db.ProductPrices.AsNoTracking().Where(x => productIds.Contains(x.ProductId) && x.PriceTypeId == priceTypeId).ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> LockProductsAsync(Guid organizationId, Guid[] ids, CancellationToken cancellationToken) =>
        ids.Length == 0 ? [] : await db.Products.FromSql($"""
            SELECT * FROM products WHERE "OrganizationId" = {organizationId} AND "Id" = ANY ({ids}) ORDER BY "Id" FOR SHARE
            """).ToListAsync(cancellationToken);

    public async Task<PurchaseInvoice?> FindPurchaseAsync(Guid organizationId, Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            var documents = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (documents.Count == 0) return null;
        }
        IQueryable<PurchaseInvoice> query = db.PurchaseInvoices.Include(x => x.Document).Include(x => x.Lines);
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<Batch>> FindBatchesAsync(Guid[] lineIds, CancellationToken cancellationToken) =>
        await db.Batches.Where(x => lineIds.Contains(x.PurchaseInvoiceLineId)).ToListAsync(cancellationToken);

    public Task<long> NextDocumentSequenceAsync(CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<long>("""
            SELECT nextval('document_numbers') AS "Value"
            """).SingleAsync(cancellationToken);

    public async Task DeleteMovementsAsync(Guid documentId, CancellationToken cancellationToken) =>
        await db.StockMovements.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<StockBalanceResponse>> GetBalancesAsync(Guid organizationId,
        Guid? storageLocationId, Guid? batchId, CancellationToken cancellationToken) =>
        await (from balance in db.StockBalances
               join batch in db.Batches on balance.BatchId equals batch.Id
               join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               where purchase.Document.OrganizationId == organizationId
                   && (storageLocationId == null || balance.StorageLocationId == storageLocationId)
                   && (batchId == null || balance.BatchId == batchId)
               orderby balance.BatchId, balance.StorageLocationId
               select new StockBalanceResponse(balance.BatchId, balance.StorageLocationId, balance.PhysicalQuantity,
                   balance.ReservedQuantity, balance.AvailableQuantity))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsAsync(Guid documentId, CancellationToken cancellationToken) =>
        await db.StockMovements.AsNoTracking().Where(x => x.DocumentId == documentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BatchReference>> LockBatchesAsync(Guid organizationId, Guid[] batchIds,
        CancellationToken cancellationToken)
    {
        if (batchIds.Length == 0) return [];
        var batches = await db.Batches.FromSql($"""
            SELECT b.* FROM batches b
            JOIN purchase_invoice_lines l ON l."Id" = b."PurchaseInvoiceLineId"
            JOIN purchase_invoices p ON p."DocumentId" = l."PurchaseInvoiceId"
            JOIN documents d ON d."Id" = p."DocumentId"
            WHERE d."OrganizationId" = {organizationId} AND b."Id" = ANY ({batchIds})
            ORDER BY b."Id" FOR UPDATE OF b
            """).ToListAsync(cancellationToken);
        var lineIds = batches.Select(x => x.PurchaseInvoiceLineId).ToArray();
        var products = await db.PurchaseInvoiceLines.Where(x => lineIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ProductId }).ToDictionaryAsync(x => x.Id, x => x.ProductId, cancellationToken);
        return batches.Select(x => new BatchReference(x.Id, products[x.PurchaseInvoiceLineId])).ToArray();
    }

    public async Task<IReadOnlyList<StockState>> GetStockAsync(Guid[] batchIds, Guid? excludeDocumentId,
        CancellationToken cancellationToken)
    {
        if (batchIds.Length == 0) return [];
        // This is a separate SQL statement after locking Batch, so READ COMMITTED sees the last writer's commit.
        var balances = await db.StockBalances.Where(x => batchIds.Contains(x.BatchId)).ToListAsync(cancellationToken);
        if (excludeDocumentId is not { } id)
            return balances.Select(x => new StockState(x.BatchId, x.StorageLocationId, x.PhysicalQuantity, x.ReservedQuantity)).ToArray();
        var own = await (from line in db.SaleInvoiceLines
                         join sale in db.SaleInvoices on line.SaleInvoiceId equals sale.DocumentId
                         join product in db.Products on line.ProductId equals product.Id
                         where sale.DocumentId == id && sale.Document.Status == DocumentStatus.DRAFT
                             && sale.Document.DocumentType == DocumentType.SALE_INVOICE
                             && sale.Document.DeletedAt == null && line.RemovedAt == null
                             && product.ItemType == ItemType.GOODS && line.BatchId != null && batchIds.Contains(line.BatchId.Value)
                         group line by new { line.BatchId, sale.StorageLocationId } into grouped
                         select new { BatchId = grouped.Key.BatchId!.Value, grouped.Key.StorageLocationId,
                             Quantity = grouped.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
        var ownByKey = own.ToDictionary(x => (x.BatchId, x.StorageLocationId), x => x.Quantity);
        var supplierOwn = await (from line in db.SupplierReturnLines
                                 join header in db.SupplierReturns on line.SupplierReturnId equals header.DocumentId
                                 join batch in db.Batches on line.SourcePurchaseLineId equals batch.PurchaseInvoiceLineId
                                 where header.DocumentId == id && header.Document.Status == DocumentStatus.DRAFT
                                     && header.Document.DocumentType == DocumentType.SUPPLIER_RETURN && header.Document.DeletedAt == null
                                     && batchIds.Contains(batch.Id)
                                 group line by new { BatchId = batch.Id, header.StorageLocationId } into grouped
                                 select new { grouped.Key.BatchId, grouped.Key.StorageLocationId,
                                     Quantity = grouped.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
        foreach (var item in supplierOwn)
        {
            var key = (item.BatchId, item.StorageLocationId);
            ownByKey[key] = ownByKey.GetValueOrDefault(key) + item.Quantity;
        }
        var transferOwn = await (from line in db.StockTransferLines
                                 join transfer in db.StockTransfers on line.StockTransferId equals transfer.DocumentId
                                 where transfer.DocumentId == id && transfer.Document.Status == DocumentStatus.DRAFT
                                     && transfer.Document.DocumentType == DocumentType.STOCK_TRANSFER && transfer.Document.DeletedAt == null
                                     && batchIds.Contains(line.BatchId)
                                 group line by new { line.BatchId, transfer.FromLocationId } into grouped
                                 select new { grouped.Key.BatchId, StorageLocationId = grouped.Key.FromLocationId,
                                     Quantity = grouped.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
        foreach (var item in transferOwn)
        {
            var key = (item.BatchId, item.StorageLocationId);
            ownByKey[key] = ownByKey.GetValueOrDefault(key) + item.Quantity;
        }
        var writeOffOwn = await (from line in db.WriteOffLines
                                 join header in db.WriteOffs on line.WriteOffId equals header.DocumentId
                                 where header.DocumentId == id && header.Document.Status == DocumentStatus.DRAFT
                                     && header.Document.DocumentType == DocumentType.WRITE_OFF && header.Document.DeletedAt == null
                                     && batchIds.Contains(line.BatchId)
                                 group line by new { line.BatchId, header.StorageLocationId } into grouped
                                 select new { grouped.Key.BatchId, grouped.Key.StorageLocationId,
                                     Quantity = grouped.Sum(x => x.Quantity) }).ToListAsync(cancellationToken);
        foreach (var item in writeOffOwn)
        {
            var key = (item.BatchId, item.StorageLocationId);
            ownByKey[key] = ownByKey.GetValueOrDefault(key) + item.Quantity;
        }
        return balances.Select(x => new StockState(x.BatchId, x.StorageLocationId, x.PhysicalQuantity,
            x.ReservedQuantity - ownByKey.GetValueOrDefault((x.BatchId, x.StorageLocationId)))).ToArray();
    }

    public async Task<SaleInvoice?> FindSaleAsync(Guid organizationId, Guid id, bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (forUpdate)
        {
            var documents = await db.Documents.FromSql($"""
                SELECT * FROM documents WHERE "Id" = {id} AND "OrganizationId" = {organizationId}
                    AND "DeletedAt" IS NULL FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (documents.Count == 0) return null;
        }
        IQueryable<SaleInvoice> query = db.SaleInvoices.Include(x => x.Document).Include(x => x.Lines);
        if (!forUpdate) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.DocumentId == id && x.Document.OrganizationId == organizationId
            && x.Document.DeletedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<AvailableBatchResponse>> GetAvailableBatchesAsync(Guid organizationId,
        Guid productId, Guid storageLocationId, CancellationToken cancellationToken) =>
        await (from balance in db.StockBalances
               join batch in db.Batches on balance.BatchId equals batch.Id
               join line in db.PurchaseInvoiceLines on batch.PurchaseInvoiceLineId equals line.Id
               join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
               join product in db.Products on line.ProductId equals product.Id
               where purchase.Document.OrganizationId == organizationId && product.Id == productId
                   && product.ItemType == ItemType.GOODS && balance.StorageLocationId == storageLocationId
                   && balance.AvailableQuantity > 0
               orderby purchase.Document.DocumentDate, batch.CreatedAt, batch.Id
               select new AvailableBatchResponse(batch.Id, product.Id, balance.StorageLocationId, balance.PhysicalQuantity,
                   balance.ReservedQuantity, balance.AvailableQuantity, purchase.Document.DocumentDate, batch.BatchNumber))
            .ToListAsync(cancellationToken);

    public void AddProduct(Product product) => db.Products.Add(product);
    public void AddPriceType(PriceType priceType) => db.PriceTypes.Add(priceType);
    public void AddBatchPrice(BatchPrice price) => db.BatchPrices.Add(price);
    public void AddProductPrice(ProductPrice price) => db.ProductPrices.Add(price);
    public void AddPurchase(PurchaseInvoice purchase) => db.PurchaseInvoices.Add(purchase);
    public void AddSale(SaleInvoice sale) => db.SaleInvoices.Add(sale);
    public void AddBatch(Batch batch) => db.Batches.Add(batch);
    public void AddMovement(StockMovement movement) => db.StockMovements.Add(movement);
}
