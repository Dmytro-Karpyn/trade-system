# UI Consistency Audit

## Summary

Аудит від 31.10.2026 охоплює весь поточний frontend trade-system. Еталон: Warm Sand + Terracotta, data-first, split-view, compact ERP typography та mobile bottom navigation.

Статус: **CONSISTENT WITH MINOR ISSUES**. Зафіксовано **10 типів зауважень**, **9 виправлено**, **1 свідомо залишено як неблокуюче UX-покращення**. Кількість означає типи проблем, а не число файлів чи окремих occurrences.

Backend, business rules, routes, permissions і API contracts не змінено. Нових бізнес-функцій, таблиць, пакетів або глобальних stores немає. Поточні незакомічені зміни попередніх slices збережено.

## Theme

- Primary accent — Terracotta; text/outlined primary actions використовують контрастний primary ink із чинних tokens.
- Нового hardcoded blue primary або legacy blue backgrounds не знайдено. Warm borders, поверхні й status tones збережено.
- Default MUI popup shadows узгоджено через `tokens.overlayShadow` для Menu, Dialog і temporary Drawer. Permanent sidebar залишається без тіні.
- Login hero, різні розміри brand/icon і компактні chips залишено відповідно до призначення компонентів; новий redesign не виконувався.

## Typography

- Залишки окремих реалізацій page headings замінено `PageHeader` у new/detail screens Products, Counterparties, Purchases, Sales, Stock operations, Money, Expenses і Returns — 13 місць реалізації.
- Збережено h1 для окремого screen і h2 для detail у split-view; довгі назви можуть переноситися без збільшення body width.
- Section/table/label/helper typography використовує чинну MUI theme. Відмінна щільність compact list, таблиці з рядками документа та read-only report є навмисною.
- Числові formatter-и вже мали однакову точність 2–4 знаки. SERVICE prices переведено на `MoneyAmount` з UAH, правим вирівнюванням у таблиці та збереженням усіх 4 знаків. Нульова ціна відрізняється від «Не задано».

## Navigation

- Перевірено чинні основні routes: Dashboard; Sales/Purchases/Products list/new/detail; Stock overview/product/batch/transfer/write-off; Money dashboard/accounts/documents; Settlements; Expenses; Returns; усі Reports/Directories; Users/Roles/Audit; також Counterparties.
- Desktop sidebar, collapse, permissions і direct links збережено. На mobile лишаються bottom navigation та drawer «Ще».
- Sales і Counterparties тепер використовують той самий mobile filter sheet, що решта списків. Search залишається першим полем; наявні filters, їх значення, debounce і query parameters не змінено.

## Tables

- Shared `DataTable` зберігає keyboard Enter/Space selection, hover і `aria-selected`.
- Додано геометричний marker уздовж першої cell вибраного рядка: selection більше не залежить тільки від warm tint.
- Numeric/amount alignment перевірено; виправлено price column SERVICE. Фінансові від’ємні значення залишаються допустимими, без автоматичного success/error color.
- Stock movements використовують нейтральні signed quantities; звичайний outgoing movement не виглядає як помилка. Напрям визначається знаком і типом документа.
- Pagination, API-backed empty/error/loading states та server-filtered data збережено. Великі report/document tables можуть прокручуватися всередині контейнера.

## Forms

- Виправлено різну висоту закритих batch Select у Sale і Stock operations: поле показує короткий BatchNumber. Розгорнуті options зберігають Product, available quantity, дату й ціну, а helper-блоки — поточні залишки та own reserve.
- Chromium перевіряє висоти видимих single-line inputs/selects; допустима різниця native controls — до 1 CSS px. Multiline поля порівнюються окремо від single-line.
- Картку підсумків Sale обмежено до 500 px із responsive width: вона більше не створює великої порожньої білої секції. Формули totals не змінено.
- Labels, required indicators, validation messages, read-only fields, header spacing та mobile single-column forms збережено. Validation і write handlers не змінювалися.
- У Sale cost/profit, customer balance та write-off cost використано shared loading state з одним skeleton для компактного підсумку. SERVICE price types і Batch prices мають shared EmptyState; customer-balance read failure — shared ErrorState/retry.

## Split Views

Перевірено desktop selection у Sales, Purchases, Products, Money documents, Expenses, обох Returns, Users, Roles і Directories. Межі панелей, незалежний scroll, dirty guards та direct detail links збережено. На mobile detail відкривається окремим screen; довідник використовує чинний controlled editor dialog.

Proportions використовують shared `SplitViewLayout`; ширший catalog list Products — навмисна чинна конфігурація для додаткових колонок.

## Statuses

`PurchaseStatus` та `ActiveBadge` повторно використовують `StatusBadge`:

- DRAFT — warm gray;
- POSTED / ACTIVE — soft green;
- PENDING / WAITING — amber;
- ERROR / PROBLEM — muted red;
- INACTIVE — neutral gray.

Математичний знак суми чи кількості не прирівнюється до status. Settlement badges окремо показують «Нам винні», «Ми винні», «Нульовий баланс».

## Dialogs

- POST/UNPOST/delete використовують чинний ConfirmDialog, однакову структуру Cancel/Confirm та muted-red destructive action.
- Перевірено DRAFT Save/Post/Delete і POSTED Unpost/related actions без прямого delete.
- Popup surfaces отримали єдиний warm shadow. Source business warnings, save-before-POST, Version baseline і permission checks залишилися без змін.
- У mobile перевірено bounds діалогів цін і confirmations, прокручування контенту та збереження кнопок у межах viewport.

## Mobile

Viewport-и: **390×844** і **430×844**.

