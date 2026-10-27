# Сценарії закупівлі, продажу, ціноутворення, грошей та витрат

## Контекст локального API

Усі бізнес-запити вимагають Identity cookie та permission. Організація та audit-поля беруться з authenticated user; X-Organization-Id і X-Actor-Id ігноруються в усіх середовищах. Для POST/PUT/DELETE передавайте X-CSRF-TOKEN із GET /auth/csrf; після login отримайте новий token. Приклади нижче — JSON payload після входу; cookie/token flow описаний у authentication.md.

## Ендпоїнти

| Метод | Шлях | Результат |
| --- | --- | --- |
| POST | /products | Створити Product, 201 |
| GET | /products | Номенклатура поточної організації |
| GET | /products/{id} | Product |
| PUT | /products/{id} | Оновити Product |
| POST | /purchases | Створити DRAFT, 201 |
| GET | /purchases/{id} | Документ, реквізити закупівлі, всі рядки |
| PUT | /purchases/{id} | Замінити активні рядки і реквізити DRAFT з поточною Version |
| POST | /purchases/{id}/post | Провести або повернути вже POSTED стан |
| POST | /purchases/{id}/unpost | Скасувати проведення або повернути вже DRAFT стан |
| GET | /stock/balances | Залишки з view для поточної організації |
| POST | /sales | Створити DRAFT sale і зарезервувати GOODS, 201 |
| GET | /sales/{id} | Документ, реквізити sale та всі рядки |
| PUT | /sales/{id} | Оновити DRAFT з поточною Version і перевірити резерв |
| POST | /sales/{id}/post | Провести sale або повернути вже POSTED стан |
| POST | /sales/{id}/unpost | Повернути у DRAFT і відновити резерв |
| DELETE | /sales/{id} | Логічно видалити тільки DRAFT, 204 |
| GET | /stock/available-batches | Доступні партії Product у StorageLocation, найстаріші першими |
| POST / GET | /price-types | Створити тип цін / список поточної організації |
| GET / PUT | /price-types/{id} | Прочитати / оновити назву, націнку, активність і порядок |
| GET / PUT | /batches/{batchId}/prices/{priceTypeId} | Прочитати / встановити ціну партії GOODS |
| GET / PUT | /products/{productId}/prices/{priceTypeId} | Прочитати / встановити ціну SERVICE |
| POST / GET | /counterparties | Створити / список контрагентів поточної організації |
| GET / PUT | /counterparties/{id} | Прочитати / оновити реквізити контрагента |
| GET | /settlements/balances | SUM балансів за контрагентами й валютами |
| GET | /settlements/counterparties/{id} | Баланси та хронологія з обчислюваним BalanceAfterMovement |
| POST / GET | /money/accounts | Створити рахунок / список власних рахунків |
| GET / PUT | /money/accounts/{id} | Прочитати / оновити реквізити рахунку |
| POST | /money/receipts | Створити DRAFT надходження |
| GET / PUT | /money/receipts/{id} | Прочитати / редагувати DRAFT надходження |
| POST | /money/receipts/{id}/post | Провести надходження |
| POST | /money/receipts/{id}/unpost | Скасувати проведення надходження |
| POST | /money/payments | Створити DRAFT оплати |
| GET / PUT | /money/payments/{id} | Прочитати / редагувати DRAFT оплати |
| POST | /money/payments/{id}/post | Провести оплату |
| POST | /money/payments/{id}/unpost | Скасувати проведення оплати |
| POST | /money/transfers | Створити DRAFT переказу |
| GET / PUT | /money/transfers/{id} | Прочитати / редагувати DRAFT переказу |
| POST | /money/transfers/{id}/post | Провести переказ |
| POST | /money/transfers/{id}/unpost | Скасувати проведення переказу |
| GET | /money/balances | SUM балансів рахунків у поточній організації |
| POST / GET | /expense-categories | Створити категорію / список дерева поточної організації |
| GET / PUT | /expense-categories/{id} | Прочитати / змінити назву, parent та Active |
| POST / GET | /expenses | Створити DRAFT витрати / список із фільтрами |
| GET / PUT | /expenses/{id} | Прочитати / редагувати DRAFT із поточною Version |
| POST | /expenses/{id}/post | Створити негативний settlement без money movement |
| POST | /expenses/{id}/unpost | Видалити settlement, повернути DRAFT |
| DELETE | /expenses/{id} | Логічно видалити тільки DRAFT, 204 |
| POST / GET | /customer-returns | Створити DRAFT / список повернень організації |
| GET / PUT / DELETE | /customer-returns/{id} | Прочитати / редагувати DRAFT із Version / logical DELETE |
| POST | /customer-returns/{id}/post | Провести повернення або повернути вже POSTED стан |
| POST | /customer-returns/{id}/unpost | Видалити stock/settlement після перевірки залишків |
| POST | /sales/{id}/customer-return | Створити DRAFT із prefilled source sale та складом |
| GET | /sales/{id}/returnable-lines | Обчислювані доступні кількості та суми source lines |
| POST / GET | /supplier-returns | Створити DRAFT із reserve / список організації |
| GET / PUT / DELETE | /supplier-returns/{id} | Прочитати / редагувати DRAFT із Version / logical DELETE зі звільненням reserve |
| POST | /supplier-returns/{id}/post | Списати original Batch та створити positive settlement |
| POST | /supplier-returns/{id}/unpost | Видалити рухи й відновити DRAFT reserve |
| POST | /purchases/{id}/supplier-return | Створити DRAFT із prefilled source/складом |
| GET | /purchases/{id}/returnable-lines | Purchase limit і stock; optional storageLocationId |
| POST / GET | /stock-transfers | Створити DRAFT із source reserve / список із фільтрами |
| GET / PUT / DELETE | /stock-transfers/{id} | Прочитати / редагувати DRAFT із Version / logical DELETE |
| POST | /stock-transfers/{id}/post | Перенести stock двома рухами того самого Batch |
| POST | /stock-transfers/{id}/unpost | Видалити обидва рухи після stock validation, відновити source reserve |
| POST / GET | /write-off-reasons | Створити причину / список поточної організації |
| GET / PUT | /write-off-reasons/{id} | Прочитати / змінити name/category/active |
| POST / GET | /write-offs | Створити DRAFT із reserve / список із шістьма фільтрами |
| GET / PUT / DELETE | /write-offs/{id} | Прочитати / редагувати DRAFT із Version / logical DELETE |
| POST | /write-offs/{id}/post | Списати stock без фінансових рухів |
| POST | /write-offs/{id}/unpost | Видалити issue й відновити DRAFT reserve |

