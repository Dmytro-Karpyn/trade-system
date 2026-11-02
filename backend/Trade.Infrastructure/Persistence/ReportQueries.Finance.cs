using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class ReportQueries
{
    private async Task<ReportTable> Expenses(Guid org, ReportFilter f, CancellationToken ct)
    {
        var (fromDate,toDate) = Period(f);
        var query = from e in db.Expenses.AsNoTracking() join c in db.ExpenseCategories on e.ExpenseCategoryId equals c.Id
            join p in db.Counterparties on e.CounterpartyId equals p.Id
            where e.Document.OrganizationId == org && c.OrganizationId == org && p.OrganizationId == org
                && e.Document.Status == DocumentStatus.POSTED && e.Document.DeletedAt == null
                && e.Document.DocumentDate >= fromDate && e.Document.DocumentDate <= toDate
            select new { e, Category = c.Name, Party = p.Name };
        if (f.CategoryId is { } category) query = query.Where(x => x.e.ExpenseCategoryId == category);
        if (f.CounterpartyId is { } party) query = query.Where(x => x.e.CounterpartyId == party);
        if (f.CurrencyId is { } currency) query = query.Where(x => x.e.CurrencyId == currency);
        if (f.IsPurchaseExpense is { } direct) query = query.Where(x => (x.e.PurchaseInvoiceId != null) == direct);
        var rows = await query.OrderBy(x => x.e.Document.DocumentDate).ThenBy(x => x.e.DocumentId).Select(x => new { x.e.DocumentId,
            x.e.Document.Number,x.e.Document.DocumentDate,x.Category,x.Party,x.e.CurrencyId,x.e.Amount,
            AmountUah = Math.Round(x.e.Amount*x.e.ExchangeRate,4),x.e.PurchaseInvoiceId,x.e.ExpenseCategoryId,
            PurchaseNumber = db.Documents.Where(d => d.Id == x.e.PurchaseInvoiceId && d.OrganizationId == org).Select(d => d.Number).FirstOrDefault() }).ToListAsync(ct);
        var paths = await CategoryPaths(org,ct);
        return new("expenses","Витрати",[new("date","Дата","date"),new("documentNumber","Документ","document"),new("category","Категорія"),new("counterparty","Контрагент"),
            new("kind","Тип"),new("currencyId","Валюта","currency"),new("amount","Сума","money"),new("amountUah","Еквівалент UAH","money"),new("purchaseNumber","Закупівля","purchase")],
            rows.Select(x => Row(("date",x.DocumentDate),("documentId",x.DocumentId),("documentNumber",x.Number),("documentType","EXPENSE"),("category",paths.GetValueOrDefault(x.ExpenseCategoryId,x.Category)),("categoryId",x.ExpenseCategoryId),
                ("counterparty",x.Party),("kind",x.PurchaseInvoiceId is null ? "Загальна" : "Закупівельна"),("currencyId",x.CurrencyId),("amount",x.Amount),("amountUah",x.AmountUah),
                ("purchaseInvoiceId",x.PurchaseInvoiceId),("purchaseNumber",x.PurchaseNumber))).ToArray(),
            [new("generalExpenses","Загальні витрати",rows.Where(x => x.PurchaseInvoiceId == null).Sum(x => x.AmountUah),Uah),
             new("directExpenses","Закупівельні витрати",rows.Where(x => x.PurchaseInvoiceId != null).Sum(x => x.AmountUah),Uah)],
            ["Управлінський результат окремо віднімає лише загальні витрати. Закупівельні витрати вже включені в поточну собівартість партій."]);
    }

    private async Task<ReportTable> Purchases(Guid org, ReportFilter f, bool showCost, CancellationToken ct)
    {
        var (fromDate,toDate) = Period(f);
        var query = from l in db.PurchaseInvoiceLines.AsNoTracking() join p in db.PurchaseInvoices on l.PurchaseInvoiceId equals p.DocumentId
            join product in db.Products on l.ProductId equals product.Id join c in db.Counterparties on p.CounterpartyId equals c.Id
            join b in db.Batches on l.Id equals b.PurchaseInvoiceLineId
            where p.Document.OrganizationId == org && product.OrganizationId == org && c.OrganizationId == org
                && p.Document.DocumentType == DocumentType.PURCHASE_INVOICE && p.Document.Status == DocumentStatus.POSTED && p.Document.DeletedAt == null && l.RemovedAt == null
                && p.Document.DocumentDate >= fromDate && p.Document.DocumentDate <= toDate
            select new { l,p,product,c,b };
        if (f.CounterpartyId is { } supplier) query = query.Where(x => x.p.CounterpartyId == supplier);
        if (f.ProductId is { } productId) query = query.Where(x => x.product.Id == productId);
        if (f.CurrencyId is { } currency) query = query.Where(x => x.p.CurrencyId == currency);
        var facts = await query.OrderBy(x => x.p.Document.DocumentDate).ThenBy(x => x.p.DocumentId).ThenBy(x => x.l.LineNo)
            .Select(x => new { x.p.DocumentId,x.p.Document.Number,x.p.Document.DocumentDate,Supplier = x.c.Name,Product = x.product.Name,x.b.BatchNumber,
                x.p.CurrencyId,x.p.ExchangeRate,x.p.DiscountPercent,x.l.Quantity,x.l.UnitPriceCurrency,x.b.Id }).ToListAsync(ct);
        var purchaseIds = facts.Select(x => x.DocumentId).Distinct().ToArray(); var batchIds = facts.Select(x => x.Id).ToArray();
        var direct = await db.Expenses.Where(x => purchaseIds.Contains(x.PurchaseInvoiceId!.Value) && x.Document.OrganizationId == org
            && x.Document.Status == DocumentStatus.POSTED && x.Document.DeletedAt == null).GroupBy(x => x.PurchaseInvoiceId!.Value)
            .Select(g => new { Id = g.Key, Amount = g.Sum(x => Math.Round(x.Amount*x.ExchangeRate,4)) }).ToDictionaryAsync(x => x.Id,x => x.Amount,ct);
        var costs = showCost ? await Costs(batchIds,ct) : [];
        var columns = new List<ReportColumn> { new("date","Дата","date"),new("documentNumber","Закупівля","document"),new("supplier","Постачальник"),new("product","Номенклатура"),new("batch","Партія"),
            new("quantity","Кількість","number"),new("currencyId","Валюта","currency"),new("grossAmount","До знижки","money"),new("discountPercent","Знижка, %","percent"),new("netAmount","Після знижки","money"),new("amountUah","Еквівалент UAH","money") };
        if (showCost) columns.Add(new("currentUnitCost","Поточна собівартість одиниці UAH","money"));
        var rows = facts.Select(x => { var gross = x.Quantity*x.UnitPriceCurrency; var net = gross*(1-x.DiscountPercent/100);
            var row = Row(("date",x.DocumentDate),("documentId",x.DocumentId),("documentNumber",x.Number),("documentType","PURCHASE_INVOICE"),("supplier",x.Supplier),("product",x.Product),("batch",x.BatchNumber),
                ("quantity",x.Quantity),("currencyId",x.CurrencyId),("grossAmount",gross),("discountPercent",x.DiscountPercent),("netAmount",net),("amountUah",net*x.ExchangeRate));
            if (showCost) row["currentUnitCost"] = costs.GetValueOrDefault(x.Id); return row; }).ToList();
        // Direct expenses are per purchase, not per line: a separate row avoids multiplying them by line count.
        foreach (var group in facts.GroupBy(x => x.DocumentId)) { var x = group.First(); rows.Add(Row(("date",x.DocumentDate),("documentId",x.DocumentId),("documentNumber",x.Number),
            ("documentType","PURCHASE_INVOICE"),("product","Закупівельні витрати · усього по документу"),("amountUah",direct.GetValueOrDefault(x.DocumentId)),("isExpenseTotal",true))); }
        var metrics = facts.GroupBy(x => x.CurrencyId).Select(g => new ReportMetric("purchaseAmount","Закупівлі після знижки",g.GroupBy(x=>x.DocumentId).Sum(doc=>Round(doc.Sum(x=>x.Quantity*x.UnitPriceCurrency)*(1-doc.First().DiscountPercent/100))),g.Key)).ToList();
        metrics.Add(new("purchaseUah","Закупівлі, UAH",facts.GroupBy(x => x.DocumentId).Sum(g => Round(Round(g.Sum(x => x.Quantity*x.UnitPriceCurrency)*(1-g.First().DiscountPercent/100))*g.First().ExchangeRate)),Uah));
        metrics.Add(new("directExpenses","Закупівельні витрати пов’язаних накладних",direct.Values.Sum(),Uah));
        return new("purchases","Закупівлі",columns,rows,metrics,[CostNote,"Закупівельні витрати наведено один раз на накладну, за всіма датами їх проведення; у підсумок закупівлі вони повторно не додаються. За фільтром товару показано витрати всієї пов’язаної накладної, без вигаданого розподілу."]);
    }

    private async Task<List<ReportMoneyFact>> MoneyFacts(Guid org, ReportFilter f, CancellationToken ct)
    {
        var (fromDate,toDate) = Period(f);
        // An operation filter defines the same movement subset for opening, turnover and closing, preserving the identity.
        return await db.Database.SqlQuery<ReportMoneyFact>($"""
            SELECT a."Id" AS "AccountId", a."Name" AS "Account", a."CurrencyId",
              COALESCE(SUM(m."Amount") FILTER (WHERE d."DocumentDate" < {fromDate}),0) AS "Opening",
              COALESCE(SUM(m."Amount") FILTER (WHERE d."DocumentDate" BETWEEN {fromDate} AND {toDate} AND d."DocumentType" = 'MONEY_RECEIPT'),0) AS "Receipts",
              -COALESCE(SUM(m."Amount") FILTER (WHERE d."DocumentDate" BETWEEN {fromDate} AND {toDate} AND d."DocumentType" = 'MONEY_PAYMENT'),0) AS "Payments",
              COALESCE(SUM(m."Amount") FILTER (WHERE d."DocumentDate" BETWEEN {fromDate} AND {toDate} AND d."DocumentType" = 'MONEY_TRANSFER' AND m."Amount" > 0),0) AS "TransfersIn",
              -COALESCE(SUM(m."Amount") FILTER (WHERE d."DocumentDate" BETWEEN {fromDate} AND {toDate} AND d."DocumentType" = 'MONEY_TRANSFER' AND m."Amount" < 0),0) AS "TransfersOut",
              COALESCE(SUM(m."Amount"),0) AS "Closing"
            FROM money_accounts a LEFT JOIN (
              SELECT m.* FROM money_movements m JOIN documents d ON d."Id" = m."DocumentId"
              WHERE d."OrganizationId" = {org} AND d."Status" = 'POSTED' AND d."DeletedAt" IS NULL AND d."DocumentDate" <= {toDate}
                AND ({f.OperationType}::text IS NULL OR d."DocumentType" = {f.OperationType})
            ) m ON m."MoneyAccountId" = a."Id" LEFT JOIN documents d ON d."Id" = m."DocumentId"
            WHERE a."OrganizationId" = {org} AND ({f.AccountId}::uuid IS NULL OR a."Id" = {f.AccountId})
              AND ({f.CurrencyId}::uuid IS NULL OR a."CurrencyId" = {f.CurrencyId})
            GROUP BY a."Id", a."Name", a."CurrencyId" ORDER BY a."Name", a."Id"
            """).ToListAsync(ct);
    }
    private static ReportTable Money(IReadOnlyList<ReportMoneyFact> facts, ReportFilter filter) => new("money","Гроші",
        [new("account","Рахунок"),new("currencyId","Валюта","currency"),new("opening","Початковий залишок","money"),new("receipts","Надходження","money"),new("payments","Виплати","money"),
         new("transfersIn","Перекази вхідні","money"),new("transfersOut","Перекази вихідні","money"),new("closing","Кінцевий залишок","money")],
        facts.Select(x => Row(("account",x.Account),("currencyId",x.CurrencyId),("opening",x.Opening),("receipts",x.Receipts),("payments",x.Payments),("transfersIn",x.TransfersIn),("transfersOut",x.TransfersOut),("closing",x.Closing))).ToArray(),
        facts.GroupBy(x => x.CurrencyId).Select(g => new ReportMetric("closing","Кінцевий залишок",g.Sum(x => x.Closing),g.Key)).ToArray(),
        [filter.OperationType is null ? "Мінусові залишки допустимі. Перекази відокремлено від надходжень/виплат; різні валюти не підсумовуються." : "Фільтр типу операції застосовано також до початкового та кінцевого залишку: це залишки вибраного набору рухів."]);

    private async Task<ReportTable> Settlements(Guid org, ReportFilter f, CancellationToken ct)
    {
        var facts = (await finance.SettlementsAsync(org,null,null,f.CurrencyId,ct)).Where(x => f.CounterpartyId == null || x.CounterpartyId == f.CounterpartyId)
            .Where(x => f.View != "debtors" || x.Balance > 0).Where(x => f.View != "creditors" || x.Balance < 0).ToArray();
        return new("settlements","Взаєморозрахунки",[new("counterparty","Контрагент"),new("currencyId","Валюта","currency"),new("balance","Баланс","money"),new("state","Стан")],
            facts.Select(x => Row(("counterpartyId",x.CounterpartyId),("counterparty",x.CounterpartyName),("currencyId",x.CurrencyId),("balance",x.Balance),("state",x.Balance>0 ? "Нам винні" : x.Balance<0 ? "Ми винні" : "Нульовий баланс"))).ToArray(),
            facts.GroupBy(x => x.CurrencyId).SelectMany(g => new[] { new ReportMetric("receivables","Нам винні",g.Where(x => x.Balance>0).Sum(x => x.Balance),g.Key),new ReportMetric("payables","Ми винні",-g.Where(x => x.Balance<0).Sum(x => x.Balance),g.Key) }).ToArray(),
            ["Поточні баланси за контрагентами. Дата періоду не змінює поточний баланс. Без invoice paid status або розподілу оплат."]);
    }
}
