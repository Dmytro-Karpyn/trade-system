# Продажі / SaleInvoice UI

Маршрути: `/sales`, `/sales/new`, `/sales/:id`. Список має серверний пошук за номером, коментарем і покупцем, період включно з обома датами, покупця, склад, тип ціни та статус; сторінки списку перемикаються у браузері. Суми обчислюються з активних рядків після header discount, у таблицях БД totals немає. На вузьких екранах список і рядки документа показуються картками.

## Чернетка та ціни

Спільна форма створює й редагує DRAFT: дата, покупець, склад, тип ціни, знижка, коментар і внутрішня примітка. У виборі покупців лише активні IsCustomer. Існуючий системний «Роздрібний покупець» підставляється в новий документ; UI його не створює. Для звичайного покупця поточний UAH баланс читається з settlement API за наявності `settlement.view`.

GOODS потребує Batch цього Product, SERVICE завжди передає BatchId = null та не запитує складські залишки. Номенклатура шукається за назвою, кодом і штрихкодом. Для товарів показано фізичний залишок, загальний резерв і доступну кількість. Селектор партій показує дату закупівлі, номер, кількість і ціну; порядок — найстаріша закупівля першою.

«Підібрати партії» пропонує розподіл кількості GOODS по найстаріших доступних Batch. Це лише рекомендація з `available-batches`: зміни зберігаються звичайним POST/PUT продажу, остаточну перевірку залишків і резервування виконує backend у своїй транзакції. INSUFFICIENT_STOCK підсвічує товарні рядки та пропонує перепідбір.

Для власного DRAFT запит передає `excludeSaleId`. Backend перевіряє організацію, існування та статус документа і повертає додаткові `OwnReservedQuantity` та `AvailableForDocument` тільки для його збережених рядків у тому самому складі. Базові PhysicalQuantity, ReservedQuantity й AvailableQuantity залишаються загальними, `available = physical - reserved`. Це дозволяє редагувати навіть повністю зарезервовану власним документом партію, не приховуючи резерви інших документів.

Рекомендовану ціну нового вибору читаємо через read-only query: BatchPrice для GOODS, ProductPrice для SERVICE. Збережений RecommendedUnitPrice — backend snapshot; зміна прайсу та фоновий refresh його не замінюють. Frontend не передає рекомендацію або totals у write DTO. Пропущена фактична ціна нового рядка означає рекомендовану; очищення поля незміненого наявного рядка зберігає його фактичну ціну відповідно до backend API.

Ціна нижче рекомендованої блокується без `sale.change_price`; з правом показується попередження. Backend повторно перевіряє це право. Header discount і підсумки — попередній розрахунок форми, усі продажі UAH.

## Життєвий цикл і конфлікти

Перед POST запитується підтвердження, потім зберігається змінена форма. Failed save блокує POST; якщо новий DRAFT уже створено, невдале проведення не спричиняє повторного створення документа. PUT використовує версію початкових даних форми та стабільні Id рядків; фоновий query refresh не підміняє редаговану версію. VERSION_CONFLICT пропонує явно завантажити актуальні дані.

POSTED — read-only: номер, реквізити, рядки, рекомендовані й фактичні ціни та підсумки. StockMovement і SettlementMovement створює виключно backend. Після UNPOST форма знову DRAFT і товарні рядки резервують залишок. Залежне повернення дає повідомлення: «Продаж не можна розпровести, оскільки на його основі вже створено повернення». DELETE доступний лише DRAFT та виконує logical delete. Save/Post закріплені внизу форми, зокрема на mobile.

Мутації оновлюють detail cache й інвалідують sale, stock, settlement та counterparty queries. Використано спільні cookie/CSRF, 401/403 і validation error handlers; автоматичного повторення write-запитів немає.

## Доступ і read API

UI використовує наявні `sale.view`, `sale.create`, `sale.post`, `sale.unpost`, `sale.delete`, `sale.change_price`. Для пошуку товарів потрібне існуюче `product.view`, партій — `stock.view`, балансу покупця — `settlement.view`. Без lookup permissions показано пояснення; UI не обходить API authorization.

Додані тільки read queries та DTO, без таблиць, міграцій або змін Sale write rules:

- `GET /sales` — список і фільтри `search`, `from`, `to`, `counterpartyId`, `storageLocationId`, `priceTypeId`, `status`;
- `GET /sales/lookups` — склади поточної організації;
- `GET /sales/price-options?productId=…&priceTypeId=…` — ціни для нового вибору;
- `GET /sales/{id}` — додаткові назви продуктів, партій, покупця та автора проведення;
- `GET /stock/available-batches` — необов’язковий `excludeSaleId` для власної чернетки;
- `GET /sales/{id}/cost` — CurrentCost, тільки `cost.view`;
- `GET /sales/{id}/profit` — NetSale, GrossProfit, Margin, тільки `profit.view`, без поля CurrentCost.

Показники POSTED розраховує backend: NetSale після знижки, CurrentCost через кількості GOODS × поточний `v_batch_cost`, GrossProfit = NetSale − CurrentCost, Margin = GrossProfit / NetSale × 100. Для нульового NetSale маржа не визначена. SERVICE на поточному етапі не має собівартості. Це поточна оцінка: зміни direct Expenses можуть змінити собівартість і прибуток, історичних snapshots немає. Cost і profit незалежно приховані без відповідного права.

## Перевірки

39 frontend-тестів охоплюють список і фільтри, покупців і роздрібний default, GOODS/SERVICE, власний резерв, FIFO-підбір, ціни/snapshots/permissions, знижку, DRAFT/POST/UNPOST/DELETE, помилки, залежне повернення, version conflict та mobile. 7 додаткових PostgreSQL integration tests перевіряють read queries, фільтри/суми, organization isolation, власний резерв у правильному складі, ціни/snapshots, поточну собівартість/прибуток, відсутність збережених totals і незалежну авторизацію cost/profit.

Повні перевірки: frontend lint, 158 frontend-тестів, production build; 457 backend integration tests і Release build без warnings/errors. Модель EF відповідає останній міграції. Реальний Chromium smoke використовує окрему PostgreSQL 18, Identity cookie/CSRF і production frontend за HTTPS Caddy; робоча dev-база не змінюється.

Chromium smoke 2026-10-28 пройшов: GOODS розподілено 10 + 2 по двох партіях, DRAFT має physical 15 / reserve 12 / available 3; POST дає physical 3 / reserve 0 та один settlement +1395 UAH після 10% знижки. UNPOST відновлює physical 15 / reserve 12 і прибирає settlement; залежний CustomerReturn блокує UNPOST. Після зміни BatchPrice збережена рекомендація лишається 125. Окремий SERVICE sale проведено з settlement +100 UAH, SQL підтвердив 0 StockMovement. Mobile картки, видимість Save/Post, відсутність горизонтального overflow та runtime errors перевірені в Chromium.

Повернення, платежі, друк, POS, scanner та quick sale UI не входять до цього slice.
