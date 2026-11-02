import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import { uahId } from '../purchases/contracts'
import { returnConfig } from './contracts'
import type { ReturnKind } from './contracts'

const guid = (n: number) => `019a073c-9ee5-7000-8000-${String(n).padStart(12, '0')}`
const sourceId = guid(10); const location = { id: guid(20), name: 'Основний склад' }; const partyId = guid(30); const goodsLineId = guid(40); const serviceLineId = guid(41)
const goods = { productId: guid(50), name: 'Кавомашина' }; const service = { productId: guid(51), name: 'Монтаж' }; const batch = { batchId: guid(60), batchNumber: 'B-00000001' }
const permissions = [...testUser.permissions, 'sale.create', 'sale.post', 'sale.unpost', 'sale.delete', 'purchase.create', 'purchase.post', 'purchase.unpost', 'purchase.delete']
const servicesToClean: ReturnType<typeof createAppServices>[] = []
function RouteProbe() { return <span data-testid="route">{useLocation().pathname}</span> }
function setup({ kind = 'customer', path, empty = false, pending = false, allowed = permissions, posted = false, noSources = false, serviceOnly = false, persisted = false }: { kind?: ReturnKind; path?: string; empty?: boolean; pending?: boolean; allowed?: string[]; posted?: boolean; noSources?: boolean; serviceOnly?: boolean; persisted?: boolean } = {}) {
  const config = returnConfig[kind]; path ??= config.root
  const source = { id: sourceId, number: kind === 'customer' ? 'SI-00000001' : 'PI-00000001', documentDate: '2026-10-20', counterpartyId: partyId, counterpartyName: kind === 'customer' ? 'Покупець' : 'Постачальник', storageLocationId: location.id, currencyId: uahId, exchangeRate: 1, discountPercent: 10 }
  const persistedLines = serviceOnly ? [{ id: guid(71), [config.sourceLineKey]: serviceLineId, quantity: 1, refundAmount: 50 }] : [{ id: guid(70), [config.sourceLineKey]: goodsLineId, quantity: 2, ...(kind === 'customer' ? { refundAmount: 180 } : {}) }]
  let current = { document: { id: guid(80), number: posted ? 'RETURN-00000001' : null as string | null, documentDate: '2026-10-29', status: posted ? 'POSTED' : 'DRAFT', comment: 'Повернення', internalNote: null as string | null, version: 1 }, [config.sourceKey]: sourceId, storageLocationId: location.id, lines: persistedLines }
  let exists = persisted || !path.split('?')[0].endsWith('/new'); let deleted = false; let physical = 10
  const calls: { path: string; method: string; body: Record<string, unknown> }[] = []; const failures = new Map<string, { status: number; code?: string; detail?: string; errors?: Record<string, string[]> }>()
  let release: (() => void) | undefined; const gate = pending ? new Promise<void>(resolve => { release = resolve }) : Promise.resolve()
  const customerRows = [
    { sourceSaleLineId: goodsLineId, productId: goods.productId, itemType: 'GOODS', batchId: batch.batchId, soldQuantity: 4, alreadyReturnedQuantity: 0, returnableQuantity: 4, originalUnitPrice: 100, originalDiscountPercent: 10, refundableAmount: 360 },
    { sourceSaleLineId: serviceLineId, productId: service.productId, itemType: 'SERVICE', batchId: null, soldQuantity: 1, alreadyReturnedQuantity: 0, returnableQuantity: 1, originalUnitPrice: 200, originalDiscountPercent: 10, refundableAmount: 180 },
  ]
  const supplierRows = [{ sourcePurchaseLineId: goodsLineId, productId: goods.productId, batchId: batch.batchId, purchasedQuantity: 10, alreadyReturnedQuantity: 0, remainingReturnableByPurchase: 10, physicalQuantity: 10, reservedQuantity: 0, availableQuantity: 10, availableForDocument: 10, maxReturnableQuantity: 10, originalUnitPriceCurrency: 100, originalDiscountPercent: 10 }]
  function amounts(lines: Record<string, unknown>[]) { const refunds = lines.map(l => ({ sourceLineId: l[config.sourceLineKey], refundAmount: l[config.sourceLineKey] === serviceLineId ? Number(l.refundAmount) : Number(l.quantity) * 90 })); return { totalRefund: refunds.reduce((sum, l) => sum + l.refundAmount, 0), lines: refunds } }
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const requestPath = parsed.pathname.replace(/^\/api/, ''); const method = options?.method ?? 'GET'; const body = options?.body ? JSON.parse(String(options.body)) : {}
    calls.push({ path: requestPath + parsed.search, method, body }); const failure = failures.get(method + ' ' + requestPath); if (failure) return json(failure, failure.status)
    if (requestPath === '/auth/me') return json({ ...testUser, permissions: allowed })
    if (requestPath === '/auth/csrf') return json({ requestToken: 'test-csrf', headerName: 'X-CSRF-TOKEN' })
    if (requestPath === config.root + '/lookups') return json({ sources: noSources ? [] : [source], locations: [location], products: [goods, service], batches: [batch] })
    if (requestPath === config.sourceRoot + '/' + sourceId + '/returnable-lines') {
      if (kind === 'customer') return json(serviceOnly ? customerRows.slice(1) : customerRows)
      const reserved = exists && !deleted && current.document.status === 'DRAFT' ? Number(current.lines[0]?.quantity ?? 0) : 0
      return json(supplierRows.map(row => ({ ...row, physicalQuantity: physical, reservedQuantity: reserved, availableQuantity: physical - reserved, availableForDocument: parsed.searchParams.has('excludeSupplierReturnId') ? physical : physical - reserved, maxReturnableQuantity: Math.min(10, parsed.searchParams.has('excludeSupplierReturnId') ? physical : physical - reserved) })))
    }
    if (requestPath === config.root + '/overview') { await gate; const match = !empty && exists && !deleted && (!parsed.searchParams.get('status') || parsed.searchParams.get('status') === current.document.status); return json(match ? [{ id: current.document.id, number: current.document.number, documentDate: current.document.documentDate, counterpartyId: partyId, counterpartyName: source.counterpartyName, sourceId, sourceNumber: source.number, currencyId: uahId, totalRefund: amounts(current.lines).totalRefund, status: current.document.status }] : []) }
    if (requestPath === config.root + '/preview') return json(amounts(body.lines))
    if (requestPath === config.root && method === 'POST') { exists = true; current = { ...current, ...body, lines: body.lines.map((l: Record<string, unknown>, i: number) => ({ ...l, id: guid(70 + i), ...(kind === 'customer' ? { refundAmount: amounts([l]).totalRefund } : {}) })), document: { ...current.document, documentDate: body.documentDate, comment: body.comment, internalNote: body.internalNote } }; return json(current, 201) }
    if (requestPath === `${config.root}/${current.document.id}/refund`) return json(amounts(current.lines))
    if (requestPath === `${config.root}/${current.document.id}/post` || requestPath === `${config.root}/${current.document.id}/unpost`) { current = { ...current, document: { ...current.document, number: 'RETURN-00000001', status: requestPath.endsWith('/unpost') ? 'DRAFT' : 'POSTED', version: current.document.version + 1 } }; return json(current) }
    if (requestPath === `${config.root}/${current.document.id}`) {
      if (method === 'DELETE') { deleted = true; return new Response(null, { status: 204 }) }
      if (deleted) return json({}, 404)
      if (method === 'PUT') { if (body.version !== current.document.version) return json({ code: 'VERSION_CONFLICT' }, 409); current = { ...current, ...body, document: { ...current.document, comment: body.comment, version: current.document.version + 1 } }; if (kind === 'customer') current.lines = current.lines.map(l => ({ ...l, refundAmount: amounts([l]).totalRefund })) }
      return json(current)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch); const services = createAppServices('/api'); servicesToClean.push(services); services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><RouteProbe /></MemoryRouter></AppProviders>)
  return { calls, services, setPhysical: (value: number) => { physical = value }, release: () => release?.(), current: () => current, setVersion: (version: number) => { current.document.version = version }, fail: (method: string, path: string, status: number, code?: string, detail?: string, errors?: Record<string, string[]>) => failures.set(method + ' ' + path, { status, code, detail, errors }), clear: () => failures.clear() }
}
async function choose(label: string, option: string | RegExp) { await userEvent.click(await screen.findByRole('combobox', { name: new RegExp('^' + label) })); await userEvent.click(await screen.findByRole('option', { name: option })) }
async function number(label: string, value: string) { const input = screen.getByRole('spinbutton', { name: label }); await userEvent.clear(input); if (value) await userEvent.type(input, value) }
async function selectGoods(quantity = '2') { await userEvent.click(await screen.findByRole('checkbox', { name: 'Повернути · Кавомашина' })); await number('Кількість · Кавомашина', quantity) }
async function confirm(action: string) { const button = await screen.findByRole('button', { name: action }); await waitFor(() => expect(button).toBeEnabled()); await userEvent.click(button); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: action })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()) }
beforeEach(() => setViewport()); afterEach(() => { servicesToClean.splice(0).forEach(s => s.queryClient.clear()); vi.unstubAllGlobals() })

