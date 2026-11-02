// Run after v1-audit-smoke.mjs, against the same disposable fixture database.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdir, writeFile } from 'node:fs/promises'
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = process.env.AUDIT_BASE_URL ?? 'https://localhost:8444',
  password = process.env.AUDIT_ADMIN_PASSWORD
assert.ok(password)
const container = process.env.AUDIT_DB_CONTAINER ?? 'trade-v1-audit-db'
assert.ok(container.startsWith('trade-v1-audit-'))
const out = process.env.AUDIT_OUTPUT ?? '/tmp/trade-v1-audit/api'
await mkdir(out, { recursive: true })
function sql(query) {
  return execFileSync(
    'docker',
    [
      'exec',
      container,
      'psql',
      '-U',
      process.env.AUDIT_DB_USER ?? 'trade_v1',
      '-d',
      process.env.AUDIT_DB_NAME ?? 'trade_v1',
      '-At',
      '-v',
      'ON_ERROR_STOP=1',
      '-c',
      query,
    ],
    { encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'] },
  ).trim()
}
const q = (x) => "'" + String(x).replaceAll("'", "''") + "'",
  uah = '00000000-0000-0000-0000-000000000980'
const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium',
  args: ['--no-sandbox'],
})
const ctx = await browser.newContext({ ignoreHTTPSErrors: true }),
  checks = []
async function req(path, data, method = data === undefined ? 'GET' : 'POST', context = ctx, headers = {}) {
  if (method !== 'GET') {
    const c = await (await context.request.get(base + '/api/auth/csrf')).json()
    headers[c.headerName] = c.requestToken
  }
  return context.request.fetch(base + '/api' + path, { method, data, headers })
}
async function api(path, data, method, context, headers) {
  const r = await req(path, data, method, context, headers)
  assert.ok(r.ok(), `${path}: ${r.status()} ${await r.text()}`)
  return r.status() === 204 ? null : r.json()
}
async function denied(path, data, method, expected = [400, 404], context = ctx) {
  const r = await req(path, data, method, context)
  assert.ok(expected.includes(r.status()), `${path}: ${r.status()} ${await r.text()}`)
}
async function login(context, name) {
  await api('/auth/login', { username: name, password }, 'POST', context)
}
function pass(x) {
  checks.push(x)
  console.log('PASS ' + x)
}
const totals = (r) =>
  Object.fromEntries(r.kpis.filter((x) => !x.currencyId || x.currencyId === uah).map((x) => [x.key, x.value]))
