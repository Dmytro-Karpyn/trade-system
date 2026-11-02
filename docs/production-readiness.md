# Production Readiness

## Status

**NOT READY — EXTERNAL BLOCKER CONFIRMED: proxy блокує blob endpoint.** Аудит: 2026-10-31. Реального deployment не виконано.

V1 перевірено на ізольованій PostgreSQL 18: production configuration, migrations, cookie/CSRF, Caddy HTTPS, поточні бізнес-сценарії, frontend production image та browser smoke. Офіційні .NET base images `mcr.microsoft.com/dotnet/sdk:10.0.402` і `mcr.microsoft.com/dotnet/aspnet:10.0.13` не завантажуються у цьому середовищі: мережевий proxy повертає HTTP 403 на HTTPS CONNECT до `westus.data.mcr.microsoft.com`, до TLS/HTTP-запиту до самого CDN. Registry manifests доступні; це не підтверджена відмова Microsoft CDN на авторизацію image. Docker API build та повний старт саме production Compose **не підтверджені**. Не підміняли base images і не вимикали TLS validation.

Для smoke використано production frontend/Caddy image, PostgreSQL container і `dotnet publish` API з `ASPNETCORE_ENVIRONMENT=Production` на host. Аудитний override відкривав PostgreSQL лише на loopback і направляв Caddy на цей API; цей override зберігається тільки у `/tmp`, **не входить до deployment configuration**. Перевірка підтверджує поведінку застосунку/HTTPS, але не замінює API image/startup gate.

Нових бізнес-можливостей, domain model, бізнес-правил або міграцій цей audit не додає. Зміни стосуються deployment configuration, startup validation, явного CLI bootstrap, logging/error handling, regression tests і документації. Робоча dev-БД не використовувалася.

## Required environment variables

Копіюйте `.env.example` у приватний `.env.production`. Він і `.env` ігноруються Git та Docker build context. Не додавайте secrets у commit, build arguments або screenshots. Compose читає `--env-file`; .NET напряму `.env` **не читає**. Непорожні placeholder values потрібно замінити, порожні secrets — заповнити.

| Variable | Призначення |
| --- | --- |
| `POSTGRES_DB` | Обов'язкове ім'я БД; без production default. |
| `POSTGRES_USER` | Обов'язковий owner/maintenance role PostgreSQL; **не runtime API user**. Офіційний PG image створює цей role як superuser. |
| `POSTGRES_PASSWORD` | Унікальний секрет owner. Зміна env не змінює пароль у вже створеному volume; для ротації використовуйте `psql \password`. |
| `MIGRATION_CONNECTION_STRING` | Окреме owner connection string для one-shot міграцій; `Host=postgres;Database=...;Username=...;Password=...;Timeout=5;Command Timeout=30`. |
| `ConnectionStrings__TradeDatabase` | Runtime connection string з role `trade_app`, тим самим DB та окремим секретом. `Include Error Detail`, `Log Parameters`, `Persist Security Info` мають бути false. |
| `ASPNETCORE_ENVIRONMENT` | Production Compose жорстко задає `Production` для API, міграцій і bootstrap. Не передавати Development. |
| `CADDY_SITE_ADDRESS` | Для staging/production — реальний DNS hostname, наприклад `trade.example.com`, без `http://`. `localhost` призначений лише для локального CA/smoke. |
| `ALLOWED_HOSTS` | Публічний hostname без scheme/port; кілька через `;`. Не `*`. Compose додає `127.0.0.1` для внутрішнього healthcheck. |
| `BIND_ADDRESS` | `.env.example`: `127.0.0.1` для безпечного локального запуску. На deployment host явно виберіть потрібний interface, наприклад `0.0.0.0`. |
| `HTTP_PORT`, `HTTPS_PORT` | Звичайні production значення 80 та 443; HTTPS також UDP/HTTP3. |
| `TRADE_PROXY_SUBNET` | Окремий вільний `/24`, default `172.30.250.0/24`; мусить не перетинатися з мережами сервера. Цей самий CIDR передається як trusted proxy network API. |
| `DataProtection__KeysPath` | Compose задає `/home/app/.aspnet/DataProtection-Keys`, persistent volume. |
| `VITE_API_BASE_URL` | Публічна build setting, **не secret**. Production Compose задає `/api`, той самий origin. |
| `BootstrapAdmin__Username` | Лише для явного maintenance command, без default username. |
| `BootstrapAdmin__Password` | Лише для maintenance command: унікальний секрет щонайменше 16 символів і чинні Identity password requirements. Передавайте з operator environment, потім видаліть. |
| `BootstrapAdmin__OrganizationId` | Явний непорожній GUID організації першого адміністратора. Збережіть обраний GUID. |
| `BootstrapAdmin__FullName` | Необов'язкове ім'я, default `Administrator`. |
| `TRADE_API_IMAGE`, `TRADE_WEB_IMAGE` | Необов'язкові release tags; defaults `trade-system-api:production`, `trade-system-web:production`. Для release бажані immutable tags/digests. |

