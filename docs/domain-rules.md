# Доменні правила

## Номенклатура

- Product належить OrganizationId; Code унікальний у межах організації.
- ItemType має значення GOODS або SERVICE. Лише GOODS може бути рядком PurchaseInvoice.
- Товар, на який уже посилається рядок закупівлі (також вилучений), не можна перетворити на SERVICE.
- Attributes зберігається як nullable JSONB. Категорія, штрихкод та опис необов’язкові.
- Organization, Category, Unit, StorageLocation і Currency поки є зовнішніми GUID без окремих таблиць. Перевіряється непорожній ID; належність зовнішніх довідників організації буде перевірятися після їх реалізації. Counterparty та nullable PriceTypeId уже посилаються на реалізовані довідники поточної організації.
- ItemType товару, на який посилається будь-який рядок продажу, не можна змінювати. Це зберігає правило GOODS/Batch та SERVICE/no Batch для вже створених документів.

## Закупівля та документ

- Закупівлі використовують PURCHASE_INVOICE та OPENING_STOCK, продажі — SALE_INVOICE; спільні стани DRAFT / POSTED. SALE_INVOICE не приймається як тип закупівлі.
- CounterpartyId може бути null тільки для OPENING_STOCK.
- DRAFT не створює Batch або StockMovement. Порожній DRAFT дозволено зберегти, але провести його не можна.
- Quantity > 0; UnitPriceCurrency >= 0. Обидва поля — numeric(20,4); зайва точність відхиляється, а не округлюється непомітно.
- ExchangeRate > 0, numeric(20,8); DiscountPercent у межах 0–100, numeric(7,4).
- DocumentDate і ExternalDate — календарні дати без часу. CreatedAt / UpdatedAt / PostedAt / RemovedAt / Batch.CreatedAt — UTC із точністю PostgreSQL до мікросекунд.
- Номер призначається при першому успішному POST і зберігається після UNPOST. Формат PI-00000001 / OS-00000001 / SI-00000001 / MR-00000001 / MP-00000001 / MT-00000001 / EX-00000001 / CR-00000001 / SR-00000001 / ST-00000001 / WO-00000001 використовує спільну PostgreSQL sequence. Це не нумерація з одиниці для кожної організації; після відкату транзакції можливі пропуски.
- Version — bigint, починається з 1 та збільшується при редагуванні або переході стану. PUT потребує поточної Version; застаріла версія дає 409.
- POSTED не редагується через PUT. Спочатку потрібно виконати UNPOST.

## Batch і складський прихід

- Batch створюється лише при першому POST конкретного PurchaseInvoiceLine.
- UNIQUE(PurchaseInvoiceLineId) забороняє другу партію для того самого рядка.
- Різні рядки одного Product створюють різні Batch.
- Після створення Batch ProductId рядка не змінюється, також після UNPOST або відновлення вилученого рядка. Правило перевіряється доменом і PostgreSQL trigger.
- POST створює позитивний StockMovement для кожного активного рядка. Уся операція транзакційна.
- Повторний POST уже POSTED документа повертає поточний стан без нових Batch, рухів, номера чи змін audit/Version. Паралельні операції серіалізуються блокуванням рядка Document.
- UNPOST видаляє рухи документа, залишає Batch та номер, очищує PostedBy / PostedAt і повертає DRAFT. Повторний UNPOST для DRAFT нічого не змінює.
- Повторний POST використовує той самий Batch для того самого ID рядка і поточні Quantity / StorageLocationId.
- PUT передає повний список активних рядків: ID зберігається для існуючого рядка, null створює новий. Відсутні рядки отримують RemovedAt; вони та їх Batch не видаляються. Активні LineNo мають бути унікальними.
- Batch містить лише Id, PurchaseInvoiceLineId, BatchNumber, CreatedAt.
- StockMovement містить лише Id, DocumentId, BatchId, StorageLocationId, Quantity. Product та Organization визначаються зв’язками; дата руху — Document.DocumentDate.
- v_stock_balance — звичайний PostgreSQL VIEW. PhysicalQuantity = SUM(StockMovement.Quantity), ReservedQuantity — сума активних GOODS-рядків не видалених DRAFT SaleInvoice та рядків DRAFT SupplierReturn і StockTransfer (лише FromLocationId), а також DRAFT WriteOff; AvailableQuantity = PhysicalQuantity - ReservedQuantity. Результат групується за BatchId / StorageLocationId. Кешованих залишків немає. API обмежує результат організацією через зв’язки Batch → Line → Purchase → Document.
- UNPOST закупівлі тепер перевіряє поточний stock під блокуванням Batch: не можна забрати надходження, якщо це зробить physical або available від’ємним через продажі/резерви. Це необхідний захист спільного ledger після додавання другого slice; Batch і номер, як і раніше, зберігаються.

## Продажі, резерви та списання

