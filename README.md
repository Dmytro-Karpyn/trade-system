# Trade System

Modular monolith для торгово-складської системи. Реалізовано номенклатуру → закупівлю → партії → складський прихід; продажі → резерви → списання; pricing → ціни партій/послуг і snapshot продажу; контрагентів → взаєморозрахунки; грошові рахунки → надходження/оплати/перекази; категорії витрат → операційні та прямі закупівельні витрати. Залишки, собівартість і баланси обчислюються без кешів. Є EF-міграції та integration tests. Додано Identity login/logout, користувачів, ролі та permissions. Frontend має login, authenticated shell, permissions-aware navigation, dashboard із порожніми бізнес-показниками, Product та Counterparty CRUD і взаєморозрахунки контрагента.

CustomerReturn реалізовано на основі POSTED продажу: повернення GOODS у той самий Batch, часткові компенсації SERVICE та негативний settlement без автоматичного платежу. Ліміти й доступні для повернення рядки обчислюються з проведених документів.

## Стек і структура

ASP.NET Core 10 / C# 14, EF Core 10 / Npgsql, PostgreSQL 18; React 19 / TypeScript / Vite, MUI, TanStack Query, React Hook Form / Zod; REST / OpenAPI; ASP.NET Core Identity з HttpOnly cookie, Serilog; xUnit / Testcontainers; Docker Compose / Caddy.

```text
backend/
  Trade.slnx
  Trade.Api/             # HTTP, OpenAPI, Identity cookies, composition root
  Trade.Application/     # use cases, контракти, порт persistence
  Trade.Domain/          # сутності та інваріанти, без залежностей
  Trade.Infrastructure/  # EF/Npgsql, міграції, view, транзакції, health
  Trade.Tests/           # integration tests із PostgreSQL 18
frontend/                # окремий Vite React TypeScript app
docs/
  domain-rules.md
  architecture.md
  scenarios.md
AGENTS.md
compose.yaml
Caddyfile
```

## Локальний запуск

Потрібні .NET SDK **10.0.402** (див. `global.json`), Node.js **24** та Docker з Compose v2. Команди нижче виконуються з кореня репозиторію.

```sh
cp .env.example .env
docker compose up -d --wait postgres
dotnet restore backend/Trade.slnx --locked-mode
dotnet tool restore
dotnet run --project backend/Trade.Api --no-launch-profile -- --migrate
npm --prefix frontend ci
```

У двох окремих терміналах:

```sh
dotnet run --project backend/Trade.Api --launch-profile http
```

```sh
npm --prefix frontend run dev
```

Frontend використовує порт 5173, API — 5080. Browser API client працює через `/api` proxy (Vite/Caddy прибирає prefix), base URL можна змінити через `VITE_API_BASE_URL`. Browser routes `/products`, `/sales` тощо отримують SPA, зокрема при перезавантаженні. `/login` використовує Identity cookies + CSRF; dashboard і всі розділи потребують current-user session. Показники та останні операції — порожні стани, статус системи читається з `/health`. Дизайн, матриця permissions і тестування: [docs/frontend.md](docs/frontend.md). Номенклатура `/products` має список, пошук/фільтри, create/detail/edit та inline додавання одиниці: [docs/products-ui.md](docs/products-ui.md).

Прихідні накладні `/purchases`, `/purchases/new`, `/purchases/:id`: список, DRAFT, POST/UNPOST, партії/собівартість/ціни та погоджене inline додавання складу. Перед запуском застосуйте нову міграцію `PurchaseStorageLocations`. Деталі: [docs/purchases-ui.md](docs/purchases-ui.md).

Перевірка API з термінала:

```sh
curl --fail http://localhost:5080/health
curl --fail http://localhost:5080/openapi/v1.json
```

`GET /health` повертає `200 {"status":"Healthy"}`, лише якщо PostgreSQL доступний, і `503 {"status":"Unhealthy"}` в разі відмови. OpenAPI (`/openapi/v1.json`) і Swagger UI (`/swagger`) увімкнені лише в Development.

## Конфігурація бази

