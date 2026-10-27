# Identity, користувачі, ролі та права

ASP.NET Core Identity виконує hashing/validation паролів, sign-in і lockout. Сесія — `trade.auth` HttpOnly cookie, SameSite=Lax; Secure у Production/Staging. Токенів у localStorage немає. Користувач належить рівно одній незмінній Organization. Довідника Organization ще немає: використовується GUID-контекст, як у попередніх slices.

## Cookie та CSRF flow

1. `GET /auth/csrf` без authentication: відповідь `{ "requestToken": "...", "headerName": "X-CSRF-TOKEN" }` та HttpOnly `trade.csrf` cookie. Зберігайте cookie у cookie jar/browser; token — у пам’яті клієнта.
2. `POST /auth/login` із cookie та `X-CSRF-TOKEN`, JSON `{ "username": "admin", "password": "...", "rememberMe": false }`. Успіх — 204 + session cookie. Невідомий login, неправильний пароль, inactive/locked user — 401. П’ять невдалих спроб блокують на 15 хвилин (Identity).
3. Після login знову `GET /auth/csrf`: новий request token прив’язаний до authenticated identity. Усі наступні POST/PUT/DELETE, також logout, передають його та cookie. Missing/invalid token — 400. Anonymous protected endpoint — 401, authenticated без permission — 403.
4. `GET /auth/me`: UserId, Username, FullName, OrganizationId, активні Roles та обчислені Permissions. Відповідь не кешується.
5. `POST /auth/logout`: 204, видалення cookie та оновлення SecurityStamp. Це відкликає **всі** сесії користувача, включно з копіями старої cookie. Після logout отримуйте новий anonymous CSRF token перед наступним login.

Кожний cookie request перевіряє Active, SecurityStamp та OrganizationId у БД. Deactivate та зміна credentials відкликають сесії відразу. Permissions читаються з БД на кожний запит: зміни ролей/прав діють без login. `X-Organization-Id`, `X-Actor-Id` та вигадані permission headers/JSON не є джерелом контексту чи доступу в жодному середовищі. CreatedBy/PostedBy/DeletedBy отримують authenticated user id.

## Схема

Міграція `AddIdentityAndPermissions` додає рівно п’ять таблиць:

| Таблиця | Дані |
|---|---|
| users | Identity Id/UserName/NormalizedUserName/PasswordHash/SecurityStamp/ConcurrencyStamp/LockoutEnd/LockoutEnabled/AccessFailedCount; OrganizationId, FullName, CounterpartyId?, Active, LastLoginAt? |
| roles | Identity Id/Name/NormalizedName/ConcurrencyStamp; OrganizationId, Active |
| user_roles | UserId, RoleId |
| permissions | Id, Code, Description; глобальні 34 codes |
| role_permissions | RoleId, PermissionId |

Username глобально унікальний без урахування регістру; login не потребує довільного OrganizationId. Role name унікальний у межах Organization, також без урахування регістру. Мінімальний EF Identity user store підтримує лише функції V1; зайвих таблиць claims/external logins/tokens/MFA немає. Effective permissions не зберігаються в User чи cookie. Фізичний DELETE user заборонено store, DbContext і PostgreSQL trigger. User/Role OrganizationId незмінний; Counterparty користувача та UserRole перевіряються на одну Organization також у БД.

## Керування

Усі списки/читання/зміни обмежені Organization поточного користувача; foreign id повертає 404, foreign CounterpartyId/RoleId у body — 400.

`users.manage`:

- `GET /admin/users`, `GET /admin/users/{id}`.
- `POST /admin/users`: Username, Password, FullName (default ""), CounterpartyId?, Active (default true), RoleIds (default []). Пароль — Identity policy: мінімум 12 символів, upper/lower/digit/nonalphanumeric.
- `PUT /admin/users/{id}`: Username, FullName, CounterpartyId?, Active, RoleIds, Password? (omitted/null — не змінювати). PUT замінює RoleIds повністю; OrganizationId не приймається. Password/Username/activation changes відкликають старі сесії; PUT консервативно відкликає їх при будь-якій зміні User.
- `POST /admin/users/{id}/activate`, `/deactivate`.

