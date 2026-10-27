# Архітектура

## Modular monolith

Один ASP.NET Core процес, одна PostgreSQL база, окремий React frontend. Реалізовано номенклатуру/закупівлю/партії/прихід, продажі/резерви/списання, pricing, контрагентів/взаєморозрахунки, гроші та операційні/прямі закупівельні витрати. Окремих сервісів, CQRS framework, брокерів і кешів немає.

| Проєкт | Прямі project references | Відповідальність |
| --- | --- | --- |
| Trade.Domain | — | Сутності, інваріанти, переходи стану |
| Trade.Application | Trade.Domain | Контракти API, Product/Purchase/Sale/Pricing/Counterparty/Settlement/Money/Expense/CustomerReturn/SupplierReturn/StockTransfer/WriteOff services, stock rules, порти ITradeStore та ISalePricePermissionProvider |
| Trade.Infrastructure | Trade.Application | EF/Npgsql, міграції, view, адаптер EfTradeStore, health |
| Trade.Api | Trade.Application, Trade.Infrastructure | Composition root, HTTP, контекст запиту, Problem Details, Identity cookies, OpenAPI, Serilog |
| Trade.Tests | Trade.Api | API / persistence integration tests на PostgreSQL 18 |

## Persistence та транзакції

TradeDbContext має двадцять сім бізнес-таблиць: products, documents, purchase_invoices, purchase_invoice_lines, batches, stock_movements, sale_invoices, sale_invoice_lines, price_types, batch_prices, product_prices, counterparties, settlement_movements, money_accounts, money_documents, money_movements, expense_categories, expenses, customer_returns, customer_return_lines, supplier_returns, supplier_return_lines, stock_transfers, stock_transfer_lines, write_off_reasons, write_offs, write_off_lines. Друга міграція додає sale, третя — pricing, четверта — націнку/активність/порядок PriceType та v_batch_cost, п’ята — counterparties/settlements і FK на існуючі CounterpartyId. Шоста додає три money-таблиці та v_money_account_balance. Сьома додає дві expense-таблиці, EXPENSE DocumentType та розширює v_batch_cost. Восьма додає дві customer return-таблиці та CUSTOMER_RETURN DocumentType. Дев’ята додає тільки supplier_returns/supplier_return_lines, SUPPLIER_RETURN DocumentType, перевірки посилань/залежностей і розширює v_stock_balance спільними резервами. Десята додає тільки дві stock transfer-таблиці, STOCK_TRANSFER DocumentType, tenant/uniqueness constraints та резерви з FromLocationId у v_stock_balance. Одинадцята додає три write-off таблиці, WRITE_OFF DocumentType, спільні резерви та звичайний v_write_off_cost. Нових колонок у спільному Document, партіях, stock або settlement journal немає. Чотири звичайні VIEW відображені через keyless ToView; п’ятий v_write_off_cost — SQL report helper без окремої EF entity. Жоден VIEW не матеріалізований, додаткові таблиці/кеші для них не створюються. StockReservation немає. Дванадцята міграція додає тільки users, roles, user_roles, permissions, role_permissions та tenant/deletion constraints; бізнес-таблиці й VIEW не змінює.

PurchaseInvoice використовує DocumentId як PK/FK до Document. Batch має унікальний FK PurchaseInvoiceLineId. Видалення пов’язаних сутностей обмежено FK. Sale DELETE залишається логічним; Counterparty DELETE endpoint відсутній.

Кожна write-операція працює в одній READ COMMITTED транзакції. PUT / POST / UNPOST / DELETE спочатку блокують існуючий Document через SELECT FOR UPDATE. POST перевіряє стан під блокуванням: POSTED означає успішний replay, DRAFT проходить валідацію і створює рухи. SaveChanges та commit відбуваються до повернення HTTP-відповіді. Помилка будь-якого запису відкочує всі зміни документа та ledger.

Товари закупівлі блокуються FOR SHARE у порядку ID. Оновлення Product використовує FOR UPDATE, тому перевірка GOODS не може змагатися з переходом Product у SERVICE. Version також налаштовано як EF concurrency token; PUT порівнює версію клієнта під блокуванням. GET закупівлі читає Document і рядки одним SQL-запитом.

