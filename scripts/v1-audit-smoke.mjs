// Destructive fixtures ONLY in a disposable audit database. Never point this at working data.
// npm install --prefix /tmp/trade-v1-browser playwright-core
// Supply AUDIT_ADMIN_PASSWORD and PLAYWRIGHT_MODULE_PATH=/tmp/trade-v1-browser/node_modules/playwright-core/index.mjs.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdir, writeFile } from 'node:fs/promises'

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = process.env.AUDIT_BASE_URL ?? 'https://localhost:8444'
const container = process.env.AUDIT_DB_CONTAINER ?? 'trade-v1-audit-db'
assert.ok(container.startsWith('trade-v1-audit-'), 'Only disposable audit containers are allowed')
const dbUser = process.env.AUDIT_DB_USER ?? 'trade_v1',
  dbName = process.env.AUDIT_DB_NAME ?? 'trade_v1'
const password = process.env.AUDIT_ADMIN_PASSWORD
assert.ok(password, 'AUDIT_ADMIN_PASSWORD is required; no built-in application password')
const out = process.env.AUDIT_OUTPUT ?? '/tmp/trade-v1-audit/artifacts'
await mkdir(out, { recursive: true })
const uah = '00000000-0000-0000-0000-000000000980',
  period = 'from=2026-10-01&to=2026-10-31'
const checks = [],
  errors = [],
  expectedDiagnostics = [],
  knownBrowserDiagnostics = [],
  warnings = [],
  failedRequests = [],
  ids = {}
function sql(query) {
  return execFileSync(
    'docker',
    ['exec', container, 'psql', '-U', dbUser, '-d', dbName, '-At', '-v', 'ON_ERROR_STOP=1', '-c', query],
    { encoding: 'utf8' },
  ).trim()
}
const q = (text) => "'" + String(text).replaceAll("'", "''") + "'"
const count = (table, id) => Number(sql(`SELECT count(*) FROM ${table} WHERE "DocumentId"=${q(id)}`))
const sum = (table, column, id) =>
  Number(sql(`SELECT COALESCE(SUM("${column}"),0) FROM ${table} WHERE "DocumentId"=${q(id)}`))
function passed(name) {
  checks.push(name)
  console.log('PASS ' + name)
}
assert.equal(
  sql('SELECT count(*) FROM products'),
  '0',
  'Fresh-install scenario requires an empty business database',
)
const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium',
  args: ['--no-sandbox'],
})
const context = await browser.newContext({
  ignoreHTTPSErrors: true,
  timezoneId: 'Europe/Kyiv',
  viewport: { width: 1440, height: 1050 },
  acceptDownloads: true,
})
const page = await context.newPage()
page.setDefaultTimeout(20_000)
page.on('pageerror', (e) => errors.push(e.message))
page.on('console', (m) => {
  if (m.type() === 'warning') warnings.push(m.text())
  if (m.type() === 'error') {
    if (
      m.text().includes('400') &&
      /available-batches|returnable-lines|operation-batches/.test(m.location().url)
    )
      knownBrowserDiagnostics.push({ text: m.text(), url: m.location().url })
    else if (m.location().url.includes('/auth/me') && m.text().includes('401'))
      expectedDiagnostics.push(m.text())
    else errors.push(m.text())
  }
})
page.on('requestfailed', (r) => {
  if (r.failure()?.errorText !== 'net::ERR_ABORTED')
    failedRequests.push(r.url() + ': ' + r.failure()?.errorText)
})
async function request(path, data, method = data === undefined ? 'GET' : 'POST', ctx = context) {
  let headers = {}
  if (!['GET', 'HEAD'].includes(method)) {
    const c = await (await ctx.request.get(base + '/api/auth/csrf')).json()
    headers[c.headerName] = c.requestToken
  }
  return ctx.request.fetch(base + '/api' + path, { method, data, headers })
}
async function api(path, data, method, ctx) {
  const r = await request(path, data, method, ctx)
  assert.ok(
    r.ok(),
    `${method ?? (data === undefined ? 'GET' : 'POST')} ${path}: ${r.status()} ${await r.text()}`,
  )
  return r.status() === 204 ? undefined : r.json()
}
async function rejected(path, data, method, status, code, ctx) {
  const r = await request(path, data, method, ctx)
  assert.equal(r.status(), status, path + ': ' + (await r.text()))
  if (code) assert.equal((await r.json()).code, code)
  return r
}
async function choose(label, option) {
  await page.getByRole('combobox', { name: new RegExp('^' + label) }).click()
  await page.getByRole('option', { name: option, exact: typeof option === 'string' }).click()
}
async function fill(label, value, role = 'textbox') {
  await page.getByRole(role, { name: new RegExp('^' + label + '$') }).fill(String(value))
}
async function confirm(action) {
  await page.getByRole('button', { name: action, exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: action, exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
}
async function shot(name) {
  await page.screenshot({ path: out + '/' + name + '.png', fullPage: false, animations: 'disabled' })
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth > innerWidth),
    false,
    'Horizontal overflow: ' + name,
  )
}
async function postUi(path) {
  if (path) await page.goto(base + path)
  await confirm('Провести')
  await page.getByRole('button', { name: 'Розпровести', exact: true }).waitFor()
}
async function login(p, username, pw = password) {
  await p.goto(base + '/login')
  await p.getByLabel('Логін', { exact: true }).fill(username)
  await p.getByLabel('Пароль', { exact: true }).fill(pw)
  await p.getByRole('button', { name: 'Увійти', exact: true }).click()
  await p.getByRole('heading', { name: 'Огляд діяльності', exact: true }).waitFor()
}
async function directory(kind, name, parent) {
  await page.goto(base + '/directories/' + kind)
  await page
    .getByRole('button', { name: parent ? 'Додати підкатегорію ' + parent : 'Додати', exact: true })
    .click()
  await fill('Назва', name)
  if (kind === 'write-off-reasons') await choose('Категорія витрати', 'Операційні V1')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  return (await api('/' + kind)).find((x) => x.name === name)
}
async function product(name, code, service = false) {
  await page.goto(base + '/products/new')
  if (service) await choose('Тип', 'Послуга')
  await fill('Назва', name)
  await fill('Код', code)
  await choose('Одиниця виміру', 'штука')
  await choose('Категорія', 'Техніка / Кавове')
  if (!service) await fill('Штрихкод', '482000000001')
  else await page.getByRole('spinbutton', { name: 'Початкова ціна · Роздрібна', exact: true }).fill('200')
  await page.getByRole('button', { name: 'Створити', exact: true }).click()
  await page.getByRole('heading', { name, exact: true }).waitFor()
  return (await api('/products?search=' + code))[0]
}
async function party(name, role) {
  await page.goto(base + '/counterparties/new')
  await fill('Назва', name)
  await page.getByRole('checkbox', { name: role, exact: true }).check()
  await page.getByRole('button', { name: 'Створити контрагента', exact: true }).click()
  await page.getByRole('heading', { name, exact: true }).waitFor()
  return (await api('/counterparties?search=' + encodeURIComponent(name)))[0]
}
const metric = (d, key) => d.kpis.find((m) => m.key === key)?.value
let goods,
  service,
  supplier,
  customer,
  carrier,
  source,
  destination,
  type,
  purchase,
  batch,
  sale,
  directId,
  generalId,
  expenseCategory,
  reason,
  account,
  bank
