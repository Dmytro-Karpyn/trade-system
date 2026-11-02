# Довідники та адміністрування

Довідники відкриваються з `/directories`: категорії товарів, одиниці, типи цін, місця зберігання, причини списання, валюти та посилання на наявне дерево `/expense-categories`.

`/directories/categories` підтримує дерево довільної глибини, ParentId, Description, SortOrder та Active. Backend серіалізує зміни дерева в межах організації й відхиляє цикли, чужого батька та однакові назви на одному рівні. Product вибирає категорію; Stock і звіти показують повний шлях. Старі зовнішні CategoryId збережені для сумісності; відомий чужий CategoryId заборонено.

Unit і StorageLocation отримали Active. Нових Symbol, адрес чи різновидів складів немає. Деактивація зберігає посилання та історичні документи; фізичного видалення немає. Чинні правила допустимості складів у складських документах залишаються.

Типи цін редагуються на `/directories/price-types`. Кнопка «Додати базові типи» ідемпотентно створює відсутні «Роздрібна» та «Гуртова» з нульовою початковою націнкою. Це явна дія користувача з settings.manage, без автоматичного seed/backfill. Наявні типи, їхня активність і BatchPrice зберігаються. Зміна націнки впливає тільки на початкову ціну нових партій.

Причини списання використовують існуючі ExpenseCategoryId; дерево витрат не дублюється. `/directories/currencies` доступний тільки для читання: UAH — базова (`00000000-0000-0000-0000-000000000980`), решта — відомі GUID із документів своєї організації. Для невідомих валют не вигадуються ISO-коди, назви чи Active. Нової таблиці валют і логіки курсів немає.

## Користувачі та ролі

`/admin/users`, `/admin/users/new`, `/admin/users/:id` використовують чинний ASP.NET Core Identity. Список показує імена ролей, пов’язану Counterparty, активність і останній вхід. Працівники першими у selector; окремої Employee table немає. Новий пароль перевіряється та хешується Identity; для наявного користувача пароль не показується й не надсилається формою. Організація береться із cookie, у формі доступна лише для читання. Деактивація потребує підтвердження й відкликає сеанси.

`/admin/roles`, `/admin/roles/new`, `/admin/roles/:id` показують кількість користувачів та прав. Редактор групує існуючі Permission.Code за дев’ятьма модулями; на mobile використовує accordions. Права неактивної ролі змінювати заборонено: спочатку активувати та зберегти. Effective permissions залишаються об’єднанням прав активних ролей, без нової таблиці.

Backend не дозволяє прибрати останнього активного адміністратора організації — активного користувача з users.manage **або** roles.manage через активну роль. Перевіряються деактивація, заміна ролей користувача, деактивація ролі та зняття прав. Зміни серіалізовані transaction advisory lock організації; `409 LAST_ADMIN_REQUIRED` відкочує всю операцію. Інша організація не може виконувати адміністративні дії над чужими даними. Фізичного DELETE users/roles немає.

## Аудит

`/admin/audit` потребує audit.view. Фільтри: дати, користувач, подія, тип сутності, документ, пошук у summary/entity id; серверна пагінація. Read-only drawer показує структуровані old/new values; доступні посилання на реалізовані документи, Product, User та Role за правами поточного користувача.

Нова `audit_logs` записує whitelist змін бізнес-сутностей і адміністративних дій у тій самій транзакції. Технічні журнали рухів, обчислювані залишки/собівартість, password/hash/security stamp не копіюються в аудит. Seed та фонові дії без authenticated actor не створюють вигаданого автора. Для UPDATE зберігаються змінені поля, для CREATE/DELETE — scalar values. Аудит існуючих операцій до цієї міграції не відновлюється. PostgreSQL trigger забороняє UPDATE/DELETE audit_logs; write API відсутній.

## Backend доповнення

- `GET/POST /categories`, `PUT /categories/{id}`.
- `PUT /units/{id}`, `GET /storage-locations`, `PUT /storage-locations/{id}`; наявні POST збережені.
- `GET /currencies`, `POST /price-types/defaults`.
- `GET /admin/users/lookups`, `GET /admin/permissions`.
- `GET /admin/audit`, `GET /admin/audit/lookups`, `GET /admin/audit/{id}`.

Нові read/manage endpoints використовують наявні permissions: довідники — settings.manage або відповідні read/operation permissions; users — users.manage; roles/permission definitions — roles.manage; аудит — audit.view. Frontend лише управляє видимістю, сервер остаточно авторизує запити.

Міграція `20261029210258_DirectoriesAndAudit` створює тільки `product_categories` і `audit_logs`, додає Active до units/storage_locations, індекси та immutable trigger. Застосувати звичайною командою міграцій із README до запуску нової версії.

## Перевірки

`DirectoryManagementTests` перевіряє дерева, конкурентні цикли, глибокі шляхи в Stock, org isolation, сумісність зовнішніх GUID, Active, price defaults та незмінність BatchPrice, read-only currencies. `AdministrationAuditTests` — metadata, multiple roles, active/inactive, захист останнього адміністратора включно з concurrency/rollback, old/new, actor/document links, відсутність секретів, immutable trigger та права аудиту. Наявні тести pricing/auth/isolation залишаються регресійною перевіркою.

Frontend `management.test.tsx` перевіряє каталог, дерево, create/edit, self/descendant parent exclusion, довідники, PriceType, currencies, Product categories, users/roles/permissions, activation, audit filters/details та mobile layouts.

Перевірено 29.10.2026:

- frontend lint і production build — успішно;
- усі frontend tests — 384/384, включно з 33 новими management tests;
- усі PostgreSQL integration tests — 519/519, включно з 13 новими directory/admin/audit tests; пропущених немає;
- backend Release build — 0 errors, 0 warnings;
- EF `has-pending-model-changes` — розбіжностей із міграцією немає;
- Chromium із production bundle, Caddy HTTPS, реальними Identity cookie/CSRF і окремою PostgreSQL 18 — сценарії A–D успішні: Посуд/Чашки → Product/Stock; markup 25 → 55 при незмінному BatchPrice 125; limited user/role, приховані menu items і API 403; бізнес-зміна з old/new в аудиті;
- додатково Chromium перевірив LAST_ADMIN_REQUIRED, mobile category/user/role/audit cards, permission accordions і audit drawer; browser errors/warnings — 0. Тимчасові smoke API/контейнери зупинено, робоча БД не змінювалася.
