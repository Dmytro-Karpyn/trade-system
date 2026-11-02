import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AppProviders } from '../../app/AppProviders'
import { createAppServices } from '../../app/services'
import { json, testUser } from '../../test/fixtures'
import { setViewport } from '../../test/setup'
import { directoryTree, excludedCategories } from './contracts'

const id = (n: number) => `019a073c-9ee5-7000-8000-${String(n).padStart(12, '0')}`
const org = testUser.organizationId
const root = {
  id: id(10),
  organizationId: org,
  name: 'Посуд',
  parentId: null,
  description: 'Для дому',
  sortOrder: 0,
  active: true,
}
const child = { ...root, id: id(11), name: 'Чашки', parentId: root.id, sortOrder: 1 }
const unit = { id: id(12), organizationId: org, name: 'штука', active: true }
const price = { ...unit, id: id(13), name: 'Роздрібна', defaultMarkupPercent: 15, sortOrder: 0 }
const location = { ...unit, id: id(14), name: 'Основний склад' }
const expense = { ...root, id: id(15), name: 'Втрати' }
const reason = { ...unit, id: id(16), name: 'Пошкодження', expenseCategoryId: expense.id }
const role = {
  id: id(17),
  organizationId: org,
  name: 'Оператор',
  active: true,
  permissions: ['product.view'],
  userCount: 2,
}
const role2 = { ...role, id: id(18), name: 'Склад', permissions: ['stock.view'] }
const managedUser = {
  userId: id(19),
  username: 'worker',
  fullName: 'Оператор складу',
  organizationId: org,
  counterpartyId: null,
  counterpartyName: null,
  active: true,
  lastLoginAt: null,
  roleIds: [role.id, role2.id],
  roleNames: [role.name, role2.name],
}
const audit = {
  id: id(20),
  occurredAt: '2026-10-29T10:00:00Z',
  actorId: managedUser.userId,
  username: managedUser.username,
  eventType: 'UPDATE',
  entityType: 'Product',
  entityId: id(21),
  documentId: id(22),
  documentNumber: 'PI-00000001',
  documentType: 'PURCHASE_INVOICE',
  summary: 'Чашка синя',
}
const product = {
  id: id(21),
  organizationId: org,
  categoryId: child.id,
  unitId: unit.id,
  itemType: 'GOODS',
  code: 'CUP',
  barcode: null,
  name: 'Чашка синя',
  description: null,
  attributes: null,
  active: true,
}
const servicesToClean: ReturnType<typeof createAppServices>[] = []