`POSTGRES_PORT` і `REVERSE_PROXY_KNOWN_NETWORK` із прикладу потрібні лише legacy/local Compose, production файл їх не використовує. Cookie security жорстко визначена сервером; змінна середовища не послаблює її. `DevelopmentSeed__AdminPassword` у production Compose відсутня.

Startup validation відхиляє відсутнє/неповне connection string, відомі development/example DB passwords та увімкнене логування sensitive DB details. Повідомлення не включають raw configuration values.

## Docker services

Використовуйте **`compose.production.yaml`**, не legacy `compose.yaml --profile app`.

| Service | Поведінка |
| --- | --- |
| `postgres` | `postgres:18-alpine`, persistent `/var/lib/postgresql` для PG18, internal database network, жодних published ports, `pg_isready` healthcheck, `unless-stopped`. |
| `migrate` | Той самий API image, явний `--migrate`, owner credentials; старт після healthy PostgreSQL, одноразовий exit, без listener/admin seed. |
| `api` | Non-root `USER app` у Dockerfile, `init`, dropped capabilities, no-new-privileges, private database/proxy networks. Старт тільки після успішного migrate, `unless-stopped`, DB-aware healthcheck через `dotnet Trade.Api.dll --health-check`. |
| `caddy` | Production React/Vite static frontend у Caddy image, не Node server. Automatic HTTPS, healthy API dependency, loopback-only internal liveness endpoint, `unless-stopped`. Назовні тільки 80/443. |
| `bootstrap-admin` | Profile `maintenance`, одноразова явна CLI-команда, без HTTP listener. Секрет адміністратора не передається довгоживучому API/migrate. |

Named volumes: `postgres_data`, `data_protection`, `caddy_data`, `caddy_config`. Docker local logging обмежене 5 файлами по 10 MiB на service. `down` зберігає volumes; **не виконуйте `down -v` для потрібних даних**.

Валідація production Compose та Caddy пройшла. Frontend/Caddy image зібрано. API image build заблоковано CDN, тому container UID/permissions, реальний API healthcheck усередині image і повний dependency startup ще треба перевірити після розблокування registry.

### Docker gate follow-up — 2026-10-31

**Результат: B. EXTERNAL BLOCKER CONFIRMED.** Production-readiness залишається NOT READY. Dockerfile, backend code, domain model, image tags та Compose під час follow-up не змінювалися. Синтаксис Dockerfile прийнятий BuildKit; обидва FROM існують в офіційному Microsoft Container Registry та відповідають .NET 10. Виконання build/runtime stages після отримання base images залишається неперевіреним.

**Images та architecture.** `backend/Trade.Api/Dockerfile` використовує:

