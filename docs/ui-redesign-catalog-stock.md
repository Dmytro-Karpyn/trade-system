# Redesign: Purchases, Products / Services, Stock

Продовження Warm Sand + Terracotta / Data-first / Split-view. Backend, migrations, domain rules, API contracts і routes не змінені. Інші business modules у цьому slice не перероблялися.

## Purchases

Desktop >=1200 px: shared SplitViewLayout, independent scroll, compact filters/list/pagination ліворуч, selected Purchase detail праворуч. Click або Enter/Space вибирає документ у `/purchases`; explicit open залишає `/purchases/:id`. Selected row використовує теплий tint та aria-selected. Перехід до іншого документа з dirty формою потребує confirmation.

Tabs: Загальне, Товари, Партії, Витрати, Ціни. General залишає повну робочу форму для швидкого введення; Goods приховує загальні реквізити без remount форми/втрати значень. У DRAFT додаткові posted-only tabs вимкнені. Posted read-only; current Batch / BatchPrice / purchase expenses беруться тільки з чинних APIs. Cost permission збережено. Історія та друк не додані: цих entity-specific views/actions раніше не було.

Збережено supplier/location/currency/exchange rate/external number/date/discount/comments, стабільні line Id та Version baseline, protected Product після створення Batch, save-before-post confirmation, failed-save blocking POST і no-duplicate-create після failed POST. Footer містить gross, discount, total у валюті та еквівалент UAH. Дії через shared DocumentActions/ConfirmDialog; Unpost/Delete відокремлені як destructive.

Нова закупівля лишається в `/purchases/new`. Mobile — cards, direct detail, goods line cards і sticky actions над bottom navigation. Для вузького desktop detail використано компактні goods tables, без розширення всієї сторінки.

## Products / Services

Desktop split-view зі збереженням `/products`, `/products/new`, `/products/:id`; local selection, keyboard selection і dirty-selection confirmation. Список містить Code, Name, Type, Category, Unit, price indicator, Status, actions; barcode доступний у detail та широкому списку. Коди/назви не перекривають сусідні cells; довгі значення мають ellipsis/title.

Search/type/active і debounce залишилися серверними. GET /products не має category parameter, але повертає повний відфільтрований масив; category filter застосовується до цього повного результату перед наявною client pagination. Нових endpoint/DTO fields немає.

Tabs General / Prices, для GOODS додатково Stock / Batches за stock.view. SERVICE перевикористовує існуючий ProductPrice editor; active PriceTypes, «Не задано», settings.manage на write, validation/initial-price partial failures збережені. Рядки цін компактні й адаптивні. GOODS не викликає ProductPrice API/editor: helper «Ціни товару визначаються по партіях», перехід до batch data. Stock/batch sections перевикористовують StockProductDetail та existing stock projections без вкладених дубльованих tabs.

Mobile: cards, окремий detail route, SERVICE price rows, GOODS stock summary cards. Product metadata form з shared sticky DocumentActions; Attributes не очищаються при PUT.

## Stock та складські документи

Overview лишається таблицею на всю ширину, з grouping Product+Location / Batch+Location, explicit retail/wholesale PriceTypes, filters і cost.view. Desktop selection відкриває right panel без зміни `/stock`; direct Product/Batch routes доступні. Panel можна закрити кнопкою або Escape.

Product panel: balances by location, batches, prices, movements, reservations із source documents/quantity/author, коли він є в API. Batch panel/direct detail: source purchase, current prices, balances, movements/reservations; немає Batch Active чи New Batch.

KPI: кількість унікальних товарів у поточному повному result; після selection — physical/reserved/available саме вибраного backend row. Загальні quantities/valuation не вигадані: API не має summary projection, а кількості різних одиниць не сумуються. Values не зберігаються у frontend stock store; selection містить лише IDs.

Transfer / WriteOff: компактні headers/requisites/line tables/cards, row count, shared actions/confirmation. Кількості різних units не складаються в один total. WriteOff current cost лишається backend query за cost.view; cost summary перед sticky actions, без snapshot/cost fields. Збережені Version/line IDs, own reserve, POST/UNPOST/DELETE rules.

Виправлено тільки frontend query lifecycle: після POST StockTransfer/WriteOff запит з DRAFT-only excludeDocumentId скасовується/інвалідується без refetch; інші stock queries оновлюються. Backend validation не послаблювалася.

## Shared UI та permissions

