# Dashboard і звіти

Read-only slice використовує наявні документи, StockMovement, SettlementMovement, MoneyMovement, `v_stock_balance`, `v_batch_cost`, `v_money_account_balance` і `v_counterparty_balance`. Нових business tables, міграцій, кешованих balances/totals/cost snapshots та write rules немає.

## Маршрути

- `/` — dashboard із вибором періоду, KPI, денними трендами продажів/повернень/чистих продажів та валового прибутку, загальними витратами за категоріями, поточними рахунками/боргами/складом і останніми документами.
- `/reports` — каталог доступних звітів.
- `/reports/sales`, `/reports/profitability`, `/reports/stock`, `/reports/purchases`, `/reports/money`, `/reports/expenses`, `/reports/settlements`.

Періоди: сьогодні, останні 7 календарних днів із сьогодні, поточний місяць, попередній місяць, власні from/to. Дати включні. Некоректний порядок дат відхиляє також backend. Тренди містять дні з проведеними операціями; дати точок мають календарне розташування. Наведення показує дату/суму. Категорії витрат показують повний шлях дерева; однакові назви в різних гілках не об’єднуються.

На mobile фільтри звітів розміщені у drawer; таблиці прокручуються всередині свого контейнера. Dashboard має одну/дві колонки та адаптивні SVG-графіки без нового chart/UI framework. Empty/loading/retry, 401/403 та direct-route guards використовують спільні application components.

## Фінансові правила читання

Тільки POSTED, невилучені документи; для purchase/sale — активні рядки. Повернення враховуються датою повернення.

```
Sales = SUM(posted sale net amounts)
CustomerReturns = SUM(posted customer return RefundAmount)
NetSales = Sales - CustomerReturns
CurrentCOGS = SUM(sold GOODS quantity × current batch cost)
              - SUM(returned GOODS quantity × current batch cost)
GrossProfit = NetSales - CurrentCOGS
ManagementResult = GrossProfit - general operating expenses - current writeoff cost
```

SERVICE не має COGS/stock. Refund для SERVICE читається як збережений факт, включно з частковою фінансовою компенсацією. У sales report для такої компенсації не вигадується gross amount. Знижки беруться з original headers, actual/recommended prices — із source sale snapshots. Фільтр менеджера повернення відносить до автора початкового продажу. Суми продажів округлюються один раз на документ до чотирьох знаків, як у settlement posting; product/batch зрізи можуть мати окремий залишок округлення на межі груп.

Прямі закупівельні витрати вже включені в current `v_batch_cost`: їх не віднімають вдруге. Вони показані окремо у expense/purchase reports. У purchase report такі витрати наведені один раз на накладну, за всіма датами проведення; при product filter — витрати всієї пов’язаної накладної, без нового алгоритму розподілу. Current cost і historical profit змінюються після зміни direct Expenses; BatchPrice не переписується.

Модель поки не має окремого джерела other financial result. Тому management result показує наведені реалізовані компоненти; у UI є явне пояснення. Грошові рухи, OWNER, OPENING_BALANCE та null Purpose не трактуються як інші доходи/витрати. Нової класифікації грошових операцій не додано.

Необмежена PostgreSQL numeric scale при дробовому розподілі витрат може перевищувати можливості стандартного Npgsql decimal reader. Read projections передають cost через numeric text і перетворюють на точність CLR decimal без SQL-округлення unit cost; фінансові підсумки округлюються лише для результату. View і persisted schema залишаються незмінними.

## Поточні залишки та період

На dashboard продажі, повернення, загальні витрати та списання — за вибраний період. Борги, гроші й склад — поточні залишки, з явними підписами. У stock report from/to застосовуються до movements/writeoffs; balances/by-batch завжди поточні.

Stock physical/reserved/available читаються зі спільного view, включно з DRAFT Sale/SupplierReturn/Transfer/WriteOff reservations. Valuation = physical × current cost. Роздрібний/оптовий PriceType користувач вибирає явно; без вибору ціна відсутня, а не підібрана за назвою. Для різних batch prices/costs показано діапазон. Movement running balance рахується SQL window окремо за Batch/Location, із попередніми датами до застосування date filter.

Money report використовує реальні MoneyMovement: opening до from; receipts, payments, transfer-in, transfer-out в періоді; closing до to включно. Переказ не є receipt/payment. Мінус дозволений. Operation filter застосовується до opening/turnover/closing одного й того самого набору рухів; UI пояснює цю семантику. Без operation filter це повні баланси рахунку.

Валюти ніколи не сумуються між собою: money і settlement metrics згруповані за CurrencyId. Dashboard debtors/creditors — top 5 у кожній валюті. Знак settlement: + «Нам винні», − «Ми винні». Paid invoice status, PaymentAllocation, debtor/advance tables відсутні. ISO-коди невідомих зовнішніх CurrencyId не вигадуються; узгоджений UAH має GUID `00000000-0000-0000-0000-000000000980`.