| Stage | Registry / repository / tag | Tag manifest | linux/amd64 manifest |
| --- | --- | --- | --- |
| build | `mcr.microsoft.com/dotnet/sdk:10.0.402` | HTTP 200 | HTTP 200 |
| runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.13` | HTTP 200 | HTTP 200 |

SDK 10.0.402 відповідає `global.json`; ASP.NET Core runtime 10.0.13 — та сама major version 10. У manifests також є linux/arm/v7 і linux/arm64. Поточний host — Linux x86_64, Docker daemon/client — linux/amd64, `DOCKER_DEFAULT_PLATFORM` не задана, у Compose немає platform override. Diagnostic `--platform linux/amd64` відповідає native platform цього host; майбутню архітектуру іншого deployment host цим не підтверджено. Теги залишено без змін.

**Окремі pulls і повтор із native platform.** Усі чотири команди завершилися exit 1, однакова terminal error після 6 внутрішніх спроб Docker:

```text
error pulling image configuration: download failed after attempts=6: Forbidden
```

Точні команди з кореня repo, виконані в поточному Linux-середовищі:

```sh
DOCKER_CONFIG=/tmp/trade-production-docker docker pull mcr.microsoft.com/dotnet/sdk:10.0.402
DOCKER_CONFIG=/tmp/trade-production-docker docker pull mcr.microsoft.com/dotnet/aspnet:10.0.13
DOCKER_CONFIG=/tmp/trade-production-docker docker pull --platform linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0.402
DOCKER_CONFIG=/tmp/trade-production-docker docker pull --platform linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0.13
```

`DOCKER_CONFIG` — локальний writable каталог без `config.json`/auth, потрібний для Buildx state цього cloud workspace. Він не змінює daemon, endpoint або registry. Default client config також не має registry auths/credential helpers/custom HTTP headers. `docker login` не потрібний для публічних MCR images і не усуває відмову CONNECT. Secrets/config values не друкувалися.

**Де саме падає.** Registry `/v2/dotnet/{sdk|aspnet}/manifests/{tag}` і відповідний linux/amd64 digest повертають 200. Blob endpoint на `mcr.microsoft.com` повертає 307 на Microsoft data endpoint `westus.data.mcr.microsoft.com`. При переході proxy повертає `403 Forbidden` на CONNECT; urllib повідомляє `Tunnel connection failed: 403 Forbidden`, curl — exit 56 `CONNECT tunnel failed, response 403`. Той самий CONNECT 403 відтворюється для кореня CDN host, без signed URL. Отже, це не прострочений signed query, не відсутній tag, не невідповідна architecture і не доведена проблема credentials самого MCR. Причину конкретної proxy policy може підтвердити лише власник мережі.

Config blobs, на яких завершуються pulls:

- SDK: `sha256:7ca4958c64b7f73fea033fc86c35eca186fc3313cab3df4bf85134691d86724d`.
- ASP.NET: `sha256:3ad8ff999165897fa657a9aa29f6bea22cd7db50269f4b99ec97f7858b21f990`.
- Перший filesystem layer обох images також перевірено окремо: `sha256:f2b4ef06ff01a67d4bed7657a4ae379a7d07934b7c3e743454a22a0d5275fb2b` → 307 → CONNECT 403 на тому самому CDN.

Для кожного config blob виконано дві незалежні HTTP-спроби; результат стабільний. Docker паралельно пробує filesystem layers, але terminal error стосується image configuration. Signed redirect URLs та їхні query parameters навмисно не включені у звіт.

**Docker/network diagnostics.** `docker info`: Engine 28.4.0, Linux containerized Debian 13, vfs storage driver; context `default`, endpoint `unix:///var/run/docker.sock`. Це Docker Engine, не Docker Desktop: Desktop GUI/network settings у цьому середовищі відсутні; налаштування Docker Desktop користувача на Windows цим audit не перевірялися. Registry mirrors відсутні. Daemon HTTP/HTTPS proxy — `http://proxy:8080`, без credentials; NO_PROXY — тільки loopback. Default client build proxy та isolated config не змінюють daemon proxy, який обслуговує pulls.

Локальні public DNS lookups повертають `EAI_AGAIN`; proxy hostname доступний через host mapping. Доступ до MCR manifests через proxy працює. Public DNS failure є окремим обмеженням цього managed environment і не пояснює/не усуває відтворювану CONNECT 403 від proxy на data endpoint. DNS, proxy, Docker daemon/Desktop configuration не змінювалися. `docker builder prune` не виконувався: видалення спільного cache не виправляє відмову CONNECT і не потрібне для цієї перевірки. Volumes/data не видалялися.

