using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

internal sealed class StockQueries(TradeDbContext db, ITradeStore store) : IStockQueries
{
    public async Task<StockLookups> LookupsAsync(Guid organizationId, CancellationToken ct)
    {
        var all = (await store.GetPurchaseLookupsAsync(organizationId, ct)).Locations;
        var used = await db.PurchaseInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)
            .Union(db.SaleInvoices.Where(x => x.Document.OrganizationId == organizationId).Select(x => x.StorageLocationId)).ToListAsync(ct);
        var categories = await db.Products.Where(x => x.OrganizationId == organizationId && x.ItemType == ItemType.GOODS && x.CategoryId != null)
            .Select(x => x.CategoryId!.Value).Distinct().Order().ToListAsync(ct);
        var categoryNames=await DirectoryLabels.Categories(db,organizationId,ct);
        return new(all, all.Where(x => used.Contains(x.Id)).ToArray(), await db.PriceTypes.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new StockPriceType(x.Id, x.Name, x.Active)).ToListAsync(ct), categories.Select(id => new StockCategory(id, categoryNames.GetValueOrDefault(id))).ToArray());
    }

    private async Task<StockProduct> RequireProduct(Guid organizationId, Guid id, CancellationToken ct)
    {
        var product = await (from p in db.Products
               where p.Id == id && p.OrganizationId == organizationId && p.ItemType == ItemType.GOODS
               select new StockProduct(p.Id, p.Name, p.Code, db.Units.Where(u => u.Id == p.UnitId && u.OrganizationId == organizationId).Select(u => u.Name).FirstOrDefault(), db.ProductCategories.Where(c=>c.Id==p.CategoryId&&c.OrganizationId==organizationId).Select(c=>c.Name).FirstOrDefault(), p.Active, p.CategoryId))
        .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Stock product not found.");
        var categories = await DirectoryLabels.Categories(db, organizationId, ct);
        return product with { Category = product.CategoryId is { } category ? categories.GetValueOrDefault(category) : null };
    }

    private async Task RequireScope(Guid organizationId, Guid? productId, Guid? batchId, CancellationToken ct)
    {
        if (productId is { } product) _ = await RequireProduct(organizationId, product, ct);
        if (batchId is { } batch && !await (from b in db.Batches join l in db.PurchaseInvoiceLines on b.PurchaseInvoiceLineId equals l.Id
            join p in db.PurchaseInvoices on l.PurchaseInvoiceId equals p.DocumentId
            where b.Id == batch && p.Document.OrganizationId == organizationId && (productId == null || l.ProductId == productId) select b.Id).AnyAsync(ct))
            throw new NotFoundException("Batch not found.");
    }

    public async Task<IReadOnlyList<StockRow>> OverviewAsync(Guid organizationId, StockFilter filter, bool includeCost, CancellationToken ct)
    {
        foreach (var type in new[] { filter.RetailPriceTypeId, filter.WholesalePriceTypeId }.OfType<Guid>().Distinct())
            if (!await db.PriceTypes.AnyAsync(x => x.Id == type && x.OrganizationId == organizationId, ct)) throw new NotFoundException("PriceType not found.");
        var query = from balance in db.StockBalances join b in db.Batches on balance.BatchId equals b.Id
                    join line in db.PurchaseInvoiceLines on b.PurchaseInvoiceLineId equals line.Id
                    join purchase in db.PurchaseInvoices on line.PurchaseInvoiceId equals purchase.DocumentId
                    join product in db.Products on line.ProductId equals product.Id
                    where purchase.Document.OrganizationId == organizationId && product.OrganizationId == organizationId && product.ItemType == ItemType.GOODS
                    select new { balance, b, line, purchase, product };
        if (filter.ProductId is { } productId) query = query.Where(x => x.product.Id == productId);
        if (filter.CategoryId is { } category) query = query.Where(x => x.product.CategoryId == category);
        if (filter.StorageLocationId is { } location) query = query.Where(x => x.balance.StorageLocationId == location);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.product.Name, pattern, "\\") || EF.Functions.ILike(x.product.Code, pattern, "\\")
                || x.product.Barcode != null && EF.Functions.ILike(x.product.Barcode, pattern, "\\") || EF.Functions.ILike(x.b.BatchNumber, pattern, "\\"));
        }
        var facts = await query.Select(x => new { x.product.Id, x.product.Name, x.product.Code, x.product.UnitId, x.product.CategoryId,
            x.balance.StorageLocationId, BatchId = x.b.Id, x.b.BatchNumber, x.purchase.Document.DocumentDate, x.purchase.CounterpartyId,
            x.balance.PhysicalQuantity, x.balance.ReservedQuantity, x.balance.AvailableQuantity }).ToListAsync(ct);
        var categoryNames=await DirectoryLabels.Categories(db,organizationId,ct);
        var ids = facts.Select(x => x.BatchId).Distinct().ToArray();
        var units = await db.Units.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var locations = (await store.GetPurchaseLookupsAsync(organizationId, ct)).Locations.ToDictionary(x => x.Id, x => x.Name);
        var parties = await db.Counterparties.AsNoTracking().Where(x => x.OrganizationId == organizationId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var selectedTypes = new[] { filter.RetailPriceTypeId, filter.WholesalePriceTypeId }.OfType<Guid>().ToArray();
        var prices = await db.BatchPrices.AsNoTracking().Where(x => ids.Contains(x.BatchId) && selectedTypes.Contains(x.PriceTypeId)).ToListAsync(ct);
        var costs = includeCost ? await db.BatchCosts.Where(x => ids.Contains(x.BatchId)).ToDictionaryAsync(x => x.BatchId, x => x.UnitCostUah, ct) : [];
        static StockPriceRange? Range(IEnumerable<decimal> values) { var array = values.ToArray(); return array.Length == 0 ? null : new(array.Min(), array.Max()); }
        var rows = facts.GroupBy(x => (x.Id, x.StorageLocationId, BatchId: filter.ByBatch ? (Guid?)x.BatchId : null)).Select(group =>
        {
            var first = group.First(); var batches = group.Select(x => x.BatchId).ToHashSet();
            return new StockRow(first.Id, first.Name, first.Code, units.GetValueOrDefault(first.UnitId), first.CategoryId is {} categoryId?categoryNames.GetValueOrDefault(categoryId):null, first.StorageLocationId,
                locations.GetValueOrDefault(first.StorageLocationId), group.Key.BatchId, filter.ByBatch ? first.BatchNumber : null,
                filter.ByBatch ? first.DocumentDate : null, filter.ByBatch && first.CounterpartyId is { } party ? parties.GetValueOrDefault(party) : null,
                group.Sum(x => x.PhysicalQuantity), group.Sum(x => x.ReservedQuantity), group.Sum(x => x.AvailableQuantity),
                Range(prices.Where(p => batches.Contains(p.BatchId) && p.PriceTypeId == filter.RetailPriceTypeId).Select(p => p.UnitPrice)),
                Range(prices.Where(p => batches.Contains(p.BatchId) && p.PriceTypeId == filter.WholesalePriceTypeId).Select(p => p.UnitPrice)),
                includeCost ? Range(batches.Where(costs.ContainsKey).Select(b => costs[b])) : null, first.CategoryId);
        });
        // Apply quantity filters after grouping, so they describe the row the user actually sees.
        return rows.Where(x => (!filter.WithStock || x.PhysicalQuantity > 0) && (!filter.WithReservations || x.ReservedQuantity > 0))
            .OrderBy(x => x.ProductName).ThenBy(x => x.LocationName).ThenBy(x => x.PurchaseDocumentDate).ThenBy(x => x.BatchId).ToArray();
    }

    public async Task<StockProductDetail> ProductAsync(Guid organizationId, Guid id, Guid? retail, Guid? wholesale, bool includeCost, CancellationToken ct) =>
        new(await RequireProduct(organizationId, id, ct),
            await OverviewAsync(organizationId, new(ProductId: id, RetailPriceTypeId: retail, WholesalePriceTypeId: wholesale), includeCost, ct),
            await OverviewAsync(organizationId, new(ByBatch: true, ProductId: id, RetailPriceTypeId: retail, WholesalePriceTypeId: wholesale), includeCost, ct));

    public async Task<StockBatchDetail> BatchAsync(Guid organizationId, Guid id, bool includeCost, CancellationToken ct)
    {
        await RequireScope(organizationId, null, id, ct);
        var source = await (from b in db.Batches join l in db.PurchaseInvoiceLines on b.PurchaseInvoiceLineId equals l.Id
            join p in db.PurchaseInvoices on l.PurchaseInvoiceId equals p.DocumentId where b.Id == id && p.Document.OrganizationId == organizationId
            select new { b.BatchNumber, l.ProductId, l.UnitPriceCurrency, p.DocumentId, p.Document.Number, p.Document.DocumentType,
                p.Document.DocumentDate, p.CounterpartyId, p.CurrencyId, p.ExchangeRate, p.DiscountPercent }).SingleAsync(ct);
        var supplier = source.CounterpartyId is { } party ? await db.Counterparties.Where(x => x.Id == party && x.OrganizationId == organizationId).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        var prices = await (from price in db.BatchPrices join type in db.PriceTypes on price.PriceTypeId equals type.Id
            where price.BatchId == id && type.OrganizationId == organizationId orderby type.SortOrder, type.Name
            select new StockBatchPrice(type.Id, type.Name, price.UnitPrice)).ToListAsync(ct);
        StockPurchaseValuation? valuation = includeCost ? new(source.UnitPriceCurrency, source.ExchangeRate, source.DiscountPercent,
            await db.BatchCosts.Where(x => x.BatchId == id).Select(x => x.UnitCostUah).SingleAsync(ct)) : null;
        return new(id, source.BatchNumber, await RequireProduct(organizationId, source.ProductId, ct), source.DocumentId, source.Number,
            source.DocumentType.ToString(), source.DocumentDate, supplier, source.CurrencyId, valuation, prices,
            (await OverviewAsync(organizationId, new(ByBatch: true, ProductId: source.ProductId), includeCost, ct)).Where(x => x.BatchId == id).ToArray());
    }

    public async Task<IReadOnlyList<StockMovementEntry>> MovementsAsync(Guid organizationId, Guid? productId, Guid? batchId, Guid? locationId, CancellationToken ct)
    {
        await RequireScope(organizationId, productId, batchId, ct);
        return await db.Database.SqlQuery<StockMovementEntry>($"""
            SELECT m."Id", d."Id" AS "DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate",
                m."StorageLocationId", loc."Name" AS "LocationName", m."Quantity",
                SUM(m."Quantity") OVER (PARTITION BY pl."ProductId", m."StorageLocationId"
                    ORDER BY d."DocumentDate", d."CreatedAt", d."Id", m."Id" ROWS UNBOUNDED PRECEDING) AS "PhysicalBalanceAfter"
            FROM stock_movements m JOIN documents d ON d."Id" = m."DocumentId"
            JOIN batches b ON b."Id" = m."BatchId" JOIN purchase_invoice_lines pl ON pl."Id" = b."PurchaseInvoiceLineId"
            JOIN documents source ON source."Id" = pl."PurchaseInvoiceId"
            JOIN products product ON product."Id" = pl."ProductId"
            LEFT JOIN storage_locations loc ON loc."Id" = m."StorageLocationId" AND loc."OrganizationId" = {organizationId}
            WHERE d."OrganizationId" = {organizationId} AND source."OrganizationId" = {organizationId}
                AND product."OrganizationId" = {organizationId} AND product."ItemType" = 'GOODS' AND d."DeletedAt" IS NULL
                AND ({productId}::uuid IS NULL OR pl."ProductId" = {productId}) AND ({batchId}::uuid IS NULL OR b."Id" = {batchId})
                AND ({locationId}::uuid IS NULL OR m."StorageLocationId" = {locationId})
            ORDER BY d."DocumentDate", d."CreatedAt", d."Id", m."Id"
            """).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockReservationEntry>> ReservationsAsync(Guid organizationId, Guid? productId, Guid? batchId, Guid? locationId, CancellationToken ct)
    {
        await RequireScope(organizationId, productId, batchId, ct);
        return await db.Database.SqlQuery<StockReservationEntry>($"""
            WITH outgoing AS (
                SELECT l."SaleInvoiceId" AS "DocumentId", l."BatchId", s."StorageLocationId", l."Quantity"
                FROM sale_invoice_lines l JOIN sale_invoices s ON s."DocumentId" = l."SaleInvoiceId"
                JOIN products p ON p."Id" = l."ProductId" WHERE l."RemovedAt" IS NULL AND p."ItemType" = 'GOODS'
                UNION ALL SELECT l."SupplierReturnId", b."Id", s."StorageLocationId", l."Quantity"
                FROM supplier_return_lines l JOIN supplier_returns s ON s."DocumentId" = l."SupplierReturnId"
                JOIN batches b ON b."PurchaseInvoiceLineId" = l."SourcePurchaseLineId"
                UNION ALL SELECT l."StockTransferId", l."BatchId", s."FromLocationId", l."Quantity"
                FROM stock_transfer_lines l JOIN stock_transfers s ON s."DocumentId" = l."StockTransferId"
                UNION ALL SELECT l."WriteOffId", l."BatchId", s."StorageLocationId", l."Quantity"
                FROM write_off_lines l JOIN write_offs s ON s."DocumentId" = l."WriteOffId"
            )
            SELECT d."Id" AS "DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate",
                o."BatchId", o."StorageLocationId", o."Quantity", u."FullName" AS "CreatedByName"
            FROM outgoing o JOIN documents d ON d."Id" = o."DocumentId"
            JOIN batches b ON b."Id" = o."BatchId" JOIN purchase_invoice_lines pl ON pl."Id" = b."PurchaseInvoiceLineId"
            JOIN documents source ON source."Id" = pl."PurchaseInvoiceId"
            LEFT JOIN users u ON u."Id" = d."CreatedBy" AND u."OrganizationId" = {organizationId}
            WHERE d."OrganizationId" = {organizationId} AND source."OrganizationId" = {organizationId}
                AND d."Status" = 'DRAFT' AND d."DeletedAt" IS NULL
                AND ({productId}::uuid IS NULL OR pl."ProductId" = {productId}) AND ({batchId}::uuid IS NULL OR b."Id" = {batchId})
                AND ({locationId}::uuid IS NULL OR o."StorageLocationId" = {locationId})
            ORDER BY d."DocumentDate", d."CreatedAt", d."Id", o."BatchId"
            """).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockCandidate>> CandidatesAsync(Guid organizationId, Guid locationId, Guid? excludeDocumentId, CancellationToken ct)
    {
        var own = new Dictionary<Guid, decimal>();
        if (excludeDocumentId is { } id)
        {
            var doc = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId && d.DeletedAt == null, ct)
                ?? throw new NotFoundException("Document not found.");
            doc.RequireDraft();
            if (doc.DocumentType == DocumentType.STOCK_TRANSFER)
                own = await db.StockTransferLines.Where(l => l.StockTransferId == id && db.StockTransfers.Any(s => s.DocumentId == id && s.FromLocationId == locationId)).ToDictionaryAsync(l => l.BatchId, l => l.Quantity, ct);
            else if (doc.DocumentType == DocumentType.WRITE_OFF)
                own = await db.WriteOffLines.Where(l => l.WriteOffId == id && db.WriteOffs.Any(s => s.DocumentId == id && s.StorageLocationId == locationId)).ToDictionaryAsync(l => l.BatchId, l => l.Quantity, ct);
            else throw new Trade.Domain.Common.DomainException("Only a DRAFT transfer or write-off can exclude its own reserve.");
        }
        return (await OverviewAsync(organizationId, new(StorageLocationId: locationId, ByBatch: true), false, ct))
            .Select(row => new StockCandidate(row, own.GetValueOrDefault(row.BatchId!.Value), row.AvailableQuantity + own.GetValueOrDefault(row.BatchId!.Value)))
            .Where(x => x.AvailableForDocument > 0).OrderBy(x => x.Stock.PurchaseDocumentDate).ThenBy(x => x.Stock.BatchId).ToArray();
    }

    public async Task<StockWriteOffCost> WriteOffCostAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        _ = await store.FindWriteOffAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Write-off not found.");
        return new(await db.Database.SqlQuery<decimal>($"""
            SELECT cost."TotalCostUah" AS "Value" FROM v_write_off_cost cost JOIN documents d ON d."Id" = cost."DocumentId"
            WHERE d."Id" = {id} AND d."OrganizationId" = {organizationId}
            """).SingleAsync(ct));
    }

    public async Task<IReadOnlyList<StockRow>> OperationLinesAsync(Guid organizationId, Guid id, bool transfer, CancellationToken ct)
    {
        Guid location; Guid[] batches;
        if (transfer)
        {
            var doc = await store.FindStockTransferAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Transfer not found.");
            location = doc.FromLocationId; batches = doc.Lines.Select(x => x.BatchId).ToArray();
        }
        else
        {
            var doc = await store.FindWriteOffAsync(organizationId, id, false, ct) ?? throw new NotFoundException("Write-off not found.");
            location = doc.StorageLocationId; batches = doc.Lines.Select(x => x.BatchId).ToArray();
        }
        return (await OverviewAsync(organizationId, new(StorageLocationId: location, ByBatch: true), false, ct))
            .Where(x => batches.Contains(x.BatchId!.Value)).ToArray();
    }
}