GET /stock/balances підтримує optional query parameters storageLocationId і batchId та повертає physicalQuantity, reservedQuantity і availableQuantity. GET /stock/available-batches потребує productId і storageLocationId. GET /counterparties підтримує isCustomer/isSupplier/isEmployee/active; GET /settlements/balances — counterpartyId/currencyId. Enums передаються рядками GOODS / SERVICE, PURCHASE_INVOICE / OPENING_STOCK / SALE_INVOICE, DRAFT / POSTED, PERSON / ENTREPRENEUR / COMPANY.

## 0. Контрагент перед створенням документів

POST /counterparties:

```json
{
  "name": "Партнер",
  "legalType": "COMPANY",
  "taxId": null,
  "registrationCode": null,
  "phone": null,
  "email": null,
  "address": null,
  "defaultPriceTypeId": null,
  "isCustomer": true,
  "isSupplier": true,
  "isEmployee": false,
  "isSystem": false,
  "active": true,
  "note": null
}
```

Відповідь 201 містить id та organizationId з контексту. Використовуйте цей id у counterpartyId закупівель/продажів. Випадковий або чужий GUID більше не приймається. DefaultPriceTypeId, якщо заданий, має бути PriceType тієї самої організації. PUT передає повний набір реквізитів; GET /counterparties?isCustomer=true&active=true фільтрує клієнтів, `active=false` повертає неактивних. LegalType та інші nullable реквізити можна не задавати; мінімальний body містить name.

Системний «Роздрібний покупець» не створюється автоматично. UI/bootstrap і DELETE endpoint не додаються; IsSystem захищений від фізичного видалення на рівні Domain/EF та SQL.

## 1. Створення номенклатури

Приклад body POST /products (UnitId — ID зовнішнього довідника):

```json
{
  "categoryId": null,
  "unitId": "33333333-3333-3333-3333-333333333333",
  "itemType": "GOODS",
  "code": "GOODS-001",
  "barcode": null,
  "name": "Тестовий товар",
  "description": null,
  "attributes": { "color": "blue" },
  "active": true
}
```

Відповідь містить згенерований id та organizationId з контексту. SERVICE теж можна створити як Product, але не використати в закупівлі.

## 2. Збереження DRAFT

Для POST /purchases замініть productId на отриманий ID товару:

```json
{
  "documentType": "PURCHASE_INVOICE",
  "documentDate": "2026-10-19",
  "comment": "Надходження",
  "internalNote": null,
  "counterpartyId": "<ID з POST /counterparties>",
  "storageLocationId": "44444444-4444-4444-4444-444444444444",
  "currencyId": "55555555-5555-5555-5555-555555555555",
  "exchangeRate": 1,
  "externalNumber": null,
  "externalDate": null,
  "discountPercent": 0,
  "lines": [
    {
      "id": null,
      "lineNo": 1,
      "productId": "<ID з POST /products>",
      "quantity": 5.25,
      "unitPriceCurrency": 10
    }
  ]
}
```

Відповідь має document із id/status/version/audit і реквізити закупівлі та lines на верхньому рівні. document.status = DRAFT, document.number = null, document.version = 1. Batch і StockMovement відсутні.

OPENING_STOCK може мати counterpartyId = null. Звичайна закупівля — ні. Порожні lines дозволені лише для збереження DRAFT.

## 3. POST та залишок

1. POST /purchases/{document.id}/post без body.
2. З’являється номер, одна Batch на активний рядок та позитивний рух у storageLocationId закупівлі.
3. GET /stock/balances повертає batchId, storageLocationId, physicalQuantity = 5.25.
4. Повторний POST повертає той самий документ, audit і Version. Паралельні POST не дублюють Batch/StockMovement.

Два рядки того самого товару мають дві різні партії та два окремі залишки за BatchId.

## 4. UNPOST, редагування та повторне проведення

1. POST /purchases/{id}/unpost видаляє рухи та повертає DRAFT; номер і Batch зберігаються.
2. PUT /purchases/{id} передає documentDate/comment/internalNote, реквізити закупівлі, version з останньої відповіді та повний список активних lines. documentType і organizationId не змінюються через PUT.
3. Для наявного рядка зберігайте його id. Для нового — id = null. Відсутній у PUT старий рядок отримує removedAt.
4. Можна змінити Quantity, ціну, дату та місце зберігання. ProductId рядка з Batch змінити не можна.
5. Після повторного POST Batch того самого рядка та номер документа лишаються попередніми; рух відображає нову Quantity / StorageLocationId.
6. Вилучені рядки не створюють рухів. Якщо повернути старий ID рядка з тим самим ProductId, використовується його збережена Batch.
7. PUT із застарілою version дає 409; PUT для POSTED — 400.

## 5. Відхилення та атомарність

