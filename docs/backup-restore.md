# PostgreSQL Backup / Restore

## Результат drill

**BACKUP/RESTORE PASS — 2026-10-31.** Реального deployment або restore у робочу БД не виконували. Backend, frontend, Dockerfile, domain model, бізнес-правила та migrations не змінювалися. Додаткових business tables не створювали: тестова схема отримана виключно з поточних V1 migrations.

| Перевірка | Результат |
| --- | --- |
| PostgreSQL / tools | Server **18.6**, `pg_dump 18.6`, `pg_restore 18.6`, офіційний `postgres:18-alpine` |
| Format | Custom archive, `-Fc`, schema + data + sequences + constraints + indexes |
| Artifact | **158 261 bytes** (154.6 KiB), header `PGDMP`, mode 600 у приватному каталозі mode 700 |
| SHA-256 | `3791422a3bf6c4c989e9a3246779dc68cc4680e9a7c1e938fcffbb048e08e617` |
| `pg_restore --list` | Exit 0, 296 TOC entries; TABLE DATA, SEQUENCE SET, constraints/indexes присутні |
| Full restore | Exit 0, нова порожня БД на **іншому container/volume**, інший owner/runtime role |
| Data | Counts і logical row fingerprints усіх **37 tables** збіглися до початку functional writes |
| Schema | 323 constraints, 103 indexes, 32 non-internal triggers, columns/functions/views збіглися; constraints validated |
| Migrations | **15/15** записів `__EFMigrationsHistory`, точний збіг із поточними migration IDs у repo |
| Audit | **29 original records**, IDs та logical fingerprint збережені, включно після нового документа |
| Functional | Production backend проти restored DB: login та **30 API responses** збігаються із source, включно з dashboard і всіма 7 reports |
| Sequence | `document_numbers`: last value **5 → 6**, створено/POSTED **PI-00000006** та новий Batch, duplicate key немає |

Artifact залишено **поза Git**: `/tmp/trade-backup-restore-drill-20261031/trade-v1-drill-20261031.dump`. Це тимчасовий локальний файл із тестовими даними; `/tmp` не є production backup storage. Він містить Identity password hashes та audit/business data, тому також потребує захисту. Декодування archive у SQL пройшло без помилок; plaintext test admin/DB passwords та TLS private key у ньому не знайдені. Зовнішні env/config, TLS certificate/key і Data Protection keys не входять у dump.

## PostgreSQL setup та ізоляція

Робочий контейнер **`trade-system-postgres-1`** інспектувався тільки через Docker metadata, без SQL connections: офіційний `postgres:18-alpine`, configured DB/user `trade`/`trade`, volume `trade-system_postgres_data` → `/var/lib/postgresql`, PGDATA `/var/lib/postgresql/18/docker`, port `127.0.0.1:5432`. Contents, додаткові ролі або extensions робочої БД не читалися. Container ID, StartedAt, image, mounts і port config залишилися незмінними; health — healthy.

Drill використовував два свіжі volumes та окрему network `trade-backup-drill-20261031`:

| | Source | Restore |
| --- | --- | --- |
| Container / volume | `trade-backup-drill-20261031-source` | `trade-backup-drill-20261031-restored` |
| Database | `trade_drill_source` | `trade_drill_restored` |
| Owner | `drill_source_owner` | `drill_restored_owner` |
| Runtime role | `drill_source_app` | `drill_restored_app` |
| DB port, тільки loopback | 55461 | 55462 |
| Temporary native API HTTPS port | 18661 | 18662 |

Обидва server — 18.6, UTF8, application schema `public`, search path `"$user", public`; extension тільки builtin `plpgsql 1.0` у `pg_catalog`. Config/HBA у `/var/lib/postgresql/18/docker`. Runtime roles — LOGIN без SUPERUSER/CREATEDB/CREATEROLE/REPLICATION; grants застосовано через `scripts/grant-runtime-role.sql`. Official PostgreSQL image bootstrap owner — superuser, він використаний тільки для maintenance. У restore cluster **немає source PostgreSQL roles**: archive відновлено з `--no-owner --no-privileges`, після чого grants створено заново. Application Identity `users/roles` — таблиці всередині БД, вони збережені.

