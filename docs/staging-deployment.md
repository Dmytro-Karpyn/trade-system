# Staging Deployment

**Scope: preparation only. Deployment не виконувався.** VPS, SSH access, staging domain і DNS ще не створені. IP/domain/credentials не призначалися. Наведені команди виконувати після отримання фактичних реквізитів, на окремому staging VPS. Поточні локальні Docker/CDN обмеження не обходилися; backend, frontend business code, domain model, migrations, Dockerfiles та Caddyfile не змінені.

Підготовлено `compose.staging.yaml`, публічний `.env.staging.example`, генератор приватного env, restricted DB-role provisioning, snapshot packager і HTTPS smoke harness. Local checks: Compose interpolation/isolation/dependency gates, private env mode/overwrite refusal, Python/Node syntax та Caddy template validation у container без network/server startup. Remote pulls/build/startup/TLS/migrations/admin/E2E/restart/backup/security **NOT RUN**, доки немає VPS. Попередні 539 backend / 461 frontend tests і backup/restore PASS — окремі результати, не staging deployment proof.

Порядок майбутнього виконання: (1) створити VPS та SSH key access; (2) налаштувати DNS/firewall і Docker; (3) доставити та перевірити source snapshot, створити release/current/shared directories; (4) створити приватний staging env; (5) виконати official pulls і build; (6) migrations → runtime grants → явний admin bootstrap; (7) start → public HTTPS/health; (8) E2E/mobile → restart → backup → logs/security. Після кожної помилки зупинятися; remote PASS фіксувати лише за фактичними результатами.

## Prerequisites

Рекомендована конфігурація для V1 staging із build на самому VPS:

| Resource | Recommended | Minimum for a small staging |
| --- | --- | --- |
| CPU / architecture | **4 vCPU, x86_64 / amd64** | 2 vCPU, amd64 |
| RAM | **8 GiB** | 4 GiB; builds можуть потребувати більше пам'яті |
| Storage | **80 GiB SSD/NVMe** | 40 GiB зі строгим контролем вільного місця |
| OS | **Ubuntu Server 24.04 LTS** або **Debian 13**, amd64 | Підтримувана Docker версія Ubuntu/Debian з security updates |
| Network | Стабільний public IPv4, доступ до DNS та outbound HTTPS | SSH + TCP 80/443; UDP 443 опціонально для HTTP/3 |

Це окремий VPS/project/volume/env/organization. Production credentials і локальні test DB dumps не переносити. Host .NET/Node для build не потрібні: вони у Docker build stages. Потрібні Docker Engine, Compose v2 plugin, Python 3, curl, CA certificates, OpenSSH, tar, достатній диск і синхронізований clock. Source packager запускається у цьому Git workspace; на VPS `.git` не потрібний.

Після створення VPS потрібні: фактичний IP/hostname, SSH user/port/alias, спосіб key access, перевірений SSH host-key fingerprint, staging FQDN, DNS status. Admin username можна обрати окремо; OrganizationId генерується один раз. Паролі/private keys через чат не передавати.

SSH setup:

1. На своєму комп'ютері створити окремий Ed25519 key з passphrase, наприклад PowerShell `ssh-keygen -t ed25519 -a 100 -f "$env:USERPROFILE\.ssh\trade-staging"`. Додати **public `.pub` key** через panel/cloud-init VPS provider. Private key лишається на клієнті або у захищеному SSH agent.
2. Перевірити server fingerprint через provider console. Для першого SSH connection використовувати звичайну перевірку host key; не вимикати `StrictHostKeyChecking`. Не пересилати agent на VPS.
3. Використовувати окремий sudo-capable deployment account. Його фактичне ім'я залежить від створеного VPS; не припускати `root`/`ubuntu` без перевірки. Якщо account створюється вручну, налаштувати `authorized_keys` owner/mode 700/600 та робочий sudo до вимкнення password/root login.
4. Рекомендований client SSH config template, замінивши placeholders фактичними значеннями:

```sshconfig
Host trade-staging
    HostName VPS_IP_OR_HOSTNAME
    User SSH_USER
    Port SSH_PORT
    IdentityFile ~/.ssh/trade-staging
    IdentitiesOnly yes
    ForwardAgent no
```

