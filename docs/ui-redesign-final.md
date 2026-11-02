# Фінальний UI/UX redesign

Warm Sand + Terracotta, компактна ERP typography, data-first layout, desktop split-view і mobile bottom navigation. Backend, схему БД, бізнес-правила, routes, permissions та контракти API в цьому slice не змінено.

## 1. Reports

- `/reports` — компактний каталог семи звітів із коротким описом.
- Усі звіти використовують `PageHeader`, спільний `FilterBar`, компактні KPI, `DataTable` та CSV action. На mobile фільтри відкриваються в нижній sheet; великі звіти мають горизонтальний scroll усередині таблиці.
- Sales зберігає server summaries: quantity, gross sales, discounts, returns, net sales. Profitability показує поточну собівартість за `cost.view`, gross profit, margin та стриманий below-cost badge; повний рядок не фарбується червоним.
- Stock зберігає tabs «Поточні залишки», «За партіями», «Рух товару», «Списання», чинні фільтри й cost guards.
- Purchase/Money/Expense/Settlement використовують той самий layout. Від’ємний money balance допустимий; settlement badges нейтральні й узгоджені з основним модулем взаєморозрахунків.
- Формули та CSV escaping/formula protection збережено. CSV експортує поточні видимі дані після permission filtering. Нових графіків або вигаданих показників немає; dashboard не дублюється.

## 2. Directories

- `/directories` — компактний каталог семи чинних довідників з icons/name/description; без додаткових запитів за декоративними counts.
- Категорії товарів і витрат використовують спільний `DirectoryEntries`: читабельна вкладеність, повні paths, status і мінімальні дії. Обмеження візуального відступу не обмежує глибину дерева.
- Desktop — список/tree з пошуком і status filter ліворуч, редактор праворуч. Mobile — cards і той самий editor у controlled dialog.
- Категорія товарів зберігає Name/Parent/Description/SortOrder/Active. Категорія витрат — тільки наявні поля. Parent self/descendant choices і backend cycle validation збережено.
- Units/PriceTypes/Locations/WriteOffReasons мають однаковий compact list/editor pattern. Валюти лишаються read-only.
- PriceType helper пояснює початкову націнку нових партій та незмінність наявних BatchPrice. У Chromium зміна націнки додатково перевірена порівнянням існуючої BatchPrice до/після.

## 3. Administration

- `/admin` — компактні секції Users/Roles/Audit без вигаданих metrics.
- `/admin/users` — desktop list + selected user editor; поля list: name/username, roles, active, last login. Збережено прямі detail/new routes, створення, редагування, confirmation для deactivate/activate та відкликання сесій backend.
- `/admin/roles` — split-view; праворуч форма ролі та дев’ять груп permissions у compact accordions. Жодних нових permission codes або auth mechanisms.
- Dirty selection у Users/Roles не відкидається без confirmation. Після успішного user save dirty state очищається.
- Last-admin conflict показує чинне зрозуміле повідомлення. Inactive role спочатку потребує активації та збереження. Password/hash не виводяться для наявного користувача.

## 4. Audit

- Спільний PageHeader/FilterBar, data-first table, server filters і pagination; mobile — cards.
- Вибраний event відкриває read-only right drawer. Збережено документальні й entity links за чинними permissions.
- Raw JSON замінено порівнянням field/old/new. Вкладені ключі мають читабельний path; відсутні, null і boolean values показано явно.
- Лише змінені values отримують легкий old red/new green tint. Edit/delete відсутні.

## 5. Спільні компоненти

Повторно використано `PageHeader`, `FilterBar`, `DataTable`, `SplitViewLayout`, `DocumentActions`, `ConfirmDialog`, `KpiCard`, `MoneyAmount`, `BalanceBadge`, `ActiveBadge`, `MobileEntityCard`, `DocumentLink` та наявні loading/error/empty states.

Додано спільні `CatalogSections`, `EditorSurface` та `DirectoryEntries`; вони замінюють окремі landing/tree/dialog implementations. `AuditValueDiff` залишається локальним read-only відображенням аудиту. Нових global stores, filter architecture або UI dependencies немає.

## 6. Legacy visual remnants

Пошук hex/RGB/blue usage по всьому `frontend/src` виявив стару синю декоративну палітру login, сині/фіолетові badges ролей контрагентів і холодний фон footer форми контрагента. Їх замінено чинними theme tokens. Semantic information alerts не перефарбовувалися механічно. Довгі назви у вузьких списках отримали ellipsis/title; дії довідників і compact admin tables — доступні іконки з aria-label.

## 7. Desktop/mobile

Desktop: незалежний scroll панелей, selected rows, direct links та dirty guards. Mobile: cards для довідників/users/roles/audit, sheets для фільтрів, controlled directory editor, direct user/role detail, permissions accordions і audit drawer. Mobile bottom navigation та «Ще» не змінено.

## 8–10. Tests, backend, lint/build

```bash
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx -c Release --no-restore
```

Frontend regression tests додано для shared report layout, нейтральних settlement signs, below-cost style, category/expense tree split editor, dirty guards, mobile editor, directory search, users/roles split-view, permission groups, last-admin message та mobile audit diff/filters. Наявні assertions бізнес-форм збережено; selectors оновлено там, де raw JSON замінено field diff або власний report drawer замінено спільним FilterBar.

Frontend: **450/450 passed**, 14 test files; після фінального ущільнення списків management/expense regression suite також пройшов **82/82**. Lint та production build — **passed**.

Backend Release: **522/522 passed**, 0 skipped. SHA-256 порівняння всіх 237 backend source files із початком цього slice підтверджує відсутність змін і нових backend файлів.

## 11. Chromium smoke

`scripts/final-redesign-smoke.mjs`: реальний HttpOnly cookie/CSRF login через HTTPS Caddy, окрема disposable PostgreSQL БД; production data не використовуються. Пароль передається через `AUDIT_ADMIN_PASSWORD`, він не записаний у скрипт.

Перевірено desktop 1440×1050 та mobile 390×844: Reports landing, усі сім reports, CSV download, stock tabs, below-cost, Directories, Product/Expense Categories, PriceTypes, Admin landing, Users save, Roles permissions save, Audit server filters та реальний diff 25 → 35. Mobile: sheets/cards, directory editor dialog, direct user/role detail, permission accordions, audit drawer; без page overflow.

Chromium desktop/mobile — **passed**, **0 unexpected browser errors**, **0 warnings**. Скриншоти та machine-readable results: [ui-redesign-final-screenshots](ui-redesign-final-screenshots/). Очікуваний initial anonymous `/auth/me` = 401 обліковується окремо від unexpected browser errors.
