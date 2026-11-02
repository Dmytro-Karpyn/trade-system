import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import type { Product } from '../products/contracts'
import type { Counterparty } from '../counterparties/contracts'
import { purchaseError, uahId } from './contracts'
import type { Purchase, PurchaseBatch, PurchaseExpense, PurchaseInput } from './contracts'
import { purchaseKeys } from './queries'
import { purchaseTotals } from './totals'
import { ApiError } from '../../api/client'

const guid = (value: number) => `019a073c-9ee5-7000-8000-${String(value).padStart(12, '0')}`
const foreignCurrency = guid(840)
const location = { id: guid(1), name: 'Основний склад' }
const supplier: Counterparty = { id: guid(2), organizationId: testUser.organizationId, name: 'ТОВ Постачсервіс', legalType: 'COMPANY', taxId: null, registrationCode: null, phone: null, email: null, address: null, defaultPriceTypeId: null, isCustomer: false, isSupplier: true, isEmployee: false, isSystem: false, active: true, note: null }
const buyer: Counterparty = { ...supplier, id: guid(3), name: 'Лише покупець', isCustomer: true, isSupplier: false }
const inactive: Counterparty = { ...supplier, id: guid(4), name: 'Неактивний постачальник', active: false }
const goods: Product = { id: guid(5), organizationId: testUser.organizationId, categoryId: null, unitId: guid(6), itemType: 'GOODS', code: 'TOOL-001', barcode: '4820000123', name: 'Дриль', description: null, attributes: null, active: true }
const service: Product = { ...goods, id: guid(7), itemType: 'SERVICE', name: 'Монтаж', code: 'SERVICE-001' }
const inactiveGoods: Product = { ...goods, id: guid(8), name: 'Неактивний товар', active: false }
const draft: Purchase = { document: { id: guid(10), organizationId: testUser.organizationId, documentType: 'PURCHASE_INVOICE', number: null, documentDate: '2026-10-20', status: 'DRAFT', comment: 'Замовлення постачальнику', internalNote: 'Для закупівель', createdBy: testUser.userId, createdAt: '2026-10-20T10:00:00Z', updatedAt: '2026-10-20T10:00:00Z', postedBy: null, postedAt: null, deletedBy: null, deletedAt: null, version: 1 }, counterpartyId: supplier.id, storageLocationId: location.id, currencyId: uahId, exchangeRate: 1, externalNumber: 'EXT-001', externalDate: null, discountPercent: 10, lines: [{ id: guid(11), purchaseInvoiceId: guid(10), lineNo: 1, productId: goods.id, quantity: 2, unitPriceCurrency: 100, removedAt: null }], products: [{ productId: goods.id, name: goods.name, code: goods.code }], postedByName: null }
const batch = { batchId: guid(12), purchaseInvoiceLineId: guid(11), productId: goods.id, productName: goods.name, productCode: goods.code, batchNumber: 'B-20261020-001', createdAt: '2026-10-20T10:05:00Z' }
const permissions = [...testUser.permissions, 'purchase.create', 'purchase.post', 'purchase.unpost', 'purchase.delete', 'cost.view', 'product.create']
const servicesToClean: ReturnType<typeof createAppServices>[] = []
const expense: PurchaseExpense = { document: { id: guid(40), documentDate: '2026-10-21', number: 'EX-00000003', status: 'POSTED', comment: 'Доставка' }, purchaseInvoiceId: draft.document.id, currencyId: uahId, amount: 20 }
function LocationProbe() { return <span data-testid="route">{useLocation().pathname}</span> }
interface SetupOptions { path?: string; purchase?: Purchase; empty?: boolean; permissions?: string[]; retainedBatch?: boolean; pending?: boolean; productPending?: boolean; noLocations?: boolean; batches?: PurchaseBatch[]; expenseRecords?: PurchaseExpense[]; expensesPending?: boolean; detailPending?: boolean; postPending?: boolean; postResponseLost?: boolean }
function setup({ path = '/purchases', purchase = draft, empty = false, permissions: allowed = permissions, retainedBatch = false, pending = false, productPending = false, noLocations = false, batches: batchRecords, expenseRecords = [], expensesPending = false, detailPending = false, postPending = false, postResponseLost = false }: SetupOptions = {}) {
  let document: Purchase = structuredClone(purchase)
  let deleted = false
  let hasBatch = retainedBatch || document.document.status === 'POSTED'
  let locations = noLocations ? [] : [location]
  const errors = new Map<string, { status: number; body: unknown }>()
  const calls: { path: string; method: string; body?: unknown }[] = []
  let releaseList: (() => void) | undefined
  let releaseProducts: (() => void) | undefined
  let releaseExpenses: (() => void) | undefined
  let releaseDetail: (() => void) | undefined
  let releasePost: (() => void) | undefined
  const listGate = pending ? new Promise<void>(resolve => { releaseList = resolve }) : Promise.resolve()
  const productGate = productPending ? new Promise<void>(resolve => { releaseProducts = resolve }) : Promise.resolve()
  const expenseGate = expensesPending ? new Promise<void>(resolve => { releaseExpenses = resolve }) : Promise.resolve()
  const detailGate = detailPending ? new Promise<void>(resolve => { releaseDetail = resolve }) : Promise.resolve()
  const postGate = postPending ? new Promise<void>(resolve => { releasePost = resolve }) : Promise.resolve()
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const path = parsed.pathname.replace(/^\/api/, '')
    const method = options?.method ?? 'GET'; const body = options?.body ? JSON.parse(String(options.body)) : undefined
    calls.push({ path: parsed.pathname + parsed.search, method, body })
    const error = errors.get(method + ' ' + path); if (error) return json(error.body, error.status)
    if (path === '/auth/me') return json({ ...testUser, permissions: allowed })
    if (path === '/supplier-returns/overview') return json([])
    if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/health') return json({ status: 'Healthy' })
    if (path === '/expenses') { await expenseGate; return json(expenseRecords.filter(item => item.purchaseInvoiceId === parsed.searchParams.get('purchaseInvoiceId'))) }
    if (path === '/counterparties') return json([supplier, buyer, inactive].filter(item => (!parsed.searchParams.has('isSupplier') || item.isSupplier) && (!parsed.searchParams.has('active') || item.active)))
    if (path === '/products') { await productGate; const search = parsed.searchParams.get('search')?.toLowerCase(); return json([goods, service, inactiveGoods].filter(item => !search || [item.name, item.code, item.barcode].some(value => value?.toLowerCase().includes(search)))) }
    if (path === '/purchases/lookups') return json({ locations, currencyIds: [uahId, foreignCurrency] })
    if (path === '/storage-locations' && method === 'POST') { const created = { id: guid(13), name: body.name }; locations = [...locations, created]; return json(created, 201) }
    if (path === '/purchases' && method === 'GET') { await listGate; const search = parsed.searchParams.get('search')?.toLowerCase(); const hidden = empty || deleted || search && ![document.document.number, document.externalNumber, supplier.name].some(value => value?.toLowerCase().includes(search)) || parsed.searchParams.has('status') && parsed.searchParams.get('status') !== document.document.status; return json(hidden ? [] : [{ id: document.document.id, number: document.document.number, documentDate: document.document.documentDate, counterpartyId: supplier.id, counterpartyName: supplier.name, storageLocationId: location.id, currencyId: document.currencyId, totalCurrency: purchaseTotals(document.lines.filter(line => !line.removedAt), document.discountPercent, document.exchangeRate).total, status: document.document.status, externalNumber: document.externalNumber }]) }
    if (path === '/purchases' && method === 'POST') { const input = body as PurchaseInput; document = { ...document, ...input, document: { ...draft.document, id: guid(14), documentDate: input.documentDate, comment: input.comment, internalNote: input.internalNote }, lines: input.lines.map((line, index) => ({ ...line, id: guid(20 + index), purchaseInvoiceId: guid(14), removedAt: null })), products: draft.products }; return json(document, 201) }
    if (path === `/purchases/${document.document.id}/batches`) return json(hasBatch ? batchRecords ?? [{ ...batch, purchaseInvoiceLineId: document.lines[0]?.id ?? batch.purchaseInvoiceLineId }] : [])
    if (path === `/purchases/${document.document.id}/batch-costs`) return json(hasBatch ? [{ batchId: batch.batchId, unitCostUah: 90 }] : [])
    if (path === `/purchases/${document.document.id}/batch-prices`) return json(hasBatch ? [{ batchId: batch.batchId, priceTypeId: guid(15), priceTypeName: 'Роздрібна', unitPrice: 112.5 }] : [])
    if (path === `/purchases/${document.document.id}/post`) { await postGate; hasBatch = true; document = { ...document, document: { ...document.document, status: 'POSTED', number: 'PI-00000001', postedAt: '2026-10-20T10:05:00Z', postedBy: testUser.userId, version: document.document.version + 1 }, postedByName: testUser.fullName }; if (postResponseLost) { postResponseLost = false; return json({}, 503) } return json(document) }
    if (path === `/purchases/${document.document.id}/unpost`) { document = { ...document, document: { ...document.document, status: 'DRAFT', postedAt: null, postedBy: null, version: document.document.version + 1 } }; return json(document) }
    if (path === `/purchases/${document.document.id}`) {
      if (method === 'DELETE') { deleted = true; return new Response(null, { status: 204 }) }
      if (deleted) return json({}, 404)
      if (method === 'GET') await detailGate
      if (method === 'PUT') { const input = body as PurchaseInput; if (input.version !== document.document.version) return json({ code: 'VERSION_CONFLICT' }, 409); document = { ...document, ...input, document: { ...document.document, documentDate: input.documentDate, comment: input.comment, internalNote: input.internalNote, version: document.document.version + 1 }, lines: input.lines.map((line, index) => ({ ...line, id: line.id ?? guid(20 + index), purchaseInvoiceId: document.document.id, removedAt: null })) } }
      return json(document)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch)
  const services = createAppServices('/api'); servicesToClean.push(services)
  services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><LocationProbe /></MemoryRouter></AppProviders>)
  return { calls, services, releaseList: () => releaseList?.(), releaseProducts: () => releaseProducts?.(), releaseExpenses: () => releaseExpenses?.(), releaseDetail: () => releaseDetail?.(), releasePost: () => releasePost?.(), setServerDocument: (value: Purchase) => { document = value }, error: (method: string, path: string, status: number, body: unknown) => errors.set(method + ' ' + path, { status, body }), clearErrors: () => errors.clear() }
}
async function choose(label: string, option: string) { await userEvent.click(await screen.findByRole('combobox', { name: new RegExp('^' + label) })); await userEvent.click(await screen.findByRole('option', { name: option })) }
async function number(label: string, value: string) { const input = screen.getByRole('spinbutton', { name: label }); await userEvent.clear(input); if (value) await userEvent.type(input, value) }
async function openDraft(options: SetupOptions = {}) { const result = setup({ path: `/purchases/${draft.document.id}`, ...options }); await screen.findByRole('button', { name: 'Зберегти' }); return result }
async function openExpenses(options: SetupOptions = {}) { const app = setup({ path: `/purchases/${draft.document.id}`, purchase: posted, ...options }); await userEvent.click(await screen.findByRole('tab', { name: 'Витрати' })); return app }
async function fillNewPurchase() {
  await choose('Постачальник', supplier.name); await choose('Склад', location.name)
  await userEvent.click(screen.getByRole('button', { name: 'Додати товар' }))
  await userEvent.click(screen.getByRole('combobox', { name: 'Товар 1' }))
  await userEvent.click(await screen.findByRole('option', { name: /Дриль/ }))
  await number('Ціна 1', '100')
}
async function postDraft() { await userEvent.click(screen.getByRole('button', { name: 'Провести' })); const dialog = await screen.findByRole('dialog'); await userEvent.click(within(dialog).getByRole('button', { name: 'Провести' })); await screen.findByRole('button', { name: 'Розпровести' }) }
const posted: Purchase = { ...draft, document: { ...draft.document, status: 'POSTED', number: 'PI-00000001', postedAt: '2026-10-20T10:05:00Z', postedBy: testUser.userId }, postedByName: testUser.fullName }
beforeEach(() => setViewport())
afterEach(() => { servicesToClean.splice(0).forEach(item => item.queryClient.clear()); vi.unstubAllGlobals() })