**Офіційний альтернативний шлях.** [Microsoft Artifact Registry](https://github.com/microsoft/containerregistry) документує MCR як основний registry Microsoft images, а Docker Hub — як discovery/catalog integration; це не доказ незалежного mirror з тими самими tags/blobs. [Офіційні Client Firewall Rules](https://github.com/microsoft/containerregistry/blob/main/docs/client-firewall-rules.md) окремо вимагають HTTPS-доступ до `mcr.microsoft.com` та `*.data.mcr.microsoft.com` і документують офіційний регіональний counterpart `mcr.azure.cn` / `*.data.mcr.azure.cn`. Анонімні manifest GET до `mcr.azure.cn/v2/dotnet/sdk/manifests/10.0.402` та `mcr.azure.cn/v2/dotnet/aspnet/manifests/10.0.13` також відхилені proxy на CONNECT з 403. Наявність саме цих тегів у counterpart тому не підтверджена; його не використано для build. Доступний незалежний офіційний mirror для цих images у цьому середовищі не знайдено. Сторонні registries/images, вимкнення TLS, підміна DNS або обхід proxy не застосовувалися.

**Повторний API build.** Точна команда:

```sh
DOCKER_CONFIG=/tmp/trade-production-docker docker build --progress=plain -f backend/Trade.Api/Dockerfile -t trade-system-api:production .
```

Exit 1 до restore/compile: `failed to resolve source metadata for mcr.microsoft.com/dotnet/aspnet:10.0.13` → blob URL на `westus.data.mcr.microsoft.com` → `Forbidden`. SDK metadata task скасовано BuildKit після цієї помилки; окремий SDK pull вище незалежно підтвердив ту саму відмову. Порядок відмови FROM у паралельному BuildKit не є відмінністю проблеми.

Native publish повторно перевірено успішно (exit 0, без warnings), без запуску HTTP listener або доступу до БД:

```sh
dotnet publish backend/Trade.Api/Trade.Api.csproj -c Release --no-restore -o /tmp/trade-docker-gate-20261031/publish /p:UseAppHost=false
```

**Compose smoke:** не запускався повторно через відсутній API image. Не підтверджені container runtime/non-root/key-volume permissions, API image healthcheck, migrate→api→caddy dependency startup та login/dashboard у повністю контейнерному stack. Попередній native API + Docker Caddy/PostgreSQL smoke лишається окремим результатом, не закриває цей gate. Робочий `trade-system-postgres-1` залишився healthy; follow-up не створював БД, volumes або application containers.

**Наступний безпечний крок:** власник build-network має дозволити HTTPS CONNECT до офіційних `mcr.microsoft.com` і `*.data.mcr.microsoft.com` згідно з Microsoft Firewall Rules, або виконати ці самі pulls/build на дозволеному build host. Після успішного API build обов'язково виконати описаний нижче full production-like Compose smoke з окремим project/volume, тестовими secrets, localhost/local CA та login/dashboard. Реальний deployment не виконувати в межах цього gate.

## Database

Production PostgreSQL не публікує host port. API/Caddy мають окрему proxy network; PostgreSQL доступна тільки database network. Credentials отримуються через env. Для Docker на одному host транспорт до PostgreSQL приватний; для зовнішньої БД оператор мусить налаштувати TLS із перевіркою сертифіката.

Runtime user має бути окремим LOGIN без `SUPERUSER`, `CREATEDB`, `CREATEROLE`, `REPLICATION`, ownership БД/таблиць та membership owner role. Для свіжої БД, **після міграцій**, створіть `trade_app` як owner/operator, встановіть пароль інтерактивно, потім застосуйте grants:

```sh
docker compose --env-file .env.production -f compose.production.yaml exec postgres psql -U YOUR_OWNER -d YOUR_DATABASE -c 'CREATE ROLE trade_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION'
docker compose --env-file .env.production -f compose.production.yaml exec postgres psql -U YOUR_OWNER -d YOUR_DATABASE -c '\password trade_app'
# Bash: pipe only the non-secret SQL file; on PowerShell use Get-Content ... | docker ...
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres psql -U YOUR_OWNER -d YOUR_DATABASE -v runtime_role=trade_app -v migration_role=YOUR_OWNER < scripts/grant-runtime-role.sql
```

Замість `YOUR_OWNER`/`YOUR_DATABASE` використайте налаштовані `POSTGRES_USER`/`POSTGRES_DB`. Після password prompt запишіть той самий runtime secret у приватне `ConnectionStrings__TradeDatabase`. Для існуючого role не виконуйте повторний CREATE; перевірте привілеї та виконайте grants.

`scripts/grant-runtime-role.sql` надає потрібні DML/sequence/default grants, забирає UPDATE/DELETE `audit_logs` і write migration history. Це SQL привілеїв, **не schema migration**. Чинний trigger також забороняє зміну audit entries. Grants повторно застосовуйте після release migrations. У smoke runtime role мав усі superuser/create-role/create-db/replication flags false; UPDATE/DELETE audit, INSERT migration history та CREATE schema були заборонені. Purchase/Sale/Stock/Admin операції працювали з цим role.

## Migrations

У repo вже є повна EF chain; нових міграцій аудит не створює. Production startup не виконує schema update неявно. Compose запускає одноразовий `migrate` перед API, неуспішні міграції блокують API/Caddy.

Перевірено реальним `dotnet ef database update` з `ASPNETCORE_ENVIRONMENT=Production`:

- свіжа порожня PostgreSQL 18 БД → усі migrations застосовані;
- повторний update → нічого застосовувати;
- БД на попередній migration → upgrade до latest успішний;
- повторний update existing БД → pending migrations немає;
- `dotnet ef migrations has-pending-model-changes` → model відповідає snapshot.

SDK-команди для ізольованого тестового середовища/захищеного DB connection (connection string через env, не command-line argument):

```sh
dotnet tool restore
dotnet ef database update --project backend/Trade.Infrastructure --startup-project backend/Trade.Api -c Release --no-build
dotnet ef migrations has-pending-model-changes --project backend/Trade.Infrastructure --startup-project backend/Trade.Api -c Release --no-build
```

Перед SDK-командами встановіть `ConnectionStrings__TradeDatabase` на доступний maintenance connection та `ASPNETCORE_ENVIRONMENT=Production`; не відкривайте production PostgreSQL назовні для EF CLI. Docker release використовує `migrate --migrate` service. Перед оновленням потрібні backup і погоджене maintenance window. Окремий backup/restore drill завершено PASS: [backup-restore.md](backup-restore.md).

## Authentication

Identity cookie `trade.auth`: HttpOnly, Secure поза Development, SameSite=Lax, 8-hour lifetime зі sliding renewal. CSRF cookie `trade.csrf`: HttpOnly/Secure/Lax. Cookies мають host scope; passwords/токени не зберігаються у Web Storage. Frontend використовує `credentials: include`, same-origin `/api`, CSRF header `X-CSRF-TOKEN`; token оновлюється після login, writes автоматично не replay-яться. Password hashing, lockout та перевірка active roles/permissions залишилися чинними. Logout змінює security stamp, cookie replay відхиляється.

Caddy завершує TLS, API приймає тільки `X-Forwarded-Proto`, один hop, від явно довіреної proxy network/loopback; довільним зовнішнім forwarded headers не довіряє. API не має public port. Existing proxy integration tests перевіряють trusted/untrusted IP. HTTPS/cookies/CSRF також перевірені реальним Chromium через Caddy.

Development seed працює **лише** за `IsDevelopment()` і явно переданого password. Production/Staging не seed-ять admin навіть якщо задано `DevelopmentSeed__AdminPassword` — це підтверджено integration tests. `Test123!Test` не є production credential/default.

Production administrator: явна **одноразова** команда `--bootstrap-admin`. Вона дозволена лише Production/Staging, вимагає обрані username/organization та сильний secret, створює чинний OWNER з наявними permissions, використовує транзакцію і відмовляється перезаписувати будь-якого existing user у цій організації або existing username. Normal startup і `--migrate` її ніколи не викликають. Identity validation failure відкочує створення role/user; це перевірено тестом.

PowerShell після міграцій і налаштування runtime DB role (username/organization з приватної `.env.production` або operator environment):

```powershell
$securePassword = Read-Host 'Unique production admin password (16+ chars)' -AsSecureString
$env:BootstrapAdmin__Password = [System.Net.NetworkCredential]::new('', $securePassword).Password
try {
    docker compose --env-file .env.production -f compose.production.yaml --profile maintenance run --rm --no-deps bootstrap-admin
    if ($LASTEXITCODE -ne 0) { throw 'Admin bootstrap failed; review service logs.' }
} finally {
    Remove-Item Env:BootstrapAdmin__Password -ErrorAction SilentlyContinue
    $securePassword.Dispose()
}
```

Не залишайте bootstrap password у `.env.production`; Compose читає його з поточного operator environment. Не використовуйте цей command для password reset або повторного provisioning існуючої організації.

## Caddy / HTTPS

`Caddyfile` перевірений `caddy validate`. Для справжнього DNS hostname automatic HTTPS/HTTP→HTTPS, persistent ACME storage. DNS A/AAAA повинні вказувати на server; 80/443 доступні; якщо зовнішній CDN/load balancer з'явиться пізніше, trust settings потрібно окремо переглянути. В audit використовувався Caddy local CA для `localhost`; Chromium мав `ignoreHTTPSErrors` **лише для цієї локальної CA**. Public CA issuance/renewal у цьому audit не перевіряли.

`/api/*` прибирає prefix і проксіюється до API, `/health` — API DB health. HTML document routes отримують SPA fallback і працюють після reload. Legacy root API paths збережені для non-HTML clients. Приватні `/.env`, `/.git`, `/backend`, `/node_modules`, `/.config` шляхи блокуються до SPA handler; реальних source/env файлів у `/srv` немає. Compression zstd/gzip; hashed assets immutable, index no-cache, API no-store.

Headers: nosniff, frame DENY, referrer policy, permissions policy, CSP same-origin; `style-src 'unsafe-inline'` потрібний поточному MUI/Emotion. Для public host — HSTS max-age 1 year без preload/includeSubDomains. Caddy admin і liveness listener прив'язані до loopback, не published. Нестандартні локальні audit ports потребували явного `https://localhost:18443`; production redirect очікує стандартний HTTPS port 443.

## Logging

Serilog пише startup, request method/path/status/duration, unhandled exceptions і DB failures у console; Docker rotation обмежує локальний обсяг. Healthy probes не засмічують Info log; unhealthy probes пишуть Error. Login rejection має окреме Warning без username/password/body/cookie/token. EF sensitive-data logging не ввімкнено; production validator блокує parameter logging/error details/persisted connection credentials.

Live check: startup, auth failure, PostgreSQL outage, unhandled antiforgery HTTP failure присутні у server logs. Реальні ізольовані admin/DB passwords, auth/CSRF cookie values та test password-canary у перевірених API/bootstrap logs відсутні. Stack traces допустимі у захищених server logs, клієнту їх не повертають. Не вмикайте body/header logging або Npgsql parameter logging у deployment.

## Health checks

`GET /health` anonymous, `{"status":"Healthy"}` / 200 при доступній PostgreSQL, 503 `Unhealthy` при її зупинці, без connection details. Live database outage/restart перевірено. Readiness CLI повертав відповідно 0 → 1 → 0. API Compose probe використовує внутрішній `127.0.0.1:8080` і дозволений loopback Host.

PostgreSQL `pg_isready`, Caddy loopback `:8081/healthz`; migrate без healthcheck, оскільки він має успішно завершитися. Docker healthcheck сам по собі не перезапускає unhealthy service; `unless-stopped` перезапускає exited process. Оператор має реагувати на unhealthy стан; monitoring platform не додавалася.

## Frontend production build

`npm ci`, `npm run lint`, `npm test`, `npm run build`: **passed**; **461/461 tests**, 15 test files. Docker frontend/Caddy production image також успішно зібраний із locked dependencies. Production API base — `/api`; localhost backend/dev port не hardcode-иться. `VITE_API_BASE_URL` застосовується під час build, зміна runtime env готового static image його не змінює.

Chromium: login, dashboard, purchase list/detail, sale list/detail, stock, money, reports/profitability, admin/users/roles/audit; desktop 1440 і mobile 390. **20 route visits, 0 unexpected errors/warnings**. Очікуваний anonymous `/auth/me` 401 до login зафіксований окремо. Перевірені реальні створення/POST purchase та sale, stock result, cookies/CSRF, SPA reload, static compression, private file denial. Дані лише disposable audit DB. Повторюваний harness: `scripts/production-readiness-smoke.mjs`; він не призначений для робочих даних, credentials отримує через environment, private browser state залишає лише у `/tmp` default output.

## Backend production build

.NET SDK 10.0.402 / runtime 10.0.13. Locked `dotnet restore`, `dotnet build -c Release`, `dotnet publish -c Release`: **passed, 0 warnings / 0 errors**. Усі backend tests: **539/539 passed**, 0 failed, 0 skipped; final run 7 хв 47 с. Додано 17 production configuration/bootstrap/error regression cases; focused **17/17 passed**. Existing Development-only seed, proxy, cookie, auth/CSRF, organization isolation і бізнес integration tests збережені.

Команди з кореня:

```sh
dotnet restore backend/Trade.slnx --locked-mode
dotnet build backend/Trade.slnx -c Release --no-restore
dotnet test backend/Trade.slnx -c Release --no-build --no-restore
dotnet publish backend/Trade.Api/Trade.Api.csproj -c Release --no-restore -o ./artifacts/api /p:UseAppHost=false
```

Docker API build **blocked** на отриманні Microsoft base images, до компіляції image. Усі перевірки native publish не усувають цей blocker.

## Security findings

Виправлено: cache headers розділені для hashed assets та HTML без взаємного перезапису; production connection string більше не успадковує development password; required secrets/unsafe logging settings перевіряються на startup; окремий production Compose без DB/API published ports; вузький proxy CIDR; persistent keys із mode 700 у image; bounded auth cookie lifetime; явний admin bootstrap замість production development seed; safe empty error responses; auth failure log; health dependency order; HTTPS/security headers; приватні paths блокуються перед SPA fallback; runtime DB role/grants задокументовані й перевірені.

Перевірено: auth/CSRF secure cookies, lockout, logout/replay, стандартні 400/401/403/409/500 Problem Details без stack trace, DB outage 503; CORS не ввімкнено (same origin); Swagger/OpenAPI лише Development, production authenticated API повертає 404. Test-only authentication handler знаходиться в test assembly, auth UI не використовує spoofable organization headers. Upload/attachment endpoints у поточному backend не знайдені; складні upload controls не додавалися.

Worktree: 481 text files та 2 reachable Git commits перевірено на high-confidence private key/GitHub/AWS token patterns — збігів немає; `.env` ignored і його вміст не читався. Це обмежений quick scan, не підтвердження ротації секретів або аудит стороннього secret store. Відомий локальний dev DB password лишається лише Development configuration/test fixtures, production його відхиляє. `npm audit --omit=dev`: 0 vulnerabilities. NuGet include-transitive vulnerability check: 0 reported vulnerabilities у 5 projects на момент audit.

Data Protection keys у volume не мають окремого application key-encryption key; ASP.NET видає відповідне Warning. Mode 700 заданий у image для нового key volume; для existing volume перед оновленням перевірте owner/mode, оскільки image build не змінює права існуючого mount. Host encrypted storage/access control потрібні, encrypted key/backup protection залишається operational prerequisite. Application admin/MFA/roles model не змінювався.

## Backup commands

У перевіреному PostgreSQL image доступні **pg_dump 18.6** та **pg_restore 18.6**. Окремий ізольований drill завершено **BACKUP/RESTORE PASS**: custom archive 158 261 bytes, restore на іншому volume, logical comparison 37 tables, 29 audit records, 30 API responses та sequence 5 → 6. Команди й повний runbook: [backup-restore.md](backup-restore.md). Production RPO/RTO, encryption/storage/retention/off-host policy цим маленьким dataset не перевірялися.

Приклад для Bash на захищеному deployment host. Connection password береться лише всередині контейнера; його немає у host command arguments. `backups` містить sensitive data, має бути поза repository, доступ 700/600, encryption/retention/off-host copy — обов'язки оператора.

```sh
umask 077
mkdir -p /secure/trade-backups
# -T preserves binary custom archive; stderr is not redirected into the archive.
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc --no-owner --no-acl' > /secure/trade-backups/trade.dump
# Inspect archive metadata only:
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres pg_restore --list < /secure/trade-backups/trade.dump
```

Операторський приклад restore: тільки окрема порожня disposable DB `RESTORE_TARGET`, не поточна production БД. Drill вже виконав еквівалентне відновлення у власному isolated target; наведена команда проти production **не виконувалася**:

```sh
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -h 127.0.0.1 -U "$POSTGRES_USER" -d RESTORE_TARGET --exit-on-error --no-owner --no-acl' < /secure/trade-backups/trade.dump
```

Після restore потрібні grants, migrations/model verification, `ANALYZE`, health та business smoke. Ролі/їхні credentials зберігайте окремо захищено; dump без owner/ACL їх не відновлює. Data Protection keys та Caddy volumes мають окрему encrypted backup policy. Для binary streams на Windows використайте перевірений binary-safe shell/transfer, не старий Windows PowerShell text redirection.

## Deployment prerequisites

Staging preparation runbook: [staging-deployment.md](staging-deployment.md). Окремий Compose override, env/provisioning/release helpers і strict-HTTPS smoke harness підготовлені; VPS/domain/DNS/SSH ще не створені, remote deployment та staging gates не виконувалися. Ця підготовка не змінює статус Docker gate або production-readiness.

1. Розблокувати офіційні .NET registry/CDN на build host; отримати успішний API Docker build і **повторити full Compose clean-image startup + browser smoke**. Не оголошувати staging готовим до цього gate.
2. Підготувати підтримуваний Docker Engine/Compose v2, диск/volumes, вільний proxy subnet, hostname/DNS, firewall 80/443. PostgreSQL/API порти не публікувати. Repository working state ще треба доставити на build host контрольованим release process; цей audit нічого не commit/push/deploy-ив.
3. Приватний `.env.production`: різні owner/runtime DB passwords, real domain/AllowedHosts, обрані admin username/OrganizationId. Bootstrap password передати лише на одноразову команду.
4. Для першого запуску (операторські команди, у цьому audit real deployment не виконувався):

```sh
docker compose --env-file .env.production -f compose.production.yaml config --quiet
docker compose --env-file .env.production -f compose.production.yaml build api caddy
docker compose --env-file .env.production -f compose.production.yaml up -d --wait postgres
docker compose --env-file .env.production -f compose.production.yaml run --rm --no-deps migrate
# Create runtime role, set password, apply grants as shown above.
# Execute explicit bootstrap-admin with the secret provided through the operator environment.
docker compose --env-file .env.production -f compose.production.yaml up -d --no-build --wait
curl --fail https://YOUR_DOMAIN/health
docker compose --env-file .env.production -f compose.production.yaml ps
```

5. Для existing installation не bootstrap-ити повторно. Погодити maintenance window, backup, виконати migration, повторні grants і health/smoke. Не запускати кілька release migration jobs паралельно.
6. Обмежити доступ до Docker socket, env, keys, DB volumes та logs; використати encrypted host/backups. Визначити відповідального за recovery і ротацію секретів. Ізольований backup/restore drill уже пройшов; перед справжнім production налаштуйте й перевірте production storage/encryption/retention/off-host copy та цільові RPO/RTO.

## Critical blockers

- **API Docker build / full production Compose startup не підтверджені: proxy повертає HTTP 403 на HTTPS CONNECT до Microsoft blob/CDN endpoint.** Потрібен доступ registry з release/staging build host і успішне повторення gate. Native smoke не замінює цю перевірку.

## High priority

- До staging оператор повинен встановити реальні secrets/domain, обмежений runtime DB role і виконати явний bootstrap; repo їх навмисно не містить. Не використовувати `POSTGRES_USER` як API runtime user.
- Data Protection keys та backups потребують encrypted storage і access policy; окремий key encryption mechanism не налаштований. Втрата/витік keys впливає на сесії.
- Backup/restore drill — PASS; перед production залишаються recovery/retention/off-host/encryption policy, цільові RPO/RTO, HTTPS public-certificate/renewal check та release rollback procedure. Див. [backup-restore.md](backup-restore.md).

## Minor issues

- PG18/Caddy2/Node24 tags прив'язані до major, не digest; для відтворюваних release artifacts зафіксуйте перевірені digests у release process і регулярно оновлюйте security patches.
- Docker health status не є автоматичним recovery/alerting. Узгодьте операційне реагування на unhealthy/disk-full/certificate renewal errors; monitoring platform не реалізовувалася.
- Caddy local CA не є публічним TLS certificate. Нестандартний локальний port перевірявся вручну за HTTPS URL; реальні DNS/ACME/host firewall у staging мають власний smoke gate.
- Legacy local Compose з `app` profile залишений для сумісності; production конфігурація і prerequisites — лише в окремому production файлі.