- SaleInvoice містить тільки DocumentId, CounterpartyId, StorageLocationId, nullable PriceTypeId, DiscountPercent. Усі суми — UAH; валютних полів або перерахунку немає.
- SaleInvoiceLine містить Id, SaleInvoiceId, LineNo, ProductId, nullable BatchId, Quantity, RecommendedUnitPrice, UnitPrice, nullable RemovedAt.
- GOODS обов’язково має Batch саме цього Product і організації. SERVICE має BatchId = null, не резервує товар і не створює StockMovement.
- Quantity > 0; ціни невід’ємні; Quantity / RecommendedUnitPrice / UnitPrice — numeric(20,4). Зайва десяткова точність відхиляється. RecommendedUnitPrice є серверним знімком рекомендованої ціни під час збереження рядка.
- Один Batch може бути лише в одному активному рядку одного SaleInvoice. Різні продажі можуть резервувати той самий Batch, якщо stock достатній. Активні LineNo та ID рядків не дублюються.
- Порожній DRAFT можна зберегти, але не провести. DRAFT не створює рухів; reserve існує завдяки рядкам.
- Резерв враховує тільки DocumentType = SALE_INVOICE, Status = DRAFT, DeletedAt = null, RemovedAt рядка = null, Product.ItemType = GOODS та непорожній BatchId. StockReservation не створюється.
- Створення/редагування DRAFT перевіряє available для кожного Batch / StorageLocation. При PUT/POST від reserved віднімається лише поточний збережений резерв цього самого документа, у його поточному місці зберігання. Резерви інших sales залишаються врахованими.
- PUT передає поточну Version і повний список активних рядків; відсутні рядки логічно вилучаються через RemovedAt. Редагування може змінити Product/Batch у DRAFT, якщо нові зв’язки й залишки валідні.
- POST під повторною перевіркою physical та available створює негативний рух для кожного GOODS-рядка й переводить документ у POSTED. Резерв цього документа атомарно зникає. SERVICE рухів не створює.
- Повторний POST уже POSTED sale не створює рухів і не змінює номер, audit або Version.
- UNPOST видаляє рухи sale та повертає DRAFT: physical збільшується, рядки знову резервують товар. Перевіряється прогнозований поточний physical та available після видалення рухів і відновлення резервів. Невалідний результат відхиляється без часткових змін. Повторний UNPOST для DRAFT — no-op.
- Недостатній reserve/physical або небезпечний UNPOST повертає HTTP 409 Problem Details з code = INSUFFICIENT_STOCK.
- DELETE дозволений лише для DRAFT: встановлює DeletedBy / DeletedAt, збільшує Version і звільняє резерв. SaleInvoice/Line не видаляються фізично. POSTED спочатку потребує UNPOST. Видалені sale не читаються/редагуються/проводяться через API.
- Усі операції stock/reserve блокують Batch у впорядкованому порядку до commit; документ додатково блокується FOR UPDATE. Це захищає від конкурентного overreserve/oversell та гонки з UNPOST закупівлі.
- Перевіряється лише поточний залишок. Backdated DocumentDate не запускає історичної перевірки.
- available-batches повертає тільки AvailableQuantity > 0, сортує за PurchaseDocumentDate, Batch.CreatedAt та Batch.Id; SERVICE повертає порожній список.

## Типи цін і фактична ціна продажу

- PriceType містить Id, OrganizationId, Name, DefaultMarkupPercent, Active, SortOrder. DefaultMarkupPercent — numeric(7,4), >= 0 (до 999.9999). Список сортується за SortOrder, потім Name та Id. Для попередніх PriceType міграція встановлює націнку 0, Active = true, SortOrder = 0.
- BatchPrice — BatchId, PriceTypeId, UnitPrice; ProductPrice — ProductId, PriceTypeId, UnitPrice. Складені PK забороняють дубль ціни для тієї самої пари. Усі ціни — UAH, numeric(20,4), >= 0. Нуль є валідною налаштованою ціною.
- GOODS отримує ціну тільки з BatchPrice своєї партії та вибраного PriceType. SERVICE — тільки з ProductPrice свого Product і PriceType. ProductPrice для GOODS заборонений. SERVICE з ProductPrice не може змінити ItemType на GOODS.
- PriceType та Product/Batch мають належати поточній організації. OrganizationId не дублюється в цінах; для Batch Product і організація визначаються через рядок закупівлі та Document.
- POST/PUT sale не приймає RecommendedUnitPrice як довірене поле. Для нового рядка backend читає його з довідника; пропущений або null UnitPrice нового рядка дорівнює рекомендованій ціні. Для наявного рядка без зміни вибору пропущений/null UnitPrice зберігає фактичну ціну. Явний UnitPrice зберігається після доменної перевірки.
- При PriceTypeId = null збережено ручний режим: RecommendedUnitPrice = 0, фактична ціна невід’ємна, пропущена = 0. При вибраному PriceType відсутня ціна дає HTTP 400, code = PRICE_NOT_FOUND; fallback на іншу партію, ProductPrice для GOODS або нуль відсутній.
- Збережений RecommendedUnitPrice — snapshot. Оновлення прайсу, GET, звичайний PUT DRAFT, POST, UNPOST та re-POST його не змінюють. PUT із тим самим ID рядка, ProductId, BatchId та PriceTypeId зберігає попередню рекомендацію, навіть якщо ціна довідника змінилася. Явна зміна Product/Batch/PriceType є новим вибором джерела ціни: рекомендація визначається заново; пропущений UnitPrice у цьому випадку дорівнює новій рекомендації. Новий рядок завжди отримує поточну рекомендацію. POST перевіряє збережену пару цін.
- UnitPrice >= RecommendedUnitPrice дозволений без спеціального дозволу. Нижча ціна потребує SalePricePermissions.CanSellBelowRecommendedPrice. Перевірка діє при create/edit DRAFT і першому POST, у Domain. Відмова — HTTP 403, code = PRICE_BELOW_RECOMMENDED. Дозвіл не скасовує невід’ємність та numeric precision.
- ISalePricePermissionProvider передає capability sale.change_price із поточних активних ролей до домену. Це authorization rule, а не постійна заборона нижчої ціни. HTTP body та заголовки не можуть надати право. Перевірка діє для create/edit/POST і порівнює ціни одиниці до DiscountPercent.
- Збережені RecommendedUnitPrice і UnitPrice — факти рядка документа. Cached price/stock/balance/cost, історія прайсів та currency logic не додаються.