5. Після перевірки нового key-only session і доступу до provider console налаштувати SSH server: `PasswordAuthentication no`, `KbdInteractiveAuthentication no`, `PermitRootLogin no`; перед reload виконати `sudo sshd -t`. Зберігати початкову SSH session до успішної перевірки нової.

Ports/firewall:

| Port | Access |
| --- | --- |
| SSH TCP 22 або фактичний custom SSH port | Тільки від довірених operator IP/CIDR; зберегти доступ до зміни firewall |
| TCP 80 | Public: HTTP redirect та ACME challenge |
| TCP 443 | Public: HTTPS |
| UDP 443 | Public, якщо використовується HTTP/3; TCP HTTPS працює окремо |
| TCP 5432 | **Не publish**, тільки internal database network |
| TCP 8080 | **Не publish**, тільки API↔Caddy/DB private networks |
| TCP 2019 / 8081 | Caddy admin/liveness тільки loopback усередині container, не host ports |

Спочатку перевірити provider firewall і поточний SSH port. Docker published ports можуть обходити звичайні UFW правила; основний захист DB/API — відсутність published ports плюс provider firewall. Не вмикати firewall навмання із закритим SSH.

## DNS

Після вибору реального staging FQDN створити **A → фактичний public IPv4 VPS**. Початковий setup використовує IPv4 bind; **AAAA не додавати**, доки IPv6 routing/listeners/firewall не налаштовані та перевірені. Stale AAAA може зламати ACME/доступ. Якщо є CAA, він має дозволяти вибраний Caddy ACME CA. Спочатку використовувати прямий DNS до VPS без додаткового CDN/proxy; його введення потребуватиме перегляду trusted proxies.

Перевірити A/AAAA з зовнішнього resolver/client (`dig +short A "$STAGING_DOMAIN"`, `dig +short AAAA "$STAGING_DOMAIN"`). Цю перевірку наразі не виконували: домен не визначений. `STAGING_DOMAIN` у командах нижче — фактичний hostname, без scheme/port/path.

## Environment variables

Шаблон: [`.env.staging.example`](../.env.staging.example). Приватний файл — `/opt/trade-system-staging/shared/.env.staging`, owner deployment user, directory 700/file 600. `.env.staging` ignored Git і Docker context. `docker compose config --quiet` перевіряє interpolation без друку secrets; не друкувати повний resolved config. Compose process environment має пріоритет над env file: використовуйте окрему staging SSH session без експортованих production POSTGRES/ConnectionStrings/image variables.

| Variable | Staging policy |
| --- | --- |
| `POSTGRES_DB`, `POSTGRES_USER` | `trade_staging`, maintenance owner `trade_staging_owner` у generator |
| `POSTGRES_PASSWORD` | Новий випадковий staging-only secret |
| `POSTGRES_RUNTIME_USER`, `POSTGRES_RUNTIME_PASSWORD` | Окремий restricted runtime role/secret; використовуються provisioning helper |
| `ConnectionStrings__TradeDatabase` | Runtime role, `Host=postgres`, staging DB; генерується з окремим password |
| `MIGRATION_CONNECTION_STRING` | Maintenance owner, та сама staging DB; тільки migrate job |
| `ASPNETCORE_ENVIRONMENT` | **Production**, примусово також у base Compose |
| `CADDY_SITE_ADDRESS`, `ALLOWED_HOSTS` | Фактичний staging FQDN, не localhost, без scheme/port; Compose додає loopback health Host |
| `BIND_ADDRESS`, `HTTP_PORT`, `HTTPS_PORT` | `0.0.0.0`, 80, 443; відкривати тільки ці application ports |
| `TRADE_PROXY_SUBNET` | Вільний private /24, staging default `172.30.251.0/24`; перевірити відсутність overlap з VPS/VPN/Docker networks |
| `TRADE_API_IMAGE`, `TRADE_WEB_IMAGE` | `staging-<releaseId>` tags, прив'язані до snapshot |
| `VITE_API_BASE_URL` | `/api`, build-time public setting; base Compose фіксує same-origin |
| `BootstrapAdmin__Username`, `BootstrapAdmin__OrganizationId` | Явний staging user та окремий GUID, збережені для цього environment |
| `BootstrapAdmin__Password` | Порожній у файлі; тільки transient environment для явного maintenance command |
| Data Protection path | Compose: `/home/app/.aspnet/DataProtection-Keys` → staging persistent volume; не secret у env template |
| Caddy email | Не обов'язковий для current automatic HTTPS; поточний Caddyfile не задає email/contact env |