describe.each(['customer', 'supplier'] as const)('%s return UI', kind => {
  const config = returnConfig[kind]
  it('loads list with source, party, amount and status without IDs', async () => { setup({ kind }); const table = await screen.findByRole('table', { name: config.title }); expect(within(table).getByRole('cell', { name: kind === 'customer' ? /^Покупець/ : /^Постачальник/ })).toBeVisible(); expect(within(table).getByRole('link', { name: kind === 'customer' ? 'SI-00000001' : 'PI-00000001' })).toHaveAttribute('href', `${config.sourceRoot}/${sourceId}`); expect(screen.queryByText(sourceId)).not.toBeInTheDocument() })
  it('shows loading, empty list and retry', async () => { const app = setup({ kind, pending: true, empty: true }); expect(await screen.findByRole('status', { name: 'Завантаження повернень' })).toBeVisible(); await act(async () => app.release()); expect(await screen.findByText('Повернень ще немає')).toBeVisible() })
  it('sends date, counterparty, status and source filters', async () => { const app = setup({ kind }); await screen.findByRole('table', { name: config.title }); fireEvent.change(screen.getByLabelText('Період від'), { target: { value: '2026-10-01' } }); fireEvent.change(screen.getByLabelText('Період до'), { target: { value: '2026-10-31' } }); await choose(config.party, kind === 'customer' ? 'Покупець' : 'Постачальник'); await choose(config.source, kind === 'customer' ? 'SI-00000001' : 'PI-00000001'); await choose('Статус', 'Проведені'); expect(app.calls.some(c => ['from', 'to', 'counterpartyId', config.sourceKey, 'status'].every(k => new URL(c.path, 'http://localhost').searchParams.has(k)))).toBe(true) })
  it('creates from source prefill using stable source line IDs and backend-derived refunds', async () => { const app = setup({ kind, path: `${config.root}/new?${config.sourceKey}=${sourceId}` }); await selectGoods(); await waitFor(() => expect(screen.getByRole('textbox', { name: 'Сума компенсації · Кавомашина' })).toHaveValue('180,00')); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку повернення збережено.'); const body = app.calls.find(c => c.method === 'POST' && c.path === config.root)!.body; expect(body).toMatchObject({ [config.sourceKey]: sourceId, storageLocationId: location.id, lines: [{ [config.sourceLineKey]: goodsLineId, quantity: 2 }] }); expect((body.lines as Record<string, unknown>[])[0]).not.toHaveProperty('refundAmount'); expect(body).not.toHaveProperty('totalRefund') })
  it('validates required source and selected lines', async () => { const app = setup({ kind, path: config.root + '/new' }); await userEvent.click(await screen.findByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Виберіть проведений документ-підставу.')).toBeVisible(); expect(screen.getByText('Виберіть хоча б один рядок повернення.')).toBeVisible(); expect(app.calls.some(c => c.method === 'POST' && c.path === config.root)).toBe(false) })
  it('offers only posted sources and explains unavailable source prefill', async () => { setup({ kind, path: `${config.root}/new?${config.sourceKey}=${guid(999)}` }); expect(await screen.findByText('Документ-підстава недоступний. Потрібен проведений документ своєї організації.')).toBeVisible(); await userEvent.click(screen.getByRole('combobox', { name: new RegExp('^' + config.source) })); expect(screen.queryByRole('option', { name: /DRAFT|OPENING_STOCK/ })).not.toBeInTheDocument() })
  it('posts dirty form after confirmation and keeps POSTED read-only without direct delete', async () => { const app = setup({ kind, path: `${config.root}/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); await number('Кількість · Кавомашина', '3'); await confirm('Провести'); await screen.findByRole('button', { name: 'Розпровести' }); const writes = app.calls.filter(c => c.method === 'PUT' || c.path.endsWith('/post')); expect(writes.map(c => c.method)).toEqual(['PUT', 'POST']); expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument() })
  it('failed save blocks posting', async () => { const app = setup({ kind, path: `${config.root}/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); await number('Кількість · Кавомашина', '3'); app.fail('PUT', `${config.root}/${guid(80)}`, 409, 'VERSION_CONFLICT'); await confirm('Провести'); expect(app.calls.some(c => c.path.endsWith('/post'))).toBe(false); expect((await screen.findAllByText('Документ змінено іншим користувачем. Оновіть дані перед збереженням.')).length).toBeGreaterThan(0) })
  it('retains a created draft after failed POST instead of creating it again', async () => { const app = setup({ kind, path: `${config.root}/new?${config.sourceKey}=${sourceId}` }); await selectGoods(); app.fail('POST', `${config.root}/${guid(80)}/post`, 409, 'INSUFFICIENT_STOCK'); await confirm('Провести'); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); app.clear(); await confirm('Провести'); await screen.findByRole('button', { name: 'Розпровести' }); expect(app.calls.filter(c => c.path === config.root && c.method === 'POST')).toHaveLength(1) })
  it('unposts and restores editable draft', async () => { const app = setup({ kind, posted: true, path: `${config.root}/${guid(80)}` }); await confirm('Розпровести'); await screen.findByRole('button', { name: 'Зберегти' }); expect(app.calls.some(c => c.path.endsWith('/unpost'))).toBe(true); if (kind === 'supplier') expect(await screen.findByText(/Чернетка резервує товар/)).toBeVisible() })
  it('logically deletes DRAFT through existing DELETE', async () => { const app = setup({ kind, path: `${config.root}/${guid(80)}` }); await screen.findByRole('button', { name: 'Зберегти' }); await confirm('Видалити'); expect(await screen.findByText('Повернень ще немає')).toBeVisible(); expect(app.calls.some(c => c.method === 'DELETE' && c.path === `${config.root}/${guid(80)}`)).toBe(true) })
  it('hides create/edit/post/delete actions for view-only user', async () => { setup({ kind, path: `${config.root}/${guid(80)}`, allowed: [`${config.permission}.view`] }); await screen.findByText(/Загальна сума компенсації:/); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Провести' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument() })
  it('rejects direct route without its existing permission', async () => { setup({ kind, allowed: [] }); expect(await screen.findByText('Недостатньо прав')).toBeVisible() })
  it('hides create link without create permission', async () => { setup({ kind, allowed: [`${config.permission}.view`] }); await screen.findByRole('table', { name: config.title }); expect(screen.queryByRole('link', { name: '+ ' + config.create })).not.toBeInTheDocument() })
  it('can post with post permission while the draft remains read-only', async () => { const app = setup({ kind, path: `${config.root}/${guid(80)}`, allowed: [`${config.permission}.view`, `${config.permission}.post`] }); await confirm('Провести'); await screen.findByText('Повернення проведено.'); expect(app.calls.some(c => c.method === 'PUT')).toBe(false); expect(app.calls.some(c => c.path.endsWith('/post'))).toBe(true) })
  it('create-only permission can save without requesting a protected detail', async () => { const app = setup({ kind, path: `${config.root}/new?${config.sourceKey}=${sourceId}`, allowed: [`${config.permission}.create`] }); await selectGoods(); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку повернення збережено.'); expect(app.calls.some(c => c.method === 'GET' && c.path === `${config.root}/${guid(80)}`)).toBe(false); expect(screen.queryByText('Недостатньо прав')).not.toBeInTheDocument() })
  it('handles backend 403 with the existing forbidden state', async () => { const app = setup({ kind, pending: true }); app.fail('GET', `${config.root}/overview`, 403); await act(async () => { app.release(); await app.services.queryClient.invalidateQueries({ queryKey: ['returns', kind, 'list'] }) }); expect((await screen.findAllByText('Недостатньо прав')).length).toBeGreaterThan(0) })
  it('retries a failed list request', async () => { const app = setup({ kind, pending: true }); app.fail('GET', `${config.root}/overview`, 503); await act(async () => { app.release(); await app.services.queryClient.invalidateQueries({ queryKey: ['returns', kind, 'list'] }) }); const button = await screen.findByRole('button', { name: 'Спробувати ще раз' }); app.clear(); await userEvent.click(button); expect(await screen.findByRole('table', { name: config.title })).toBeVisible() })
  it('renders mobile list cards', async () => { setViewport(390); setup({ kind }); expect(await screen.findByRole('list', { name: config.title })).toBeVisible(); expect(screen.queryByRole('table', { name: config.title })).not.toBeInTheDocument() })
  it('renders mobile line cards and accessible save actions', async () => { setViewport(390); setup({ kind, path: `${config.root}/new?${config.sourceKey}=${sourceId}` }); expect(await screen.findByRole('list', { name: 'Рядки повернення' })).toBeVisible(); expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible(); expect(screen.queryByRole('table', { name: 'Рядки повернення' })).not.toBeInTheDocument() })
})

describe('Return line rules and errors', () => {
  it('saves and posts a SERVICE financial compensation with zero quantity', async () => {
    const app = setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}`, serviceOnly: true })
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Повернути · Монтаж' }))
    await number('Кількість · Монтаж', '0'); await number('Сума компенсації · Монтаж', '50')
    expect(screen.getByRole('spinbutton', { name: 'Кількість · Монтаж' })).toHaveAttribute('min', '0')
    await confirm('Провести')
    expect(await screen.findByRole('button', { name: 'Розпровести' })).toBeVisible()
    expect(app.calls.find(c => c.path === '/customer-returns' && c.method === 'POST')!.body.lines).toMatchObject([{ quantity: 0, refundAmount: 50 }])
  })
  it('supports SERVICE partial refund without stock fields', async () => { const app = setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}`, serviceOnly: true }); await userEvent.click(await screen.findByRole('checkbox', { name: 'Повернути · Монтаж' })); await number('Сума компенсації · Монтаж', '50'); expect(screen.getByText('Для послуги можна вказати часткову суму компенсації.')).toBeVisible(); expect(screen.queryByText(/Партія:/)).not.toBeInTheDocument(); expect(screen.queryByText('Фізичний залишок')).not.toBeInTheDocument(); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку повернення збережено.'); expect(app.calls.find(c => c.path === '/customer-returns' && c.method === 'POST')!.body.lines).toMatchObject([{ sourceSaleLineId: serviceLineId, quantity: 1, refundAmount: 50 }]) })
  it('supports mixed return and backend refund total', async () => { setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}` }); await selectGoods('1'); await userEvent.click(screen.getByRole('checkbox', { name: 'Повернути · Монтаж' })); await number('Сума компенсації · Монтаж', '50'); expect(await screen.findByText('140,00 UAH')).toBeVisible(); await confirm('Провести'); expect(await screen.findByRole('button', { name: 'Розпровести' })).toBeVisible() })
  it('blocks GOODS over returnable and SERVICE over refundable amount', async () => { const app = setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}` }); await selectGoods('5'); await userEvent.click(screen.getByRole('checkbox', { name: 'Повернути · Монтаж' })); await number('Сума компенсації · Монтаж', '181'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Кількість перевищує доступну до повернення.')).toBeVisible(); expect(screen.getByText('Сума перевищує залишок компенсації.')).toBeVisible(); expect(app.calls.some(c => c.path === '/customer-returns' && c.method === 'POST')).toBe(false) })
  it('shows supplier stock, reserve, own availability and purchase limit separately', async () => { const app = setup({ kind: 'supplier', path: `/supplier-returns/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); expect(screen.getByText('Фізичний залишок')).toBeVisible(); expect(screen.getByText('У резерві')).toBeVisible(); expect(screen.getByText('Доступно до власного резерву')).toBeVisible(); expect(app.calls.some(c => c.path.includes('excludeSupplierReturnId=' + guid(80)))).toBe(true); await number('Кількість · Кавомашина', '10'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку повернення збережено.') })
  it('blocks supplier quantity above max and excludes direct expenses from preview', async () => { const app = setup({ kind: 'supplier', path: `/supplier-returns/new?sourcePurchaseInvoiceId=${sourceId}` }); await selectGoods('11'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Недостатньо доступного товару або перевищено ліміт закупівлі.')).toBeVisible(); expect(screen.getByText(/Закупівельні витрати до неї не входять/)).toBeVisible(); expect(app.calls.some(c => c.path === '/supplier-returns' && c.method === 'POST')).toBe(false) })
  it('maps INSUFFICIENT_STOCK to a row after server rejection', async () => { const app = setup({ kind: 'supplier', path: `/supplier-returns/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); await number('Кількість · Кавомашина', '3'); app.fail('PUT', `/supplier-returns/${guid(80)}`, 409, 'INSUFFICIENT_STOCK'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Перевірте доступний залишок цього рядка. Оновіть дані.')).toBeVisible() })
  it('keeps original Version baseline across background refetch', async () => { const app = setup({ path: `/customer-returns/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); app.setVersion(2); await act(async () => { await app.services.queryClient.invalidateQueries({ queryKey: ['returns', 'customer', 'detail', guid(80)] }) }); await number('Кількість · Кавомашина', '3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findAllByText('Документ змінено іншим користувачем. Оновіть дані перед збереженням.'); expect(app.calls.find(c => c.method === 'PUT')!.body.version).toBe(1) })
  it('maps backend field validation and invalid source', async () => { const app = setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}` }); await selectGoods(); app.fail('POST', '/customer-returns', 400, undefined, undefined, { StorageLocationId: ['Склад недоступний.'] }); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Склад недоступний.')).toBeVisible() })
  it('validates only selected lines and ignores removed invalid inputs', async () => { const app = setup({ path: `/customer-returns/new?sourceSaleInvoiceId=${sourceId}` }); await selectGoods('0'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Кількість має бути більше нуля.')).toBeVisible(); await number('Кількість · Кавомашина', '1'); await userEvent.click(screen.getByRole('checkbox', { name: 'Повернути · Монтаж' })); fireEvent.change(screen.getByRole('spinbutton', { name: 'Сума компенсації · Монтаж' }), { target: { value: '-1' } }); expect(screen.getByRole('spinbutton', { name: 'Сума компенсації · Монтаж' })).toHaveValue(-1); await userEvent.click(screen.getByRole('checkbox', { name: 'Повернути · Монтаж' })); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку повернення збережено.'); expect(app.calls.find(c => c.method === 'POST' && c.path === '/customer-returns')!.body.lines).toHaveLength(1) })

  it('explicit refresh reloads stock limits together with the document after insufficient stock', async () => { const app = setup({ kind: 'supplier', path: `/supplier-returns/${guid(80)}` }); await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' }); await number('Кількість · Кавомашина', '6'); app.fail('PUT', `/supplier-returns/${guid(80)}`, 409, 'INSUFFICIENT_STOCK'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Перевірте доступний залишок цього рядка. Оновіть дані.'); app.setPhysical(4); app.clear(); await userEvent.click(screen.getByRole('button', { name: 'Оновити дані' })); await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Кількість · Кавомашина' })).toHaveValue(2)); await number('Кількість · Кавомашина', '6'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Недостатньо доступного товару або перевищено ліміт закупівлі.')).toBeVisible(); expect(app.calls.filter(c => c.method === 'PUT')).toHaveLength(1) })

})

describe.each(['customer', 'supplier'] as const)('%s return redesign', kind => {
  it('selects the return and keeps source, amounts, status and actions in the split panel', async () => {
    const config = returnConfig[kind]
    setup({ kind })
    const table = await screen.findByRole('table', { name: config.title })
    const row = within(table).getByRole('row', { name: /Чернетка/ })
    await userEvent.click(within(row).getByRole('link', { name: 'Чернетка' }))
    const panel = await screen.findByRole('region', { name: 'Вибране повернення' })
    expect(await within(panel).findByRole('button', { name: 'Зберегти' })).toBeVisible()
    expect(within(panel).getByRole('button', { name: 'Провести' })).toBeVisible()
    expect(row).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('route')).toHaveTextContent(config.root)
    expect(within(panel).getByRole('link', { name: 'Відкрити окремо' })).toHaveAttribute('href', `${config.root}/${guid(80)}`)
  })
  it('uses mobile list cards, a filter sheet and a separate detail with line cards', async () => {
    const config = returnConfig[kind]
    setViewport(390); setup({ kind })
    const list = await screen.findByRole('list', { name: config.title })
    await userEvent.click(screen.getByRole('button', { name: 'Фільтри' }))
    expect(await screen.findByRole('combobox', { name: config.source })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Показати результати' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Фільтри' })).not.toBeInTheDocument())
    await userEvent.click(within(list).getByRole('link', { name: 'Чернетка' }))
    expect(await screen.findByRole('list', { name: 'Рядки повернення' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible()
  })
})
it('does not refetch a supplier return own-reserve query after POST', async () => {
  const app = setup({ kind: 'supplier', path: '/supplier-returns/' + guid(80) })
  await screen.findByRole('spinbutton', { name: 'Кількість · Кавомашина' })
  await confirm('Провести')
  const index = app.calls.findIndex(call => call.path.endsWith('/post'))
  expect(app.calls.slice(index + 1).some(call => call.path.includes('excludeSupplierReturnId='))).toBe(false)
})
