# Frontend shell

React 19 / TypeScript / Vite, MUI theme, TanStack Query, React Hook Form + Zod. React Router керує browser routes. UI використовує існуючі бізнес-правила backend; погоджені мінімальні довідники одиниць і складів описані у відповідних UI slices.

## Сторінки

`/login` — форма входу. `/` — dashboard shell із сімома порожніми фінансовими показниками та стрічкою операцій без вигаданих даних. Стан з’єднання читається з існуючого `/health`.

`/products`, `/products/new`, `/products/:id` — робочий CRUD номенклатури: [products-ui.md](products-ui.md).

`/counterparties`, `/counterparties/new`, `/counterparties/:id` — список, create/edit, загальні дані та взаєморозрахунки: [counterparties-ui.md](counterparties-ui.md).

`/purchases`, `/purchases/new`, `/purchases/:id` — список, DRAFT, POST/UNPOST, партії та ціни: [purchases-ui.md](purchases-ui.md).

`/sales`, `/stock`, `/money`, `/expenses`, `/settlements`, `/reports`, `/directories`, `/admin` — placeholders відповідних розділів. Пряме відкриття або перезавантаження URL підтримується Vite та Caddy. `/forbidden` показує «Недостатньо прав»; невідомий URL — сторінку 404. Усі routes, крім `/login`, вимагають сесію.

Layout: desktop sidebar від 1200 px, drawer для tablet/mobile, top bar з поточним розділом, disabled placeholders пошуку/створення та меню поточного користувача. Drawer закривається кнопкою, Escape, backdrop та після переходу. Mobile bottom navigation немає. Дизайн-токени та глобальні MUI налаштування — `src/theme/`.

## API та сесія

`VITE_API_BASE_URL` задає API base URL на етапі Vite build. За замовчуванням `/api`; приклад у `frontend/.env.example`. Docker build також приймає build arg VITE_API_BASE_URL; Compose читає його з .env. Значення VITE_* публічні — секретів там бути не повинно. Dev proxy `/api/*` видаляє `/api` та передає запит на localhost:5080; Caddy робить те саме для production. Canonical backend routes залишилися `/auth/*`, `/products`, тощо. Legacy Caddy API aliases доступні для JSON/curl; browser navigation з Accept:text/html отримує SPA. Це усуває конфлікт `/products` сторінки з `/products` API.

Усі запити йдуть через `src/api/client.ts` із credentials:include. Antiforgery request token з `/auth/csrf` живе тільки в пам’яті, передається X-CSRF-TOKEN для unsafe methods; спільний in-flight запит не дублюється. Після login токен оновлюється під authenticated identity. Автоматичного replay write-запитів немає. Ні auth cookie/token, ні пароль не зберігаються в localStorage/sessionStorage.

GET `/auth/me` зберігає UserId, Username, FullName, OrganizationId, Roles, Permissions у TanStack Query `['current-user']`; окремого auth store немає. Сесію перевіряємо при відкритті, фокусуванні вікна та раз на хвилину. Login: RHF + Zod -> POST `/auth/login` -> новий CSRF -> GET `/auth/me` -> `/`. Authenticated `/login` переводить на `/`. Login mutation не затримує credentials після unmount.

Logout: POST `/auth/logout`, очищення CSRF і query/mutation caches, `/login`. Мережева помилка logout показує Alert із можливістю повторити дію, сесія не оголошується завершеною до успіху/401.

- 401 захищеного запиту: cancel queries, видалення приватних cached даних, current-user=null, перехід на `/login`. Неправильний пароль залишається помилкою форми.
- 403: сторінка «Недостатньо прав», без очищення активної сесії.
- 409: ApiError з code та повідомленням конфлікту; INSUFFICIENT_STOCK має окремий текст.
- 400/422: збереження Problem Details field errors; форма login показує пов’язані помилки полів.
- Network/server/unexpected response: безпечне повідомлення та retry; некоректну відповідь `/auth/me` не вважаємо сесією.

Current user DTO перевіряється через Zod GUID, який підтримує .NET GUID, зокрема зовнішній demo OrganizationId із нульовими version/variant bits.

## Permissions

Одна конфігурація `src/routes/navigation.ts` керує меню і direct-route guard. Це виключно UI; backend завжди авторизує запит самостійно.

| Розділ | Достатньо будь-якого права |
|---|---|
| Головна | authenticated user |
| Товари | product.view |
| Закупівлі | purchase.view |
| Продажі | sale.view |
| Склад | stock.view, stock.transfer, stock.writeoff |
| Гроші | money.view |
| Витрати | expense.view |
| Взаєморозрахунки | settlement.view |
| Контрагенти | settlement.view, sale.view, purchase.view (відповідає backend GET) |
| Звіти | reports.view, profit.view |
| Довідники | settings.manage або доступ до відповідного довідника: product.view, sale.view, purchase.view, money.view, expense.view, stock.writeoff |
| Адміністрування | users.manage, roles.manage, settings.manage, audit.view |

Без права картка dashboard лишається порожньою з «Доступ обмежено». Ніяких запитів до бізнес-звітів або розрахунку фінансових значень на frontend немає.

## HTTPS reverse proxy

Secure antiforgery cookie потребує правильного original scheme. API обробляє тільки X-Forwarded-Proto від стандартного loopback або явно довірених `ReverseProxy:KnownNetworks`; не довіряє всім адресам. Compose передає приватний Docker CIDR 172.16.0.0/12, який можна перевизначити `REVERSE_PROXY_KNOWN_NETWORK` для власної мережі. API port не публікується. Caddy сам визначає X-Forwarded-Proto із TLS-з’єднання. Бізнес-логіки ця конфігурація не стосується.

## Запуск та перевірки

Для входу потрібен backend admin, створений Development seed із паролем через `DevelopmentSeed__AdminPassword` (див. [authentication.md](authentication.md)). Production seed вимкнений. Frontend сам користувачів/ролей не створює.

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
npm --prefix frontend run lint
npm --prefix frontend test
npm --prefix frontend run build
dotnet test backend/Trade.slnx --configuration Release
```

Vitest + Testing Library + jsdom тестують повний React application із MemoryRouter, API responses та query cache: redirect/login/current user, permissions, 403, session expiry, logout, mobile drawer, validation та відсутність fake dashboard data. API client tests перевіряють cookies, CSRF renewal, помилки й cancellation-safe session flow. Залежності зафіксовані в package-lock.json.

Реалізовано Product та Counterparty CRUD, вкладку взаєморозрахунків контрагента та Purchase UI з проведенням/розпроведенням; решта бізнес-CRUD, global search, quick-create logic, reports/admin UI та printed forms відкладені. `src/features/` містить відповідні вертикальні UI slices.
