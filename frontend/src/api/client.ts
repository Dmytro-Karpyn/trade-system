import { z } from 'zod'

const csrfSchema = z.object({ requestToken: z.string().min(1), headerName: z.string().min(1) })
const problemSchema = z.object({
  detail: z.string().optional(),
  code: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
})

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly detail?: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, code?: string, fieldErrors: Record<string, string[]> = {}, detail?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.detail = detail
    this.fieldErrors = fieldErrors
  }
}

interface RequestOptions<T> {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  body?: unknown
  signal?: AbortSignal
  schema?: z.ZodType<T>
  handleUnauthorized?: boolean
}

function errorMessage(status: number, code?: string) {
  if (status === 401) return 'Сеанс завершився. Увійдіть знову.'
  if (status === 403) return 'Недостатньо прав для цієї дії.'
  if (code === 'INSUFFICIENT_STOCK') return 'Недостатньо доступного товару на складі.'
  if (status === 409) return 'Дані змінилися або операція суперечить поточному стану. Оновіть дані та спробуйте ще раз.'
  if (status === 400 || status === 422) return 'Перевірте правильність введених даних.'
  if (status >= 500) return 'Система тимчасово недоступна. Спробуйте пізніше.'
  return 'Не вдалося виконати запит. Спробуйте ще раз.'
}

export class ApiClient {
  private readonly baseUrl: string
  private csrf?: z.infer<typeof csrfSchema>
  private csrfRequest?: Promise<z.infer<typeof csrfSchema>>
  private csrfGeneration = 0
  onUnauthorized?: () => void
  onForbidden?: () => void

  constructor(baseUrl = '') {
    this.baseUrl = baseUrl.replace(/\/+$/, '')
  }

  resetCsrf() {
    this.csrfGeneration += 1
    this.csrf = undefined
    this.csrfRequest = undefined
  }

  setForbiddenHandler(handler?: () => void) {
    this.onForbidden = handler
  }

  async refreshCsrf() {
    if (this.csrf) return this.csrf
    if (!this.csrfRequest) {
      const generation = this.csrfGeneration
      this.csrfRequest = this.request('/auth/csrf', { schema: csrfSchema }).then(token => {
        if (generation === this.csrfGeneration) this.csrf = token
        return token
      }).finally(() => {
        if (generation === this.csrfGeneration) this.csrfRequest = undefined
      })
    }
    return this.csrfRequest
  }

  async request<T = void>(path: string, options: RequestOptions<T> = {}): Promise<T> {
    if (!path.startsWith('/') || path.startsWith('//')) throw new Error('API paths must be relative to the configured API base URL.')
    const method = options.method ?? 'GET'
    const headers = new Headers({ Accept: 'application/json' })
    if (options.body !== undefined) headers.set('Content-Type', 'application/json')
    if (method !== 'GET') {
      const csrf = await this.refreshCsrf()
      headers.set(csrf.headerName, csrf.requestToken)
    }
    let response: Response
    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        method, headers, credentials: 'include', cache: 'no-store',
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: options.signal
          ? AbortSignal.any([options.signal, AbortSignal.timeout(15_000)])
          : AbortSignal.timeout(15_000),
      })
    } catch (error) {
      if (options.signal?.aborted) throw error
      throw new ApiError(0, 'Не вдалося з’єднатися із системою. Перевірте підключення та спробуйте ще раз.')
    }
    if (!response.ok) {
      const parsed = problemSchema.safeParse(await response.json().catch(() => undefined))
      const problem = parsed.success ? parsed.data : undefined
      if (response.status === 401 && options.handleUnauthorized !== false) this.onUnauthorized?.()
      if (response.status === 403) this.onForbidden?.()
      if (response.status === 400) this.resetCsrf() // Next explicit attempt gets a fresh token; never replay writes automatically.
      throw new ApiError(response.status, errorMessage(response.status, problem?.code), problem?.code, problem?.errors, response.status < 500 ? problem?.detail : undefined)
    }
    if (response.status === 204) return undefined as T
    const body: unknown = await response.json().catch(() => undefined)
    if (options.schema) {
      const parsed = options.schema.safeParse(body)
      if (!parsed.success) throw new ApiError(0, 'Система повернула неочікувану відповідь. Спробуйте оновити сторінку.')
      return parsed.data
    }
    return body as T
  }
}
