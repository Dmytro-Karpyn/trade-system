# Features

`products/` — робочий Product CRUD: список, detail, спільна форма, пошук/фільтри, inline створення одиниці та tests.

`counterparties/` — список, create/detail/edit, ролі, системний badge, взаєморозрахунки за валютами та tests.

`purchases/` — список, DRAFT форма, POST/UNPOST, logical delete, партії/current cost/BatchPrice, inline склад, responsive UI та tests.

Спільні API/auth/layout/theme залишаються поза features. Feature використовує централізований API client; permissions керують UI, backend авторизує кожний запит. Наступні бізнес-slices не реалізовувати без окремого завдання.
