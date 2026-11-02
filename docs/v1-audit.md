# V1 Audit

## Executive summary

Аудит розпочато 29.10.2026, завершальні перевірки — 30.10.2026 (Europe/Kyiv). Перевіряється поточний робочий стан, а не лише опублікований commit `b4039ca`. Попередні незакомічені зміни збережені; commit/push цим завданням не виконувалися.

Підсумок: **READY WITH MINOR ISSUES** для контрольованого реального тестового використання однією організацією з налаштованим адміністратором, HTTPS та резервним копіюванням. Це висновок для pilot, не підтвердження публічного deployment чи навантажувальної готовності.

Відкриті: Critical **0**, High **0**, Medium **3**, Low **1**. Одна локальна High-помилка виправлена з regression test. Знайдений gap ProductPrice UI закритий наступною явною вказівкою користувача, без зміни pricing model, BatchPrice або схеми.

## Critical issues

Відтворених блокерів цілісності stock, money, settlements, document lifecycle або organization isolation не знайдено. Нових таблиць, індексів чи міграцій аудит не додає.

## High priority

Відкритих High немає.

**H1 — виправлено:** CustomerReturn frontend забороняв Quantity=0 для SERVICE, хоча domain дозволяє фінансову компенсацію без повернення кількості. Фактичний mixed return зі SERVICE Quantity=0/RefundAmount=50 зупинявся у формі. Виправлено лише Zod та min атрибут SERVICE quantity; GOODS усе ще >0, refund >0, precision checks збережено. Regression test: `saves and posts a SERVICE financial compensation with zero quantity`; реальний browser flow повторений.

## Medium priority

1. **M1 — Barcode не унікальний.** Унікальний Product Code у межах Organization; Barcode не має unique constraint/index. Два різні Code можуть зберегти однаковий Barcode. Пошук повертає кілька варіантів, автоматичного scanner/POS немає. Не додано unique index: попередня модель не фіксувала такої вимоги, а чинні дублікати потребують окремого узгодженого очищення. До pilot використовувати однозначні коди та контролювати barcode даними.
2. **M2 — новий склад не одразу доступний для outgoing operations.** IsKnownStorageLocation та operation lookups вимагають попереднього використання складу в Purchase/Sale; сам запис StorageLocation не достатній. Це чинне погоджене legacy правило. У smoke destination зареєстровано тестовою закупівлею (одночасно перевірено zero-value allocation); у реальній роботі слід заздалегідь пройти погоджений purchase/opening workflow. Зміна правила під час аудиту не виконувалася.
3. **M3 — можливий пізній запит own-reserve після POST.** Під час первинного browser прогону відтворено GET available-batches з excludeSaleId уже POSTED документа → 400. Backend коректно відхиляє DRAFT-only exclusion, документ проводиться правильно. У завершальному E2E прогоні було 2 такі console HTTP400 для stock/operation-batches після transition Transfer/WriteOff; у первинному також відтворено available-batches Sale. Це frontend transition/query race, а не overselling чи неправильний reserve. Рекомендовано окремо зупиняти/скасовувати такі query до transition. Medium автоматично не виправлявся; діагностика відділена від runtime errors у smoke output.

## Low priority / polish

**L1 — PriceType Name не має унікальності.** Записи і ціни однозначно ідентифікуються Id/composite key, але однакові активні назви роблять selector неоднозначним для оператора. Зараз дотримуватися naming convention. Не нав’язувати новий unique index без окремого бізнес-рішення та перевірки існуючих даних.

## Confirmed design rules

- Physical stock — SUM StockMovement; reserve — активні рядки невилучених DRAFT Sale GOODS/SupplierReturn/StockTransfer/WriteOff; available=physical−reserved. Є один v_stock_balance, немає StockReservation/cached stock.
- Batch — один на purchase line, створюється тільки першим POST, зберігається після UNPOST/repost; ProductId після Batch незмінний. Batch/StockMovement не дублюють ProductId/OrganizationId/MovementDate.
- GOODS використовує BatchPrice, SERVICE — ProductPrice; SERVICE заборонений у purchase, не має Batch/stock movements, підтримує часткове повернення компенсації.
- v_batch_cost — purchase net price × rate + POSTED direct expenses на одиницю. Поточна собівартість, COGS/profit/writeoff перераховуються; BatchPrice та збережена RecommendedUnitPrice не переписуються.
- Settlement signs: Sale+, CustomerReturn−, Purchase−, SupplierReturn+, Expense−, customer Receipt−, supplier/expense Payment+. Валюти не змішуються. Немає paid%, payment allocations, invoice status/balance fields.
- Money balance — SUM MoneyMovement, від’ємний дозволений. Transfers — дві сторони; cross-currency amounts можуть відрізнятися, effective rate не зберігається. OWNER/OPENING не є фінансовим доходом/витратою.
- Number призначається першим POST, зберігається після UNPOST та не перевикористовується; POST і повторний UNPOST ідемпотентні. PUT використовує Version; stale DRAFT update →409. DELETE лише logical DRAFT.
- Direct Expense та SupplierReturn, включно з DRAFT, блокують source Purchase UNPOST; CustomerReturn блокує source Sale UNPOST. Safe removal враховує поточний physical/reserved у destination/поверненому stock. Історична backdated validation не додається.
- Backend stock writers мають впорядковані Batch locks; writers одного документа — Document FOR UPDATE; return writers — source locks; category/admin changes — org advisory locks. Паралельні DRAFT save/POST не перепродають stock.
- Перевірені п’ять звичайних views: v_stock_balance, v_batch_cost, v_counterparty_balance, v_money_account_balance, v_write_off_cost. Заборонених derived business columns не знайдено; read projections, рекомендована ціна snapshot і audit old/new не є кешами поточного стану.