const purchaseBody = (quantity = 20, price = 100) => ({
  documentType: 'PURCHASE_INVOICE',
  documentDate: '2026-10-20',
  counterpartyId: supplier.id,
  storageLocationId: source.id,
  currencyId: uah,
  exchangeRate: 1,
  discountPercent: 10,
  lines: [{ lineNo: 1, productId: goods.id, quantity, unitPriceCurrency: price }],
})
const saleBody = (quantity) => ({
  documentDate: '2026-10-22',
  counterpartyId: customer.id,
  storageLocationId: source.id,
  priceTypeId: type.id,
  discountPercent: 0,
  lines: [{ lineNo: 1, productId: goods.id, batchId: batch.batchId, quantity, unitPrice: 150 }],
})
async function stock(location = source.id) {
  return (
    (await api(`/stock/balances?batchId=${batch.batchId}&storageLocationId=${location}`))[0] ?? {
      physicalQuantity: 0,
      reservedQuantity: 0,
      availableQuantity: 0,
    }
  )
}
async function expectStock(location, physical, reserved) {
  const s = await stock(location)
  assert.deepEqual(
    [s.physicalQuantity, s.reservedQuantity, s.availableQuantity],
    [physical, reserved, physical - reserved],
  )
}
const cost = () => Number(sql(`SELECT "UnitCostUah" FROM v_batch_cost WHERE "BatchId"=${q(batch.batchId)}`))
const price = async () => (await api(`/batches/${batch.batchId}/prices/${type.id}`)).unitPrice
async function accountUi(name, isBank) {
  await page.goto(base + '/money/accounts')
  await page.getByRole('button', { name: 'Додати рахунок', exact: true }).click()
  await fill('Назва рахунку', name)
  if (isBank) await choose('Тип рахунку', 'Банк (BANK)')
  await page.getByRole('button', { name: 'Зберегти рахунок', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  return (await api('/money/accounts')).find((x) => x.name === name)
}
async function moneyUi(kind, amount, counterparty, purpose = 'SETTLEMENT') {
  await page.goto(base + `/money/${kind}/new`)
  await choose(kind === 'receipts' ? 'Рахунок зарахування' : 'Рахунок списання', account.name + ' · UAH')
  if (purpose !== 'SETTLEMENT') await choose('Призначення', 'Операція власника')
  await fill(kind === 'receipts' ? 'Сума зарахування' : 'Сума списання', amount, 'spinbutton')
  if (counterparty) {
    await choose('Контрагент', counterparty.name)
    await fill('Сума взаєморозрахунку', amount, 'spinbutton')
  }
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-23')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.waitForURL(new RegExp(`/money/${kind}/[0-9a-f-]{36}$`))
  const id = new URL(page.url()).pathname.split('/').at(-1)
  await postUi()
  return id
}

try {
  await login(page, 'admin')
  const me = await api('/auth/me')
  ids.organizationId = me.organizationId
  const cookie = (await context.cookies()).find((c) => c.name === 'trade.auth')
  assert.ok(cookie?.secure && cookie.httpOnly)
  assert.equal(cookie.sameSite, 'Lax')
  passed('Production HTTPS + Identity login, Secure HttpOnly SameSite cookie')
  const root = await directory('categories', 'Техніка')
  await directory('categories', 'Кавове', root.name)
  await directory('units', 'штука')
  source = await directory('storage-locations', 'Основний')
  destination = await directory('storage-locations', 'Резервний')
  await page.goto(base + '/directories/price-types')
  await page.getByRole('button', { name: 'Додати базові типи', exact: true }).click()
  await page.getByRole('button', { name: 'Редагувати Роздрібна', exact: true }).click()
  await fill('Націнка за замовчуванням, %', 25, 'spinbutton')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  type = (await api('/price-types')).find((x) => x.name === 'Роздрібна')
  supplier = await party('Постачальник V1', 'Постачальник')
  customer = await party('Покупець V1', 'Покупець')
  carrier = await party('Перевізник V1', 'Постачальник')
  goods = await product('Кавомашина V1', 'V1-GOODS')
  service = await product('Монтаж V1', 'V1-SERVICE', true)
  passed(
    'UI: category tree, units, locations, PriceTypes, supplier/customer, GOODS/SERVICE; initial SERVICE price through Product UI',
  )
  await page.goto(base + '/purchases/new')
  await choose('Постачальник', supplier.name)
  await choose('Склад', source.name)
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-20')
  await page.getByRole('button', { name: 'Додати товар', exact: true }).click()
  await page.getByRole('combobox', { name: 'Товар 1', exact: true }).fill(goods.code)
  await page.getByRole('option', { name: new RegExp(goods.name) }).click()
  await fill('Кількість 1', 20, 'spinbutton')
  await fill('Ціна 1', 100, 'spinbutton')
  await fill('Знижка, %', 10, 'spinbutton')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка закупівлі', exact: true }).waitFor()
  const purchaseId = new URL(page.url()).pathname.split('/').at(-1)
  ids.purchaseId = purchaseId
  assert.deepEqual(await api(`/purchases/${purchaseId}/batches`), [])
  assert.equal(count('stock_movements', purchaseId), 0)
  assert.equal(count('settlement_movements', purchaseId), 0)
  await postUi()
  purchase = await api('/purchases/' + purchaseId)
  batch = (await api(`/purchases/${purchaseId}/batches`))[0]
  ids.batchId = batch.batchId
  assert.ok(purchase.document.number)
  assert.equal(sum('stock_movements', 'Quantity', purchaseId), 20)
  assert.equal(sum('settlement_movements', 'Amount', purchaseId), -1800)
  assert.equal(cost(), 90)
  assert.equal(await price(), 112.5)
  const initialPrices = await api(`/purchases/${purchaseId}/batch-prices`)
  assert.equal(initialPrices.length, 2)
  await confirm('Розпровести')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).waitFor()
  assert.equal(count('stock_movements', purchaseId), 0)
  assert.equal(count('settlement_movements', purchaseId), 0)
  assert.deepEqual(await api(`/purchases/${purchaseId}/batch-prices`), initialPrices)
  const alternate = await api('/products', {
    ...goods,
    id: undefined,
    organizationId: undefined,
    name: 'Заміна для перевірки',
    code: 'V1-ALT',
    barcode: null,
  })
  const draft = await api('/purchases/' + purchaseId)
  await rejected(
    '/purchases/' + purchaseId,
    {
      ...draft,
      ...purchaseBody(),
      version: draft.document.version,
      lines: [
        { id: draft.lines[0].id, lineNo: 1, productId: alternate.id, quantity: 20, unitPriceCurrency: 100 },
      ],
    },
    'PUT',
    400,
  )
  await postUi()
  assert.equal((await api(`/purchases/${purchaseId}/batches`))[0].batchId, batch.batchId)
  assert.equal((await api('/purchases/' + purchaseId)).document.number, purchase.document.number)
  await api(`/purchases/${purchaseId}/post`, {})
  assert.equal(count('stock_movements', purchaseId), 1)
  assert.equal(count('settlement_movements', purchaseId), 1)
  await rejected(
    '/purchases',
    { ...purchaseBody(), lines: [{ lineNo: 1, productId: service.id, quantity: 1, unitPriceCurrency: 10 }] },
    'POST',
    400,
  )
  passed(
    'UI purchase DRAFT/POST/UNPOST/repost: permanent number, same Batch/prices; immutable ProductId, no SERVICE purchase, idempotency',
  )
  await page.goto(base + '/expense-categories')
  await page.getByRole('button', { name: 'Додати категорію', exact: true }).click()
  await page.getByRole('textbox', { name: 'Назва категорії', exact: true }).fill('Операційні V1')
  await page.getByRole('button', { name: 'Зберегти категорію', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  expenseCategory = (await api('/expense-categories')).find((x) => x.name === 'Операційні V1')
  reason = await directory('write-off-reasons', 'Пошкодження V1')
  await page.goto(base + '/purchases/' + purchaseId)
  await page.getByRole('tab', { name: 'Закупівельні витрати', exact: true }).click()
  await page.getByRole('link', { name: '+ Додати витрату', exact: true }).click()
  await choose('Контрагент', carrier.name)
  await choose('Категорія витрати', expenseCategory.name)
  await fill('Сума витрати', 200, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-21')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка витрати', exact: true }).waitFor()
  directId = new URL(page.url()).pathname.split('/').at(-1)
  ids.directExpenseId = directId
  await rejected(`/purchases/${purchaseId}/unpost`, {}, 'POST', 409, 'DOCUMENT_HAS_DEPENDENCIES')
  await postUi()
  assert.equal(cost(), 100)
  assert.equal(await price(), 112.5)
  assert.equal(sum('settlement_movements', 'Amount', directId), -200)
  assert.equal(count('money_movements', directId), 0)
  passed(
    'UI direct Expense prefill; DRAFT dependency safety; POST cost 90 → 100, BatchPrice 112.5 unchanged, no MoneyMovement',
  )
  await page.goto(base + '/sales/new')
  await choose('Покупець', customer.name)
  await choose('Склад', source.name)
  await choose('Тип ціни', type.name)
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-22')
  await page.getByRole('button', { name: 'Підібрати партії', exact: true }).click()
  await page
    .getByRole('dialog')
    .getByRole('combobox', { name: 'Товар для підбору', exact: true })
    .fill(goods.code)
  await page.getByRole('option', { name: new RegExp(goods.name) }).click()
  await page
    .getByRole('dialog')
    .getByRole('spinbutton', { name: 'Потрібна кількість', exact: true })
    .fill('4')
  await page.getByRole('dialog').getByRole('button', { name: 'Застосувати підбір', exact: true }).click()
  await page.getByRole('dialog').waitFor({ state: 'hidden' })
  await fill('Фактична ціна 1', 150, 'spinbutton')
  await page.getByRole('button', { name: 'Додати рядок', exact: true }).click()
  await page.getByRole('combobox', { name: 'Номенклатура 2', exact: true }).fill(service.code)
  await page.getByRole('option', { name: new RegExp(service.name) }).click()
  await fill('Кількість 2', 1, 'spinbutton')
  await fill('Знижка, %', 10, 'spinbutton')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка продажу', exact: true }).waitFor()
  const saleId = new URL(page.url()).pathname.split('/').at(-1)
  ids.saleId = saleId
  sale = await api('/sales/' + saleId)
  assert.deepEqual(
    sale.lines.map((x) => [x.recommendedUnitPrice, x.unitPrice]),
    [
      [112.5, 150],
      [200, 200],
    ],
  )
  await expectStock(source.id, 20, 4)
  assert.equal(count('stock_movements', saleId), 0)
  await api(`/batches/${batch.batchId}/prices/${type.id}`, { unitPrice: 130 }, 'PUT')
  await api(`/products/${service.id}/prices/${type.id}`, { unitPrice: 260 }, 'PUT')
  assert.deepEqual(
    (await api('/sales/' + saleId)).lines.map((x) => x.recommendedUnitPrice),
    [112.5, 200],
  )
  await postUi()
  await expectStock(source.id, 16, 0)
  assert.equal(count('stock_movements', saleId), 1)
  assert.equal(sum('settlement_movements', 'Amount', saleId), 720)
  await api(`/sales/${saleId}/post`, {})
  assert.equal(count('stock_movements', saleId), 1)
  await rejected('/sales/' + saleId, undefined, 'DELETE', 400)
  await shot('sale-posted')
  passed(
    'UI mixed GOODS/SERVICE sale: FIFO picker, snapshots, actual price, header discount, reserve → one negative movement, settlement +720',
  )
  await page.goto(base + '/expenses/' + directId)
  await confirm('Розпровести')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).waitFor()
  assert.equal(cost(), 90)
  assert.equal(metric(await api('/dashboard/summary?' + period), 'grossProfit'), 360)
  await postUi()
  assert.equal(metric(await api('/dashboard/summary?' + period), 'grossProfit'), 320)
  passed(
    'Historical gross profit recomputes 320 → 360 → 320 on direct Expense UNPOST/POST; saved BatchPrice unchanged',
  )
  // Simultaneous DRAFTs and writes use real cookie/CSRF against the same stock pool.
  const a = await api('/sales', saleBody(2)),
    b = await api('/sales', saleBody(2))
  await expectStock(source.id, 16, 4)
  await api(
    '/sales/' + a.document.id,
    { ...saleBody(2), version: a.document.version, lines: a.lines.map((x) => ({ ...x, quantity: 2 })) },
    'PUT',
  )
  await rejected('/sales', saleBody(13), 'POST', 409, 'INSUFFICIENT_STOCK')
  await api(`/sales/${a.document.id}/post`, {})
  await expectStock(source.id, 14, 2)
  await api(`/sales/${a.document.id}/unpost`, {})
  await expectStock(source.id, 16, 4)
  for (const x of [a, b]) await api('/sales/' + x.document.id, undefined, 'DELETE')
  await expectStock(source.id, 16, 0)
  const concurrent = await Promise.all([request('/sales', saleBody(9)), request('/sales', saleBody(9))])
  assert.deepEqual(concurrent.map((r) => r.status()).sort(), [201, 409])
  const winner = await concurrent.find((r) => r.status() === 201).json()
  await Promise.all([
    api(`/sales/${winner.document.id}/post`, {}),
    api(`/sales/${winner.document.id}/post`, {}),
  ])
  assert.equal(count('stock_movements', winner.document.id), 1)
  await expectStock(source.id, 7, 0)
  await api(`/sales/${winner.document.id}/unpost`, {})
  await api('/sales/' + winner.document.id, undefined, 'DELETE')
  await expectStock(source.id, 16, 0)
  const stale = await api('/sales', saleBody(1))
  const edit = { ...saleBody(1), version: stale.document.version, lines: stale.lines }
  await api('/sales/' + stale.document.id, edit, 'PUT')
  await rejected('/sales/' + stale.document.id, edit, 'PUT', 409)
  await api('/sales/' + stale.document.id, undefined, 'DELETE')
  passed(
    'Two DRAFTs, own reserve, insufficiency, reserve restoration/delete, concurrent oversell prevention, parallel idempotent POST, stale Version 409',
  )
  account = await accountUi('Каса V1', false)
  bank = await accountUi('Банк V1', true)
  const receiptId = await moneyUi('receipts', 500, customer)
  const supplierPaymentId = await moneyUi('payments', 1000, supplier)
  assert.equal(sum('settlement_movements', 'Amount', receiptId), -500)
  assert.equal(sum('settlement_movements', 'Amount', supplierPaymentId), 1000)
  assert.equal(
    Number(sql(`SELECT "Balance" FROM v_money_account_balance WHERE "MoneyAccountId"=${q(account.id)}`)),
    -500,
  )
  await page.goto(base + '/expenses/' + directId)
  await page.getByRole('link', { name: 'Створити виплату', exact: true }).click()
  await choose('Рахунок списання', account.name + ' · UAH')
  await fill('Сума списання', 200, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-23')
  assert.equal(
    await page.getByRole('spinbutton', { name: 'Сума взаєморозрахунку', exact: true }).inputValue(),
    '200',
  )
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.waitForURL(/\/money\/payments\/[0-9a-f-]{36}$/)
  const expensePaymentId = new URL(page.url()).pathname.split('/').at(-1)
  await postUi()
  assert.equal(sum('settlement_movements', 'Amount', expensePaymentId), 200)
  const ownerId = await moneyUi('receipts', 1000, null, 'OWNER')
  assert.equal(count('settlement_movements', ownerId), 0)
  await page.goto(base + '/money/transfers/new')
  await choose('Рахунок списання', account.name + ' · UAH')
  await choose('Рахунок зарахування', bank.name + ' · UAH')
  await fill('Сума списання', 50, 'spinbutton')
  await fill('Сума зарахування', 50, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-23')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.waitForURL(/\/money\/transfers\/[0-9a-f-]{36}$/)
  const moneyTransferId = new URL(page.url()).pathname.split('/').at(-1)
  await postUi()
  assert.equal(count('money_movements', moneyTransferId), 2)
  assert.equal(sum('money_movements', 'Amount', moneyTransferId), 0)
  assert.equal(count('settlement_movements', moneyTransferId), 0)
  await api(`/money/receipts/${receiptId}/unpost`, {})
  assert.equal(
    Number(sql(`SELECT "Balance" FROM v_money_account_balance WHERE "MoneyAccountId"=${q(account.id)}`)),
    -250,
  )
  await api(`/money/receipts/${receiptId}/post`, {})
  passed(
    'UI accounts, receipt, supplier/expense payments, negative balance, OWNER, two-sided transfer; UNPOST receipt permits negative balance',
  )
  await page.goto(base + '/expenses/new')
  await choose('Контрагент', carrier.name)
  await choose('Категорія витрати', expenseCategory.name)
  await fill('Сума витрати', 30, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-24')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка витрати', exact: true }).waitFor()
  generalId = new URL(page.url()).pathname.split('/').at(-1)
  await postUi()
  assert.equal(sum('settlement_movements', 'Amount', generalId), -30)
  assert.equal(count('money_movements', generalId), 0)
  await page.goto(base + '/sales/' + saleId)
  await page.getByRole('link', { name: '+ Створити повернення', exact: true }).click()
  await page.getByRole('checkbox', { name: 'Повернути · ' + goods.name, exact: true }).check()
  await fill('Кількість · ' + goods.name, 1, 'spinbutton')
  await page.getByRole('checkbox', { name: 'Повернути · ' + service.name, exact: true }).check()
  await fill('Кількість · ' + service.name, 0, 'spinbutton')
  await fill('Сума компенсації · ' + service.name, 50, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-24')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.waitForURL(/\/customer-returns\/[0-9a-f-]{36}$/)
  const customerReturnId = new URL(page.url()).pathname.split('/').at(-1)
  ids.customerReturnId = customerReturnId
  await rejected(`/sales/${saleId}/unpost`, {}, 'POST', 409, 'DOCUMENT_HAS_DEPENDENCIES')
  await postUi()
  assert.equal(sum('stock_movements', 'Quantity', customerReturnId), 1)
  assert.equal(count('stock_movements', customerReturnId), 1)
  assert.equal(sum('settlement_movements', 'Amount', customerReturnId), -185)
  await expectStock(source.id, 17, 0)
  passed(
    'UI CustomerReturn: same Batch GOODS +1, SERVICE compensation 50/quantity0, settlement -185; DRAFT blocks Sale UNPOST',
  )
  // A newly created directory location is intentionally not an operation location until used in a purchase/sale.
  const zero = await api('/purchases', { ...purchaseBody(1, 0), storageLocationId: destination.id })
  await api(`/purchases/${zero.document.id}/post`, {})
  await rejected(
    '/expenses',
    {
      documentDate: '2026-10-24',
      counterpartyId: carrier.id,
      expenseCategoryId: expenseCategory.id,
      currencyId: uah,
      amount: 10,
      exchangeRate: 1,
      purchaseInvoiceId: zero.document.id,
    },
    'POST',
    400,
    'INVALID_PURCHASE_EXPENSE_ALLOCATION',
  )
  await api(`/purchases/${zero.document.id}/unpost`, {})
  await api('/purchases/' + zero.document.id, undefined, 'DELETE')
  passed('Zero-value direct allocation rejected; new-location prerequisite recorded (no domain rule changed)')
  await page.goto(base + '/purchases/' + purchaseId)
  await page.getByRole('link', { name: '+ Повернути постачальнику', exact: true }).click()
  await page.getByRole('checkbox', { name: 'Повернути · ' + goods.name, exact: true }).check()
  await fill('Кількість · ' + goods.name, 2, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-25')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.waitForURL(/\/supplier-returns\/[0-9a-f-]{36}$/)
  const supplierReturnId = new URL(page.url()).pathname.split('/').at(-1)
  ids.supplierReturnId = supplierReturnId
  await page.goto(base + '/stock/transfers/new')
  await choose('Зі складу', source.name)
  await choose('На склад', destination.name)
  await page.getByRole('button', { name: 'Додати рядок', exact: true }).click()
  await choose('Партія 1', new RegExp(goods.name))
  await fill('Кількість 1', 3, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-25')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка: переміщення', exact: true }).waitFor()
  const transferId = new URL(page.url()).pathname.split('/').at(-1)
  ids.transferId = transferId
  await page.goto(base + '/stock/write-offs/new')
  await choose('Склад', source.name)
  await choose('Причина списання', reason.name)
  await page.getByRole('button', { name: 'Додати рядок', exact: true }).click()
  await choose('Партія 1', new RegExp(goods.name))
  await fill('Кількість 1', 1, 'spinbutton')
  await page.getByLabel('Дата документа', { exact: true }).fill('2026-10-26')
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка: списання', exact: true }).waitFor()
  const writeoffId = new URL(page.url()).pathname.split('/').at(-1)
  ids.writeoffId = writeoffId
  const reserveSale = await api('/sales', saleBody(2))
  await expectStock(source.id, 17, 8)
  await expectStock(destination.id, 0, 0)
  await api('/sales/' + reserveSale.document.id, undefined, 'DELETE')
  await expectStock(source.id, 17, 6)
  await postUi('/supplier-returns/' + supplierReturnId)
  assert.equal(sum('settlement_movements', 'Amount', supplierReturnId), 180)
  await api(`/supplier-returns/${supplierReturnId}/unpost`, {})
  await expectStock(source.id, 17, 6)
  await api(`/supplier-returns/${supplierReturnId}/post`, {})
  await postUi('/stock/transfers/' + transferId)
  await postUi('/stock/write-offs/' + writeoffId)
  await expectStock(source.id, 11, 0)
  await expectStock(destination.id, 3, 0)
  assert.equal(count('stock_movements', transferId), 2)
  assert.equal(sum('stock_movements', 'Quantity', transferId), 0)
  for (const id of [transferId, writeoffId]) {
    assert.equal(count('settlement_movements', id), 0)
    assert.equal(count('money_movements', id), 0)
  }
  assert.equal(
    Number(sql(`SELECT "TotalCostUah" FROM v_write_off_cost WHERE "DocumentId"=${q(writeoffId)}`)),
    100,
  )
  passed(
    'UI SupplierReturn/Transfer/WriteOff: all four reserve sources SUM=8; correct movements, original refund180, no extra money/settlement/batches',
  )
  const destUse = await api('/write-offs', {
    documentDate: '2026-10-26',
    storageLocationId: destination.id,
    writeOffReasonId: reason.id,
    lines: [{ lineNo: 1, batchId: batch.batchId, quantity: 1 }],
  })
  await rejected(`/stock-transfers/${transferId}/unpost`, {}, 'POST', 409, 'INSUFFICIENT_STOCK')
  await api(`/write-offs/${destUse.document.id}/post`, {})
  await rejected(`/stock-transfers/${transferId}/unpost`, {}, 'POST', 409, 'INSUFFICIENT_STOCK')
  await api(`/write-offs/${destUse.document.id}/unpost`, {})
  await api('/write-offs/' + destUse.document.id, undefined, 'DELETE')
  await api(`/stock-transfers/${transferId}/unpost`, {})
  await expectStock(destination.id, 0, 0)
  await expectStock(source.id, 14, 3)
  await api(`/stock-transfers/${transferId}/post`, {})
  const reserveReturn = await api('/sales', saleBody(11))
  await rejected(`/customer-returns/${customerReturnId}/unpost`, {}, 'POST', 409, 'INSUFFICIENT_STOCK')
  await api('/sales/' + reserveReturn.document.id, undefined, 'DELETE')
  await api(`/customer-returns/${customerReturnId}/unpost`, {})
  await api(`/customer-returns/${customerReturnId}/post`, {})
  await expectStock(source.id, 11, 0)
  passed(
    'Transfer UNPOST rejects destination reservations and consumed stock; CustomerReturn UNPOST rejects stock already promised; safe repost restores state',
  )
  const expected = {
    sales: 720,
    returns: 185,
    netSales: 535,
    cogs: 300,
    grossProfit: 235,
    expenses: 30,
    writeoffs: 100,
    managementResult: 105,
    money: 300,
  }
  const dashboard = await api('/dashboard/summary?' + period)
  for (const [key, value] of Object.entries(expected)) assert.equal(metric(dashboard, key), value, key)
  const partyBalances = async (p) =>
    (await api('/settlements/counterparties/' + p.id)).balances.find((x) => x.currencyId === uah)?.balance ??
    0
  assert.equal(await partyBalances(customer), 35)
  assert.equal(await partyBalances(supplier), -620)
  assert.equal(await partyBalances(carrier), -30)
  await api(`/expenses/${directId}/unpost`, {})
  const revised = await api('/dashboard/summary?' + period)
  assert.equal(metric(revised, 'cogs'), 270)
  assert.equal(metric(revised, 'writeoffs'), 90)
  assert.equal(metric(revised, 'managementResult'), 145)
  assert.equal(await price(), 130)
  await api(`/expenses/${directId}/post`, {})
  for (const [kind, title] of [
    ['sales', 'Продажі'],
    ['purchases', 'Закупівлі'],
    ['expenses', 'Витрати'],
    ['profitability', 'Рентабельність'],
    ['stock', 'Поточні залишки'],
    ['money', 'Гроші'],
    ['settlements', 'Взаєморозрахунки'],
  ]) {
    await page.goto(base + '/reports/' + kind)
    await page.getByRole('table', { name: title, exact: true }).waitFor()
  }
  await page.goto(base + '/reports/money')
  await choose('Рахунок', account.name)
  await page.waitForFunction((name) => {
    const rows = [...document.querySelectorAll('table[aria-label="Гроші"] tbody tr')]
    return rows.length === 1 && rows[0].textContent.includes(name)
  }, account.name)
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Експорт CSV', exact: true }).click()
  const download = await downloadPromise
  await download.saveAs(out + '/filtered-money.csv')
  const csv = await (await import('node:fs/promises')).readFile(out + '/filtered-money.csv', 'utf8')
  assert.ok(csv.includes(account.name))
  assert.ok(!csv.includes(bank.name))
  await page.goto(base + '/')
  await page.getByTestId('metric-managementResult').filter({ hasText: '105,00 UAH' }).waitFor()
  await shot('dashboard-control')
  passed(
    'Control dataset reconciles dashboard + seven reports + ledgers; direct expenses not deducted twice, OWNER excluded; CSV respects account filter',
  )
  await page.goto(base + '/admin/roles/new')
  await fill('Назва ролі', 'Оператор V1')
  for (const code of ['product.view', 'sale.view', 'sale.create', 'stock.view'])
    await page.getByRole('checkbox', { name: new RegExp(code.replace('.', '\\.') + '$') }).check()
  await page.getByRole('button', { name: 'Зберегти роль', exact: true }).click()
  await page.waitForURL(/\/admin\/roles\/[0-9a-f-]{36}$/)
  const limitedRole = (await api('/admin/roles')).find((x) => x.name === 'Оператор V1')
  await page.goto(base + '/admin/users/new')
  await fill('Логін', 'operator_v1')
  await fill('Повне ім’я', 'Оператор V1')
  await page.locator('input[name="password"]').fill(password)
  await page.locator('input[name="confirmPassword"]').fill(password)
  await page.getByRole('checkbox', { name: limitedRole.name, exact: true }).check()
  await page.getByRole('button', { name: 'Зберегти користувача', exact: true }).click()
  await page.waitForURL(/\/admin\/users\/[0-9a-f-]{36}$/)
  const limited = await browser.newContext({ ignoreHTTPSErrors: true }),
    lp = await limited.newPage()
  lp.on('pageerror', (e) => errors.push(e.message))
  await login(lp, 'operator_v1')
  const nav = lp.getByRole('navigation', { name: 'Основна навігація' })
  for (const name of ['Адміністрування', 'Гроші', 'Звіти'])
    assert.equal(await nav.getByRole('link', { name, exact: true }).count(), 0)
  await lp.goto(base + '/admin/users')
  await lp.getByRole('heading', { name: 'Недостатньо прав', exact: true }).waitFor()
  await rejected('/admin/users', undefined, 'GET', 403, null, limited)
  await rejected(
    '/sales',
    { ...saleBody(1), lines: [{ ...saleBody(1).lines[0], unitPrice: 129 }] },
    'POST',
    403,
    'PRICE_BELOW_RECOMMENDED',
    limited,
  )
  const noCost = await api('/stock/overview', undefined, 'GET', limited)
  assert.ok(noCost.every((row) => row.currentUnitCost == null))
  await rejected('/reports/profitability', undefined, 'GET', 403, null, limited)
  await limited.close()
  passed(
    'UI role/user creation + limited login, hidden navigation, direct route/API403, sale.change_price server enforcement, hidden stock cost',
  )
  const unauth = await browser.newContext({ ignoreHTTPSErrors: true })
  await rejected('/products', undefined, 'GET', 401, null, unauth)
  const noCsrf = await context.request.post(base + '/api/categories', { data: { name: 'Rejected CSRF' } })
  assert.equal(noCsrf.status(), 400)
  await unauth.close()
  const audit = await api('/admin/audit?take=200')
  assert.ok(audit.items.some((x) => x.entityType === 'BatchPrice' && x.eventType === 'UPDATE'))
  assert.ok(audit.items.some((x) => x.entityType === 'ProductPrice' && x.eventType === 'UPDATE'))
  assert.ok(audit.items.some((x) => x.entityType === 'Role' && x.eventType === 'PERMISSIONS'))
  for (const event of ['CREATE', 'POST', 'UNPOST', 'DELETE'])
    assert.ok(
      audit.items.some((x) => x.eventType === event),
      event,
    )
  assert.ok(audit.items.every((x) => x.actorId === me.userId))
  const entry = audit.items.find((x) => x.entityType === 'BatchPrice' && x.eventType === 'UPDATE')
  const detail = await api('/admin/audit/' + entry.id)
  assert.equal(detail.oldValues.UnitPrice, 112.5)
  assert.equal(detail.newValues.UnitPrice, 130)
  await rejected('/admin/audit/' + entry.id, undefined, 'DELETE', 405)
  await page.goto(base + '/admin/audit')
  await choose('Тип сутності', 'Тип ціни')
  await page.getByRole('button', { name: 'Деталі', exact: true }).first().click()
  await page.getByRole('heading', { name: 'Нові значення', exact: true }).waitFor()
  await shot('audit-detail')
  await page.getByRole('button', { name: 'Закрити деталі', exact: true }).click()
  await page.setViewportSize({ width: 390, height: 844 })
  for (const [path, label] of [
    ['/products', 'Список товарів'],
    ['/counterparties', 'Список контрагентів'],
    ['/purchases', 'Список закупівель'],
    ['/sales', 'Список продажів'],
    ['/stock', 'Складські залишки'],
    ['/expenses', 'Витрати'],
    ['/customer-returns', 'Повернення покупців'],
    ['/supplier-returns', 'Повернення постачальникам'],
    ['/admin/users', 'Користувачі'],
    ['/admin/roles', 'Ролі'],
    ['/admin/audit', 'Події аудиту'],
  ]) {
    await page.goto(base + path)
    await page.getByRole('list', { name: label, exact: true }).waitFor()
    await shot('mobile-' + path.replaceAll('/', '-'))
  }
  assert.deepEqual(errors, [])
  assert.deepEqual(failedRequests, [])
  passed(
    'Audit event coverage, actor/old/new, read-only API; 401/CSRF; eleven mobile lists, zero runtime/unexpected errors; expected auth and known M3 network diagnostics classified',
  )
  await writeFile(
    out + '/result.json',
    JSON.stringify(
      {
        result: 'PASS',
        checks,
        ids,
        expected,
        balances: { customer: 35, supplier: -620, carrier: -30 },
        browserErrors: errors,
        expectedDiagnostics,
        knownBrowserDiagnostics,
        browserWarnings: warnings,
        failedRequests,
        apiAssistedSetup: [
          'Initial SERVICE ProductPrice entered through UI',
          'ExpenseCategory/WriteOffReason through directory UI',
          'zero purchase verifies allocation and registers destination per existing rule',
        ],
        scope:
          'One continuous fresh-install flow, Production API + HTTPS Caddy + PostgreSQL18 + real Identity',
      },
      null,
      2,
    ),
  )
} catch (e) {
  await page.screenshot({ path: out + '/failure.png', fullPage: false })
  console.error('URL:', page.url())
  console.error(e)
  console.error({ errors, warnings, failedRequests })
  process.exitCode = 1
} finally {
  await browser.close()
}