Звичайний SQL VIEW об’єднує physical = SUM(StockMovement.Quantity) і reserved = SUM(active DRAFT sale lines + DRAFT SupplierReturn lines + DRAFT StockTransfer lines + DRAFT WriteOff lines) через FULL OUTER JOIN за BatchId / StorageLocationId; available = physical - reserved. Резерв — лише невилучені DRAFT SALE_INVOICE GOODS-рядки та невилучені DRAFT SUPPLIER_RETURN/STOCK_TRANSFER/WRITE_OFF рядки. Нічого не кешується. OrganizationId/ProductId/MovementDate не дублюються у рухах і партіях.

Stock/reserve write-операції додатково блокують рядки Batch через FOR UPDATE OF b, завжди за зростанням ID. Це спільне блокування для create/edit/post/unpost/delete sales та post/unpost purchases. Воно працює навіть без рядка у view та консервативно серіалізує різні storage locations одного Batch. Стан stock читається окремим SQL-запитом після отримання блокування: READ COMMITTED бачить commit попереднього writer. PUT/POST виключає з reserved лише власний збережений DRAFT, не резерви інших sale, SupplierReturn, StockTransfer або WriteOff документів.

UNPOST sale перевіряє projected physical після видалення його негативних рухів та projected reserved після повернення у DRAFT. UNPOST purchase не може забрати проданий або зарезервований прихід. Обидві перевірки обмежені поточним залишком, без історичної реконструкції.

PostgreSQL trigger першого slice захищає ProductId закупівельного рядка після появи Batch. Новий deferred constraint trigger забороняє дубль активного Batch у sale на commit; він дозволяє атомарно поміняти Batch між двома рядками під час PUT. Існуючі FK та унікальність Batch/рухів збережені.

## HTTP та контекст

Канонічні маршрути: /products, /purchases, /sales, /stock/balances, /stock/available-batches, /price-types, /batches/{id}/prices/{priceTypeId}, /products/{id}/prices/{priceTypeId}, /counterparties, /settlements/balances, /settlements/counterparties/{id}, /money/accounts, /money/receipts, /money/payments, /money/transfers, /money/balances, /expense-categories, /expenses. Vite і Caddy проксіюють їх до API. /health залишається публічним, перевіряє PostgreSQL. У Development доступні /openapi/v1.json і /swagger з описом X-CSRF-TOKEN для write-запитів.

Створення повертає 201, читання/оновлення/переходи — 200, logical DELETE sale/expense — 204. Бізнес-валідація — 400 Problem Details, відсутній ресурс у поточній організації — 404, stale Version / unique conflict — 409. Недостатній stock — 409 із code = INSUFFICIENT_STOCK. Відсутня автентифікація у Production — 401. Продажі завжди UAH, без CurrencyId/ExchangeRate чи currency logic.

Контекст визначається з перевіреної Identity cookie (NameIdentifier та organization_id). X-Organization-Id і X-Actor-Id ігноруються в усіх середовищах. Користувач належить одній Organization; claims створює сервер, кожний запит перевіряє Active, SecurityStamp та OrganizationId у БД. Test authentication scheme живе тільки в Trade.Tests.

IdentityCore/UserManager/SignInManager використовують мінімальний EF user store: паролі, security stamp, lockout, role membership; тільки п’ять auth-таблиць без невикористаних зовнішніх login/claim/token таблиць. IdentityRole має tenant-scoped унікальне normalized name; Username глобально унікальний для однозначного login без вибору організації. Централізований permission service на кожний запит обчислює union активних ролей. Cookie не містить ролей чи permissions. Dynamic authorization policies і централізована mapping захищають усі бізнес-API; невідома route mapping завершує startup помилкою.

Cookie HttpOnly, SameSite=Lax, Secure поза Development; JSON unsafe endpoints перевіряють ASP.NET antiforgery. GET /auth/csrf повертає request token, cookie залишається HttpOnly; після login потрібен новий token. Logout змінює security stamp, відкликаючи всі сесії. Production bootstrap не додано. Frontend login та application shell описані в [frontend.md](frontend.md). Development seed opt-in через конфігурацію пароля, без таблиці Organization. Докладно: [authentication.md](authentication.md).

## Pricing та доменний дозвіл

PriceType містить Id, OrganizationId, Name, DefaultMarkupPercent (numeric(7,4), >= 0), Active і SortOrder. Ціни зберігаються за складеними ключами (BatchId, PriceTypeId) та (ProductId, PriceTypeId), без зайвих ID чи дублювання Product/Organization у BatchPrice. ProductPrice дозволений тільки для SERVICE. Product row lock захищає від одночасної зміни ItemType.