- SERVICE, товар іншої організації, Quantity <= 0, зайва десяткова точність, від’ємна ціна, ExchangeRate <= 0, DiscountPercent поза 0–100 або повторені активні LineNo → 400.
- Чужий документ/товар за ID не читається і не змінюється; API повертає 404.
- Помилка запису руху відкочує весь POST: документ залишається DRAFT без номера, Batch і рухи не з’являються.
- v_stock_balance сумує кілька рухів для однієї пари BatchId / StorageLocationId та розділяє різні місця зберігання.
- Production не довіряє Development-заголовкам і повертає 401 без автентифікованого контексту.

## 6. Продаж, резерв і списання

Спочатку проведіть закупівлю GOODS. Отримайте партії через:

```text
GET /stock/available-batches?productId=<ProductId>&storageLocationId=<StorageLocationId>
```

Кожен елемент містить batchId, productId, storageLocationId, physicalQuantity, reservedQuantity, availableQuantity, purchaseDocumentDate, batchNumber. Повертаються тільки availableQuantity > 0; порядок — найстаріша дата закупівлі першою, потім Batch.CreatedAt / Batch.Id. SERVICE повертає порожній список.

Приклад body POST /sales (ціни завжди UAH, замініть ID на отримані значення):

```json
{
  "documentDate": "2026-10-20",
  "comment": "Продаж",
  "internalNote": null,
  "counterpartyId": "<ID з POST /counterparties>",
  "storageLocationId": "44444444-4444-4444-4444-444444444444",
  "priceTypeId": null,
  "discountPercent": 0,
  "lines": [
    {
      "id": null,
      "lineNo": 1,
      "productId": "<GOODS ProductId>",
      "batchId": "<BatchId з available-batches>",
      "quantity": 2,
      "unitPrice": 12
    }
  ]
}
```

Відповідь має document (тип SALE_INVOICE), реквізити sale та lines. Для SERVICE вкажіть його ProductId і batchId = null; він не потребує фізичного залишку, не резервує і не створює рухів. GOODS без Batch, SERVICE із Batch, Batch іншого Product або повторений Batch в активних рядках одного sale відхиляються з 400.

Для початкового physical = 5.25 та продажу 2 одиниць:

| Стан | PhysicalQuantity | ReservedQuantity | AvailableQuantity |
| --- | ---: | ---: | ---: |
| До продажу | 5.25 | 0 | 5.25 |
| DRAFT sale | 5.25 | 2 | 3.25 |
| POST sale | 3.25 | 0 | 3.25 |
| UNPOST sale | 5.25 | 2 | 3.25 |
| DELETE DRAFT sale | 5.25 | 0 | 5.25 |

- POST /sales/{id}/post не потребує body. Для GOODS з’являється негативний рух; SERVICE не створює рухів. Резерв цього документа зникає в тій самій транзакції.
- Перший POST присвоює SI-номер; повторний POST не змінює номер/audit/Version і не дублює рухи.
- PUT /sales/{id} передає поточну version, documentDate/comment/internalNote, реквізити sale та повний список активних lines. Відсутній старий рядок отримує removedAt, новий має id = null.
- При редагуванні з доступного залишку виключається тільки власний збережений reserve. Тому DRAFT, що зарезервував усі 5.25 одиниць, може редагувати свої ціни або кількість до 5.25; резерви інших документів залишаються обмеженням.
- POST /sales/{id}/unpost видаляє негативні рухи, повертає DRAFT та резерв. Прогнозований physical і available мають бути невід’ємними. Повторний UNPOST для DRAFT нічого не змінює.
- DELETE /sales/{id} встановлює DeletedBy/DeletedAt та повертає 204. Дані SaleInvoice/Line зберігаються; deleted DRAFT не резервує. POSTED спочатку потребує UNPOST; прямий DELETE дає 400.
- Резерв/POST понад залишок або небезпечний UNPOST повертає 409 Problem Details з `code: "INSUFFICIENT_STOCK"`. Жодних часткових змін не зберігається.
- UNPOST закупівлі теж не може забрати вже зарезервований або проданий товар. Після звільнення sale reserve та відновлення stock застосовуються попередні правила Batch/номера.
- Дата документа може бути backdated: на цьому slice перевіряється лише поточний залишок, без історичної перевірки.

## 7. PriceType → ціни → продаж

1. POST /price-types із `{ "name": "Роздрібна", "defaultMarkupPercent": 25, "active": true, "sortOrder": 0 }` повертає 201, згенерований id, organizationId та передані поля. DefaultMarkupPercent — numeric(7,4), >= 0. GET /price-types сортує за sortOrder, потім name/id. PUT передає повні реквізити типу цін.
2. Для вже створеної партії GOODS виконайте PUT /batches/{batchId}/prices/{priceTypeId} із `{ "unitPrice": 15 }`.
3. Для Product типу SERVICE виконайте PUT /products/{productId}/prices/{priceTypeId} із `{ "unitPrice": 100 }`. Для GOODS цей endpoint поверне 400.
4. У POST /sales виберіть отриманий priceTypeId. GOODS бере рекомендацію зі своєї партії, SERVICE — зі свого Product. Для автоматичної фактичної ціни пропустіть unitPrice або передайте null.

Приклад двох рядків такого продажу:

```json
{
  "lines": [
    {
      "id": null,
      "lineNo": 1,
      "productId": "<GOODS ProductId>",
      "batchId": "<BatchId>",
      "quantity": 2,
      "unitPrice": null
    },
    {
      "id": null,
      "lineNo": 2,
      "productId": "<SERVICE ProductId>",
      "batchId": null,
      "quantity": 1,
      "unitPrice": 120
    }
  ]
}
```