## Поточна собівартість і початкові ціни партій

- v_batch_cost — звичайний PostgreSQL VIEW із BatchId, PurchaseInvoiceLineId, ProductId, UnitCostUah. Базова собівартість одиниці = PurchaseInvoiceLine.UnitPriceCurrency × (1 - PurchaseInvoice.DiscountPercent / 100) × PurchaseInvoice.ExchangeRate. До неї додається частка POSTED direct Expenses на одиницю. Проміжного округлення немає.
- VIEW використовує поточні первинні дані закупівлі, також після UNPOST; вилучення рядка не видаляє збережену Batch чи її базову собівартість. UnitCostUah ніде не зберігається в таблицях. У розподілі витрат беруть участь лише активні purchase lines.
- POST для кожної новоствореної Batch створює BatchPrice усіх активних PriceType цієї організації. PriceUah (поле BatchPrice.UnitPrice) = UnitCostUah × (1 + DefaultMarkupPercent / 100), округлення лише кінцевої ціни до 4 знаків за PostgreSQL round. Неактивні PriceType пропускаються. Націнка 0 створює ціну, що дорівнює поточній собівартості з округленням.
- Лише нова Batch отримує початкові ціни. Перший POST нового рядка, доданого після UNPOST, ціни створює; старі Batch не переоцінюються і не отримують ціни пізніше доданих/активованих PriceType автоматично. Наявна пара Batch/PriceType ніколи не перезаписується POST. Ручний PUT BatchPrice залишається явним оновленням ціни.
- UNPOST зберігає BatchPrice. Повторний POST не дублює і не змінює їх. Зміна закупівельної ціни, знижки або курсу після UNPOST змінює обчислюваний UnitCostUah, але не збережені BatchPrice. Зміна DefaultMarkupPercent також не змінює старих цін; нові Batch використовують нову націнку.
- Документ, Batch, рухи та початкові ціни записуються в одній транзакції. Помилка створення початкової ціни, включно з перевищенням numeric(20,4), відкочує весь POST. SERVICE не закуповується і Batch/BatchPrice для нього не створюються.

## Контрагенти

- Counterparty містить лише Id, OrganizationId, Name, nullable LegalType (PERSON/ENTREPRENEUR/COMPANY), nullable TaxId/RegistrationCode/Phone/Email/Address/DefaultPriceTypeId, IsCustomer, IsSupplier, IsEmployee, IsSystem, Active, nullable Note. Name обов’язкове, обрізається по краях. OrganizationId визначає backend, він незмінний через API.
- DefaultPriceTypeId, якщо заданий, посилається на існуючий PriceType тієї самої організації. Це реквізит контрагента; цей slice не змінює явний вибір PriceType чи snapshot у продажі.
- POST/GET/PUT працюють лише з поточною Organization. GET list підтримує незалежні nullable bool-фільтри isCustomer/isSupplier/isEmployee/active; false теж є фільтром. Без active-фільтра повертаються обидва стани. Прапорці ролей є ознаками для фільтрації; додаткової заборони використання контрагента за цими прапорцями не вводиться.
- Purchase/Sale при create/edit/першому POST перевіряють існування Counterparty у поточній Organization. Для OPENING_STOCK CounterpartyId може бути null; якщо заданий, теж перевіряється. Сервіси тримають Counterparty FOR SHARE під час запису, PUT контрагента бере FOR UPDATE; FK та PostgreSQL triggers додатково захищають посилання організацій.
- Системного контрагента фізично видаляти не можна: правило перевіряється доменом через EF і trigger для прямого SQL. DELETE endpoint не додається. «Роздрібний покупець» призначений для майбутнього retail flow; UI та автоматичний seed/bootstrap поки відсутні.