Passwords згенеровано випадково у приватних `/tmp` файлах; у repository їх не записували. Source API використовував Development тільки для явного seed username **`admin`**; password передано через `DevelopmentSeed__AdminPassword`, organization — окремий GUID. Після міграцій seed password не передавався довгоживучому API. Restored API запущено з **Production**, без development seed/bootstrap. Для HTTPS використано тимчасовий localhost certificate і різні Data Protection directories; успішний login підтверджує відновлення Identity hashes, не перенесення cookie session.

## Тестові дані та integrity comparison

Через існуючі API створено category, unit, PriceType з markup 25%, location, supplier/customer, GOODS і SERVICE з ProductPrice. Purchase: 10 × 100 UAH, header discount 10%, POST. Batch створив існуючий purchase handler. Sale: 3 GOODS за рекомендованою 112.50 та 1 SERVICE за 200, header discount 5%, POST. Operating expense 50, money receipt 200 від customer та payment 100 supplier також POSTED.

До backup source API зупинено, щоб зафіксувати стабільний baseline для порівняння. `pg_dump` сам використовує consistent snapshot; зупинка API не є загальною вимогою online backup.

| Critical table / data | Source = restored, до functional writes |
| --- | ---: |
| users / roles / user_roles | 1 / 7 / 1 |
| permissions / role_permissions | 34 / 68 |
| products / counterparties | 2 / 2 |
| documents | 5 POSTED |
| purchase_invoices / purchase_invoice_lines / batches | 1 / 1 / 1 |
| sale_invoices / sale_invoice_lines | 1 / 2, GOODS + SERVICE |
| expenses / money_documents | 1 / 2 |
| stock_movements / money_movements / settlement_movements | 2 / 2 / 5 |
| batch_prices / product_prices | 1 / 1 |
| audit_logs | 29 |

Збіглися numbers/IDs/status/dates усіх вихідних документів, sequence definition і last_value/is_called, constraints/indexes/triggers, повні logical fingerprints усіх таблиць. Порівнювали logical data, не фізичні database files. Database name, owner, OIDs/storage/statistics можуть відрізнятися.

Збіглися всі п'ять наявних views: `v_stock_balance`, `v_money_account_balance`, `v_counterparty_balance`, `v_batch_cost`, `v_write_off_cost` (останній порожній для цього dataset). Основні контрольні значення:

| Показник | Source = restored |
| --- | ---: |
| Stock physical / reserved / available | 7 / 0 / 7 |
| Money balance, UAH | 100 |
| Customer settlement, UAH | +310.625 |
| Supplier settlement, UAH | -850 |
| Batch UnitCostUah / BatchPrice | 90 / 112.50 |
| Net sales / COGS / gross profit, UAH | 510.625 / 270 / 240.625 |
| Operating expenses / management result, UAH | 50 / 190.625 |

Functional API smoke охопив `/auth/login`, `/auth/me`, products/directories/counterparties, purchase/sale/expense list та details, stock, money accounts/documents/balances, settlements і counterparty ledger, dashboard, усі reports: sales, profitability, stock, purchases, money, expenses, settlements. JSON порівнювали канонічно, без вимоги порядку незалежних rows. Production cookies працювали через локальний HTTPS. Users/roles/audit API доступні. Це backend/API smoke; браузерний UI чи повний Docker API stack у цьому drill не перевірявся.

Business IDs у поточній моделі UUID, GENERATED AS IDENTITY columns немає; єдиний sequence — `document_numbers`. Після integrity comparison і functional smoke створено окремий новий purchase на restored DB. DRAFT Number=null; після POST Number=`PI-00000006`, sequence=6, новий Batch, duplicate number/key відсутній. Додаткові writes після baseline є навмисною частиною sequence test. Початкові 29 audit records повторно звірено за IDs та fingerprint — не змінилися.

## Backup command

Використовуйте офіційний PostgreSQL client відповідної version (у drill 18.6). Не робіть `tar` активного PGDATA замість logical backup. `pg_dump -Fc` включає schema/data/sequence values/constraints/indexes; у цьому сценарії ownership/ACL навмисно переносимо окремою процедурою. PostgreSQL globals і passwords database roles не входять у database dump; Identity data всередині БД входять.

Нижче **операторський приклад для production**, під час drill він не запускався проти production. Потрібні реальні приватні env, maintenance access та захищений storage. Секрет читається тільки всередині контейнера; не передавайте password у command-line connection URI.

Bash, з кореня repo:

```sh
set -euo pipefail
umask 077
backupDir=/secure/trade-backups
mkdir -p "$backupDir"
backupStamp=$(date -u +%Y%m%dT%H%M%SZ)
backupRemote="/tmp/trade-$backupStamp.dump"
backupFile="$backupDir/trade-$backupStamp.dump"

docker compose --env-file .env.production -f compose.production.yaml exec -T postgres sh -eu -c 'umask 077; PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc --no-owner --no-acl --file="$1"' sh "$backupRemote"
docker compose --env-file .env.production -f compose.production.yaml cp "postgres:$backupRemote" "$backupFile"
chmod 600 "$backupFile"
test -s "$backupFile"
sha256sum "$backupFile" > "$backupFile.sha256"
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres pg_restore --list "$backupRemote" > "$backupFile.toc"
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres rm -- "$backupRemote"
```

Кожен failed exit припиняє процедуру; не позначайте partial dump як успішний backup. Синхронізуйте maintenance password у container environment під час ротації. Не видаляйте попередній успішний backup до перевірки нового. SHA-256 допомагає виявляти пошкодження; для захисту від підміни потрібне автентифіковане encrypted storage.

**Windows PowerShell:** не пропускайте binary `-Fc` stream через `>` у старому Windows PowerShell. Використовуйте `pg_dump --file` усередині container та `docker compose cp`, як вище. Наприклад:

```powershell
$backupDir = 'D:\SecureBackups\trade'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$backupStamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$backupRemote = "/tmp/trade-$backupStamp.dump"
$backupFile = Join-Path $backupDir "trade-$backupStamp.dump"
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres sh -eu -c 'umask 077; PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -h 127.0.0.1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc --no-owner --no-acl --file="$1"' sh $backupRemote
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed' }
docker compose --env-file .env.production -f compose.production.yaml cp "postgres:$backupRemote" $backupFile
if ($LASTEXITCODE -ne 0) { throw 'Copy failed' }
if ((Get-Item $backupFile).Length -le 0) { throw 'Empty backup' }
Get-FileHash -Algorithm SHA256 $backupFile
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres pg_restore --list $backupRemote
if ($LASTEXITCODE -ne 0) { throw 'Archive list failed' }
docker compose --env-file .env.production -f compose.production.yaml exec -T postgres rm -- $backupRemote
```

Для Windows налаштуйте NTFS ACL/шифрування backup directory окремо. Перевірте точне передавання аргументів `sh -c` у вашій версії PowerShell. Production збереження, encryption і off-host copy у цьому drill не налаштовувалися.

## Restore command — тільки новий isolated target

Для перевірки backup створіть окремий PostgreSQL 18 container/volume та нову empty DB, з окремими тестовими secrets у приватній `.env.restore-test` (не production credentials). Підготуйте `.env.restore-test` з `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB=postgres`; password випадковий, не example/default. API Docker image для цього PostgreSQL етапу не потрібний.

Приклад для disposable restore target; names мають бути вільні. Ніколи не підставляйте working container/volume:

```sh
set -euo pipefail
docker volume create trade-restore-check-data
docker run -d --name trade-restore-check --env-file .env.restore-test -p 127.0.0.1:55463:5432 -v trade-restore-check-data:/var/lib/postgresql postgres:18-alpine
# Wait until this isolated server reports accepting connections:
docker exec trade-restore-check sh -eu -c 'pg_isready -U "$POSTGRES_USER" -d postgres'
docker exec trade-restore-check sh -eu -c 'PGPASSWORD="$POSTGRES_PASSWORD" createdb -h 127.0.0.1 -U "$POSTGRES_USER" --template=template0 --owner="$POSTGRES_USER" trade_restore_check'
docker cp "$backupFile" trade-restore-check:/tmp/trade.dump
docker exec trade-restore-check chmod 600 /tmp/trade.dump
docker exec trade-restore-check pg_restore --list /tmp/trade.dump
# Must print 0 application tables before restore:
docker exec trade-restore-check sh -eu -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d trade_restore_check -X -v ON_ERROR_STOP=1 -Atc "SELECT count(*) FROM pg_tables WHERE schemaname = '\''public'\'';"'
docker exec trade-restore-check sh -eu -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -h 127.0.0.1 -U "$POSTGRES_USER" -d trade_restore_check --exit-on-error --single-transaction --no-owner --no-privileges /tmp/trade.dump'
docker exec trade-restore-check sh -eu -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d trade_restore_check -X -v ON_ERROR_STOP=1 -c ANALYZE'
```