Доповніть body реквізитами з попереднього прикладу POST /sales і вибраним priceTypeId. У відповіді рядок GOODS матиме recommendedUnitPrice = 15, unitPrice = 15; SERVICE — recommendedUnitPrice = 100, unitPrice = 120. Рекомендація у вхідному JSON не потрібна й не є довіреною.

- Рівна або вища фактична ціна дозволена без спеціального дозволу. Значення 14.9999 при рекомендації 15 дасть 403 Problem Details з `code: "PRICE_BELOW_RECOMMENDED"`.
- ISalePricePermissionProvider передає CanSellBelowRecommendedPrice відповідно до sale.change_price активних ролей користувача; HTTP body та заголовки його не змінюють. Без права нижча ціна дає 403 на create/edit/POST; рівна/вища доступна без додаткового права.
- При відсутній ціні для вибраного PriceType POST/PUT sale дає 400 з `code: "PRICE_NOT_FOUND"`, без часткового документа чи резерву. Налаштована ціна 0 дозволена.
- При priceTypeId = null зберігається ручний режим: recommendedUnitPrice = 0. Від’ємна фактична ціна завжди заборонена.
- PUT ціни повторно оновлює той самий складений ключ, не додає дубль. Інші PriceType та партії можуть мати інші ціни того самого Product. Ціни іншої організації не читаються й не встановлюються.
- PUT DRAFT sale зберігає snapshot рекомендації для того самого ID рядка, ProductId, BatchId та PriceTypeId, навіть якщо прайс змінився. Пропущений/null unitPrice такого рядка зберігає його фактичну ціну. Новий рядок або явна зміна Product/Batch/PriceType отримує поточну рекомендацію; пропущений unitPrice нового вибору дорівнює їй. Відмова у дозволі порівнює UnitPrice зі snapshot, а не з пізнішим прайсом; невдалий PUT залишає попередню Version/ціни/резерв.
- GET, POST, UNPOST та повторний POST зберігають знімок цін рядка. Оновлення довідника саме не переоцінює документи. Перший POST повторно перевіряє доменний дозвіл щодо збереженої рекомендації; replay POSTED документа залишається idempotent.

## 8. Поточна собівартість і початкові ціни

1. Перед проведенням закупівлі створіть активний PriceType з defaultMarkupPercent = 25. Неактивні типи цін не беруть участі у початковому ціноутворенні.
2. Збережіть закупівлю з UnitPriceCurrency = 100, DiscountPercent = 10, ExchangeRate = 40. У DRAFT ще немає Batch, її собівартості у view чи BatchPrice.
3. Перший POST створює Batch. v_batch_cost для неї повертає UnitCostUah = 100 × 0.9 × 40 = 3600. Початковий BatchPrice.UnitPrice = 3600 × 1.25 = 4500. Для кожного іншого активного PriceType організації створюється своя ціна з його націнкою.
4. GET /batches/{batchId}/prices/{priceTypeId} повертає цю ціну. Якщо потрібно, її можна змінити явно через PUT; POST закупівлі більше не перезаписує її.
5. UNPOST зберігає BatchPrice. Після зміни UnitPriceCurrency на 120 поточний VIEW покаже 4320, але ціна лишиться 4500. Зміна ExchangeRate або DiscountPercent також змінює тільки обчислювану собівартість. Повторний POST не переоцінює Batch.
6. Зміна DefaultMarkupPercent не переписує попередні BatchPrice. Нові Batch, також для нового рядка після UNPOST, отримують початкові ціни з поточною націнкою. Старі Batch не отримують автоматично ціни пізніше доданих або активованих PriceType.
7. Продаж GOODS бере збережений BatchPrice, SERVICE — ProductPrice. Пізніша зміна прайсу з 4500 до 5000 не змінить RecommendedUnitPrice = 4500 уже створеного рядка sale, також після PUT, POST/UNPOST/re-POST.

VIEW містить BatchId, PurchaseInvoiceLineId, ProductId, UnitCostUah. Його можна прочитати SQL-запитом `SELECT * FROM v_batch_cost`; окремого cost endpoint цей slice не додає. Це звичайний обчислюваний VIEW, без збереженої собівартості. Розрахунок не округлює проміжну собівартість; тільки кінцевий початковий BatchPrice округлюється до 4 знаків. Direct purchase expenses відкладені. Помилка запису початкової ціни або перевищення numeric(20,4) відкочує весь POST, включно з Batch/StockMovement/номером документа.

## 9. Взаєморозрахунки

1. Звичайна закупівля 3 × 10 + 2 × 20 зі знижкою 10% має кінцеву суму 63 у її CurrencyId. POST записує один SettlementMovement: amount = -63; при ExchangeRate = 40.5, baseAmountUah = -2551.5. Removed-рядки не враховуються. OPENING_STOCK не записує settlement навіть із заданим контрагентом.
2. Продаж 2 GOODS × 12.5 + 3 SERVICE × 20 зі знижкою 15% має суму 72.25. POST записує один movement: amount = baseAmountUah = +72.25, currencyId = UAH 00000000-0000-0000-0000-000000000980. Для GOODS у тому самому POST також залишається складське списання; SERVICE не створює stock movement.
3. Повторний POST повертає той самий стан і не створює новий settlement. UNPOST видаляє рух цього документа; повторне проведення після редагування рахує актуальну суму. Помилка settlement відкочує зміни stock/цін/Document, помилка UNPOST — також попередньо видалені stock movements.
4. GET /settlements/balances?counterpartyId={id}&currencyId={id} повертає масив `{ "counterpartyId": "...", "currencyId": "...", "balance": 72.25 }`. Фільтри незалежні; без них — усі валютні баланси поточної організації. Рухів немає — рядка балансу немає.
5. GET /settlements/counterparties/{id} повертає counterpartyId, balances та movements. Кожний movement містить id, documentId, documentType, documentDate, counterpartyId, currencyId, amount, baseAmountUah та balanceAfterMovement. Список і баланси читаються з одного snapshot.

