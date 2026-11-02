import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import { ApiError } from '../../api/client'
import { operationFormSchema, stockError } from './contracts'
import type { OperationKind, StockOperation, StockRow } from './contracts'

const guid = (id: number) => `019a073c-9ee5-7000-8000-${String(id).padStart(12, '0')}`
const source = { id: guid(1), name: 'Основний склад' }; const destination = { id: guid(2), name: 'Магазин' }
const type = { id: guid(3), name: 'Роздрібна', active: true }; const wholesale = { id: guid(4), name: 'Оптова', active: true }
const reason = { id: guid(5), name: 'Пошкодження', active: true }
const product = { id: guid(6), name: 'Дриль', code: 'GOODS-001', unit: 'шт.', category: null, active: true }
const base: StockRow = { productId: product.id, productName: product.name, code: product.code, unit: 'шт.', category: null, storageLocationId: source.id, locationName: source.name, batchId: guid(7), batchNumber: 'B-OLD', purchaseDocumentDate: '2026-10-01', supplier: 'Постачальник', physicalQuantity: 10, reservedQuantity: 2, availableQuantity: 8, retailPrice: { min: 125, max: 125 }, wholesalePrice: { min: 110, max: 110 }, currentUnitCost: { min: 100, max: 100 }, categoryId: null }
const newer: StockRow = { ...base, batchId: guid(8), batchNumber: 'B-NEW', purchaseDocumentDate: '2026-10-10', physicalQuantity: 5, reservedQuantity: 0, availableQuantity: 5, retailPrice: { min: 150, max: 150 }, currentUnitCost: { min: 120, max: 120 } }
const draft: StockOperation = { document: { id: guid(30), documentDate: '2026-10-28', number: null, status: 'DRAFT', version: 1, comment: 'Перемістити товар', internalNote: null }, fromLocationId: source.id, toLocationId: destination.id, lines: [{ id: guid(40), lineNo: 1, batchId: base.batchId!, quantity: 2 }] }
const writeOff: StockOperation = { document: { ...draft.document }, storageLocationId: source.id, writeOffReasonId: reason.id, lines: [...draft.lines] }
const allowed = [...testUser.permissions, 'stock.transfer', 'stock.writeoff', 'cost.view']
const clients: ReturnType<typeof createAppServices>[] = []
function RouteProbe() { return <span data-testid="route">{useLocation().pathname}</span> }
type Input = { version?: number; documentDate: string; comment: string | null; internalNote: string | null; fromLocationId?: string; toLocationId?: string; storageLocationId?: string; writeOffReasonId?: string; lines: { id: string | null; lineNo: number; batchId: string; quantity: number }[] }
function setup({ path = '/stock', permissions = allowed, empty = false, pending = false, doc, reservations = true, categories = false }: { path?: string; permissions?: string[]; empty?: boolean; pending?: boolean; doc?: StockOperation; reservations?: boolean; categories?: boolean } = {}) {
  let current = structuredClone(doc ?? (path.includes('write-offs') ? writeOff : draft)); let exists = Boolean(doc || path.match(/\/(transfers|write-offs)\/[\da-f-]+$/)); let deleted = false
  const calls: { path: string; method: string; body?: unknown }[] = []; const failures = new Map<string, { status: number; code?: string }>()
  let release: (() => void) | undefined; const gate = pending ? new Promise<void>(resolve => { release = resolve }) : Promise.resolve()
  const rows = () => [base, newer].map(row => {
    const quantity = exists && !deleted ? current.lines.filter(l => l.batchId === row.batchId).reduce((sum, l) => sum + l.quantity, 0) : 0
    const posted = current.document.status === 'POSTED'; const physical = row.physicalQuantity - (posted ? quantity : 0); const reserved = exists ? (posted ? 0 : quantity) : row.reservedQuantity
    return { ...row, physicalQuantity: physical, reservedQuantity: reserved, availableQuantity: physical - reserved }
  })
  const aggregated = () => { const data = rows(); return { ...base, batchId: null, batchNumber: null, purchaseDocumentDate: null, supplier: null, physicalQuantity: data.reduce((a, b) => a + b.physicalQuantity, 0), reservedQuantity: data.reduce((a, b) => a + b.reservedQuantity, 0), availableQuantity: data.reduce((a, b) => a + b.availableQuantity, 0), retailPrice: { min: 125, max: 150 }, currentUnitCost: { min: 100, max: 120 } } }
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const path = parsed.pathname.replace(/^\/api/, ''); const method = options?.method ?? 'GET'; const body = options?.body ? JSON.parse(String(options.body)) : undefined
    calls.push({ path: parsed.pathname + parsed.search, method, body }); const failure = failures.get(method + ' ' + path); if (failure) return json({ code: failure.code }, failure.status)
    if (path === '/auth/me') return json({ ...testUser, permissions })
    if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/stock/lookups') return json({ locations: [source, destination], operationLocations: [source, destination], priceTypes: [type, wholesale], categories: categories ? [{ id: guid(99), name: null }] : [] })
    if (path === '/stock/overview') { await gate; const search = parsed.searchParams.get('search')?.toLowerCase(); let data = parsed.searchParams.get('byBatch') === 'true' ? rows() : [aggregated()]; if (empty || search && ![product.name, product.code].some(x => x.toLowerCase().includes(search))) data = []; if (parsed.searchParams.get('storageLocationId') === destination.id) data = []; return json(data) }
    if (path === `/stock/products/${product.id}`) return json({ product, balances: [aggregated()], batches: rows() })
    if (path === `/stock/batches/${base.batchId}`) return json({ batchId: base.batchId, batchNumber: base.batchNumber, product, purchaseDocumentId: guid(20), purchaseNumber: 'PI-00000001', purchaseDocumentType: 'PURCHASE_INVOICE', purchaseDocumentDate: base.purchaseDocumentDate, supplier: base.supplier, currencyId: '00000000-0000-0000-0000-000000000980', valuation: { unitPriceCurrency: 100, exchangeRate: 1, discountPercent: 0, unitCostUah: 100 }, prices: [{ priceTypeId: type.id, priceTypeName: type.name, unitPrice: 125 }], balances: [base] })
    if (path === '/stock/movements') return json(empty ? [] : [{ id: guid(50), documentId: guid(20), documentNumber: 'PI-00000001', documentType: 'PURCHASE_INVOICE', documentDate: '2026-10-01', storageLocationId: source.id, locationName: source.name, quantity: 10, physicalBalanceAfter: 10 }, { id: guid(51), documentId: guid(21), documentNumber: 'SI-00000001', documentType: 'SALE_INVOICE', documentDate: '2026-10-02', storageLocationId: source.id, locationName: source.name, quantity: -2, physicalBalanceAfter: 8 }])
    if (path === '/stock/reservations') return json(!reservations ? [] : ['SALE_INVOICE', 'SUPPLIER_RETURN', 'STOCK_TRANSFER', 'WRITE_OFF'].map((documentType, i) => ({ documentId: guid(60 + i), documentNumber: null, documentType, documentDate: '2026-10-28', batchId: base.batchId, storageLocationId: source.id, quantity: 1, createdByName: 'Олена Коваль' })))
    if (path === '/stock/operation-batches') { const exclude = parsed.searchParams.has('excludeDocumentId'); return json(rows().map(stock => { const own = exclude && exists && current.document.status === 'DRAFT' ? current.lines.filter(l => l.batchId === stock.batchId).reduce((sum, l) => sum + l.quantity, 0) : 0; return { stock, ownReservedQuantity: own, availableForDocument: stock.availableQuantity + own } })) }
    if (path === '/write-off-reasons') return json([reason])
    if (path.endsWith('/stock-lines')) return json(rows())
    if (path.endsWith('/cost')) return json({ totalCostUah: 200 })
    if (path === '/stock-transfers' || path === '/write-offs') {
      if (method === 'GET') return json(!exists || deleted ? [] : [current])
      current = { ...body, document: { ...current.document, id: guid(31), documentDate: body.documentDate, comment: body.comment, internalNote: body.internalNote, version: 1 }, lines: body.lines.map((l: Input['lines'][number], i: number) => ({ ...l, id: l.id ?? guid(41 + i) })) }; exists = true; return json(current, 201)
    }
    if (/^\/(stock-transfers|write-offs)\/[^/]+\/(post|unpost)$/.test(path)) { current = { ...current, document: { ...current.document, status: path.endsWith('/unpost') ? 'DRAFT' : 'POSTED', number: path.startsWith('/write-offs') ? 'WO-00000001' : 'ST-00000001', version: current.document.version + 1 } }; return json(current) }
    if (/^\/(stock-transfers|write-offs)\/[^/]+$/.test(path)) {
      if (method === 'DELETE') { deleted = true; return new Response(null, { status: 204 }) }
      if (deleted) return json({}, 404)
      if (method === 'PUT') { if (body.version !== current.document.version) return json({ code: 'VERSION_CONFLICT' }, 409); current = { ...current, ...body, document: { ...current.document, documentDate: body.documentDate, comment: body.comment, internalNote: body.internalNote, version: current.document.version + 1 }, lines: body.lines.map((l: Input['lines'][number], i: number) => ({ ...l, id: l.id ?? guid(41 + i) })) } }
      return json(current)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch); const services = createAppServices('/api'); clients.push(services); services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><RouteProbe /></MemoryRouter></AppProviders>)
  return { calls, services, release: () => release?.(), fail: (method: string, path: string, status: number, code?: string) => failures.set(method + ' ' + path, { status, code }), clearErrors: () => failures.clear(), current: () => current, setCurrent: (doc: StockOperation) => { current = doc }, rows }
}
async function choose(label: string, option: string | RegExp) { await userEvent.click(await screen.findByRole('combobox', { name: new RegExp('^' + label) })); await userEvent.click(await screen.findByRole('option', { name: option })) }
async function quantity(value: string) { const input = screen.getByRole('spinbutton', { name: 'Кількість 1' }); await userEvent.clear(input); await userEvent.type(input, value) }
async function newOperation(kind: OperationKind) { const app = setup({ path: `/stock/${kind}/new` }); await choose(kind === 'transfers' ? 'Зі складу' : 'Склад', source.name); await choose(kind === 'transfers' ? 'На склад' : 'Причина списання', kind === 'transfers' ? destination.name : reason.name); await userEvent.click(await screen.findByRole('button', { name: 'Додати рядок' })); await choose('Партія 1', new RegExp('B-OLD')); return app }
async function confirm(action: string) { await userEvent.click(await screen.findByRole('button', { name: action })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: action })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()) }
beforeEach(() => setViewport())
afterEach(() => { clients.splice(0).forEach(x => x.queryClient.clear()); vi.unstubAllGlobals() })