function mount(
  path: string,
  options: {
    permissions?: string[]
    pending?: boolean
    empty?: boolean
    inactiveRole?: boolean
    status?: number
    lastAdmin?: boolean
  } = {},
) {
  const data: Record<string, Record<string, unknown>[]> = structuredClone({
    '/categories': options.empty ? [] : [root, child],
    '/units': [unit, { ...unit, id: id(30), name: 'Неактивна одиниця', active: false }],
    '/price-types': [price],
    '/storage-locations': [location],
    '/expense-categories': [expense],
    '/write-off-reasons': [reason],
    '/admin/users': [managedUser],
    '/admin/roles': [{ ...role, active: !options.inactiveRole }, role2],
    '/products': [product],
  })
  let release: (() => void) | undefined
  const gate = options.pending
    ? new Promise<void>((resolve) => {
        release = resolve
      })
    : Promise.resolve()
  const calls: { path: string; method: string; body: Record<string, unknown> }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const parsed = new URL(url, 'http://localhost')
      const path = parsed.pathname.replace(/^\/api/, '')
      const method = init?.method ?? 'GET'
      const body = init?.body ? JSON.parse(init.body as string) : {}
      calls.push({ path: path + parsed.search, method, body })
      if (options.lastAdmin && method !== 'GET' && (body.active === false || path.endsWith('/permissions') || path.endsWith('/deactivate'))) return json({ title: 'Conflict', detail: 'LAST_ADMIN_REQUIRED', code: 'LAST_ADMIN_REQUIRED' }, 409)
      if (path === '/auth/me')
        return json({
          ...testUser,
          permissions: options.permissions ?? [
            ...testUser.permissions,
            'audit.view',
            'product.create',
            'product.edit',
          ],
        })
      if (path === '/auth/csrf') return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
      if (path === '/admin/permissions')
        return json(
          [
            'product.view',
            'product.create',
            'sale.post',
            'sale.change_price',
            'stock.view',
            'cost.view',
            'users.manage',
          ].map((code, i) => ({ id: id(50 + i), code, description: code })),
        )
      if (path === '/admin/users/lookups')
        return json({
          roles: [role, role2],
          counterparties: [{ id: id(60), name: 'Працівник', isEmployee: true }],
        })
      if (path === '/admin/audit/lookups')
        return json({
          users: [{ id: managedUser.userId, name: managedUser.username }],
          eventTypes: ['UPDATE'],
          entityTypes: ['Product'],
          documents: [{ id: audit.documentId, name: audit.documentNumber }],
        })
      if (path === '/admin/audit') return json({ items: [audit], total: 1 })
      if (path === '/admin/audit/' + audit.id)
        return json({ entry: audit, oldValues: { Name: 'Чашка' }, newValues: { Name: audit.summary } })
      if (path === '/currencies')
        return json([
          {
            id: '00000000-0000-0000-0000-000000000980',
            code: 'UAH',
            name: 'Українська гривня',
            active: null,
            isBase: true,
          },
        ])
      if (data[path]) {
        if (method === 'GET') {
          await gate
          return json(data[path], options.status ?? 200)
        }
        const created = {
          ...(data[path][0] ?? root),
          ...body,
          id: id(70),
          ...(path === '/admin/users' ? { userId: id(70), roleNames: [role.name], lastLoginAt: null } : {}),
        }
        data[path].push(created)
        return json(created, 201)
      }
      const base = Object.keys(data).find((key) => path.startsWith(key + '/'))
      if (base) {
        const row = data[base].find(
          (x) => (x.userId ?? x.id) === path.split('/')[3] || (x.userId ?? x.id) === path.split('/').at(-1),
        )
        if (!row) return json({}, 404)
        if (method !== 'GET') {
          if (path.endsWith('/permissions')) row.permissions = body.permissionCodes
          else if (/\/(deactivate|activate)$/.test(path)) row.active = path.endsWith('/activate')
          else Object.assign(row, body)
        }
        return json(row)
      }
      return json({}, 404)
    }),
  )
  const services = createAppServices('/api')
  servicesToClean.push(services)
  services.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  render(
    <AppProviders services={services}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AppProviders>,
  )
  return { calls, services, release: () => release?.() }
}
async function choose(label: string | RegExp, option: string) {
  await userEvent.click(screen.getByRole('combobox', { name: label }))
  await userEvent.click(await screen.findByRole('option', { name: option }))
}
beforeEach(() => setViewport())
afterEach(() => {
  servicesToClean.splice(0).forEach((s) => s.queryClient.clear())
  vi.unstubAllGlobals()
})