Для одного контрагента з такими документами:

| DocumentDate | Тип | Валюта | Amount | BalanceAfterMovement |
| --- | --- | --- | ---: | ---: |
| 2026-10-17 | SALE_INVOICE | UAH | 30 | 30 |
| 2026-10-18 | PURCHASE_INVOICE | UAH | -50 | -20 |
| 2026-10-19 | PURCHASE_INVOICE | інша CurrencyId | -7 | -7 |
| 2026-10-20 | SALE_INVOICE | UAH | 10 | -10 |

Підсумкові balances: UAH = -10, інша валюта = -7. Порядок проведення може відрізнятися від дат: query сортує DocumentDate, далі CreatedAt/DocumentId/MovementId. Running balance рахується окремо для кожної валюти через SQL SUM OVER; у таблиці його немає. Плюс — контрагент винен нам, мінус — ми винні йому. Чужий профіль/історія повертає 404, фільтр чужого контрагента — порожній масив.

Line/document total не зберігаються й не округлюються по рядках. Після header discount сума округлюється до 4 знаків; BaseAmountUah закупівлі рахується від кінцевого Amount × ExchangeRate, також із кінцевим округленням до 4 знаків. Balance, Debt, Advance, PaidAmount, PaymentStatus, InvoiceStatus не додаються. Оплати та надходження далі змінюють той самий settlement journal без прив’язки до конкретних invoices.

## 10. Грошові рахунки та документи

POST /money/accounts:

```json
{
  "name": "Каса UAH",
  "accountType": "CASH",
  "currencyId": "00000000-0000-0000-0000-000000000980",
  "active": true
}
```

AccountType: CASH/BANK/SAFE/OTHER. Відповідь має id, organizationId та ці реквізити без balance. Для використаного рахунку CurrencyId незмінний, також після UNPOST. GET /money/accounts повертає власні рахунки; назву, тип та Active можна редагувати через PUT.

Надходження оплати від контрагента: POST /money/receipts:

```json
{
  "documentDate": "2026-10-20",
  "comment": "Отримано оплату",
  "purpose": "SETTLEMENT",
  "counterpartyId": "<CounterpartyId>",
  "destinationAccountId": "<MoneyAccountId>",
  "destinationAmount": 100,
  "settlementCurrencyId": "00000000-0000-0000-0000-000000000980",
  "settlementAmount": 100
}
```

SourceAccountId/SourceAmount мають бути null або пропущені. Для UAH пропущений SettlementExchangeRate підставляється як 1. Перший POST /money/receipts/{id}/post створює MoneyMovement +100 та SettlementMovement -100; номер MR призначається лише тоді. Якщо продаж дав settlement +100, після надходження баланс контрагента = 0, рахунку = +100.

Оплата постачальнику: POST /money/payments:

```json
{
  "documentDate": "2026-10-20",
  "purpose": "SETTLEMENT",
  "counterpartyId": "<CounterpartyId>",
  "sourceAccountId": "<MoneyAccountId>",
  "sourceAmount": 60,
  "settlementCurrencyId": "00000000-0000-0000-0000-000000000980",
  "settlementAmount": 60,
  "settlementExchangeRate": 1
}
```

DestinationAccountId/DestinationAmount мають бути null. POST створює MoneyMovement -60 та SettlementMovement +60; номер MP. Для рахунку без надходжень баланс стане -60 — це валідно. Достатність коштів не перевіряється. Для settlement в іншій валюті передайте її CurrencyId та positive rate; BaseAmountUah = SettlementAmount × rate із кінцевим округленням до 4 знаків. Сума/валюта money руху та settlement можуть відрізнятися.

Для OWNER або OPENING_BALANCE передайте відповідний Purpose, потрібний account і positive amount. Counterparty не потрібний, settlementCurrencyId/Amount/ExchangeRate мають бути null. Null Purpose теж дозволений без settlement. Такі документи створюють тільки money рух.

Переказ: POST /money/transfers:

```json
{
  "documentDate": "2026-10-20",
  "sourceAccountId": "<SourceAccountId>",
  "destinationAccountId": "<DestinationAccountId>",
  "sourceAmount": 10,
  "destinationAmount": 400
}
```

Цей приклад валідний для різних валют, наприклад -10 із валютного рахунку та +400 на UAH рахунок. Якщо валюта однакова, SourceAmount і DestinationAmount мають бути рівні. Рахунки різні, обидва власні; transfer не має SETTLEMENT Purpose та ніколи не створює settlement. Курс 400/10 можна обчислити, але поле effective rate не зберігається.

- DRAFT не створює money/settlement рухів. PUT потребує поточної version та DRAFT, повертає нову Version. Невалідна сторона, сума, settlement-реквізити, невідомий enum чи чужі account/counterparty → 400; stale Version → 409; чужий документ/неправильний тип маршруту → 404.
- Повторний і паралельний POST не дублюють рухів і не змінюють номер/audit/Version. UNPOST видаляє money та відповідний settlement, повертає DRAFT, зберігає номер. Після Receipt +100 і Payment -80 баланс +20; UNPOST receipt залишає -80, що дозволено.
- POST/UNPOST атомарні для обох журналів. Помилка другого руху transfer не залишає першого; помилка settlement відкочує також money movement. Ні account balance, ні AllowNegativeBalance, operation_type, MovementDate чи CurrencyId у MoneyMovement не додаються.
- GET /money/balances повертає moneyAccountId, currencyId, balance з v_money_account_balance. Без рухів рядка немає; сума рухів 0 дає рядок із balance = 0. Інші організації не потрапляють у результат.

## 11. Операційні та прямі закупівельні витрати

