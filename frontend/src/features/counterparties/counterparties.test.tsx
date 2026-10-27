import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import { partyKeys } from './queries'
import { uahId } from './contracts'
import type { Counterparty, PartySettlement, PriceType } from './contracts'

const guid = (value: number) => `019a073c-9ee5-7000-8000-${String(value).padStart(12, '0')}`
const retail: PriceType = { id: guid(10), organizationId: testUser.organizationId, name: 'Роздрібна', defaultMarkupPercent: 10, active: true, sortOrder: 0 }
const buyer: Counterparty = { id: guid(11), organizationId: testUser.organizationId, name: 'ТОВ Обрій', legalType: 'COMPANY', taxId: '1234567890', registrationCode: 'REG-001', phone: '+380501234567', email: 'office@obrii.test', address: 'Київ', defaultPriceTypeId: retail.id, isCustomer: true, isSupplier: false, isEmployee: false, isSystem: false, active: true, note: 'Постійний покупець' }
const supplier: Counterparty = { ...buyer, id: guid(12), name: 'Постачсервіс', isCustomer: false, isSupplier: true, email: 'supply@example.test' }
const employee: Counterparty = { ...buyer, id: guid(13), name: 'Працівник Олена', legalType: 'PERSON', isCustomer: false, isEmployee: true }
const system: Counterparty = { ...buyer, id: guid(14), name: 'Роздрібний покупець', isSystem: true }
const inactive: Counterparty = { ...supplier, id: guid(15), name: 'Архівний контрагент', active: false }
const otherCurrency = guid(840)
const history: PartySettlement = {
  counterpartyId: buyer.id,
  balances: [{ counterpartyId: buyer.id, currencyId: uahId, balance: 1250 }, { counterpartyId: buyer.id, currencyId: otherCurrency, balance: -300 }],
  movements: [
    { id: guid(20), documentId: guid(30), documentNumber: 'PI-00000001', documentType: 'PURCHASE_INVOICE', documentDate: '2026-10-20', counterpartyId: buyer.id, currencyId: otherCurrency, amount: -300, baseAmountUah: -12000, balanceAfterMovement: -300 },
    { id: guid(21), documentId: guid(31), documentNumber: 'SI-00000002', documentType: 'SALE_INVOICE', documentDate: '2026-10-21', counterpartyId: buyer.id, currencyId: uahId, amount: 1250, baseAmountUah: 1250, balanceAfterMovement: 1250 },
  ],
}
const servicesToClean: ReturnType<typeof createAppServices>[] = []
function Location() { return <span data-testid="route">{useLocation().pathname}</span> }
interface SetupOptions { path?: string; permissions?: string[]; parties?: Counterparty[]; prices?: PriceType[]; settlements?: PartySettlement; pending?: boolean }
function setup({ path = '/counterparties', permissions = [...testUser.permissions], parties = [buyer, supplier, employee, system, inactive], prices = [retail], settlements = history, pending = false }: SetupOptions = {}) {
  let items = [...parties]
  let listStatus = 200
  let saveError: { status: number; body: unknown } | undefined
  let release: (() => void) | undefined
  const gate = pending ? new Promise<void>(resolve => { release = resolve }) : Promise.resolve()
  const calls: { path: string; options?: RequestInit }[] = []
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    const parsed = new URL(url, 'http://localhost'); const path = parsed.pathname.replace(/^\/api/, '')
    calls.push({ path: parsed.pathname + parsed.search, options })
    if (path === '/auth/me') return json({ ...testUser, permissions })
    if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/price-types') return json(prices)
    if (path === '/health') return json({ status: 'Healthy' })
    if (path.startsWith('/settlements/counterparties/')) return json(settlements)
    if (path === '/counterparties' && options?.method === 'POST') {
      if (saveError) return json(saveError.body, saveError.status)
      const item: Counterparty = { ...buyer, ...JSON.parse(options.body as string), id: guid(16) }; items.push(item); return json(item, 201)
    }
    if (path === '/counterparties') {
      await gate
      if (listStatus !== 200) return json({}, listStatus)
      const params = parsed.searchParams
      const search = params.get('search')?.toLowerCase()
      return json(items.filter(item => (!search || [item.name, item.phone, item.email, item.taxId, item.registrationCode].some(value => value?.toLowerCase().includes(search))) &&
        (['isCustomer', 'isSupplier', 'isEmployee', 'isSystem', 'active'] as const).every(key => !params.has(key) || String(item[key]) === params.get(key))))
    }
    if (path.startsWith('/counterparties/')) {
      const item = items.find(item => item.id === path.split('/').at(-1))
      if (!item) return json({}, 404)
      if (options?.method === 'PUT') {
        if (saveError) return json(saveError.body, saveError.status)
        const updated = { ...item, ...JSON.parse(options.body as string) }; items = items.map(candidate => candidate.id === item.id ? updated : candidate); return json(updated)
      }
      return json(item)
    }
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch)
  const services = createAppServices('/api'); servicesToClean.push(services)
  services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><Location /></MemoryRouter></AppProviders>)
  return { services, calls, release: () => release?.(), setListStatus: (status: number) => { listStatus = status }, setSaveError: (status: number, body: unknown) => { saveError = { status, body } } }
}
async function choose(label: string, option: string) { await userEvent.click(screen.getByRole('combobox', { name: label })); await userEvent.click(await screen.findByRole('option', { name: option })) }
async function openSettlements(options: SetupOptions = {}) { const result = setup({ path: `/counterparties/${buyer.id}`, ...options }); await userEvent.click(await screen.findByRole('tab', { name: 'Взаєморозрахунки' })); return result }
beforeEach(() => setViewport())
afterEach(() => { servicesToClean.splice(0).forEach(item => item.queryClient.clear()); vi.unstubAllGlobals() })

