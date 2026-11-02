# Склад / Stock UI

Раніше `/stock` був placeholder; StockTransfer і WriteOff уже мали backend API та перевірені доменні правила. Цей slice додає frontend і read-only queries. Domain, write services, таблиці, міграції та формула `v_stock_balance` не змінені.

## Маршрути

- `/stock` — GOODS залишки по Product + StorageLocation або Batch + StorageLocation;
- `/stock/products/:productId` — реквізити, залишки, партії, рухи та поточні ціни;
- `/stock/batches/:batchId` — походження, ціни, залишки та рухи партії;
- `/stock/transfers`, `/stock/transfers/new`, `/stock/transfers/:id` — переміщення;
- `/stock/write-offs`, `/stock/write-offs/new`, `/stock/write-offs/:id` — списання.

## Залишки та довідники

Основна сторінка має пошук із debounce 300 ms за товаром, кодом, штрихкодом і номером партії; склад, категорію, «Тільки з залишком», «Тільки з резервом» та перемикач групування. Quantity-фільтри застосовуються до готової групи на backend. SERVICE не входить до read queries складу.

Physical/Reserved/Available читаються з `v_stock_balance`; backend query агрегує партії. Frontend тримає тільки TanStack Query cache та стан фільтрів/форми, без persistent copies залишків. Reservation drilldown читає активні DRAFT SaleInvoice GOODS, SupplierReturn, StockTransfer та WriteOff із датою, документом, кількістю й іменем автора, якщо воно доступне. Deleted документи та removed sale lines не входять у резерви.

Категорії використовують спільне дерево product_categories: фільтри, overview і detail показують повні шляхи своєї організації. Невідомі старі CategoryId мають нейтральний fallback без GUID або вигаданих назв. Якщо посилань немає, фільтр вимкнений. Довідник описано в [directories-admin.md](directories-admin.md). Відомі склади мають назви з мінімального довідника; старі зовнішні посилання мають нейтральний fallback.

PriceType не має ознаки retail/wholesale. Користувач явно вибирає тип для колонки «Роздрібна ціна» та для «Оптова ціна» у двох необов’язкових селекторах. Вибір не записується у БД. До вибору або за відсутності ціни — «—». Різні ціни партій показуються діапазоном min–max, без вигаданої середньої ціни. У Batch detail показані всі наявні BatchPrice з назвами PriceType. Зміни прайсу тут не виконуються.

Партії доступні лише для читання: немає Batch Active або ручного створення Batch. Походження веде на source PurchaseInvoice/OpeningStock, якщо користувач має purchase.view. Поточна собівартість — тільки `v_batch_cost`; фінансові реквізити закупівлі (ціна, курс, знижка) та UnitCost приховані на server і UI без cost.view. Невідомій валюті не приписується ISO-код; погоджена UAH має відому назву.

Історія читає StockMovement та дату/тип/номер із Document. Running physical balance обчислює SQL window SUM за датою, часом створення, Id документа й Id руху окремо для Product + StorageLocation; у batch query спочатку обмежується конкретний Batch. Цей поточний read report не додає historical validation або збережених balances.

## Переміщення та списання

Списки мають період і статус. Спільні форми використовують React Hook Form + Zod, зберігають початкову Version та Id рядків. Фоновий refresh не підміняє редаговану версію; VERSION_CONFLICT пропонує явне оновлення. Header містить дату, склади або причину списання, Comment та InternalNote. Товар визначається через вибраний Batch; ProductId/OrganizationId/cost/stock не передаються у write DTO.

Партії джерела сортуються за датою закупівлі. `AvailableForDocument` = загальний Available + лише власний збережений резерв того самого DRAFT у тому самому source location. Загальні Physical/Reserved/Available не змінюються. Backend перевіряє власника, тип і DRAFT-статус excludeDocumentId. Власний резерв іншого складу не додається. Остаточні блокування та перевірка достатності залишаються в існуючих write services.

Для складів використано існуюче правило StockTransfer/WriteOff: склад уже має використовуватися у purchase/sale поточної організації. UI не змінює цю business logic і показує лише дозволені operation locations. Причина списання вибирається з існуючого `/write-off-reasons`, без inline створення. За відсутності активних причин форма пояснює, що їх потрібно додати через наявний API.