describe('Stock UI', () => {
  it('loads GOODS stock grouped by product and location with real quantities', async () => { setup(); const table = await screen.findByRole('table', { name: 'Складські залишки' }); expect(within(table).getByText('Дриль')).toBeVisible(); expect(within(table).getByText('15')).toBeVisible(); expect(within(table).getByText('13')).toBeVisible(); expect(screen.getByText('Товари · GOODS')).toBeVisible(); expect(screen.queryByText('Послуга')).not.toBeInTheDocument() })
  it('shows a loading skeleton then the no stock empty state', async () => { const app = setup({ pending: true, empty: true }); expect(await screen.findByRole('status', { name: 'Завантаження складу' })).toBeVisible(); await act(async () => app.release()); expect(await screen.findByText('Залишків немає')).toBeVisible() })
  it('debounces server search', async () => { const app = setup(); await screen.findByRole('table', { name: 'Складські залишки' }); await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'невідомий'); expect(await screen.findByText('Залишків немає')).toBeVisible(); expect(app.calls.filter(c => c.path.includes('search=')).length).toBe(1) })
  it('sends location and quantity filters without making a category directory', async () => { const app = setup(); await screen.findByRole('table', { name: 'Складські залишки' }); await userEvent.click(screen.getByLabelText('Тільки з залишком')); await userEvent.click(screen.getByLabelText('Тільки з резервом')); await choose('Місце зберігання', destination.name); expect(await screen.findByText('Залишків немає')).toBeVisible(); expect(app.calls.some(c => c.path.includes('withStock=true') && c.path.includes('withReservations=true') && c.path.includes(destination.id))).toBe(true); expect(screen.getByRole('combobox', { name: 'Категорія' })).toHaveAttribute('aria-disabled', 'true') })
  it('switches to a separate row per batch', async () => { const app = setup(); await screen.findByRole('table', { name: 'Складські залишки' }); await userEvent.click(screen.getByRole('button', { name: 'По партіях' })); expect(await screen.findByRole('link', { name: 'B-OLD' })).toBeVisible(); expect(screen.getByRole('link', { name: 'B-NEW' })).toBeVisible(); expect(app.calls.some(c => c.path.includes('byBatch=true'))).toBe(true) })
  it('filters existing category references without showing raw IDs or inventing directory names', async () => { const app = setup({ categories: true }); await screen.findByRole('table', { name: 'Складські залишки' }); await choose('Категорія', 'Категорія без назви 1'); expect(app.calls.some(c => c.path.includes(`categoryId=${guid(99)}`))).toBe(true); expect(screen.queryByText(guid(99))).not.toBeInTheDocument() })
  it('hides cost without permission even if a response contains it', async () => { setup({ permissions: ['stock.view'] }); await screen.findByRole('table', { name: 'Складські залишки' }); expect(screen.queryByRole('columnheader', { name: 'Собівартість' })).not.toBeInTheDocument(); expect(screen.queryByRole('link', { name: 'Переміщення' })).not.toBeInTheDocument() })
  it('shows cost ranges only with cost.view and uses explicit price types', async () => { const app = setup(); await screen.findByRole('columnheader', { name: 'Собівартість' }); expect(screen.getByText('100,00–120,00')).toBeVisible(); await choose('Тип роздрібної ціни', type.name); await choose('Тип оптової ціни', wholesale.name); expect(app.calls.some(c => c.path.includes(`retailPriceTypeId=${type.id}`) && c.path.includes(`wholesalePriceTypeId=${wholesale.id}`))).toBe(true) })
  it('shows the product stock header and batch tab', async () => { setup({ path: `/stock/products/${product.id}` }); expect(await screen.findByRole('heading', { name: product.name })).toBeVisible(); expect(screen.getByText('Активний')).toBeVisible(); await userEvent.click(screen.getByRole('tab', { name: 'Партії' })); expect(screen.getAllByRole('link', { name: 'B-OLD' }).length).toBeGreaterThan(0) })
  it('shows batch origin, prices and balances without Batch active or New Batch', async () => { setup({ path: `/stock/batches/${base.batchId}` }); expect(await screen.findByRole('heading', { name: 'B-OLD' })).toBeVisible(); expect(screen.getAllByText('Постачальник').length).toBeGreaterThan(0); expect(screen.getByRole('link', { name: 'PI-00000001' })).toBeVisible(); expect(screen.queryByText('Активний')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: /Нова партія/ })).not.toBeInTheDocument() })
  it('does not show purchase valuation or current cost without cost.view', async () => { setup({ path: `/stock/batches/${base.batchId}`, permissions: ['stock.view'] }); await screen.findByRole('heading', { name: 'B-OLD' }); expect(screen.queryByText('Поточна собівартість')).not.toBeInTheDocument(); expect(screen.queryByText('Закупівельна ціна за одиницю')).not.toBeInTheDocument() })
  it('loads document movements and backend running balance', async () => { setup({ path: `/stock/products/${product.id}` }); await userEvent.click(await screen.findByRole('tab', { name: 'Рух товару' })); const table = await screen.findByRole('table', { name: 'Рух товару' }); expect(within(table).getByText('+10')).toBeVisible(); expect(within(table).getByText('-2')).toBeVisible(); expect(within(table).getByText('8')).toBeVisible(); expect(within(table).getByText('SI-00000001')).toBeVisible() })
  it('opens reserve drilldown with sale, supplier return, transfer and writeoff sources', async () => { setup(); await userEvent.click(await screen.findByRole('button', { name: 'Резерв Дриль' })); const dialog = await screen.findByRole('dialog'); for (const label of ['Продаж', 'Повернення постачальнику', 'Переміщення', 'Списання']) expect(await within(dialog).findByText(new RegExp(label))).toBeVisible(); expect(dialog.textContent?.match(/DRAFT/g)).toHaveLength(4); expect(within(dialog).getAllByText(/Олена Коваль/)).toHaveLength(4) })
  it('shows no reservations and no movements empty states', async () => { setup({ reservations: false }); await userEvent.click(await screen.findByRole('button', { name: 'Резерв Дриль' })); expect(await screen.findByText('Активних резервів немає')).toBeVisible() })
  it('shows an empty movement history without calculated fake movements', async () => { setup({ path: `/stock/products/${product.id}`, empty: true }); await userEvent.click(await screen.findByRole('tab', { name: 'Рух товару' })); expect(await screen.findByText('Рухів ще немає')).toBeVisible(); expect(screen.queryByRole('table', { name: 'Рух товару' })).not.toBeInTheDocument() })
  it('renders stock as mobile cards', async () => { setViewport(390); setup(); expect(await screen.findByRole('list', { name: 'Складські залишки' })).toBeVisible(); expect(screen.queryByRole('table', { name: 'Складські залишки' })).not.toBeInTheDocument(); expect(screen.getByText('Фізично: 15')).toBeVisible(); expect(screen.getByText('Доступно: 13')).toBeVisible() })
  it('keeps transfer navigation available without granting stock.view', async () => { const app = setup({ permissions: ['stock.transfer'] }); expect(await screen.findByRole('link', { name: 'Переміщення' })).toBeVisible(); expect(app.calls.some(c => c.path.startsWith('/api/stock/overview'))).toBe(false); expect(screen.queryByRole('link', { name: 'Списання' })).not.toBeInTheDocument() })
  it('shows a forbidden state when stock API returns 403', async () => { const app = setup({ pending: true }); app.fail('GET', '/stock/overview', 403); await act(async () => app.release()); await act(async () => { await app.services.queryClient.invalidateQueries({ queryKey: ['stock', 'overview'] }) }); expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible() })
})
describe('Stock operation UI', () => {
  it('creates a transfer through the existing API without product, stock or cost writes', async () => { const app = await newOperation('transfers'); await quantity('3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку збережено.'); await waitFor(() => expect(screen.getByTestId('route')).toHaveTextContent(`/stock/transfers/${guid(31)}`)); const body = app.calls.find(c => c.method === 'POST' && c.path === '/api/stock-transfers')!.body as Input; expect(body.fromLocationId).toBe(source.id); expect(body.toLocationId).toBe(destination.id); expect(body.lines[0]).toMatchObject({ batchId: base.batchId, quantity: 3 }); expect(body.lines[0]).not.toHaveProperty('productId'); expect(app.calls.some(c => /stock_movements|settlements|money/.test(c.path) && c.method !== 'GET')).toBe(false) })
  it('shows the own transfer DRAFT reserve without blocking editing', async () => { const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft }); expect(await screen.findByText('Власний резерв: 2')).toBeVisible(); await quantity('9'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Чернетку збережено.')).toBeVisible(); expect(app.calls.some(c => c.path.includes(`excludeDocumentId=${draft.document.id}`))).toBe(true); expect((app.calls.find(c => c.method === 'PUT')!.body as Input).lines[0].id).toBe(draft.lines[0].id) })
  it('posts transfer after confirmation and displays read-only quantities', async () => { const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft }); await confirm('Провести'); expect(await screen.findByRole('heading', { name: 'ST-00000001' })).toBeVisible(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument(); expect(app.rows()[0].physicalQuantity).toBe(8) })
  it('unposts transfer and restores draft reserve', async () => { setup({ path: `/stock/transfers/${draft.document.id}`, doc: { ...draft, document: { ...draft.document, status: 'POSTED', number: 'ST-00000001' } } }); await confirm('Розпровести'); expect(await screen.findByRole('button', { name: 'Зберегти' })).toBeVisible(); expect(await screen.findByText('Власний резерв: 2')).toBeVisible() })
  it('shows insufficient destination stock on transfer UNPOST without changing the form to DRAFT', async () => { const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: { ...draft, document: { ...draft.document, status: 'POSTED', number: 'ST-00000001' } } }); app.fail('POST', `/stock-transfers/${draft.document.id}/unpost`, 409, 'INSUFFICIENT_STOCK'); await confirm('Розпровести'); expect(screen.getAllByText('Недостатньо доступного залишку.').length).toBeGreaterThan(0); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument() })
  it('creates a writeoff with an existing reason', async () => { const app = await newOperation('write-offs'); await quantity('2'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку збережено.'); const input = app.calls.find(c => c.path === '/api/write-offs' && c.method === 'POST')!.body as Input; expect(input.writeOffReasonId).toBe(reason.id); expect(input.storageLocationId).toBe(source.id); expect(input).not.toHaveProperty('totalCostUah') })
  it('shows the own writeoff reserve and posts using only the existing document endpoint', async () => { const app = setup({ path: `/stock/write-offs/${draft.document.id}`, doc: writeOff }); expect(await screen.findByText('Власний резерв: 2')).toBeVisible(); await confirm('Провести'); expect(await screen.findByRole('heading', { name: 'WO-00000001' })).toBeVisible(); expect(app.calls.filter(c => c.method === 'POST').map(c => c.path)).toEqual([`/api/write-offs/${draft.document.id}/post`]); expect(app.rows()[0].physicalQuantity).toBe(8) })
  it('loads calculated writeoff cost with cost.view', async () => { setup({ path: `/stock/write-offs/${draft.document.id}`, doc: writeOff }); expect(await screen.findByText('200,00 UAH')).toBeVisible() })
  it('does not request writeoff cost without cost.view', async () => { const app = setup({ path: `/stock/write-offs/${draft.document.id}`, doc: writeOff, permissions: ['stock.writeoff'] }); await screen.findByRole('button', { name: 'Зберегти' }); expect(app.calls.some(c => c.path.endsWith('/cost'))).toBe(false); expect(screen.queryByText('Поточна собівартість списання')).not.toBeInTheDocument() })
  it('rejects equal transfer locations and missing required fields', async () => { const app = setup({ path: '/stock/transfers/new' }); await userEvent.click(await screen.findByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Виберіть склад.')).toBeVisible(); await choose('Зі складу', source.name); await choose('На склад', source.name); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Склади джерела та призначення мають відрізнятися.')).toBeVisible(); expect(app.calls.some(c => c.method === 'POST')).toBe(false) })
  it('blocks quantities beyond available and reports backend stock conflicts by line', async () => { const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft }); await screen.findByText('Власний резерв: 2'); await quantity('11'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Недостатньо доступного залишку.')).toBeVisible(); expect(app.calls.some(c => c.method === 'PUT')).toBe(false); app.fail('PUT', `/stock-transfers/${draft.document.id}`, 409, 'INSUFFICIENT_STOCK'); await quantity('3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await waitFor(() => expect(screen.getAllByText('Недостатньо доступного залишку.').length).toBeGreaterThan(1)); expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveAttribute('aria-invalid', 'true') })
  it('keeps baseline version across background updates and offers explicit conflict refresh', async () => { const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft }); await screen.findByText('Власний резерв: 2'); await quantity('3'); const newer = { ...draft, document: { ...draft.document, version: 2 } }; app.setCurrent(newer); await act(async () => { app.services.queryClient.setQueryData(['stock', 'transfers', draft.document.id], newer) }); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findAllByText('Документ був змінений іншим користувачем. Оновіть дані.')).not.toHaveLength(0); expect((app.calls.find(c => c.method === 'PUT')!.body as Input).version).toBe(1); await userEvent.click(screen.getByRole('button', { name: 'Оновити дані' })); await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(2)) })
  it('logically deletes DRAFT through DELETE only', async () => { const app = setup({ path: `/stock/write-offs/${draft.document.id}`, doc: writeOff }); await confirm('Видалити'); expect(await screen.findByText('Документів ще немає')).toBeVisible(); expect(app.calls.filter(c => c.method === 'DELETE').map(c => c.path)).toEqual([`/api/write-offs/${draft.document.id}`]) })
  it('renders transfer lines as mobile cards', async () => { setViewport(390); setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft }); expect(await screen.findByRole('list', { name: 'Рядки складського документа' })).toBeVisible(); expect(screen.queryByRole('table', { name: 'Рядки складського документа' })).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Провести' })).toBeVisible() })
  it('validates duplicate batches and positive quantity', () => { const values = { documentDate: '2026-10-28', source: source.id, destination: destination.id, reason: '', comment: '', internalNote: '', lines: [{ lineId: null, batchId: base.batchId, quantity: 1 }, { lineId: null, batchId: base.batchId, quantity: 1 }] }; expect(operationFormSchema.safeParse(values).success).toBe(false); expect(operationFormSchema.safeParse({ ...values, lines: [{ ...values.lines[0], quantity: 0 }] }).success).toBe(false) })
  it('maps the documented historical, dependency and duplicate errors', () => { expect(stockError(new ApiError(409, '', 'HISTORICAL_NEGATIVE_STOCK'))).toContain('від’ємного'); expect(stockError(new ApiError(409, '', 'DOCUMENT_HAS_DEPENDENCIES'))).toContain('залежні'); expect(stockError(new ApiError(400, '', 'DUPLICATE_BATCH_LINE'))).toContain('повторюватися') })
})