## Test results

| Перевірка | Результат |
|---|---|
| Усі backend tests, PostgreSQL Testcontainers | 522/522, failed0, skipped0 |
| Release backend build | 0 errors, 0 warnings |
| Frontend lint | PASS |
| Усі frontend tests | 397/397 |
| Frontend production build | PASS |
| EF model vs migrations | no pending model changes |
| SERVICE pricing Chromium A–D/mobile | PASS |

Команди: `dotnet test backend/Trade.slnx -c Release`, `dotnet build backend/Trade.slnx -c Release`, `npm --prefix frontend run lint`, `npm --prefix frontend run test`, `npm --prefix frontend run build`. Testcontainers потребує Docker.

## Fresh-install result

Контрольна БД PostgreSQL18 створена окремо від робочої. Застосовано migrations, виконано наявний Development `--migrate` seed з паролем лише через env; процес завершився без відкриття HTTP port. Після цього API запущено **Production**, frontend — production bundle, Caddy — HTTPS.

Наскрізний сценарій: каталоги → GOODS/SERVICE → supplier/customer → Purchase DRAFT/POST/UNPOST/repost → direct Expense → mixed Sale → reserves/concurrency → receipt/payment/OWNER/transfer → general Expense → customer/supplier return → stock transfer/writeoff → Dashboard/7 reports/CSV → users/roles/limited login → audit/mobile.

Розмежування evidence: happy-path документи створюються у browser UI; API/SQL використовуються для перевірки журналів, негативних/конкурентних сценаріїв і тестової другої організації. Це не mock backend. Робоча PostgreSQL БД не отримує audit fixtures.

## Browser smoke result

Контрольний dataset UAH:

| Показник | Значення |
|---|---:|
| Sale net after header discount | 720 |
| CustomerReturn GOODS135 + SERVICE50 | 185 |
| Net sales | 535 |
| Current COGS | 300 |
| Gross profit | 235 |
| General expenses | 30 |
| Writeoff current cost | 100 |
| Management result | 105 |
| Money total | 300 |
| Customer / Supplier / Carrier settlement | +35 / −620 / −30 |

Direct Expense UNPOST змінює unit cost100→90, COGS300→270, writeoff100→90, management result105→145; повторний POST повертає значення. Збережена BatchPrice залишається130 після окремої ручної зміни, initial112.5 до неї теж не переписувалася витратами. CSV money відбирає лише обраний account.

SERVICE-specific сценарії A–D: Retail300/Wholesale250 після reopen, POST recommendation300 без StockMovement; price350 для нового Sale, старий snapshot300; Audit300→350 із actor, composite Product/PriceType identity та timestamp. Mobile price editor без page overflow. Неочікуваних browser/runtime errors у цих сценаріях немає. Anonymous `/auth/me`401 — очікувана перевірка сесії, а не виняток застосунку; її мережеву діагностику не маскують під успішний login.

Відтворювані harness: `scripts/v1-audit-smoke.mjs` (clean dataset), `scripts/v1-audit-api.mjs` (додаткові API/SQL перевірки на ньому), `scripts/service-pricing-smoke.mjs` (A–D). Вони призначені **лише для disposable audit середовища**, не для робочої БД. Встановити Playwright поза app dependencies: `npm install --prefix /tmp/trade-v1-browser playwright-core`; передати `PLAYWRIGHT_MODULE_PATH=/tmp/trade-v1-browser/node_modules/playwright-core/index.mjs`, `AUDIT_ADMIN_PASSWORD`, `AUDIT_BASE_URL`, за потреби `CHROMIUM_PATH` і `AUDIT_DB_CONTAINER/USER/NAME`. Запускати Node після migrations/bootstrap та Caddy HTTPS; harness перевіряє PostgreSQL через Docker exec. Output за замовчуванням `/tmp/trade-v1-audit`, credentials не комітяться.