## Взаєморозрахунки

- SettlementMovement містить лише Id, DocumentId, CounterpartyId, CurrencyId, Amount, BaseAmountUah. Обидві суми — numeric(20,4); Organization та дата визначаються зв’язками. Один рух на invoice, Expense, CustomerReturn, SupplierReturn або SETTLEMENT money document, UNIQUE(DocumentId).
- Плюс означає, що контрагент винен нам; мінус — ми винні контрагенту. PURCHASE_INVOICE POST створює негативний Amount у валюті закупівлі та негативний BaseAmountUah. OPENING_STOCK не створює settlement незалежно від наявності CounterpartyId.
- SALE_INVOICE POST створює позитивний Amount та рівний йому BaseAmountUah, тільки в UAH. GUID UAH погоджено: 00000000-0000-0000-0000-000000000980; нової таблиці валют немає.
- Purchase total = SUM(active Quantity × UnitPriceCurrency) × (1 - DiscountPercent / 100). Sale total = SUM(active Quantity × actual UnitPrice) × (1 - DiscountPercent / 100), включно з GOODS і SERVICE. RemovedAt != null виключається. Totals та line totals у таблицях не зберігаються.
- Добутки та сума рахуються точно через масштабовані цілі числа без переповнення проміжного decimal та без округлення рядків. Кінцевий Amount округлюється до 4 знаків (половина від нуля); BaseAmountUah закупівлі = округлений Amount × ExchangeRate з кінцевим округленням до 4 знаків. Знак застосовується до обох сум. Нульова сума допустима. Перевищення numeric(20,4) дає 400 та відкат POST.
- DRAFT не створює settlement. POST/UNPOST використовують ту саму транзакцію та блокування Document, що й stock/початкові ціни. Повторний POST уже POSTED документа не додає рух і не змінює audit/номер/Version. UNPOST видаляє settlement цього документа; помилка видалення відкочує також видалені stock movements. Повторний POST після UNPOST рахує новий settlement із поточних рядків/реквізитів.
- v_counterparty_balance — звичайний VIEW: CounterpartyId, CurrencyId, Balance = SUM(SettlementMovement.Amount). Валюти не змішуються, BaseAmountUah не використовується як валютний баланс. Якщо рухів немає, рядок балансу відсутній. Кешів або balance-полів у Counterparty немає.
- GET /settlements/balances має optional counterpartyId/currencyId. GET /settlements/counterparties/{id} повертає balances по валютах та movements. BalanceAfterMovement — window SUM(Amount), PARTITION BY CurrencyId; порядок DocumentDate → Document.CreatedAt → Document.Id → Movement.Id. Це поточна хронологія з урахуванням backdated дат, без збереженого running balance. Для балансу та історії використовується спільний REPEATABLE READ snapshot.
- Результати завжди обмежені поточною організацією. Counterparty/Document різних організацій не можуть утворювати settlement. Чужий профіль/історія — 404; чужий counterpartyId у фільтрі балансів — порожній список.

## Гроші

