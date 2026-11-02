// Run only against an isolated production-like audit stack, never production data.
// Credentials are supplied through the operator environment; no fixture password is built in.
import assert from 'node:assert/strict'
import { chmod, mkdir, writeFile } from 'node:fs/promises'
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = process.env.AUDIT_BASE_URL ?? 'https://localhost:18443'
const username = process.env.AUDIT_ADMIN_USERNAME, password = process.env.AUDIT_ADMIN_PASSWORD
assert.ok(username && password, 'Supply AUDIT_ADMIN_USERNAME and AUDIT_ADMIN_PASSWORD')
const output = process.env.AUDIT_OUTPUT ?? '/tmp/trade-production-audit/browser'
await mkdir(output, { recursive: true, mode: 0o700 })
const checks = [], errors = [], warnings = [], expectedDiagnostics = [], visited = []
const run = Date.now().toString(36)
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium', args: ['--no-sandbox'] })
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 }, timezoneId: 'Europe/Kyiv', locale: 'uk-UA' })
const page = await context.newPage()
page.setDefaultTimeout(20000)
page.on('pageerror', error => errors.push(error.message))
page.on('console', message => {
  if (message.type() === 'warning') warnings.push(message.text())
  if (message.type() === 'error') {
    if (message.location().url.endsWith('/api/auth/me') && message.text().includes('401')) expectedDiagnostics.push('Anonymous /auth/me = 401')
    else errors.push(message.text() + ' ' + message.location().url)
  }
})
page.on('requestfailed', request => { if (request.failure()?.errorText !== 'net::ERR_ABORTED') errors.push(request.url() + ': ' + request.failure()?.errorText) })
page.on('request', request => {
  if (['fetch', 'xhr'].includes(request.resourceType())) {
    const url = new URL(request.url())
    if (url.origin !== new URL(base).origin || !url.pathname.startsWith('/api/')) errors.push('Unexpected API origin/path: ' + url.origin + url.pathname)
  }
})
function pass(check) { checks.push(check); console.log('PASS ' + check) }
async function request(path, data, method = data === undefined ? 'GET' : 'POST', ctx = context) {
  const headers = { Accept: 'application/json' }
  if (method !== 'GET') {
    const csrf = await (await ctx.request.get(base + '/api/auth/csrf')).json()
    headers[csrf.headerName] = csrf.requestToken
  }
  return ctx.request.fetch(base + '/api' + path, { method, data, headers })
}
async function api(path, data, method) {
  const response = await request(path, data, method)
  assert.ok(response.ok(), `${path}: ${response.status()} ${await response.text()}`)
  return response.status() === 204 ? undefined : response.json()
}
async function visit(path, screenshot) {
  await page.goto(base + path)
  await page.waitForLoadState('networkidle')
  await page.getByRole('main').getByRole('heading', { level: 1 }).waitFor()
  assert.equal(await page.getByText('Не вдалося відкрити сторінку', { exact: true }).count(), 0)
  assert.equal(await page.getByText('Недостатньо прав', { exact: true }).count(), 0)
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false)
  if (screenshot) await page.screenshot({ path: `${output}/${screenshot}.png`, animations: 'disabled' })
  visited.push(path)
}
try {
  const health = await context.request.get(base + '/health')
  assert.equal(health.status(), 200)
  assert.equal((await health.json()).status, 'Healthy')
  assert.match(health.headers()['cache-control'], /no-store/)
  const html = await context.request.get(base + '/login')
  const headers = html.headers()
  assert.equal(headers['x-content-type-options'], 'nosniff')
  assert.equal(headers['x-frame-options'], 'DENY')
  assert.match(headers['content-security-policy'], /frame-ancestors 'none'/)
  assert.equal(headers.server, undefined)
  assert.match(headers['cache-control'], /no-cache/)
  const source = await html.text(), asset = source.match(/src="([^"]+\.js)"/)[1]
  const javascript = await context.request.get(base + asset, { headers: { 'Accept-Encoding': 'gzip' } })
  assert.match(javascript.headers()['cache-control'], /immutable/)
  assert.match(javascript.headers()['content-encoding'], /gzip/)
  const redirect = await context.request.get((process.env.AUDIT_HTTP_BASE_URL ?? base.replace('https:', 'http:').replace(':18443', ':18480')) + '/health', { maxRedirects: 0 })
  assert.equal(redirect.status(), 308)
  assert.match(redirect.headers().location, /^https:\/\//)
  for (const path of ['/.env', '/.env.production', '/.git/config', '/backend/appsettings.json']) assert.equal((await context.request.get(base + path)).status(), 404)
  pass('HTTPS, DB health, security headers and private-file denial')
  const noCsrf = await context.request.post(base + '/api/auth/login', { data: { username, password } })
  assert.equal(noCsrf.status(), 400)
  assert.equal((await context.request.get(base + '/api/auth/me')).status(), 401)
  pass('Anonymous requests and missing CSRF are rejected')
  await page.goto(base + '/login')
  await page.getByLabel('Логін', { exact: true }).fill(username)
  await page.getByLabel('Пароль', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Увійти', exact: true }).click()
  await page.getByRole('heading', { name: 'Огляд діяльності', exact: true }).waitFor()
  const cookies = await context.cookies()
  for (const name of ['trade.auth', 'trade.csrf']) {
    const cookie = cookies.find(value => value.name === name)
    assert.ok(cookie, name); assert.equal(cookie.httpOnly, true); assert.equal(cookie.secure, true); assert.equal(cookie.sameSite, 'Lax')
  }
  const me = await api('/auth/me')
  assert.equal(me.username, username)
  assert.ok(me.permissions.includes('users.manage'))
  assert.equal((await context.request.get(base + '/api/swagger/index.html')).status(), 404)
  assert.equal((await context.request.get(base + '/api/openapi/v1.json')).status(), 404)
  pass('Real production bootstrap login, HttpOnly/Secure cookies, trusted HTTPS proxy and no public Swagger')
  const unit = await api('/units', { name: `Audit unit ${run}` })
  const location = await api('/storage-locations', { name: `Audit location ${run}` })
  const priceType = await api('/price-types', { name: `Audit retail ${run}`, defaultMarkupPercent: 20 })
  const party = await api('/counterparties', { name: `Audit counterparty ${run}`, isCustomer: true, isSupplier: true })
  const product = await api('/products', { unitId: unit.id, itemType: 'GOODS', code: `AUDIT-PRODUCTION-${run}`, name: 'Audit product', active: true })
  const date = new Date().toISOString().slice(0, 10), uah = '00000000-0000-0000-0000-000000000980'
  let purchase = await api('/purchases', { documentType: 'PURCHASE_INVOICE', documentDate: date, counterpartyId: party.id, storageLocationId: location.id, currencyId: uah, exchangeRate: 1, discountPercent: 0, lines: [{ lineNo: 1, productId: product.id, quantity: 3, unitPriceCurrency: 100 }] })
  purchase = await api(`/purchases/${purchase.document.id}/post`, null)
  const batches = await api(`/stock/available-batches?productId=${product.id}&storageLocationId=${location.id}`)
  assert.equal(batches.length, 1)
  let sale = await api('/sales', { documentDate: date, counterpartyId: party.id, storageLocationId: location.id, priceTypeId: priceType.id, discountPercent: 0, lines: [{ lineNo: 1, productId: product.id, batchId: batches[0].batchId, quantity: 1 }] })
  assert.equal(sale.lines[0].recommendedUnitPrice, 120)
  sale = await api(`/sales/${sale.document.id}/post`, null)
  const balances = await api('/stock/balances')
  assert.equal(balances.find(row => row.batchId === batches[0].batchId && row.storageLocationId === location.id).physicalQuantity, 2)
  pass('Existing purchase/sale/stock rules work through the production HTTPS API with a restricted DB user')
  for (const path of ['/', '/purchases', `/purchases/${purchase.document.id}`, '/sales', `/sales/${sale.document.id}`, '/stock', '/money', '/reports', '/reports/profitability', '/admin', '/admin/users', '/admin/roles', '/admin/audit']) await visit(path, path === '/' ? 'desktop-dashboard' : undefined)
  pass('Desktop dashboard, purchase, sale, stock, money, reports and admin routes including SPA reload')
  await page.setViewportSize({ width: 390, height: 844 })
  for (const path of ['/', '/purchases', '/sales', '/stock', '/money', '/reports', '/admin']) await visit(path, path === '/' ? 'mobile-dashboard' : undefined)
  pass('Mobile production pages without horizontal overflow')
  await page.goto(base + '/')
  await page.waitForLoadState('networkidle')
  const storage = await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) }))
  assert.equal(storage.local.some(key => /token|password|csrf|auth/i.test(key)), false)
  assert.equal(storage.session.some(key => /token|password|csrf|auth/i.test(key)), false)
  // Save browser state privately for the separate key-persistence restart check.
  await context.storageState({ path: output + '/private-session.json' })
  await chmod(output + '/private-session.json', 0o600)
  pass('No auth secrets in Web Storage; cookie state saved privately for the restart check')
  assert.deepEqual(errors, []); assert.deepEqual(warnings, [])
} finally {
  await writeFile(output + '/result.json', JSON.stringify({ checks, visited, errors, warnings, expectedDiagnostics }, null, 2))
  await browser.close()
}
console.log(`${checks.length} production smoke groups passed; ${visited.length} routes visited; 0 unexpected errors/warnings`)