describe('Counterparty UI', () => {
  it('loads the list with lookup names and without internal IDs', async () => {
    setup()
    const table = await screen.findByRole('table', { name: 'Контрагенти' })
    expect(await within(table).findByRole('link', { name: buyer.name })).toBeVisible()
    expect(within(table).getAllByText(retail.name).length).toBeGreaterThan(0)
    expect(screen.queryByText(buyer.id)).not.toBeInTheDocument(); expect(screen.queryByText(buyer.organizationId)).not.toBeInTheDocument()
  })
  it('shows a loading skeleton and replaces it with the list', async () => {
    const { release } = setup({ pending: true })
    expect(await screen.findByRole('status', { name: 'Завантаження контрагентів' })).toBeVisible()
    await act(async () => release())
    expect(await screen.findByRole('link', { name: buyer.name })).toBeVisible()
  })
  it('shows the empty state', async () => { setup({ parties: [] }); expect(await screen.findByRole('heading', { name: 'Контрагентів ще немає' })).toBeVisible() })
  it('debounces search and sends the text to the backend', async () => {
    const { calls } = setup(); await screen.findByRole('link', { name: buyer.name })
    await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'Постачсервіс')
    await waitFor(() => expect(screen.queryByRole('link', { name: buyer.name })).not.toBeInTheDocument())
    expect(await screen.findByRole('link', { name: supplier.name })).toBeVisible()
    const searched = calls.filter(call => new URL(call.path, 'http://localhost').searchParams.has('search'))
    expect(searched).toHaveLength(1)
    expect(new URL(searched[0].path, 'http://localhost').searchParams.get('search')).toBe('Постачсервіс')
  })
  it.each([['Покупці', 'isCustomer', buyer.name, supplier.name], ['Постачальники', 'isSupplier', supplier.name, buyer.name], ['Працівники', 'isEmployee', employee.name, buyer.name], ['Системні', 'isSystem', system.name, buyer.name]])('filters %s using the existing role flags', async (label, parameter, shown, hidden) => {
    const { calls } = setup(); await screen.findByRole('link', { name: buyer.name }); await choose('Ролі', label)
    expect(await screen.findByRole('link', { name: shown })).toBeVisible()
    await waitFor(() => expect(screen.queryByRole('link', { name: hidden })).not.toBeInTheDocument())
    expect(calls.some(call => call.path.includes(`${parameter}=true`))).toBe(true)
  })
  it('filters inactive parties and includes them in All', async () => {
    setup(); await screen.findByRole('link', { name: buyer.name }); await choose('Статус', 'Неактивні')
    expect(await screen.findByRole('link', { name: inactive.name })).toBeVisible()
    expect(screen.getByText('Неактивний')).toBeVisible()
    await choose('Статус', 'Усі'); expect(await screen.findByRole('link', { name: buyer.name })).toBeVisible(); expect(screen.getByRole('link', { name: inactive.name })).toBeVisible()
  })
  it('validates required name and an email only when filled', async () => {
    const { calls } = setup({ path: '/counterparties/new' })
    await userEvent.click(await screen.findByRole('button', { name: 'Створити контрагента' }))
    expect(await screen.findByText('Введіть назву контрагента.')).toBeVisible()
    await userEvent.type(screen.getByRole('textbox', { name: 'Назва' }), 'Новий покупець')
    await userEvent.type(screen.getByRole('textbox', { name: 'Email' }), 'not-email')
    await userEvent.click(screen.getByRole('button', { name: 'Створити контрагента' }))
    expect(await screen.findByText('Введіть коректний email.')).toBeVisible()
    expect(calls.some(call => call.options?.method === 'POST')).toBe(false)
  })
  it('creates a party with optional empty fields, multiple roles and a selected PriceType', async () => {
    const { calls, services } = setup({ path: '/counterparties/new' })
    services.queryClient.setQueryData(partyKeys.list({ search: '', role: '', active: 'true' }), [buyer])
    await userEvent.type(await screen.findByRole('textbox', { name: 'Назва' }), 'Новий покупець')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Покупець' })); await userEvent.click(screen.getByRole('checkbox', { name: 'Постачальник' }))
    await choose('Тип ціни за замовчуванням', retail.name)
    await userEvent.click(screen.getByRole('button', { name: 'Створити контрагента' }))
    expect(await screen.findByRole('heading', { name: 'Новий покупець' })).toBeVisible()
    expect(await screen.findByRole('alert')).toHaveTextContent('Контрагента створено')
    const input = JSON.parse(calls.find(call => call.options?.method === 'POST' && call.path === '/api/counterparties')!.options?.body as string)
    expect(input).toMatchObject({ legalType: null, email: null, phone: null, isCustomer: true, isSupplier: true, isEmployee: false, isSystem: false, defaultPriceTypeId: retail.id })
    expect(services.queryClient.getQueryState(partyKeys.list({ search: '', role: '', active: 'true' }))?.isInvalidated).toBe(true)
  })
  it('edits a party and deactivates it without a DELETE', async () => {
    const { calls } = setup({ path: `/counterparties/${buyer.id}` })
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати' }))
    const name = screen.getByRole('textbox', { name: 'Назва' }); await userEvent.clear(name); await userEvent.type(name, 'Оновлений контрагент')
    await userEvent.click(screen.getByRole('checkbox', { name: 'Працівник' })); await userEvent.click(screen.getByRole('checkbox', { name: 'Активний' }))
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти зміни' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Зберегти зміни' })).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: 'Оновлений контрагент' })).toBeVisible(); expect(screen.getByText('Неактивний')).toBeVisible()
    const input = JSON.parse(calls.find(call => call.options?.method === 'PUT')!.options?.body as string)
    expect(input).toMatchObject({ isEmployee: true, active: false, defaultPriceTypeId: retail.id })
    expect(calls.some(call => call.options?.method === 'DELETE')).toBe(false)
  })
  it('displays multiple business roles and the system badge', async () => {
    setup({ parties: [{ ...buyer, isSupplier: true, isEmployee: true, isSystem: true }] })
    const table = await screen.findByRole('table', { name: 'Контрагенти' })
    for (const label of ['Покупець', 'Постачальник', 'Працівник', 'Системний']) expect(within(table).getByText(label)).toBeVisible()
  })
  it('preserves IsSystem when editing and never offers a system checkbox', async () => {
    const { calls } = setup({ path: `/counterparties/${system.id}` })
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати' }))
    expect(screen.queryByRole('checkbox', { name: /Систем/ })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти зміни' }))
    await waitFor(() => expect(calls.some(call => call.options?.method === 'PUT')).toBe(true))
    expect(JSON.parse(calls.find(call => call.options?.method === 'PUT')!.options?.body as string).isSystem).toBe(true)
  })
  it('loads settlements only when the tab is opened and displays actual document numbers', async () => {
    const { calls } = setup({ path: `/counterparties/${buyer.id}` }); await screen.findByRole('heading', { name: buyer.name })
    expect(calls.some(call => call.path.startsWith('/api/settlements/'))).toBe(false)
    await userEvent.click(screen.getByRole('tab', { name: 'Взаєморозрахунки' }))
    const table = await screen.findByRole('table', { name: 'Рухи взаєморозрахунків' })
    expect(within(table).getByText('PI-00000001')).toBeVisible(); expect(within(table).getByText('SI-00000002')).toBeVisible()
    expect(within(table).getByText('20.10.2026')).toBeVisible()
    expect(screen.queryByText(history.movements[0].documentId)).not.toBeInTheDocument()
  })
  it('labels a positive balance as Нам винні', async () => { await openSettlements(); expect(await screen.findByText('Нам винні')).toBeVisible() })
  it('labels a negative balance as Ми винні', async () => { await openSettlements(); expect(await screen.findByText('Ми винні')).toBeVisible() })
  it('keeps currencies and running balances separate without a combined total or invented ISO code', async () => {
    await openSettlements()
    const uah = await screen.findByRole('region', { name: 'Баланс UAH' }); const other = screen.getByRole('region', { name: 'Баланс Валюта 1' })
    expect(within(uah).getByText('Нам винні')).toBeVisible(); expect(within(other).getByText('Ми винні')).toBeVisible()
    expect(within(uah).getByText(/1\s250,00/)).toBeVisible(); expect(within(other).getByText('-300,00')).toBeVisible()
    expect(screen.queryByText('950,00')).not.toBeInTheDocument(); expect(screen.queryByText('USD')).not.toBeInTheDocument(); expect(screen.queryByText(otherCurrency)).not.toBeInTheDocument()
  })
  it('shows zero status only for a zero balance actually returned by the API', async () => {
    await openSettlements({ settlements: { counterpartyId: buyer.id, balances: [{ counterpartyId: buyer.id, currencyId: uahId, balance: 0 }], movements: [] } })
    expect(await screen.findByText('Нульовий баланс')).toBeVisible()
  })
  it('shows empty settlements without inventing a zero UAH balance', async () => {
    await openSettlements({ settlements: { counterpartyId: buyer.id, balances: [], movements: [] } })
    expect(await screen.findByRole('heading', { name: 'Рухів взаєморозрахунків ще немає' })).toBeVisible()
    expect(screen.queryByRole('region', { name: 'Баланс UAH' })).not.toBeInTheDocument()
  })
  it('renders mobile cards instead of the desktop table', async () => {
    setViewport(390); setup()
    const list = await screen.findByRole('list', { name: 'Список контрагентів' })
    expect(await within(list).findByRole('link', { name: buyer.name })).toBeVisible()
    expect(within(list).getAllByText(buyer.phone!).length).toBeGreaterThan(0); expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
  it('keeps settlements disabled and editing hidden for a sales-only viewer', async () => {
    const { calls } = setup({ path: `/counterparties/${buyer.id}`, permissions: ['sale.view'] })
    await screen.findByRole('heading', { name: buyer.name }); expect(screen.getByRole('tab', { name: 'Взаєморозрахунки' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Редагувати' })).not.toBeInTheDocument()
    expect(calls.some(call => call.path.startsWith('/api/settlements/'))).toBe(false)
  })
  it('settlement-only viewers are not redirected by an unauthorized PriceType request', async () => {
    const { calls } = setup({ permissions: ['settlement.view'] }); await screen.findByRole('link', { name: buyer.name })
    expect(calls.some(call => call.path === '/api/price-types')).toBe(false)
    expect(screen.queryByRole('link', { name: 'Додати контрагента' })).not.toBeInTheDocument()
  })
  it('denies direct creation without settings.manage', async () => {
    setup({ path: '/counterparties/new', permissions: ['sale.view'] })
    expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
  })
  it('leaves the remaining tabs as placeholders without business requests', async () => {
    const { calls } = setup({ path: `/counterparties/${buyer.id}` }); await screen.findByRole('heading', { name: buyer.name })
    await userEvent.click(screen.getByRole('tab', { name: 'Продажі' }))
    expect(await screen.findByRole('heading', { name: 'Розділ буде реалізовано наступним етапом' })).toBeVisible()
    expect(calls.some(call => /^\/api\/(sales|purchases|expenses|money)/.test(call.path))).toBe(false)
  })
  it('shows a retryable list error and recovers', async () => {
    const { services, setListStatus } = setup(); await screen.findByRole('link', { name: buyer.name }); setListStatus(503)
    await act(async () => { await services.queryClient.refetchQueries({ queryKey: partyKeys.all }) })
    const retry = await screen.findByRole('button', { name: 'Спробувати ще раз' }); setListStatus(200); await userEvent.click(retry)
    expect(await screen.findByRole('link', { name: buyer.name })).toBeVisible()
  })
  it('maps backend validation errors to fields without clearing input', async () => {
    const { setSaveError } = setup({ path: '/counterparties/new' }); setSaveError(400, { errors: { Name: ['Перевірте назву.'] } })
    await userEvent.type(await screen.findByRole('textbox', { name: 'Назва' }), 'Моя назва'); await userEvent.click(screen.getByRole('button', { name: 'Створити контрагента' }))
    expect(await screen.findByText('Перевірте назву.')).toBeVisible(); expect(screen.getByRole('textbox', { name: 'Назва' })).toHaveValue('Моя назва')
  })
  it('settings-only creators can save without fetching forbidden lookups or detail', async () => {
    const { calls } = setup({ path: '/counterparties/new', permissions: ['settings.manage'] })
    await userEvent.type(await screen.findByRole('textbox', { name: 'Назва' }), 'Новий контрагент без перегляду')
    await userEvent.click(screen.getByRole('button', { name: 'Створити контрагента' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Контрагента створено')
    expect(screen.getByTestId('route')).toHaveTextContent('/counterparties/new')
    expect(calls.some(call => call.path === '/api/price-types')).toBe(false)
    expect(calls.some(call => call.path.startsWith('/api/counterparties/') && call.options?.method === 'GET')).toBe(false)
  })
  it('editing without PriceType read access preserves the existing selection', async () => {
    const { calls } = setup({ path: `/counterparties/${buyer.id}`, permissions: ['settlement.view', 'settings.manage'] })
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати' }))
    expect(screen.getByRole('combobox', { name: 'Тип ціни за замовчуванням' })).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти зміни' }))
    await waitFor(() => expect(calls.some(call => call.options?.method === 'PUT')).toBe(true))
    expect(JSON.parse(calls.find(call => call.options?.method === 'PUT')!.options?.body as string).defaultPriceTypeId).toBe(retail.id)
    expect(calls.some(call => call.path === '/api/price-types')).toBe(false)
  })

})
