import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { currentUserKey } from '../auth/contracts'
import type { CurrentUser } from '../auth/contracts'
import { ApiError } from '../api/client'
import { json, testUser } from '../test/fixtures'
import { setViewport } from '../test/setup'
import { AppProviders } from './AppProviders'
import { createAppServices } from './services'

function Location() { return <span data-testid="location">{useLocation().pathname}</span> }
const servicesToClean: ReturnType<typeof createAppServices>[] = []
function mount(path = '/', currentUser: CurrentUser = testUser, initiallyAuthenticated = true, initialMeStatus = 200) {
  let authenticated = initiallyAuthenticated
  let meStatus = initialMeStatus
  const calls: { path: string; options?: RequestInit }[] = []
  const fetch = vi.fn(async (url: string, options?: RequestInit) => {
    calls.push({ path: url, options })
    const path = url.replace(/^\/api/, '')
    if (path === '/auth/csrf') return json({ requestToken: authenticated ? 'signed-in-token' : 'anonymous-token', headerName: 'X-CSRF-TOKEN' })
    if (path === '/auth/me') return authenticated ? json(currentUser, meStatus) : json({}, 401)
    if (path === '/auth/login') { authenticated = true; return new Response(null, { status: 204 }) }
    if (path === '/auth/logout') { authenticated = false; return new Response(null, { status: 204 }) }
    if (path === '/health') return json({ status: 'Healthy' })
    return json({}, 404)
  })
  vi.stubGlobal('fetch', fetch)
  const services = createAppServices('/api'); servicesToClean.push(services)
  render(<AppProviders services={services}><MemoryRouter initialEntries={[path]}><App /><Location /></MemoryRouter></AppProviders>)
  return { services, fetch, calls, setMeStatus: (value: number) => { meStatus = value }, expire: () => { authenticated = false } }
}

beforeEach(() => setViewport())
afterEach(() => { servicesToClean.splice(0).forEach(x => x.queryClient.clear()); vi.unstubAllGlobals() })