`backend/Trade.Api/appsettings.json` містить лише локальні dev credentials, які відповідають `.env.example`. ASP.NET Core не завантажує `.env` автоматично: цей файл читає Docker Compose. Якщо змінюєте локальні параметри PostgreSQL, передайте відповідний `ConnectionStrings__TradeDatabase` у процес API:

```sh
export ConnectionStrings__TradeDatabase='Host=localhost;Port=5432;Database=trade;Username=trade;Password=YOUR_LOCAL_PASSWORD;Timeout=5;Command Timeout=5'
dotnet run --project backend/Trade.Api --launch-profile http
```

Не комітьте секрети. Compose передає connection string контейнеру API автоматично. Зміна пароля у `.env` не змінює пароль уже ініціалізованої бази у volume. Звичайний запуск не змінює схему; `--migrate` застосовує EF-міграції і завершує процес. `EnsureCreated` не використовується. Для PostgreSQL 18 volume монтується до `/var/lib/postgresql`.

Для роботи через EF CLI:

```sh
dotnet ef database update --project backend/Trade.Infrastructure --startup-project backend/Trade.Api
dotnet ef migrations has-pending-model-changes --project backend/Trade.Infrastructure --startup-project backend/Trade.Api
```

## API

Списання: `POST/GET /write-off-reasons`, `GET/PUT /write-off-reasons/{id}`, `POST/GET /write-offs`, `GET/PUT/DELETE /write-offs/{id}`, `POST /write-offs/{id}/post|unpost`. Фільтри списку: `from`, `to` (включно), `storageLocationId`, `writeOffReasonId`, `expenseCategoryId`, `status`. Причина належить організації й задає ExpenseCategory; склад перевіряється за використанням у purchase/sale організації. DRAFT резервує stock разом із рештою outgoing документів; POST створює тільки negative StockMovement. UNPOST повертає stock і reserve, DELETE logical DRAFT звільняє reserve; WO-номер зберігається, POST idempotent. `v_write_off_cost` обчислює управлінську вартість як SUM(quantity × current batch cost), без cost snapshot або автоматичного Expense. Історична вартість змінюється після direct purchase Expenses. Vite/Caddy проксіюють нові маршрути; приклад SQL-звіту зі scope організації — у docs/scenarios.md.

Складські переміщення: `POST/GET /stock-transfers`, `GET/PUT/DELETE /stock-transfers/{id}`, `POST /stock-transfers/{id}/post|unpost`. Список підтримує `from`, `to` (включно), `fromLocationId`, `toLocationId`, `status`. Vite/Caddy проксіюють маршрути. From/To — різні склади, вже використані організацією; Batch перевіряється через source purchase. DRAFT резервує тільки From; POST переносить Quantity двома протилежними рухами того самого Batch, без фінансових рухів і зміни cost/price. UNPOST перевіряє stock/reservations destination, видаляє обидва рухи й відновлює source reserve. POST/UNPOST атомарні, повторний POST не дублює рухів, ST-номер зберігається. PUT потребує Version/DRAFT; DELETE логічний лише DRAFT. `v_stock_balance` та `/stock/available-batches` враховують спільні резерви sale, SupplierReturn і StockTransfer; недостатність → `INSUFFICIENT_STOCK`.

Повернення постачальнику: `POST/GET /supplier-returns`, `GET/PUT/DELETE /supplier-returns/{id}`, `POST /supplier-returns/{id}/post|unpost`, `POST /purchases/{id}/supplier-return`, `GET /purchases/{id}/returnable-lines?storageLocationId={id}` (склад optional, default — source purchase). Vite/Caddy проксіюють нові маршрути. Source — POSTED PURCHASE_INVOICE, OPENING_STOCK заборонений. DRAFT резервує разом із sales; own DRAFT не блокує редагування. POST списує original Batch і створює positive settlement за original purchase price після знижки у вихідній валюті/курсі. Direct Expenses у refund не входять; current cost/BatchPrice незмінні. UNPOST відновлює stock і reserve. POST/UNPOST атомарні й idempotent, номер SR зберігається; logical DELETE DRAFT прибирає reserve. Активний return блокує Purchase UNPOST. Помилки: `RETURN_QUANTITY_EXCEEDED`, `INSUFFICIENT_STOCK`, `DOCUMENT_HAS_DEPENDENCIES`. Доступні кількості — лише query, без кешу.