SaleLineRequest містить nullable UnitPrice і не містить RecommendedUnitPrice. Application визначає рекомендації пакетними запитами лише для нового рядка або явного нового вибору Product/Batch/PriceType. Незмінений вибір використовує збережений snapshot, також при PUT. Пропущений UnitPrice нового рядка дорівнює рекомендації, наявного — зберігає фактичну ціну. Domain перевіряє фактичну ціну та SalePricePermissions. Дозвіл потрібний лише нижче рекомендації. ISalePricePermissionProvider отримує capability з sale.change_price поточного користувача. Це authorization policy, а не постійна доменна заборона; body/headers не можуть надати право. Нижча ціна дає 403 / PRICE_BELOW_RECOMMENDED, відсутня ціна нового вибору — 400 / PRICE_NOT_FOUND. PriceTypeId = null залишає ручний режим із рекомендованою ціною 0.

PUT price блокує Product або Batch, потім PriceType FOR UPDATE: паралельні upsert відсутнього ключа серіалізуються. Sale create/edit після своїх Product/Batch locks бере PriceType FOR SHARE до commit, після цього читає джерела нових рекомендацій. Порядок Product → Batch → PriceType уникає інверсії блокувань і забезпечує узгоджений знімок рекомендацій у документі. Оновлення довідника, звичайний PUT, POST/UNPOST/re-POST не переписують попередні знімки.

v_batch_cost обчислює поточний UnitCostUah через JOIN Batch → PurchaseInvoiceLine → PurchaseInvoice: UnitPriceCurrency × (1 - DiscountPercent / 100) × ExchangeRate плюс пропорційна частка POSTED direct Expenses на одиницю, без проміжного округлення. Він не матеріалізується, не фільтрує збережені Batch після UNPOST та не дублює собівартість у таблицях. Розподіл описано в секції витрат нижче.

PurchaseService передає адаптеру лише IDs новостворених Batch для початкового ціноутворення. Адаптер після Batch locks бере впорядковані PriceType FOR SHARE, фіксує набір активних типів і виконує SaveChanges у тій самій відкритій транзакції, щоб VIEW побачив нові Batch. INSERT SELECT із v_batch_cost обчислює ціни з націнкою та округлює тільки результат до numeric(20,4); ON CONFLICT DO NOTHING не перезаписує наявну ціну. Зовнішня транзакція комітить документ/Batch/рухи/ціни разом; помилка ціни відкочує й попередній SaveChanges. Повторний POST старих Batch цей крок не запускає, UNPOST ціни не видаляє. Нові рядки після UNPOST отримують початкові ціни лише для своїх нових Batch.

## Контрагенти та settlement journal

Counterparty має рівно погоджені 16 полів; Organization визначає контекст, DefaultPriceType має бути в тій самій організації. Purchase/Sale перед записом беруть Counterparty FOR SHARE, потім збережений порядок Product → Batch → PriceType. Counterparty PUT бере Counterparty FOR UPDATE, потім DefaultPriceType FOR SHARE. Обернених захоплень цих locks немає. Domain/EF захищають видалення IsSystem; PostgreSQL trigger захищає також прямий SQL. Системних контрагентів чи retail bootstrap не створено.

SettlementMovement має шість полів, FK до Document/Counterparty, numeric(20,4) суми та UNIQUE(DocumentId). У Domain постинг закупівлі обчислює мінусову суму активних рядків після знижки; продажу — плюсову суму GOODS + SERVICE за фактичним UnitPrice. OPENING_STOCK пропускається. Масштабовані BigInteger зберігають точність добутків/суми до фінального округлення, не додаючи залежностей чи persisted totals. BaseAmountUah = Amount × ExchangeRate для закупівлі; у продажу Amount = BaseAmountUah і CurrencyId = погоджений UAH GUID 00000000-0000-0000-0000-000000000980.

AddSettlementMovement входить у поточну POST-транзакцію. UNPOST видаляє journal разом зі stock або money movements; помилка будь-якого запису/видалення відкочує всі зміни документа та журналів. POSTED replay повертає стан до нових записів. Прив’язка платежів до invoices чи окремі таблиці зобов’язань не додаються.

