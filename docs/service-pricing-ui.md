# Ціни послуг

Product detail для SERVICE містить секцію «Ціни»: всі активні PriceType, поточна ціна UAH або «Не задано», редагування через існуючий ProductPrice upsert. Нульова ціна відрізняється від незаданої. Порожніх/вигаданих цін і автоматичного markup для SERVICE немає.

GOODS показує пояснення «Ціни товару встановлюються окремо для кожної партії.»; ProductPrice editor і запити цін послуги для GOODS не виконуються. BatchPrice та модель ціноутворення не змінені, нових таблиць/міграцій немає.

На створенні SERVICE користувач із settings.manage може необов’язково заповнити початкові ціни. Спочатку створюється Product, потім ціни записуються окремими існуючими PUT. Якщо частина PUT не вдалася, форма показує, що Product уже створено, фіксує його Id, блокує повторне створення та повторює лише невдалі/змінені ціни. Можна явно завершити без решти цін. Ці операції не оголошуються атомарними.

## API та права

- Наявний `GET /price-types` — назви, порядок та Active.
- **Доданий read endpoint** `GET /products/{productId}/prices` — наявні ProductPrice одного SERVICE, без N запитів/404 для незаданих цін. Поточна org та SERVICE перевіряються backend; чужий Product → 404, GOODS → 400. Повертає ProductId, PriceTypeId, UnitPrice; відсутній запис не перетворюється на 0.
- Наявні `GET/PUT /products/{productId}/prices/{priceTypeId}`; PUT payload `{ "unitPrice": 300 }` створює або змінює один composite key Product+PriceType.
- Читання — наявні pricing read permissions, зокрема product.view; запис — **settings.manage**. product.edit сам по собі не дає права міняти ціну. Нових permissions немає.

RHF/Zod перевіряє ціну >= 0 та підтримувану backend точність numeric(20,4). Збереження використовує cookie/CSRF через централізований client; помилки лишають введені дані. TanStack Query інвалідує ціни послуги, нові sale price options і аудит. На mobile — компактні price rows і вузький dialog без горизонтального scroll.

## Продаж та snapshot

З вибраним PriceType backend бере SERVICE RecommendedUnitPrice з ProductPrice. За відсутності ціни UI показує: «Для послуги не задано ціну для вибраного типу цін.» Збереження не підставляє 0. Поточний backend вимагає ProductPrice, якщо PriceType вибрано, навіть за ручного UnitPrice; це правило збережене. Для чинного ручного режиму можна явно вибрати «Не вибрано» у полі типу ціни й ввести фактичну ціну. sale.change_price залишається перевіркою backend для зниження відносно рекомендації.

Нова ціна застосовується до нових рядків. Існуючий snapshot SaleInvoiceLine автоматично не переоцінюється. Новий PriceType у картці SERVICE відображається як «Не задано», доки ціну явно не збережено.

## Аудит і перевірки

Наявний аудит ProductPrice уже записує actor/time, composite EntityId з ProductId та PriceTypeId, old/new UnitPrice. Перевірено інтеграційним тестом і Chromium: 300 → 350. Нової таблиці історії цін немає.

Додано 11 frontend pricing tests і перевірку відсутньої ціни в Sale; окремий regression test виправляє знайдену під час V1 audit заборону Quantity=0 для фінансової компенсації SERVICE. Backend `ServicePriceUiTests` перевіряє bulk read, upsert/concurrency/unique key, zero/negative, GOODS rejection, permissions, org isolation й audit old/new.

Результати: 397/397 frontend tests, 522/522 backend tests, lint і production build успішні; Release backend build — 0 errors/warnings, EF pending model changes відсутні.

Chromium A–D: «Оформлення букета», Роздрібна=300/Гуртова=250, reopen; POST продажу з recommendation300 без StockMovement; зміна до350 й новий продаж350 зі старим snapshot300; Audit300→350. Mobile editor перевірений. Скрипт відтворення — `scripts/service-pricing-smoke.mjs`, параметри та ізольований запуск див. `docs/v1-audit.md`.