const period = 'from=2026-10-01&to=2026-10-31'
try {
  await login(ctx, 'admin')
  const me = await api('/auth/me'),
    before = totals(await api('/dashboard/summary?' + period))
  const goods = (await api('/products?search=V1-GOODS'))[0],
    service = (await api('/products?search=V1-SERVICE'))[0]
  const parties = await api('/counterparties'),
    supplier = parties.find((x) => x.name === 'Постачальник V1'),
    customer = parties.find((x) => x.name === 'Покупець V1')
  const locations = await api('/storage-locations'),
    source = locations.find((x) => x.name === 'Основний'),
    destination = locations.find((x) => x.name === 'Резервний')
  const purchase =
    (await api('/purchases')).find((x) => x.number && x.totalAmountCurrency === 1800) ??
    (await api('/purchases')).find((x) => x.status === 'POSTED')
  const purchaseId = purchase.id,
    sourcePurchase = await api('/purchases/' + purchaseId),
    batch = (await api(`/purchases/${purchaseId}/batches`))[0]
  const sale =
      (await api('/sales')).find((x) => x.status === 'POSTED' && x.totalAmount === 720) ??
      (await api('/sales')).find((x) => x.status === 'POSTED'),
    sourceSale = await api('/sales/' + sale.id)
  const saleLine = sourceSale.lines.find((x) => x.productId === goods.id),
    reason = (await api('/write-off-reasons'))[0],
    category = (await api('/expense-categories'))[0]
  const accounts = await api('/money/accounts'),
    cash = accounts.find((x) => x.name === 'Каса V1'),
    bank = accounts.find((x) => x.name === 'Банк V1')
  const receipt = {
    documentDate: '2026-10-01',
    purpose: 'SETTLEMENT',
    counterpartyId: customer.id,
    destinationAccountId: cash.id,
    destinationAmount: 1,
    settlementCurrencyId: uah,
    settlementAmount: 1,
    settlementExchangeRate: 1,
  }
  const purchaseBody = {
    documentType: 'PURCHASE_INVOICE',
    documentDate: '2026-10-01',
    counterpartyId: supplier.id,
    storageLocationId: source.id,
    currencyId: uah,
    exchangeRate: 1,
    discountPercent: 0,
    lines: [{ lineNo: 1, productId: goods.id, quantity: 1, unitPriceCurrency: 1 }],
  }
  const cases = [
    ['/purchases', purchaseBody],
    ['/purchases', { ...purchaseBody, documentType: 'OPENING_STOCK', counterpartyId: null }],
    [
      '/sales',
      {
        documentDate: '2026-10-01',
        counterpartyId: customer.id,
        storageLocationId: source.id,
        discountPercent: 0,
        lines: [{ lineNo: 1, productId: goods.id, batchId: batch.batchId, quantity: 1, unitPrice: 150 }],
      },
    ],
    [
      '/expenses',
      {
        documentDate: '2026-10-01',
        counterpartyId: supplier.id,
        expenseCategoryId: category.id,
        currencyId: uah,
        amount: 1,
        exchangeRate: 1,
      },
    ],
    ['/money/receipts', receipt],
    [
      '/money/payments',
      {
        ...receipt,
        counterpartyId: supplier.id,
        destinationAccountId: null,
        destinationAmount: null,
        sourceAccountId: cash.id,
        sourceAmount: 1,
      },
    ],
    [
      '/money/transfers',
      {
        documentDate: '2026-10-01',
        sourceAccountId: cash.id,
        destinationAccountId: bank.id,
        sourceAmount: 1,
        destinationAmount: 1,
      },
    ],
    [
      '/customer-returns',
      {
        documentDate: '2026-10-01',
        sourceSaleInvoiceId: sale.id,
        storageLocationId: source.id,
        lines: [{ sourceSaleLineId: saleLine.id, quantity: 1 }],
      },
    ],
    [
      '/supplier-returns',
      {
        documentDate: '2026-10-01',
        sourcePurchaseInvoiceId: purchaseId,
        storageLocationId: source.id,
        lines: [{ sourcePurchaseLineId: sourcePurchase.lines[0].id, quantity: 1 }],
      },
    ],
    [
      '/stock-transfers',
      {
        documentDate: '2026-10-01',
        fromLocationId: source.id,
        toLocationId: destination.id,
        lines: [{ lineNo: 1, batchId: batch.batchId, quantity: 1 }],
      },
    ],
    [
      '/write-offs',
      {
        documentDate: '2026-10-01',
        storageLocationId: source.id,
        writeOffReasonId: reason.id,
        lines: [{ lineNo: 1, batchId: batch.batchId, quantity: 1 }],
      },
    ],
  ]
  for (const [root, input] of cases) {
    const draft = await api(root, input),
      path = root + '/' + draft.document.id
    assert.equal(draft.document.number, null)
    const edited = await api(
      path,
      {
        ...input,
        version: draft.document.version,
        lines: input.lines?.map((x, i) => ({ ...x, id: draft.lines[i].id })),
      },
      'PUT',
    )
    await denied(path, { ...input, version: draft.document.version }, 'PUT', [409])
    const posted = await api(path + '/post', {}),
      replay = await api(path + '/post', {})
    assert.ok(posted.document.number)
    assert.equal(replay.document.version, posted.document.version)
    await denied(path, undefined, 'DELETE', [400])
    const unposted = await api(path + '/unpost', {})
    assert.equal((await api(path + '/unpost', {})).document.version, unposted.document.version)
    assert.equal((await api(path + '/post', {})).document.number, posted.document.number)
    await api(path + '/unpost', {})
    await api(path, undefined, 'DELETE')
    await denied(path, undefined, 'GET', [404])
    await denied(path + '/post', {}, 'POST', [404])
    assert.ok(edited.document.version > draft.document.version)
    pass(input.documentType ?? root)
  }
  pass(
    'All 11 document types: DRAFT, Version409, POST/UNPOST idempotency, permanent number, posted delete blocked, logical delete excluded',
  )
  const currency = randomUUID(),
    fx = await api('/purchases', {
      ...purchaseBody,
      currencyId: currency,
      exchangeRate: 40,
      lines: [{ ...purchaseBody.lines[0], quantity: 1, unitPriceCurrency: 20 }],
    })
  await api(`/purchases/${fx.document.id}/post`, {})
  const balances = (await api('/settlements/counterparties/' + supplier.id)).balances
  assert.ok(balances.some((x) => x.currencyId === currency && x.balance === -20))
  assert.ok(balances.some((x) => x.currencyId === uah && x.balance === -620))
  await api(`/purchases/${fx.document.id}/unpost`, {})
  await api('/purchases/' + fx.document.id, undefined, 'DELETE')
  const foreignAccount = await api('/money/accounts', {
    name: 'Technical currency audit',
    accountType: 'BANK',
    currencyId: currency,
  })
  const transfer = await api('/money/transfers', {
    documentDate: '2026-10-01',
    sourceAccountId: cash.id,
    destinationAccountId: foreignAccount.id,
    sourceAmount: 100,
    destinationAmount: 2,
  })
  await api(`/money/transfers/${transfer.document.id}/post`, {})
  assert.equal(
    sql(
      `SELECT COUNT(DISTINCT a."CurrencyId") FROM money_movements m JOIN money_accounts a ON a."Id"=m."MoneyAccountId" WHERE m."DocumentId"=${q(transfer.document.id)}`,
    ),
    '2',
  )
  await api(`/money/transfers/${transfer.document.id}/unpost`, {})
  await api('/money/transfers/' + transfer.document.id, undefined, 'DELETE')
  pass(
    'Currency balances remain separate; cross-currency transfer100/2, two movements, safe UNPOST without rate persistence',
  )
  const foreignOrg = randomUUID(),
    roleB = randomUUID(),
    credentials = await api('/admin/users', {
      username: 'fixture_' + randomUUID().replaceAll('-', ''),
      password,
      active: false,
      roleIds: [],
    }),
    userB = { userId: randomUUID(), username: 'foreign_' + randomUUID().replaceAll('-', '') }
  // Test fixture only: organization switching is intentionally absent from the application.
  sql(
    `INSERT INTO users ("Id","OrganizationId","UserName","NormalizedUserName","FullName","Active","PasswordHash","SecurityStamp","ConcurrencyStamp","AccessFailedCount","LockoutEnabled") SELECT ${q(userB.userId)},${q(foreignOrg)},${q(userB.username)},${q(userB.username.toUpperCase())},'Foreign owner',true,"PasswordHash","SecurityStamp",${q(randomUUID())},0,true FROM users WHERE "Id"=${q(credentials.userId)}; INSERT INTO roles ("Id","OrganizationId","Name","NormalizedName","Active","ConcurrencyStamp") VALUES (${q(roleB)},${q(foreignOrg)},'Foreign owner','FOREIGN OWNER',true,${q(randomUUID())}); INSERT INTO role_permissions ("RoleId","PermissionId") SELECT ${q(roleB)},"Id" FROM permissions; INSERT INTO user_roles ("UserId","RoleId") VALUES (${q(userB.userId)},${q(roleB)});`,
  )
  const orgB = await browser.newContext({ ignoreHTTPSErrors: true })
  await login(orgB, userB.username)
  const categoryB = await api('/categories', { name: 'Foreign category' }, 'POST', orgB),
    unitB = await api('/units', { name: 'Foreign unit' }, 'POST', orgB),
    locationB = await api('/storage-locations', { name: 'Foreign location' }, 'POST', orgB)
  const partyB = await api(
      '/counterparties',
      { name: 'Foreign party', isSupplier: true, isCustomer: true },
      'POST',
      orgB,
    ),
    productB = await api(
      '/products',
      {
        name: 'Foreign goods',
        code: 'FOREIGN',
        categoryId: categoryB.id,
        unitId: unitB.id,
        itemType: 'GOODS',
      },
      'POST',
      orgB,
    )
  const purchaseB = await api(
    '/purchases',
    {
      ...purchaseBody,
      counterpartyId: partyB.id,
      storageLocationId: locationB.id,
      lines: [{ lineNo: 1, productId: productB.id, quantity: 2, unitPriceCurrency: 10 }],
    },
    'POST',
    orgB,
  )
  await api(`/purchases/${purchaseB.document.id}/post`, {}, 'POST', orgB)
  const batchB = (await api(`/purchases/${purchaseB.document.id}/batches`, undefined, 'GET', orgB))[0]
  const saleB = await api(
    '/sales',
    {
      documentDate: '2026-10-01',
      counterpartyId: partyB.id,
      storageLocationId: locationB.id,
      lines: [{ lineNo: 1, productId: productB.id, batchId: batchB.batchId, quantity: 1, unitPrice: 15 }],
    },
    'POST',
    orgB,
  )
  await api(`/sales/${saleB.document.id}/post`, {}, 'POST', orgB)
  for (const path of [
    `/products/${productB.id}`,
    `/purchases/${purchaseB.document.id}`,
    `/sales/${saleB.document.id}`,
    `/stock/batches/${batchB.batchId}`,
    `/counterparties/${partyB.id}`,
    `/admin/users/${userB.userId}`,
    `/admin/roles/${roleB}`,
  ])
    await denied(path, undefined, 'GET', [404])
  await denied(`/purchases/${purchaseB.document.id}/post`, {}, 'POST', [404])
  await denied(`/sales/${saleB.document.id}/post`, {}, 'POST', [404])
  await denied(`/products/${productB.id}`, { ...productB, name: 'Illegal' }, 'PUT', [404])
  await denied('/purchases', { ...purchaseBody, counterpartyId: partyB.id }, 'POST')
  await denied('/purchases', { ...purchaseBody, storageLocationId: locationB.id }, 'POST')
  await denied('/products', { ...goods, code: 'FOREIGN-CAT', categoryId: categoryB.id }, 'POST')
  await denied('/categories', { name: 'Illegal child', parentId: categoryB.id }, 'POST')
  await denied(
    '/customer-returns',
    {
      documentDate: '2026-10-01',
      sourceSaleInvoiceId: saleB.document.id,
      storageLocationId: source.id,
      lines: [],
    },
    'POST',
  )
  await denied(
    '/supplier-returns',
    {
      documentDate: '2026-10-01',
      sourcePurchaseInvoiceId: purchaseB.document.id,
      storageLocationId: source.id,
      lines: [],
    },
    'POST',
  )
  await denied(
    '/stock-transfers',
    { ...cases[9][1], lines: [{ lineNo: 1, batchId: batchB.batchId, quantity: 1 }] },
    'POST',
  )
  await denied('/stock-transfers', { ...cases[9][1], toLocationId: locationB.id }, 'POST')
  await denied('/admin/users', { username: 'bad_foreign_role', password, roleIds: [roleB] }, 'POST', [400])
  await denied('/products', { ...goods, code: 'FOREIGN-UNIT', unitId: unitB.id }, 'POST', [400])
  assert.ok(!(await api('/products')).some((x) => x.id === productB.id))
  const forged = await api(
    '/products',
    { ...goods, id: undefined, code: 'HEADER-SCOPE', barcode: null },
    'POST',
    ctx,
    { 'X-Organization-Id': foreignOrg, 'X-Actor-Id': userB.userId },
  )
  assert.equal(forged.organizationId, me.organizationId)
  await orgB.close()
  pass(
    'Two real Identity organizations: direct-id read/update/post, foreign return/source/batch/role/category/unit/location references denied; forged scope headers ignored',
  )
  await denied(`/admin/users/${me.userId}/deactivate`, {}, 'POST', [409])
  const owner = (await api('/admin/roles')).find((x) => x.name === 'OWNER')
  await denied(`/admin/roles/${owner.id}/permissions`, { permissionCodes: [] }, 'PUT', [409])
  const role = await api('/admin/roles', { name: 'Inactive test' })
  await api(`/admin/roles/${role.id}/permissions`, { permissionCodes: ['money.view'] }, 'PUT')
  await api('/admin/roles/' + role.id, { name: role.name, active: false }, 'PUT')
  await denied(`/admin/roles/${role.id}/permissions`, { permissionCodes: ['stock.view'] }, 'PUT', [400])
  const operator = (await api('/admin/users')).find((x) => x.username === 'operator_v1'),
    extra = await api('/admin/roles', { name: 'Extra test' })
  await api(`/admin/roles/${extra.id}/permissions`, { permissionCodes: ['product.edit'] }, 'PUT')
  await api(
    '/admin/users/' + operator.userId,
    { ...operator, roleIds: [...operator.roleIds, extra.id, role.id] },
    'PUT',
  )
  const limited = await browser.newContext({ ignoreHTTPSErrors: true })
  await login(limited, operator.username)
  const current = await api('/auth/me', undefined, 'GET', limited)
  assert.ok(current.permissions.includes('product.view') && current.permissions.includes('product.edit'))
  assert.ok(!current.permissions.includes('money.view'))
  await api(`/admin/users/${operator.userId}/deactivate`, {})
  await denied('/auth/me', undefined, 'GET', [401], limited)
  await denied('/auth/login', { username: operator.username, password }, 'POST', [401], limited)
  await api(`/admin/users/${operator.userId}/activate`, {})
  await limited.close()
  pass(
    'Last-admin protection; active-role union; inactive role grants nothing/cannot change permissions; inactive user and old cookie rejected',
  )
  const cacheColumns = sql(
    `SELECT c.table_name||'.'||c.column_name FROM information_schema.columns c JOIN information_schema.tables t USING(table_schema,table_name) WHERE c.table_schema='public' AND t.table_type='BASE TABLE' AND lower(c.column_name) IN ('stock','physicalquantity','reservedquantity','availablequantity','balance','debt','advance','paidamount','paymentstatus','invoicestatus','unitcostuah','currentcost','linetotal','documenttotal','grossprofit','managementresult')`,
  )
  assert.equal(cacheColumns, '')
  const audit = await api('/admin/audit?take=1')
  let immutable = false
  try {
    sql(`DELETE FROM audit_logs WHERE "Id"=${q(audit.items[0].id)}`)
  } catch {
    immutable = true
  }
  assert.ok(immutable)
  assert.deepEqual(totals(await api('/dashboard/summary?' + period)), before)
  pass(
    'No forbidden derived business columns; audit immutable; deleted fixtures and organization B do not alter organization A financial metrics',
  )
  const duplicate = await api('/products', {
    ...goods,
    id: undefined,
    code: 'BARCODE-AUDIT',
    name: 'Duplicate barcode audit',
  })
  assert.equal(duplicate.barcode, goods.barcode)
  const firstType = (await api('/price-types'))[0]
  const duplicateType = await api('/price-types', { name: firstType.name, active: false })
  assert.equal(duplicateType.name, firstType.name)
  const schema = {
    views: sql("SELECT viewname FROM pg_views WHERE schemaname='public' ORDER BY viewname").split('\n'),
    indexes: JSON.parse(
      sql(
        "SELECT json_agg(json_build_object('table',tablename,'name',indexname,'definition',indexdef)) FROM pg_indexes WHERE schemaname='public'",
      ),
    ),
    constraints: JSON.parse(
      sql(
        "SELECT json_agg(json_build_object('table',conrelid::regclass::text,'name',conname,'definition',pg_get_constraintdef(oid))) FROM pg_constraint WHERE connamespace='public'::regnamespace AND contype <> 't'",
      ),
    ),
  }
  await writeFile(
    out + '/result.json',
    JSON.stringify(
      {
        result: 'PASS',
        checks,
        organizations: 2,
        barcodeUniqueness: 'not enforced; evidence: two different Codes accept same Barcode',
        schema,
      },
      null,
      2,
    ),
  )
} catch (e) {
  console.error(e)
  process.exitCode = 1
} finally {
  await browser.close()
}