Блок restore — Bash; для PowerShell передавайте SQL через текстовий файл/stdin або еквівалентне перевірене quoting. Не повторюйте restore на target після failed partial attempt без перевірки; `--single-transaction --exit-on-error` дозволяє rollback на помилці. `--no-owner --no-privileges` вимагають повторного provisioning owner/runtime grants. Застосуйте `scripts/grant-runtime-role.sql` з фактичними `runtime_role`/`migration_role`; passwords задайте окремо через secret management/`psql \password`, не SQL у Git.

## Verify commands

1. Перевірте size/checksum перед transfer і після нього; `pg_restore --list` — лише перша перевірка. **Повний restore та functional smoke обов'язкові** для підтвердження recoverability.
2. Запустіть `psql -X -v ON_ERROR_STOP=1` лише проти isolated source snapshot/restore DB. Read-only SQL для критичних даних:

```sql
SELECT version(), current_database(), current_user;
SELECT extname, extversion FROM pg_extension ORDER BY extname;
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT 'products' AS object, count(*) FROM products
UNION ALL SELECT 'users', count(*) FROM users
UNION ALL SELECT 'roles', count(*) FROM roles
UNION ALL SELECT 'counterparties', count(*) FROM counterparties
UNION ALL SELECT 'purchase_invoices', count(*) FROM purchase_invoices
UNION ALL SELECT 'batches', count(*) FROM batches
UNION ALL SELECT 'sale_invoices', count(*) FROM sale_invoices
UNION ALL SELECT 'expenses', count(*) FROM expenses
UNION ALL SELECT 'stock_movements', count(*) FROM stock_movements
UNION ALL SELECT 'money_movements', count(*) FROM money_movements
UNION ALL SELECT 'settlement_movements', count(*) FROM settlement_movements
UNION ALL SELECT 'audit_logs', count(*) FROM audit_logs;
SELECT "Id", "DocumentType", "Number", "Status" FROM documents ORDER BY "Id";
SELECT * FROM v_stock_balance ORDER BY "BatchId", "StorageLocationId";
SELECT * FROM v_money_account_balance ORDER BY "MoneyAccountId";
SELECT * FROM v_counterparty_balance ORDER BY "CounterpartyId", "CurrencyId";
SELECT * FROM v_batch_cost ORDER BY "BatchId";
SELECT * FROM batch_prices ORDER BY "BatchId", "PriceTypeId";
SELECT last_value, is_called FROM document_numbers;
SELECT schemaname, sequencename, increment_by, cache_size FROM pg_sequences WHERE schemaname='public';
SELECT conname FROM pg_constraint WHERE connamespace='public'::regnamespace AND NOT convalidated;
SELECT tablename, indexname, indexdef FROM pg_indexes WHERE schemaname='public' ORDER BY tablename,indexname;
SELECT "OrganizationId", "DocumentType", "Number", count(*) FROM documents
WHERE "Number" IS NOT NULL GROUP BY "OrganizationId", "DocumentType", "Number" HAVING count(*)>1;
```

Збережіть цей блок у приватний `verify.sql`; приклад виконання для test restore container, де owner/local access уже налаштовано:

```sh
docker exec -i trade-restore-check sh -eu -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -U "$POSTGRES_USER" -d trade_restore_check -X -v ON_ERROR_STOP=1' < verify.sql > restore-verification.txt
```

Для всіх таблиць можна виконати read-only `psql` meta-command генерацію без створення helper tables:

```sql
SELECT format('SELECT %L AS table_name, count(*) AS rows, md5(COALESCE(string_agg(row_to_json(t)::text, chr(10) ORDER BY row_to_json(t)::text), '''')) AS logical_fingerprint FROM public.%I t;', tablename, tablename)
FROM pg_tables WHERE schemaname='public' ORDER BY tablename
\gexec
```

Fingerprints потрібні для logical comparison невеликого стабільного dataset; це не security signature і не оптимальний full-scan для великої production DB. Не порівнюйте live source, який уже змінився після snapshot, з точкою backup як тотожні стани. До першого login/writes restored users data мають збігатися; login закономірно змінює LastLoginAt, нові документи — audit/sequence/balances.

