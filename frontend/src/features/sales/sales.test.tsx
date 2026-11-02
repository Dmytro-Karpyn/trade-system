import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import type { Counterparty } from '../counterparties/contracts'
import type { Product } from '../products/contracts'
import { allocate, saleError, saleFormSchema, totals, uahId } from './contracts'
import type { Sale, SaleInput } from './contracts'
import { saleKeys } from './queries'
import { ApiError } from '../../api/client'

const guid = (id: number) => `019a073c-9ee5-7000-8000-${String(id).padStart(12, '0')}`
const type = { id: guid(10), organizationId: testUser.organizationId, name: 'Роздрібна', defaultMarkupPercent: 25, active: true, sortOrder: 0 }
const customer: Counterparty = { id: guid(2), organizationId: testUser.organizationId, name: 'ТОВ Покупець', legalType: 'COMPANY', taxId: null, registrationCode: null, phone: null, email: null, address: null, defaultPriceTypeId: type.id, isCustomer: true, isSupplier: false, isEmployee: false, isSystem: false, active: true, note: null }
const retail: Counterparty = { ...customer, id: guid(3), name: 'Роздрібний покупець', isSystem: true }
const supplier: Counterparty = { ...customer, id: guid(8), name: 'Лише постачальник', isCustomer: false, isSupplier: true }
const inactive: Counterparty = { ...customer, id: guid(9), name: 'Неактивний покупець', active: false }
const location = { id: guid(4), name: 'Основний склад' }
const goods: Product = { id: guid(5), organizationId: testUser.organizationId, name: 'Дриль', code: 'GOODS-001', barcode: '482001', itemType: 'GOODS', unitId: guid(1), categoryId: null, attributes: null, description: null, active: true }
const service: Product = { ...goods, id: guid(6), itemType: 'SERVICE', name: 'Монтаж', code: 'SERVICE-001', barcode: null }
const batches = [{ batchId: guid(20), productId: goods.id, storageLocationId: location.id, physicalQuantity: 5, reservedQuantity: 0, availableQuantity: 5, ownReservedQuantity: 0, availableForDocument: null, purchaseDocumentDate: '2026-10-01', batchNumber: 'B-OLD' }, { batchId: guid(21), productId: goods.id, storageLocationId: location.id, physicalQuantity: 5, reservedQuantity: 0, availableQuantity: 5, ownReservedQuantity: 0, availableForDocument: null, purchaseDocumentDate: '2026-10-10', batchNumber: 'B-NEW' }]
const draft: Sale = { document: { id: guid(30), number: null, documentDate: '2026-10-28', status: 'DRAFT', comment: 'Продаж інструменту', internalNote: null, version: 1, postedAt: null }, counterpartyId: customer.id, storageLocationId: location.id, priceTypeId: type.id, discountPercent: 10, lines: [{ id: guid(40), lineNo: 1, productId: goods.id, batchId: batches[0].batchId, quantity: 2, recommendedUnitPrice: 100, unitPrice: 100, removedAt: null }], products: [{ productId: goods.id, name: goods.name, code: goods.code, itemType: goods.itemType }, { productId: service.id, name: service.name, code: service.code, itemType: service.itemType }], batches: batches.map(batch => ({ batchId: batch.batchId, batchNumber: batch.batchNumber, purchaseDocumentDate: batch.purchaseDocumentDate })), postedByName: null, counterpartyName: customer.name }
const posted: Sale = { ...draft, document: { ...draft.document, status: 'POSTED', number: 'SI-00000001', postedAt: '2026-10-28T10:00:00Z' }, postedByName: testUser.fullName }
const permissions = [...testUser.permissions, 'sale.create', 'sale.post', 'sale.unpost', 'sale.delete', 'sale.change_price', 'cost.view']
const cleanupServices: ReturnType<typeof createAppServices>[] = []
function RouteProbe() { return <span data-testid="route">{useLocation().pathname}</span> }
interface SetupOptions { path?: string; sale?: Sale; empty?: boolean; retail?: boolean; permissions?: string[]; pending?: boolean; postPending?: boolean; reverseBatches?: boolean }
function setup({ path = '/sales', sale = draft, empty = false, retail: includeRetail = false, permissions: allowed = permissions, pending = false, postPending = false, reverseBatches = false }: SetupOptions = {}) {
  let document = structuredClone(sale); let exists = !path.endsWith('/new') && !empty; let deleted = false
  const prices: Record<string, number> = { [batches[0].batchId]: 100, [batches[1].batchId]: 120, [service.id]: 50 }
  const calls: { path: string; method: string; body?: unknown }[] = []
  const errors = new Map<string, { status: number; body: unknown }>()
  let release: (() => void) | undefined; const gate = pending ? new Promise<void>(resolve => { release = resolve }) : Promise.resolve()
  let releasePost: (() => void) | undefined; const postGate = postPending ? new Promise<void>(resolve => { releasePost = resolve }) : Promise.resolve()
  const available = (exclude = false) => batches.map(batch => {
    const quantity = exists && !deleted ? document.lines.filter(line => line.batchId === batch.batchId && !line.removedAt).reduce((sum, line) => sum + line.quantity, 0) : 0
    const own = document.document.status === 'DRAFT' ? quantity : 0
    const physical = batch.physicalQuantity - (document.document.status === 'POSTED' ? quantity : 0)
    return { ...batch, physicalQuantity: physical, reservedQuantity: own, availableQuantity: physical - own, ownReservedQuantity: exclude ? own : 0, availableForDocument: exclude ? physical : null }
  }).filter(batch => (batch.availableForDocument ?? batch.availableQuantity) > 0)
  const update = (input: SaleInput, creating: boolean) => {
    const previous = document
    const id = creating ? guid(31) : previous.document.id
    document = { ...previous, ...input, document: { ...previous.document, id, documentDate: input.documentDate, comment: input.comment, internalNote: input.internalNote, version: creating ? 1 : previous.document.version + 1 }, lines: input.lines.map((line, index) => {
      const old = !creating && previous.priceTypeId === input.priceTypeId ? previous.lines.find(old => old.id === line.id && old.productId === line.productId && old.batchId === line.batchId) : undefined
      const recommended = old?.recommendedUnitPrice ?? (input.priceTypeId ? prices[line.batchId ?? line.productId] : 0)
      return { ...line, id: line.id ?? guid(50 + index), recommendedUnitPrice: recommended, unitPrice: line.unitPrice ?? old?.unitPrice ?? recommended, removedAt: null }
    }) }
    exists = true
    return document
  }
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const path = parsed.pathname.replace(/^\/api/, ''); const method = options?.method ?? 'GET'; const body = options?.body ? JSON.parse(String(options.body)) : undefined
    calls.push({ path: parsed.pathname + parsed.search, method, body })
    const error = errors.get(method + ' ' + path); if (error) return json(error.body, error.status)
    if (path === '/auth/me') return json({ ...testUser, permissions: allowed })
    if (path === '/customer-returns/overview') return json([])
    if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/health') return json({ status: 'Healthy' })
    if (path === '/counterparties') return json([customer, supplier, inactive, ...(includeRetail ? [retail] : [])].filter(item => (!parsed.searchParams.has('isCustomer') || item.isCustomer) && (!parsed.searchParams.has('active') || item.active)))
    if (path === '/price-types') return json([type])
    if (path === '/sales/lookups') return json({ locations: [location] })
    if (path === '/products') { const search = parsed.searchParams.get('search')?.toLowerCase(); return json([goods, service].filter(product => (!parsed.searchParams.has('itemType') || product.itemType === parsed.searchParams.get('itemType')) && (!search || [product.name, product.code, product.barcode].some(value => value?.toLowerCase().includes(search))))) }
    if (path === '/sales/price-options') { if (parsed.searchParams.get('productId') === service.id && parsed.searchParams.has('priceTypeId') && Number.isNaN(prices[service.id])) return json([]); const productId = parsed.searchParams.get('productId'); const priced = parsed.searchParams.has('priceTypeId'); return json(productId === service.id ? [{ batchId: null, unitPrice: priced ? prices[service.id] : 0 }] : batches.map(batch => ({ batchId: batch.batchId, unitPrice: priced ? prices[batch.batchId] : 0 }))) }
    if (path === '/stock/available-batches') { const rows = parsed.searchParams.get('productId') === service.id ? [] : available(parsed.searchParams.has('excludeSaleId')); return json(reverseBatches ? [...rows].reverse() : rows) }
    if (path === '/settlements/balances') return json([{ counterpartyId: customer.id, currencyId: uahId, balance: 250 }])
    if (path === `/sales/${document.document.id}/cost`) return json({ currentCost: 120 })
    if (path === `/sales/${document.document.id}/profit`) return json({ netSale: 180, grossProfit: 60, margin: 33.3333 })
    if (path === '/sales' && method === 'GET') { await gate; const params = parsed.searchParams; const search = params.get('search')?.toLowerCase(); const hidden = !exists || deleted || search && ![document.document.number, document.document.comment, customer.name].some(value => value?.toLowerCase().includes(search)) || params.has('status') && params.get('status') !== document.document.status; return json(hidden ? [] : [{ id: document.document.id, number: document.document.number, documentDate: document.document.documentDate, counterpartyId: document.counterpartyId, counterpartyName: customer.name, storageLocationId: document.storageLocationId, priceTypeId: document.priceTypeId, priceTypeName: type.name, totalUah: totals(document.lines, document.discountPercent).total, discountPercent: document.discountPercent, status: document.document.status }]) }
    if (path === '/sales' && method === 'POST') return json(update(body as SaleInput, true), 201)
    if (path === `/sales/${document.document.id}/post`) { await postGate; document = { ...document, document: { ...document.document, status: 'POSTED', number: 'SI-00000001', postedAt: '2026-10-28T10:00:00Z', version: document.document.version + 1 } }; return json(document) }
    if (path === `/sales/${document.document.id}/unpost`) { document = { ...document, document: { ...document.document, status: 'DRAFT', postedAt: null, version: document.document.version + 1 } }; return json(document) }
    if (path === `/sales/${document.document.id}`) {
      if (method === 'DELETE') { deleted = true; return new Response(null, { status: 204 }) }
      if (deleted) return json({}, 404)
      if (method === 'PUT') { if (body.version !== document.document.version) return json({ code: 'VERSION_CONFLICT' }, 409); return json(update(body as SaleInput, false)) }
      return json(document)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch)
  const services = createAppServices('/api'); cleanupServices.push(services)
  services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><RouteProbe /></MemoryRouter></AppProviders>)
  return { calls, services, release: () => release?.(), releasePost: () => releasePost?.(), error: (method: string, path: string, status: number, body: unknown) => errors.set(method + ' ' + path, { status, body }), clearErrors: () => errors.clear(), setPrices: (key: string, price: number) => { prices[key] = price }, setDocument: (sale: Sale) => { document = sale }, stock: () => available(true) }
}
async function choose(label: string, option: string | RegExp) { await userEvent.click(await screen.findByRole('combobox', { name: new RegExp('^' + label) })); await userEvent.click(await screen.findByRole('option', { name: option })) }
async function number(label: string, value: string) { const input = screen.getByRole('spinbutton', { name: label }); await userEvent.clear(input); if (value) await userEvent.type(input, value) }
async function openDraft(options: SetupOptions = {}) { const app = setup({ path: `/sales/${draft.document.id}`, ...options }); await screen.findByRole('button', { name: 'Зберегти' }); await waitFor(() => expect(screen.getByRole('button', { name: 'Зберегти' })).toBeEnabled()); return app }
async function newSale(options: SetupOptions = {}) { const app = setup({ path: '/sales/new', ...options }); await choose('Покупець', customer.name); await choose('Склад', location.name); await choose('Тип ціни', type.name); return app }
async function addProduct(name: string, index = 1) { await userEvent.click(screen.getByRole('button', { name: 'Додати рядок' })); await userEvent.click(screen.getByRole('combobox', { name: `Номенклатура ${index}` })); await userEvent.click(await screen.findByRole('option', { name: new RegExp(name) })) }
async function postSale() { await userEvent.click(screen.getByRole('button', { name: 'Провести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); await screen.findByRole('button', { name: 'Розпровести' }) }
beforeEach(() => setViewport())
afterEach(() => { cleanupServices.splice(0).forEach(service => service.queryClient.clear()); vi.unstubAllGlobals() })

describe('Sale UI', () => {
  it('loads the list with derived totals and no internal IDs', async () => { setup(); const table = await screen.findByRole('table', { name: 'Продажі' }); expect(within(table).getByText(customer.name)).toBeVisible(); expect(within(table).getByText('180,00')).toBeVisible(); expect(screen.queryByText(draft.document.id)).not.toBeInTheDocument() })
  it('shows list loading and empty states', async () => { const app = setup({ empty: true, pending: true }); expect(await screen.findByRole('status', { name: 'Завантаження продажів' })).toBeVisible(); await act(async () => app.release()); expect(await screen.findByRole('heading', { name: 'Продажів ще немає' })).toBeVisible() })
  it('sends combined filters and date boundaries to the API', async () => { const app = setup(); await screen.findByRole('table', { name: 'Продажі' }); fireEvent.change(screen.getByLabelText('Період від'), { target: { value: '2026-10-01' } }); fireEvent.change(screen.getByLabelText('Період до'), { target: { value: '2026-10-31' } }); await choose('Покупець', customer.name); await choose('Склад', location.name); await choose('Тип ціни', type.name); await choose('Статус', 'Чернетки'); expect(app.calls.some(call => ['from', 'to', 'counterpartyId', 'storageLocationId', 'priceTypeId', 'status'].every(key => new URL(call.path, 'http://localhost').searchParams.has(key)))).toBe(true) })
  it('debounces search', async () => { const app = setup(); await screen.findByRole('table', { name: 'Продажі' }); await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'невідомий'); expect(await screen.findByRole('heading', { name: 'Нічого не знайдено' })).toBeVisible(); expect(app.calls.filter(call => call.path.includes('search='))).toHaveLength(1) })
  it('opens the new sale form with UAH and active customer options', async () => { setup({ path: '/sales/new' }); expect(await screen.findByRole('heading', { name: 'Новий продаж' })).toBeVisible(); await userEvent.click(screen.getByRole('combobox', { name: /^Покупець/ })); expect(await screen.findByRole('option', { name: customer.name })).toBeVisible(); expect(screen.queryByRole('option', { name: supplier.name })).not.toBeInTheDocument(); expect(screen.queryByRole('option', { name: inactive.name })).not.toBeInTheDocument() })
  it('defaults to the existing active system retail customer without creating one', async () => { const app = setup({ path: '/sales/new', retail: true }); await waitFor(() => expect(screen.getByRole('combobox', { name: /^Покупець/ })).toHaveTextContent(retail.name)); expect(screen.getByText('Системний покупець')).toBeVisible(); expect(app.calls.some(call => call.path.startsWith('/api/settlements/'))).toBe(false); expect(app.calls.some(call => call.path === '/api/counterparties' && call.method === 'POST')).toBe(false) })
  it('loads the real UAH customer balance only for a non-system customer', async () => { await openDraft(); expect(await screen.findByText('250,00 UAH')).toBeVisible() })
  it('offers GOODS and SERVICE together', async () => { await newSale(); await userEvent.click(screen.getByRole('button', { name: 'Додати рядок' })); await userEvent.click(screen.getByRole('combobox', { name: 'Номенклатура 1' })); expect(await screen.findByRole('option', { name: /Дриль/ })).toBeVisible(); expect(screen.getByRole('option', { name: /Монтаж/ })).toBeVisible() })
  it('loads available batches and sorts oldest first', async () => { const app = await openDraft({ reverseBatches: true }); await userEvent.click(screen.getByRole('combobox', { name: 'Партія 1' })); const options = await screen.findAllByRole('option'); const labels = options.map(option => option.textContent); expect(labels.findIndex(label => label?.includes('B-OLD'))).toBeLessThan(labels.findIndex(label => label?.includes('B-NEW'))); expect(app.calls.some(call => call.path.includes(`/stock/available-batches?productId=${goods.id}&storageLocationId=${location.id}&excludeSaleId=${draft.document.id}`))).toBe(true) })
  it('displays physical, reserved and available including own reserve capacity', async () => { await openDraft(); expect(await screen.findByText('Фізично: 5 · Зарезервовано: 2 · Доступно: 3')).toBeVisible(); expect(screen.getByText('Для цього документа доступно: 5 (власний резерв: 2)')).toBeVisible() })
  it('requires a batch for GOODS', async () => { const app = await newSale(); await addProduct(goods.name); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Для товару виберіть партію.')).toBeVisible(); expect(app.calls.some(call => call.method === 'POST' && call.path === '/api/sales')).toBe(false) })
  it('does not show a batch or request stock for SERVICE', async () => { const app = await newSale(); await addProduct(service.name); await waitFor(() => expect(screen.getByRole('button', { name: 'Зберегти' })).toBeEnabled()); expect(screen.queryByRole('combobox', { name: 'Партія 1' })).not.toBeInTheDocument(); expect(screen.getByText('Послуга не має партій та складських резервів.')).toBeVisible(); expect(app.calls.some(call => call.path.includes('/stock/available-batches') && call.path.includes(service.id))).toBe(false) })
  it('rejects a SERVICE batch in form validation', () => { expect(saleFormSchema.safeParse({ documentDate: '2026-10-28', counterpartyId: customer.id, storageLocationId: location.id, priceTypeId: type.id, discountPercent: 0, comment: '', internalNote: '', lines: [{ lineId: null, productId: service.id, itemType: 'SERVICE', batchId: batches[0].batchId, quantity: 1, unitPrice: 50 }] }).success).toBe(false) })
  it('recommends FIFO batches across multiple lines without posting', async () => { const app = await newSale({ reverseBatches: true }); await userEvent.click(screen.getByRole('button', { name: 'Підібрати партії' })); const dialog = await screen.findByRole('dialog'); await userEvent.click(within(dialog).getByRole('combobox', { name: 'Товар для підбору' })); await userEvent.click(await screen.findByRole('option', { name: /Дриль/ })); const qty = within(dialog).getByRole('spinbutton', { name: 'Потрібна кількість' }); await userEvent.clear(qty); await userEvent.type(qty, '8'); const apply = within(dialog).getByRole('button', { name: 'Застосувати підбір' }); await waitFor(() => expect(apply).toBeEnabled()); await userEvent.click(apply); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(5); expect(screen.getByRole('spinbutton', { name: 'Кількість 2' })).toHaveValue(3); expect(screen.getByRole('combobox', { name: 'Партія 1' })).toHaveTextContent('B-OLD'); expect(app.calls.some(call => call.method === 'POST' && call.path === '/api/sales')).toBe(false) })
  it('does not suggest quantities beyond available capacity', () => { const result = allocate([...batches].reverse(), 12); expect(result.lines.map(line => line.batch.batchNumber)).toEqual(['B-OLD', 'B-NEW']); expect(result.shortage).toBe(2) })
  it('shows a backend recommended price for GOODS', async () => { await openDraft(); expect(screen.getByLabelText('Рекомендована ціна 1')).toHaveTextContent('100,00') })
  it('defaults a new SERVICE actual price through the backend when omitted', async () => { const app = await newSale(); await addProduct(service.name); await waitFor(() => expect(screen.getByLabelText('Рекомендована ціна 1')).toHaveTextContent('50,00')); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку продажу збережено.'); const input = app.calls.find(call => call.method === 'POST' && call.path === '/api/sales')!.body as SaleInput; expect(input.lines[0].batchId).toBeNull(); expect(input.lines[0]).not.toHaveProperty('unitPrice'); expect(input.lines[0]).not.toHaveProperty('recommendedUnitPrice'); expect(input.lines[0]).not.toHaveProperty('lineTotal') })
  it('blocks a lower actual price without sale.change_price', async () => { const app = await openDraft({ permissions: permissions.filter(permission => permission !== 'sale.change_price') }); await number('Фактична ціна 1', '90'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Недостатньо прав для зниження ціни нижче рекомендованої.')).toBeVisible(); expect(app.calls.some(call => call.method === 'PUT')).toBe(false) })
  it('allows a lower actual price with permission and warns the user', async () => { const app = await openDraft(); await number('Фактична ціна 1', '90'); expect(screen.getByText('Ціна нижча за рекомендовану.')).toBeVisible(); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку продажу збережено.'); expect(app.calls.find(call => call.method === 'PUT')!.body).toMatchObject({ lines: [{ unitPrice: 90 }] }) })
  it('explains a missing SERVICE price and preserves manual mode without a PriceType', async () => {
    const app = await newSale(); app.setPrices(service.id, Number.NaN)
    await addProduct(service.name)
    expect(await screen.findByText(/Для послуги не задано ціну для вибраного типу цін/)).toBeVisible()
    expect(screen.getByLabelText('Рекомендована ціна 1')).toHaveTextContent('Не визначено')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    expect(app.calls.some(c => c.path === '/api/sales' && c.method === 'POST')).toBe(false)
    await choose('Тип ціни', 'Не вибрано'); await number('Фактична ціна 1', '75')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await screen.findByText('Чернетку продажу збережено.')
    expect(app.calls.find(c => c.path === '/api/sales' && c.method === 'POST')!.body).toMatchObject({ priceTypeId: null, lines: [{ unitPrice: 75, batchId: null }] })
  })
  it('keeps saved recommendation and actual price snapshots after a current price change', async () => { const app = await openDraft(); app.setPrices(batches[0].batchId, 999); await act(async () => { await app.services.queryClient.invalidateQueries({ queryKey: ['sales', 'price-options'] }) }); expect(screen.getByLabelText('Рекомендована ціна 1')).toHaveTextContent('100,00'); expect(screen.getByRole('spinbutton', { name: 'Фактична ціна 1' })).toHaveValue(100); await number('Кількість 1', '3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку продажу збережено.'); expect(screen.getByLabelText('Рекомендована ціна 1')).toHaveTextContent('100,00') })
  it('clearing an existing actual price preserves its saved value', async () => { const app = await openDraft({ sale: { ...draft, lines: [{ ...draft.lines[0], unitPrice: 150 }] } }); await number('Фактична ціна 1', ''); expect(screen.getByRole('region', { name: 'Підсумки продажу' })).toHaveTextContent('270,00 UAH'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку продажу збережено.'); expect((app.calls.find(call => call.method === 'PUT')!.body as SaleInput).lines[0]).not.toHaveProperty('unitPrice'); expect(screen.getByRole('spinbutton', { name: 'Фактична ціна 1' })).toHaveValue(150) })
  it('calculates header discount and totals without persisted fields', async () => { await openDraft(); const footer = screen.getByRole('region', { name: 'Підсумки продажу' }); expect(within(footer).getByText('200,00 UAH')).toBeVisible(); expect(within(footer).getByText('20,00 UAH')).toBeVisible(); expect(within(footer).getByText('180,00 UAH')).toBeVisible(); await number('Знижка, %', '25'); expect(within(footer).getByText('150,00 UAH')).toBeVisible() })
  it('saves DRAFT with version and line identity and invalidates stock', async () => { const app = await openDraft(); await number('Кількість 1', '3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await screen.findByText('Чернетку продажу збережено.'); expect(app.calls.find(call => call.method === 'PUT')!.body).toMatchObject({ version: 1, lines: [{ id: draft.lines[0].id, quantity: 3 }] }); expect(await screen.findByText('Фізично: 5 · Зарезервовано: 3 · Доступно: 2')).toBeVisible(); expect(app.calls.some(call => call.path.endsWith('/post'))).toBe(false) })
  it('posts after confirmation and switches to read-only', async () => { const app = await openDraft(); await userEvent.click(screen.getByRole('button', { name: 'Провести' })); expect(await screen.findByText('Після проведення буде списано товар зі складу та створено взаєморозрахунок з покупцем.')).toBeVisible(); expect(app.calls.some(call => call.path.endsWith('/post'))).toBe(false); await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Провести' })); expect(await screen.findByRole('heading', { name: 'SI-00000001' })).toBeVisible(); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument(); expect(app.stock()[0].physicalQuantity).toBe(3) })
  it('unposts and restores DRAFT reserves', async () => { const app = setup({ path: `/sales/${draft.document.id}`, sale: posted }); await userEvent.click(await screen.findByRole('button', { name: 'Розпровести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Розпровести' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(await screen.findByRole('button', { name: 'Зберегти' })).toBeVisible(); expect(await screen.findByText('Фізично: 5 · Зарезервовано: 2 · Доступно: 3')).toBeVisible(); expect(app.calls.some(call => call.method === 'DELETE' && call.path.includes('stock'))).toBe(false) })
  it('explains dependent CustomerReturn errors', async () => { const app = setup({ path: `/sales/${draft.document.id}`, sale: posted }); app.error('POST', `/sales/${draft.document.id}/unpost`, 409, { code: 'DOCUMENT_HAS_DEPENDENCIES' }); await userEvent.click(await screen.findByRole('button', { name: 'Розпровести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Розпровести' })); await waitFor(() => expect(screen.getAllByText('Продаж не можна розпровести, оскільки на його основі вже створено повернення.').length).toBeGreaterThan(0)); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument() })
  it('logically deletes a DRAFT and frees its reserve', async () => { const app = await openDraft(); await userEvent.click(screen.getByRole('button', { name: 'Видалити' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Видалити' })); expect(await screen.findByRole('heading', { name: 'Продажів ще немає' })).toBeVisible(); expect(app.calls.filter(call => call.method === 'DELETE').map(call => call.path)).toEqual([`/api/sales/${draft.document.id}`]); expect(app.stock().every(batch => batch.reservedQuantity === 0)).toBe(true) })
  it('highlights GOODS stock errors and offers reallocation', async () => { const app = await openDraft(); app.error('PUT', `/sales/${draft.document.id}`, 409, { code: 'INSUFFICIENT_STOCK' }); await number('Кількість 1', '3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await waitFor(() => expect(screen.getAllByText('Недостатньо доступного залишку.').length).toBeGreaterThan(0)); expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveAttribute('aria-invalid', 'true'); await userEvent.click(screen.getByRole('button', { name: 'Перепідібрати партії' })); expect(await screen.findByRole('dialog')).toBeVisible() })
  it('hides write actions from a view-only user', async () => { setup({ path: `/sales/${draft.document.id}`, permissions: ['sale.view'] }); await screen.findByRole('table', { name: 'Рядки продажу' }); for (const name of ['Зберегти', 'Провести', 'Видалити']) expect(screen.queryByRole('button', { name })).not.toBeInTheDocument(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument() })
  it('hides New sale without sale.create', async () => { setup({ permissions: ['sale.view'] }); await screen.findByRole('table', { name: 'Продажі' }); expect(screen.queryByRole('link', { name: 'Новий продаж' })).not.toBeInTheDocument() })
  it('does not show delete on POSTED documents', async () => { setup({ path: `/sales/${draft.document.id}`, sale: posted }); await screen.findByRole('heading', { name: 'SI-00000001' }); expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument() })
  it('does not fetch or render cost/profit without permissions', async () => { const app = setup({ path: `/sales/${draft.document.id}`, sale: posted, permissions: ['sale.view'] }); await screen.findByRole('heading', { name: 'SI-00000001' }); expect(app.calls.some(call => /\/(cost|profit)$/.test(call.path))).toBe(false); expect(screen.queryByText('Поточна собівартість')).not.toBeInTheDocument(); expect(screen.queryByText('Валовий прибуток')).not.toBeInTheDocument() })
  it('fetches cost and profit independently and displays backend values', async () => { const app = setup({ path: `/sales/${draft.document.id}`, sale: posted, permissions: ['sale.view', 'profit.view'] }); expect(await screen.findByText('60,00 UAH')).toBeVisible(); expect(screen.getByText('33,3333%')).toBeVisible(); expect(app.calls.some(call => call.path.endsWith('/cost'))).toBe(false); expect(screen.queryByText('Поточна собівартість')).not.toBeInTheDocument() })
  it('renders mobile line cards and accessible Save/Post actions', async () => { setViewport(390); await openDraft(); expect(screen.getByRole('list', { name: 'Рядки продажу' })).toBeVisible(); expect(screen.queryByRole('table', { name: 'Рядки продажу' })).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible(); expect(screen.getByRole('button', { name: 'Провести' })).toBeVisible() })
  it('preserves edits on VERSION_CONFLICT until explicit reload', async () => { const app = await openDraft(); app.error('PUT', `/sales/${draft.document.id}`, 409, { code: 'VERSION_CONFLICT' }); await number('Фактична ціна 1', '130'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await waitFor(() => expect(screen.getAllByText('Документ був змінений іншим користувачем. Оновіть дані.').length).toBeGreaterThan(0)); expect(screen.getByRole('spinbutton', { name: 'Фактична ціна 1' })).toHaveValue(130); await userEvent.click(screen.getByRole('button', { name: 'Оновити дані' })); await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Фактична ціна 1' })).toHaveValue(100)) })
  it('retains the baseline version across background cache updates', async () => { const app = await openDraft(); await number('Кількість 1', '3'); const newer = { ...draft, document: { ...draft.document, version: 2 } }; app.setDocument(newer); await act(async () => { app.services.queryClient.setQueryData(saleKeys.detail(draft.document.id), newer) }); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await waitFor(() => expect(screen.getAllByText('Документ був змінений іншим користувачем. Оновіть дані.').length).toBeGreaterThan(0)); expect(app.calls.find(call => call.method === 'PUT')!.body).toMatchObject({ version: 1 }) })
  it('retains a new saved document after POST fails instead of creating a duplicate', async () => { const app = await newSale(); app.error('POST', `/sales/${guid(31)}/post`, 503, {}); await addProduct(service.name); await waitFor(() => expect(screen.getByRole('button', { name: 'Провести' })).toBeEnabled()); await userEvent.click(screen.getByRole('button', { name: 'Провести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' })); await waitFor(() => expect(screen.getByTestId('route')).toHaveTextContent(`/sales/${guid(31)}`)); app.clearErrors(); await postSale(); expect(app.calls.filter(call => call.method === 'POST' && call.path === '/api/sales')).toHaveLength(1) })
  it('blocks duplicate batches and invalid quantity/price at form validation', () => { const line = { lineId: null, productId: goods.id, itemType: 'GOODS', batchId: batches[0].batchId, quantity: 1, unitPrice: 100 }; const input = { documentDate: '2026-10-28', counterpartyId: customer.id, storageLocationId: location.id, priceTypeId: type.id, discountPercent: 0, comment: '', internalNote: '', lines: [line, line] }; expect(saleFormSchema.safeParse(input).success).toBe(false); expect(saleFormSchema.safeParse({ ...input, lines: [{ ...line, quantity: 0, unitPrice: -1 }] }).success).toBe(false) })
  it('maps documented state and duplicate batch errors without losing specific messages', () => { expect(saleError(new ApiError(400, 'generic', 'DOCUMENT_NOT_DRAFT'))).toContain('Стан документа'); expect(saleError(new ApiError(400, 'generic', 'DUPLICATE_BATCH_LINE'))).toContain('Одна партія'); expect(saleError(new ApiError(403, 'generic', 'PERMISSION_DENIED'))).toContain('Недостатньо прав') })
  it('links a posted sales to a return with source prefill', async () => { setup({ path: `/sales/${draft.document.id}`, sale: posted }); expect(await screen.findByRole('link', { name: '+ Створити повернення' })).toHaveAttribute('href', `/customer-returns/new?sourceSaleInvoiceId=${draft.document.id}`) })

})

describe('Sales split view', () => {
  it('selects a row without navigation, shows detail and retains an explicit detail route', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Продажі' })
    expect(screen.getByRole('heading', { name: 'Оберіть продаж' })).toBeVisible()
    const row = within(table).getByRole('row', { name: /Без номера/ })
    await userEvent.click(within(row).getByRole('link', { name: 'Без номера' }))
    expect(await screen.findByRole('heading', { name: 'Чернетка продажу' })).toBeVisible()
    expect(row).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('route')).toHaveTextContent(/^\/sales$/)
    expect(screen.getByRole('region', { name: 'Вибраний продаж' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Відкрити окремо' })).toHaveAttribute('href', '/sales/' + draft.document.id)
    expect(screen.getByRole('tab', { name: 'Документ' })).toHaveAttribute('aria-selected', 'true')
  })
  it('supports keyboard selection with Enter', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Продажі' })
    const row = within(table).getByRole('row', { name: /Без номера/ })
    row.focus()
    await userEvent.keyboard('{Enter}')
    expect(await screen.findByRole('heading', { name: 'Чернетка продажу' })).toBeVisible()
    expect(row).toHaveAttribute('aria-selected', 'true')
  })
  it('uses mobile cards and navigates to a separate detail screen', async () => {
    setViewport(390)
    setup()
    const list = await screen.findByRole('list', { name: 'Список продажів' })
    expect(screen.queryByRole('region', { name: 'Вибраний продаж' })).not.toBeInTheDocument()
    expect(screen.queryByRole('table', { name: 'Продажі' })).not.toBeInTheDocument()
    await userEvent.click(within(list).getByRole('link', { name: 'Без номера' }))
    expect(await screen.findByRole('heading', { name: 'Чернетка продажу' })).toBeVisible()
    expect(screen.getByTestId('route')).toHaveTextContent('/sales/' + draft.document.id)
    expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible()
    expect(screen.getByRole('navigation', { name: 'Мобільна навігація' })).toBeVisible()
  })
  it('retains the full table on tablet without a narrow detail pane', async () => {
    setViewport(1000)
    setup()
    expect(await screen.findByRole('table', { name: 'Продажі' })).toBeVisible()
    expect(screen.getByRole('columnheader', { name: 'Тип ціни' })).toBeVisible()
    expect(screen.queryByRole('region', { name: 'Вибраний продаж' })).not.toBeInTheDocument()
  })
})

describe('Sales query lifecycle', () => {
  it('does not refetch DRAFT-only own-reserve queries after POST', async () => {
    const app = await openDraft()
    await waitFor(() => expect(app.calls.some(call => call.path.includes('excludeSaleId='))).toBe(true))
    await postSale()
    const postIndex = app.calls.findIndex(call => call.path.endsWith('/post'))
    expect(app.calls.slice(postIndex + 1).some(call => call.path.includes('excludeSaleId='))).toBe(false)
    const entry = app.services.queryClient.getQueryState(['stock', 'available-batches', goods.id, location.id, draft.document.id])
    expect(entry?.isInvalidated).toBe(true)
  })
})

describe('Sales UI consistency', () => {
  it.each([390, 430])('uses a mobile filter sheet at %s px without losing filters', async width => {
    setViewport(width); const app = setup()
    await screen.findByRole('list', { name: 'Список продажів' })
    expect(screen.queryByRole('combobox', { name: 'Статус' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Фільтри' }))
    const sheet = screen.getByRole('dialog', { name: 'Фільтри' })
    expect(within(sheet).getByRole('textbox', { name: 'Пошук' })).toBeVisible()
    await choose('Статус', 'Чернетки')
    await userEvent.click(within(sheet).getByRole('button', { name: 'Показати результати' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(app.calls.some(call => call.path.includes('status=DRAFT'))).toBe(true)
    expect(screen.getByRole('navigation', { name: 'Мобільна навігація' })).toBeVisible()
  })
})

it('keeps the closed batch selector compact while preserving option availability details', async () => {
  await openDraft()
  const selector = screen.getByRole('combobox', { name: 'Партія 1' })
  expect(selector).toHaveTextContent(/^B-OLD$/)
  await userEvent.click(selector)
  const option = await screen.findByRole('option', { name: /B-OLD/ })
  expect(option).toHaveTextContent('Доступно:')
  expect(option).toHaveTextContent('Ціна:')
})