- MoneyAccount містить тільки Id, OrganizationId, Name, AccountType (CASH/BANK/SAFE/OTHER), CurrencyId, Active. Name обов’язкове. Один рахунок — одна валюта. Валюта може змінитися до першого посилання money document/movement; після використання, включно з DRAFT та після UNPOST, вона незмінна, щоб не переінтерпретувати історію. Назва, тип та Active редагуються.
- MoneyDocument містить тільки DocumentId, nullable Purpose (SETTLEMENT/OWNER/OPENING_BALANCE), nullable CounterpartyId/SourceAccountId/DestinationAccountId/SourceAmount/DestinationAmount/SettlementCurrencyId/SettlementAmount/SettlementExchangeRate. Суми — numeric(20,4), settlement rate — numeric(20,8), > 0 при SETTLEMENT. Тип операції тільки Document.DocumentType: MONEY_RECEIPT/MONEY_PAYMENT/MONEY_TRANSFER. Окремого operation_type немає.
- MoneyMovement містить лише Id, DocumentId, MoneyAccountId, Amount numeric(20,4). CurrencyId береться з MoneyAccount, дата — з DocumentDate, Organization — зі зв’язків. Рух не містить цих копій, Balance чи cached полів. UNIQUE(DocumentId, MoneyAccountId) забороняє дубль.
- Receipt вимагає DestinationAccountId і DestinationAmount > 0; SourceAccountId та SourceAmount мають бути null. POST створює +DestinationAmount на рахунок.
- Payment вимагає SourceAccountId і SourceAmount > 0; DestinationAccountId та DestinationAmount мають бути null. POST створює -SourceAmount. Достатність коштів не перевіряється, навіть без попередніх надходжень.
- Transfer вимагає два різні рахунки та обидві positive суми. POST створює -SourceAmount і +DestinationAmount. Для однакової валюти суми рівні, для різних можуть відрізнятися. Курс переказу — співвідношення фактичних сум; окремо не зберігається. Transfer не має SETTLEMENT purpose і ніколи не створює SettlementMovement.
- Для SETTLEMENT receipt/payment потрібні власний CounterpartyId, SettlementCurrencyId, SettlementAmount > 0, SettlementExchangeRate > 0. Для UAH пропущений rate підставляється як 1; явно переданий інший rate відхиляється. Для іншої валюти rate потрібно передати.
- SETTLEMENT receipt створює від’ємні Amount/BaseAmountUah у settlement journal: клієнт погашає борг. SETTLEMENT payment створює додатні: оплата зменшує нашу заборгованість. BaseAmountUah = SettlementAmount × SettlementExchangeRate, кінцеве округлення до 4 знаків. Actual сума руху рахунку та settlement-сума/валюта — окремі задані факти, їх не прирівнюємо автоматично.
- OWNER, OPENING_BALANCE і nullable Purpose не створюють settlement. Усі settlement-поля для них мають бути null. Counterparty не обов’язковий; якщо переданий, має бути своєї організації.
- DRAFT не створює жодного журналу. POST перевіряє DRAFT, присвоює MR/MP/MT номер при першому проведенні, атомарно записує money/settlement рухи і встановлює POSTED. Replay POSTED — без нових рухів або змін audit/Version.
- UNPOST видаляє MoneyMovement документа та його SettlementMovement для SETTLEMENT, повертає DRAFT, зберігає номер. Повторний UNPOST DRAFT — no-op. Помилка видалення будь-якого журналу відкочує обидва. Мінус після UNPOST дозволений.
- v_money_account_balance — звичайний VIEW із MoneyAccountId, CurrencyId, Balance = SUM(MoneyMovement.Amount). Рахунок без рухів не має рядка у view; рухи із сумою 0 дають Balance = 0. Рахунки та валюти не змішуються. Балансу в MoneyAccount немає; AllowNegativeBalance не створюється.
- API: POST/GET /money/accounts, GET/PUT /money/accounts/{id}; POST /money/receipts|payments|transfers, GET/PUT /money/{kind}/{id}, POST /money/{kind}/{id}/post|unpost, GET /money/balances. PUT документа потребує Version і DRAFT. Операція задається маршрутом, неправильний тип маршруту повертає 404. Видалення документів/рахунків не додається.
- Усі рахунки, контрагенти та документи належать поточній організації. Money writes блокують Document FOR UPDATE, Counterparty FOR SHARE і впорядковані MoneyAccount FOR SHARE; Account PUT бере FOR UPDATE. Це захищає валюту й посилання, без блокування/перевірки балансу. PostgreSQL додатково захищає форму документа, організації, використану валюту рахунку та відповідність money movement його документу.

## Списання

- WriteOffReason: Id, OrganizationId, Name, ExpenseCategoryId, Active. Name обов’язкове й trimmed, ExpenseCategoryId обов’язковий та належить цій організації. CRUD не створює seed reasons/categories. Active — ознака довідника; її зміна не переписує документи.
- WriteOff використовує DocumentType WRITE_OFF, має тільки DocumentId, StorageLocationId, WriteOffReasonId. Одна причина на документ. Склад — уже використаний у purchase/sale цієї організації за погодженим адаптером. Reason/category і Batch через source PurchaseInvoice — лише тієї самої організації.
- WriteOffLine: Id, WriteOffId, LineNo, BatchId, Quantity numeric(20,4) > 0. LineNo > 0 та унікальний; Batch лише раз у документі. Product/Organization/UnitCost/Amount не зберігаються. DRAFT PUT замінює відсутні рядки без додаткового RemovedAt; logical DELETE документа зберігає рядки.
- DRAFT не створює рухів, але резервує stock у StorageLocationId. ReservedQuantity спільного view включає sale GOODS, SupplierReturn, StockTransfer (From) і WriteOff; available = physical - reserved. Create/edit/post беруть Batch locks та не допускають надлишкового reserve → INSUFFICIENT_STOCK. PUT/POST виключають лише власний saved reserve, також при зміні складу/Batch.
- POST повторно перевіряє DRAFT, reason/scope та available/physical, призначає WO-номер при першому проведенні й створює по одному negative StockMovement для кожного Batch у складі документа. Нових Batch, Expense, SettlementMovement або MoneyMovement немає. Replay POSTED не змінює номер/audit/Version і не додає рухів.
- UNPOST видаляє stock movements, повертає DRAFT і reserve. Projected physical/reserved перевіряються атомарно під Batch locks; некоректний результат → INSUFFICIENT_STOCK. Повторний UNPOST DRAFT — no-op. DELETE лише logical DRAFT; POSTED спочатку UNPOST. PUT потребує поточної Version, deleted/чужі документи не читаються й не змінюються API.
- v_write_off_cost — звичайний, нематеріалізований VIEW: WriteOffId, DocumentId, DocumentDate, StorageLocationId, WriteOffReasonId, ExpenseCategoryId, TotalCostUah. TotalCostUah = SUM(Quantity × current v_batch_cost.UnitCostUah), без округлення окремих рядків або snapshot. Категорія — поточний WriteOffReason.ExpenseCategoryId. Empty DRAFT дає 0, deleted документи виключені; DRAFT/POSTED обидва доступні для query, звіт фактичних списань фільтрує POSTED через Document.
- Пізній direct Expense, його UNPOST/редагування/re-POST змінює current batch cost та історичну calculated WriteOffCost. Документ/рядки/BatchPrice при цьому не змінюються; це очікувана поведінка. SQL report query фільтрує Document.OrganizationId. Кешованих costs/totals та автоматичного Expense-документа немає.

