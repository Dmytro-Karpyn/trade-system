# Features

`products/` — робочий Product CRUD: список, detail, спільна форма, пошук/фільтри, inline створення одиниці та tests.

`counterparties/` — список, create/detail/edit, ролі, системний badge, взаєморозрахунки за валютами та tests.

`purchases/` — список, DRAFT форма, POST/UNPOST, logical delete, партії/current cost/BatchPrice, inline склад, responsive UI та tests.

`sales/` — список, GOODS/SERVICE, DRAFT резерви, FIFO рекомендація партій, snapshot цін, POST/UNPOST/delete, показники за правами, responsive UI та tests.

`stock/` — GOODS залишки по товарах/партіях, Product/Batch detail, рухи/резерви, explicit price types, cost permissions, StockTransfer/WriteOff CRUD transitions, mobile та tests.

`money/` — рахунки, грошові баланси, receipt/payment/transfer форми, lifecycle, permissions, mobile та tests.

`settlements/` — unified balances із фільтрами та окремий detail через спільну Counterparty settlements view, без frontend SUM.

`expenses/` — список/пошук, DRAFT, POST/UNPOST/delete, категорії деревом, read cost impact, purchase/payment cross-links, mobile та tests.

Спільні API/auth/layout/theme залишаються поза features. Feature використовує централізований API client; permissions керують UI, backend авторизує кожний запит. Наступні бізнес-slices не реалізовувати без окремого завдання.

`returns/` — CustomerReturn/SupplierReturn: shared list/form/detail, posted source prefill, backend preview, supplier own reserve, source links; existing sale/purchase permissions and lifecycle APIs.

`reports/` — read-only dashboard, seven reports, period/filter controls, responsive charts, mobile filter drawer and CSV. Server computes all totals/current cost/balances; permissions gate data and routes.

`directories/` — каталог, дерево ProductCategory, Unit/PriceType/StorageLocation/WriteOffReason management, read-only currencies, shared notices і tests. ExpenseCategory посилається на існуючий slice; PriceType markup не переписує BatchPrice.

`admin/` — users/roles, групи чинних permissions і read-only audit із filters, old/new drawer та source links. Backend захищає останнього адміністратора, inactive roles й org isolation; без нових auth/password mechanisms.