Повний V1 E2E: functional PASS, runtime errors0, warnings0, failed requests0; console network diagnostics: 1 очікуваний anonymous auth/me401 та 2 відомі HTTP400 (M3). Отже вимога повністю чистої console ще має зауваження M3; вона не оголошується безумовно виконаною. SERVICE A–D окремо не має цих HTTP400.

## Migration result

`dotnet ef database update --project backend/Trade.Infrastructure --startup-project backend/Trade.Api --configuration Release --connection <audit-connection>` успішно створює fresh database. DirectoriesAndAudit є останньою міграцією ланцюга; `has-pending-model-changes` не знаходить відхилень.

Окрему upgrade БД створено до `20261026150710_PurchaseStorageLocations`, заповнено legacy Unit/StorageLocation/Product із зовнішнім CategoryId/PriceType/Counterparty/POSTED Purchase/line/Batch/StockMovement/BatchPrice/SettlementMovement. Після database update до latest збігаються digests старих даних; нові Active у units/storage_locations=true, існуючі Active збережені. Старі GUID, number, audit dates, JSON attributes, ціна112.5, stock5 та settlement−500 не втрачені. Це контрольний upgrade fixture, не копія реальних даних користувача.

Перевірені PK/unique key Batch.PurchaseInvoiceLineId, BatchPrice(BatchId+PriceTypeId), ProductPrice(ProductId+PriceTypeId), Product Organization+Code, Document Organization+type+Number; active line uniqueness (включно з deferred triggers), numeric quantities/prices/rates/header discounts та scope constraints у наявних PostgreSQL tests. Barcode — зауваження M1. Індексів «про всяк випадок» не додано.

Додатково виконано pg_dump custom-format → pg_restore в іншу чисту disposable БД. Кількості рядків і digests усіх public base tables (включно з Identity, аудитом і migration history) збігаються. Це перевірка відновлення PostgreSQL dataset; production Data Protection keys і сертифікати Caddy потрібно резервувати окремо разом із volumes.

## Security result

Cookie HttpOnly/Secure/SameSite=Lax у Production через HTTPS. Mutations із CSRF, anonymous→401, missing permissions→403. Organization/Actor беруться з валідованої Identity; forged X-Organization-Id/X-Actor-Id не змінюють контекст. Перевірені inactive user/session revocation, active-role union/inactive-role exclusion та захист останнього адміністратора.

Дві org — GUID-контексти, без нової Organization table/switching UI. OrgB identity fixtures provisioned тільки в disposable test DB; запити виконуються двома реальними cookie sessions. Чужі direct ids, batches, source returns, categories, locations, units, roles та references відхилені для orgA (404/400); forged headers не змінюють scope. Після операцій orgB фінансові показники orgA не змінилися. Backend/integration coverage також перевіряє cost/profit/money/settlement/report permissions та tenant predicates.

Додатковий API harness успішно перевірив усі 11 типів документів (stale409, повторні POST/UNPOST, logical delete), 2 реальні Identity org, cross-currency transfer100/2, окремі валютні balances, inactive role/user та last-admin.

Audit append-only: actor/entity/document/time, old/new; price й role/permission changes; API write/delete відсутні, PostgreSQL trigger забороняє UPDATE/DELETE. Password/hash/stamps не потрапляють у Audit DTO. Production seed не запускається автоматично. Перед будь-яким зовнішнім pilot потрібні свій password, закритий DB port, HTTPS, persistent Data Protection volume та перевірений backup/restore. Compose вже містить відповідні named volumes; standalone ephemeral smoke keys не є production configuration.

## Remaining V1 gaps

M1–M3 і L1 вище. SERVICE ProductPrice UI gap **закрито** окремим завданням після первинної знахідки; деталі — [service-pricing-ui.md](service-pricing-ui.md). Валюти поки technical GUID/read-only UAH, де немає довідникової назви вона не вигадується. Production bootstrap/відновлення доступу потребують контрольованої процедури з наявними засобами Identity; публічний автоматичний seed не додавався. Навантаження на великих реальних даних та production disaster recovery в інфраструктурі користувача не сертифіковані цим функціональним аудитом.

## Explicitly deferred to V2

Inventory count, POS/PRRO, bank/marketplace integration, CRM, multi-company UI, tax/VAT/payroll, forecasting/AI analytics, offline sync, notifications/approvals, payment allocation, cost snapshots, historical backdated stock validation. Не додавалися service packages/variants/time-based/employee/customer pricing, promotions або price-history table.

Audit smoke API/контейнери після перевірки зупинені; робочий trade-system-postgres не змінювався. Відтворювані сценарії й документація збережені в репозиторії, тимчасові outputs — поза ним.
