import { QueryClient } from '@tanstack/react-query'
import { ApiClient, ApiError } from '../api/client'
import { currentUserKey } from '../auth/contracts'

export function createAppServices(baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 1,
      },
      mutations: { retry: false },
    },
  })
  const api = new ApiClient(baseUrl)
  function clearSession() {
    api.resetCsrf()
    void queryClient.cancelQueries()
    queryClient.removeQueries({ predicate: query => query.queryKey[0] !== currentUserKey[0] })
    queryClient.getMutationCache().clear()
    queryClient.setQueryData(currentUserKey, null)
  }
  api.onUnauthorized = clearSession
  return { api, queryClient, clearSession }
}
export type AppServices = ReturnType<typeof createAppServices>
