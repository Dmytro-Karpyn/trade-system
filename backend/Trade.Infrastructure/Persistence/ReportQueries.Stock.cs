using Microsoft.EntityFrameworkCore;
using Trade.Application;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class ReportQueries
{
    private async Task<ReportTable> Stock(Guid org, ReportFilter f, bool cost, CancellationToken ct)
    {
        if (f.View is "movements" or "writeoffs")
        {
            var rows = f.View == "movements" ? await MovementFacts(org,f,cost,ct) : await WriteOffFacts(org,f,cost,ct);
            var columns = new List<ReportColumn> { new("date","Дата","date"),new("documentNumber","Документ","document"),new("product","Номенклатура"),new("batch","Партія"),new("location","Склад"),new("quantity","Кількість","number") };
            if (f.View == "movements") columns.Add(new("balanceAfter","Залишок партії після руху","number"));
            if (cost) columns.Add(new("currentCost","Поточна вартість UAH","money"));
            return new("stock",f.View == "movements" ? "Рух товару" : "Списання",columns,rows.Select(x => { var r = Row(("date",x.Date),("documentId",x.DocumentId),("documentNumber",x.DocumentNumber),("documentType",x.DocumentType),("product",x.Product),("batch",x.Batch),("location",x.Location),("quantity",x.Quantity),("balanceAfter",x.BalanceAfter)); if (cost) r["currentCost"] = x.CurrentCost; return r; }).ToArray(),
                cost && f.View == "writeoffs" ? [new("writeoffs","Поточна вартість списань",Round(rows.Sum(x => x.CurrentCost ?? 0)),Uah)] : [],[CostNote,"Історія — тільки проведені рухи. Залишок після руху враховує також попередні дати та обчислюється окремо для партії/складу."]);
        }
        var rowsByBatch = (await stock.OverviewAsync(org,new(CategoryId:f.CategoryId,StorageLocationId:f.StorageLocationId,ByBatch:true,ProductId:f.ProductId,
            RetailPriceTypeId:f.RetailPriceTypeId,WholesalePriceTypeId:f.WholesalePriceTypeId),false,ct)).Where(x => f.BatchId == null || x.BatchId == f.BatchId).ToArray();
        if (cost) { var current = await Costs(rowsByBatch.Select(x=>x.BatchId!.Value).Distinct().ToArray(),ct); rowsByBatch = rowsByBatch.Select(x=>x with { CurrentUnitCost = new(current.GetValueOrDefault(x.BatchId!.Value),current.GetValueOrDefault(x.BatchId.Value)) }).ToArray(); }
        var byBatch = f.View == "batches";
        var groups = rowsByBatch.GroupBy(x => (x.ProductId,x.StorageLocationId,Batch:byBatch ? x.BatchId : null))
            .Where(g => !f.WithStock || g.Sum(x => x.PhysicalQuantity)>0).Where(g => !f.WithReservations || g.Sum(x => x.ReservedQuantity)>0).ToArray();
        static StockPriceRange? Range(IEnumerable<StockPriceRange?> source) { var values = source.OfType<StockPriceRange>().ToArray(); return values.Length == 0 ? null : new(values.Min(x => x.Min),values.Max(x => x.Max)); }
        var columnsStock = new List<ReportColumn> { new("product","Номенклатура"),new("location","Склад") };
        if (byBatch) columnsStock.Add(new("batch","Партія"));
        columnsStock.AddRange([new("physical","Фізичний залишок","number"),new("reserved","Резерв","number"),new("available","Доступно","number"),new("retailPrice","Роздрібна ціна UAH","range"),new("wholesalePrice","Оптова ціна UAH","range")]);
        if (cost) columnsStock.AddRange([new("currentUnitCost","Поточна собівартість UAH","range"),new("valuation","Вартість залишку UAH","money")]);
        var selectedRows = groups.Select(g => { var x = g.First(); var row = Row(("product",x.ProductName),("location",x.LocationName ?? "Склад без назви"),("batch",byBatch ? x.BatchNumber : null),
            ("physical",g.Sum(x => x.PhysicalQuantity)),("reserved",g.Sum(x => x.ReservedQuantity)),("available",g.Sum(x => x.AvailableQuantity)),
            ("retailPrice",Range(g.Select(x => x.RetailPrice))),("wholesalePrice",Range(g.Select(x => x.WholesalePrice))));
            if (cost) { row["currentUnitCost"] = Range(g.Select(x => x.CurrentUnitCost)); row["valuation"] = g.Sum(x => x.PhysicalQuantity*(x.CurrentUnitCost?.Min ?? 0)); } return row; }).ToArray();
        var metrics = new List<ReportMetric> { new("physical","Фізичний залишок",groups.Sum(g => g.Sum(x => x.PhysicalQuantity))),new("reserved","Резерв",groups.Sum(g => g.Sum(x => x.ReservedQuantity))),new("available","Доступно",groups.Sum(g => g.Sum(x => x.AvailableQuantity))) };
        if (cost) metrics.Add(new("stockCost","Вартість залишку",Round(groups.Sum(g => g.Sum(x => x.PhysicalQuantity*(x.CurrentUnitCost?.Min ?? 0)))),Uah));
        return new("stock",byBatch ? "Залишки за партіями" : "Поточні залишки",columnsStock,selectedRows,metrics,["Залишки поточні; період застосовується лише до історії руху та списань. Типи роздрібної й оптової ціни вибираються явно; довільного підбору за назвою немає.",CostNote]);
    }

    private async Task<List<ReportMovementFact>> MovementFacts(Guid org, ReportFilter f, bool cost, CancellationToken ct)
    {
        var (from,to) = Period(f);
        return await db.Database.SqlQuery<ReportMovementFact>($"""
            WITH history AS (
              SELECT d."Id" AS "DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate" AS "Date",
                p."Name" AS "Product", b."BatchNumber" AS "Batch", loc."Name" AS "Location", m."Quantity",
                SUM(m."Quantity") OVER (PARTITION BY m."BatchId",m."StorageLocationId" ORDER BY d."DocumentDate",d."CreatedAt",d."Id",m."Id" ROWS UNBOUNDED PRECEDING) AS "BalanceAfter",
                (CASE WHEN {cost} THEN m."Quantity"*bc."UnitCostUah" ELSE NULL END)::text AS "CostText"
              FROM stock_movements m JOIN documents d ON d."Id" = m."DocumentId"
              JOIN batches b ON b."Id" = m."BatchId" JOIN purchase_invoice_lines pl ON pl."Id" = b."PurchaseInvoiceLineId"
              JOIN documents source ON source."Id" = pl."PurchaseInvoiceId" AND source."OrganizationId" = {org}
              JOIN products p ON p."Id" = pl."ProductId" AND p."OrganizationId" = {org}
              LEFT JOIN storage_locations loc ON loc."Id" = m."StorageLocationId" AND loc."OrganizationId" = {org}
              LEFT JOIN v_batch_cost bc ON bc."BatchId" = b."Id"
              WHERE d."OrganizationId" = {org} AND d."Status" = 'POSTED' AND d."DeletedAt" IS NULL
                AND ({f.ProductId}::uuid IS NULL OR p."Id" = {f.ProductId}) AND ({f.CategoryId}::uuid IS NULL OR p."CategoryId" = {f.CategoryId})
                AND ({f.BatchId}::uuid IS NULL OR b."Id" = {f.BatchId}) AND ({f.StorageLocationId}::uuid IS NULL OR m."StorageLocationId" = {f.StorageLocationId})
            ) SELECT * FROM history WHERE "Date" BETWEEN {from} AND {to} ORDER BY "Date","DocumentId","Batch"
            """).ToListAsync(ct);
    }
    private async Task<List<ReportMovementFact>> WriteOffFacts(Guid org, ReportFilter f, bool cost, CancellationToken ct)
    {
        var (from,to) = Period(f);
        return await db.Database.SqlQuery<ReportMovementFact>($"""
            SELECT d."Id" AS "DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate" AS "Date",
              p."Name" AS "Product", b."BatchNumber" AS "Batch", loc."Name" AS "Location", l."Quantity", 0::numeric AS "BalanceAfter",
              (CASE WHEN {cost} THEN l."Quantity"*bc."UnitCostUah" ELSE NULL END)::text AS "CostText"
            FROM write_off_lines l JOIN write_offs w ON w."DocumentId" = l."WriteOffId" JOIN documents d ON d."Id" = w."DocumentId"
            JOIN batches b ON b."Id" = l."BatchId" JOIN purchase_invoice_lines pl ON pl."Id" = b."PurchaseInvoiceLineId"
            JOIN documents source ON source."Id" = pl."PurchaseInvoiceId" AND source."OrganizationId" = {org}
            JOIN products p ON p."Id" = pl."ProductId" AND p."OrganizationId" = {org}
            LEFT JOIN storage_locations loc ON loc."Id" = w."StorageLocationId" AND loc."OrganizationId" = {org}
            LEFT JOIN v_batch_cost bc ON bc."BatchId" = b."Id"
            WHERE d."OrganizationId" = {org} AND d."Status" = 'POSTED' AND d."DeletedAt" IS NULL AND d."DocumentDate" BETWEEN {from} AND {to}
              AND ({f.ProductId}::uuid IS NULL OR p."Id" = {f.ProductId}) AND ({f.CategoryId}::uuid IS NULL OR p."CategoryId" = {f.CategoryId})
              AND ({f.BatchId}::uuid IS NULL OR b."Id" = {f.BatchId}) AND ({f.StorageLocationId}::uuid IS NULL OR w."StorageLocationId" = {f.StorageLocationId})
            ORDER BY d."DocumentDate",d."Id",l."LineNo"
            """).ToListAsync(ct);
    }
}
