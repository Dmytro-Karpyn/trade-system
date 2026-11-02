# UI redesign: Warm Sand + Terracotta

Обсяг: shared theme/components, application Shell, Dashboard і Sales. Backend, migrations, business rules, permissions, API contracts та URL не змінені. Інші feature screens отримали спільну тему, без окремого redesign.

## Тема і спільні компоненти

`src/theme/tokens.ts`: узгоджені кольори #2B211C / #C85A32 / #F6F1EA / #FFFDFC, hover, border, text, selected row, окремі status tones. Spacing 8, radius 8, sidebar 232 / collapsed 72, header 64, mobile navigation 64. ERP typography: h1 26, section 17, body 14, table 13, secondary 12–13 px. Компактні table cells; split-list row близько 44 px.

Білий дрібний текст на базовому terracotta має недостатній AA contrast, тому filled primary/nav використовують темний `actionInk`, hover — світлий текст на темнішому terracotta. Status badges мають окремі контрастні inks; перевірено ratios >= 4.5. Загальний focus-visible, keyboard row selection, skip link, aria-label icon actions, mobile buttons >= 44 px.

Створено PageHeader, FilterBar, DataTable, SplitViewLayout, StatusBadge, DocumentActions, KpiCard, MobileEntityCard, ConfirmDialog. ActiveBadge/PurchaseStatus делегують StatusBadge. EmptyState ущільнено; наявні LoadingState/ListLoading та RequestErrorState перевикористані, доступні через LoadingState/ErrorState exports без дублювання реалізації.

## Shell

Warm dark sidebar, terracotta active state, collapse/expand без localStorage. Header з search/quick-create placeholders, notification placeholder без вигаданих сповіщень, наявне user menu/logout. На viewport < 1200 — drawer і bottom navigation: Головна, Продажі, Склад, Гроші, Ще. Недозволені пункти приховані за чинною permission configuration; «Ще» відкриває решту меню. Верхня menu button також залишає доступ до повного drawer.

Mobile content має власний scroll container між header та navigation, із safe-area inset. Існуючі sticky actions документів розміщуються над navigation.

## Dashboard

Тільки GET /dashboard/summary та попередня permission filtering. Компактні KPI, sales/profit charts, структура sales/returns/netSales, permission-aware quick actions на існуючі форми, recent documents, money/settlement/stock/expense blocks і примітки API.

API не повертає розподіл GOODS/SERVICE та low-stock item list: структура не вигадується, «Низькі залишки» — пояснювальний empty state з посиланням на Склад. Валюти залишаються окремими; нових фінансових розрахунків немає. PeriodControls має optional compact layout лише для Dashboard; Reports зберігає попередній layout.

## Sales

На >= 1200 px `/sales` — дві незалежно прокручувані області: filters/list/pagination і selected detail. Selection click/Enter/Space не змінює route; явна дія відкриває `/sales/:id`. Вибраний рядок має selected background та aria-selected. Довгі назви в list не збільшують висоту рядка; повні реквізити доступні в detail.

Панель перевикористовує ту саму SaleForm, SaleInvoiceView, stock/price queries, totals, cost/profit permission checks, POST/UNPOST/DELETE. Вкладки «Документ»/«Повернення»; посилання на повернення лишилося доступним і в основному перегляді. Незбережені зміни захищені confirmation при зміні selection. Draft version baseline, line IDs, recommended snapshot і save-before-post confirmation збережені.

У split panel рядки форми показані компактними stacked controls, щоб не стискати desktop table на 1150 px у вузьку панель. На tablet — повна таблиця та окремий detail; < 900 px — mobile cards і окремий detail route. Save/Post sticky, Unpost/Delete відокремлені як danger actions.

Виправлено frontend race: після успішного POST query з excludeSaleId більше не виконується для POSTED документа. Перед POST in-flight own-reserve queries скасовуються; після POST вони лише інвалідуються, решта stock data refetch як раніше. Backend перевірки не змінено.

## Перевірка

Перевірено 2026-10-30:

| Перевірка | Результат |
| --- | --- |
| `npm --prefix frontend run lint` | PASS |
| `npm --prefix frontend test` | 407/407, 13 files, жодного пропуску |
| Додаткова перевірка Shell/Sales/Dashboard/shared після фінального cleanup | 99/99 |
| `npm --prefix frontend run build` | PASS, TypeScript + production Vite |
| `dotnet test backend/Trade.slnx -c Release --no-restore` | 522/522, 0 failed, 0 skipped |
| Backend source content до/після | Усі 237 файлів тотожні за SHA-256 |
| Chromium + production bundle/API, Caddy HTTPS, PostgreSQL 18 | PASS desktop 1440×1050, mobile 390×844 |
| Console/page/request errors після login | 0; warnings 0 |

10 нових frontend tests: sidebar active/collapse; permission-aware mobile bottom navigation/More; desktop navigation layout; split selection без navigation; selected row/keyboard Enter; mobile cards → detail; tablet table; shared status consistency; AA contrast; відсутність DRAFT-only stock refetch після POST. Попередні 397 tests залишилися зеленими.

Browser scenario: UI login → Dashboard → collapse/expand → Sales list → вибір DRAFT без зміни URL → dirty-selection confirmation → Save → POST → returns tab → UNPOST → direct detail URL → mobile Dashboard → cards → detail → sticky actions → More drawer → Home. Перевірено відсутність page horizontal overflow та розмір/позицію mobile Save (>=44 px, над navigation).

До login browser отримує один очікуваний HTTP 401 від `/auth/me` — нормальна перевірка відсутньої сесії, записана окремо від помилок authenticated smoke. Немає uncaught JS errors або інших console/request errors. Сирий підсумок: [browser-result.json](ui-redesign-screenshots/browser-result.json).

Скриншоти з disposable fixtures, не production values:

- [Desktop Dashboard](ui-redesign-screenshots/desktop-dashboard.png)
- [Desktop Sales split-view](ui-redesign-screenshots/desktop-sales-split.png)
- [Mobile Dashboard](ui-redesign-screenshots/mobile-dashboard.png)
- [Mobile Sales cards](ui-redesign-screenshots/mobile-sales-cards.png)
- [Mobile Sale detail / sticky actions](ui-redesign-screenshots/mobile-sale-detail.png)

Відтворюваний browser harness: `scripts/redesign-smoke.mjs`. Playwright залишається зовнішнім test tool, без нових frontend dependencies. Потрібні production frontend/API за HTTPS та окрема disposable БД з Development seed; задаються `PLAYWRIGHT_MODULE_PATH`, `AUDIT_ADMIN_PASSWORD`, `AUDIT_BASE_URL`, optional `AUDIT_OUTPUT`. Не запускати fixture harness на робочій БД. Пароль застосунку в коді не визначений.

Наступний узгоджений етап: [Purchases, Products / Services, Stock](ui-redesign-catalog-stock.md).