v_counterparty_balance групує SUM(Amount) за Counterparty/Currency без кешів. Деталізація читає документні дату/тип через JOIN, а BalanceAfterMovement — через SUM OVER PARTITION BY CurrencyId у порядку DocumentDate/CreatedAt/DocumentId/MovementId. Обидва набори endpoint деталізації читає в одному REPEATABLE READ snapshot, щоб баланс узгоджувався зі списком при конкурентних POST/UNPOST. API scope і DB triggers захищають організацію DefaultPriceType, invoice/Counterparty та settlement Document/Counterparty.

Після цього slice довільний зовнішній CounterpartyId більше не є валідним посиланням: потрібен існуючий профіль поточної організації. Міграція не створює контрагентів із вигаданими іменами для старих GUID; наявні зовнішні посилання в заповненій базі потребують імпорту реальних профілів перед увімкненням FK.

## Грошові документи та журнал

MoneyAccount має шість полів без balance/AllowNegativeBalance. MoneyDocument використовує DocumentId як PK/FK до спільного Document та десять погоджених полів; MONEY_RECEIPT/MONEY_PAYMENT/MONEY_TRANSFER визначають операцію, Purpose — лише її призначення. MoneyMovement має чотири поля, FK до MoneyDocument/Account і UNIQUE(DocumentId, MoneyAccountId). Currency/Organization/Date беруться через зв’язки, не дублюються.

MoneyService працює через той самий ITradeStore та транзакцію. POST/UNPOST блокують Document FOR UPDATE; посилання беруть Counterparty FOR SHARE, потім відсортовані Account FOR SHARE. Account PUT бере FOR UPDATE й не дозволяє змінити CurrencyId після посилання money document/movement, включно з DRAFT. MoneyModelConfiguration і EfTradeStore.Money тримають money mappings/queries окремо всередині тих самих шарів monolith.

MoneyDocument валідує сторони, positive numeric(20,4) суми, дві різні account для transfer і рівність сум для однакової валюти. Від’ємний баланс не перевіряється і не блокується; account locks захищають валюту/посилання, не ліміт коштів. PostgreSQL triggers додатково перевіряють документний тип/форму, організації, валюту використаного рахунку та account/amount руху.

SETTLEMENT receipt додає негативний SettlementMovement, payment — позитивний. BaseAmountUah використовує наявне точне множення/округлення до чотирьох знаків; валюта/сума settlement можуть відрізнятися від фактичної валюти/суми рахунку. UAH rate нормалізується з null у 1, явно інший відхиляється. OWNER/OPENING_BALANCE/null Purpose та transfer settlement не створюють. POST/UNPOST атомарні для money/settlement; transfer має два рухи в одній транзакції.

v_money_account_balance — звичайний SUM view за MoneyAccountId/валютою з account. Немає cached balance, дубльованої валюти в movement, persisted effective transfer rate або PaymentAllocation. У money POST/UNPOST не змінюються stock/price правила. Повторний POST не додає рухів, номер MR/MP/MT присвоюється лише вперше та зберігається після UNPOST. Операції прив’язані до власного маршруту й організації.

## Витрати та direct purchase cost

ExpenseCategory зберігає рівно п’ять погоджених полів; Expense — сім, із DocumentId PK/FK до спільного DocumentType EXPENSE. Amount — numeric(20,4), ExchangeRate — numeric(20,6), обидва > 0. Немає Description або allocation/cost таблиць. ExpenseService працює через той самий ITradeStore; mappings і queries винесені в ExpenseModelConfiguration та EfTradeStore.Expenses.

Category create/reparent бере pg_advisory_xact_lock(hashtextextended(OrganizationId, 72831)) до читання дерева й тримає до commit. Перевірка батьків проходить без обмеження глибини; DB trigger використовує той самий lock і recursive CTE для перевірки циклів. Посилання — лише всередині організації.

Expense writes блокують власний Document FOR UPDATE, контрагента FOR SHARE і, для direct expense, Document закупівлі FOR SHARE до commit. Перевіряються POSTED PURCHASE_INVOICE, організація та ненульова точна вартість активних рядків після знижки; OPENING_STOCK заборонений. Purchase UNPOST вже тримає Document FOR UPDATE і перед stock changes перевіряє всі невилучені dependent Expenses, включно з DRAFT. Отже attach і UNPOST не можуть залишити Expense на DRAFT закупівлі; SQL trigger додатково захищає залежність. Помилки: INVALID_PURCHASE_EXPENSE_ALLOCATION (400), DOCUMENT_HAS_DEPENDENCIES (409).

