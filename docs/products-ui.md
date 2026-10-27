# Товари / Номенклатура

Перший робочий frontend CRUD використовує наявний Product та спільні cookie/CSRF, API client, TanStack Query і MUI theme.

## Routes та доступ

- `/products`: список, product.view.
- `/products/new`: спільна форма створення, product.create.
- `/products/:id`: перегляд, product.view; редагування на цій же сторінці лише з product.edit.

Меню, кнопки та direct URL мають UI guards. Backend перевіряє права незалежно. Користувач лише з product.create може створювати за прямим URL: після успіху форма очищається, без спроби GET detail, на який у нього немає права. DELETE відсутній; деактивація — Active=false через PUT.

## Список

GET `/products?search=...&itemType=GOODS|SERVICE&active=true|false` підтримує серверні фільтри. Без active параметра повертаються обидва стани, як до цього slice. Пошук case-insensitive за Name/Code/Barcode, debounce 300 ms; `%`, `_` та `\` трактуються буквально. Запити параметризовані й обмежені поточною Organization. Порядок — Code, Id. Frontend має локальні сторінки 10/25/50 для отриманого відфільтрованого масиву, без нового backend pagination contract.

Початковий статус у UI — «Активні». «Усі» включає неактивні з відповідним badge. Desktop: компактна таблиця Код/Назва/Тип/Категорія/Одиниця/Штрихкод/Активний/Дії. Нижче 900 px — рядки-картки замість таблиці. Внутрішні Id, OrganizationId та raw Attributes не відображаються.

## Форма

React Hook Form + Zod; обов’язкові тип, код, назва та одиниця. Тип за замовчуванням GOODS; SERVICE — той самий Product, без окремого довідника. Helper text пояснює складський облік для GOODS і відсутність залишків/партій для SERVICE.

Є штрихкод, опис, Active. Категорія вимкнена, оскільки API цього довідника немає. Наявний CategoryId та Attributes зберігаються незмінними в PUT; відсутність редактора не очищає їх. Одиниці без відомої назви зі старих даних показуються як «Назву не вказано»; GUID не показується і не підмінюється вигаданою назвою.

Backend field errors зіставляються з полями форми; наявні обмеження зміни ItemType після використання в документах/цінах пояснюються біля типу. Помилки не очищають введені дані. 409 має загальне повідомлення конфлікту, 401/403 обробляються спільним client. Після збереження detail cache оновлюється, списки/detail invalidated, показується snackbar; успіх створення відкриває detail, редагування повертає до перегляду.

## Мінімальний довідник одиниць

Погоджений користувачем довідник потрібен, бо UnitId обов’язковий, а до цього всі UnitId були зовнішніми GUID. Міграція `ProductUnits` додає лише `units`:

- Id;
- OrganizationId;
- Name.

Є перевірка непорожньої назви та UNIQUE(OrganizationId, Name). Без Active/Code/Symbol/коефіцієнтів/seed values. GET `/units` — вибір у формі; POST `/units` з `{ "name": "..." }` — простий inline dialog. Organization визначається сервером. GET дозволений product.view/create/edit або settings.manage; POST — product.create/edit або settings.manage. Повноцінного unit directory UI, редагування/видалення одиниць і Category CRUD немає.

Product domain model, його колонки та старі зовнішні UnitId не змінено. Невідомі GUID лишаються сумісними; Unit, який уже існує в довіднику іншої організації, заборонений на Product create/update. FK на всі старі UnitId не додається, бо це порушило б існуючі дані. Організації не бачать одиниці одна одної.

## Перевірки

Frontend tests покривають завантаження/порожній стан/помилку/retry, пошук, type/status filters, create/edit permissions та direct URL, RHF validation, успішні create/edit/deactivate, збереження прихованих полів, SERVICE helper, inline Unit та mobile cards. Backend integration tests із PostgreSQL Testcontainers перевіряють пошук/буквальні wildcard символи/кирилицю, комбіновані фільтри, organization isolation, Unit permissions/схему та заборону foreign Unit.

Додатково перевірено production bundle в Chromium через Caddy HTTPS із реальними Identity/PostgreSQL: inline Unit, GOODS/SERVICE create, edit/deactivate, search/filter, reload detail та mobile cards. Дані браузерної перевірки створюються в окремій тимчасовій БД; робоча база не отримує demo Product/Unit seed.

Не реалізуються stock balances, batches, prices, Attributes editor, import/scanner, category tree, unit directory UI, purchase/sale quick create.