## API та доступ

Усі endpoints — GET, під `/api` для browser client:

| Endpoint | Права |
| --- | --- |
| `/dashboard/summary` | authenticated; кожен KPI/block фільтрується сервером за effective permissions |
| `/reports/lookups` | reports.view; accounts тільки з money.view |
| `/reports/sales` | reports.view |
| `/reports/profitability` | reports.view **і** profit.view |
| `/reports/stock` | reports.view |
| `/reports/purchases` | reports.view |
| `/reports/money` | reports.view **і** money.view |
| `/reports/expenses` | reports.view |
| `/reports/settlements` | reports.view **і** settlement.view |

cost.view окремо відкриває COGS, stock valuation і cost columns; profit.view не відкриває cost fields. Profit може обчислюватись на сервері без повернення cost DTO. Dashboard basic sales/expenses/stock використовують відповідні existing view rights або reports.view; money/settlement/profit/cost завжди мають власні gates. Недоступні keys/columns/blocks не повертаються; frontend також фільтрує їх. Recent documents відбираються за доступними типами; source links відкриваються лише за правами відповідних business routes.

Фільтри ReportFilter: from/to, productId/categoryId/counterpartyId/createdBy/batchId/priceTypeId/storageLocationId/currencyId/accountId/operationType/isPurchaseExpense/withStock/withReservations/view/retailPriceTypeId/wholesalePriceTypeId. Відповідні форми показують тільки релевантні поля. `view`: stock — balances/default, batches, movements, writeoffs; settlements — default/all, debtors, creditors.

ReportTable — unmapped read DTO із columns, rows, metrics, notes. Aggregate views/SQL projections та bulk reference loads уникають N+1; report/dashboard запити використовують repeatable-read snapshot. Жодної persistent report summary table або нового індексу без виміряної потреби не додано. Нові routes включені до центральної authorization mapping; Caddy підтримує API prefix та HTML navigation.

TanStack Query keys включають period/filters/effective permissions. Після успішної mutation централізовано інвалідуються dashboard/reports, тому зміна direct Expense, POST/UNPOST або довідника не залишає показники свіжими на вигляд зі старого query cache. Сесію/CSRF обробляє чинний cookie client.

## CSV

CSV експортує лише колонки та рядки поточної відповіді з поточними фільтрами. Під час завантаження export заблокований. UTF-8 BOM, `;`, escaping quotes/newlines, захист від spreadsheet formula injection для текстових значень. Hidden cost fields/internal ids не експортуються. PDF/Excel і custom builder не додано.

## Перевірки

Frontend tests покривають dashboard values/permission visibility/date changes/no fake values, automatic refresh після mutation, сім report routes, filters, current-cost/below-cost UI, stock, negative money, currencies/signs, general/direct expenses, CSV, mobile drawer, 401/403 та retry. PostgreSQL tests перевіряють формули/rounding/discounts/returns, current cost після direct Expense і незмінний BatchPrice, writeoffs, opening/turnover/closing, transfer isolation, currencies, category branches, org/permission isolation та відсутність schema/write changes.

Chromium smoke: справжні Identity cookie/CSRF, production frontend, PostgreSQL 18 в окремій тимчасовій БД. Sale 540 − return 135 = net 405; direct Expense змінює cost 90 → 140, COGS 270 → 420, gross profit 135 → −15; general expense залишається 30, writeoff cost 140, management result −185. BatchPrice 112.50 незмінний. Customer +405, supplier −1400; рахунки −70/+20, загалом UAH −50. CSV зберігає account filter. Mobile charts/filter drawer/table scroll не дають page overflow. Жодних page errors/browser warnings. Робоча development БД не використовується для smoke fixtures.

Результати перевірок 29.10.2026:

| Команда / перевірка | Результат |
| --- | --- |
| `npm --prefix frontend run lint` | без помилок і попереджень |
| `npm --prefix frontend test` | 351/351, 11 файлів; додано 33 report/dashboard tests |
| `npm --prefix frontend run build` | production build успішний |
| `dotnet test backend/Trade.slnx -c Release` | 506/506, 0 skipped; додано 17 PostgreSQL report cases |
| `dotnet build backend/Trade.slnx -c Release` | 0 errors, 0 warnings |
| EF pending-model check | модель не змінилася відносно останньої міграції |
| Chromium, сценарії A–D + CSV/mobile | PASS, 0 page errors, 0 browser warnings |

Тимчасові smoke API/proxy/БД зупинені після перевірки; поточний робочий PostgreSQL збережено.