Повернення покупця: `POST/GET /customer-returns`, `GET/PUT/DELETE /customer-returns/{id}`, `POST /customer-returns/{id}/post|unpost`, `POST /sales/{id}/customer-return`, `GET /sales/{id}/returnable-lines`. Маршрут `/customer-returns` і підмаршрути проксіюються Vite/Caddy. PUT потребує Version/DRAFT; logical DELETE лише DRAFT. SourceSaleInvoice — POSTED поточної організації, Counterparty визначається через нього. StorageLocation має вже використовуватися в purchase/sale організації. GOODS refund backend рахує зі знижкою source sale, SERVICE приймає частковий RefundAmount або підставляє залишок; Quantity=0 дозволено для фінансової компенсації послуги. POST/UNPOST атомарні й idempotent, номер CR зберігається. Перевищення лімітів → `RETURN_QUANTITY_EXCEEDED` / `RETURN_AMOUNT_EXCEEDED`; активний return блокує Sale UNPOST → `DOCUMENT_HAS_DEPENDENCIES`. UNPOST return не може забрати проданий/зарезервований прихід → `INSUFFICIENT_STOCK`. Приклади — у docs/scenarios.md.

Доступні `POST/GET /products`, `GET/PUT /products/{id}`, `POST /purchases`, `GET/PUT /purchases/{id}`, `POST /purchases/{id}/post`, `POST /purchases/{id}/unpost`, `GET /stock/balances`.

Продажі: `POST /sales`, `GET/PUT/DELETE /sales/{id}`, `POST /sales/{id}/post`, `POST /sales/{id}/unpost`. Вибір партій: `GET /stock/available-batches?productId={id}&storageLocationId={id}` — лише партії з AvailableQuantity > 0, найстаріші за датою закупівлі першими.

Ціни: `POST/GET /price-types`, `GET/PUT /price-types/{id}`, `GET/PUT /batches/{batchId}/prices/{priceTypeId}` для GOODS, `GET/PUT /products/{productId}/prices/{priceTypeId}` для SERVICE. PriceType body — `{ "name": "Роздрібна", "defaultMarkupPercent": 25, "active": true, "sortOrder": 0 }`, price body — `{ "unitPrice": 100 }`. DefaultMarkupPercent — невід’ємний numeric(7,4). Список типів цін сортується за SortOrder. Усі ціни UAH, невід’ємні, numeric(20,4). PUT ціни створює або оновлює один складений ключ.

У POST/PUT sale передавайте `priceTypeId` і за потреби `lines[].unitPrice`; для нового рядка пропущена/null ціна дорівнює рекомендованій. `recommendedUnitPrice` встановлює backend з BatchPrice/ProductPrice і зберігає як snapshot. Зміна прайсу та звичайний PUT не переоцінюють наявний рядок; пропущений UnitPrice такого рядка зберігає фактичну ціну. Нова рекомендація береться для нового рядка або явної зміни Product/Batch/PriceType. Вибраний тип без ціни нового вибору дає `PRICE_NOT_FOUND` (400). Nullable PriceTypeId зберігає ручний режим із рекомендацією 0. Нижча фактична ціна потребує permission `sale.change_price`; рівна/вища — ні. `ISalePricePermissionProvider` отримує це право з активних ролей поточного користувача. Без права відповідь — `PRICE_BELOW_RECOMMENDED` (403), також при POST збереженого документа. JSON/заголовки не надають прав.

`v_batch_cost` обчислює поточний UnitCostUah із ціни закупівлі × (1 - знижка / 100) × курс плюс частка POSTED direct Expenses на одиницю. Частка пропорційна вартості активних рядків закупівлі; кожна витрата використовує власний курс. Собівартість не зберігається в таблицях. Під час створення Batch при POST закупівлі початкові BatchPrice всіх активних PriceType = cost × (1 + default markup / 100), із кінцевим округленням до 4 знаків. UNPOST зберігає ціни; повторний POST та зміни закупівлі/курсу/націнки/витрат старих цін не переписують.