## Межі

Усі бізнес-правила виконуються на backend; frontend-валідація служить UX. Не зберігаємо однозначно обчислювані дані, cached stock/debt/balance/cost. Opening settlement UI, Admin CRUD UI/MFA/SSO, historical stock validation, currency revaluation report, printed forms, бізнес-UI, PaymentAllocation, Obligation та Advance tables не реалізовані. Собівартість — лише обчислювана через VIEW; гроші та взаєморозрахунки — окремі журнали з SUM без збережених балансів або прив’язки платежів до конкретних invoices.

## Повернення покупця

- CustomerReturn: DocumentId PK/FK спільного DocumentType CUSTOMER_RETURN, SourceSaleInvoiceId, StorageLocationId. Counterparty береться із source sale. CustomerReturnLine: Id, CustomerReturnId, SourceSaleLineId, Quantity numeric(20,4), RefundAmount numeric(20,4). Product/Batch/UnitPrice не дублюються. Source має бути POSTED своєї організації, рядки активні й саме з цього продажу; один source line не може повторюватися в одному return.
- StorageLocation — GUID, уже використаний у purchase/sale поточної організації. Це погоджений адаптер до появи довідника, нових таблиць немає. Чужий/невідомий склад відхиляється.
- OriginalNetAmount = round(Source.Quantity × Source.UnitPrice × (1 - Sale.DiscountPercent / 100), 4). BigInteger scaling зберігає точність добутків до округлення. GOODS Quantity > 0; сума quantity активних POSTED returns не перевищує sold quantity → RETURN_QUANTITY_EXCEEDED. RefundAmount для GOODS не приймається від клієнта; backend рахує його пропорційно. Частковий refund округлюється до 4 знаків і не перевищує залишок net; остання кількість отримує залишок суми, узгоджуючи округлення. Нульовий refund безкоштовного товару дозволений.
- SERVICE RefundAmount > 0 і не перевищує залишок net після всіх POSTED returns → RETURN_AMOUNT_EXCEEDED. Якщо omitted при збереженні, підставляється залишок refundable amount. Quantity >= 0; нуль означає фінансову компенсацію без повернення кількості. Для SERVICE ліміт фінансовий; додатна Quantity інформаційна й не замінює refund limit. StockMovement/Batch для SERVICE не створюються.
- DRAFT не створює рухів, stock reserve чи резерву return limits. POST під source sale lock повторно перевіряє ліміти, створює GOODS stock receipt у original Batch та вибраному складі і один SettlementMovement = -SUM(refunds), CurrencyId = UAH, BaseAmountUah = Amount. MoneyMovement немає. POST idempotent, CR-номер лише вперше, після UNPOST зберігається.
- UNPOST видаляє stock/settlement атомарно та повертає DRAFT; не можна забрати проданий/зарезервований товар → INSUFFICIENT_STOCK. Історичної реконструкції немає. Невилучений return, включно з DRAFT, блокує source Sale UNPOST → DOCUMENT_HAS_DEPENDENCIES; спочатку UNPOST return та logical DELETE. POSTED напряму DELETE заборонений.
- GET /sales/{id}/returnable-lines повертає активні source lines та всі погоджені derived поля. AlreadyReturnedQuantity/AlreadyRefundedAmount — SUM POSTED невилучених returns; ReturnableQuantity = max(0, SoldQuantity - AlreadyReturnedQuantity); RefundableAmount = OriginalNetAmount - AlreadyRefundedAmount. Поля не кешуються. Source і суми читаються під одним source lock. Редагування DRAFT потребує Version; вилучені документи не читаються API.

## Повернення постачальнику

