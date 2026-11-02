using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;
using Trade.Domain.Settlements;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class ReportQueries(TradeDbContext db, IStockQueries stock, IFinanceQueries finance) : IReportQueries
{
    private static readonly Guid Uah = SettlementCurrencies.Uah;
    private const string CostNote = "Собівартість і валовий прибуток використовують поточну собівартість партій. Зміна закупівельних витрат може змінити історичний прибуток.";
    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) => cells.ToDictionary(x => x.Key, x => x.Value);
    private static decimal Net(IEnumerable<ReportSaleFact> facts) => facts.GroupBy(x=>x.DocumentId).Sum(g=>Round(g.Sum(x=>x.NetAmount)));
    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    private async Task<Dictionary<Guid,decimal>> Costs(Guid[] ids,CancellationToken ct)
    {
        // Unbounded PostgreSQL numeric scale (e.g. expense allocation 1/3) can exceed Npgsql's decimal reader.
        // Parse the lossless numeric text into CLR decimal precision; do not round unit cost in SQL or persist it.
        var facts = await db.Database.SqlQuery<ReportCostFact>($"""SELECT "BatchId", "UnitCostUah"::text AS "CostText" FROM v_batch_cost WHERE "BatchId" = ANY({ids})""").ToListAsync(ct);
        return facts.ToDictionary(x=>x.BatchId,x=>decimal.Parse(x.CostText,CultureInfo.InvariantCulture));
    }
    private static (DateOnly From, DateOnly To) Period(ReportFilter filter)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = filter.From ?? new DateOnly(today.Year, today.Month, 1); var to = filter.To ?? today;
        if (from > to) throw new Trade.Domain.Common.DomainException("From must not be later than To.");
        return (from, to);
    }
    public async Task<ReportTable> ReportAsync(Guid org, string kind, ReportFilter filter, ReportAccess access, CancellationToken ct)
    {
        _ = Period(filter);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var result = kind switch
        {
            "sales" => Sales(await SalesFacts(org, filter, false, ct)),
            "profitability" => Profit(await SalesFacts(org, filter, true, ct), access.Cost),
            "purchases" => await Purchases(org, filter, access.Cost, ct),
            "money" => Money(await MoneyFacts(org, filter, ct), filter),
            "expenses" => await Expenses(org, filter, ct),
            "settlements" => await Settlements(org, filter, ct),
            "stock" => await Stock(org, filter, access.Cost, ct),
            _ => throw new NotFoundException("Report not found.")
        };
        await snapshot.CommitAsync(ct); return result;
    }

    private async Task<List<ReportSaleFact>> SalesFacts(Guid org, ReportFilter f, bool cost, CancellationToken ct)
    {
        var (from, to) = Period(f);
        return await db.Database.SqlQuery<ReportSaleFact>($"""
            WITH events AS (
              SELECT d."Id" AS "DocumentId", d."Number" AS "DocumentNumber", d."DocumentType", d."DocumentDate" AS "Date",
                l."ProductId", p."Name" AS "Product", l."BatchId", b."BatchNumber" AS "Batch", s."CounterpartyId", c."Name" AS "Customer",
                l."Quantity", l."RecommendedUnitPrice" AS "RecommendedPrice", l."UnitPrice" AS "ActualPrice",
                l."Quantity" * l."UnitPrice" AS "GrossAmount", s."DiscountPercent",
                l."Quantity" * l."UnitPrice" * (1 - s."DiscountPercent" / 100) AS "NetAmount",
                (CASE WHEN {cost} AND p."ItemType" = 'GOODS' THEN l."Quantity" * COALESCE(bc."UnitCostUah",0) ELSE 0 END)::text AS "CostText",
                false AS "IsReturn", p."ItemType" = 'GOODS' AS "Goods", p."CategoryId", d."CreatedBy" AS "Manager", s."PriceTypeId"
              FROM sale_invoice_lines l JOIN sale_invoices s ON s."DocumentId" = l."SaleInvoiceId"
              JOIN documents d ON d."Id" = s."DocumentId" JOIN products p ON p."Id" = l."ProductId" AND p."OrganizationId" = {org}
              JOIN counterparties c ON c."Id" = s."CounterpartyId" AND c."OrganizationId" = {org}
              LEFT JOIN batches b ON b."Id" = l."BatchId" LEFT JOIN v_batch_cost bc ON bc."BatchId" = b."Id"
              WHERE d."OrganizationId" = {org} AND d."Status" = 'POSTED' AND d."DeletedAt" IS NULL AND l."RemovedAt" IS NULL
              UNION ALL
              SELECT d."Id", d."Number", d."DocumentType", d."DocumentDate", l."ProductId", p."Name", l."BatchId", b."BatchNumber",
                s."CounterpartyId", c."Name", -rl."Quantity", l."RecommendedUnitPrice", l."UnitPrice", -rl."Quantity" * l."UnitPrice",
                s."DiscountPercent", -rl."RefundAmount",
                (CASE WHEN {cost} AND p."ItemType" = 'GOODS' THEN -rl."Quantity" * COALESCE(bc."UnitCostUah",0) ELSE 0 END)::text,
                true, p."ItemType" = 'GOODS', p."CategoryId", source."CreatedBy", s."PriceTypeId"
              FROM customer_return_lines rl JOIN customer_returns r ON r."DocumentId" = rl."CustomerReturnId"
              JOIN documents d ON d."Id" = r."DocumentId" JOIN sale_invoices s ON s."DocumentId" = r."SourceSaleInvoiceId"
              JOIN documents source ON source."Id" = s."DocumentId" AND source."OrganizationId" = {org}
              JOIN sale_invoice_lines l ON l."Id" = rl."SourceSaleLineId" AND l."SaleInvoiceId" = s."DocumentId"
              JOIN products p ON p."Id" = l."ProductId" AND p."OrganizationId" = {org}
              JOIN counterparties c ON c."Id" = s."CounterpartyId" AND c."OrganizationId" = {org}
              LEFT JOIN batches b ON b."Id" = l."BatchId" LEFT JOIN v_batch_cost bc ON bc."BatchId" = b."Id"
              WHERE d."OrganizationId" = {org} AND d."Status" = 'POSTED' AND d."DeletedAt" IS NULL
            ) SELECT "DocumentId", "DocumentNumber", "DocumentType", "Date", "ProductId", "Product", "BatchId", "Batch",
              "CounterpartyId", "Customer", "Quantity", "RecommendedPrice", "ActualPrice", "GrossAmount", "DiscountPercent", "NetAmount", "CostText", "IsReturn", "Goods"
            FROM events WHERE "Date" BETWEEN {from} AND {to}
              AND ({f.ProductId}::uuid IS NULL OR "ProductId" = {f.ProductId})
              AND ({f.CategoryId}::uuid IS NULL OR "CategoryId" = {f.CategoryId})
              AND ({f.CounterpartyId}::uuid IS NULL OR "CounterpartyId" = {f.CounterpartyId})
              AND ({f.CreatedBy}::uuid IS NULL OR "Manager" = {f.CreatedBy})
              AND ({f.BatchId}::uuid IS NULL OR "BatchId" = {f.BatchId})
              AND ({f.PriceTypeId}::uuid IS NULL OR "PriceTypeId" = {f.PriceTypeId})
            ORDER BY "Date", "DocumentId", "ProductId", "BatchId"
            """).ToListAsync(ct);
    }

    private static ReportTable Sales(IReadOnlyList<ReportSaleFact> facts)
    {
        var sold = facts.Where(x => !x.IsReturn).ToArray(); var gross = sold.Sum(x => x.GrossAmount); var net = Net(sold);
        return new("sales", "Продажі", [new("date","Дата","date"), new("documentNumber","Документ","document"), new("customer","Покупець"),
            new("product","Номенклатура"), new("batch","Партія"), new("quantity","Кількість","number"), new("recommendedPrice","Рекомендована ціна","money"),
            new("actualPrice","Фактична ціна","money"), new("grossAmount","Сума до знижки","money"), new("discountPercent","Знижка, %","percent"), new("netAmount","Чиста сума","money")],
            facts.Select(x => Row(("date",x.Date),("documentId",x.DocumentId),("documentNumber",x.DocumentNumber),("documentType",x.DocumentType),("customer",x.Customer),
                ("product",x.Product),("batch",x.Goods ? x.Batch : null),("quantity",x.Quantity),("recommendedPrice",x.RecommendedPrice),("actualPrice",x.ActualPrice),
                ("grossAmount",x.IsReturn && !x.Goods ? null : x.GrossAmount),("discountPercent",x.DiscountPercent),("netAmount",x.NetAmount))).ToArray(),
            [new("quantity","Кількість після повернень",facts.Sum(x => x.Quantity)), new("grossSales","Продажі до знижки",Round(gross),Uah),
             new("discounts","Знижки продажів",Round(gross-net),Uah),new("sales","Продажі після знижки",Round(net),Uah),
             new("returns","Повернення покупців",Round(-facts.Where(x => x.IsReturn).Sum(x => x.NetAmount)),Uah),new("netSales","Чисті продажі",Net(facts),Uah)],
            ["Тільки проведені документи. Повернення відображені датою повернення; менеджер — автор початкового продажу. Для часткової компенсації SERVICE сума до знижки не визначається."]);
    }
    private static ReportTable Profit(IReadOnlyList<ReportSaleFact> facts, bool showCost)
    {
        var columns = new List<ReportColumn> { new("product","Номенклатура"),new("batch","Партія"),new("customer","Покупець"),new("netSales","Чисті продажі","money") };
        if (showCost) columns.Add(new("currentCost","Поточна собівартість","money"));
        columns.AddRange([new("grossProfit","Валовий прибуток","money"),new("margin","Маржа, %","percent"),new("priceDeviation","Відхилення від рекомендованої ціни, грн","money"),new("belowCost","Нижче собівартості","boolean")]);
        var rows = facts.GroupBy(x => (x.ProductId,x.BatchId,x.CounterpartyId)).Select(g => {
            var x = g.First(); var net = Net(g); var cost = g.Sum(x => x.CurrentCost); var profit = net-cost;
            var row = Row(("product",x.Product),("batch",x.Batch),("customer",x.Customer),("netSales",Round(net)),("grossProfit",Round(profit)),
                ("margin",net == 0 ? null : Round(profit/net*100)),("priceDeviation",Round(g.Sum(x => x.Quantity*(x.ActualPrice-x.RecommendedPrice)))),
                ("belowCost",g.Any(x => !x.IsReturn && x.Goods && x.NetAmount < x.CurrentCost)));
            if (showCost) row["currentCost"] = Round(cost); return row;
        }).ToArray();
        var totalNet = Net(facts); var totalCost = facts.Sum(x => x.CurrentCost);
        var metrics = new List<ReportMetric> { new("netSales","Чисті продажі",Round(totalNet),Uah),new("grossProfit","Валовий прибуток",Round(totalNet-totalCost),Uah) };
        if (showCost) metrics.Add(new("cogs","Поточна собівартість реалізованого",Round(totalCost),Uah));
        return new("profitability","Рентабельність",columns,rows,metrics,[CostNote,"Відхилення ціни — сума Quantity × (Actual − Recommended), з урахуванням поверненої кількості. Індикатор нижче собівартості стосується початкового проведеного продажу після знижки; він не змінює документ."]);
    }
}
