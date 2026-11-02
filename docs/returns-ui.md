# Повернення покупців і постачальникам

Backend lifecycle, return limits, stock/reservations, settlements, organization isolation та logical DELETE вже існували. Slice додає спільний frontend workspace для двох видів повернення, без нових таблиць або міграцій.

## Маршрути та робота

| Вид | Список | Нова чернетка | Документ |
| --- | --- | --- | --- |
| CustomerReturn | `/customer-returns` | `/customer-returns/new` | `/customer-returns/:id` |
| SupplierReturn | `/supplier-returns` | `/supplier-returns/new` | `/supplier-returns/:id` |

Списки підтримують дату від/до, контрагента, статус і документ-підставу; на mobile показують cards. Переходи доступні зі списків продажів/закупівель та секцій повернень у проведених накладних. Нові форми приймають `sourceSaleInvoiceId` / `sourcePurchaseInvoiceId` у query string. Посилання на повернення також працюють у складських рухах і взаєморозрахунках через спільний DocumentLink.

Source picker містить тільки POSTED, невилучені документи поточної організації. OPENING_STOCK виключений. Контрагент, початкова ціна, знижка, валюта/курс і Batch беруться з підстави; їх не можна довільно змінити у поверненні. Locations — тільки склади, які вже використовує purchase/sale організації, відповідно до існуючого backend rule; legacy external IDs показуються без вигаданих назв.

Рядки вибираються checkbox. GOODS мають додатну кількість, незмінну початкову партію та розраховану компенсацію. SERVICE не має складських полів, підтримує окрему часткову компенсацію; нова UI-форма вимагає додатну кількість згідно із завданням. Існуюча backend можливість фінансової компенсації SERVICE із нульовою кількістю залишається незмінною.

SupplierReturn показує окремо залишок за закупівлею, physical/reserved/available, доступність без власного резерву та максимальну кількість. Збережений own reserve виключається тільки у своєму складі. Компенсація використовує original purchase price/discount/currency/rate; direct Expenses/current cost не є її вхідними даними.

Форми використовують RHF + Zod, стабільні Id рядків і Version baseline початкових даних. Фонове оновлення query не переписує версію редагованої форми. Підтвердження → save dirty → POST; failed save блокує POST, а збережена перед failed POST чернетка не створюється повторно. POSTED — read-only, UNPOST/DELETE потребують підтвердження; DELETE доступний лише для DRAFT. Інвалідація охоплює return lists/details/limits, stock, settlements, counterparties та source sale/purchase. Success/error — snackbar; stock errors також біля кількості, stale Version має явне оновлення форми.

## Read API та permissions

Існуючі create/update/post/unpost/delete API використовуються без зміни lifecycle. Залишаються сумісними старі GET списків і DTO. Для UI додано:

- `GET /customer-returns/overview` та `/supplier-returns/overview`: фільтри `from`, `to`, `counterpartyId`, `status`, відповідний `sourceSaleInvoiceId` / `sourcePurchaseInvoiceId`; назви та обчислені суми.
- `GET /customer-returns/lookups` та `/supplier-returns/lookups`: мінімальні source/reference names, без cost/price/balance snapshots.
- `POST /customer-returns/preview` та `/supplier-returns/preview`: read-only розрахунок за існуючими domain rules, без збереження документа, номеру, reserve чи movements. Потребує CSRF як кожен unsafe HTTP method.
- `GET /customer-returns/:id/refund` та `/supplier-returns/:id/refund`: загальна сума та компенсація за рядками, тільки query.
- `GET /purchases/:id/returnable-lines` має необов’язковий `excludeSupplierReturnId`. Backend перевіряє власну організацію, DRAFT, невилучений документ та відповідну підставу. Публічні PhysicalQuantity/ReservedQuantity/AvailableQuantity збережено; окремий AvailableForDocument та MaxReturnableQuantity допомагають редагувати own DRAFT.

CustomerReturn використовує існуючі `sale.view/create/post/unpost/delete`; SupplierReturn — `purchase.view/create/post/unpost/delete`. PUT потребує create, як у чинному backend. Lookups/returnable-lines дозволені view або create; preview — create; overview/detail/refund — view. UI guards і кнопки використовують ці ж права. 401/403 обробляє спільний cookie/CSRF API client, auth store або нові permissions не вводяться.

Суми preview обчислює backend, включно з останнім GOODS refund із залишком округлення та сумою документа без проміжного округлення рядків. Збережені customer refund facts не замінюються поточним прайсом. Немає автоматичного cash refund, supplier payment, нових MoneyMovement, Batch або direct expense allocations.

## Перевірка

Vitest/Testing Library перевіряють списки/фільтри, prefill, GOODS/SERVICE/mixed, ліміти, total preview, own reserve, lifecycle, permissions, version conflicts, failed save/POST, loading/error/retry та mobile. PostgreSQL integration tests перевіряють read projections/filters, isolation/authorization, відсутність preview writes, точний refund, service partial compensation та own reserve у збереженому складі; попередні lifecycle/stock/settlement tests залишаються обов’язковими.

Chromium smoke використовує справжні Identity/CSRF, production frontend та окремий PostgreSQL 18. GOODS: physical 6 → 8 → 6, та сама партія, settlement −270 → 0. SERVICE: compensation 50, жодного stock movement. Supplier: DRAFT reserve 2, physical 6 → 4 → 6, settlement +180 → 0, reserve після UNPOST знову 2; current unit cost 100 не впливає на refund 90/од. Перевірено блокування source UNPOST, mobile cards/actions, відсутність horizontal overflow, нових Batch та MoneyMovement. Робоча development БД не використовується для smoke fixtures.

Результати 29.10.2026:

| Перевірка | Результат |
| --- | --- |
| `npm --prefix frontend run lint` | без помилок і попереджень |
| `npm --prefix frontend test` | 318/318, 10 файлів; з них 50 return tests та 2 нові source cross-link tests |
| `npm --prefix frontend run build` | production build успішний |
| `dotnet test backend/Trade.slnx -c Release` | 489/489, 0 skipped; додано 8 PostgreSQL return UI/query cases |
| `dotnet build backend/Trade.slnx -c Release` | 0 errors, 0 warnings |
| EF pending-model check | модель не змінилася відносно останньої міграції |
| Chromium GOODS / SERVICE / supplier + mobile | PASS, 0 page errors, 0 browser warnings |

Vitest використовує один worker для повного набору MUI application tests поряд із PostgreSQL containers; assertion/render timeouts не збільшувалися. Тимчасові smoke API/proxy/БД зупинені після перевірки.