POST Expense створює тільки негативний SettlementMovement, BaseAmountUah — Amount × власний курс із фінальним округленням до 4 знаків через наявний точний конвертер. UNPOST видаляє settlement і змінює стан в одній транзакції. POSTED replay не змінює номер/audit; EX-номер призначається лише вперше. PUT потребує Version/DRAFT, DELETE лише logical DRAFT, список відсікає deleted та підтримує всі погоджені фільтри. MoneyMovement не створюється: платіж — окремий MoneyPayment SETTLEMENT.

v_batch_cost використовує CTE активних line totals та SUM(Expense.Amount × Expense.ExchangeRate) для POSTED невилучених direct Expenses. LineBaseUah = Quantity × базова собівартість одиниці. Розподіл за LineBaseUah / загальна вартість закупівлі; для unit cost Quantity скорочується: unit_base + unit_base × expense_total / purchase_total. Це зберігає точність базового множення та не округлює окремі allocations. Removed purchase lines не беруть участі в знаменнику й не отримують частки. General/DRAFT/deleted Expenses на cost не впливають. Cost змінюється також після sale або UNPOST/редагування/re-POST Expense; історичний gross profit може перерахуватися. Збережені BatchPrice і sale recommendations не переписуються.

## CustomerReturn

CustomerReturnService, EfTradeStore.CustomerReturns та CustomerReturnModelConfiguration залишаються у поточних шарах modular monolith. Header має три поля, рядок — п’ять; жодних Counterparty/Product/Batch/UnitPrice copies або cached limits. Рядки DRAFT замінюються через PUT без додаткового RemovedAt; DELETE самого документа логічний і зберігає його рядки. UNIQUE(CustomerReturnId, SourceSaleLineId) deferred дозволяє атомарно поміняти source references між двома рядками. Deferred trigger перевіряє належність рядків поточному source sale на commit.

SourceSaleInvoice має бути POSTED і належати поточній організації. За погодженим тимчасовим правилом StorageLocation перевіряється за використанням у purchase/sale цієї організації, без нової таблиці. PostgreSQL triggers захищають header scope та забороняють Sale UNPOST за невилучених dependent returns, також DRAFT. DocumentHasDependenciesException має загальний текст про залежні документи.

Write операції спочатку беруть власний Document FOR UPDATE, потім SourceSale Document FOR UPDATE, Product FOR SHARE, Batch FOR UPDATE за ID. Source lock серіалізує POST/UNPOST різних returns одного продажу; після нього query читає SUM quantity/refund лише POSTED невилучених returns. DRAFT лімітів не резервує. При зміні source через PUT старий і новий продажі блокуються за ID. Returnable-lines використовує той самий source lock для узгодженого читання source і агрегатів; derived поля не зберігаються.

GOODS використовує original UnitPrice/DiscountPercent та створює позитивний StockMovement до original Batch у вибраному складі. SERVICE дає часткову фінансову компенсацію без Batch/stock; quantity може бути нульовою, фінансовий ліміт — залишок line net. Refund goods округлюється до 4 знаків, остання кількість отримує залишок line net; часткове округлення обмежується залишком, щоб не переплатити. POST повторно перевіряє ліміти й розраховує goods refund перед створенням рухів. Сума всіх refunds дає один негативний UAH SettlementMovement; MoneyMovement не створюється. UNPOST перевіряє поточні stock/reservations під Batch locks через наявний StockRules, потім атомарно видаляє обидва журнали. Нових cost/price правил або backdated validation немає. POSTED replay не змінює audit/Version/номер; CR-номер присвоюється лише вперше.

## SupplierReturn та спільні резерви

SupplierReturnService працює через ITradeStore, mappings/queries — у SupplierReturnModelConfiguration та EfTradeStore.SupplierReturns. Header має лише три поля, рядок — чотири, без Product/Batch/Counterparty/цінових копій. FK ведуть до source purchase/line; зв’язки source/Batch/організацій та залежність Purchase UNPOST захищені application validation і PostgreSQL triggers. DRAFT lines замінюються без додаткового RemovedAt, DELETE документа логічний. Маршрути /supplier-returns, /purchases/{id}/supplier-return та /purchases/{id}/returnable-lines проксіюються Vite/Caddy.