Повторно використано PageHeader, FilterBar, DataTable, SplitViewLayout, StatusBadge через PurchaseStatus/ActiveBadge, DocumentActions, KpiCard, MobileEntityCard, ConfirmDialog та наявні loading/error/empty states. Нових дубльованих primitives немає. Shared FilterBar має optional mobile bottom sheet; інші екрани з default props зберігають layout. DataTable підтримує min width; SplitViewLayout — ширину list pane; ConfirmDialog — optional busy label.

Bottom navigation збережена: Головна / Продажі / Склад / Гроші / Ще. Purchases/Products доступні через More за тією ж navigation permission configuration. Нова permission subsystem не створена.

## Перевірка

Перевірено 2026-10-30:

| Перевірка | Результат |
| --- | --- |
| Frontend lint | PASS, 0 errors / 0 warnings |
| Повний frontend suite | 420/420, 13 files |
| Frontend production build | PASS, TypeScript + Vite |
| Backend Release tests | 522/522, 0 failed / 0 skipped |
| Backend SHA-256 до/після | Усі 237 файлів тотожні |
| Chromium desktop | PASS, 1440×1050 |
| Chromium mobile | PASS, 390×844 |
| Browser page errors / unexpected console/network errors / warnings | 0 / 0 / 0 |

13 нових frontend tests: Purchase split selection/selected row/no route change; dirty line retention across tabs; purchase mobile cards/filter sheet/direct route; Product keyboard selection/permissions/no GOODS price editor; SERVICE prices in split view/missing price; mobile product filters/direct route; mobile GOODS stock API/cards; Stock selected row/panel/reservations; Batch panel/no Active/no New Batch/cost hidden; Stock mobile sheet/cards; mobile WriteOff/no cost permission; no DRAFT-only candidate refetch after operation POST; Edit from Prices відкриває General form. Попередні 407 tests лишилися зеленими. Оновлено лише selector назви Purchase tab «Витрати» відповідно до redesign; підписи таблиць витрат і pending action збережені.

Chromium: login → Purchase list/selection → DRAFT edit/save → POST → Goods/Prices/Expenses → UNPOST/re-POST → Product SERVICE prices 300→350 та Edit from Prices → GOODS stock/batches → Stock row/panel/reservations → Batch panel/direct URL → mobile More/Purchases/Products → mobile filters/cards/detail → Stock → Transfer/WriteOff create/save/POST/UNPOST. Виміряно mobile action target >=44 px, action у viewport над navigation, відсутність horizontal page overflow.

До login один очікуваний HTTP 401 від `/auth/me` записано окремо як перевірку відсутньої сесії. Після login немає неочікуваних console/network errors або warnings. [Сирий browser summary](ui-redesign-catalog-stock-screenshots/browser-result.json).

Скриншоти з disposable fixtures, не production values:

- [Desktop Purchases](ui-redesign-catalog-stock-screenshots/desktop-purchases-split.png)
- [Desktop SERVICE prices](ui-redesign-catalog-stock-screenshots/desktop-service-prices.png)
- [Desktop GOODS stock/batches](ui-redesign-catalog-stock-screenshots/desktop-goods-stock.png)
- [Desktop Stock panel](ui-redesign-catalog-stock-screenshots/desktop-stock-panel.png)
- [Desktop Batch detail](ui-redesign-catalog-stock-screenshots/desktop-batch-detail.png)
- [Mobile Purchases](ui-redesign-catalog-stock-screenshots/mobile-purchases.png), [detail](ui-redesign-catalog-stock-screenshots/mobile-purchase-detail.png)
- [Mobile Products](ui-redesign-catalog-stock-screenshots/mobile-products.png), [SERVICE prices](ui-redesign-catalog-stock-screenshots/mobile-service-prices.png)
- [Mobile Stock](ui-redesign-catalog-stock-screenshots/mobile-stock.png)
- [Mobile Transfer](ui-redesign-catalog-stock-screenshots/mobile-transfers.png)
- [Mobile WriteOff](ui-redesign-catalog-stock-screenshots/mobile-write-offs.png)

Browser harness: `scripts/catalog-redesign-smoke.mjs`, тільки disposable dataset. Задати `PLAYWRIGHT_MODULE_PATH`, `AUDIT_ADMIN_PASSWORD`, optional `AUDIT_BASE_URL`, `AUDIT_OUTPUT`, `CHROMIUM_PATH`. Playwright залишається зовнішнім test tool; frontend dependencies/lockfile не змінені. Fixtures створюються через наявні API; пароль надходить тільки через env.
