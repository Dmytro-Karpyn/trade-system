import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import { productKeys, unitsKey } from './queries'
import type { Product, Unit } from './contracts'

const unit: Unit = { id: '019a073c-9ee5-7000-8000-000000000002', organizationId: testUser.organizationId, name: 'шт.' }
const goods: Product = { id: '019a073c-9ee5-7000-8000-000000000003', organizationId: testUser.organizationId, categoryId: null, unitId: unit.id, itemType: 'GOODS', code: 'G-001', name: 'Дриль акумуляторний', barcode: '4820001234567', description: 'Компактний інструмент', attributes: { color: 'blue' }, active: true }
const service: Product = { ...goods, id: '019a073c-9ee5-7000-8000-000000000004', code: 'S-001', name: 'Монтаж обладнання', itemType: 'SERVICE', barcode: null, attributes: null }
const inactive: Product = { ...goods, id: '019a073c-9ee5-7000-8000-000000000005', code: 'G-002', name: 'Архівна позиція', active: false }
const defaultPermissions = ['product.view', 'product.create', 'product.edit']
const servicesToClean: ReturnType<typeof createAppServices>[] = []
function Location() { return <span data-testid="route">{useLocation().pathname}</span> }
interface SetupOptions { path?: string; permissions?: string[]; products?: Product[]; units?: Unit[]; pending?: boolean }
function setup({ path = '/products', permissions = defaultPermissions, products = [goods, service, inactive], units = [unit], pending = false }: SetupOptions = {}) {
  let items = [...products]
  let unitItems = [...units]
  let listStatus = 200
  let saveError: { status: number; body: unknown } | undefined
  let pendingResolver: (() => void) | undefined
  const gate = pending ? new Promise<void>(resolve => { pendingResolver = resolve }) : Promise.resolve()
  const calls: { path: string; options?: RequestInit }[] = []
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const path = parsed.pathname.replace(/^\/api/, '')
    calls.push({ path: parsed.pathname + parsed.search, options })
    if (path === '/auth/me') return json({ ...testUser, permissions })
    if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/health') return json({ status: 'Healthy' })
    if (path === '/units') {
      if (options?.method === 'POST') {
        const created = { ...unit, id: '019a073c-9ee5-7000-8000-000000000006', name: JSON.parse(options.body as string).name }
        unitItems = [...unitItems, created]; return json(created, 201)
      }
      return json(unitItems)
    }
    if (path === '/products' && options?.method === 'POST') {
      if (saveError) return json(saveError.body, saveError.status)
      const created: Product = { ...goods, ...JSON.parse(options.body as string), id: '019a073c-9ee5-7000-8000-000000000007' }
      items.push(created); return json(created, 201)
    }
    if (path === '/products') {
      await gate
      if (listStatus !== 200) return json({}, listStatus)
      const search = parsed.searchParams.get('search')?.toLowerCase()
      const type = parsed.searchParams.get('itemType'); const active = parsed.searchParams.get('active')
      return json(items.filter(item => (!search || [item.name, item.code, item.barcode ?? ''].some(value => value.toLowerCase().includes(search))) && (!type || item.itemType === type) && (active === null || String(item.active) === active)))
    }
    if (path.startsWith('/products/')) {
      const item = items.find(item => item.id === path.split('/').at(-1))
      if (!item) return json({}, 404)
      if (options?.method === 'PUT') {
        if (saveError) return json(saveError.body, saveError.status)
        const updated = { ...item, ...JSON.parse(options.body as string) }
        items = items.map(candidate => candidate.id === item.id ? updated : candidate); return json(updated)
      }
      return json(item)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch)
  const services = createAppServices('/api'); servicesToClean.push(services)
  services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><Location /></MemoryRouter></AppProviders>)
  return { calls, services, release: () => pendingResolver?.(), setListStatus: (status: number) => { listStatus = status }, setSaveError: (status: number, body: unknown) => { saveError = { status, body } } }
}
async function choose(label: string, option: string) {
  await userEvent.click(screen.getByRole('combobox', { name: label }))
  await userEvent.click(await screen.findByRole('option', { name: option }))
}
async function fillNew() {
  await userEvent.type(await screen.findByRole('textbox', { name: 'Код' }), 'NEW-001')
  await userEvent.type(screen.getByRole('textbox', { name: 'Назва' }), 'Новий інструмент')
  await choose('Одиниця виміру', 'шт.')
}
beforeEach(() => setViewport())
afterEach(() => { servicesToClean.splice(0).forEach(service => service.queryClient.clear()); vi.unstubAllGlobals() })