v_stock_balance лишається звичайним VIEW: physical = SUM(stock movements); reserve_lines через UNION ALL поєднує активні GOODS DRAFT sale lines і DRAFT SupplierReturn lines, Batch виводиться із SourcePurchaseLine. Далі SUM за Batch/StorageLocation; available = physical - reserved. Deleted документи й removed sale/source lines не резервують. GetStockAsync віднімає власний reserve лише одного Sale/SupplierReturn документа за переданим ID. Старі sale/CustomerReturn/stock правила використовують цей самий view без окремого механізму резервів.

Source Purchase Document FOR UPDATE серіалізує create/edit/post/unpost та query лімітів одного invoice. Власний SupplierReturn Document блокують FOR UPDATE, source Product — FOR SHARE, Batch — FOR UPDATE за ID. PUT змінює parent locks за ID і бере union old/new batches, щоб безпечно звільнити попередній reserve. Загальний StockRules отримав overload для demand dictionary; старий Sale overload делегує йому з тією самою семантикою. UNPOST перевіряє projected physical після видалення negative movements і projected reserved після відновлення DRAFT, атомарно видаляє обидва журнали. Dependency check Purchase UNPOST враховує також DRAFT SupplierReturn.

Settlement refund рахується точним DocumentAmount із original quantity × unit price після header discount, без проміжного округлення рядків; кінцевий Amount — 4 знаки, BaseAmountUah використовує курс source. Direct Expenses/v_batch_cost до refund не залучаються. Current cost/BatchPrice не змінюються. Number SR призначається лише вперше; повторний POST без змін audit/Version. Returnable-lines читає source, SUM POSTED returns та stock під source/Batch locks, optional storageLocationId має належати організації за погодженим адаптером. MaxReturnableQuantity = MIN(залишок purchase limit, available).

## StockTransfer

StockTransferService та EfTradeStore.StockTransfers використовують той самий ITradeStore й StockRules без нових financial/cost правил. Header має три поля, рядок п’ять; Product/Organization не копіюються. Location scope перевіряється за погодженим використанням у purchase/sale організації; Batch scope — через source purchase. EF/FK та PostgreSQL triggers додатково захищають scope, quantity і різні локації; deferred uniqueness дозволяє атомарні перестановки Batch/LineNo. DRAFT PUT замінює рядки без додаткового RemovedAt, logical DELETE зберігає їх.

v_stock_balance отримав третій UNION ALL у reserve_lines: невилучені DRAFT StockTransfer lines із StorageLocationId = FromLocationId. Physical як і раніше рахується лише з SUM stock_movements. GetStockAsync за excludeDocumentId віднімає тільки власний saved reserve sale/SupplierReturn/StockTransfer, включно з попередніми Batch/From при PUT. Available batches використовує той самий view без окремого алгоритму або кешу.

Create/PUT/POST/UNPOST/DELETE беруть впорядковані Batch FOR UPDATE; наявний transfer спочатку бере свій Document FOR UPDATE. PUT блокує union old/new Batch IDs. Source document/Product locks не додаються: transfer не змінює purchase/Batch/Product і не захоплює їх після Batch locks. За READ COMMITTED stock query після locks бачить останній commit іншого writer; спільні Batch locks серіалізують transfers, sales, supplier/customer returns і purchase UNPOST.

POST повторно перевіряє available/physical і створює два StockMovement одного Batch із Quantity -/+ на From/To. Фінансові журнали не використовуються. UNPOST передає обидва напрямки в StockRules.RequireSafeRemoval разом із майбутнім DRAFT reserve лише у From. Таким чином destination receipt не можна забрати, якщо projected physical/available стає від’ємним; source issue повертається й одночасно резервується. Усі movement insert/delete та статус в одній транзакції; помилка будь-якого руху відкочує обидві сторони. ST-номер лише вперше, idempotent replay зберігає audit/Version. Жодних історичних stock snapshots або нового historical validation немає.

API: POST/GET /stock-transfers, GET/PUT/DELETE /stock-transfers/{id}, POST /stock-transfers/{id}/post|unpost. Фільтри списку — from/to (включно), fromLocationId, toLocationId, status; deleted відсікаються. Vite/Caddy маршрутизують новий prefix, OpenAPI містить DTO та CSRF header для unsafe operations.

## WriteOff