Перевірено Dashboard, Sales, Purchases, Products, Stock, Money, Settlements, Expenses, CustomerReturn, SupplierReturn, Reports, Directories, Users, Roles, Audit і Counterparties. Додатково перевірено десять document/editor flows зі sticky Save actions над bottom navigation.

Перевірено cards, filter sheets, «Ще», separate detail screens, named icon actions, відсутність horizontal body overflow і clipped dialogs. Збережено чинне `safe-area-inset-bottom` для navigation, main scroll area та filter sheet. Фізичний device safe area не емулюється самим числовим viewport.

## Accessibility

- Keyboard skip link має visible focus; перевірено keyboard row selection і `aria-selected`.
- Selection має геометричний marker, status — текст, quantity direction — знак, тобто зміст не передається лише кольором.
- Для видимих icon buttons перевірено accessible labels; для form fields збережено labels і required state.
- Контраст основних text/action tokens: body **13.44:1**, secondary **4.63:1**, primary action **4.55:1**, primary link **5.72:1**, destructive action **4.52:1**.
- Chromium додатково перевіряє фактичні кольори enabled contained buttons і status badges, а також touch height звичайних mobile buttons щонайменше 44 px.

## Legacy remnants

Code search охопив hex/RGB/blue usage, backgrounds, borders, shadows, radius, font sizes/weights, duplicated headings/filters/statuses/states та inline styles.

Primary blue remnants не залишилися. Native semantic hyperlinks, semantic information alerts і disabled states не перефарбовувалися механічно. Brand typography, login hero та декоративні borders не є окремими status або primary colors.

## Issues fixed

| ID | Знайдена розбіжність | Виправлення |
| --- | --- | --- |
| UI-01 | Окремі page headers мали інші gaps/позицію status та не використовували shared pattern | PageHeader у 13 new/detail/list implementations |
| UI-02 | Sales і Counterparties залишали повні filters над mobile list | Shared FilterBar mobile sheet зі збереженням чинних filters |
| UI-03 | SERVICE prices показували «грн» поруч із UAH в решті financial UI; price column був left-aligned | MoneyAmount/UAH, right alignment, узгоджений input/helper label |
| UI-04 | Negative stock movement мав error-red лише через знак | Нейтральна signed quantity; direction і тип документа збережено |
| UI-05 | Деякі cost/balance loading та prices empty/error states мали ad-hoc вигляд | Shared ListLoading/EmptyState/ErrorState з компактним summary loading |
| UI-06 | Візуальне виділення selected row залежало тільки від background tint | Structural edge marker + збережений aria-selected/keyboard selection |
| UI-07 | Popup shadows відрізнялися від warm overlay системи | Єдиний theme shadow token для menus/dialogs/temporary drawers |
| UI-08 | Rich batch option клонувався в закритий Select і збільшував його висоту | Короткий closed value у Sale/Stock; details лишилися в options/helper |
| UI-09 | Картка підсумків нового продажу розтягувалася на всю ширину з великим порожнім полем | Responsive summary surface обмежено до 500 px, totals і праве вирівнювання збережено |

## Issues intentionally left

**UI-10 — minor:** єдину progressive-disclosure policy «Ще фільтри» для всіх складних desktop filter sets не вводили. Наявні поля залишено видимими, доступними та функціональними; mobile використовує shared sheet. Визначення primary/advanced filters для кожного report/list є окремим UX-рішенням, а цей audit не повинен вводити новий великий interaction pattern.

Це не блокує реальне тестове використання. Семантичні blue links, inline Dashboard period selector, природно різні висоти multiline/compact/read-only контенту та login-specific typography свідомо збережено як допустимі відмінності, а не дефекти.

## Test results

```bash
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx -c Release --no-restore
```

Regression tests додано тільки для знайдених issues: selected row marker/keyboard, overlay shadow, mobile Sales/Counterparties filters на двох ширинах, SERVICE amount/currency/alignment/empty state, neutral stock movement та компактні batch selectors зі збереженими rich options. Старі SERVICE price assertions і два попередні Chromium scripts узгоджено з label UAH.

Backend: **522/522 passed**, 0 skipped, Release. Контроль SHA-256 усіх 237 backend source files підтверджує, що source і перелік backend файлів не змінилися від початку аудиту.

Frontend: **461/461 passed**, 15 test files. Додано 11 regression tests; lint і production build — **passed**. Routes, API client і query contracts не змінювалися.

## Browser result

Скрипт: `scripts/ui-consistency-smoke.mjs`. Реальна HttpOnly cookie/CSRF session через HTTPS Caddy; окрема disposable PostgreSQL database. Password — лише через `AUDIT_ADMIN_PASSWORD`; fixture IDs зберігаються тільки у `/tmp` для повторних локальних smoke runs.

Перевірки включають 66 окремих desktop routes, десять desktop selections, confirmations, focus, обидва mobile viewport-и, sticky actions, input heights, text/action contrast та console/network/page errors.

Результат: **passed**. Всього **134 screen visits**: 78 desktop із повторними detail/selection checks, 28 на 390 px і 28 на 430 px. Виконано **284 перевірки фактичного text/action contrast**; найменше виміряне значення цих checks — 4.55:1. Фінальна summary surface Sale не ширша за 500 px. **0 unexpected browser errors, 0 warnings, 0 React warnings, 0 MUI warnings**. Збережено 34 скриншоти.

Скриншоти й machine-readable results: [ui-consistency-audit-screenshots](ui-consistency-audit-screenshots/). Початковий anonymous `/auth/me` = 401 є очікуваним auth handshake та обліковується окремо від unexpected errors.