Спочатку створіть категорію через POST /expense-categories:

```json
{
  "name": "Доставка",
  "parentId": null,
  "active": true
}
```

ParentId може посилатися на категорію своєї організації. PUT дозволяє переміщати піддерево без довільного обмеження глибини; self/ancestor cycles заборонені, також при паралельних запитах.

POST /expenses для direct expense:

```json
{
  "documentDate": "2026-10-20",
  "counterpartyId": "<CounterpartyId>",
  "expenseCategoryId": "<ExpenseCategoryId>",
  "currencyId": "00000000-0000-0000-0000-000000000980",
  "amount": 120,
  "exchangeRate": 1,
  "purchaseInvoiceId": "<POSTED PurchaseInvoice DocumentId>",
  "comment": "Доставка товару",
  "internalNote": null
}
```

Для операційної витрати purchaseInvoiceId = null. Amount > 0 до 4 знаків, ExchangeRate > 0 до 6 знаків. Усі посилання належать поточній організації. DRAFT має number = null, не створює ledger і не змінює cost. POST /expenses/{id}/post призначає EX-номер, створює settlement -120 та base -120, без MoneyMovement. Повторний POST повертає той самий стан. Оплата — окремий POST /money/payments із Purpose=SETTLEMENT: settlement +120 погашає загальний баланс; окремої allocation між документами немає.

Приклад розподілу: закупівля має 2 × 100 і 4 × 25, header discount 20%, курс 2. LineBaseUah = 320 та 160, total = 480. Direct expense 60 у власній валюті з курсом 2 дає 120 UAH. Частки — 80 та 40; UnitCostUah стає 200 та 50 замість 160 та 40. DRAFT expense та general POSTED expense цього не змінюють. UNPOST direct expense повертає собівартість до 160 та 40; змінити expense можна через PUT із поточною Version і повторний POST. BatchPrice та snapshot у збереженому sale не змінюються, навіть якщо expense додано після продажу. Історичний gross profit може перерахуватися.

Direct expense на OPENING_STOCK або DRAFT закупівлю → 400. Zero-total після знижки → 400 із code=INVALID_PURCHASE_EXPENSE_ALLOCATION. Поки невидалений Expense посилається на закупівлю, її UNPOST → 409 із code=DOCUMENT_HAS_DEPENDENCIES, також для DRAFT Expense. Спочатку UNPOST Expense та від’єднайте purchaseInvoiceId через PUT або логічно видаліть DRAFT. DELETE POSTED Expense → 400; DELETE DRAFT → 204, ставить Document.DeletedAt/DeletedBy, зберігає рядок Expense, прибирає його зі списку; GET та write-операції після видалення → 404.

GET /expenses?from=2026-10-01&to=2026-10-31&counterpartyId={id}&categoryId={id}&purchaseInvoiceId={id}&status=POSTED&currencyId={id} повертає документи поточної організації, без deleted. Усі фільтри optional і поєднуються через AND; from/to включні. GET /expense-categories повертає плоский список із ParentId для відтворення дерева.

## 12. Повернення покупця

GET /sales/{id}/returnable-lines доступний для POSTED sale та повертає SourceSaleLineId, ProductId, ItemType, BatchId, SoldQuantity, AlreadyReturnedQuantity, ReturnableQuantity, OriginalUnitPrice, OriginalDiscountPercent, OriginalNetAmount, AlreadyRefundedAmount, RefundableAmount. Агрегати враховують лише POSTED невилучені returns; DRAFT нічого не резервує.

POST /customer-returns:

```json
{
  "documentDate": "2026-10-20",
  "sourceSaleInvoiceId": "<POSTED SaleInvoice DocumentId>",
  "storageLocationId": "<Location already used by this organization>",
  "lines": [
    { "sourceSaleLineId": "<GOODS SaleInvoiceLineId>", "quantity": 2 },
    { "sourceSaleLineId": "<SERVICE SaleInvoiceLineId>", "quantity": 0, "refundAmount": 30 }
  ],
  "comment": "Повернення та часткова компенсація",
  "internalNote": null
}
```

GOODS: продано 4 × 12, discount 10%, net 43.2. Return 2 дає автоматичний RefundAmount 21.6; ручне значення для GOODS → 400. SERVICE: продано 1 × 100, discount 10%, net 90; компенсація 30 залишає refundable 60. Якщо пропустити RefundAmount нового service рядка, backend підставляє поточний залишок. Quantity=0 дозволено для фінансової компенсації; додатна quantity теж дозволена. GOODS quantity > sold мінус already returned → 409/RETURN_QUANTITY_EXCEEDED; SERVICE refund понад залишок → 409/RETURN_AMOUNT_EXCEEDED. Останній GOODS refund узгоджує округлення до 4 знаків із залишком net amount.

POST /customer-returns/{id}/post створює stock +2 у тому самому goods Batch, але у вибраному складі, та один UAH settlement -51.6. SERVICE stock не змінює; нових Batch і MoneyMovement немає. Номер CR призначається вперше, replay не змінює рухи/audit/Version. GET /customer-returns повертає власні невилучені документи з рядками. PUT потребує Version та повного списку рядків DRAFT; ID наявного рядка передайте для його збереження, без ID створюється новий, відсутні рядки DRAFT прибираються.

POST /sales/{id}/customer-return із body `{ "documentDate": "2026-10-20" }` створює порожній DRAFT із source sale та його складом. Можна передати StorageLocationId, Lines, Comment, InternalNote; перед POST додайте хоча б один рядок через PUT. Source має бути POSTED своєї організації, його UNPOST блокується будь-яким невилученим dependent return, включно з DRAFT → 409/DOCUMENT_HAS_DEPENDENCIES.

