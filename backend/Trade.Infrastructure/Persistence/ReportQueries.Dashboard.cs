using System.Data;
using Microsoft.EntityFrameworkCore;
using Trade.Application;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

internal sealed partial class ReportQueries
{
    private async Task<Dictionary<Guid,string>> CategoryPaths(Guid org,CancellationToken ct)
    {
        var categories = await db.ExpenseCategories.AsNoTracking().Where(x=>x.OrganizationId==org).Select(x=>new {x.Id,x.Name,x.ParentId}).ToDictionaryAsync(x=>x.Id,ct);
        return categories.Values.ToDictionary(x=>x.Id,x=> {
            var parts=new List<string>();var seen=new HashSet<Guid>();var node=x;
            while(seen.Add(node.Id)) { parts.Add(node.Name); if(node.ParentId is not { } parent || !categories.TryGetValue(parent,out var next)) break; node=next; }
            parts.Reverse();return string.Join(" / ",parts);
        });
    }
    public async Task<ReportLookups> LookupsAsync(Guid org, ReportAccess access, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name).Select(x => new ReportOption(x.Id,x.Name)).ToListAsync(ct);
        var categoryIds = await db.Products.Where(x => x.OrganizationId == org && x.CategoryId != null).Select(x => x.CategoryId!.Value).Distinct().Order().ToArrayAsync(ct);
        var categoryNames=await DirectoryLabels.Categories(db,org,ct);
        var parties = await db.Counterparties.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Name).Select(x => new ReportOption(x.Id,x.Name)).ToListAsync(ct);
        var managers = await db.Users.AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.FullName ?? x.UserName).Select(x => new ReportOption(x.Id,x.FullName ?? x.UserName!)).ToListAsync(ct);
        var batches = await (from b in db.Batches join l in db.PurchaseInvoiceLines on b.PurchaseInvoiceLineId equals l.Id join p in db.PurchaseInvoices on l.PurchaseInvoiceId equals p.DocumentId
            where p.Document.OrganizationId == org orderby b.BatchNumber select new ReportOption(b.Id,b.BatchNumber)).ToListAsync(ct);
        var types = await db.PriceTypes.Where(x => x.OrganizationId == org).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new ReportOption(x.Id,x.Name)).ToListAsync(ct);
        var locations = (await stock.LookupsAsync(org,ct)).Locations.Select((x,i) => new ReportOption(x.Id,x.Name ?? $"Склад без назви {i+1}")).ToArray();
        var accounts = access.Money ? await db.MoneyAccounts.Where(x => x.OrganizationId == org).OrderBy(x => x.Name).Select(x => new ReportOption(x.Id,x.Name)).ToListAsync(ct) : [];
        var categories = (await CategoryPaths(org,ct)).OrderBy(x=>x.Value).Select(x=>new ReportOption(x.Key,x.Value)).ToArray();
        return new(products,categoryIds.Select((x,i) => new ReportOption(x,categoryNames.GetValueOrDefault(x,$"Категорія без назви {i+1}"))).ToArray(),parties,managers,batches,types,locations,accounts,categories,await finance.CurrenciesAsync(org,ct));
    }

    public async Task<DashboardSummary> DashboardAsync(Guid org, ReportFilter filter, ReportAccess a, CancellationToken ct)
    {
        var (from,to) = Period(filter);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var facts = a.Sales || a.Profit || a.Cost ? await SalesFacts(org,filter,a.Profit || a.Cost,ct) : [];
        var sales = Net(facts.Where(x => !x.IsReturn)); var returns = Round(-facts.Where(x => x.IsReturn).Sum(x => x.NetAmount));
        var net = Net(facts); var cogs = Round(facts.Sum(x => x.CurrentCost)); var profit = Round(net-cogs);
        var expense = a.Expenses || a.Profit ? await Expenses(org,filter with { IsPurchaseExpense = false },ct) : null;
        var general = expense?.Metrics.Single(x => x.Key == "generalExpenses").Value ?? 0;
        var writeoffs = a.Profit || a.Cost && a.WriteOffs ? await WriteOffFacts(org,filter,true,ct) : [];
        var writeoffCost = Round(writeoffs.Sum(x => x.CurrentCost ?? 0));
        var kpis = new List<ReportMetric>(); var blocks = new List<ReportTable>();
        if (a.Sales) kpis.AddRange([new("sales","Продажі",sales,Uah),new("returns","Повернення покупців",returns,Uah),new("netSales","Чисті продажі",net,Uah)]);
        if (a.Cost) kpis.Add(new("cogs","Поточна собівартість реалізованого",cogs,Uah));
        if (a.Profit) kpis.AddRange([new("grossProfit","Валовий прибуток",profit,Uah),new("managementResult","Управлінський результат",Round(profit-general-writeoffCost),Uah)]);
        if (a.Expenses) kpis.Add(new("expenses","Витрати",general,Uah));
        if (a.Cost && a.WriteOffs) kpis.Add(new("writeoffs","Списання",writeoffCost,Uah));
        if (a.Expenses && expense is not null) blocks.Add(new("expenseCategories","Загальні витрати за категоріями",[new("category","Категорія"),new("amountUah","Сума UAH","money")],
            expense.Rows.GroupBy(x => (Guid)x["categoryId"]!).Select(g => Row(("category",g.First()["category"]),("amountUah",g.Sum(x => (decimal)x["amountUah"]!)))).OrderByDescending(x => x["amountUah"]).ToArray(),[],[]));
        if (a.Money)
        {
            var accounts = await finance.AccountsAsync(org,ct);
            kpis.AddRange(accounts.GroupBy(x => x.Account.CurrencyId).Select(g => new ReportMetric("money","Грошові кошти",g.Sum(x => x.Balance),g.Key)));
            blocks.Add(new("moneyAccounts","Поточні залишки за рахунками",[new("account","Рахунок"),new("currencyId","Валюта","currency"),new("balance","Баланс","money")],accounts.Select(x => Row(("account",x.Account.Name),("currencyId",x.Account.CurrencyId),("balance",x.Balance))).ToArray(),[],[]));
        }
        if (a.Settlement)
        {
            var balances = await finance.SettlementsAsync(org,null,null,null,ct);
            kpis.AddRange(balances.GroupBy(x => x.CurrencyId).SelectMany(g => new[] { new ReportMetric("receivables","Нам винні",g.Where(x => x.Balance>0).Sum(x => x.Balance),g.Key),new ReportMetric("payables","Ми винні",-g.Where(x => x.Balance<0).Sum(x => x.Balance),g.Key) }));
            foreach (var debtor in new[] { true,false }) blocks.Add(new(debtor ? "debtors" : "creditors",debtor ? "Найбільші боржники" : "Найбільші кредитори",
                [new("counterparty","Контрагент"),new("currencyId","Валюта","currency"),new("balance","Баланс","money")],
                balances.Where(x => debtor ? x.Balance>0 : x.Balance<0).GroupBy(x => x.CurrencyId).SelectMany(g => g.OrderByDescending(x => Math.Abs(x.Balance)).Take(5))
                    .Select(x => Row(("counterparty",x.CounterpartyName),("currencyId",x.CurrencyId),("balance",x.Balance))).ToArray(),[],["Рейтинг окремо за валютою; конвертація валют не виконується."]));
        }
        if (a.Stock)
        {
            var summary = await db.Database.SqlQuery<ReportStockFact>($"""
                SELECT COUNT(DISTINCT l."ProductId") FILTER (WHERE s."PhysicalQuantity">0)::integer AS "Products",
                    COALESCE(SUM(s."PhysicalQuantity"),0) AS "Physical",COALESCE(SUM(s."ReservedQuantity"),0) AS "Reserved",COALESCE(SUM(s."AvailableQuantity"),0) AS "Available",
                    CASE WHEN {a.Cost} THEN ROUND(COALESCE(SUM(s."PhysicalQuantity"*bc."UnitCostUah"),0),4) ELSE NULL END AS "Valuation"
                FROM v_stock_balance s JOIN batches b ON b."Id"=s."BatchId" JOIN purchase_invoice_lines l ON l."Id"=b."PurchaseInvoiceLineId"
                JOIN documents d ON d."Id"=l."PurchaseInvoiceId" AND d."OrganizationId"={org}
                LEFT JOIN v_batch_cost bc ON bc."BatchId"=b."Id"
                """).SingleAsync(ct);
            var row = Row(("products",summary.Products),("physical",summary.Physical),("reserved",summary.Reserved),("available",summary.Available));
            var cols = new List<ReportColumn> { new("products","Товарів із залишком","number"),new("physical","Фізичний залишок","number"),new("reserved","Резерв","number"),new("available","Доступно","number") };
            if (a.Cost) { row["valuation"] = summary.Valuation; cols.Add(new("valuation","Поточна вартість UAH","money")); }
            blocks.Add(new("stock","Поточний склад",cols,[row],[],["Поточні залишки; кількості різних одиниць виміру наведені як технічний підсумок."]));
        }
        var allowed = new List<DocumentType>();
        if (a.Purchases) allowed.AddRange([DocumentType.PURCHASE_INVOICE,DocumentType.SUPPLIER_RETURN]);
        if (a.Sales) allowed.AddRange([DocumentType.SALE_INVOICE,DocumentType.CUSTOMER_RETURN]);
        if (a.Money) allowed.AddRange([DocumentType.MONEY_RECEIPT,DocumentType.MONEY_PAYMENT,DocumentType.MONEY_TRANSFER]);
        if (a.Expenses) allowed.Add(DocumentType.EXPENSE);
        if (a.Transfers) allowed.Add(DocumentType.STOCK_TRANSFER); if (a.WriteOffs) allowed.Add(DocumentType.WRITE_OFF);
        var recent = await db.Documents.AsNoTracking().Where(x => x.OrganizationId == org && x.DeletedAt == null && allowed.Contains(x.DocumentType) && x.DocumentDate>=from && x.DocumentDate<=to)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(12).Select(x => new { x.Id,x.Number,x.DocumentType,x.DocumentDate,x.Status }).ToListAsync(ct);
        blocks.Add(new("recent","Останні операції",[new("documentNumber","Документ","document"),new("documentType","Тип","documentType"),new("date","Дата","date"),new("status","Статус","status")],
            recent.Select(x => Row(("documentId",x.Id),("documentNumber",x.Number),("documentType",x.DocumentType.ToString()),("date",x.DocumentDate),("status",x.Status.ToString()))).ToArray(),[],[]));
        var trend = facts.GroupBy(x => x.Date).OrderBy(g => g.Key).Select(g => new ReportTrend(g.Key,Net(g.Where(x => !x.IsReturn)),
            Round(-g.Where(x => x.IsReturn).Sum(x => x.NetAmount)),Net(g),a.Profit ? Round(Net(g)-g.Sum(x=>x.CurrentCost)) : null)).ToArray();
        await snapshot.CommitAsync(ct);
        return new(from,to,kpis,a.Sales ? trend.Select(x => x with { GrossProfit = null }).ToArray() : [],a.Profit ? trend : [],blocks,
            [CostNote,"Продажі, повернення, витрати та списання — за період. Борги, гроші та склад — поточні залишки. Витрати KPI — лише загальні; закупівельні витрати вже включені до собівартості реалізованого.",
             "Управлінський результат = валовий прибуток − загальні витрати − поточна вартість списань. Окремого джерела інших фінансових результатів у моделі ще немає; некласифіковані грошові рухи, OWNER та початкові залишки не вважаються доходами/витратами."]);
    }
}