Контрагенти: `POST/GET /counterparties`, `GET/PUT /counterparties/{id}`. Мінімальний body — `{ "name": "Партнер", "isCustomer": true, "isSupplier": true }`. Фільтри списку: `isCustomer`, `isSupplier`, `isEmployee`, `isSystem`, `active`, включно з false, та `search` за Name/Phone/Email/TaxId/RegistrationCode. Nullable DefaultPriceTypeId має належати поточній організації. Purchase/Sale використовують лише існуючих контрагентів своєї організації. Frontend `/counterparties` має список, пошук/фільтри, create/detail/edit та взаєморозрахунки: [docs/counterparties-ui.md](docs/counterparties-ui.md). Системного «Роздрібного покупця» автоматично не створюємо; DELETE endpoint відсутній, фізичне видалення IsSystem захищене доменом і базою.

Взаєморозрахунки: `GET /settlements/balances?counterpartyId={id}&currencyId={id}` (обидва фільтри optional), `GET /settlements/counterparties/{id}` (balances по валютах і chronological movements із BalanceAfterMovement). Purchase POST створює один негативний SettlementMovement після header discount, Sale POST — один позитивний для GOODS + SERVICE. OPENING_STOCK settlement не створює. UNPOST видаляє рух, повторний POST не дублює. UAH GUID для sale: `00000000-0000-0000-0000-000000000980`. Суми округлюються лише наприкінці до 4 знаків, BaseAmountUah закупівлі = Amount × ExchangeRate. Баланс = SUM(Amount) через `v_counterparty_balance`; running balance обчислюється SQL window SUM окремо для кожної валюти. Balance/debt/advance/paid/status та line/document totals у таблицях відсутні.

Гроші: `POST/GET /money/accounts`, `GET/PUT /money/accounts/{id}`, `GET /money/balances`. Для кожного виду `receipts`, `payments`, `transfers`: `POST /money/{kind}`, `GET/PUT /money/{kind}/{id}`, `POST /money/{kind}/{id}/post`, `POST /money/{kind}/{id}/unpost`. Тип операції задає маршрут і Document.DocumentType, окремого operation_type немає. Рахунок має одну валюту; використана валюта незмінна. DRAFT не створює рухів. POST і UNPOST атомарні та ідемпотентні; номер зберігається після UNPOST. Мінус дозволений завжди, перевірки достатності коштів немає. Баланс — тільки `v_money_account_balance` через SUM(Amount).

Receipt створює плюс на DestinationAccount, payment — мінус на SourceAccount, transfer — обидва рухи. Переказ однієї валюти потребує рівних сум, різних — може мати різні суми без збереженого effective rate. SETTLEMENT receipt зменшує settlement баланс (мінус), payment збільшує (плюс); використовуються окремі SettlementCurrencyId/Amount/ExchangeRate. UAH rate = 1 (пропущений підставляється, інший відхиляється). OWNER, OPENING_BALANCE, null Purpose та transfer settlement не створюють. UI, PaymentAllocation та прив’язка оплат до invoices відсутні; приклади — у docs/scenarios.md.

Витрати: `POST/GET /expense-categories`, `GET/PUT /expense-categories/{id}` — дерево без циклів; `POST/GET /expenses`, `GET/PUT/DELETE /expenses/{id}`, `POST /expenses/{id}/post|unpost`. Фільтри списку: `from`, `to` (включно), `counterpartyId`, `categoryId`, `purchaseInvoiceId`, `status`, `currencyId`. PUT потребує Version. POST створює негативний settlement без MoneyMovement; оплата — окремий SETTLEMENT payment. Текст у Document.Comment/InternalNote. Direct Expense має PurchaseInvoiceId на POSTED PURCHASE_INVOICE своєї організації; нульова вартість → `INVALID_PURCHASE_EXPENSE_ALLOCATION`. Невилучений direct Expense, також DRAFT, блокує UNPOST закупівлі → `DOCUMENT_HAS_DEPENDENCIES`. DELETE лише логічний і лише DRAFT. POST/UNPOST/редагування direct Expense змінюють поточний cost і можуть перерахувати історичний gross profit; BatchPrice зберігається.