UNPOST повернення видаляє stock/settlement і повертає DRAFT. Якщо повернений товар уже продано або зарезервовано, операція → 409/INSUFFICIENT_STOCK без часткових змін. DELETE POSTED → 400; спочатку UNPOST, потім DELETE DRAFT → 204 із DeletedAt/DeletedBy, без фізичного видалення документа/рядків. Тоді source sale можна UNPOST за умови решти stock rules. Витрати, BatchPrice і поточний batch cost цей slice не змінює.

## 13. Повернення постачальнику

Source — лише POSTED PURCHASE_INVOICE поточної організації, OPENING_STOCK відхиляється. POST /purchases/{id}/supplier-return із body `{ "documentDate": "2026-10-20" }` створює порожній DRAFT із source та складом закупівлі; до POST додайте рядки через PUT. Або POST /supplier-returns:

```json
{
  "documentDate": "2026-10-20",
  "sourcePurchaseInvoiceId": "<POSTED PurchaseInvoice DocumentId>",
  "storageLocationId": "<Location already used by this organization>",
  "lines": [
    { "sourcePurchaseLineId": "<Active PurchaseInvoiceLineId>", "quantity": 2 }
  ],
  "comment": "Повернення постачальнику",
  "internalNote": null
}
```

У body немає Counterparty/Product/Batch/UnitPrice/Currency/ExchangeRate. Backend бере їх із source; GOODS only, quantity > 0 до 4 знаків. Якщо purchased 10, уже POSTED returned 3, physical 7, а sale reserve 2 — можна додати максимум 5. DRAFT SupplierReturn 2 збільшує reserved до 4 та зменшує available до 3, жодних journal movements немає. PUT із тією ж Version і quantity=2 виключає лише власний reserve і проходить; quantity=6 порушує доступність → INSUFFICIENT_STOCK. Quantity понад purchased мінус POSTED returned → RETURN_QUANTITY_EXCEEDED.

POST /supplier-returns/{id}/post створює stock -2 у original Batch і вибраному складі; reserve документа зникає. Якщо original price 100 у валюті закупівлі, discount 10%, rate 40 — settlement +180 у цій валюті та base +7200 UAH. Direct logistics expense 100 UAH може змінити v_batch_cost, але refund лишається +180/+7200. Current unit cost і BatchPrice після повернення незмінні. MoneyMovement не створюється.

UNPOST видаляє stock/settlement і повертає DRAFT: physical зростає на 2, reserved також на 2, available не змінюється. Перевіряється прогнозований current stock у тій самій транзакції; помилка будь-якого journal відкочує все. POST/UNPOST idempotent; номер SR, audit і Version при POSTED replay незмінні. DELETE POSTED → 400; спочатку UNPOST, потім DELETE DRAFT → 204, рядки зберігаються, резерв зникає. Будь-який невилучений SupplierReturn, також DRAFT, блокує Purchase UNPOST → 409/DOCUMENT_HAS_DEPENDENCIES.

GET /purchases/{id}/returnable-lines?storageLocationId={id} повертає SourcePurchaseLineId, ProductId, BatchId, PurchasedQuantity, AlreadyReturnedQuantity, RemainingReturnableByPurchase, PhysicalQuantity, ReservedQuantity, AvailableQuantity, MaxReturnableQuantity, OriginalUnitPriceCurrency, OriginalDiscountPercent, CurrencyId, ExchangeRate. Без storageLocationId використовується склад source purchase. AlreadyReturnedQuantity враховує лише POSTED невилучені SupplierReturn. MaxReturnableQuantity = MIN(RemainingReturnableByPurchase, AvailableQuantity), усі значення обчислювані. GET /supplier-returns повертає лише власні невилучені документи з рядками.

Спільні резерви захищають усі slices: sale не може забрати товар, зарезервований SupplierReturn, а UNPOST CustomerReturn не може видалити позитивний прихід, на який уже є SupplierReturn reserve. Для іншого складу перевіряється саме його stock; чужий/невідомий склад відхиляється за погодженим правилом належності.

## 14. Складське переміщення

POST /stock-transfers:

```json
{
  "documentDate": "2026-10-20",
  "fromLocationId": "<Source location already used by the organization>",
  "toLocationId": "<Different destination already used by the organization>",
  "lines": [
    { "lineNo": 1, "batchId": "<Existing Batch of this organization>", "quantity": 3 }
  ],
  "comment": "Переміщення між складами",
  "internalNote": null
}
```

From/To різні, обидва власні за погодженим правилом використання у purchase/sale. Batch належить організації через source PurchaseInvoice; ProductId/OrganizationId у body/рядках немає. Quantity > 0 до 4 знаків, LineNo > 0 та унікальний; повтор Batch у документі → 400. Невідомі/чужі Batch або location → 400, чужий документ → 404.

При physical=10 у From DRAFT 3 дає physical=10, reserved=3, available=7. To не отримує stock/reserve. Якщо sale резервує 2, SupplierReturn ще 2, загальний reserve=7, available=3; саме ці значення повертає `/stock/available-batches` для From. Transfer 4 → 409/INSUFFICIENT_STOCK. PUT потребує поточної Version та повного списку рядків, із ID наявних; власний saved reserve виключається з перевірки, інші лишаються. При зміні From/Batch попередній reserve звільняється, новий перевіряється в його локації.

POST /stock-transfers/{id}/post призначає ST-номер та створює StockMovement -3 у From, +3 у To, Batch той самий. Reserve цього документа зникає; source physical=7, destination physical=3. Нових Batch, MoneyMovement, SettlementMovement немає, cost/price не змінюються. Паралельний/повторний POST не дублює рухи й не змінює audit/Version.

