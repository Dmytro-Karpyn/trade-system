# Гроші та взаєморозрахунки UI

До цього slice існували MoneyAccount, MoneyDocument, обидва журнали, balance views та backend POST/UNPOST. На frontend були placeholders `/money` і `/settlements` та робоча вкладка взаєморозрахунків у Counterparty. Вкладка використовується повторно для окремої сторінки; баланс та running balance у frontend не перераховуються.

## Маршрути

- `/money` — рахунки, поточні баланси та швидкі переходи до грошових операцій;
- `/money/accounts` — create/edit рахунків і деактивація через Active;
- `/money/receipts`, `/money/receipts/new`, `/money/receipts/:id` — надходження;
- `/money/payments`, `/money/payments/new`, `/money/payments/:id` — виплати;
- `/money/transfers`, `/money/transfers/new`, `/money/transfers/:id` — перекази;
- `/settlements` — unified balances Counterparty + Currency;
- `/settlements/:counterpartyId` — баланси та хронологічні рухи контрагента.

## Грошові рахунки та валюти

Таблиця/картки показують Name, тип CASH/BANK/SAFE/OTHER, валюту, поточний Balance та Active. Баланс читається через backend query над `v_money_account_balance`; для рахунку без рухів backend повертає 0. Значення не додається до MoneyAccount або persistent frontend state. Від’ємний баланс відображається звичайним числом; недостатність коштів не блокує create/edit/POST/UNPOST.

Create/edit використовують існуючі POST/PUT `/money/accounts`. Для них backend уже вимагає `settings.manage`; UI використовує це саме право, нових permissions немає. Валюта використаного рахунку вимкнена у формі відповідно до backend invariant. CurrencyLocked — read DTO, обчислений із посилань документів/рухів, без нового поля таблиці. Physical delete відсутній; Active можна змінити у формі.

Довідник валют не створюється. Lookups містять погоджену UAH `00000000-0000-0000-0000-000000000980` та CurrencyId, уже використані організацією у рахунках і документах. Невідомим валютам не приписуються ISO-коди: UI показує окремі нейтральні «Валюта N», без GUID. Для нової зовнішньої валюти спочатку потрібне наявне посилання через API; Currency CRUD не входить до цього slice.

## Надходження, виплата та переказ

Спільна RHF + Zod форма має дату, коментар і внутрішню примітку. Receipt використовує DestinationAccount/Amount, payment — SourceAccount/Amount. Призначення SETTLEMENT показує окремі Counterparty, SettlementCurrency, SettlementAmount та SettlementExchangeRate. Для UAH курс дорівнює 1 і не редагується. Фактична сума руху рахунку та сума взаєморозрахунку можуть бути різними; frontend не виконує автоматичної конвертації.

OWNER і OPENING_BALANCE приховують settlement fields та передають їх як null; вони не є income/expense. OPENING_BALANCE — звичайна операція без wizard. Перемикання з SETTLEMENT прибирає приховані реквізити. Раніше створений документ із null Purpose можна зберегти без примусового призначення. Наявний non-settlement Purpose старого transfer зберігається при редагуванні, хоча форма не вводить для нього новий вибір призначення.

Transfer потребує різних рахунків. Однакова валюта потребує рівних сум; різні валюти дозволяють різні суми. Effective rate = DestinationAmount / SourceAmount показується у формі й read view та не передається/не зберігається окремо. Amounts мають до 4 знаків, settlement rate — до 8. Фінальна валідація залишається на backend.

Списки мають inclusive from/to, account (source або destination), counterparty, purpose та status. Для transfer не показуються непридатні settlement-фільтри; API підтримує загальний контракт фільтрів. Суми transfer показуються окремо для двох рахунків/валют, без їх додавання.

## Життєвий цикл

DRAFT редагується, не створює журналів. Перед POST потрібне confirmation; dirty форма спочатку зберігається. Failed save блокує POST; після успішного create та невдалого POST зберігається Id чернетки, повторного create немає. Version належить початковим даним форми; background cache refresh не підміняє її. VERSION_CONFLICT пропонує явно завантажити поточні дані.

POSTED read-only, DELETE не показується. POST/UNPOST використовують наявну атомарну, ідемпотентну backend реалізацію та незмінні знаки: receipt +money/-settlement, payment -money/+settlement, transfer -/+ money без settlement. UNPOST має confirmation та повертає DRAFT. Ідемпотентність, від’ємний баланс і присвоєння номера не реалізуються заново у frontend.

Для запитаного logical delete додано відсутній DELETE для receipts/payments/transfers. Application викликає існуючий Document.Delete під тим самим Document FOR UPDATE, що й POST. Видалення дозволене лише DRAFT, бізнес-рядок MoneyDocument лишається в БД; POSTED journal не змінюється. Deleted документи виключені зі списків та GET/PUT/POST/UNPOST. Інші Money write rules не змінені.

