# Прихідна накладна

## Routes та доступ

- `/purchases` — список закупівель, `purchase.view`.
- `/purchases/new` — нова закупівля, `purchase.create`.
- `/purchases/:id` — перегляд, `purchase.view`; DRAFT редагується з `purchase.create`, відповідно до наявного backend PUT.
- Проведення — `purchase.post`, розпроведення — `purchase.unpost`, логічне видалення DRAFT — `purchase.delete`.
- Поточна собівартість — тільки `cost.view`. Без цього права запиту на cost endpoint немає. Інші дані накладної залишаються доступними.

Backend авторизує кожен запит. Product selector використовує існуючий GET `/products` із `product.view`; supplier lookup — існуючий Counterparty GET із `purchase.view`, `sale.view` або `settlement.view`. Якщо відповідних прав немає, форма пояснює недоступність lookup замість неавторизованих запитів. Нова permission model не вводиться. Користувач із `purchase.post`, але без `purchase.create`, може провести збережену чернетку без PUT.

## Список

GET `/purchases` повертає лише невилучені PURCHASE_INVOICE поточної Organization; OPENING_STOCK не входить до основного flow. Є серверні фільтри `search`, `from`, `to`, `counterpartyId`, `storageLocationId`, `currencyId`, `status`. Пошук за номером, зовнішнім номером або назвою постачальника, debounce 300 ms; `%`, `_`, `\` трактуються буквально. Період включає обидві межі. Порядок — дата/створення від нових до старих. Сума обчислюється query з активних рядків після header discount, з округленням кінцевої суми до 4 знаків; у БД total не додається.

Desktop — компактна таблиця з номером, датою, постачальником, складом, валютою, сумою, статусом, зовнішнім номером та діями. Нижче 900 px — картки. Є skeleton, empty/error/retry та локальні сторінки 10/25/50 для відфільтрованого масиву.

## DRAFT

Спільна RHF + Zod форма: дата, постачальник, склад, валюта, курс, header discount, зовнішні реквізити, коментар і внутрішня примітка. Вибір нового постачальника обмежено Active + IsSupplier; наявне значення не втрачається, якщо контрагент згодом став неактивним.

Рядки містять лише ProductId, Quantity, UnitPriceCurrency та ідентифікатор наявного рядка. Пошук товару через наявний API за Name/Code/Barcode, лише Active GOODS. SERVICE та неактивні товари не пропонуються. За `product.create` порожній lookup має посилання `/products/new` в окремій вкладці, щоб не втрачати чернетку. Quantity > 0, UnitPriceCurrency >= 0, до 4 десяткових знаків; курс > 0 із точністю наявного backend. Для UAH курс встановлюється в 1 та вимикається редагування.

Підсумки — лише UI preview: сума товарів, відсоток/сума знижки, разом у валюті, курс, еквівалент у грн. Цілочисельне масштабування у preview не округлює проміжні рядки до header discount. Суми, рухи та собівартість, які мають бізнес-наслідки, завжди визначає backend. Line/document totals у payload не надсилаються.

Save створює/оновлює DRAFT; можна зберегти чернетку без рядків, як дозволяє backend. Для POST потрібен хоча б один рядок. Редагування передає поточну Version та зберігає Id рядків. Партії/собівартість не показуються як результат DRAFT. Якщо Batch уже існує після UNPOST, product select цього рядка вимкнений із поясненням «Товар не можна змінити після створення партії.»; quantity/price доступні відповідно до backend rules. Невдала загрузка batches блокує форму, щоб не дозволити зміну Product до перевірки.

## POST / UNPOST / DELETE

POST потребує confirmation «Після проведення будуть створені партії та складські залишки.» Після підтвердження незбережені зміни спершу зберігаються; якщо save неуспішний, POST не виконується. Під час запиту кнопки й форма заблоковані. Після успіху — read-only накладна, номер, статус, PostedAt і доступне ім’я автора.

Вкладки POSTED: «Накладна», «Партії», «Закупівельні витрати», «Ціни». Накладна повна, партії показують справжній BatchNumber, товар/код, дату та поточний UnitCostUah з `v_batch_cost`, якщо дозволено. «Ціни» показує збережені BatchPrice. «Закупівельні витрати» — placeholder, без створення Expense.

UNPOST також потребує confirmation. Frontend тільки викликає endpoint та оновлює queries; збереження Batch/BatchPrice та видалення StockMovement виконує існуючий backend. DOCUMENT_HAS_DEPENDENCIES пояснюється українською. Мінусовий склад, залежності та інші правила проведення не змінюються.

DELETE доступний тільки для DRAFT після confirmation. Наявний Document.Delete виконується транзакційно під блокуванням документа, заповнює DeletedAt/DeletedBy; рядки, Batch та ціни фізично не видаляються. Видалений документ не повертається у GET/list. POSTED напряму видаляти не можна.

Мутації invalidated purchase/lookups, stock, batches/pricing та settlements/counterparty queries. Snackbar розміщений під верхньою панеллю, щоб не перекривати mobile Save/Post/Delete. VERSION_CONFLICT та наявний backend 409 із stale Version пояснюються як «Документ був змінений іншим користувачем. Оновіть дані.» Введені значення зберігаються до явного оновлення; write-запити автоматично не повторюються. Обробляються field validation, DOCUMENT_NOT_DRAFT, INVALID_SOURCE_DOCUMENT та спільні 401/403.

## Мінімальний довідник складів

Погоджена міграція `20261026150710_PurchaseStorageLocations` додає лише `storage_locations`: Id, OrganizationId, Name. Непорожня назва, UNIQUE(OrganizationId, Name); без Active, адрес, seed та окремого directory UI. POST `/storage-locations` дозволений `purchase.create` або `settings.manage`. Inline dialog додає склад і вибирає його у формі.

GET `/purchases/lookups` повертає іменовані склади організації та раніше використані в purchase/sale зовнішні GUID. Старі дані не мігруються у вигадані назви й залишаються сумісними. Склад із довідника іншої Organization заборонений на Purchase create/update. Валюти читаються з уже використаних даних організації, плюс погоджений UAH GUID; нової таблиці валют і currency logic немає. Невідомі назви мають окремі нейтральні підписи «Валюта 1», «Валюта 2», без вгадування ISO-кодів або показу GUID.

Перед запуском нової версії застосуйте міграцію звичайною командою репозиторію:

```sh
dotnet run --project backend/Trade.Api --no-launch-profile -- --migrate
```

## Read API та перевірки

Додано GET `/purchases`, `/purchases/lookups`, `/purchases/{id}/batches`, `/purchases/{id}/batch-costs`, `/purchases/{id}/batch-prices`. Detail DTO має read-only Product names/codes та PostedByName; вони не зберігаються в Purchase. Собівартість не дублюється у звичайній відповіді batches/detail.

Frontend tests покривають список/filters/search/loading/empty, форму, постачальників, GOODS lookup/loading/barcode, validation, рядки/discount/rate totals, UAH, save/version/line identity, confirmation, POST/read-only, Batch/cost/prices, UNPOST/dependencies/immutable Product, logical delete, permissions, mobile та inline склад. PostgreSQL integration tests перевіряють query filters/derived totals/isolation, retained Batch/current cost/unchanged prices, logical delete, склад/схему та permissions. Chromium smoke використовує production bundle через Caddy HTTPS з реальними Identity/PostgreSQL і окремою тимчасовою БД, без demo seed у робочій базі.

SupplierReturn, Expense creation, OpeningStock, Sale UI, printed forms, import і scanner не входять до slice.