UNPOST видаляє обидва рухи, повертає DRAFT і source reserve: physical From знову 10, reserved=3. Якщо destination 3 уже списано або зарезервовано sale/SupplierReturn/іншим transfer так, що після видалення +3 projected physical/available буде від’ємним, UNPOST → 409/INSUFFICIENT_STOCK без часткових змін. Після звільнення цих резервів/рухів UNPOST проходить. Перевіряється поточний stock, повної backdated validation немає. Номер зберігається після UNPOST/re-POST.

DELETE POSTED → 400; спочатку UNPOST, потім DELETE DRAFT → 204 із DeletedAt/DeletedBy та збереженими рядками. Reserve зникає, deleted документ не читається/редагується/проводиться. GET /stock-transfers підтримує optional from/to (включно), fromLocationId, toLocationId, status, фільтри поєднуються через AND. GET /stock-transfers/{id} повертає спільний Document, локації та рядки за LineNo. Vite/Caddy проксіюють `/stock-transfers` і підмаршрути.

## 15. Списання

Спочатку створіть ExpenseCategory через наявний API, потім POST /write-off-reasons:

```json
{
  "name": "Бій / пошкодження",
  "expenseCategoryId": "<ExpenseCategory of current organization>",
  "active": true
}
```

Name і category обов’язкові; чужа category → 400. Причина має одну категорію управлінських витрат, CRUD не створює Expense-документів або seed-довідників. GET/PUT /write-off-reasons/{id} і GET /write-off-reasons мають scope поточної організації.

POST /write-offs:

```json
{
  "documentDate": "2026-10-24",
  "storageLocationId": "<Location already used by this organization>",
  "writeOffReasonId": "<WriteOffReasonId>",
  "lines": [
    { "lineNo": 1, "batchId": "<Batch of current organization>", "quantity": 2 }
  ],
  "comment": "Пошкоджено під час зберігання",
  "internalNote": null
}
```

Batch і LineNo не повторюються, quantity > 0 до 4 знаків. Product/Organization/UnitCost/Amount у рядках відсутні. DRAFT 2 із physical=10 дає reserved=2, available=8 без movements. Резерв додається до sale/SupplierReturn/StockTransfer reserves та зменшує `/stock/available-batches`. Create/PUT понад доступний stock → 409/INSUFFICIENT_STOCK. PUT із поточною Version передає повний список рядків, виключає лише власний попередній reserve; ID потрібен для збереження рядка, відсутні DRAFT lines прибираються.

POST /write-offs/{id}/post створює stock -2 у вибраному складі та original Batch; reserve зникає, physical=8. WO-номер призначається лише вперше; повторний/паралельний POST не дублює рухів і не змінює audit/Version. Expense, SettlementMovement, MoneyMovement та Batch автоматично не створюються.

UNPOST видаляє issue, повертає DRAFT, physical=10, reserved=2. Projected physical/available перевіряються під Batch locks разом з іншими reserves, помилка відкочує всі зміни. DELETE POSTED → 400; спочатку UNPOST, потім DELETE DRAFT → 204, DeletedAt/DeletedBy та збережені рядки, reserve зникає. Deleted/чужі документи не читаються і не змінюються API.

GET /write-offs?from=2026-10-01&to=2026-10-31&storageLocationId={id}&writeOffReasonId={id}&expenseCategoryId={id}&status=POSTED підтримує всі ці optional фільтри через AND, дати включні. Category filter використовує поточний reason mapping.

SQL report helper v_write_off_cost має WriteOffId, DocumentId, DocumentDate, StorageLocationId, WriteOffReasonId, ExpenseCategoryId, TotalCostUah. Наприклад, звіт фактичних списань однієї організації (параметри задає caller):

```sql
SELECT cost.*
FROM v_write_off_cost cost
JOIN documents document ON document."Id" = cost."DocumentId"
WHERE document."OrganizationId" = @organizationId
  AND document."Status" = 'POSTED'
ORDER BY cost."DocumentDate", cost."WriteOffId";
```

VIEW включає невилучені DRAFT і POSTED документи, empty DRAFT має TotalCostUah=0; posted-only і organization scope задаються query. Вартість — SUM(quantity × current v_batch_cost.UnitCostUah) без snapshot або rounding до invoice precision. Списання 2 одиниць із unit cost=10 дає 20 UAH. Якщо пізній direct Expense додає 100 UAH на закупівлю 10 одиниць, unit cost стає 20 і вартість того самого історичного WriteOff — 40; UNPOST Expense повертає 20. Документні дані, BatchPrice і stock при зміні cost не переписуються. WriteOffReason визначає поточну ExpenseCategory; окремого Expense document немає.

## Integration tests

```sh
dotnet test backend/Trade.slnx
```

Тести самі запускають PostgreSQL 18 через Testcontainers та застосовують міграції в окремих базах. Охоплено всі slices: життєвий цикл DRAFT → POST → UNPOST → POST / DELETE, reserve без StockReservation, SERVICE/GOODS, виключення власного reserve, partial-write rollback, oldest available batches, конкурентне резервування/редагування/POST, гонку з UNPOST закупівлі, ціни партій/послуг, підстановку й збереження snapshot рекомендації після зміни прайсу та редагування, дозволені та заборонені зміни фактичної ціни, недовірені JSON/заголовки, паралельний price upsert, поточну собівартість/знижку/курс, націнки/активність PriceType, створення тільки початкових цін нових Batch, збереження цін після UNPOST/зміни закупівлі/націнки, rollback помилки початкового ціноутворення, SQL-схему/view, tenant isolation та Down/Up міграцій. Після кожного тесту очищується його Npgsql pool.

Opening settlement UI, Admin CRUD UI/MFA/SSO, historical stock validation, currency revaluation report, printed forms, бізнес-UI, PaymentAllocation, Obligation та Advance tables не входять до цих slices.