Усі мутації інвалідують money, settlements та counterparty queries. Cookie/CSRF, 401/403, validation errors, retry та snackbar використовують спільний API client. Автоматичного повторення write-запитів немає.

## Взаєморозрахунки та посилання

Unified overview читає `v_counterparty_balance`, не створює Debtors/Creditors/Advances modules. Фільтри: Усі / Нам винні (>0) / Ми винні (<0) / Нульові (=0), пошук за назвою з debounce 300 ms, валюта. Рядок без рухів не отримує вигаданого нульового балансу у довільній валюті.

Detail використовує існуючий `/settlements/counterparties/{id}`: balances за валютами й BalanceAfterMovement з backend SQL window SUM. UI пояснює: + = контрагент винен нам, − = ми винні контрагенту. Колонка валюти окрема; різні валюти не агрегуються. Source document link доступний тільки для вже реалізованого route та за правом перегляду джерела.

Counterparty settlements tab посилається на окремий detail. Money SETTLEMENT form/read view має посилання на detail за `settlement.view`. Спільний DocumentLink підтримує Purchase/OpeningStock, Sale, Money documents, StockTransfer, WriteOff та Expense. Для Returns, detail UI яких ще немає, номер залишається текстом. Expense може відкрити новий payment із перевіреним navigation-state prefill; документ або рухи від переходу не створюються: [expenses-ui.md](expenses-ui.md).

## Доступ і mobile

money.view відкриває dashboard, списки, рахунки та detail; money.receipt/payment/transfer керують create/edit/post/unpost/delete відповідного виду. Користувач із write permission без money.view бачить на `/money` тільки дозволені quick actions і може працювати зі своїм щойно створеним документом без заборонених read-запитів. Мінімальний money lookup не надає прав на повний Counterparty API. `settlement.view` захищає unified balances/detail, `settings.manage` — Account CRUD, як на backend.

Mobile використовує cards для рахунків, документів і settlement balances, вертикальні поля та sticky Save/Post. Передбачені skeleton, empty accounts/documents/balances/movements, API error та retry. Обробляються VERSION_CONFLICT, DOCUMENT_NOT_DRAFT, INVALID_MONEY_OPERATION, PERMISSION_DENIED, field validation errors і загальні 401/403; legacy backend detail для конфлікту Version також перекладається.

## Нові API та перевірки

Read additions: `GET /money/account-overview`, `/money/lookups`, `/money/receipts|payments|transfers`, `/settlements/overview`, `/settlements/currencies`. Новий write endpoint лише `DELETE /money/{kind}/{id}`. Нових таблиць, міграцій або persisted balances/totals/rates немає. Existing Money mapper став доступним для read query без дублювання DTO mapping.

Frontend-тести охоплюють dashboard, від’ємний баланс, Account CRUD/permissions/currency lock, усі види форм, SETTLEMENT/OWNER/OPENING_BALANCE, UAH rate, same/cross-currency transfer, derived rate без його збереження, списки/фільтри, POST/UNPOST/delete, version baseline/conflicts, failed POST recovery, field errors, mobile та unified settlements/source links. PostgreSQL integration tests перевіряють view balances, фільтри/metadata, logical delete трьох видів, concurrent POST/DELETE, права, currency separation/running balance та organization isolation.

Фінальні результати 2026-10-29: **230 frontend-тестів** і **475 backend integration tests** пройшли, без failed/skipped; додано 38 frontend та 9 integration tests. Lint, production build і Release build успішні; backend має 0 warnings/errors. EF model відповідає останній міграції.

Real Chromium smoke пройшов через HTTPS Caddy, справжні Identity cookie/CSRF та окрему PostgreSQL 18. Рахунки CASH UAH і BANK створено через UI. OPENING_BALANCE receipt дає +100 MoneyMovement і 0 SettlementMovement. Customer SETTLEMENT receipt дає +50 money / −50 settlement, buyer balance 200 → 150; UNPOST прибирає обидва рухи й відновлює 200. Supplier payment дає −250 money / +250 settlement і дозволяє cash balance −150. Transfer −30/+30 дає cash −180 / bank 30, без settlement. SQL перевірив кількість і знаки фактичних рухів. Посилання settlement → source document і назад, mobile cards, sticky actions, відсутність runtime errors та горизонтального overflow перевірені. Робоча dev-база не змінювалась.

Команди перевірки:

```sh
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx --configuration Release --no-restore
dotnet build backend/Trade.slnx --configuration Release --no-restore
```

Bank import/API, reconciliation, invoice matching/PaymentAllocation, currency revaluation, POS та printed cash orders не реалізуються.