describe('Product UI', () => {
  it('loads the products table with real lookup names and no internal IDs or raw attributes', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Товари' })
    expect(await within(table).findByRole('link', { name: goods.name })).toBeVisible()
    expect(within(table).getByText(goods.code)).toBeVisible()
    expect(within(table).getAllByText(unit.name)).toHaveLength(2)
    expect(within(table).getByText('Товар')).toBeVisible(); expect(within(table).getByText('Послуга')).toBeVisible()
    expect(screen.queryByText(goods.id)).not.toBeInTheDocument(); expect(screen.queryByText(goods.organizationId)).not.toBeInTheDocument()
    expect(screen.queryByText('blue')).not.toBeInTheDocument()
  })

  it('shows loading skeletons until the product query completes', async () => {
    const { release } = setup({ pending: true })
    expect(await screen.findByRole('status', { name: 'Завантаження товарів' })).toBeVisible()
    await act(async () => release())
    expect(await screen.findByRole('link', { name: goods.name })).toBeVisible()
  })

  it('shows the empty state without inventing rows', async () => {
    setup({ products: [] })
    expect(await screen.findByRole('heading', { name: 'Товарів ще немає' })).toBeVisible()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('debounces search and sends it to the backend instead of filtering only cached rows', async () => {
    const { calls } = setup()
    await screen.findByRole('link', { name: goods.name })
    await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'монтаж')
    await waitFor(() => expect(screen.queryByRole('link', { name: goods.name })).not.toBeInTheDocument())
    expect(await screen.findByRole('link', { name: service.name })).toBeVisible()
    const searched = calls.filter(call => new URL(call.path, 'http://localhost').searchParams.has('search'))
    expect(searched).toHaveLength(1)
    expect(new URL(searched[0].path, 'http://localhost').searchParams.get('search')).toBe('монтаж')
  })

  it('filters GOODS and SERVICE through API query parameters', async () => {
    const { calls } = setup()
    await screen.findByRole('link', { name: goods.name })
    await choose('Тип', 'Послуги')
    expect(await screen.findByRole('link', { name: service.name })).toBeVisible()
    await waitFor(() => expect(screen.queryByRole('link', { name: goods.name })).not.toBeInTheDocument())
    expect(calls.some(call => call.path.includes('itemType=SERVICE'))).toBe(true)
    await choose('Тип', 'Товари')
    expect(await screen.findByRole('link', { name: goods.name })).toBeVisible()
    expect(screen.queryByRole('link', { name: service.name })).not.toBeInTheDocument()
  })

  it('shows inactive items with the inactive filter and preserves them with All', async () => {
    setup(); await screen.findByRole('link', { name: goods.name })
    await choose('Статус', 'Неактивні')
    expect(await screen.findByRole('link', { name: inactive.name })).toBeVisible()
    expect(screen.getByText('Неактивний')).toBeVisible()
    await choose('Статус', 'Усі')
    expect(await screen.findByRole('link', { name: goods.name })).toBeVisible()
    expect(screen.getByRole('link', { name: inactive.name })).toBeVisible()
  })

  it('shows Add only with product.create', async () => {
    setup(); expect(await screen.findByRole('link', { name: 'Додати' })).toBeVisible()
  })
  it('hides Add without product.create and denies the direct new URL', async () => {
    setup({ permissions: ['product.view'] })
    await screen.findByRole('link', { name: goods.name })
    expect(screen.queryByRole('link', { name: 'Додати' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: `Редагувати ${goods.name}` })).not.toBeInTheDocument()
  })
  it('denies direct creation without product.create', async () => {
    const { calls } = setup({ path: '/products/new', permissions: ['product.view'] })
    expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
    expect(calls.some(call => call.path.startsWith('/api/products'))).toBe(false)
  })

  it('shows Edit with product.edit on the detail page', async () => {
    setup({ path: `/products/${goods.id}` })
    expect(await screen.findByRole('button', { name: 'Редагувати' })).toBeVisible()
    expect(screen.getByRole('heading', { name: goods.name })).toBeVisible()
  })
  it('keeps the detail read-only without product.edit even with forged edit navigation state', async () => {
    setup({ path: `/products/${goods.id}`, permissions: ['product.view'] })
    await screen.findByRole('heading', { name: goods.name })
    expect(screen.queryByRole('button', { name: 'Редагувати' })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Назва' })).not.toBeInTheDocument()
  })

  it('validates code, name and unit before creating a product', async () => {
    const { calls } = setup({ path: '/products/new' })
    await userEvent.click(await screen.findByRole('button', { name: 'Створити' }))
    expect(await screen.findByText('Введіть код.')).toBeVisible(); expect(screen.getByText('Введіть назву.')).toBeVisible(); expect(screen.getByText('Виберіть одиницю виміру.')).toBeVisible()
    expect(calls.some(call => call.options?.method === 'POST' && call.path === '/api/products')).toBe(false)
  })

  it('creates a product, invalidates list data, opens its detail and shows success', async () => {
    const { calls, services } = setup({ path: '/products/new' })
    services.queryClient.setQueryData(productKeys.list({ search: '', itemType: '', active: 'true' }), [goods])
    await fillNew(); await userEvent.click(screen.getByRole('button', { name: 'Створити' }))
    expect(await screen.findByRole('heading', { name: 'Новий інструмент' })).toBeVisible()
    expect(screen.getByTestId('route')).toHaveTextContent('/products/019a073c-9ee5-7000-8000-000000000007')
    expect(await screen.findByRole('alert')).toHaveTextContent('створено')
    const request = calls.find(call => call.path === '/api/products' && call.options?.method === 'POST')!
    expect(JSON.parse(request.options?.body as string)).toMatchObject({ name: 'Новий інструмент', code: 'NEW-001', itemType: 'GOODS', unitId: unit.id, categoryId: null, attributes: null, active: true })
    expect(services.queryClient.getQueryState(productKeys.list({ search: '', itemType: '', active: 'true' }))?.isInvalidated).toBe(true)
  })

  it('edits and deactivates a product while preserving hidden category and attributes', async () => {
    const categoryId = '019a073c-9ee5-7000-8000-000000000008'
    const { calls } = setup({ path: `/products/${goods.id}`, products: [{ ...goods, categoryId }] })
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати' }))
    const name = screen.getByRole('textbox', { name: 'Назва' })
    await userEvent.clear(name); await userEvent.type(name, 'Оновлений інструмент')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Активний' }))
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти зміни' }))
    expect(await screen.findByRole('heading', { name: 'Оновлений інструмент' })).toBeVisible()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Зберегти зміни' })).not.toBeInTheDocument())
    expect(screen.getByText('Неактивний')).toBeVisible()
    const input = JSON.parse(calls.find(call => call.options?.method === 'PUT')!.options?.body as string)
    expect(input).toMatchObject({ active: false, categoryId, attributes: goods.attributes })
    expect(calls.some(call => call.options?.method === 'DELETE')).toBe(false)
  })

  it('shows SERVICE helper text in the shared form', async () => {
    setup({ path: '/products/new' })
    await screen.findByRole('heading', { name: 'Нова номенклатура' })
    await choose('Тип *', 'Послуга')
    expect(screen.getByText('Послуга не має складських залишків і партій')).toBeVisible()
  })

  it('renders compact cards on a narrow screen instead of the desktop table', async () => {
    setViewport(390); setup()
    const list = await screen.findByRole('list', { name: 'Список товарів' })
    expect(await within(list).findByRole('link', { name: goods.name })).toBeVisible()
    expect(within(list).getByText(`Код: ${goods.code}`)).toBeVisible()
    expect(within(list).getAllByText(unit.name)).toHaveLength(2)
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('can create a real unit inline when the lookup is empty and selects it', async () => {
    const { services, calls } = setup({ path: '/products/new', units: [] })
    await userEvent.click(await screen.findByRole('button', { name: 'Додати одиницю' }))
    const dialog = screen.getByRole('dialog', { name: 'Нова одиниця виміру' })
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Назва одиниці виміру' }), 'комплект')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Додати одиницю' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('combobox', { name: 'Одиниця виміру' })).toHaveTextContent('комплект')
    expect(services.queryClient.getQueryData(unitsKey)).toEqual([expect.objectContaining({ name: 'комплект' })])
    expect(calls.some(call => call.path === '/api/units' && call.options?.method === 'POST')).toBe(true)
  })

  it('shows server validation errors at matching fields and preserves user input on failure', async () => {
    const { setSaveError } = setup({ path: '/products/new' })
    setSaveError(400, { errors: { Code: ['Перевірте код товару.'] } })
    await fillNew(); await userEvent.click(screen.getByRole('button', { name: 'Створити' }))
    expect(await screen.findByText('Перевірте код товару.')).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Назва' })).toHaveValue('Новий інструмент')
  })

  it('shows API error with retry and can load the list after recovery', async () => {
    const { setListStatus, services } = setup()
    await screen.findByRole('link', { name: goods.name })
    setListStatus(503)
    await act(async () => { await services.queryClient.refetchQueries({ queryKey: productKeys.all }) })
    expect(await screen.findByRole('button', { name: 'Спробувати ще раз' })).toBeVisible()
    setListStatus(200); await userEvent.click(screen.getByRole('button', { name: 'Спробувати ще раз' }))
    expect(await screen.findByRole('link', { name: goods.name })).toBeVisible()
  })
})