`roles.manage`:

- `GET /admin/roles`, `GET /admin/roles/{id}`.
- `POST /admin/roles`, `PUT /admin/roles/{id}`: Name, Active (default true).
- `PUT /admin/roles/{id}/permissions`: `{ "permissionCodes": ["product.view", "sale.create"] }`, повна заміна. Невідомий code — 400 без часткових змін. Повний каталог — `PermissionCatalog` у Application.

User/Role responses ніколи не містять password hash/security stamp. User може мати кілька ролей; permission достатньо в одній активній ролі. Назви OWNER/ADMIN не обходять перевірку: вони отримують права через RolePermission.

## Матриця доступу

| API / дія | Permission |
|---|---|
| Units GET / POST | product.view/create/edit або settings.manage / product.create/edit або settings.manage |
| Products GET / POST / PUT | product.view / product.create / product.edit |
| Purchases та SupplierReturn GET / create+PUT / post / unpost / delete | purchase.view / purchase.create / purchase.post / purchase.unpost / purchase.delete |
| Sales та CustomerReturn GET / create+PUT / post / unpost / delete | sale.view / sale.create / sale.post / sale.unpost / sale.delete |
| Sale UnitPrice < RecommendedUnitPrice, також при POST | додатково sale.change_price |
| Stock balances та available-batches | stock.view |
| Усі StockTransfer endpoints | stock.transfer |
| WriteOff та WriteOffReason endpoints | stock.writeoff |
| GET /write-offs/costs (POSTED своєї Organization, поточний VIEW) | cost.view |
| Money GET | money.view |
| MoneyReceipt / MoneyPayment / MoneyTransfer зміни, post/unpost | money.receipt / money.payment / money.transfer |
| MoneyAccount зміни | settings.manage |
| Expenses GET / create+PUT / post / unpost / delete | expense.view / expense.create / expense.post / expense.unpost / expense.delete |
| ExpenseCategory GET / зміни | expense.view / expense.create |
| Settlements GET | settlement.view |
| Counterparties GET / зміни | будь-який settlement.view, sale.view, purchase.view / settings.manage |
| PriceType та BatchPrice/ProductPrice GET / зміни | будь-який product.view, sale.view, purchase.view / settings.manage |
| Users / Roles | users.manage / roles.manage |

`reports.view`, `profit.view`, `audit.view` зареєстровані для наступних відповідних API; нових reports/profit/audit модулів не додано. Cost endpoint має окрему перевірку незалежно від stock.writeoff/reports.view. Майбутній profit API має вимагати profit.view. Публічні лише `/health`, login/CSRF (login має CSRF validation), Development OpenAPI/Swagger. Нова business route без mapping завершує startup помилкою.

## Development seed

Застосуйте міграції; передайте `DevelopmentSeed__AdminPassword` через середовище і запускайте з Development (`--launch-profile http`). Seed може виконуватись також у Development `--migrate`. Без пароля він пропускається. Production/Staging не виконує seed навіть за наявності пароля. В коді/config немає built-in admin password.

Demo OrganizationId — `00000000-0000-0000-0000-000000000001`, опціональний override `DevelopmentSeed__OrganizationId`. Створюються сім ролей OWNER, SALES_MANAGER, PURCHASE_MANAGER, WAREHOUSE, FINANCE, ADMIN, CASHIER; OWNER/ADMIN отримують усі права, інші порожні до налаштування. `admin` отримує OWNER. Повторний seed не змінює існуючий пароль, не реактивує користувача та не створює дублікати. Не запускайте dev seed у публічному production; production bootstrap не реалізовано в цьому slice.

Integration tests використовують реальні Identity cookies, native hashing та antiforgery із PostgreSQL Testcontainers. Старі business tests явно реєструють test authentication scheme і CSRF test double тільки в test assembly, без runtime Development bypass. Login UI та application shell реалізовані наступним frontend slice: див. [frontend.md](frontend.md). MFA/SSO/LDAP, verification/password-reset email, warehouse permissions та approval workflows не входять до slice.