WriteOffService та EfTradeStore.WriteOffs використовують поточні ITradeStore/StockRules. WriteOffReason має п’ять полів, header три, рядок п’ять; FKs ведуть до ExpenseCategory/Reason/Document/Batch без cost/amount/Product/Organization copies. Reason CRUD scope перевіряється application і DB trigger; на посилання reason береться FOR SHARE, на його PUT — FOR UPDATE. Header scope перевіряє reason/склад, deferred line scope — Batch → source purchase organization. Deferred uniqueness дозволяє атомарно переставляти LineNo/Batch у DRAFT.

Документні writes беруть власний Document FOR UPDATE, reason FOR SHARE, далі впорядковані union old/new Batch locks. Reason PUT не захоплює Batch, тому не вводить зворотний порядок. v_stock_balance отримав четвертий UNION у reserve_lines для активних DRAFT WriteOff.StorageLocationId. GetStockAsync виключає лише власний saved reserve за excludeDocumentId для будь-якого з чотирьох outgoing типів; available-batches автоматично читає оновлений view.

POST створює тільки stock issue, без фінансових журналів/Expense/Batch. UNPOST перевіряє видалення negative movements і відновлення DRAFT reserve через StockRules.RequireSafeRemoval, потім видаляє рухи й змінює статус в одній транзакції. Number WO лише вперше, POSTED replay зберігає audit/Version. Logical DELETE DRAFT прибирає reserve та report row, але зберігає бізнес-рядки.

v_write_off_cost — окремий SQL report helper, не EF entity/table: JOIN header/document/reason, LEFT JOIN lines/v_batch_cost та SUM(quantity × current UnitCostUah). Повертає сім погоджених report fields, включно з двома ідентифікаторами shared-PK документа. TotalCostUah ніде не зберігається й не округлюється до invoice amount precision. Класифікація береться з поточного reason; late direct Expenses змінюють історичну cost без зміни write-off facts або BatchPrice. VIEW показує невилучені DRAFT/POSTED, caller scope — JOIN Document.OrganizationId, фактичний звіт — POSTED filter.

Міграція Down спочатку прибирає report view і відновлює попередній stock view, потім triggers/tables. Targeted pricing migration tests тимчасово прибирають dependent report view, round-trip старий pricing, відновлюють наявне direct-expense cost extension і report view, зберігаючи дані пізніших slices. Production pricing/expense logic не змінюється.

## Міграції і запуск

Пінований dotnet-ef зберігається в .config/dotnet-tools.json. Міграції застосовуються явно через dotnet ef database update або запуск API з --migrate. Звичайний запуск API не змінює схему автоматично; EnsureCreated не використовується. --migrate завершує процес після оновлення, що дозволяє запуск окремим Compose command перед API.

React 19 / MUI / TanStack Query / React Hook Form / Zod залишаються підготовленим frontend-стеком. Поточний екран перевіряє health; бізнес-екрани цей backend slice не додає.

## Перевірки та розгортання

Один PostgreSQL 18 Testcontainer використовується fixture; кожен тест отримує окрему базу та справжні EF-міграції. Лише test fixture створює звичайного контрагента для запитів; це не application seed. Після тесту закривається саме його EF/Npgsql pool. Перевіряються всі slices, money receipt/payment/transfer та negative balances, same/cross currency, settlement signs, rollback обох журналів і часткового transfer, конкурентний POST, CurrencyId protection, tenant isolation, точні колонки та відсутність cached полів. Повний Down/Up перевіряється на базі без документів; money-міграція також на базі з наявними purchase/stock/settlement даними. Pricing проходить цільовий round-trip без видалення новіших наповнених журналів. Model drift перевіряється окремо.

Compose запускає PostgreSQL; профіль app додає API та Caddy з frontend assets. API не публікує порт назовні. Внутрішній api upstream виключений з egress-проксі через NO_PROXY. Live-процеси не зберігаються у cloud snapshot; їх треба перезапускати. Data Protection keys та дані PostgreSQL зберігаються у named volumes.


Product UI slice додає триколонковий довідник units (Id, OrganizationId, Name) без seed та Category table. GET/POST /units обслуговують lookup/inline dialog; GET /products отримує search/itemType/active без зміни domain model. Відомі Unit іншої організації заборонені, старі зовнішні GUID лишаються сумісними без FK. Існуючі бізнес-таблиці, stock/cost/balance VIEW не змінені. Докладно: [products-ui.md](products-ui.md).
