# Контрагенти

Frontend використовує наявні Counterparty, PriceType та settlement API, спільний MUI theme, cookie/CSRF client і TanStack Query. Нових бізнес-сутностей, таблиць, міграцій або permissions немає.

## Routes та права

- `/counterparties` — список; `/counterparties/:id` — деталі. Достатньо будь-якого існуючого права `settlement.view`, `sale.view`, `purchase.view`, як на backend GET.
- `/counterparties/new` — створення; редагування на detail page та кнопки додавання/редагування потребують `settings.manage`, як backend POST/PUT.
- Вкладка взаєморозрахунків потребує `settlement.view`; без нього вона вимкнена і запит не виконується.
- Довідник типів цін читається лише за наявності `product.view`, `sale.view` або `purchase.view`. Без цих прав поле вимкнене, наявний DefaultPriceTypeId зберігається при PUT; назва позначається недоступною. Права backend не розширюються.

Користувач лише з `settings.manage` може створити контрагента за прямим URL. Після успіху форма очищається без переходу на недоступний detail GET. Усі UI guards доповнюють серверну авторизацію.

## Список та форма

Серверний пошук із debounce 300 ms: Name, Phone, Email, TaxId, RegistrationCode. GET `/counterparties` підтримує `search`, `isCustomer`, `isSupplier`, `isEmployee`, `isSystem`, `active`; фільтри комбінуються. Пошук case-insensitive, `%`, `_`, `\` трактуються буквально. Запити параметризовані та обмежені Organization. Порядок — Name, Id. Сторінки 10/25/50 локально ділять отриманий відфільтрований список.

Початковий статус — «Активні». Desktop показує таблицю, нижче 900 px — картки з назвою, ролями, телефоном, статусом і діями. Internal IDs не відображаються. Є skeleton, порожній список, порожній результат пошуку, API error та retry.

Спільна RHF + Zod форма містить назву, nullable LegalType, ІПН, реєстраційний код, контакти, адресу, необов’язковий тип ціни, ролі, Active та примітку. Назва обов’язкова; email перевіряється лише якщо заповнений. Ролі Покупець/Постачальник/Працівник незалежні. IsSystem показується badge «Системний» і не редагується; при оновленні зберігається серверне значення. DELETE немає, деактивація — через Active.

Backend validation errors зіставляються з полями, введені значення при помилці зберігаються. Успіх та помилка mutation показуються snackbar; після збереження оновлюється detail cache та invalidated queries контрагентів. 401/403 обробляються спільним API client.

## Деталі та взаєморозрахунки

Header показує назву, LegalType, ролі, системну ознаку та Active. Вкладки горизонтально прокручуються на mobile. «Загальне» містить дані та форму редагування. «Продажі», «Закупівлі», «Витрати», «Гроші» — лише placeholders.

«Взаєморозрахунки» ліниво завантажує GET `/settlements/counterparties/{id}`. Кожна валюта має окремий баланс і статус: позитивний — «Нам винні», негативний — «Ми винні», нуль — «Нульовий баланс». Рухи відображаються в серверному хронологічному порядку: дата, номер документа, тип операції, рух і баланс після операції. На mobile — список рухів замість широкої таблиці.

Balance та BalanceAfterMovement беруться з backend, не обчислюються повторно на frontend. Read-only DTO доповнено DocumentNumber із наявного Document.Number, щоб показувати номер замість GUID. Дані та схема не змінені.

Довідника валют поки немає. Тільки погоджений GUID UAH має підпис «UAH»; інші валюти мають окремі нейтральні підписи «Валюта 1», «Валюта 2» з поясненням, що назви не задані. ISO-коди не вгадуються. Різні валюти не підсумовуються. Відсутність рухів показує empty state без вигаданих балансів.

## Перевірки

29 frontend tests цього slice перевіряють список/loading/empty/retry, debounce і фільтри, validation/backend field errors, create/edit, кілька ролей, системний badge та збереження IsSystem, permissions, PriceType lookup, вкладку взаєморозрахунків, знаки й окремі валюти, нуль/порожні рухи, placeholders та mobile cards. 7 додаткових PostgreSQL integration cases перевіряють усі search fields, literal wildcards, комбіновані фільтри, Organization isolation та номер документа в history без зміни балансів.

Production bundle додатково перевіряється у Chromium через Caddy HTTPS із реальними Identity/PostgreSQL. Тестові дані створюються тільки в окремій тимчасовій БД, без production seed контрагентів або фінансових значень.