describe('Purchase UI', () => {
  it('loads the purchase list without internal IDs', async () => { setup(); const table = await screen.findByRole('table', { name: 'Закупівлі' }); expect(within(table).getByText(supplier.name)).toBeVisible(); expect(within(table).getByText('180,00')).toBeVisible(); expect(screen.queryByText(draft.document.id)).not.toBeInTheDocument() })
  it('shows a list skeleton', async () => { const app = setup({ pending: true }); expect(await screen.findByRole('status', { name: 'Завантаження закупівель' })).toBeVisible(); await act(async () => app.releaseList()); expect(await screen.findByRole('table', { name: 'Закупівлі' })).toBeVisible() })
  it('shows an empty purchase list', async () => { setup({ empty: true }); expect(await screen.findByRole('heading', { name: 'Закупівель ще немає' })).toBeVisible() })
  it('renders all filters and sends combined filters to the backend', async () => {
    const { calls } = setup(); await screen.findByRole('table', { name: 'Закупівлі' })
    expect(screen.getByLabelText('Період від')).toBeVisible(); expect(screen.getByLabelText('Період до')).toBeVisible()
    await choose('Постачальник', supplier.name); await choose('Склад', location.name); await choose('Валюта', 'UAH'); await choose('Статус', 'Чернетки')
    expect(calls.some(call => ['counterpartyId', 'storageLocationId', 'currencyId', 'status'].every(key => new URL(call.path, 'http://localhost').searchParams.has(key)))).toBe(true)
  })
  it('debounces search and displays an empty search result', async () => { const { calls } = setup(); await screen.findByRole('table', { name: 'Закупівлі' }); await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'невідомий'); expect(await screen.findByRole('heading', { name: 'Нічого не знайдено' })).toBeVisible(); expect(calls.filter(call => call.path.includes('search='))).toHaveLength(1) })
  it('opens a new purchase form with UAH and local document date', async () => { setup({ path: '/purchases/new' }); expect(await screen.findByRole('heading', { name: 'Нова закупівля' })).toBeVisible(); expect((screen.getByLabelText('Дата документа') as HTMLInputElement).value).toMatch(/^\d{4}-\d{2}-\d{2}$/); expect(screen.getByRole('spinbutton', { name: 'Курс' })).toHaveValue(1); expect(screen.queryByRole('tab')).not.toBeInTheDocument() })
  it('offers only active suppliers', async () => { await openDraft(); await userEvent.click(screen.getByRole('combobox', { name: /^Постачальник/ })); expect(await screen.findByRole('option', { name: supplier.name })).toBeVisible(); expect(screen.queryByRole('option', { name: buyer.name })).not.toBeInTheDocument(); expect(screen.queryByRole('option', { name: inactive.name })).not.toBeInTheDocument() })
  it('adds a product line and excludes SERVICE and inactive products', async () => { const { calls } = await openDraft(); await userEvent.click(screen.getByRole('button', { name: 'Додати товар' })); await userEvent.click(screen.getByRole('combobox', { name: 'Товар 2' })); expect(await screen.findByRole('option', { name: /Дриль/ })).toBeVisible(); expect(screen.queryByRole('option', { name: /Монтаж|Неактивний товар/ })).not.toBeInTheDocument(); await userEvent.click(screen.getByRole('option', { name: /Дриль/ })); expect(screen.getByRole('combobox', { name: 'Товар 2' })).toHaveValue(goods.name); expect(calls.some(call => call.path.includes('/products?active=true&itemType=GOODS'))).toBe(true) })
  it('searches products by barcode with a loading state', async () => { const app = await openDraft({ productPending: true }); await userEvent.click(screen.getByRole('combobox', { name: 'Товар 1' })); expect(await screen.findByLabelText('Завантаження товарів')).toBeVisible(); await userEvent.clear(screen.getByRole('combobox', { name: 'Товар 1' })); await userEvent.type(screen.getByRole('combobox', { name: 'Товар 1' }), goods.barcode!); await act(async () => app.releaseProducts()); await waitFor(() => expect(app.calls.some(call => call.path.includes('search=4820000123'))).toBe(true)); expect(await screen.findByRole('option', { name: /Дриль/ })).toBeVisible() })
  it('validates positive quantity and nonnegative price', async () => { await openDraft(); await number('Кількість 1', '0'); await number('Ціна 1', '-1'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Значення має бути більше нуля.')).toBeVisible(); expect(await screen.findByText('Значення не може бути від’ємним.')).toBeVisible() })
  it('calculates the displayed line total', async () => { await openDraft(); await number('Кількість 1', '3'); expect(within(screen.getByRole('table', { name: 'Рядки закупівлі' })).getByText('300,00')).toBeVisible() })
  it('calculates header discount and total without persisted totals', async () => { await openDraft(); const totals = screen.getByRole('region', { name: 'Підсумки накладної' }); expect(within(totals).getByText('200,00 UAH')).toBeVisible(); expect(within(totals).getByText('20,00 UAH')).toBeVisible(); expect(within(totals).getAllByText('180,00 UAH')).toHaveLength(2); await number('Знижка, %', '25'); expect(within(totals).getByText('50,00 UAH')).toBeVisible(); expect(within(totals).getAllByText('150,00 UAH')).toHaveLength(2) })
  it('converts the discounted total using the editable foreign currency rate', async () => { await openDraft(); await choose('Валюта', 'Валюта 1'); await number('Курс', '40'); expect(within(screen.getByRole('region', { name: 'Підсумки накладної' })).getByText(/7\s200,00 UAH/)).toBeVisible() })
  it('sets the UAH rate to one and disables editing it', async () => { await openDraft(); await choose('Валюта', 'Валюта 1'); await number('Курс', '40'); await choose('Валюта', 'UAH'); expect(screen.getByRole('spinbutton', { name: 'Курс' })).toHaveValue(1); expect(screen.getByRole('spinbutton', { name: 'Курс' })).toBeDisabled() })
  it('saves a new draft without creating batches or posting', async () => { const { calls } = setup({ path: '/purchases/new' }); await choose('Постачальник', supplier.name); await choose('Склад', location.name); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Чернетку збережено.')).toBeVisible(); await waitFor(() => expect(screen.getByTestId('route')).toHaveTextContent('/purchases/' + guid(14))); const saved = calls.find(call => call.method === 'POST' && call.path === '/api/purchases')!; expect(saved.body).toMatchObject({ documentType: 'PURCHASE_INVOICE', counterpartyId: supplier.id, storageLocationId: location.id, exchangeRate: 1 }); expect(saved.body).not.toHaveProperty('total'); expect(calls.some(call => call.path.endsWith('/post'))).toBe(false); expect(screen.queryByText(batch.batchNumber)).not.toBeInTheDocument() })
  it('saves an edited draft with the original version and line identity', async () => { const { calls } = await openDraft(); await number('Кількість 1', '3'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); expect(await screen.findByText('Чернетку збережено.')).toBeVisible(); const saved = calls.find(call => call.method === 'PUT')!; expect(saved.body).toMatchObject({ version: 1, lines: [{ id: draft.lines[0].id, quantity: 3, unitPriceCurrency: 100 }] }); expect((saved.body as PurchaseInput).lines[0]).not.toHaveProperty('lineTotal') })
  it('requires confirmation, posts the purchase and switches to read-only', async () => { const { calls } = await openDraft(); await userEvent.click(screen.getByRole('button', { name: 'Провести' })); expect(await screen.findByText('Після проведення будуть створені партії та складські залишки.')).toBeVisible(); expect(calls.some(call => call.path.endsWith('/post'))).toBe(false); await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Провести' })); expect(await screen.findByRole('button', { name: 'Розпровести' })).toBeVisible(); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'PI-00000001' })).toBeVisible() })
  it('saves pending edits before POST and does not POST after a failed save', async () => { const app = await openDraft(); await number('Кількість 1', '4'); app.error('PUT', `/purchases/${draft.document.id}`, 409, { code: 'VERSION_CONFLICT' }); await userEvent.click(screen.getByRole('button', { name: 'Провести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' })); await waitFor(() => expect(screen.getAllByText('Документ був змінений іншим користувачем. Оновіть дані.').length).toBeGreaterThan(0)); expect(app.calls.some(call => call.path.endsWith('/post'))).toBe(false); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(4) })
  it('shows real batch data and current backend cost after POST', async () => { await openDraft(); await postDraft(); expect(await screen.findByText(batch.batchNumber)).toBeVisible(); expect(await screen.findByText('90,00 UAH')).toBeVisible(); expect(screen.getByText(/Проведено .*Олена Коваль/)).toBeVisible() })
  it('loads saved BatchPrice on the Prices tab', async () => { setup({ path: `/purchases/${draft.document.id}`, purchase: posted }); await userEvent.click(await screen.findByRole('tab', { name: 'Ціни' })); expect(await screen.findByText('Роздрібна')).toBeVisible(); expect(screen.getByText('112,50')).toBeVisible() })
  it('unposts after confirmation and retains the same locked product', async () => { const { calls } = setup({ path: `/purchases/${draft.document.id}`, purchase: posted }); await userEvent.click(await screen.findByRole('button', { name: 'Розпровести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Розпровести' })); const product = await screen.findByRole('combobox', { name: 'Товар 1' }); expect(product).toBeDisabled(); expect(screen.getByText('Товар не можна змінити після створення партії.')).toBeVisible(); expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toBeEnabled(); expect(calls.filter(call => call.method === 'POST' && call.path.endsWith('/unpost'))).toHaveLength(1); expect(calls.some(call => call.method === 'DELETE' && /batch|stock|price/.test(call.path))).toBe(false) })
  it('explains a dependency error while keeping the posted purchase intact', async () => { const app = setup({ path: `/purchases/${draft.document.id}`, purchase: posted }); app.error('POST', `/purchases/${draft.document.id}/unpost`, 409, { code: 'DOCUMENT_HAS_DEPENDENCIES' }); await userEvent.click(await screen.findByRole('button', { name: 'Розпровести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Розпровести' })); await waitFor(() => expect(screen.getAllByText('Закупівлю не можна розпровести, оскільки на її основі вже створено повернення постачальнику або інший залежний документ.').length).toBeGreaterThan(0)); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.getByRole('tab', { name: 'Партії' })).toBeVisible(); expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument() })
  it('disables Product selection when a batch already exists in a DRAFT', async () => { await openDraft({ retainedBatch: true }); expect(screen.getByRole('combobox', { name: 'Товар 1' })).toBeDisabled(); expect(screen.queryByText(batch.batchNumber)).not.toBeInTheDocument() })
  it('logically deletes a draft after confirmation and returns to the list', async () => { const { calls } = await openDraft(); await userEvent.click(screen.getByRole('button', { name: 'Видалити' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Видалити' })); expect(await screen.findByRole('heading', { name: 'Закупівель ще немає' })).toBeVisible(); expect(calls.filter(call => call.method === 'DELETE').map(call => call.path)).toEqual([`/api/purchases/${draft.document.id}`]) })
  it('hides unavailable draft actions for a view-only user', async () => { setup({ path: `/purchases/${draft.document.id}`, permissions: ['purchase.view'] }); await screen.findByRole('table', { name: 'Рядки закупівлі' }); for (const name of ['Зберегти', 'Провести', 'Видалити']) expect(screen.queryByRole('button', { name })).not.toBeInTheDocument(); expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument() })
  it('allows posting a saved draft without create permission and never calls PUT', async () => { const { calls } = setup({ path: `/purchases/${draft.document.id}`, permissions: ['purchase.view', 'purchase.post'] }); await userEvent.click(await screen.findByRole('button', { name: 'Провести' })); await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' })); expect(await screen.findByText('Закупівлю проведено.')).toBeVisible(); expect(calls.some(call => call.method === 'PUT')).toBe(false); expect(calls.some(call => call.path.endsWith('/batch-costs'))).toBe(false) })
  it('does not expose delete on a POSTED purchase or fetch cost without cost.view', async () => { const { calls } = setup({ path: `/purchases/${draft.document.id}`, purchase: posted, permissions: ['purchase.view', 'purchase.delete'] }); await screen.findByText(batch.batchNumber); expect(screen.queryByRole('button', { name: 'Видалити' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Розпровести' })).not.toBeInTheDocument(); expect(calls.some(call => call.path.endsWith('/batch-costs'))).toBe(false) })
  it('hides New purchase without purchase.create', async () => { setup({ permissions: ['purchase.view'] }); await screen.findByRole('table', { name: 'Закупівлі' }); expect(screen.queryByRole('link', { name: 'Нова закупівля' })).not.toBeInTheDocument() })
  it('renders mobile line cards and accessible save/post actions', async () => { setViewport(390); await openDraft(); expect(screen.getByRole('list', { name: 'Рядки закупівлі' })).toBeVisible(); expect(screen.queryByRole('table', { name: 'Рядки закупівлі' })).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Зберегти' })).toBeVisible(); expect(screen.getByRole('button', { name: 'Провести' })).toBeVisible() })
  it('handles version conflict and preserves entered values until explicit refresh', async () => { const app = await openDraft(); app.error('PUT', `/purchases/${draft.document.id}`, 409, { detail: 'Document Version is stale. Reload the document before editing.' }); await number('Ціна 1', '120'); await userEvent.click(screen.getByRole('button', { name: 'Зберегти' })); await waitFor(() => expect(screen.getAllByText('Документ був змінений іншим користувачем. Оновіть дані.').length).toBeGreaterThan(0)); expect(screen.getByRole('spinbutton', { name: 'Ціна 1' })).toHaveValue(120); await userEvent.click(screen.getByRole('button', { name: 'Оновити дані' })); await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Ціна 1' })).toHaveValue(100)) })
  it('creates a minimal location inline and selects it', async () => { await openDraft({ noLocations: true }); await userEvent.click(screen.getByRole('button', { name: 'Додати склад' })); await userEvent.type(screen.getByRole('textbox', { name: 'Назва складу' }), 'Резервний'); await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Додати склад' })); await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); expect(screen.getByRole('combobox', { name: /^Склад/ })).toHaveTextContent('Резервний') })
  it('shows the detail skeleton until the purchase arrives', async () => {
    const app = setup({ path: `/purchases/${draft.document.id}`, detailPending: true })
    expect(await screen.findByRole('status', { name: 'Завантаження закупівлі' })).toBeVisible()
    await act(async () => app.releaseDetail())
    expect(await screen.findByRole('button', { name: 'Зберегти' })).toBeVisible()
  })
  it('removes a line without changing the identity of the surviving row', async () => {
    const second = { ...draft.lines[0], id: guid(22), lineNo: 2, quantity: 5 }
    const app = await openDraft({ purchase: { ...draft, lines: [draft.lines[0], second] } })
    await userEvent.click(screen.getByRole('button', { name: 'Вилучити рядок 1' }))
    expect(screen.queryByRole('combobox', { name: 'Товар 2' })).not.toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(5)
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await screen.findByText('Чернетку збережено.')
    expect((app.calls.find(call => call.method === 'PUT')!.body as PurchaseInput).lines).toEqual([{ id: second.id, lineNo: 1, productId: goods.id, quantity: 5, unitPriceCurrency: 100 }])
  })
  it('shows each batch quantity by source line even for the same Product', async () => {
    const second = { ...draft.lines[0], id: guid(22), lineNo: 2, quantity: 5 }
    const secondBatch = { ...batch, batchId: guid(23), purchaseInvoiceLineId: second.id, batchNumber: 'B-SECOND' }
    setup({ path: `/purchases/${draft.document.id}`, purchase: { ...posted, lines: [draft.lines[0], second] }, batches: [batch, secondBatch] })
    await userEvent.click(await screen.findByRole('tab', { name: 'Партії' }))
    const table = await screen.findByRole('table', { name: 'Партії закупівлі' })
    const first = within(table).getByText(batch.batchNumber).closest('tr')!
    const other = within(table).getByText(secondBatch.batchNumber).closest('tr')!
    expect(within(first).getByRole('cell', { name: '2' })).toBeVisible()
    expect(within(other).getByRole('cell', { name: '5' })).toBeVisible()
  })
  it('labels a retained batch from a removed line instead of showing an active quantity', async () => {
    setup({ path: `/purchases/${draft.document.id}`, purchase: { ...posted, lines: [{ ...posted.lines[0], removedAt: '2026-10-21T10:00:00Z' }] } })
    await userEvent.click(await screen.findByRole('tab', { name: 'Партії' }))
    expect(await screen.findByRole('cell', { name: 'Рядок вилучено' })).toBeVisible()
  })
  it('shows batch quantity in mobile cards', async () => {
    setViewport(390)
    setup({ path: `/purchases/${draft.document.id}`, purchase: posted })
    await userEvent.click(await screen.findByRole('tab', { name: 'Партії' }))
    expect(await screen.findByText('Кількість: 2')).toBeVisible()
    expect(screen.getByRole('list', { name: 'Партії закупівлі' })).toBeVisible()
  })
  it('loads only related purchase expenses through the existing API without edit actions', async () => {
    const unrelated = { ...expense, document: { ...expense.document, id: guid(41), number: 'EX-OTHER' }, purchaseInvoiceId: guid(99) }
    const app = await openExpenses({ expenseRecords: [expense, unrelated] })
    const table = await screen.findByRole('table', { name: 'Закупівельні витрати' })
    expect(within(table).getByText(expense.document.number!)).toBeVisible()
    expect(within(table).getByText('20,00')).toBeVisible()
    expect(within(table).getByText('UAH')).toBeVisible()
    expect(within(table).getByText('Доставка')).toBeVisible()
    expect(screen.queryByText('EX-OTHER')).not.toBeInTheDocument()
    expect(screen.queryByText(expense.document.id)).not.toBeInTheDocument()
    expect(app.calls.filter(call => call.path.startsWith('/api/expenses')).map(call => call.path)).toEqual([`/api/expenses?purchaseInvoiceId=${draft.document.id}`])
    expect(screen.queryByRole('button', { name: /Додати витрату|Створити витрату/ })).not.toBeInTheDocument()
  })
  it('links to an existing expense and opens a prefilled new expense when permitted', async () => {
    await openExpenses({ expenseRecords: [expense], permissions: [...permissions, 'expense.create'] })
    expect(await screen.findByRole('link', { name: 'EX-00000003' })).toHaveAttribute('href', `/expenses/${expense.document.id}`)
    expect(screen.getByRole('link', { name: '+ Додати витрату' })).toHaveAttribute('href', `/expenses/new?purchaseInvoiceId=${draft.document.id}`)
  })
  it('shows an expense empty state', async () => {
    await openExpenses()
    expect(await screen.findByRole('heading', { name: 'Закупівельних витрат ще немає' })).toBeVisible()
  })
  it('shows expense loading and then the result', async () => {
    const app = await openExpenses({ expenseRecords: [expense], expensesPending: true })
    expect(screen.getByRole('status', { name: 'Завантаження закупівельних витрат' })).toBeVisible()
    await act(async () => app.releaseExpenses())
    expect(await screen.findByRole('table', { name: 'Закупівельні витрати' })).toBeVisible()
  })
  it('retries expense query errors', async () => {
    const app = setup({ path: `/purchases/${draft.document.id}`, purchase: posted, expenseRecords: [expense] })
    app.error('GET', '/expenses', 503, {})
    await userEvent.click(await screen.findByRole('tab', { name: 'Витрати' }))
    const retry = await screen.findByRole('button', { name: 'Спробувати ще раз' })
    app.clearErrors(); await userEvent.click(retry)
    expect(await screen.findByRole('table', { name: 'Закупівельні витрати' })).toBeVisible()
  })
  it('requires expense.view and never fetches expenses for a purchase-only viewer', async () => {
    const app = await openExpenses({ permissions: ['purchase.view'] })
    expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
    expect(app.calls.some(call => call.path.startsWith('/api/expenses'))).toBe(false)
  })
  it('shows expense cards on mobile with currencies and document status', async () => {
    setViewport(390)
    const foreign = { ...expense, document: { ...expense.document, id: guid(42), number: null, status: 'DRAFT' as const, comment: null }, currencyId: foreignCurrency, amount: 30 }
    await openExpenses({ expenseRecords: [expense, foreign] })
    const list = await screen.findByRole('list', { name: 'Закупівельні витрати' })
    expect(within(list).getByText('20,00 UAH')).toBeVisible()
    expect(within(list).getByText('30,00 Валюта 1')).toBeVisible()
    expect(within(list).getByText('Чернетка')).toBeVisible()
    expect(screen.queryByRole('table', { name: 'Закупівельні витрати' })).not.toBeInTheDocument()
  })
  it('keeps a newly created draft after a failed POST and retries the same document', async () => {
    const app = setup({ path: '/purchases/new' })
    app.error('POST', `/purchases/${guid(14)}/post`, 503, {})
    await fillNewPurchase()
    await userEvent.click(screen.getByRole('button', { name: 'Провести' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' }))
    await waitFor(() => expect(screen.getByTestId('route')).toHaveTextContent(`/purchases/${guid(14)}`))
    expect(await screen.findByRole('heading', { name: 'Чернетка закупівлі' })).toBeVisible()
    app.clearErrors()
    await postDraft()
    expect(app.calls.filter(call => call.method === 'POST' && call.path === '/api/purchases')).toHaveLength(1)
    expect(app.calls.filter(call => call.path === `/api/purchases/${guid(14)}/post`)).toHaveLength(2)
  })
  it('reconciles a successful POST with a lost response by reading the saved document', async () => {
    const app = setup({ path: '/purchases/new', postResponseLost: true })
    await fillNewPurchase()
    await userEvent.click(screen.getByRole('button', { name: 'Провести' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Провести' }))
    expect(await screen.findByRole('heading', { name: 'PI-00000001' })).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Зберегти' })).not.toBeInTheDocument()
    expect(app.calls.filter(call => call.method === 'POST' && call.path === '/api/purchases')).toHaveLength(1)
    expect(app.calls.filter(call => call.path.endsWith('/post'))).toHaveLength(1)
  })
  it('keeps the form baseline version when a background read observes another edit', async () => {
    const app = await openDraft()
    await number('Ціна 1', '120')
    const newer = { ...draft, document: { ...draft.document, version: 2 }, lines: [{ ...draft.lines[0], unitPriceCurrency: 150 }] }
    app.setServerDocument(newer)
    await act(async () => { app.services.queryClient.setQueryData(purchaseKeys.detail(draft.document.id), newer) })
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await waitFor(() => expect(screen.getAllByText('Документ був змінений іншим користувачем. Оновіть дані.').length).toBeGreaterThan(0))
    expect(app.calls.find(call => call.method === 'PUT')!.body).toMatchObject({ version: 1 })
    expect(screen.getByRole('spinbutton', { name: 'Ціна 1' })).toHaveValue(120)
    await userEvent.click(screen.getByRole('button', { name: 'Оновити дані' }))
    await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Ціна 1' })).toHaveValue(150))
  })
  it('normalizes a legacy UAH draft rate to one only when the user saves', async () => {
    const app = await openDraft({ purchase: { ...draft, exchangeRate: 2 } })
    expect(screen.getByRole('spinbutton', { name: 'Курс' })).toHaveValue(1)
    expect(screen.getByRole('spinbutton', { name: 'Курс' })).toBeDisabled()
    expect(app.calls.some(call => call.method === 'PUT')).toBe(false)
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await screen.findByText('Чернетку збережено.')
    expect(app.calls.find(call => call.method === 'PUT')!.body).toMatchObject({ version: 1, exchangeRate: 1 })
  })
  it('blocks actions while POST is in progress', async () => {
    const app = await openDraft({ postPending: true })
    await userEvent.click(screen.getByRole('button', { name: 'Провести' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Провести' }))
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Проводимо…' })).toBeDisabled())
    expect(within(dialog).getByRole('button', { name: 'Скасувати' })).toBeDisabled()
    await act(async () => app.releasePost())
    expect(await screen.findByRole('button', { name: 'Розпровести' })).toBeVisible()
  })
  it('rounds only the document preview and handles the remaining documented error codes', () => { expect(purchaseTotals([{ quantity: 1, unitPriceCurrency: 0.0001 }, { quantity: 1, unitPriceCurrency: 0.0001 }], 25, 40)).toMatchObject({ gross: 0.0002, total: 0.0002, uah: 0.008 }); expect(purchaseError(new ApiError(400, 'generic', 'DOCUMENT_NOT_DRAFT'))).toContain('Стан документа'); expect(purchaseError(new ApiError(400, 'generic', 'INVALID_SOURCE_DOCUMENT'))).toContain('Вихідний документ') })
  it('links a posted purchases to a return with source prefill', async () => { setup({ path: `/purchases/${draft.document.id}`, purchase: posted }); expect(await screen.findByRole('link', { name: '+ Повернути постачальнику' })).toHaveAttribute('href', `/supplier-returns/new?sourcePurchaseInvoiceId=${draft.document.id}`) })

})

describe('Purchase redesign', () => {
  it('selects a purchase in split view without changing /purchases', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Закупівлі' })
    const row = within(table).getByRole('row', { name: /Без номера/ })
    await userEvent.click(within(row).getByRole('link', { name: 'Без номера' }))
    expect(await screen.findByRole('region', { name: 'Вибрана закупівля' })).toBeVisible()
    expect(await screen.findByRole('heading', { name: 'Чернетка закупівлі' })).toBeVisible()
    expect(row).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('route')).toHaveTextContent(/^\/purchases$/)
    expect(screen.getByRole('link', { name: 'Відкрити окремо' })).toHaveAttribute('href', `/purchases/${draft.document.id}`)
    expect(screen.getByRole('tab', { name: 'Загальне' })).toHaveAttribute('aria-selected', 'true')
  })
  it('keeps dirty line values when switching general and goods tabs', async () => {
    await openDraft()
    await number('Кількість 1', '3')
    await userEvent.click(screen.getByRole('tab', { name: 'Товари' }))
    expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(3)
    await userEvent.click(screen.getByRole('tab', { name: 'Загальне' }))
    expect(screen.getByRole('spinbutton', { name: 'Кількість 1' })).toHaveValue(3)
  })
  it('uses mobile cards, a bottom filter sheet and a separate detail route', async () => {
    setViewport(390)
    setup()
    const list = await screen.findByRole('list', { name: 'Список закупівель' })
    expect(screen.queryByRole('table', { name: 'Закупівлі' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Вибрана закупівля' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Фільтри' }))
    expect(await screen.findByRole('dialog', { name: 'Фільтри' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Пошук' })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Показати результати' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Фільтри' })).not.toBeInTheDocument())
    await userEvent.click(within(list).getByRole('link', { name: 'Без номера' }))
    expect(await screen.findByRole('button', { name: 'Зберегти' })).toBeVisible()
    expect(screen.getByTestId('route')).toHaveTextContent('/purchases/' + draft.document.id)
    expect(screen.getByRole('list', { name: 'Рядки закупівлі' })).toBeVisible()
  })
})