Після source upload/extract і створення directories із розділу Compose, отримавши domain/release ID, **на VPS з directory release**, створити env один раз:

```sh
STAGING_ROOT=/opt/trade-system-staging
install -d -m 700 "$STAGING_ROOT/shared"
python3 scripts/create-staging-env.py --domain "$STAGING_DOMAIN" --release-id "$RELEASE_ID" --admin-username staging-admin --output "$STAGING_ROOT/shared/.env.staging"
```

Helper не друкує secrets, використовує різні random DB passwords, mode 600, генерує новий OrganizationId і відмовляється overwrite. На update не генерувати env заново: це не спосіб rotation/reset існуючого DB volume. `Test123!Test`, DevelopmentSeed і production credentials не використовувати.

## Docker setup

Нижче команди **для нового виділеного VPS**, зараз не виконувалися. Перевірити `/etc/os-release`, `uname -m`, `dpkg --print-architecture`, clock та вільний диск. Target — native amd64; не форсувати іншу architecture. Якщо Docker вже встановлено, спочатку перевірити його config/data, не видаляти чужі packages/volumes.

Офіційні інструкції: [Ubuntu](https://docs.docker.com/engine/install/ubuntu/), [Debian](https://docs.docker.com/engine/install/debian/). Приклад Bash із official apt repository:

```sh
set -euo pipefail
. /etc/os-release
case "$ID" in ubuntu|debian) ;; *) echo 'Use supported Ubuntu/Debian'; exit 1 ;; esac
test "$(dpkg --print-architecture)" = amd64
sudo apt-get update
sudo apt-get install -y ca-certificates curl python3 openssh-client
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
codename=${UBUNTU_CODENAME:-$VERSION_CODENAME}
sudo tee /etc/apt/sources.list.d/docker.sources > /dev/null <<EOF
Types: deb
URIs: https://download.docker.com/linux/$ID
Suites: $codename
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker "$USER"
# Log out and back in before running Docker as this user.
```

Docker group дає root-equivalent доступ; додавати тільки staging operator. Після нового SSH session перевірити:

```sh
docker version
docker compose version
docker info --format '{{.OSType}} {{.Architecture}}'
docker pull mcr.microsoft.com/dotnet/sdk:10.0.402
docker pull mcr.microsoft.com/dotnet/aspnet:10.0.13
```

Якщо pulls fail — зупинити gate, зафіксувати hostname/status/proxy phase без secrets. Дозволити official `mcr.microsoft.com` та `*.data.mcr.microsoft.com` у build-network; не міняти валідні tags/Dockerfile і не підміняти images. Build також потребує Docker Hub, NuGet, npm та їхніх CDN. Локальний cloud proxy blocker не є результатом VPS pull test.

## Compose

Файли: [compose.production.yaml](../compose.production.yaml) + [compose.staging.yaml](../compose.staging.yaml). Project завжди **`trade-system-staging`**. Compose isolation дає окремі named volumes/networks та labels `trade.environment=staging`.

Components: PostgreSQL, API, frontend, Caddy. **Frontend — static production build у Caddy image**, тому frontend/Caddy мають один runtime container; окремого Node server немає. Також є one-shot `migrate` та maintenance-profile `bootstrap-admin`.

- PostgreSQL persistent volume, internal database network, pg_isready health, `unless-stopped`, без host port.
- API non-root image, dropped capabilities/no-new-privileges, persistent Data Protection, DB-aware health, `unless-stopped`. Старт після успішного migrate.
- Caddy persistent cert/config volumes, automatic TLS, private proxy network, loopback health, `unless-stopped`. Старт після healthy API.
- Bootstrap тільки явно, password не передається API/migrate; container видаляється через `run --rm`.

Перед доставкою на VPS пакет поточного worktree можна створити **локально**, включно з незакоміченими sources і migrations:

```sh
python3 scripts/package-staging-release.py --output-dir /PRIVATE_PATH_OUTSIDE_REPOSITORY
```

Helper виключає `.git`, ignored build caches, private env/keys/dumps; додає `SOURCE-MANIFEST.json` із file hashes. Отриманий releaseId — fingerprint snapshot, **не Git HEAD**. Перевірити archive inventory перед передаванням. Release checksum sidecar не є digital signature; transport — через перевірений SSH.

Після появи фактичного SSH alias завчасно створити приватний upload directory на VPS через перевірений SSH session. З client:

```sh
scp /PRIVATE_PATH/trade-system-staging-RELEASE_ID.tar.gz /PRIVATE_PATH/trade-system-staging-RELEASE_ID.tar.gz.sha256 trade-staging:/PRIVATE_UPLOAD_DIRECTORY/
```

На VPS після Docker setup під deployment user підготувати directories. Це перший запуск на новому dedicated staging host; existing `current` не перезаписувати:

```sh
set -euo pipefail
STAGING_ROOT=/opt/trade-system-staging
sudo install -d -m 0755 "$STAGING_ROOT" "$STAGING_ROOT/releases"
sudo chown "$USER:$(id -gn)" "$STAGING_ROOT" "$STAGING_ROOT/releases"
install -d -m 700 "$STAGING_ROOT/shared"
# Set RELEASE_ID from the package report and UPLOAD_DIR to the real upload directory.
cd "$UPLOAD_DIR"
sha256sum -c "trade-system-staging-$RELEASE_ID.tar.gz.sha256"
releaseDir="$STAGING_ROOT/releases/$RELEASE_ID"
mkdir "$releaseDir"
tar -xzf "trade-system-staging-$RELEASE_ID.tar.gz" -C "$releaseDir" --strip-components=1 --no-same-owner
cd "$releaseDir"
```

Звірити manifest з directory розпакованого release перед створенням `current`:


```sh
python3 - <<'PY'
import hashlib, json
from pathlib import Path
manifest = json.loads(Path('SOURCE-MANIFEST.json').read_text())
for name, expected in manifest.items():
    assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == expected['sha256'], name
print('Release source manifest verified')
PY
```

Для першого запуску створити `current` через `ln -sT "$releaseDir" "$STAGING_ROOT/current"`; existing symlink вимагає update/rollback procedure. Створити приватний env за розділом Environment variables, потім **у поточному Bash session на VPS** визначити wrapper:

```sh
set -euo pipefail
STAGING_ROOT=/opt/trade-system-staging
STAGING_ENV="$STAGING_ROOT/shared/.env.staging"
cd "$STAGING_ROOT/current"
dc=(docker compose --env-file "$STAGING_ENV" -p trade-system-staging -f compose.production.yaml -f compose.staging.yaml)
"${dc[@]}" config --quiet
"${dc[@]}" build --pull api caddy
```

Source/build artifacts лишаються окремо від persistent DB/key volumes. API Dockerfile і frontend API routing залишені чинними. Build failure блокує наступні кроки. Не запускати deployment з outdated clone `main`, якщо він не містить потрібного worktree snapshot.

## Caddy

Configuration template — чинний [Caddyfile](../Caddyfile). Він сумісний з Linux Docker і використовує `CADDY_SITE_ADDRESS` з private staging env. Зберігає hardened headers, HSTS для public host, compression, `/api` prefix stripping, `/health`, SPA fallback, immutable hashed assets, private path denial і loopback-only admin/health listeners. Заголовок HTTPS scheme приймається API тільки від dedicated trusted proxy network.

На VPS перевірити build image config без запуску app:

```sh
"${dc[@]}" run --rm --no-deps --entrypoint caddy caddy validate --config /etc/caddy/Caddyfile
```

Automatic public TLS потребує правильного DNS, TCP 80/443 та outbound ACME access. Caddy data volume зберігає account/cert state. Email не є обов'язковим у поточному template. Після Start перевіряти normal TLS validation, без `curl -k`/`ignoreHTTPSErrors`.

## Database migrations

На **чистому staging**:

```sh
"${dc[@]}" up -d --wait postgres
"${dc[@]}" run --rm --no-deps migrate
"${dc[@]}" exec -T postgres psql -X -U trade_staging_owner -d trade_staging -v ON_ERROR_STOP=1 -c 'SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
```

Очікується **15/15** current release migration IDs. Зіставити список із `backend/Trade.Infrastructure/Persistence/Migrations/[0-9]*.cs` без `.Designer.cs`; count сам по собі недостатній. Повторний migrate має бути no-op. Не restore-ити local/test dump і не запускати Development seed. Normal API startup не змінює schema неявно.

Після migrations створити restricted runtime role і grants, використовуючи приватний env та staging-labelled container:

```sh
python3 scripts/provision-staging-runtime.py --env-file "$STAGING_ENV"
```

Helper працює тільки з `trade-system-staging` та `trade.environment=staging`, перевіряє owner/DB metadata, передає SQL/password через stdin без друку або argv. Existing runtime password не змінює; grants можна застосувати повторно після migrations. API працює через runtime role; maintenance owner лишається тільки migrate/backup.

## Admin bootstrap

Вже є production-safe явний CLI `--bootstrap-admin`: працює тільки Production/Staging, вимагає username/OrganizationId та password ≥16 з Identity validation, створює OWNER з чинними permissions, transaction/lock, відмовляється overwrite existing organization user/username. Нових auth flows не потрібно.

На VPS, після migrations/runtime grants, ввести окремий staging password через прихований terminal input:

```sh
read -r -s -p 'Unique staging admin password (16+ characters): ' BootstrapAdmin__Password
printf '\n'
export BootstrapAdmin__Password
if "${dc[@]}" --profile maintenance run --rm --no-deps bootstrap-admin; then
    unset BootstrapAdmin__Password
else
    unset BootstrapAdmin__Password
    echo 'Bootstrap failed; stop and review protected logs.'
    exit 1
fi
```

Для автоматизованої operator session допустимий ephemeral random secret через захищений secret injection; у коді/commit його немає. Normal startup/migrations його не викликають. Profile не стартує під час звичайного `up`, `run --rm` прибирає container, password unset і не зберігається у private env. На existing staging/restart bootstrap повторно не запускати. Не використовувати DevelopmentSeed або `Test123!Test`.

## Start

Після успішних pull/build/migrations/grants/bootstrap:

```sh
"${dc[@]}" up -d --no-build --wait
"${dc[@]}" ps
curl --fail --show-error "https://$STAGING_DOMAIN/health"
```

Має бути healthy PostgreSQL/API/Caddy; frontend перевіряється через `/login` і assets того самого Caddy. Міграційний job має exit 0. Bootstrap не має постійно працювати. Не змінювати bind/ports на DB/API для browser debugging.

## Health checks

`/health` перевіряє DB, 200 Healthy або 503 без sensitive details. API Docker probe використовує loopback Host/8080. Caddy health — внутрішній 8081, PostgreSQL — pg_isready. Docker restart policy реагує на exited process, не автоматично на unhealthy: оператор має перевіряти причину.

Перевірки після старту:

```sh
curl --fail --show-error "https://$STAGING_DOMAIN/health"
curl --fail --show-error -o /dev/null "https://$STAGING_DOMAIN/login"
curl --head "http://$STAGING_DOMAIN/health"
openssl s_client -connect "$STAGING_DOMAIN:443" -servername "$STAGING_DOMAIN" -verify_hostname "$STAGING_DOMAIN" -verify_return_error < /dev/null
```

HTTP очікує redirect на HTTPS, certificate chain/hostname мають пройти. ACME/HTTPS results наразі NOT RUN.

## Smoke test

Запускати лише на staging organization. Новий [scripts/staging-smoke.mjs](../scripts/staging-smoke.mjs) використовує **strict public TLS**, browser login/CSRF, звіряє expected OrganizationId, створює мінімальний dataset через існуючі HTTPS API та перевіряє UI. Не містить production/default password. До фактичного VPS протестовано тільки syntax; E2E execution НЕ заявляється PASS.

На захищеному Linux browser runner потрібні Chromium, Node 24 та Playwright (окремо від app dependencies, без додавання нового UI framework). `PLAYWRIGHT_MODULE_PATH` може вказувати на встановлений поза repo `playwright-core/index.mjs`; `CHROMIUM_PATH` — на browser binary. Staging VPS не зобов'язаний бути browser runner. Приклад setup на Linux runner з уже встановленими Node 24 і Chromium (package не додається до frontend dependencies):

```sh
browserDir="$HOME/.cache/trade-staging-browser"
npm install --prefix "$browserDir" --no-save playwright-core@1.64.1
export PLAYWRIGHT_MODULE_PATH="$browserDir/node_modules/playwright-core/index.mjs"
export CHROMIUM_PATH="$(command -v chromium)"
```

```sh
export STAGING_BASE_URL="https://$STAGING_DOMAIN"
export STAGING_ADMIN_USERNAME=staging-admin
export STAGING_EXPECTED_ORGANIZATION_ID=ACTUAL_GUID_FROM_PRIVATE_STAGING_ENV
export STAGING_ALLOW_FIXTURES=1
export STAGING_SMOKE_PHASE=seed
export STAGING_SMOKE_OUTPUT=/PRIVATE_PATH/staging-smoke
read -r -s -p 'Staging admin password: ' STAGING_ADMIN_PASSWORD
printf '\n'
export STAGING_ADMIN_PASSWORD
node scripts/staging-smoke.mjs
unset STAGING_ADMIN_PASSWORD
```

Coverage: products/counterparties, posted purchase/batch, direct expense, mixed GOODS/SERVICE sale, money receipt/settlements, customer return, supplier return, transfer/writeoff, reports, users/roles, audit; desktop Chromium та mobile **390×844 / 430×844**, навігація, відсутність overflow і 0 unexpected errors/warnings. Fixtures використовують нові назви/коди; production data не підтягуються. API mutations проходять через browser cookie session і Caddy. UI routes відкривають створені документи; це не твердження про ручне заповнення всіх форм у script.

Result JSON/screenshots — у private output; cookie state/fixture IDs — files 600. Не комітити або публікувати private session artifacts. Додатково вручну перевірити form interactions purchase/sale на staging, якщо потрібне підтвердження повного UI path. Expected anonymous `/auth/me` 401 фіксується окремо; інші browser errors/warnings блокують PASS.

## Restart test

Після seed smoke зберегти source values/document numbers і fingerprints існуючих Data Protection key files у захищеному каталозі, не їхній вміст. Не змінювати staging data паралельно з comparison. Volumes `trade-system-staging_postgres_data`, `trade-system-staging_data_protection`, `caddy_data`, `caddy_config` мають залишитися тими самими.

```sh
"${dc[@]}" restart postgres
"${dc[@]}" up -d --no-deps --no-build --wait postgres
"${dc[@]}" restart api
"${dc[@]}" up -d --no-deps --no-build --wait api
"${dc[@]}" restart caddy
"${dc[@]}" up -d --no-deps --no-build --wait caddy
export STAGING_SMOKE_PHASE=resume
node scripts/staging-smoke.mjs
```

Frontend і Caddy — той самий container, тому один restart покриває обидва. Resume використовує попередню auth cookie **без нового login**, перевіряє persisted documents/status/stock/money/settlement/BatchPrice та повторює desktop/mobile navigation. Потім перевірити fresh login в іншому browser session, DB health/logs і що initial key files не втратилися. Не видаляти volumes або генерувати новий `.env.staging`. Наразі restart resilience на VPS NOT RUN.

## Backup

Повний перевірений runbook: [backup-restore.md](backup-restore.md). Після staging E2E виконати новий backup саме staging DB. Приклад на VPS із staging wrapper:

```sh
umask 077
backupStamp=$(date -u +%Y%m%dT%H%M%SZ)
backupRemote="/tmp/staging-$backupStamp.dump"
backupFile="$STAGING_ROOT/shared/backups/staging-$backupStamp.dump"
install -d -m 700 "$STAGING_ROOT/shared/backups"
"${dc[@]}" exec -T postgres sh -eu -c 'umask 077; PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc --no-owner --no-acl --file="$1"' sh "$backupRemote"
"${dc[@]}" cp "postgres:$backupRemote" "$backupFile"
chmod 600 "$backupFile"
test -s "$backupFile"
sha256sum "$backupFile" > "$backupFile.sha256"
"${dc[@]}" exec -T postgres pg_restore --list "$backupRemote" > "$backupFile.toc"
"${dc[@]}" exec -T postgres rm -- "$backupRemote"
```

Виконувати з `set -euo pipefail` або явно перевіряти кожен exit. Backup містить sensitive Identity/audit data; потрібні restricted/encrypted storage та off-host policy. Restore поверх staging DB заборонений. За потреби перевірити цей archive в окремій temporary empty restore DB/volume за backup runbook. Попередній local backup/restore PASS не є результатом staging backup — останній наразі NOT RUN.

## Logs

Docker local logging уже має rotation. Зберігати diagnostic logs у private directory з mode 600, перевіряти startup/DB/TLS/auth failures. Не друкувати resolved env/config або request bodies/headers.

```sh
umask 077
"${dc[@]}" logs --no-color --tail 500 api caddy postgres > "$STAGING_ROOT/shared/staging-validation.log"
```

Локально на VPS/runner звірити log contents з фактичними secret values із private env та cookie values із private smoke state; виводити тільки labels/counts findings, не значення. Production error stack traces допустимі тільки у захищених server logs, не API responses. Passwords/connection-string secrets/cookies/CSRF values у logs блокують staging PASS. Key encryption-at-rest warning від Data Protection потребує host/backup storage protection; keys у volume не мають окремого application KEK.

## Security checks

З зовнішнього дозволеного client перевірити фактичний VPS:

- HTTPS chain/hostname/expiry і HTTP→HTTPS redirect; public DNS збігається з intended VPS.
- TCP 5432/8080/2019/8081 недоступні ззовні, лише 80/443 та restricted SSH. Provider firewall плюс Docker published-port inventory.
- Production Swagger/OpenAPI не публікуються: authenticated `/api/swagger/index.html`, `/api/openapi/v1.json` → 404; anonymous 401 також закритий доступ. SPA HTML fallback на довільному root path не є OpenAPI.
- HttpOnly/Secure/SameSite=Lax cookies, CSRF required for mutations, 401/403/409/validation/500 без client stack traces, same-origin `/api`, CORS не відкрито.
- No development endpoints/seed/default passwords, database runtime role не superuser, bootstrap profile/container/password не залишилися активними.
- `/.env`, `/.git`, `/backend` private paths закриті; source/env/keys не віддаються frontend.
- Різні staging/production credentials, organization, volumes, domain, Data Protection keys та backup destination.

Ці external checks наразі NOT RUN. Перехід до production launch checklist допустимий лише після всіх remote gates, не тільки Compose syntax.

## Rollback

Для першого staging deployment попереднього release ще немає: при failure зупинити app services, зберегти volumes/env/logs і діагностувати. Перед наступними updates зберегти backup, попередній source manifest і image tags; не overwritе shared secrets/OrganizationId.

Якщо попередній відомий release сумісний з поточною DB schema, переключити `current` та **лише** image tags на попередній snapshot, виконати `up --no-build --wait` і health/smoke. При schema incompatibility не робити автоматичний EF downgrade або restore поверх існуючої БД: recovery у новий target за backup runbook з окремим погодженим cutover. `.env` generator не є rollback tool. Реальний rollback/cutover у цьому preparation slice не виконувався.

## Cleanup

`"${dc[@]}" down` зупиняє staging і зберігає data/key/cert volumes. Видаляти test cookies/private fixtures з browser runner після перевірок. Не використовувати глобальний `docker system prune --volumes` і не видаляти чужі/production volumes. Повне стирання staging data — окреме свідоме рішення, не стандартний cleanup.

На цьому етапі **ніяких VPS containers/volumes/DNS records/admin accounts не створено**, outbound SSH не виконувався. Підготовка не дає статус STAGING PASS. Після створення VPS потрібні фактичні реквізити й виконання remote gates; до цього production launch checklist запускати рано.
