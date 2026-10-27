import { afterEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { ApiClient, ApiError } from './client'
import { json } from '../test/fixtures'

afterEach(() => vi.unstubAllGlobals())

describe('central API client', () => {
  it('uses the configured base URL and includes HttpOnly cookies', async () => {
    const fetch = vi.fn().mockResolvedValue(json({ value: 42 })); vi.stubGlobal('fetch', fetch)
    const api = new ApiClient('https://trade.example/api/')
    expect(await api.request('/value', { schema: z.object({ value: z.number() }) })).toEqual({ value: 42 })
    expect(fetch).toHaveBeenCalledWith('https://trade.example/api/value', expect.objectContaining({ credentials: 'include', cache: 'no-store' }))
  })

  it('loads a CSRF token before writes and refreshes it after an identity change', async () => {
    let token = 1
    const fetch = vi.fn(async (url: string) => url.endsWith('/auth/csrf') ? json({ requestToken: `csrf-${token++}`, headerName: 'X-CSRF-TOKEN' }) : new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    const api = new ApiClient('/api')
    await api.request('/auth/login', { method: 'POST', body: { username: 'test', password: 'password' } })
    api.resetCsrf()
    await api.request('/auth/logout', { method: 'POST' })
    expect(fetch.mock.calls.map(([url]) => url)).toEqual(['/api/auth/csrf', '/api/auth/login', '/api/auth/csrf', '/api/auth/logout'])
    const calls = vi.mocked(globalThis.fetch).mock.calls
    expect(new Headers(calls[1][1]?.headers).get('X-CSRF-TOKEN')).toBe('csrf-1')
    expect(new Headers(calls[3][1]?.headers).get('X-CSRF-TOKEN')).toBe('csrf-2')
    expect(calls.every(([, options]) => options?.credentials === 'include')).toBe(true)
  })

  it('shares an in-flight CSRF request between concurrent writes', async () => {
    const fetch = vi.fn(async (url: string) => url.endsWith('/auth/csrf') ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' }) : new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetch)
    const api = new ApiClient()
    await Promise.all([api.request('/one', { method: 'POST' }), api.request('/two', { method: 'POST' })])
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/auth/csrf'))).toHaveLength(1)
  })

  it('does not send a write if obtaining its CSRF token fails', async () => {
    const fetch = vi.fn().mockResolvedValue(json({}, 503)); vi.stubGlobal('fetch', fetch)
    await expect(new ApiClient().request('/auth/login', { method: 'POST' })).rejects.toMatchObject({ status: 503 })
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('handles 401 centrally while allowing an invalid login to remain a form error', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/auth/csrf') ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' }) : json({}, 401)))
    const api = new ApiClient(); api.onUnauthorized = vi.fn()
    await expect(api.request('/products')).rejects.toMatchObject({ status: 401 })
    expect(api.onUnauthorized).toHaveBeenCalledTimes(1)
    await expect(api.request('/auth/login', { method: 'POST', handleUnauthorized: false })).rejects.toMatchObject({ status: 401 })
    expect(api.onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('handles forbidden responses and conflict codes centrally without retrying writes', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({}, 403)).mockResolvedValueOnce(json({ code: 'INSUFFICIENT_STOCK' }, 409))
    vi.stubGlobal('fetch', fetch)
    const api = new ApiClient(); api.onForbidden = vi.fn()
    await expect(api.request('/costs')).rejects.toMatchObject({ status: 403, message: 'Недостатньо прав для цієї дії.' })
    expect(api.onForbidden).toHaveBeenCalledOnce()
    await expect(api.request('/stock')).rejects.toMatchObject({ status: 409, code: 'INSUFFICIENT_STOCK', message: 'Недостатньо доступного товару на складі.' })
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('preserves field validation errors from Problem Details', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ errors: { Username: ['Логін обов’язковий.'] } }, 400)))
    await expect(new ApiClient().request('/validation')).rejects.toMatchObject({ status: 400, fieldErrors: { Username: ['Логін обов’язковий.'] } })
  })

  it('reports network and malformed API responses as safe errors', async () => {
    const fetch = vi.fn().mockRejectedValueOnce(new TypeError('fetch failed')).mockResolvedValueOnce(json({ value: 'wrong' }))
    vi.stubGlobal('fetch', fetch)
    await expect(new ApiClient().request('/offline')).rejects.toBeInstanceOf(ApiError)
    await expect(new ApiClient().request('/invalid', { schema: z.object({ value: z.number() }) })).rejects.toMatchObject({ status: 0 })
  })
})
