import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdir, writeFile } from 'node:fs/promises'
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = process.env.AUDIT_BASE_URL ?? 'https://localhost:8444',
  password = process.env.AUDIT_ADMIN_PASSWORD
assert.ok(password, 'Set AUDIT_ADMIN_PASSWORD for the isolated audit environment')
const out = process.env.AUDIT_OUTPUT ?? '/tmp/trade-v1-audit/service-pricing'
await mkdir(out, { recursive: true })
const browser = await chromium.launch({
  executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium',
  args: ['--no-sandbox'],
})
const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 1050 },
  }),
  page = await context.newPage()
const errors = [],
  warnings = []
page.on('pageerror', (e) => errors.push(e.message))
page.on('console', (m) => {
  if (m.type() === 'warning') warnings.push(m.text())
  if (m.type() === 'error' && !m.location().url.includes('/auth/me')) errors.push(m.text())
})
async function api(path, data, method = data === undefined ? 'GET' : 'POST') {
  let headers = {}
  if (method !== 'GET') {
    const c = await (await context.request.get(base + '/api/auth/csrf')).json()
    headers[c.headerName] = c.requestToken
  }
  const r = await context.request.fetch(base + '/api' + path, { method, data, headers })
  assert.ok(r.ok(), `${path}: ${r.status()} ${await r.text()}`)
  return r.status() === 204 ? null : r.json()
}
async function choose(label, name) {
  await page.getByRole('combobox', { name: new RegExp('^' + label) }).click()
  await page.getByRole('option', { name, exact: typeof name === 'string' }).click()
}
async function shot(name) {
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: false })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, name)
}
async function sale(service, amount, post) {
  await page.goto(base + '/sales/new')
  await choose('Покупець', 'Покупець V1')
  await choose('Склад', 'Основний')
  await choose('Тип ціни', 'Роздрібна')
  await page.getByRole('button', { name: 'Додати рядок', exact: true }).click()
  await page.getByRole('combobox', { name: 'Номенклатура 1', exact: true }).fill(service.code)
  await page.getByRole('option', { name: new RegExp(service.name) }).click()
  await page
    .getByLabel('Рекомендована ціна 1', { exact: true })
    .filter({ hasText: amount + ',00' })
    .waitFor()
  await page.getByRole('button', { name: 'Зберегти', exact: true }).click()
  await page.getByRole('heading', { name: 'Чернетка продажу', exact: true }).waitFor()
  const id = new URL(page.url()).pathname.split('/').at(-1)
  if (post) {
    await page.getByRole('button', { name: 'Провести', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Провести', exact: true }).click()
    await page.getByRole('button', { name: 'Розпровести', exact: true }).waitFor()
  }
  return api('/sales/' + id)
}
try {
  await page.goto(base + '/login')
  await page.getByLabel('Логін', { exact: true }).fill('admin')
  await page.getByLabel('Пароль', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Увійти', exact: true }).click()
  await page.getByRole('heading', { name: 'Огляд діяльності', exact: true }).waitFor()
  await page.goto(base + '/products/new')
  await choose('Тип', 'Послуга')
  await page.getByRole('textbox', { name: 'Назва', exact: true }).fill('Оформлення букета')
  await page.getByRole('textbox', { name: 'Код', exact: true }).fill('SERVICE-BOUQUET')
  await choose('Одиниця виміру', 'штука')
  await page.getByRole('spinbutton', { name: 'Початкова ціна · Роздрібна', exact: true }).fill('300')
  await page.getByRole('spinbutton', { name: 'Початкова ціна · Гуртова', exact: true }).fill('250')
  await page.getByRole('button', { name: 'Створити', exact: true }).click()
  await page.getByRole('heading', { name: 'Оформлення букета', exact: true }).waitFor()
  const productId = new URL(page.url()).pathname.split('/').at(-1)
  const service = await api('/products/' + productId)
  await page.reload()
  await page.getByText('300,00 UAH', { exact: true }).waitFor()
  await page.getByText('250,00 UAH', { exact: true }).waitFor()
  await shot('A-initial-prices-reopened')
  const old = await sale(service, 300, true)
  assert.equal(old.lines[0].recommendedUnitPrice, 300)
  assert.equal(old.lines[0].batchId, null)
  assert.equal(
    execFileSync(
      'docker',
      [
        'exec',
        process.env.AUDIT_DB_CONTAINER ?? 'trade-v1-audit-db',
        'psql',
        '-U',
        process.env.AUDIT_DB_USER ?? 'trade_v1',
        '-d',
        process.env.AUDIT_DB_NAME ?? 'trade_v1',
        '-At',
        '-c',
        `SELECT COUNT(*) FROM stock_movements WHERE \"DocumentId\"='${old.document.id}'`,
      ],
      { encoding: 'utf8' },
    ).trim(),
    '0',
  )
  await shot('B-service-sale-posted')
  await page.goto(base + '/products/' + service.id)
  await page.getByRole('button', { name: 'Редагувати ціну Роздрібна', exact: true }).click()
  await page.getByRole('spinbutton', { name: 'Ціна, UAH', exact: true }).fill('350')
  await page.getByRole('button', { name: 'Зберегти ціну', exact: true }).click()
  await page.getByText('350,00 UAH', { exact: true }).waitFor()
  const fresh = await sale(service, 350, false)
  assert.equal(fresh.lines[0].recommendedUnitPrice, 350)
  assert.equal((await api('/sales/' + old.document.id)).lines[0].recommendedUnitPrice, 300)
  await shot('C-new-sale-350')
  const audit = await api('/admin/audit?entityType=ProductPrice&eventType=UPDATE&search=' + service.id)
  const row = audit.items[0]
  assert.ok(row)
  const detail = await api('/admin/audit/' + row.id)
  assert.equal(detail.oldValues.UnitPrice, 300)
  assert.equal(detail.newValues.UnitPrice, 350)
  assert.ok(row.entityId.includes(service.id))
  assert.ok(row.actorId)
  assert.ok(row.occurredAt)
  await page.goto(base + '/admin/audit')
  await choose('Тип сутності', 'Ціна послуги')
  await choose('Подія', 'Зміна')
  await page.getByRole('textbox', { name: 'Пошук / ID сутності', exact: true }).fill(service.id)
  await page.waitForFunction(
    () => document.querySelectorAll('table[aria-label="Події аудиту"] tbody tr').length === 1,
  )
  await page.getByRole('button', { name: 'Деталі', exact: true }).click()
  await page.getByRole('heading', { name: 'Нові значення', exact: true }).waitFor()
  assert.ok((await page.locator('pre').allTextContents()).some((t) => t.includes('350')))
  await shot('D-audit-300-to-350')
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(base + '/products/' + service.id)
  await page.getByRole('list', { name: 'Ціни послуги', exact: true }).waitFor()
  await page.getByRole('button', { name: 'Редагувати ціну Гуртова', exact: true }).click()
  await page.getByRole('dialog').waitFor()
  await shot('mobile-editor')
  await page.getByRole('button', { name: 'Скасувати', exact: true }).click()
  assert.deepEqual(errors, [])
  await writeFile(
    out + '/result.json',
    JSON.stringify(
      {
        result: 'PASS',
        scenarios: {
          A: 'SERVICE initial Retail300/Wholesale250 persisted and reopened',
          B: 'POST sale recommendation300; no StockMovement',
          C: 'New sale350, old snapshot300',
          D: 'Audit old300/new350, actor, Product+PriceType composite identity, timestamp',
        },
        productId,
        oldSaleId: old.document.id,
        newSaleId: fresh.document.id,
        errors,
        warnings,
        mobile: 'price cards and editor, no horizontal overflow',
      },
      null,
      2,
    ),
  )
  console.log('PASS A/B/C/D + mobile; no browser errors')
} catch (e) {
  await page.screenshot({ path: out + '/failure.png', fullPage: false })
  console.error(page.url(), e, { errors, warnings })
  process.exitCode = 1
} finally {
  await browser.close()
}