describe('directory management', () => {
  it('links all seven directories including the existing expense tree', async () => {
    mount('/directories')
    expect(await screen.findByRole('heading', { name: 'Категорії товарів' })).toBeVisible()
    expect(screen.getAllByRole('link', { name: 'Відкрити' })).toHaveLength(7)
    expect(screen.getByRole('heading', { name: 'Категорії витрат' })).toBeVisible()
  })
  it('renders the category tree and nested category paths', async () => {
    mount('/directories/categories')
    const table = await screen.findByRole('table', { name: 'Категорії товарів' })
    expect(within(table).getByText(child.name)).toHaveAttribute('title', 'Посуд / Чашки')
  })
  it('creates a nested category with its existing parent and edits its name', async () => {
    const { calls } = mount('/directories/categories')
    await userEvent.click(await screen.findByRole('button', { name: 'Додати підкатегорію Посуд' }))
    expect(screen.getByRole('combobox', { name: 'Батьківська категорія' })).toHaveTextContent(root.name)
    await userEvent.type(screen.getByRole('textbox', { name: 'Назва' }), 'Тарілки')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(calls.find((c) => c.method === 'POST' && c.path === '/categories')?.body).toMatchObject({
      name: 'Тарілки',
      parentId: root.id,
    })
    await userEvent.click(screen.getByRole('button', { name: 'Редагувати Чашки' }))
    await userEvent.clear(screen.getByRole('textbox', { name: 'Назва' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Назва' }), 'Горнятка')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'PUT' && c.body.name === 'Горнятка')).toBe(true),
    )
  })
  it('excludes self and descendants from parent choices', async () => {
    mount('/directories/categories')
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати Посуд' }))
    await userEvent.click(screen.getByRole('combobox', { name: 'Батьківська категорія' }))
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(screen.getByRole('option')).toHaveTextContent('Без батьківської категорії')
  })
  it('keeps category paths and descendants beyond visual indentation limits', () => {
    const nodes = Array.from({ length: 80 }, (_, i) => ({
      ...root,
      id: id(i + 100),
      name: `N${i}`,
      parentId: i ? id(i + 99) : null,
    }))
    expect(directoryTree(nodes).at(-1)?.depth).toBe(79)
    expect(excludedCategories(nodes, nodes[0].id).size).toBe(80)
  })
  it('shows directory loading and then data', async () => {
    const { release } = mount('/directories/categories', { pending: true })
    expect(await screen.findByRole('status', { name: 'Завантаження довідника' })).toBeVisible()
    await act(async () => release())
    expect(await screen.findByRole('button', { name: 'Редагувати Посуд' })).toBeVisible()
  })
  it('shows an empty directory without fake rows', async () => {
    mount('/directories/categories', { empty: true })
    expect(await screen.findByRole('heading', { name: 'Записів ще немає' })).toBeVisible()
  })
  it('supports retry on lookup errors', async () => {
    mount('/directories/units', { status: 503 })
    expect(await screen.findByRole('button', { name: 'Спробувати ще раз' })).toBeVisible()
  })
  it.each([
    ['units', 'Одиниці виміру', unit.name],
    ['storage-locations', 'Місця зберігання', location.name],
    ['write-off-reasons', 'Причини списання', reason.name],
  ])('loads and edits %s without a delete action', async (kind, title, name) => {
    const { calls } = mount('/directories/' + kind)
    expect(await screen.findByRole('table', { name: title })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Редагувати ' + name }))
    await userEvent.click(screen.getByRole('switch', { name: 'Активний запис' }))
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.body.active === false)).toBe(true))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)
    if (kind === 'write-off-reasons')
      expect(calls.find((c) => c.method === 'PUT')?.body.expenseCategoryId).toBe(expense.id)
  })
  it('filters inactive directory items', async () => {
    mount('/directories/units')
    await screen.findByRole('table')
    await choose('Статус', 'Неактивні')
    expect(screen.getByText('Неактивна одиниця')).toBeVisible()
    expect(screen.queryByText(unit.name)).not.toBeInTheDocument()
  })
  it('edits markup through PriceType only and explains its effect', async () => {
    const { calls } = mount('/directories/price-types')
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати Роздрібна' }))
    const input = screen.getByRole('spinbutton', { name: 'Націнка за замовчуванням, %' })
    await userEvent.clear(input)
    await userEvent.type(input, '35')
    expect(
      screen.getByText('Націнка використовується для початкової рекомендованої ціни нових партій.'),
    ).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти' }))
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'PUT' && c.body.defaultMarkupPercent === 35)).toBe(true),
    )
    expect(calls.some((c) => c.path.includes('/batches/'))).toBe(false)
  })
  it('renders UAH as a read-only base currency', async () => {
    mount('/directories/currencies')
    expect(await screen.findByText('Українська гривня')).toBeVisible()
    expect(screen.getByText('Базова')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Додати' })).not.toBeInTheDocument()
  })
  it('shows actual category paths in Product detail and selects the category in edit', async () => {
    mount('/products/' + product.id)
    expect(await screen.findByText('Посуд / Чашки')).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Редагувати' }))
    expect(screen.getByRole('combobox', { name: 'Категорія' })).toHaveTextContent('Посуд / Чашки')
  })
  it('renders nested cards on mobile without the desktop table', async () => {
    setViewport(390)
    mount('/directories/categories')
    expect(await screen.findByRole('list', { name: 'Категорії товарів' })).toBeVisible()
    expect(screen.getByText('Посуд / Чашки')).toBeVisible()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})

describe('administration', () => {
  it('lists users with multiple named roles and no delete action', async () => {
    const { calls } = mount('/admin/users')
    const table = await screen.findByRole('table', { name: 'Користувачі' })
    expect(within(table).getByText(role.name)).toBeVisible()
    expect(within(table).getByText(role2.name)).toBeVisible()
    expect(within(table).getByText('Ще не входив')).toBeVisible()
    expect(screen.queryByRole('button', { name: /Видалити/ })).not.toBeInTheDocument()
    expect(calls.every((c) => c.method !== 'DELETE')).toBe(true)
  })
  it('validates a new user and password confirmation', async () => {
    const { calls } = mount('/admin/users/new')
    await userEvent.click(await screen.findByRole('button', { name: 'Зберегти користувача' }))
    expect(await screen.findByText('Введіть логін.')).toBeVisible()
    expect(screen.getByText(/Щонайменше 12/)).toBeVisible()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })
  it('creates a user with selected roles and a nullable employee link', async () => {
    const { calls } = mount('/admin/users/new')
    await userEvent.type(await screen.findByRole('textbox', { name: 'Логін' }), 'newworker')
    await userEvent.type(
      screen.getByLabelText('Пароль', { exact: false, selector: 'input[name="password"]' }),
      'Test-only.Password2026!',
    )
    await userEvent.type(
      screen.getByLabelText('Підтвердження пароля', { exact: false }),
      'Test-only.Password2026!',
    )
    await userEvent.click(await screen.findByRole('checkbox', { name: role.name }))
    await choose('Контрагент / працівник', 'Працівник (працівник)')
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти користувача' }))
    await waitFor(() =>
      expect(calls.some((c) => c.method === 'POST' && c.path === '/admin/users')).toBe(true),
    )
    expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      username: 'newworker',
      roleIds: [role.id],
      counterpartyId: id(60),
      password: 'Test-only.Password2026!',
    })
    expect(calls.find((c) => c.method === 'POST')?.body).not.toHaveProperty('organizationId')
  })
  it('keeps an existing password absent and organization read-only', async () => {
    mount('/admin/users/' + managedUser.userId)
    expect(await screen.findByRole('textbox', { name: 'Організація' })).toHaveAttribute('readonly')
    expect(screen.queryByLabelText('Пароль')).not.toBeInTheDocument()
    expect(await screen.findByRole('checkbox', { name: role.name })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: role2.name })).toBeChecked()
  })
  it('confirms deactivation and supports reactivation', async () => {
    const { calls } = mount('/admin/users')
    await userEvent.click(await screen.findByRole('button', { name: 'Деактивувати worker' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Деактивувати' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Активувати worker' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Активувати' }))
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/activate'))).toBe(true))
    expect(calls.some((c) => c.path.endsWith('/deactivate'))).toBe(true)
  })
  it('lists roles with user and permission counts', async () => {
    mount('/admin/roles')
    const table = await screen.findByRole('table', { name: 'Ролі' })
    expect(within(table).getByText(role.name)).toBeVisible()
    expect(within(table).getAllByText('2')).toHaveLength(2)
  })
  it('groups existing permission codes and saves selected permissions', async () => {
    const { calls } = mount('/admin/roles/' + role.id)
    const permission = await screen.findByRole('checkbox', { name: /Створення product.create/ })
    expect(screen.getByRole('heading', { name: 'Продажі' })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Продажі' }))
    expect(await screen.findByText('Зміна ціни нижче рекомендованої')).toBeVisible()
    await userEvent.click(permission)
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти роль' }))
    await waitFor(() =>
      expect(calls.some((c) => c.path.endsWith('/permissions') && c.method === 'PUT')).toBe(true),
    )
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({
      permissionCodes: ['product.view', 'product.create'],
    })
  })
  it('does not allow permission edits on an inactive role', async () => {
    const { calls } = mount('/admin/roles/' + role.id, { inactiveRole: true })
    expect(await screen.findByRole('checkbox', { name: /product.view/ })).toBeDisabled()
    expect(screen.getByText(/Щоб змінити її права/)).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Зберегти роль' }))
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
  })
  it.each(['/admin/users', '/admin/roles', '/admin/audit'])(
    'denies %s without the matching permission',
    async (path) => {
      const { calls } = mount(path, { permissions: ['product.view'] })
      expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
      expect(screen.queryByRole('link', { name: 'Адміністрування' })).not.toBeInTheDocument()
      expect(calls.some((c) => c.path.startsWith('/admin'))).toBe(false)
    },
  )
  it('opens audit details with formatted old/new values and source links', async () => {
    mount('/admin/audit')
    await userEvent.click(await screen.findByRole('button', { name: 'Деталі' }))
    const diff = await screen.findByRole('table', { name: 'Зміни полів' })
    expect(within(diff).getByText('Назва')).toBeVisible()
    expect(within(diff).getByText('Чашка')).toBeVisible()
    expect(within(diff).getByText('Чашка синя')).toBeVisible()
    expect(screen.getByRole('link', { name: 'PI-00000001' })).toHaveAttribute(
      'href',
      '/purchases/' + audit.documentId,
    )
    expect(screen.queryByRole('button', { name: /Видалити|Редагувати/ })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Закрити деталі' }))
    await waitFor(() => expect(screen.queryByText('Попередні значення')).not.toBeInTheDocument())
  })
  it('sends audit event, actor, entity, document and debounced search filters', async () => {
    const { calls } = mount('/admin/audit')
    await screen.findByRole('table')
    await choose('Подія', 'Зміна')
    await choose('Користувач', 'worker')
    await choose('Тип сутності', 'Номенклатура')
    await choose('Документ', audit.documentNumber)
    await userEvent.type(screen.getByRole('textbox', { name: 'Пошук / ID сутності' }), 'Чашка')
    await waitFor(() =>
      expect(
        calls.some((c) => new URL(c.path, 'http://localhost').searchParams.get('search') === 'Чашка'),
      ).toBe(true),
    )
    const params = new URL(
      calls.filter((c) => c.path.startsWith('/admin/audit?')).at(-1)!.path,
      'http://localhost',
    ).searchParams
    expect(params.get('eventType')).toBe('UPDATE')
    expect(params.get('userId')).toBe(managedUser.userId)
    expect(params.get('entityType')).toBe('Product')
    expect(params.get('documentId')).toBe(audit.documentId)
  })
  it.each([
    ['/admin/users', 'Користувачі'],
    ['/admin/roles', 'Ролі'],
    ['/admin/audit', 'Події аудиту'],
  ])('renders %s as mobile cards', async (path, label) => {
    setViewport(390)
    mount(path)
    expect(await screen.findByRole('list', { name: label })).toBeVisible()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
  it('uses grouped mobile permission accordions', async () => {
    setViewport(390)
    mount('/admin/roles/' + role.id)
    expect(await screen.findByRole('button', { name: 'Товари' })).toBeVisible()
    expect(screen.getByRole('checkbox', { name: /product.view/ })).toBeChecked()
  })
})

describe('Final management redesign', () => {
  it('selects a category in a shared tree/editor pane and keeps all existing fields', async () => {
    mount('/directories/categories')
    const table = await screen.findByRole('table', { name: 'Категорії товарів' })
    await userEvent.click(within(table).getByText(child.name))
    const panel = screen.getByRole('region', { name: 'Редактор довідника' })
    expect(within(panel).getByRole('textbox', { name: 'Назва' })).toHaveValue(child.name)
    expect(within(panel).getByRole('textbox', { name: 'Опис' })).toHaveValue(child.description)
    expect(within(panel).getByRole('spinbutton', { name: 'Порядок сортування' })).toHaveValue(1)
    expect(within(panel).getByRole('combobox', { name: 'Батьківська категорія' })).toHaveTextContent(root.name)
    expect(within(table).getByRole('row', { name: /Чашки/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
  it('guards dirty directory selection instead of discarding an edited name', async () => {
    mount('/directories/categories')
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати Чашки' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Назва' }), ' змінено')
    await userEvent.click(screen.getByRole('button', { name: 'Редагувати Посуд' }))
    const dialog = await screen.findByRole('dialog', { name: 'Незбережені зміни' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Скасувати' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('textbox', { name: 'Назва' })).toHaveValue('Чашки змінено')
  })
  it('opens the same directory editor as a controlled dialog on mobile', async () => {
    setViewport(390); mount('/directories/price-types')
    await userEvent.click(await screen.findByRole('button', { name: 'Редагувати Роздрібна' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('spinbutton', { name: 'Націнка за замовчуванням, %' })).toHaveValue(15)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Скасувати' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
  it('searches directory names locally without creating a new API contract', async () => {
    const app = mount('/directories/units')
    await screen.findByRole('table', { name: 'Одиниці виміру' })
    await userEvent.type(screen.getByRole('textbox', { name: 'Пошук' }), 'Неактивна')
    expect(screen.queryByText(unit.name)).not.toBeInTheDocument()
    expect(screen.getByText('Неактивна одиниця')).toBeVisible()
    expect(app.calls.filter(call => call.path === '/units' && call.method === 'GET')).toHaveLength(1)
  })
  it('uses the same tree and editor pattern for expense categories', async () => {
    mount('/expense-categories')
    const table = await screen.findByRole('table', { name: 'Дерево категорій витрат' })
    await userEvent.click(within(table).getByText(expense.name))
    const panel = screen.getByRole('region', { name: 'Редактор категорії витрат' })
    expect(within(panel).getByRole('textbox', { name: 'Назва категорії' })).toHaveValue(expense.name)
    expect(within(panel).getByRole('button', { name: 'Зберегти категорію' })).toBeVisible()
  })
  it('selects a user in place and retains a direct detail link', async () => {
    mount('/admin/users')
    const table = await screen.findByRole('table', { name: 'Користувачі' })
    await userEvent.click(within(table).getByRole('link', { name: managedUser.username }))
    const panel = screen.getByRole('region', { name: 'Вибраний користувач' })
    expect(await within(panel).findByRole('textbox', { name: 'Логін' })).toHaveValue(managedUser.username)
    expect(within(panel).getByRole('link', { name: 'Відкрити окремо' })).toHaveAttribute('href', '/admin/users/' + managedUser.userId)
    expect(within(panel).queryByLabelText('Пароль')).not.toBeInTheDocument()
  })
  it('selects a role and edits grouped permissions in the right pane', async () => {
    mount('/admin/roles')
    const table = await screen.findByRole('table', { name: 'Ролі' })
    await userEvent.click(within(table).getByRole('link', { name: role.name }))
    const panel = screen.getByRole('region', { name: 'Вибрана роль' })
    expect(await within(panel).findByRole('textbox', { name: 'Назва ролі' })).toHaveValue(role.name)
    expect(await within(panel).findByRole('checkbox', { name: /product.view/ })).toBeChecked()
    for (const group of ['Товари', 'Закупівлі', 'Продажі', 'Склад', 'Гроші', 'Витрати', 'Взаєморозрахунки', 'Звіти', 'Адміністрування']) expect(within(panel).getByRole('button', { name: group })).toBeVisible()
  })
  it('guards a dirty role permission selection', async () => {
    mount('/admin/roles')
    const table = await screen.findByRole('table', { name: 'Ролі' })
    await userEvent.click(within(table).getByRole('link', { name: role.name }))
    await userEvent.click(await screen.findByRole('checkbox', { name: /product.create/ }))
    await userEvent.click(within(table).getByRole('link', { name: role2.name }))
    expect(await screen.findByRole('dialog', { name: 'Незбережені зміни' })).toBeVisible()
  })
  it('shows the backend last-admin protection message in the existing activation flow', async () => {
    mount('/admin/users', { lastAdmin: true })
    await userEvent.click(await screen.findByRole('button', { name: 'Деактивувати worker' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Деактивувати' }))
    expect(await within(dialog).findByText('Останнього активного адміністратора не можна деактивувати або позбавити адміністративних прав.')).toBeVisible()
  })
  it('opens read-only audit field changes on mobile and closes its detail drawer', async () => {
    setViewport(390); mount('/admin/audit')
    await userEvent.click(await screen.findByRole('button', { name: 'Деталі' }))
    const drawer = screen.getByRole('dialog', { name: 'Деталі події' })
    const changes = await within(drawer).findByRole('list', { name: 'Зміни полів' })
    expect(within(changes).getByText('Чашка')).toBeVisible()
    expect(within(changes).getByText('Чашка синя')).toBeVisible()
    expect(within(drawer).queryByRole('button', { name: /Зберегти|Видалити/ })).not.toBeInTheDocument()
    await userEvent.click(within(drawer).getByRole('button', { name: 'Закрити деталі' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
  it('opens audit filters in the shared mobile sheet', async () => {
    setViewport(390); mount('/admin/audit')
    await screen.findByRole('list', { name: 'Події аудиту' })
    expect(screen.queryByRole('combobox', { name: 'Подія' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Фільтри' }))
    const filters = screen.getByRole('dialog', { name: 'Фільтри' })
    expect(within(filters).getByRole('combobox', { name: 'Подія' })).toBeVisible()
  })
})