3. Перевірте migrations history проти release migration IDs. Не запускайте Development seed/bootstrap на restored Production DB. Provision runtime grants, налаштуйте приватний connection string і запустіть поточний backend з `ASPNETCORE_ENVIRONMENT=Production` та HTTPS. Перевірте `/health`, свіжий login з відновленим Identity user, dashboard, products, purchases, sales, stock, money, settlements і всі reports з однаковими filters/permissions. Порівняйте report totals та snapshot prices із baseline; не перераховуйте/перезаписуйте persisted BatchPrice.
4. На **disposable restore copy** створіть/POST новий документ; перевірте number sequence і відсутність duplicate keys. Не викликайте `nextval` до контрольного POST: це змінює sequence. Не робіть `setval(...,1)` і не перенумеровуйте документи. Ніколи не створюйте тестові бізнес-документи у production заради цього drill.
5. Переконайтеся, що original audit IDs/records збережені та append-only triggers відновлені. Тестовий POST має додавати події, не переписувати старі.

## Порядок для production

1. Визначити owner, backup operator, RPO/RTO, window, retention та destination. Перевірити disk capacity і version compatibility. Врахувати, що logical `pg_dump` не забезпечує point-in-time recovery без окремого WAL/physical-backup рішення; у цьому slice його не додаємо.
2. Виконати consistent custom-format dump через maintenance role, перевірити exit/size/checksum/TOC, зашифрувати й доставити off-host з повторною перевіркою. Source application може працювати під час `pg_dump`; для точного source-vs-restored comparison потрібен стабільний baseline/snapshot або погоджене quiescence window.
3. Регулярно перевіряти recovery в окремій empty DB/volume: archive restore з error stop, roles/grants із окремого захищеного provisioning, ANALYZE, schema/migration/data/sequence/audit comparison та functional smoke. При різних owner names використовувати перевірені `--no-owner --no-privileges` і grants.
4. Зовнішні secrets/config, PostgreSQL role provisioning, Data Protection keys та TLS/Caddy state мають власну захищену recovery policy. Не додавати їх до database dump. Нові Data Protection keys означають повторний login; відновлені Identity password hashes перевіряються незалежно від cookie keys.
5. Реальне recovery/cutover — окреме погоджене завдання: control writes, підтвердити target, зберегти recovery point поточної системи, виконати перевірки та отримати deployment approval. Цей drill не переключав трафік і не відновлював production.

## Зберігання та заборони

Backup зберігати **поза repository**, на encrypted storage з обмеженим доступом та off-host copy. Linux: приватний каталог 700, files 600; Windows: NTFS ACL плюс encryption. Визначити retention, monitoring failed jobs, checksum verification та періодичний restore drill. Автоматизація, storage credentials, encryption keys, off-host delivery, retention і великі production RPO/RTO у цьому етапі не налаштовувалися.

Не можна:

- restore у source/working DB або використовувати `--clean`, `dropdb`, `down -v` щодо потрібного середовища;
- копіювати активний PGDATA як заміну перевіреному backup;
- комітити dump, private env, credentials, TLS/Data Protection keys чи unredacted SQL data;
- запускати production з Development seed або bootstrap-ити вже відновлених користувачів;
- вважати backup успішним тільки за наявністю файлу/TOC; потрібні повний restore та перевірки;
- скидати sequences, змінювати бізнес-дані/ціни чи вимикати audit/constraints, щоб приховати restore mismatch.

## Cleanup і readiness

Після перевірок зупинено native test APIs і видалено **тільки** два containers, network та volumes з prefix `trade-backup-drill-20261031`. Test source/restore DB більше не потрібні; artifact та non-secret evidence збережені у приватному `/tmp` каталозі, поза Git. Тимчасові plaintext secrets/certificate keys/Data Protection directories видалено. Робочий PostgreSQL залишився healthy з незмінними ID/StartedAt/mounts.

**Ручна logical backup/restore procedure перевірена й придатна як production runbook.** Перед production потрібні operational storage/encryption/retention/off-host/RPO/RTO налаштування. Для великого обсягу даних час потрібно виміряти окремо: цей маленький dataset не є production performance benchmark. Загальний production-readiness лишається **NOT READY** через раніше підтверджений зовнішній Docker API image network/proxy gate; backup drill цей blocker не обходить.