Усі бізнес-API потребують Identity cookie й відповідного permission. `X-Organization-Id` та `X-Actor-Id` ігноруються в усіх середовищах. Організація та автор визначаються тільки автентифікованим користувачем. Без сесії API повертає 401, без права — 403.

Для локального admin перед запуском у Development задайте пароль через середовище (не комітьте його):

```sh
read -rs -p 'Development admin password (12+ characters, mixed case, digit, symbol): ' DevelopmentSeed__AdminPassword
export DevelopmentSeed__AdminPassword
dotnet run --project backend/Trade.Api --launch-profile http
```

Seed активується лише у Development за наявності `DevelopmentSeed__AdminPassword`; створює `admin`, demo OrganizationId `00000000-0000-0000-0000-000000000001`, сім стартових ролей та всі права OWNER/ADMIN. Організація — GUID-контекст без нової таблиці довідника. Пароль існуючого admin seed не змінює. Інші стартові ролі мають порожні permissions до налаштування. У Production/Staging seed не працює навіть із заданим паролем.

API: `GET /auth/csrf`, `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`; керування — `/admin/users` (`users.manage`) та `/admin/roles` (`roles.manage`). Cookie flow, DTO й матриця доступу: [docs/authentication.md](docs/authentication.md). Для JSON write-запитів отримайте CSRF token через `/auth/csrf`, зберігайте cookie та передавайте `X-CSRF-TOKEN`; після login отримайте новий token. `GET /write-offs/costs` читає поточну собівартість POSTED списань своєї організації та потребує `cost.view`.

Для закупівлі DRAFT не створює партій/рухів; POST атомарно створює або використовує Batch та записує позитивний StockMovement і негативний SettlementMovement (крім OPENING_STOCK). Повторний POST не змінює проведений документ. UNPOST видаляє stock/settlement рухи; номер, партії та їх ціни зберігаються. PUT закупівлі потребує поточної `version` і передає повний список активних рядків, зберігаючи ID наявних рядків.

Продажі ведуться лише в UAH. GOODS потребує Batch саме цього Product; SERVICE має BatchId = null. DRAFT sale резервує товар активними рядками та не створює рухів. POST записує негативні рухи GOODS і прибирає резерв документа. UNPOST повертає фізичний товар і резерв DRAFT. DELETE логічно видаляє лише DRAFT та звільняє резерв. PUT sale також потребує поточної `version`; власний резерв не блокує редагування.

`v_stock_balance` повертає BatchId, StorageLocationId, PhysicalQuantity, ReservedQuantity, AvailableQuantity. Резерв враховує лише активні GOODS-рядки не видалених DRAFT sales; available = physical - reserved. Недостатній stock повертає HTTP 409 Problem Details із `code: "INSUFFICIENT_STOCK"`. UNPOST закупівлі теж відхиляється, якщо він забере вже проданий/зарезервований stock. Це перевірка поточного залишку; backdated історичної перевірки немає.

JSON-контракти та сценарії — у [docs/scenarios.md](docs/scenarios.md), точні інваріанти — у [docs/domain-rules.md](docs/domain-rules.md).

## Перевірки

```sh
dotnet build backend/Trade.slnx --no-restore
dotnet test backend/Trade.slnx --no-restore
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
docker compose --profile app config --quiet
```

Integration tests запускають PostgreSQL 18 через Testcontainers; кожен тест має окрему базу з реальними міграціями. Охоплено всі slices, DRAFT/POST/UNPOST/DELETE, конкурентні резерви/редагування/POST, SERVICE/Batch-інваріанти, ціни обох типів, доменні дозволи, захист від підміни рекомендації, concurrent price upsert, rollback, tenant isolation, view та SQL-схему. Після кожного тесту очищується саме його Npgsql pool. Локальна dev-база не потрібна. Потрібен Docker; тести не пропускаються при його відсутності. NuGet `packages.lock.json` і npm `package-lock.json` зберігаються в Git.

## Docker Compose + Caddy

```sh
docker compose --profile app build
docker compose up -d --wait postgres
docker compose --profile app run --rm --no-deps api --migrate
docker compose --profile app up -d --wait
curl --fail http://localhost/health
docker compose --profile app logs api caddy
```

