// Mutates ONLY an explicitly identified staging organization. Never use production credentials.
// Public HTTPS is required; TLS verification is never disabled here.
import assert from 'node:assert/strict'
import { chmod, mkdir, readFile, writeFile } from 'node:fs/promises'
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = new URL(process.env.STAGING_BASE_URL ?? '')
assert.equal(base.protocol, 'https:')
assert.ok(!base.port || base.port === '443', 'Use the public staging HTTPS domain')
assert.equal(base.pathname, '/')
const expectedOrg = process.env.STAGING_EXPECTED_ORGANIZATION_ID
assert.ok(expectedOrg, 'Specify the staging OrganizationId explicitly')
const phase = process.env.STAGING_SMOKE_PHASE ?? 'seed'
assert.ok(['seed', 'resume'].includes(phase))
if (phase === 'seed') assert.equal(process.env.STAGING_ALLOW_FIXTURES, '1', 'Explicitly allow test fixtures on staging')
const output = process.env.STAGING_SMOKE_OUTPUT ?? '/tmp/trade-staging-smoke'
await mkdir(output, { recursive: true, mode: 0o700 })
const origin = base.origin, checks = [], errors = [], warnings = [], expectedDiagnostics = [], visited = []
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium', args: ['--no-sandbox'] })
const saved = phase === 'resume' ? JSON.parse(await readFile(output + '/private-session.json', 'utf8')) : undefined
const context = await browser.newContext({ storageState: saved, viewport: { width: 1440, height: 1000 }, timezoneId: 'Europe/Kyiv', locale: 'uk-UA' })
const page = await context.newPage()
page.setDefaultTimeout(20000)
page.on('pageerror', error => errors.push(error.message))
page.on('console', message => {
  if (message.type() === 'warning') warnings.push(message.text())
  if (message.type() === 'error') {
    if (message.location().url.endsWith('/api/auth/me') && message.text().includes('401') && phase === 'seed') expectedDiagnostics.push('Anonymous /auth/me = 401 before login')
    else errors.push(message.text() + ' ' + message.location().url)
  }
})
page.on('requestfailed', request => { if (request.failure()?.errorText !== 'net::ERR_ABORTED') errors.push(request.url() + ': ' + request.failure()?.errorText) })
page.on('request', request => {
  if (['fetch', 'xhr'].includes(request.resourceType())) {
    const url = new URL(request.url())
    if (url.origin !== origin || !url.pathname.startsWith('/api/')) errors.push('Unexpected API target: ' + url.origin + url.pathname)
  }
})
function pass(check) { checks.push(check); console.log('PASS ' + check) }
async function request(path, data, method = data === undefined ? 'GET' : 'POST') {
  const headers = { Accept: 'application/json' }
  if (method !== 'GET') {
    const response = await context.request.get(origin + '/api/auth/csrf')
    assert.ok(response.ok(), 'CSRF endpoint unavailable')
    const csrf = await response.json(); headers[csrf.headerName] = csrf.requestToken
  }
  return context.request.fetch(origin + '/api' + path, { method, data, headers })
}
async function api(path, data, method) {
  const response = await request(path, data, method)
  assert.ok(response.ok(), `${path}: ${response.status()} ${await response.text()}`)
  return response.status() === 204 ? undefined : response.json()
}
async function posted(path, body) {
  const draft = await api(path, body)
  assert.equal(draft.document.number, null)
  const document = await api(`${path}/${draft.document.id}/post`, null)
  assert.equal(document.document.status, 'POSTED'); assert.ok(document.document.number)
  return document
}
async function visit(path, width) {
  await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 })
  await page.goto(origin + path); await page.waitForLoadState('networkidle')
  await page.getByRole('main').getByRole('heading', { level: 1 }).waitFor()
  assert.equal(await page.getByText('Не вдалося відкрити сторінку', { exact: true }).count(), 0)
  assert.equal(await page.getByText('Недостатньо прав', { exact: true }).count(), 0)
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Overflow: ' + path + ' at ' + width)
  if (path === '/') await page.screenshot({ path: `${output}/${phase}-dashboard-${width}.png`, animations: 'disabled' })
  visited.push({ path, width })
}
async function snapshot(ids) {
  const paths = [`/purchases/${ids.purchase.document.id}`, `/sales/${ids.sale.document.id}`, `/expenses/${ids.expense.document.id}`, `/customer-returns/${ids.customerReturn.document.id}`, `/supplier-returns/${ids.supplierReturn.document.id}`, `/stock-transfers/${ids.transfer.document.id}`, `/write-offs/${ids.writeoff.document.id}`, `/money/receipts/${ids.receipt.document.id}`, `/batches/${ids.batch.batchId}/prices/${ids.type.id}`]
  const result = {}
  for (const path of paths) result[path] = await api(path)
  result.stock = (await api('/stock/balances')).filter(row => row.batchId === ids.batch.batchId)
  result.money = (await api('/money/balances')).filter(row => row.moneyAccountId === ids.account.id)
  result.settlements = (await api('/settlements/balances')).filter(row => [ids.supplier.id, ids.customer.id].includes(row.counterpartyId))
  return result
}
function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical).sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)))
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])]))
  return value
}
try {
  const health = await context.request.get(origin + '/health')
  assert.equal(health.status(), 200); assert.equal((await health.json()).status, 'Healthy')
  const redirect = await context.request.get('http://' + base.hostname + '/health', { maxRedirects: 0 })
  assert.equal(redirect.status(), 308); assert.equal(new URL(redirect.headers().location).protocol, 'https:')
  const html = await context.request.get(origin + '/login')
  assert.equal(html.headers()['x-frame-options'], 'DENY'); assert.equal(html.headers()['x-content-type-options'], 'nosniff')
  assert.match(html.headers()['strict-transport-security'], /max-age=/)
  assert.match(html.headers()['content-security-policy'], /frame-ancestors 'none'/)
  pass('Publicly trusted TLS, redirect, health and security headers')
  if (phase === 'seed') {
    assert.ok(process.env.STAGING_ADMIN_USERNAME && process.env.STAGING_ADMIN_PASSWORD)
    const denied = await context.request.post(origin + '/api/auth/login', { data: { username: process.env.STAGING_ADMIN_USERNAME, password: process.env.STAGING_ADMIN_PASSWORD } })
    assert.equal(denied.status(), 400)
    await page.goto(origin + '/login')
    await page.getByLabel('Логін', { exact: true }).fill(process.env.STAGING_ADMIN_USERNAME)
    await page.getByLabel('Пароль', { exact: true }).fill(process.env.STAGING_ADMIN_PASSWORD)
    await page.getByRole('button', { name: 'Увійти', exact: true }).click()
    await page.getByRole('heading', { name: 'Огляд діяльності', exact: true }).waitFor()
  }
  const me = await api('/auth/me')
  assert.equal(me.organizationId.toLowerCase(), expectedOrg.toLowerCase())
  assert.ok(me.permissions.includes('users.manage') && me.permissions.includes('roles.manage'))
  for (const name of ['trade.auth', 'trade.csrf']) {
    const cookie = (await context.cookies()).find(value => value.name === name)
    assert.ok(cookie, name); assert.equal(cookie.httpOnly, true); assert.equal(cookie.secure, true); assert.equal(cookie.sameSite, 'Lax')
  }
  assert.equal((await request('/swagger/index.html')).status(), 404)
  assert.equal((await request('/openapi/v1.json')).status(), 404)
  pass(phase === 'resume' ? 'Existing auth cookie works after container restarts' : 'Browser login, staging organization, CSRF, secure cookies and no production Swagger')
  let ids, state
  if (phase === 'seed') {
    const run = Date.now().toString(36), date = new Date().toISOString().slice(0, 10), uah = '00000000-0000-0000-0000-000000000980'
    const unit = await api('/units', { name: `Staging unit ${run}` })
    const source = await api('/storage-locations', { name: `Staging source ${run}` }), destination = await api('/storage-locations', { name: `Staging destination ${run}` })
    const type = await api('/price-types', { name: `Staging retail ${run}`, defaultMarkupPercent: 25 })
    const supplier = await api('/counterparties', { name: `Staging supplier ${run}`, isSupplier: true }), customer = await api('/counterparties', { name: `Staging customer ${run}`, isCustomer: true })
    const goods = await api('/products', { unitId: unit.id, itemType: 'GOODS', code: `STAGE-G-${run}`, name: 'Staging test GOODS' })
    const service = await api('/products', { unitId: unit.id, itemType: 'SERVICE', code: `STAGE-S-${run}`, name: 'Staging test SERVICE' })
    await api(`/products/${service.id}/prices/${type.id}`, { unitPrice: 200 }, 'PUT')
    const purchase = await posted('/purchases', { documentType: 'PURCHASE_INVOICE', documentDate: date, counterpartyId: supplier.id, storageLocationId: source.id, currencyId: uah, exchangeRate: 1, discountPercent: 10, lines: [{ lineNo: 1, productId: goods.id, quantity: 20, unitPriceCurrency: 100 }] })
    const batch = (await api(`/purchases/${purchase.document.id}/batches`))[0]
    const category = await api('/expense-categories', { name: `Staging expense ${run}` })
    const expense = await posted('/expenses', { documentDate: date, counterpartyId: supplier.id, expenseCategoryId: category.id, currencyId: uah, amount: 40, exchangeRate: 1, purchaseInvoiceId: purchase.document.id })
    const sale = await posted('/sales', { documentDate: date, counterpartyId: customer.id, storageLocationId: source.id, priceTypeId: type.id, discountPercent: 0, lines: [{ lineNo: 1, productId: goods.id, batchId: batch.batchId, quantity: 4, unitPrice: 150 }, { lineNo: 2, productId: service.id, batchId: null, quantity: 1 }] })
    const account = await api('/money/accounts', { name: `Staging cash ${run}`, accountType: 'CASH', currencyId: uah })
    const receipt = await posted('/money/receipts', { documentDate: date, purpose: 'SETTLEMENT', counterpartyId: customer.id, destinationAccountId: account.id, destinationAmount: 500, settlementCurrencyId: uah, settlementAmount: 500, settlementExchangeRate: 1 })
    const customerReturn = await posted('/customer-returns', { documentDate: date, sourceSaleInvoiceId: sale.document.id, storageLocationId: source.id, lines: [{ sourceSaleLineId: sale.lines.find(line => line.productId === goods.id).id, quantity: 1 }] })
    const supplierReturn = await posted('/supplier-returns', { documentDate: date, sourcePurchaseInvoiceId: purchase.document.id, storageLocationId: source.id, lines: [{ sourcePurchaseLineId: purchase.lines[0].id, quantity: 2 }] })
    const transfer = await posted('/stock-transfers', { documentDate: date, fromLocationId: source.id, toLocationId: destination.id, lines: [{ lineNo: 1, batchId: batch.batchId, quantity: 3 }] })
    const reason = await api('/write-off-reasons', { name: `Staging reason ${run}`, expenseCategoryId: category.id })
    const writeoff = await posted('/write-offs', { documentDate: date, storageLocationId: destination.id, writeOffReasonId: reason.id, lines: [{ lineNo: 1, batchId: batch.batchId, quantity: 1 }] })
    ids = { unit, source, destination, type, supplier, customer, goods, service, purchase, batch, expense, sale, account, receipt, customerReturn, supplierReturn, transfer, writeoff }
    state = await snapshot(ids)
    assert.equal(state.stock.find(row => row.storageLocationId === source.id).physicalQuantity, 12)
    assert.equal(state.stock.find(row => row.storageLocationId === destination.id).physicalQuantity, 2)
    assert.equal(state.money[0].balance, 500)
    await writeFile(output + '/private-fixtures.json', JSON.stringify({ ids, state }, null, 2), { mode: 0o600 })
    pass('Posted purchase/direct expense/sale/money/customer return/supplier return/transfer/writeoff and stock results')
  } else {
    const fixture = JSON.parse(await readFile(output + '/private-fixtures.json', 'utf8')); ids = fixture.ids
    assert.deepEqual(canonical(await snapshot(ids)), canonical(fixture.state))
    pass('Document numbers, statuses, balances and BatchPrice persist after restarts')
  }
  for (const path of ['/dashboard/summary', '/reports/sales', '/reports/profitability', '/reports/stock', '/reports/purchases', '/reports/money', '/reports/expenses', '/reports/settlements', '/admin/users', '/admin/roles', '/admin/audit']) await api(path)
  pass('Dashboard, all reports, users/roles and audit API')
  for (const path of ['/', '/products', '/counterparties', `/purchases/${ids.purchase.document.id}`, `/sales/${ids.sale.document.id}`, '/stock', '/money', '/settlements', '/expenses', '/customer-returns', '/supplier-returns', '/stock/transfers', '/stock/write-offs', '/reports', '/admin/users', '/admin/roles', '/admin/audit']) await visit(path, 1440)
  for (const width of [390, 430]) {
    for (const path of ['/', '/sales', '/stock', '/money']) await visit(path, width)
    await page.getByRole('button', { name: 'Ще — відкрити решту меню', exact: true }).click()
    await page.getByRole('navigation', { name: 'Основна навігація', exact: true }).waitFor()
    await page.getByRole('button', { name: 'Закрити меню', exact: true }).click()
    pass(`Mobile ${width}x844 routes/navigation`)
  }
  await context.storageState({ path: output + '/private-session.json' }); await chmod(output + '/private-session.json', 0o600)
  assert.deepEqual(errors, []); assert.deepEqual(warnings, [])
} finally {
  await writeFile(`${output}/${phase}-result.json`, JSON.stringify({ phase, checks, visited, errors, warnings, expectedDiagnostics }, null, 2))
  await browser.close()
}
console.log(`${phase}: ${checks.length} smoke groups passed; ${visited.length} route visits; 0 unexpected errors/warnings`)