- SupplierReturn містить тільки DocumentId, SourcePurchaseInvoiceId, StorageLocationId. Counterparty визначає source. SupplierReturnLine містить тільки Id, SupplierReturnId, SourcePurchaseLineId, Quantity numeric(20,4) > 0. Source — невилучений POSTED PURCHASE_INVOICE своєї організації; OPENING_STOCK заборонений. Source lines активні, лише цього invoice, GOODS, зі своїм original Batch. Один source line лише раз у документі; DB deferred uniqueness дозволяє атомарну перестановку рядків у DRAFT.
- Quantity не перевищує purchased мінус SUM quantity активних POSTED SupplierReturn → RETURN_QUANTITY_EXCEEDED. Додатково вона не перевищує AvailableQuantity по Batch/StorageLocation → INSUFFICIENT_STOCK. Склад має вже використовуватися в purchase/sale організації. Обидві перевірки виконуються при create/edit та повторно при POST.
- DRAFT не створює рухів, але резервує товар рядками через v_stock_balance. ReservedQuantity = sale reserves + supplier return reserves, AvailableQuantity = physical - reserved. Own reserve виключається при PUT/POST; інші резерви залишаються. Removed DRAFT lines фізично замінюються під Batch locks, додаткового RemovedAt у схемі немає. Logical DELETE документа зберігає його рядки й прибирає резерв.
- POST створює negative StockMovement у source Batch та обраному складі, один positive SettlementMovement. Amount = round(SUM(Return.Quantity × Source.UnitPriceCurrency) × (1 - Purchase.DiscountPercent / 100), 4); CurrencyId/CounterpartyId із source; BaseAmountUah = round(Amount × Purchase.ExchangeRate, 4). Жодних direct Expenses/logistics/current batch cost у refund. Нових Batch/MoneyMovement немає; формула v_batch_cost та BatchPrice незмінні.
- POST idempotent: номер SR лише вперше, replay не змінює рухів/audit/Version. UNPOST видаляє stock/settlement і повертає DRAFT з резервом; projected physical/available перевіряються StockRules в тій самій транзакції. DELETE лише logical DRAFT, POSTED спочатку UNPOST. Будь-який невилучений SupplierReturn, також DRAFT, блокує Purchase UNPOST → DOCUMENT_HAS_DEPENDENCIES.
- Source Purchase Document FOR UPDATE серіалізує return limits та dependency checks. Далі Product FOR SHARE і Batch FOR UPDATE за ID серіалізують stock/reserve зі sales та CustomerReturn. Зміна source бере old/new parents за ID і union old/new Batch locks; GetStockAsync виключає лише власний збережений reserve Sale або SupplierReturn.
- GET /purchases/{id}/returnable-lines повертає всі погоджені source, purchase limit, stock, price/currency/rate поля. Optional storageLocationId вибирає склад; без нього — склад source purchase. RemainingReturnableByPurchase = purchased - SUM POSTED return quantities; MaxReturnableQuantity = MIN(RemainingReturnableByPurchase, AvailableQuantity). Нічого не кешується; query тримає source/Batch locks для узгодженого результату.

## Складські переміщення

- StockTransfer має тільки DocumentId PK/FK спільного DocumentType STOCK_TRANSFER, FromLocationId, ToLocationId. StockTransferLine має тільки Id, StockTransferId, LineNo, BatchId, Quantity numeric(20,4). Product визначається через Batch → PurchaseInvoiceLine, Organization — через source PurchaseInvoice.Document; копій у рядках немає.
- From/To обов’язкові, різні та належать поточній організації за погодженим адаптером: вже використані у purchase/sale. Batch має існувати в організації. Quantity > 0; LineNo > 0 та унікальний, один Batch лише раз у документі. DB deferred unique constraints дозволяють атомарну перестановку LineNo/Batch у DRAFT PUT.
- DRAFT не створює StockMovement, SettlementMovement або MoneyMovement. Рядки резервують Quantity тільки у FromLocationId, To не отримує stock чи reserve. Спільний reserve — активні DRAFT SaleInvoice GOODS lines, SupplierReturn lines і StockTransfer lines; deleted документи не резервують. PhysicalQuantity = SUM(StockMovement.Quantity), AvailableQuantity = PhysicalQuantity - ReservedQuantity, поля не зберігаються фізично.
- Create/PUT перевіряють available під Batch locks; PUT виключає лише власний попередньо збережений reserve за old Batch/From, також при зміні локацій/рядків. Інші reserves не виключаються. Недостатність → INSUFFICIENT_STOCK.
- POST перевіряє DRAFT, непорожні рядки, scope і available/physical повторно. Кожний рядок створює negative movement у From та positive у To, Batch той самий. Нових Batch/settlement/money/cost/price facts немає. Перший POST призначає ST-номер зі спільної sequence; POSTED replay не змінює audit/Version чи рухи.
- UNPOST під тими самими Batch locks обчислює projected physical після видалення обох напрямків і projected reserved із відновленим DRAFT у From. Destination має залишитися достатнім для активних reservations → INSUFFICIENT_STOCK. Якщо поточного stock достатньо, використання частини товару саме по собі не блокує UNPOST. Повної historical/backdated validation немає. Видалення рухів/зміна статусу атомарні, номер зберігається, повторний UNPOST DRAFT — no-op.
- DELETE лише logical DRAFT: DeletedAt/DeletedBy, рядки зберігаються, reserve зникає; POSTED спочатку UNPOST. PUT потребує поточної Version. GET/list/PUT/POST/UNPOST не знаходять deleted/чужий документ. Список фільтрується from/to, fromLocationId/toLocationId, status; `/stock/available-batches` читає оновлений view і враховує transfer reserve.