describe('authenticated application', () => {
  it('redirects an unauthenticated user from a protected route to /login', async () => {
    mount('/products', testUser, false)
    expect(await screen.findByRole('heading', { name: 'З поверненням' })).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent('/login')
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('valid login opens the app, refreshes CSRF and loads /auth/me into Query cache', async () => {
    const { services, calls } = mount('/login', testUser, false)
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Логін'), '  okoval  ')
    await user.type(screen.getByLabelText('Пароль', { exact: true }), 'test-password')
    await user.click(screen.getByRole('button', { name: 'Увійти' }))
    expect(await screen.findByRole('heading', { name: 'Огляд діяльності' })).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/)
    expect(services.queryClient.getQueryData(currentUserKey)).toEqual(testUser)
    const login = calls.find(call => call.path === '/api/auth/login')!
    expect(JSON.parse(login.options?.body as string)).toEqual({ username: 'okoval', password: 'test-password' })
    expect(new Headers(login.options?.headers).get('X-CSRF-TOKEN')).toBe('anonymous-token')
    const afterLogin = calls.slice(calls.indexOf(login) + 1)
    expect(afterLogin[0].path).toBe('/api/auth/csrf')
    expect(afterLogin[1].path).toBe('/api/auth/me')
    expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0)
  })

  it('loads the current user, shows their name and redirects authenticated /login to home', async () => {
    const { services, calls } = mount('/login')
    expect(await screen.findByRole('heading', { name: 'Огляд діяльності' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Меню користувача Олена Коваль' })).toBeVisible()
    expect(services.queryClient.getQueryData(currentUserKey)).toEqual(testUser)
    expect(calls.filter(call => call.path === '/api/auth/me')).toHaveLength(1)
    expect(screen.queryByLabelText('Пароль')).not.toBeInTheDocument()
  })

  it('hides unavailable menu items and shows allowed items', async () => {
    mount('/', { ...testUser, permissions: ['product.view', 'stock.transfer', 'profit.view'] })
    const menu = await screen.findByRole('navigation', { name: 'Основна навігація' })
    expect(within(menu).getByRole('link', { name: 'Товари' })).toBeVisible()
    expect(within(menu).getByRole('link', { name: 'Склад' })).toBeVisible()
    expect(within(menu).getByRole('link', { name: 'Звіти' })).toBeVisible()
    expect(within(menu).queryByRole('link', { name: 'Закупівлі' })).not.toBeInTheDocument()
    expect(within(menu).queryByRole('link', { name: 'Продажі' })).not.toBeInTheDocument()
    expect(within(menu).queryByRole('link', { name: 'Адміністрування' })).not.toBeInTheDocument()
  })

  it('denies a direct URL without its permission and lets the user navigate home', async () => {
    mount('/products', { ...testUser, permissions: [] })
    expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
    await userEvent.click(screen.getByRole('link', { name: 'На головну' }))
    expect(await screen.findByRole('heading', { name: 'Огляд діяльності' })).toBeVisible()
  })

  it('displays forbidden state when an API returns 403', async () => {
    const { services, fetch } = mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    fetch.mockResolvedValueOnce(json({}, 403))
    await act(async () => { await expect(services.api.request('/restricted')).rejects.toBeInstanceOf(ApiError) })
    expect(await screen.findByRole('heading', { name: 'Недостатньо прав' })).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent('/forbidden')
  })

  it('clears current-user and other cached data on a late 401 and redirects to login', async () => {
    const { services, expire } = mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    services.queryClient.setQueryData(['private-documents'], [{ id: 1 }]); expire()
    await act(async () => { await services.queryClient.refetchQueries({ queryKey: currentUserKey }) })
    expect(await screen.findByRole('heading', { name: 'З поверненням' })).toBeVisible()
    expect(services.queryClient.getQueryData(currentUserKey)).toBeNull()
    expect(services.queryClient.getQueryData(['private-documents'])).toBeUndefined()
  })

  it('logout posts a current-user CSRF token, clears cache and returns to login', async () => {
    const { services, calls } = mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Меню користувача Олена Коваль' }))
    await user.click(screen.getByRole('menuitem', { name: 'Вийти' }))
    expect(await screen.findByRole('heading', { name: 'З поверненням' })).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent('/login')
    expect(services.queryClient.getQueryData(currentUserKey)).toBeNull()
    const logout = calls.find(call => call.path === '/api/auth/logout')!
    expect(logout.options?.method).toBe('POST')
    expect(new Headers(logout.options?.headers).get('X-CSRF-TOKEN')).toBe('signed-in-token')
  })

  it('mobile sidebar opens, closes and closes after navigation', async () => {
    setViewport(390); mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    const user = userEvent.setup()
    expect(screen.queryByRole('navigation', { name: 'Основна навігація' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Відкрити меню' }))
    expect(await screen.findByRole('navigation', { name: 'Основна навігація' })).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Закрити меню' }))
    await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Основна навігація' })).not.toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: 'Відкрити меню' }))
    await user.click(await screen.findByRole('link', { name: 'Товари' }))
    expect(await screen.findByRole('heading', { name: 'Товари' })).toBeVisible()
    await waitFor(() => expect(screen.queryByRole('navigation', { name: 'Основна навігація' })).not.toBeInTheDocument())
  })

  it('validates required login fields without submitting to backend', async () => {
    const { calls } = mount('/login', testUser, false)
    await userEvent.click(await screen.findByRole('button', { name: 'Увійти' }))
    expect(await screen.findByText('Введіть логін.')).toBeVisible()
    expect(screen.getByText('Введіть пароль.')).toBeVisible()
    expect(calls.some(call => call.path === '/api/auth/login')).toBe(false)
  })

  it('shows a safe invalid-login message and remains on /login', async () => {
    const { fetch } = mount('/login', testUser, false)
    await screen.findByRole('heading', { name: 'З поверненням' })
    fetch.mockImplementation(async (url: string) => url.endsWith('/auth/csrf') ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' }) : json({}, 401))
    await userEvent.type(screen.getByLabelText('Логін'), 'okoval')
    await userEvent.type(screen.getByLabelText('Пароль', { exact: true }), 'invalid')
    await userEvent.click(screen.getByRole('button', { name: 'Увійти' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Перевірте логін і пароль')
    expect(screen.getByTestId('location')).toHaveTextContent('/login')
  })

  it('dashboard has honest empty metrics and makes no business-report requests', async () => {
    const { calls } = mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    expect(screen.getAllByLabelText(/показник недоступний/)).toHaveLength(7)
    expect(screen.getByRole('heading', { name: 'Операції поки не відображаються' })).toBeVisible()
    expect(calls.every(call => ['/api/auth/me', '/api/health'].includes(call.path))).toBe(true)
  })

  it('keeps the session when logout fails and shows a retryable error', async () => {
    const { services, fetch } = mount('/')
    await screen.findByRole('heading', { name: 'Огляд діяльності' })
    fetch.mockImplementation(async (url: string) => {
      if (url.endsWith('/auth/logout')) return json({}, 503)
      if (url.endsWith('/auth/csrf')) return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
      if (url.endsWith('/auth/me')) return json(testUser)
      return json({ status: 'Healthy' })
    })
    await userEvent.click(screen.getByRole('button', { name: 'Меню користувача Олена Коваль' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Вийти' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Система тимчасово недоступна')
    expect(services.queryClient.getQueryData(currentUserKey)).toEqual(testUser)
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/)
  })

  it('shows current-user loading errors and can retry without assuming authentication', async () => {
    const { setMeStatus } = mount('/', testUser, true, 503)
    expect(await screen.findByRole('alert')).toHaveTextContent('Система тимчасово недоступна')
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    setMeStatus(200)
    await userEvent.click(screen.getByRole('button', { name: 'Спробувати ще раз' }))
    expect(await screen.findByRole('heading', { name: 'Огляд діяльності' })).toBeVisible()
  })

  it('shows server field validation errors next to the login field', async () => {
    const { fetch } = mount('/login', testUser, false)
    await screen.findByRole('heading', { name: 'З поверненням' })
    fetch.mockImplementation(async (url: string) => url.endsWith('/auth/csrf')
      ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
      : json({ errors: { Username: ['Перевірте формат логіна.'] } }, 400))
    await userEvent.type(screen.getByLabelText('Логін'), 'bad-login')
    await userEvent.type(screen.getByLabelText('Пароль', { exact: true }), 'test-password')
    await userEvent.click(screen.getByRole('button', { name: 'Увійти' }))
    expect(await screen.findByText('Перевірте формат логіна.')).toBeVisible()
    expect(screen.getByTestId('location')).toHaveTextContent('/login')
  })

})