describe('Stock redesign', () => {
  it('selects a stock row, opens the right panel and keeps /stock', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Складські залишки' })
    const row = within(table).getByRole('row', { name: /Дриль/ })
    await userEvent.click(within(row).getByRole('link', { name: product.name }))
    expect(row).toHaveAttribute('aria-selected', 'true')
    const panel = await screen.findByRole('region', { name: 'Вибрана складська позиція' })
    expect(await within(panel).findByRole('heading', { name: product.name })).toBeVisible()
    expect(screen.getByTestId('route')).toHaveTextContent(/^\/stock$/)
    expect(within(panel).getByRole('link', { name: 'Відкрити окремо' })).toHaveAttribute('href', '/stock/products/' + product.id)
    await userEvent.click(within(panel).getByRole('tab', { name: 'Резерви' }))
    expect(await within(panel).findAllByText(/Резерв: 1/)).toHaveLength(4)
    await userEvent.click(screen.getByRole('button', { name: 'Закрити складську позицію' }))
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Вибрана складська позиція' })).not.toBeInTheDocument())
  })
  it('opens Batch detail in a panel without Active or New Batch', async () => {
    setup({ permissions: ['stock.view'] })
    await userEvent.click(await screen.findByRole('button', { name: 'По партіях' }))
    await userEvent.click(await screen.findByRole('link', { name: 'B-OLD' }))
    const panel = await screen.findByRole('region', { name: 'Вибрана складська позиція' })
    expect(await within(panel).findByRole('heading', { name: 'B-OLD' })).toBeVisible()
    expect(within(panel).queryByText('Активний')).not.toBeInTheDocument()
    expect(within(panel).queryByText('Поточна собівартість')).not.toBeInTheDocument()
    expect(within(panel).queryByRole('button', { name: /Нова партія|Створити партію/ })).not.toBeInTheDocument()
  })
  it('uses mobile stock cards and a bottom filter drawer', async () => {
    setViewport(390)
    setup()
    expect(await screen.findByRole('list', { name: 'Складські залишки' })).toBeVisible()
    expect(screen.queryByRole('table', { name: 'Складські залишки' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Фільтри' }))
    expect(await screen.findByRole('combobox', { name: 'Місце зберігання' })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Показати результати' }))
    expect(await screen.findByRole('navigation', { name: 'Мобільна навігація' })).toBeVisible()
  })
  it('keeps writeoff mobile actions and hides current cost without permission', async () => {
    setViewport(390)
    const app = setup({ path: `/stock/write-offs/${draft.document.id}`, doc: writeOff, permissions: ['stock.writeoff'] })
    expect(await screen.findByRole('list', { name: 'Рядки складського документа' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Провести' })).toBeVisible()
    expect(screen.getByText(/Рядків: 1/)).toBeVisible()
    expect(app.calls.some(call => call.path.endsWith('/cost'))).toBe(false)
  })
  it('does not refetch DRAFT-only own candidates after posting an operation', async () => {
    const app = setup({ path: `/stock/transfers/${draft.document.id}`, doc: draft })
    await screen.findByText('Власний резерв: 2')
    await confirm('Провести')
    const index = app.calls.findIndex(call => call.path.endsWith('/post'))
    expect(app.calls.slice(index + 1).some(call => call.path.includes('excludeDocumentId='))).toBe(false)
  })
})

describe('Stock movement UI consistency', () => {
  it('shows direction by signs without treating an outgoing movement as an error', async () => {
    setup({ path: '/stock/products/' + product.id })
    await userEvent.click(await screen.findByRole('tab', { name: 'Рух товару' }))
    const table = await screen.findByRole('table', { name: 'Рух товару' })
    const incoming = within(table).getByText('+10'), outgoing = within(table).getByText('-2')
    expect(getComputedStyle(incoming).color).toBe(getComputedStyle(outgoing).color)
    expect(incoming.closest('td')).toHaveStyle({ textAlign: 'right' })
  })
})

it('keeps the closed operation batch selector compact and preserves the product and capacity in options', async () => {
  setup({ path: '/stock/transfers/' + draft.document.id })
  const selector = await screen.findByRole('combobox', { name: 'Партія 1' })
  await waitFor(() => expect(selector).toBeEnabled())
  expect(selector).toHaveTextContent(/^B-OLD$/)
  await userEvent.click(selector)
  const option = await screen.findByRole('option', { name: /B-OLD/ })
  expect(option).toHaveTextContent(product.name)
  expect(option).toHaveTextContent('Доступно для документа:')
})