Caddy віддає зібраний frontend і проксіює `/auth/*`, `/admin/*`, `/health`, `/products`, `/purchases`, `/sales`, `/stock/*`, `/price-types`, `/batches/*`, `/counterparties`, `/settlements/*`, `/money/*`, `/expense-categories`, `/expense-categories/*`, `/expenses`, `/expenses/*` до API. API не публікує порт назовні, PostgreSQL доступний на host лише через loopback. Режим `:80` призначений для локальної перевірки. Для розгортання задайте в `.env` унікальний `POSTGRES_PASSWORD`, `CADDY_SITE_ADDRESS=trade.example.com`, налаштуйте DNS на сервер та відкрийте порти 80/443: Caddy отримає TLS-сертифікат автоматично. Cookie в Production мають Secure і потребують HTTPS. Образи й залежності можна збирати без локальних .NET/Node. Caddy також обробляє `/api/*` зі зняттям prefix; HTML document requests отримують SPA. API приймає original HTTPS scheme лише від довірених proxy (Compose: `REVERSE_PROXY_KNOWN_NETWORK`, default 172.16.0.0/12). Production потребує HTTPS для cookie-сесій; development seed у ньому вимкнений. Production bootstrap користувачів не входить до цього slice.

Compose зберігає PostgreSQL, Caddy certificates/config і ASP.NET Data Protection keys у named volumes. У production обмежте доступ до секретів і резервних копій цих volumes; ключі Data Protection у volume не зашифровані окремим key-encryption key.

Якщо середовище використовує HTTPS proxy із власним CA, передайте його довірений публічний сертифікат як BuildKit secret; TLS-перевірка залишається увімкненою. Сертифікат використовується лише на етапі збірки, не потрапляє до runtime image:

```sh
docker build --secret id=proxy_ca,src=/path/to/trusted-proxy-ca.crt -f backend/Trade.Api/Dockerfile -t trade-system-api .
docker build --secret id=proxy_ca,src=/path/to/trusted-proxy-ca.crt -f frontend/Dockerfile -t trade-system-caddy .
docker compose up -d --wait postgres
docker compose --profile app run --rm --no-deps api --migrate
docker compose --profile app up -d --no-build --wait
```

Якщо hostname проксі відомий лише host-машині, Docker-збірці також потрібні `--add-host PROXY_HOST:PROXY_IP` та `--build-arg HTTP_PROXY --build-arg HTTPS_PROXY --build-arg NO_PROXY`. Значення беруться з уже налаштованого середовища; не записуйте proxy credentials у Dockerfile. Внутрішні запити Caddy до API виключені з проксі через `NO_PROXY` у Compose.

```sh
docker compose --profile app down
```

Ця команда зберігає дані. `down -v` видаляє volumes і дані — використовуйте лише для свідомого скидання локального середовища.

## Межі slice

IdentityCore/UserManager/SignInManager працюють через мінімальний EF store у спільному DbContext. Додано тільки `users`, `roles`, `user_roles`, `permissions`, `role_permissions`; без таблиць claims/external logins/tokens/MFA, яких V1 не використовує. Cookie HttpOnly, SameSite=Lax, Secure поза Development; mutating endpoints перевіряють antiforgery token. Logout анулює security stamp і всі сесії користувача. Права активних ролей обчислюються на кожний запит без збереження effective permissions у User або cookie.

React Hook Form і Zod підготовлені для наступних форм; штучних бізнес-форм немає. Zod уже перевіряє відповідь health endpoint. Архітектура, обмеження та сценарії описані в `docs/`; правила для подальшої роботи — у `AGENTS.md`.

Opening settlement UI, MFA/SSO/LDAP, password reset email, historical stock validation, currency revaluation report, printed forms, бізнес-UI, PaymentAllocation, Obligation та Advance tables не реалізовані. Довідники, крім Unit, PriceType, Counterparty, MoneyAccount і ExpenseCategory, поки передаються як зовнішні GUID без додаткових таблиць. Рухи й партії не містять дубльованих ProductId/OrganizationId, дата руху береться з Document.