Проведення завжди має confirmation; змінена форма зберігається перед POST. Failed save блокує POST. Уже створений DRAFT зберігає свій Id при невдалому POST, повторне створення не потрібне. POSTED read-only. UNPOST має confirmation і показує INSUFFICIENT_STOCK/HISTORICAL_NEGATIVE_STOCK або DOCUMENT_HAS_DEPENDENCIES. Logical DELETE доступний тільки DRAFT.

Transfer POST створює backend -/+ рухи того самого Batch. WriteOff POST створює тільки negative StockMovement; frontend не створює Batch, Expense, SettlementMovement або MoneyMovement. TotalCostUah списання читається з `v_write_off_cost` за cost.view, збережені totals/cost snapshots не додаються.

Після змін інвалідуються stock, sales, purchases та settlement queries. Cookie/CSRF, 401/403, validation errors та retry використовують спільний API client. Повторні write-запити автоматично не виконуються.

## Permissions та mobile

stock.view відкриває balances/product/batch/history/reservations; stock.transfer керує transfer screens; stock.writeoff — writeoff screens і reasons; cost.view відкриває розраховану собівартість. profit.view не використовується. Користувач із правом складської операції без stock.view бачить на `/stock` тільки переходи до дозволених операцій. Backend авторизує кожний endpoint та відсікає cost у read DTO незалежно від frontend.

Основний список, операції та їхні рядки на mobile показуються картками. Batch detail складається з вертикальних секцій, таблиці історії мають власне прокручування. Save/Post закріплені внизу форми. Loading skeleton, no stock/reservations/movements, API error та retry передбачені.

## Read API

- `GET /stock/lookups` — склади, operation locations, PriceTypes та існуючі category references;
- `GET /stock/overview` — search, categoryId, storageLocationId, withStock, withReservations, byBatch, productId, retailPriceTypeId, wholesalePriceTypeId;
- `GET /stock/products/{id}` — продукт, агреговані залишки та партії;
- `GET /stock/batches/{id}` — походження, ціни й залишки;
- `GET /stock/movements`, `/stock/reservations` — productId, batchId, storageLocationId;
- `GET /stock/operation-batches` — storageLocationId та необов’язковий excludeDocumentId;
- `GET /stock-transfers/{id}/stock-lines`, `/write-offs/{id}/stock-lines` — read metadata рядків операції;
- `GET /write-offs/{id}/cost` — поточна TotalCostUah, тільки cost.view.

Наявний `/stock/available-batches` продовжує обслуговувати product/location запити без зміни формули залишків. Для операцій використано read adapter усіх доступних GOODS партій джерела, щоб не вимагати зайвого Product API permission.

## Перевірки

Фінальний прогін 2026-10-28: lint успішний, **192 frontend-тести** та **466 backend integration tests** пройшли без пропусків/помилок; production build і Release build успішні, backend має 0 warnings / 0 errors. Додано 34 frontend та 9 PostgreSQL tests. EF model відповідає останній міграції.

```sh
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx --configuration Release --no-restore
dotnet build backend/Trade.slnx --configuration Release --no-restore
```

Frontend-тести перевіряють список, фільтри, групування, GOODS, реквізити Product/Batch, цінові селектори, permissions, історію, усі джерела резерву, transfers/writeoffs CRUD transitions, власний reserve, version conflict, помилки та mobile. PostgreSQL integration tests перевіряють query aggregation/ranges, category references, read cost/permissions, running balance, усі чотири джерела резерву, own reserve source scope та organization isolation.

Real Chromium smoke працює через HTTPS Caddy з production frontend, Identity cookie/CSRF та окремою PostgreSQL 18. Перевірено придбаний і проданий товар: physical 17 / reserve 2 / available 15. Transfer DRAFT додає reserve 4; POST дає source 13 і destination 4; UNPOST повертає source 17 / destination 0 та reserve 6, DELETE звільняє власні 4. WriteOff DRAFT резервує 3; POST дає physical 14 / reserve 2 / available 12 і cost 300 UAH. SQL підтверджує один рух -3 для writeoff, відсутність transfer movements після UNPOST, 0 SettlementMovement/MoneyMovement для обох складських документів і збереження source Batch. Mobile cards, видимість Save/Post, відсутність runtime errors та горизонтального overflow перевірені. Робоча dev-база не змінюється.

Інвентаризація, stocktaking, scanner, labels printing, replenishment/reorder planning, warehouse permissions та offline mode не входять до slice.