## Витрати

- ExpenseCategory має тільки Id, OrganizationId, ParentId nullable, Name, Active. Name обов’язкове; parent тієї ж організації; дерево довільної глибини без циклів. Category writes серіалізовані advisory transaction lock організації, SQL trigger також перевіряє зв’язки й цикли.
- Expense використовує DocumentType EXPENSE, DocumentId PK/FK, CounterpartyId, ExpenseCategoryId, CurrencyId, Amount numeric(20,4), ExchangeRate numeric(20,6), PurchaseInvoiceId nullable. Amount і курс > 0, зайву точність відхиляємо. Description немає; текст у Document.Comment/InternalNote. Контрагент/категорія/закупівля належать поточній організації.
- PurchaseInvoiceId = null — операційна витрата. Інакше — direct expense лише для невидаленої POSTED PURCHASE_INVOICE, OPENING_STOCK заборонений. Нульова точна вартість активних рядків після знижки → 400 / INVALID_PURCHASE_EXPENSE_ALLOCATION. Дрібну додатну вартість не округлюємо до нуля для цієї перевірки.
- DRAFT не створює рухів і не впливає на cost. POST створює один SettlementMovement: Amount = -Expense.Amount, BaseAmountUah = -(Amount × власний ExchangeRate), кінцеве округлення base до 4 знаків. MoneyMovement автоматично не створюється: оплата — окремий MoneyPayment Purpose=SETTLEMENT.
- POST/UNPOST атомарні; replay POSTED не змінює рух/номер/audit/Version. UNPOST видаляє settlement і повертає DRAFT, повторний UNPOST — no-op. PUT лише DRAFT із поточною Version. DELETE логічний лише DRAFT; після нього GET/PUT/POST/UNPOST не знаходять документ, рядок Expense зберігається.
- Для v_batch_cost: LineBaseUah = Quantity × UnitPriceCurrency × (1 - DiscountPercent / 100) × Purchase.ExchangeRate. PurchaseInvoiceBaseUah — SUM LineBaseUah активних рядків. TotalDirectExpensesUah — SUM(Expense.Amount × Expense.ExchangeRate) лише POSTED невидалених Expenses цієї закупівлі, без округлення через settlement BaseAmountUah. Line allocation = LineBaseUah / PurchaseInvoiceBaseUah × TotalDirectExpensesUah; UnitCostUah = (LineBaseUah + allocation) / Quantity. SQL скорочує Quantity в додатковому доданку до unit_base × expenses / total_base, зберігаючи точну базову собівартість. Немає PurchaseCostAllocation, ручного розподілу чи snapshots.
- Додавання direct Expense після sale, його UNPOST/редагування/re-POST змінює поточну й історичну собівартість та може перерахувати gross profit. Це очікувано. BatchPrice і збережені sale recommendations автоматично не переписуються.
- Невилучений direct Expense, також DRAFT, блокує UNPOST закупівлі → 409 / DOCUMENT_HAS_DEPENDENCIES. Спочатку UNPOST Expense та від’єднати його від закупівлі або логічно видалити. Attach/POST Expense тримає shared lock документа закупівлі; purchase UNPOST — exclusive lock до перевірки залежностей, без гонки між ними.
- API: POST/GET /expense-categories, GET/PUT /expense-categories/{id}; POST/GET /expenses, GET/PUT/DELETE /expenses/{id}, POST /expenses/{id}/post|unpost. Список не включає deleted та підтримує from/to (включно), counterpartyId, categoryId, purchaseInvoiceId, status, currencyId; порядок DocumentDate/CreatedAt/Id.

- Identity User має одну незмінну Organization. Counterparty та ролі — тієї самої організації. User фізично не видаляється; inactive user не входить і не використовує стару сесію. Inactive Role не надає прав. Effective permissions тільки обчислюються як union активних ролей, без збережених snapshots/cache. Контекст і audit беруться лише з Identity cookie, ніколи з JSON або context headers.
- Зниження ціни вимагає sale.change_price; cost API — cost.view, profit API — profit.view. Permission перевіряє backend. Login/logout та всі JSON write endpoints вимагають ASP.NET antiforgery token; production cookie Secure/HttpOnly/SameSite=Lax. Development seed із конфігурації пароля не виконується у Production/Staging.
