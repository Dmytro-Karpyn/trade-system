# Витрати / Expenses UI

Раніше існували backend Expense/ExpenseCategory CRUD, POST/UNPOST/delete, settlement та поточний `v_batch_cost`; frontend мав лише read list у Purchase tab. Slice додає самостійні екрани та read queries без змін Expense write rules, domain model, таблиць або міграцій.

## Маршрути та список

`/expenses`, `/expenses/new`, `/expenses/:id`, `/expense-categories`. Екрани витрат завантажуються через React.lazy окремими chunks; стартовий application chunk залишається нижче 500 kB.

Список має пошук за номером, коментарем, контрагентом і категорією з debounce 300 ms; inclusive from/to, контрагента, категорію, валюту, тип Усі/Загальні/Закупівельні та DRAFT/POSTED. Існуючий GET `/expenses` доповнено тільки `search` та `isPurchaseExpense`; `purchaseInvoiceId` для Purchase tab лишається сумісним. Пошук трактує `%` і `_` як звичайний текст.

Показані номер, дата, контрагент, категорія, тип, закупівля, валюта, сума, еквівалент UAH, статус і перехід до detail. На mobile використовуються cards, без GUID замість назв.

## Форма й довідники

Реквізити: дата, контрагент, ExpenseCategory, валюта, Amount, ExchangeRate, nullable PurchaseInvoice, Comment та InternalNote. RHF + Zod перевіряють обов’язкові поля, позитивні Amount/ExchangeRate, 4 знаки суми та 6 знаків курсу. Backend повторно перевіряє інваріанти.

У формі UAH rate = 1, поле вимкнене; для іншої валюти курс редагується. Валюти — погоджена UAH та вже використані CurrencyId організації, без нового довідника або вигаданих ISO-кодів. AmountUah — preview Amount × ExchangeRate з кінцевим округленням до 4 знаків через наявний integer-scaled display helper. AmountUah не надсилається у write DTO та не додається до БД.

Для старого DRAFT UAH із курсом, відмінним від 1, UI явно попереджає про нормалізацію. Save або confirmation → POST спочатку зберігає видимий курс 1, навіть без інших edits. POSTED read view показує збережений курс. Backend currency rules не змінені цим UX.

PurchaseInvoiceId = null дає «Загальна витрата», заповнений — «Закупівельна витрата». Lookup містить лише невилучені POSTED PURCHASE_INVOICE своєї організації; OPENING_STOCK і DRAFT не входять у вибір. Видно номер, дату, постачальника, валюту та суму. Нульова закупівля вимкнена з поясненням. Backend CanAllocate перевіряє точне значення до display rounding, не прирівнюючи малу позитивну суму до нуля.

`GET /expenses/lookups` повертає мінімальні назви контрагентів, категорії, валюти та purchase options за expense.view або expense.create. Це не надає права на повний Counterparty/Purchase API. Loading options зберігають передзаповнені значення під час завантаження.

`/expense-categories` використовує існуючі GET/POST/PUT: Name, ParentId, Active. Desktop — таблиця з рівнями, mobile — nested cards і шлях. Create child підставляє ParentId; edit дозволяє перейменування, зміну батька та Active. Ітеративний обхід не обмежує model depth; візуальний відступ обмежується шириною екрана, усі рівні та повний шлях зберігаються. Parent options виключають себе/нащадків; backend остаточно захищає від циклів, зокрема при конкурентному reparenting. Drag-and-drop, reorder і physical delete відсутні.

## Проведення та собівартість

DRAFT editable. PUT використовує початкову Version форми; background refresh її не підміняє. VERSION_CONFLICT зберігає edits до явного «Оновити дані». Перед POST потрібне confirmation, потім save dirty form; failed save блокує POST. Після успішного create та failed POST повторного create немає.

Confirmation: «Після проведення буде створено заборгованість перед контрагентом». Для direct expense додатково: «Витрата також буде включена до поточної собівартості партій цієї закупівлі». POSTED read-only із Number/POSTED; DELETE тільки logical DRAFT. UNPOST скасовує settlement, повертає DRAFT і виключає direct expense із current cost. UI викликає існуючі write endpoints.

«Вплив на собівартість» читається через `GET /expenses/{id}/cost-impact`, лише за cost.view. Backend використовує один repeatable-read snapshot: purchase total/base value, SUM POSTED невилучених direct expenses у UAH, активні source batches із поточним UnitCostUah через `v_batch_cost`. Frontend не розподіляє витрати й не рахує UnitCost. BatchPrice не змінюються.

POSTED direct expense має інформаційне повідомлення про можливий перерахунок поточної собівартості та історичної валової маржі; воно не блокує UNPOST. INVALID_PURCHASE_EXPENSE_ALLOCATION показує погоджений текст, нульова закупівля — окреме пояснення.

Мутації інвалідують expenses/lookups, purchases/costs, stock/write-off costs, sales/cost/profit, settlements і counterparties. Cookie/CSRF, 401/403, validation, retry та snackbar використовують спільний client. Автоматичного replay writes немає.

## Посилання та виплата

Expense detail посилається на Counterparty/Purchase за правом перегляду джерела. Purchase tab «Закупівельні витрати» повторно використовує існуючий список, лінкує expense та має «+ Додати витрату» за expense.create. `/expenses/new?purchaseInvoiceId=…` підставляє закупівлю; GUID перевіряється, membership/status контролюють lookup і backend.

POSTED Expense за money.payment має «Створити виплату» → `/money/payments/new`. Через перевірений Zod navigation state передаються тільки Counterparty, Currency, Amount, ExchangeRate. Money UI встановлює Purpose=SETTLEMENT та settlement fields; SourceAccount/SourceAmount лишаються для явного введення. Prefill застосовується тільки до нового payment, не переписує збережений документ і не надає permissions. Перехід не виконує POST/PUT, не створює payment, рухи або PaymentAllocation.

## Доступ і перевірки

expense.view — list/detail/categories, expense.create — create/edit витрат і категорій, expense.post/unpost/delete — відповідні дії, cost.view — private cost query/UI. Нової permission model немає. Без cost.view cost query не запускається. Є skeleton, no expenses/categories/purchases, API error/retry, mobile cards та sticky Save/Post без horizontal scroll. Empty directories не заповнюються fake values чи seed.

Результати 2026-10-29: додано 36 frontend-тестів (35 Expense/category та 1 Purchase cross-link), 6 PostgreSQL integration tests. Усі **266 frontend-тестів** і **481 backend-тест** пройшли без пропусків. Lint, production build, Release build успішні; backend — 0 warnings/errors, EF model відповідає останній міграції.

Real Chromium smoke з production frontend за HTTPS Caddy, реальною Identity та окремою PostgreSQL пройшов:

- UI створює root/child категорії; general DRAFT без рухів, POST settlement −50 / MoneyMovement 0, UNPOST прибирає settlement;
- Purchase tab відкриває prefilled Expense; direct DRAFT cost 100, POST cost 120 / settlement −200, BatchPrice лишається 125; UNPOST повертає cost 100;
- «Створити виплату» підставляє SETTLEMENT, контрагента, UAH, суму 200, курс 1; source amount порожня, SQL підтверджує 0 MoneyDocument/MoneyMovement;
- mobile cards/tree, sticky actions, відсутність runtime errors та горизонтального overflow. Робоча dev-база не змінюється.

```sh
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx --configuration Release --no-restore
dotnet build backend/Trade.slnx --configuration Release --no-restore
```

Payroll, recurring expenses, approvals, automatic payments, OCR, tax accounting і full P&L не входять до slice.
