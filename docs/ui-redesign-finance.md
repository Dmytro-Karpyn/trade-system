# UI redesign: фінанси та повернення

Warm Sand + Terracotta, data-first, desktop split-view, mobile bottom navigation.
Backend, схему БД, API contracts, routes, permissions та бізнес-правила цього slice не змінено.

## Екрани

- **Money:** поточні баланси рахунків як компактні cards, окремо за валютами; від’ємні значення без warning або блокування. Загальний баланс — із чинного `/dashboard/summary`. Надходження, виплати й перекази показують явно підписану кількість проведених документів із повних server-filtered списків, а не вигадану грошову суму. Документи відкриваються в правій панелі; збережено реквізити SETTLEMENT та derived effective rate валютного переказу.
- **Settlements:** компактна таблиця контрагент/валюта/баланс/стан, фільтри пошуку, валюти та стану. Вибраний контрагент показує окремі валютні баланси й хронологічні рухи з посиланнями на документи. Running balance надходить із API; знак не прирівнюється до success/error.
- **Expenses:** split-view, категорія, контрагент, тип витрати, валюта, сума, курс та derived UAH equivalent. Direct purchase expense має блок «Впливає на собівартість» і посилання на закупівлю. Current cost impact читається лише з чинного API за `cost.view`.
- **CustomerReturn:** split-view, source Sale, контрагент, склад, GOODS/Batch і SERVICE/компенсація. Ліміти й refund залишаються backend-розрахунками.
- **SupplierReturn:** split-view, source Purchase, Batch, available/returnable, власний резерв і refund. DRAFT явно пояснює reserve, POSTED — складський рух. Після POST власні DRAFT-only preview/availability queries скасовуються та позначаються stale без некоректного refetch; інші залежні дані оновлюються.

Reports, Directories, Administration та Audit не редизайнено.

## Спільний UI

Повторно використано `PageHeader`, `FilterBar`, `DataTable`, `SplitViewLayout`, `DocumentActions`, `ConfirmDialog`, status/loading/error/empty states.

Додано `MoneyAmount`, `CurrencyAmount`, `BalanceBadge`, `FinancialSummaryCard` та локальний `useDetailSelection`. Amount використовує tabular numbers, nullable amount показує «—». Settlement badge однаково нейтральний для додатного, від’ємного й нульового балансу.

Desktop selection не змінює URL списку; прямі detail links і modifier-click збережені. Перехід до іншого документа не відкидає dirty форму без confirmation. DRAFT: Save/Post/Delete; POSTED: Unpost та чинні related actions, без прямого Delete. Save-before-POST, Version baseline та чинні permission guards збережені.

Mobile використовує cards, filter bottom sheet та окремі detail screens. Sticky actions мають touch target не менше 44 px і залишаються над bottom navigation.

## Перевірки

Команди:

```bash
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx -c Release --no-restore
```

Нові frontend regression tests перевіряють фінансові знаки/форматування, account cards, split selection усіх трьох money documents, валютні баланси/history, direct expense/cost permission, обидва returns, mobile sheets/cards та відсутність DRAFT-only own-reserve refetch після SupplierReturn POST. Чинні тести бізнес-форм залишаються в suite.

Результат: frontend **436/436**, 14 test files; backend Release **522/522**, 0 skipped; lint та production build — passed. Порівняння SHA-256 усіх 237 backend source files із початком цього slice підтвердило відсутність backend змін і нових backend файлів.

Chromium smoke: `scripts/finance-redesign-smoke.mjs`, реальні HttpOnly cookie/CSRF, HTTPS Caddy та окрема disposable PostgreSQL БД. Скрипт потребує `AUDIT_ADMIN_PASSWORD` із середовища, не містить production credentials. Перед запуском треба підготувати ізольований backend із development seed; не запускати проти робочої БД.

```bash
AUDIT_BASE_URL=https://localhost:8445 \
PLAYWRIGHT_MODULE_PATH=/path/to/playwright-core/index.mjs \
node scripts/finance-redesign-smoke.mjs
```

Перевірено desktop 1440×1050 та mobile 390×844: Money dashboard; receipt/payment/transfer Save/POST/UNPOST; settlements balances/history; direct Expense POST/UNPOST; CustomerReturn GOODS/SERVICE; SupplierReturn reserve/POST/UNPOST; mobile filter sheets/cards, відсутність page overflow та розташування sticky actions.

Скриншоти: [ui-redesign-finance-screenshots](ui-redesign-finance-screenshots/). Результати Chromium — `result.json` у цій самій теці. Початковий anonymous `/auth/me` = 401 є очікуваним auth handshake і обліковується окремо від unexpected browser errors.
